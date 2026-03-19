using Akka.Actor;
using Akka.TestKit;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.StreamTests.IO;

/// <summary>
/// Unit tests for <see cref="HostPoolActor.HandleEnsureHost"/> rewrite (TASK-9-011).
/// </summary>
public sealed class HostPoolActorEnsureHostTests : IoActorTestBase
{
    // ── EH-001: Slot available → handle returned immediately, MarkBusy called ──

    [Fact(DisplayName = "EH-001: Slot available returns handle immediately and marks connection busy")]
    public void EH_001_SlotAvailable_ReturnsHandleImmediately()
    {
        var controlProbe = CreateTestProbe("control");
        var (pool, _, handle) = SetupReadyPool(controlProbe, Key11);

        // First EnsureHost → immediate handle reply.
        pool.Tell(new PoolRouterActor.EnsureHost(Key11, TestOptions), TestActor);
        var received = ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));
        Assert.Equal(handle, received);

        // HTTP/1.1 MaxConcurrentStreams = 6. After MarkBusy once, PendingRequests = 1.
        // Send 5 more to fill to capacity (PendingRequests = 6).
        for (var i = 0; i < 5; i++)
        {
            pool.Tell(new PoolRouterActor.EnsureHost(Key11, TestOptions), TestActor);
            ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));
        }

        // 7th request — all 6 slots occupied → should be queued (not immediately served).
        pool.Tell(new PoolRouterActor.EnsureHost(Key11, TestOptions), TestActor);
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    // ── EH-002: All slots full + under limiter → requester queued, new connection spawned ──

    [Fact(DisplayName = "EH-002: All slots full spawns new connection when under limiter limit")]
    public void EH_002_AllSlotsFull_SpawnsNewConnection()
    {
        var controlProbe = CreateTestProbe("control");
        // HTTP/1.0: MaxConcurrentStreams = 1 → fills with a single MarkBusy.
        var (pool, fakeConn1, _) = SetupReadyPool(controlProbe, Key10);

        // Make connection ready and occupy its single slot.
        pool.Tell(new PoolRouterActor.EnsureHost(Key10, TestOptions), TestActor);
        ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));

        // Now all slots are full (HTTP/1.0 max 1). Next request → queued + spawn attempt.
        var requesterProbe = CreateTestProbe("requester");
        pool.Tell(new PoolRouterActor.EnsureHost(Key10, TestOptions), requesterProbe.Ref);

        // SpawnConnection should have been called — a new FakeConnectionActor should appear.
        var fakeConn2 = controlProbe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(5));
        Assert.NotEqual(fakeConn1, fakeConn2);

        // Requester should NOT have received a reply yet (no handle on conn2).
        requesterProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));

        // Simulate the new connection becoming ready.
        var handle2 = CreateHandle(fakeConn2, Key10);
        pool.Tell(new ConnectionActor.ConnectionReady(handle2), fakeConn2);

        // The queued requester should now receive the handle.
        requesterProbe.ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));
    }

    // ── EH-003: All slots full + at limiter limit → requester queued, no spawn ──

    [Fact(DisplayName = "EH-003: At limiter limit queues requester without spawning")]
    public void EH_003_AtLimiterLimit_QueuesOnly()
    {
        var controlProbe = CreateTestProbe("control");
        // HTTP/1.0: MaxConcurrentStreams = 1 per connection. Limiter default = 6.
        var (pool, conn1, _) = SetupReadyPool(controlProbe, Key10);

        // Fill slot on connection #1.
        pool.Tell(new PoolRouterActor.EnsureHost(Key10, TestOptions), TestActor);
        ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5)); // conn1 now busy

        // Spawn connections 2–6: each EnsureHost finds conn full → queues + spawns.
        // Then make each ready and fill its slot.
        for (var i = 2; i <= 6; i++)
        {
            var probe = CreateTestProbe($"requester-{i}");
            pool.Tell(new PoolRouterActor.EnsureHost(Key10, TestOptions), probe.Ref);

            var connN = controlProbe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(5));
            pool.Tell(new ConnectionActor.ConnectionReady(CreateHandle(connN, Key10)), connN);

            probe.ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));

            // Fill connN's slot via a fresh EnsureHost.
            pool.Tell(new PoolRouterActor.EnsureHost(Key10, TestOptions), CreateTestProbe().Ref);
        }

        // Now: 6 connections, all at capacity. Limiter is at capacity (6/6).
        // 7th request — all full AND at limiter limit → queued only, no spawn.
        var finalRequester = CreateTestProbe("final-requester");
        pool.Tell(new PoolRouterActor.EnsureHost(Key10, TestOptions), finalRequester.Ref);

        finalRequester.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
        controlProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }
}
