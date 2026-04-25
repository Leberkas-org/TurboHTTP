using System.Net;
using Akka.Actor;
using Akka.TestKit.Xunit;
using Servus.Akka.IO;
using Servus.Akka.IO.Quic;
using Servus.Akka.Tests.Utils;

#pragma warning disable CA1416

namespace Servus.Akka.Tests.IO.Quic;

public sealed class QuicConnectionManagerActorSpec : TestKit
{
    private readonly InMemoryQuicConnectionFactory _factory = new();

    private static QuicOptions CreateOptions() => new()
    {
        Host = "localhost",
        Port = 443
    };

    private static RequestEndpoint CreateEndpoint() => new()
    {
        Host = "localhost",
        Port = 443,
        Scheme = "https",
        Version = HttpVersion.Version30
    };

    private IActorRef CreateActor(TimeSpan? idleTimeout = null, int maxConnectionsPerHost = 1)
        => Sys.ActorOf(Props.Create(() =>
            new QuicConnectionManagerActor(
                _factory,
                idleTimeout ?? TimeSpan.FromSeconds(5),
                Timeout.InfiniteTimeSpan,
                maxConnectionsPerHost)));

    [Fact(Timeout = 5000)]
    public async Task Acquire_should_create_new_lease()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint();

        var lease = await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
            TestContext.Current.CancellationToken);

        Assert.NotNull(lease);
        Assert.True(lease.IsAlive);

        actor.Tell(new QuicConnectionManagerActor.Release(lease, CanReuse: true));
    }

    [Fact(Timeout = 5000)]
    public async Task Acquire_should_reuse_lease_with_available_streams()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint();

        var lease1 =
            await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);
        lease1.MaxConcurrentStreams = 10;
        actor.Tell(new QuicConnectionManagerActor.Release(lease1, CanReuse: true));

        var lease2 =
            await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);

        Assert.Same(lease1, lease2);

        actor.Tell(new QuicConnectionManagerActor.Release(lease2, CanReuse: true));
    }

    [Fact(Timeout = 5000)]
    public async Task Acquire_should_block_when_all_leases_saturated()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint();

        var lease = await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
            TestContext.Current.CancellationToken);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint, cts.Token);
        });

        actor.Tell(new QuicConnectionManagerActor.Release(lease, CanReuse: false));
    }

    [Fact(Timeout = 5000)]
    public async Task Release_reusable_should_keep_lease_alive()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint();

        var lease = await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
            TestContext.Current.CancellationToken);
        actor.Tell(new QuicConnectionManagerActor.Release(lease, CanReuse: true));

        Assert.True(lease.IsAlive);
    }

    [Fact(Timeout = 5000)]
    public async Task Release_non_reusable_should_dispose_lease()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint();

        var lease = await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
            TestContext.Current.CancellationToken);
        actor.Tell(new QuicConnectionManagerActor.Release(lease, CanReuse: false));

        AwaitCondition(() => !lease.IsAlive, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.False(lease.IsAlive);
    }

    [Fact(Timeout = 5000)]
    public async Task Release_should_hand_off_to_pending_directly()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint();

        var lease1 =
            await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);
        lease1.MaxConcurrentStreams = 10;

        var pendingTask =
            QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);

        actor.Tell(new QuicConnectionManagerActor.Release(lease1, CanReuse: true));

        var handedOff = await pendingTask.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Same(lease1, handedOff);

        actor.Tell(new QuicConnectionManagerActor.Release(handedOff, CanReuse: true));
    }

    [Fact(Timeout = 5000)]
    public async Task EvictIdle_should_keep_sentinel_lease()
    {
        var actor = CreateActor(TimeSpan.FromMilliseconds(50));
        var options = CreateOptions();
        var endpoint = CreateEndpoint();

        var lease = await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
            TestContext.Current.CancellationToken);
        actor.Tell(new QuicConnectionManagerActor.Release(lease, CanReuse: true));

        await Task.Delay(300, TestContext.Current.CancellationToken);

        Assert.True(lease.IsAlive, "Sentinel lease should survive eviction (keep at least one per host)");
    }

    [Fact(Timeout = 5000)]
    public async Task EvictIdle_should_remove_extra_idle_leases()
    {
        var actor = CreateActor(TimeSpan.FromMilliseconds(50), maxConnectionsPerHost: 3);
        var options = CreateOptions();
        var endpoint = CreateEndpoint();

        var lease1 =
            await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);
        var lease2 =
            await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);
        var lease3 =
            await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);

        actor.Tell(new QuicConnectionManagerActor.Release(lease1, CanReuse: true));
        actor.Tell(new QuicConnectionManagerActor.Release(lease2, CanReuse: true));
        actor.Tell(new QuicConnectionManagerActor.Release(lease3, CanReuse: true));

        AwaitCondition(() => (lease1.IsAlive ? 1 : 0) + (lease2.IsAlive ? 1 : 0) + (lease3.IsAlive ? 1 : 0) < 3,
            TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        var aliveCount = (lease1.IsAlive ? 1 : 0) + (lease2.IsAlive ? 1 : 0) + (lease3.IsAlive ? 1 : 0);
        Assert.True(aliveCount is >= 1 and < 3,
            $"Expected 1-2 leases alive after eviction, got {aliveCount}");
    }

    [Fact(Timeout = 5000)]
    public async Task GracefulStop_should_dispose_all_leases()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint();

        var lease = await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
            TestContext.Current.CancellationToken);

        await actor.GracefulStop(TimeSpan.FromSeconds(5));

        Assert.False(lease.IsAlive);
    }

    [Fact(Timeout = 5000)]
    public async Task GracefulStop_should_fail_pending_requests()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint();

        var lease = await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
            TestContext.Current.CancellationToken);

        var pendingTask =
            QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);

        await actor.GracefulStop(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<ObjectDisposedException>(() => pendingTask);

        Assert.False(lease.IsAlive);
    }

    [Fact(Timeout = 5000)]
    public async Task Cancellation_should_skip_cancelled_acquire()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint();

        var lease = await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
            TestContext.Current.CancellationToken);

        using var cts = new CancellationTokenSource();
        var pendingTask = QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint, cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pendingTask);

        actor.Tell(new QuicConnectionManagerActor.Release(lease, CanReuse: false));
    }

    [Fact(Timeout = 5000)]
    public async Task Multiple_hosts_should_be_independent()
    {
        var actor = CreateActor();
        var options1 = new QuicOptions { Host = "host1.example.com", Port = 443 };
        var endpoint1 = new RequestEndpoint
        {
            Host = "host1.example.com",
            Port = 443,
            Scheme = "https",
            Version = HttpVersion.Version30
        };
        var options2 = new QuicOptions { Host = "host2.example.com", Port = 443 };
        var endpoint2 = new RequestEndpoint
        {
            Host = "host2.example.com",
            Port = 443,
            Scheme = "https",
            Version = HttpVersion.Version30
        };

        var lease1 =
            await QuicConnectionManagerActor.AcquireAsync(actor, options1, endpoint1,
                TestContext.Current.CancellationToken);
        var lease2 =
            await QuicConnectionManagerActor.AcquireAsync(actor, options2, endpoint2,
                TestContext.Current.CancellationToken);

        Assert.NotSame(lease1, lease2);
        Assert.Equal(endpoint1, lease1.Key);
        Assert.Equal(endpoint2, lease2.Key);

        actor.Tell(new QuicConnectionManagerActor.Release(lease1, CanReuse: false));
        actor.Tell(new QuicConnectionManagerActor.Release(lease2, CanReuse: false));
    }

    [Fact(Timeout = 5000)]
    public async Task CanAcceptStream_false_should_create_new_lease()
    {
        var actor = CreateActor(maxConnectionsPerHost: 2);
        var options = CreateOptions();
        var endpoint = CreateEndpoint();

        var lease1 =
            await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);

        var lease2 =
            await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);

        Assert.NotSame(lease1, lease2);

        actor.Tell(new QuicConnectionManagerActor.Release(lease1, CanReuse: false));
        actor.Tell(new QuicConnectionManagerActor.Release(lease2, CanReuse: false));
    }

    [Fact(Timeout = 5000)]
    public async Task MaxConcurrentStreams_should_limit_per_lease()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint();

        var lease = await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
            TestContext.Current.CancellationToken);
        lease.MaxConcurrentStreams = 2;

        actor.Tell(new QuicConnectionManagerActor.Release(lease, CanReuse: true));

        var lease2 =
            await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);
        Assert.Same(lease, lease2);

        var lease3 =
            await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);
        Assert.Same(lease, lease3);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint, cts.Token);
        });

        actor.Tell(new QuicConnectionManagerActor.Release(lease2, CanReuse: true));
        actor.Tell(new QuicConnectionManagerActor.Release(lease3, CanReuse: true));
    }

    [Fact(Timeout = 5000)]
    public async Task Acquire_after_release_should_create_new_when_not_reusable()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint();

        var lease1 =
            await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);
        actor.Tell(new QuicConnectionManagerActor.Release(lease1, CanReuse: false));

        var lease2 =
            await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);

        Assert.NotSame(lease1, lease2);
        Assert.True(lease2.IsAlive);

        actor.Tell(new QuicConnectionManagerActor.Release(lease2, CanReuse: false));
    }

    [Fact(Timeout = 5000)]
    public async Task Release_unknown_lease_should_dispose()
    {
        var actor = CreateActor();

        var provider = new FakeClientProvider();
        var handle = new QuicConnectionHandle(provider, new QuicOptions { Host = "orphan.local", Port = 443 },
            new RequestEndpoint
            {
                Host = "orphan.local",
                Port = 443,
                Scheme = "https",
                Version = HttpVersion.Version30
            });
        var orphanLease = new QuicConnectionLease(handle);
        orphanLease.MarkBusy();

        actor.Tell(new QuicConnectionManagerActor.Release(orphanLease, CanReuse: false));

        AwaitCondition(() => !orphanLease.IsAlive, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.False(orphanLease.IsAlive);
    }

    [Fact(Timeout = 5000)]
    public async Task Acquire_should_block_when_max_connections_per_host_reached()
    {
        var actor = CreateActor(maxConnectionsPerHost: 2);
        var options = CreateOptions();
        var endpoint = CreateEndpoint();

        var lease1 =
            await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);
        var lease2 =
            await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint, cts.Token);
        });

        actor.Tell(new QuicConnectionManagerActor.Release(lease1, CanReuse: false));
        actor.Tell(new QuicConnectionManagerActor.Release(lease2, CanReuse: false));
    }

    [Fact(Timeout = 5000)]
    public async Task Release_should_free_slot_for_pending_request()
    {
        var actor = CreateActor(maxConnectionsPerHost: 1);
        var options = CreateOptions();
        var endpoint = CreateEndpoint();

        var lease1 =
            await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);

        var pendingTask =
            QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);

        actor.Tell(new QuicConnectionManagerActor.Release(lease1, CanReuse: false));

        var lease2 = await pendingTask.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.NotNull(lease2);

        actor.Tell(new QuicConnectionManagerActor.Release(lease2, CanReuse: false));
    }

    [Fact(Timeout = 5000)]
    public async Task Multiple_hosts_should_maintain_separate_pools()
    {
        var actor = CreateActor();
        var options1 = new QuicOptions { Host = "host1.example.com", Port = 443 };
        var endpoint1 = new RequestEndpoint
        {
            Host = "host1.example.com",
            Port = 443,
            Scheme = "https",
            Version = HttpVersion.Version30
        };
        var options2 = new QuicOptions { Host = "host2.example.com", Port = 443 };
        var endpoint2 = new RequestEndpoint
        {
            Host = "host2.example.com",
            Port = 443,
            Scheme = "https",
            Version = HttpVersion.Version30
        };

        var lease1 =
            await QuicConnectionManagerActor.AcquireAsync(actor, options1, endpoint1,
                TestContext.Current.CancellationToken);
        var lease2 =
            await QuicConnectionManagerActor.AcquireAsync(actor, options2, endpoint2,
                TestContext.Current.CancellationToken);

        Assert.NotSame(lease1, lease2);

        actor.Tell(new QuicConnectionManagerActor.Release(lease1, CanReuse: true));
        actor.Tell(new QuicConnectionManagerActor.Release(lease2, CanReuse: true));

        var lease3 =
            await QuicConnectionManagerActor.AcquireAsync(actor, options1, endpoint1,
                TestContext.Current.CancellationToken);
        var lease4 =
            await QuicConnectionManagerActor.AcquireAsync(actor, options2, endpoint2,
                TestContext.Current.CancellationToken);

        Assert.Same(lease1, lease3);
        Assert.Same(lease2, lease4);

        actor.Tell(new QuicConnectionManagerActor.Release(lease3, CanReuse: false));
        actor.Tell(new QuicConnectionManagerActor.Release(lease4, CanReuse: false));
    }

    [Fact(Timeout = 5000)]
    public async Task Idle_timeout_zero_should_disable_eviction()
    {
        var actor = CreateActor(TimeSpan.Zero);
        var options = CreateOptions();
        var endpoint = CreateEndpoint();

        var lease1 =
            await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);
        actor.Tell(new QuicConnectionManagerActor.Release(lease1, CanReuse: true));

        await Task.Delay(500, TestContext.Current.CancellationToken);

        // Should still be alive (no eviction)
        Assert.True(lease1.IsAlive);

        actor.Tell(new QuicConnectionManagerActor.Release(lease1, CanReuse: false));
    }

    [Fact(Timeout = 5000)]
    public async Task Acquire_with_already_cancelled_token_should_be_ignored_by_actor()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // UnsafeRegister fires synchronously for already-cancelled tokens, so TCS is completed
        // before actor.Tell. The actor receives a completed TCS and immediately returns.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint, cts.Token));

        // Actor must still be alive and functional
        var lease = await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
            TestContext.Current.CancellationToken);
        Assert.NotNull(lease);

        actor.Tell(new QuicConnectionManagerActor.Release(lease, CanReuse: false));
    }

    [Fact(Timeout = 5000)]
    public async Task Established_with_cancelled_caller_should_release_back_to_pool()
    {
        var slowFactory = new SlowQuicConnectionFactory(TimeSpan.FromMilliseconds(200));
        var actor = Sys.ActorOf(Props.Create(() =>
            new QuicConnectionManagerActor(slowFactory, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan,
                maxConnectionsPerHost: 1)));
        var options = CreateOptions();
        var endpoint = CreateEndpoint();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        // acquire1: cancelled before factory completes; establishing slot held
        var task1 = QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint, cts.Token);

        // acquire2: queued (max=1, establishing=1) → served after OnEstablished cascades via OnRelease
        var task2 = QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task1);

        // When factory resolves, OnEstablished → TrySetResult(tcs1) fails →
        // OnRelease → pending queue → ServeNextPending / direct handoff for task2
        var lease = await task2;
        Assert.NotNull(lease);
        Assert.True(lease.IsAlive);

        actor.Tell(new QuicConnectionManagerActor.Release(lease, CanReuse: false));
    }

    [Fact(Timeout = 5000)]
    public async Task Evicted_idle_connection_should_not_be_reused()
    {
        var actor = CreateActor(TimeSpan.FromMilliseconds(50));
        var options = CreateOptions();
        var endpoint = CreateEndpoint();

        var lease1 =
            await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);
        actor.Tell(new QuicConnectionManagerActor.Release(lease1, CanReuse: true));

        // Lease should be evicted, next acquire creates new
        var lease2 =
            await QuicConnectionManagerActor.AcquireAsync(actor, options, endpoint,
                TestContext.Current.CancellationToken);
        Assert.NotNull(lease2);

        actor.Tell(new QuicConnectionManagerActor.Release(lease2, CanReuse: false));
    }
}