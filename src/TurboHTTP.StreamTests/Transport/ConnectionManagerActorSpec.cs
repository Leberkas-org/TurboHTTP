using System.Net;
using Akka.Actor;
using TurboHTTP.Internal;
using TurboHTTP.Tests.Shared;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.StreamTests.Transport;

/// <summary>
/// Tests <see cref="TcpConnectionManagerActor"/> directly: version-aware acquire/release,
/// idle reuse, per-host limits, idle eviction, and actor disposal.
/// Uses <see cref="InMemoryConnectionFactory"/> — no sockets involved.
/// </summary>
public sealed class TcpConnectionManagerActorSpec : IAsyncLifetime
{
    private ActorSystem? _system;
    private InMemoryConnectionFactory _factory = null!;

    public ValueTask InitializeAsync()
    {
        _system = ActorSystem.Create("connection-manager-tests");
        _factory = new InMemoryConnectionFactory();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_system is not null)
        {
            await _system.Terminate();
        }
    }

    private static TcpOptions CreateOptions() => new()
    {
        Host = "127.0.0.1",
        Port = 8080
    };

    private static RequestEndpoint CreateEndpoint(Version? version = null) => new()
    {
        Host = "127.0.0.1",
        Port = 8080,
        Scheme = "http",
        Version = version ?? HttpVersion.Version11
    };

    private IActorRef CreateActor(TimeSpan? idleTimeout = null)
        => _system!.ActorOf(Props.Create(() =>
            new TcpConnectionManagerActor(_factory, idleTimeout ?? TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan)));

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public async Task Acquire_should_always_create_new_connection_for_http10()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version10);

        var lease1 = await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);
        actor.Tell(new TcpConnectionManagerActor.Release(lease1, CanReuse: true));

        var lease2 = await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);

        Assert.NotSame(lease1, lease2);

        lease2.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public async Task Acquire_should_reuse_idle_connection_for_http11()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease1 = await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);
        actor.Tell(new TcpConnectionManagerActor.Release(lease1, CanReuse: true));

        await Task.Delay(50, TestContext.Current.CancellationToken);

        var lease2 = await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);

        Assert.Same(lease1, lease2);

        lease2.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public async Task Release_should_return_to_idle_when_can_reuse()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease = await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);
        actor.Tell(new TcpConnectionManagerActor.Release(lease, CanReuse: true));

        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.True(lease.IsAlive);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public async Task Release_should_dispose_connection_when_cannot_reuse()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease = await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);
        actor.Tell(new TcpConnectionManagerActor.Release(lease, CanReuse: false));

        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.False(lease.IsAlive);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public async Task EvictIdle_should_remove_expired_connections()
    {
        var actor = CreateActor(TimeSpan.FromMilliseconds(50));
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease1 = await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);
        var lease2 = await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);
        Assert.NotSame(lease1, lease2);

        actor.Tell(new TcpConnectionManagerActor.Release(lease1, CanReuse: true));
        actor.Tell(new TcpConnectionManagerActor.Release(lease2, CanReuse: true));

        await Task.Delay(300, TestContext.Current.CancellationToken);

        var evictedCount = (!lease1.IsAlive ? 1 : 0) + (!lease2.IsAlive ? 1 : 0);
        Assert.True(evictedCount >= 1, "At least one idle connection should have been evicted");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public async Task EvictIdle_should_keep_at_least_one_per_host()
    {
        var actor = CreateActor(TimeSpan.FromMilliseconds(50));
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease1 = await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);
        var lease2 = await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);

        actor.Tell(new TcpConnectionManagerActor.Release(lease1, CanReuse: true));
        actor.Tell(new TcpConnectionManagerActor.Release(lease2, CanReuse: true));

        await Task.Delay(300, TestContext.Current.CancellationToken);

        var anyAlive = lease1.IsAlive || lease2.IsAlive;
        Assert.True(anyAlive);

        var bothAlive = lease1.IsAlive && lease2.IsAlive;
        Assert.False(bothAlive, "Eviction should have removed at least one expired idle connection");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public async Task Acquire_should_block_when_per_host_limit_is_full()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var leases = new List<ConnectionLease>();
        for (var i = 0; i < 6; i++)
        {
            leases.Add(await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken));
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint, cts.Token);
        });

        foreach (var lease in leases)
        {
            lease.Dispose();
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113")]
    public async Task Http2_acquire_should_create_exclusive_connection()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version20);

        var lease1 = await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);
        actor.Tell(new TcpConnectionManagerActor.Release(lease1, CanReuse: true));

        await Task.Delay(50, TestContext.Current.CancellationToken);

        var lease2 = await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);

        Assert.NotSame(lease1, lease2);

        actor.Tell(new TcpConnectionManagerActor.Release(lease2, CanReuse: true));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public async Task GracefulStop_should_dispose_all_leases()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease = await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);

        await actor.GracefulStop(TimeSpan.FromSeconds(5));

        Assert.False(lease.IsAlive);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public async Task Http10_should_always_dispose_on_release()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version10);

        var lease = await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);
        actor.Tell(new TcpConnectionManagerActor.Release(lease, CanReuse: true));

        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(lease.IsAlive);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public async Task Release_with_pending_should_hand_off_directly()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var leases = new List<ConnectionLease>();
        for (var i = 0; i < 6; i++)
        {
            leases.Add(await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken));
        }

        var pendingTask = TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);

        actor.Tell(new TcpConnectionManagerActor.Release(leases[0], CanReuse: true));

        var handedOff = await pendingTask.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Same(leases[0], handedOff);

        foreach (var lease in leases.Skip(1))
        {
            lease.Dispose();
        }

        handedOff.Dispose();
    }
}
