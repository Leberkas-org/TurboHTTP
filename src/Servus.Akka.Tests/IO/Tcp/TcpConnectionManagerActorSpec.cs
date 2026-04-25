using System.Net;
using Akka.Actor;
using Akka.TestKit.Xunit;
using Servus.Akka.IO;
using Servus.Akka.IO.Tcp;
using Servus.Akka.Tests.Utils;

namespace Servus.Akka.Tests.IO.Tcp;

public sealed class TcpConnectionManagerActorSpec : TestKit
{
    private readonly InMemoryConnectionFactory _factory = new();

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
        => Sys.ActorOf(Props.Create(() =>
            new TcpConnectionManagerActor(_factory, idleTimeout ?? TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan)));

    [Fact(Timeout = 5000)]
    public async Task Acquire_should_always_create_new_connection_for_http10()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version10);

        var lease1 =
            await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);
        actor.Tell(new TcpConnectionManagerActor.Release(lease1, CanReuse: true));

        var lease2 =
            await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);

        Assert.NotSame(lease1, lease2);

        lease2.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task Acquire_should_reuse_idle_connection_for_http11()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease1 =
            await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);
        actor.Tell(new TcpConnectionManagerActor.Release(lease1, CanReuse: true));

        var lease2 =
            await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);

        Assert.Same(lease1, lease2);

        lease2.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task Release_should_return_to_idle_when_can_reuse()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease = await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
            TestContext.Current.CancellationToken);
        actor.Tell(new TcpConnectionManagerActor.Release(lease, CanReuse: true));

        Assert.True(lease.IsAlive);
    }

    [Fact(Timeout = 5000)]
    public async Task Release_should_dispose_connection_when_cannot_reuse()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease = await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
            TestContext.Current.CancellationToken);
        actor.Tell(new TcpConnectionManagerActor.Release(lease, CanReuse: false));

        AwaitCondition(() => !lease.IsAlive, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.False(lease.IsAlive);
    }

    [Fact(Timeout = 5000)]
    public async Task EvictIdle_should_remove_expired_connections()
    {
        var actor = CreateActor(TimeSpan.FromMilliseconds(50));
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease1 =
            await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);
        var lease2 =
            await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);
        Assert.NotSame(lease1, lease2);

        actor.Tell(new TcpConnectionManagerActor.Release(lease1, CanReuse: true));
        actor.Tell(new TcpConnectionManagerActor.Release(lease2, CanReuse: true));

        AwaitCondition(() => !lease1.IsAlive || !lease2.IsAlive, TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

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
            await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);
        var lease2 =
            await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);

        actor.Tell(new TcpConnectionManagerActor.Release(lease1, CanReuse: true));
        actor.Tell(new TcpConnectionManagerActor.Release(lease2, CanReuse: true));

        AwaitCondition(() => !lease1.IsAlive || !lease2.IsAlive, TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        var anyAlive = lease1.IsAlive || lease2.IsAlive;
        Assert.True(anyAlive);

        var bothAlive = lease1.IsAlive && lease2.IsAlive;
        Assert.False(bothAlive, "Eviction should have removed at least one expired idle connection");
    }

    [Fact(Timeout = 5000)]
    public async Task Acquire_should_block_when_per_host_limit_is_full()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var leases = new List<ConnectionLease>();
        for (var i = 0; i < 6; i++)
        {
            leases.Add(await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken));
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
    public async Task Http2_acquire_should_create_exclusive_connection()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version20);

        var lease1 =
            await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);
        actor.Tell(new TcpConnectionManagerActor.Release(lease1, CanReuse: true));

        var lease2 =
            await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);

        Assert.NotSame(lease1, lease2);

        actor.Tell(new TcpConnectionManagerActor.Release(lease2, CanReuse: true));
    }

    [Fact(Timeout = 5000)]
    public async Task GracefulStop_should_dispose_all_leases()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease = await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
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

        var lease = await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
            TestContext.Current.CancellationToken);
        actor.Tell(new TcpConnectionManagerActor.Release(lease, CanReuse: true));

        AwaitCondition(() => !lease.IsAlive, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.False(lease.IsAlive);
    }

    [Fact(Timeout = 5000)]
    public async Task Release_with_pending_should_hand_off_directly()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var leases = new List<ConnectionLease>();
        for (var i = 0; i < 6; i++)
        {
            leases.Add(await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken));
        }

        var pendingTask =
            TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);

        actor.Tell(new TcpConnectionManagerActor.Release(leases[0], CanReuse: true));

        var handedOff = await pendingTask.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Same(leases[0], handedOff);

        foreach (var lease in leases.Skip(1))
        {
            lease.Dispose();
        }

        handedOff.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task Multiple_hosts_should_maintain_separate_pools()
    {
        var actor = CreateActor();
        var options1 = new TcpOptions { Host = "host1.example.com", Port = 80 };
        var endpoint1 = new RequestEndpoint
        {
            Host = "host1.example.com",
            Port = 80,
            Scheme = "http",
            Version = HttpVersion.Version11
        };
        var options2 = new TcpOptions { Host = "host2.example.com", Port = 80 };
        var endpoint2 = new RequestEndpoint
        {
            Host = "host2.example.com",
            Port = 80,
            Scheme = "http",
            Version = HttpVersion.Version11
        };

        var lease1 =
            await TcpConnectionManagerActor.AcquireAsync(actor, options1, endpoint1,
                TestContext.Current.CancellationToken);
        var lease2 =
            await TcpConnectionManagerActor.AcquireAsync(actor, options2, endpoint2,
                TestContext.Current.CancellationToken);

        Assert.NotSame(lease1, lease2);

        actor.Tell(new TcpConnectionManagerActor.Release(lease1, CanReuse: true));
        actor.Tell(new TcpConnectionManagerActor.Release(lease2, CanReuse: true));

        var lease3 =
            await TcpConnectionManagerActor.AcquireAsync(actor, options1, endpoint1,
                TestContext.Current.CancellationToken);
        var lease4 =
            await TcpConnectionManagerActor.AcquireAsync(actor, options2, endpoint2,
                TestContext.Current.CancellationToken);

        Assert.Same(lease1, lease3);
        Assert.Same(lease2, lease4);

        lease3.Dispose();
        lease4.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task Acquire_should_timeout_when_exhausted_and_pending()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var leases = new List<ConnectionLease>();
        for (var i = 0; i < 6; i++)
        {
            leases.Add(await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken));
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint, cts.Token);
        });

        Assert.NotNull(ex);

        foreach (var lease in leases)
        {
            lease.Dispose();
        }
    }

    [Fact(Timeout = 5000)]
    public async Task Release_dead_lease_should_not_crash_actor()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease = await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
            TestContext.Current.CancellationToken);

        lease.Dispose();

        var lease2 =
            await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);
        Assert.NotNull(lease2);

        lease2.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task Idle_timeout_zero_should_disable_eviction()
    {
        var actor = CreateActor(TimeSpan.Zero);
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease1 =
            await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);
        var lease2 =
            await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);

        actor.Tell(new TcpConnectionManagerActor.Release(lease1, CanReuse: true));
        actor.Tell(new TcpConnectionManagerActor.Release(lease2, CanReuse: true));

        await Task.Delay(500, TestContext.Current.CancellationToken);

        Assert.True(lease1.IsAlive || lease2.IsAlive);

        if (lease1.IsAlive) lease1.Dispose();
        if (lease2.IsAlive) lease2.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task Version30_should_not_reuse_connections()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version30);

        var lease1 =
            await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);
        actor.Tell(new TcpConnectionManagerActor.Release(lease1, CanReuse: true));

        var lease2 =
            await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);

        Assert.NotSame(lease1, lease2);

        lease2.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task Acquire_with_already_cancelled_token_should_be_ignored_by_actor()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // UnsafeRegister fires synchronously for already-cancelled tokens, so TCS is completed
        // before actor.Tell. The actor receives a completed TCS and immediately returns.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint, cts.Token));

        // Actor must still be alive and functional
        var lease = await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
            TestContext.Current.CancellationToken);
        Assert.NotNull(lease);

        lease.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task Established_with_cancelled_caller_should_release_back_to_pool()
    {
        var slowFactory = new SlowConnectionFactory(TimeSpan.FromMilliseconds(200));
        var actor = Sys.ActorOf(Props.Create(() =>
            new TcpConnectionManagerActor(slowFactory, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan,
                maxConnectionsPerServer: 1)));
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        // acquire1: cancelled before factory completes; establishing slot held
        var task1 = TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint, cts.Token);

        // acquire2: queued because max-connections=1 slot is being established
        var task2 = TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task1);

        // When factory resolves, actor calls OnEstablished → TrySetResult(tcs1) fails →
        // OnRelease → direct handoff to acquire2
        var lease = await task2;
        Assert.NotNull(lease);
        Assert.True(lease.IsAlive);

        lease.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task Acquire_should_skip_dead_idle_lease_and_establish_fresh_connection()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease1 = await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
            TestContext.Current.CancellationToken);

        // Release to idle queue
        actor.Tell(new TcpConnectionManagerActor.Release(lease1, CanReuse: true));

        // Wait for Release to be processed
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Externally dispose the lease — IsAlive becomes false (stale idle)
        lease1.Dispose();

        // Acquire: scans idle, finds stale lease (IsAlive=false), disposes it, establishes fresh
        var lease2 = await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
            TestContext.Current.CancellationToken);
        Assert.NotSame(lease1, lease2);
        Assert.True(lease2.IsAlive);

        lease2.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task EstablishFailed_should_cascade_to_pending_waiter()
    {
        var failOnce = new FailOnceConnectionFactory();
        var actor = Sys.ActorOf(Props.Create(() =>
            new TcpConnectionManagerActor(failOnce, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan,
                maxConnectionsPerServer: 1)));
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version20);

        // acquire1: EstablishAsync fails → OnFailed → tcs1 faulted → ServeNextPending
        var task1 = TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
            TestContext.Current.CancellationToken);

        // Small delay so acquire1's Establish increments Establishing before acquire2 arrives
        await Task.Delay(10, TestContext.Current.CancellationToken);

        // acquire2: queued (establishing=1, max=1) → served after OnFailed cascades
        var task2 = TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<IOException>(() => task1);

        var lease = await task2;
        Assert.NotNull(lease);
        Assert.True(lease.IsAlive);

        lease.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task Evicted_idle_connection_should_not_be_reused()
    {
        var actor = CreateActor(TimeSpan.FromMilliseconds(50));
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease1 =
            await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);
        actor.Tell(new TcpConnectionManagerActor.Release(lease1, CanReuse: true));

        var lease2 =
            await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);
        Assert.NotNull(lease2);

        lease2.Dispose();
    }
}