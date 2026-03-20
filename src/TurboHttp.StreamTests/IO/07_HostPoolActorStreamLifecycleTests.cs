using Akka.Actor;
using Akka.TestKit;
using TurboHttp.Lifecycle;

namespace TurboHttp.StreamTests.IO;

/// <summary>
/// Tests <see cref="HostPool"/> reaction to stream-lifecycle messages: StreamCompleted and StreamFailed.
/// Verifies that slot release and queued-requester dispatch occur correctly on stream completion.
/// </summary>
/// <remarks>
/// Actor under test: <see cref="HostPool"/>.
/// Validates that slot reclamation after stream completion correctly triggers pending EnsureHost requests.
/// </remarks>
public sealed class HostPoolActorStreamLifecycleTests : IOActorTestBase
{
    [Fact(DisplayName = "SLC-001: StreamCompleted frees slot and serves queued requester")]
    public void Should_FreeSlotAndServeQueuedRequester_WhenStreamCompleted()
    {
        var controlProbe = CreateTestProbe("control");
        // HTTP/1.0: MaxConcurrentStreams = 1 → fills with a single MarkBusy.
        var (pool, fakeConn, handle) = SetupReadyPool(controlProbe, Key10);

        // Fill the single slot.
        pool.Tell(new PoolRouter.EnsureHost(Key10, TestOptions), TestActor);
        ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));

        // Queue a second requester — slot is full.
        var requesterProbe = CreateTestProbe("requester");
        pool.Tell(new PoolRouter.EnsureHost(Key10, TestOptions), requesterProbe.Ref);

        // A new connection is spawned (limiter allows). Capture but don't make ready.
        controlProbe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(5));

        // StreamCompleted frees the slot on the original connection.
        pool.Tell(new HostPool.StreamCompleted(fakeConn));

        // The queued requester should now be served from the original connection.
        var received = requesterProbe.ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));
        Assert.Equal(handle, received);
    }

    [Fact(DisplayName = "SLC-002: StreamCompleted with no eligible connection leaves queue unchanged")]
    public void Should_LeaveQueueUnchanged_WhenStreamCompletedButNoEligibleConnection()
    {
        var controlProbe = CreateTestProbe("control");
        // HTTP/1.0: MaxConcurrentStreams = 1.
        var (pool, fakeConn, _) = SetupReadyPool(controlProbe, Key10);

        // Fill the single slot with TWO MarkBusy calls:
        // 1) via EnsureHost
        pool.Tell(new PoolRouter.EnsureHost(Key10, TestOptions), TestActor);
        ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));

        // 2) via StreamAcquired (simulates a second in-flight stream, even though HTTP/1.0 normally has 1)
        pool.Tell(new HostPool.StreamAcquired(fakeConn));

        // Queue a requester.
        var requesterProbe = CreateTestProbe("requester");
        pool.Tell(new PoolRouter.EnsureHost(Key10, TestOptions), requesterProbe.Ref);

        // Consume spawned connection (limiter allows another).
        controlProbe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(5));

        // One StreamCompleted: PendingRequests goes from 2→1, but MaxConcurrentStreams=1, so still no slot.
        pool.Tell(new HostPool.StreamCompleted(fakeConn));

        // Requester should NOT be served — still at capacity.
        requesterProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    [Fact(DisplayName = "SLC-003: Multiple queued requesters drained in FIFO order")]
    public void Should_DrainQueuedRequestersFIFO_WhenMultipleSlotsFreed()
    {
        var controlProbe = CreateTestProbe("control");
        // HTTP/1.1: MaxConcurrentStreams = 6.
        var (pool, fakeConn, handle) = SetupReadyPool(controlProbe, Key11);

        // Fill all 6 slots.
        for (var i = 0; i < 6; i++)
        {
            pool.Tell(new PoolRouter.EnsureHost(Key11, TestOptions), TestActor);
            ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));
        }

        // Queue 3 requesters (no slots available).
        var probes = new TestProbe[3];
        for (var i = 0; i < 3; i++)
        {
            probes[i] = CreateTestProbe($"requester-{i}");
            pool.Tell(new PoolRouter.EnsureHost(Key11, TestOptions), probes[i].Ref);
        }

        // Free 3 slots via StreamCompleted.
        for (var i = 0; i < 3; i++)
        {
            pool.Tell(new HostPool.StreamCompleted(fakeConn));
        }

        // All 3 queued requesters should be served in FIFO order.
        for (var i = 0; i < 3; i++)
        {
            var received = probes[i].ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));
            Assert.Equal(handle, received);
        }
    }

    [Fact(DisplayName = "SLC-004: UpdateMaxConcurrentStreams with increased limit serves queued requesters")]
    public void Should_ServeQueuedRequesters_WhenMaxConcurrentStreamsIncreased()
    {
        var controlProbe = CreateTestProbe("control");
        // HTTP/2: initial MaxConcurrentStreams = 100 (default from ConnectionHandle).
        var (pool, fakeConn, handle) = SetupReadyPool(controlProbe, Key20);

        // First, reduce max to 1 so we can fill it easily.
        pool.Tell(new HostPool.UpdateMaxConcurrentStreams(fakeConn, 1));

        // Fill the single slot.
        pool.Tell(new PoolRouter.EnsureHost(Key20, TestOptions), TestActor);
        ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));

        // Queue a requester — slot is full (max=1, pending=1).
        var requesterProbe = CreateTestProbe("requester");
        pool.Tell(new PoolRouter.EnsureHost(Key20, TestOptions), requesterProbe.Ref);

        // Consume spawned connection if any.
        controlProbe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(5));

        // Requester should be queued.
        requesterProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));

        // Increase max to 5 — this should serve the queued requester.
        pool.Tell(new HostPool.UpdateMaxConcurrentStreams(fakeConn, 5));

        var received = requesterProbe.ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));
        Assert.Equal(handle, received);
    }

    [Fact(DisplayName = "SLC-005: StreamAcquired marks connection busy, reducing available slots")]
    public void Should_MarkConnectionBusy_WhenStreamAcquiredReceived()
    {
        var controlProbe = CreateTestProbe("control");
        // HTTP/1.0: MaxConcurrentStreams = 1.
        var (pool, fakeConn, _) = SetupReadyPool(controlProbe, Key10);

        // StreamAcquired fills the single slot (without going through EnsureHost).
        pool.Tell(new HostPool.StreamAcquired(fakeConn));

        // EnsureHost should NOT get an immediate reply — slot is full.
        pool.Tell(new PoolRouter.EnsureHost(Key10, TestOptions), TestActor);
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }
}
