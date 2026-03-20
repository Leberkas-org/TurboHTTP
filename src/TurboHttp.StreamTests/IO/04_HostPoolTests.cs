using Akka.Actor;
using TurboHttp.Lifecycle;


namespace TurboHttp.StreamTests.IO;

/// <summary>
/// Tests <see cref="HostPool"/> composite behavior including failure recovery, reconnect scheduling, and slot management.
/// Covers ConnectionFailed, Reconnect, and EnsureHost message interactions.
/// </summary>
/// <remarks>
/// Actor under test: <see cref="HostPool"/>.
/// Validates the pool actor's response to connection failures and subsequent reconnect scheduling.
/// </remarks>
public sealed class HostPoolTests : IOActorTestBase
{
    [Fact(DisplayName = "HPA-001: ConnectionFailed clears active handle; next EnsureHost is queued")]
    public void Should_ClearActiveHandleAndQueueRequester_WhenConnectionFailed()
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

    [Fact(DisplayName = "HPA-002: Queued EnsureHost requester is served when reconnected handle arrives")]
    public void Should_ServeQueuedRequester_WhenReconnectedHandleArrives()
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
