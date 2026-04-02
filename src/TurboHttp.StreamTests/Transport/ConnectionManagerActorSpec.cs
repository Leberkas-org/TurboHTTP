using System.Net;
using System.Net.Sockets;
using Akka.Actor;
using TurboHttp.Internal;
using TurboHttp.Transport.Connection;

namespace TurboHttp.StreamTests.Transport;

/// <summary>
/// Tests <see cref="ConnectionManagerActor"/> directly: version-aware acquire/release,
/// idle reuse, MRU selection, per-host limits, idle eviction, and actor disposal.
/// </summary>
public sealed class ConnectionManagerActorSpec : IAsyncLifetime
{
    private ActorSystem? _system;
    private TcpListener? _listener;
    private int _port;

    public ValueTask InitializeAsync()
    {
        _system = ActorSystem.Create("connection-manager-tests");
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _listener?.Stop();
        if (_system is not null)
        {
            await _system.Terminate();
        }
    }

    private TcpOptions CreateOptions() => new()
    {
        Host = "127.0.0.1",
        Port = _port,
        MaxFrameSize = 16384
    };

    private RequestEndpoint CreateEndpoint(Version? version = null) => new()
    {
        Host = "127.0.0.1",
        Port = (ushort)_port,
        Scheme = "http",
        Version = version ?? HttpVersion.Version11
    };

    private IActorRef CreateActor(TimeSpan? idleTimeout = null)
        => _system!.ActorOf(Props.Create(() => new ConnectionManagerActor(idleTimeout ?? TimeSpan.FromSeconds(5))));

    [Fact(Timeout = 5000)]
    public async Task ConnectionManagerActor_should_always_create_new_connection_for_http10()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version10);

        var lease1 =
            await ConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);
        actor.Tell(new ConnectionManagerActor.Release(lease1, CanReuse: true));

        var lease2 =
            await ConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);

        // HTTP/1.0 never reuses — must be different leases
        Assert.NotSame(lease1, lease2);

        lease2.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task ConnectionManagerActor_should_reuse_idle_connection_for_http11()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease1 =
            await ConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);
        actor.Tell(new ConnectionManagerActor.Release(lease1, CanReuse: true));

        // Small delay for actor mailbox to process Release before Acquire
        await Task.Delay(50, TestContext.Current.CancellationToken);

        var lease2 =
            await ConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);

        // HTTP/1.1 should reuse the idle connection
        Assert.Same(lease1, lease2);

        lease2.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task Release_should_return_to_idle_when_can_reuse()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease = await ConnectionManagerActor.AcquireAsync(actor, options, endpoint,
            TestContext.Current.CancellationToken);
        actor.Tell(new ConnectionManagerActor.Release(lease, CanReuse: true));

        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Connection is still alive (returned to idle, not disposed)
        Assert.True(lease.IsAlive);
    }

    [Fact(Timeout = 5000)]
    public async Task Release_should_dispose_connection_when_cannot_reuse()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease = await ConnectionManagerActor.AcquireAsync(actor, options, endpoint,
            TestContext.Current.CancellationToken);
        actor.Tell(new ConnectionManagerActor.Release(lease, CanReuse: false));

        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.False(lease.IsAlive);
    }

    [Fact(Timeout = 5000)]
    public async Task EvictIdle_should_remove_expired_connections()
    {
        var actor = CreateActor(TimeSpan.FromMilliseconds(50));
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease1 =
            await ConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);
        var lease2 =
            await ConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);
        Assert.NotSame(lease1, lease2);

        actor.Tell(new ConnectionManagerActor.Release(lease1, CanReuse: true));
        actor.Tell(new ConnectionManagerActor.Release(lease2, CanReuse: true));

        // Wait for idle timeout + eviction timer to fire
        await Task.Delay(300, TestContext.Current.CancellationToken);

        var evictedCount = (!lease1.IsAlive ? 1 : 0) + (!lease2.IsAlive ? 1 : 0);
        Assert.True(evictedCount >= 1, "At least one idle connection should have been evicted");
    }

    [Fact(Timeout = 5000)]
    public async Task EvictIdle_should_keep_at_least_one_per_host()
    {
        var actor = CreateActor(TimeSpan.FromMilliseconds(50));
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease1 =
            await ConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);
        var lease2 =
            await ConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);

        actor.Tell(new ConnectionManagerActor.Release(lease1, CanReuse: true));
        actor.Tell(new ConnectionManagerActor.Release(lease2, CanReuse: true));

        await Task.Delay(300, TestContext.Current.CancellationToken);

        var anyAlive = lease1.IsAlive || lease2.IsAlive;
        Assert.True(anyAlive);

        var bothAlive = lease1.IsAlive && lease2.IsAlive;
        Assert.False(bothAlive, "Eviction should have removed at least one expired idle connection");
    }

    [Fact(Timeout = 5000)]
    public async Task AcquireAsync_should_block_when_per_host_limit_is_full()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var leases = new List<ConnectionLease>();
        for (var i = 0; i < 6; i++)
        {
            leases.Add(await ConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken));
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await ConnectionManagerActor.AcquireAsync(actor, options, endpoint, cts.Token);
        });

        foreach (var lease in leases)
        {
            lease.Dispose();
        }
    }

    [Fact(Timeout = 5000)]
    public async Task SelectMru_should_return_latest_active_lease()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version20);

        var lease1 =
            await ConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);
        actor.Tell(new ConnectionManagerActor.Release(lease1, CanReuse: true));

        await Task.Delay(50, TestContext.Current.CancellationToken);

        lease1.MarkNoReuse();

        var lease2 =
            await ConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);

        Assert.True(lease2.LastActivity >= lease1.LastActivity);

        actor.Tell(new ConnectionManagerActor.Release(lease2, CanReuse: true));
        lease1.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task GracefulStop_should_dispose_all_leases()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease = await ConnectionManagerActor.AcquireAsync(actor, options, endpoint,
            TestContext.Current.CancellationToken);

        await actor.GracefulStop(TimeSpan.FromSeconds(5));

        Assert.False(lease.IsAlive);
    }

    [Fact(Timeout = 5000)]
    public async Task Http10_should_always_dispose_on_release()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version10);

        var lease = await ConnectionManagerActor.AcquireAsync(actor, options, endpoint,
            TestContext.Current.CancellationToken);
        actor.Tell(new ConnectionManagerActor.Release(lease, CanReuse: true));

        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(lease.IsAlive);
    }
}