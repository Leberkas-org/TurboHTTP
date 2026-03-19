using Akka.Actor;
using Akka.TestKit;
using TurboHttp.IO;
using TurboHttp.IO.Stages;
using TurboHttp.Lifecycle;


namespace TurboHttp.StreamTests.IO;

/// <summary>
/// Unit tests for <see cref="HostPool"/> connection lifecycle and stale-state cleanup.
/// </summary>
public sealed class HostPoolTests : IoActorTestBase
{
    // ── HPA-001: ConnectionFailed clears active handle ────────────────────────

    [Fact(DisplayName = "HPA-001: ConnectionFailed clears active handle; next EnsureHost is queued")]
    public void HPA_001_ConnectionFailed_ClearsActiveHandleAndQueuesNextRequester()
    {
        var controlProbe = CreateTestProbe("control");
        // Long reconnect interval so the scheduled Reconnect does not fire during the test.
        var (pool, fakeConn, handle) = SetupReadyPool(controlProbe, Key11, TimeSpan.FromSeconds(60));

        // EnsureHost should be served immediately with the active handle.
        pool.Tell(new PoolRouter.EnsureHost(Key11, TestOptions), TestActor);
        var received = ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));
        Assert.Equal(handle, received);

        // Simulate the TCP connection dropping.
        pool.Tell(new HostPool.ConnectionFailed(fakeConn));

        // Active handle must be cleared. A subsequent EnsureHost must NOT get
        // an immediate reply — the requester is queued until a new handle arrives.
        pool.Tell(new PoolRouter.EnsureHost(Key11, TestOptions), TestActor);
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    // ── HPA-002: Queued requester served when new ConnectionReady arrives ─────

    [Fact(DisplayName = "HPA-002: Queued EnsureHost requester is served when reconnected handle arrives")]
    public void HPA_002_QueuedRequester_ServedAfterReconnect()
    {
        var controlProbe = CreateTestProbe("control");
        var (pool, fakeConn, _) = SetupReadyPool(controlProbe, Key11, TimeSpan.FromMilliseconds(200));

        pool.Tell(new PoolRouter.EnsureHost(Key11, TestOptions), TestActor);
        ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));

        // Drop the connection — removes the ConnectionState from _connections.
        pool.Tell(new HostPool.ConnectionFailed(fakeConn));

        // Queue a requester while no handle is available.
        pool.Tell(new PoolRouter.EnsureHost(Key11, TestOptions), TestActor);
        ExpectNoMsg(TimeSpan.FromMilliseconds(100));

        // Reconnect fires after 200ms, spawning a new FakeConnectionActor.
        var fakeConn2 = controlProbe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(5));

        // Simulate the new connection reporting ConnectionReady.
        var handle2 = CreateHandle(fakeConn2, Key11);
        pool.Tell(new ConnectionActor.ConnectionReady(handle2), fakeConn2);

        // The queued requester should now receive the new handle.
        ExpectMsg<ConnectionHandle>(h => h == handle2, TimeSpan.FromSeconds(5));
    }
}
