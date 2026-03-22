using Akka.Actor;
using TurboHttp.Pooling;

namespace TurboHttp.StreamTests.IO;

/// <summary>
/// Tests version-aware connection pooling in <see cref="HostPool"/> for HTTP/3 (QUIC).
/// Verifies that QUIC connections bypass the per-host limiter, reuse connections via
/// <see cref="Http3ConnectionActor.OpenNewStream"/>, and fall back to new connections when
/// the stream limit is reached.
/// </summary>
public sealed class HostPoolQuicTests : IOActorTestBase
{
    [Fact(DisplayName = "QUIC-001: First QUIC request spawns connection and returns handle")]
    public void Should_SpawnConnectionAndReturnHandle_WhenFirstQuicRequest()
    {
        var controlProbe = CreateTestProbe("control");
        var (pool, fakeConn, handle) = SetupReadyPool(controlProbe, Key30, TestQuicOptions);

        // First EnsureHost → should find the ready connection and send OpenNewStream
        pool.Tell(new PoolRouter.EnsureHost(Key30, TestQuicOptions), TestActor);

        // For QUIC, HostPool sends OpenNewStream to the connection actor which forwards to controlProbe.
        var openMsg = controlProbe.ExpectMsg<Http3ConnectionActor.OpenNewStream>(TimeSpan.FromSeconds(5));
        Assert.Equal(TestActor, openMsg.Requester);
    }

    [Fact(DisplayName = "QUIC-002: Subsequent QUIC requests reuse same connection via OpenNewStream")]
    public void Should_ReuseConnectionViaOpenNewStream_WhenSubsequentQuicRequests()
    {
        var controlProbe = CreateTestProbe("control");
        var pool = CreatePool(controlProbe, Key30, TestQuicOptions);

        // Wait for the eagerly spawned connection actor
        var fakeConn = controlProbe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(5));
        var handle = CreateHandle(fakeConn, Key30);
        pool.Tell(new ConnectionActorBase.ConnectionReady(handle), fakeConn);

        // Send multiple requests — each should route to same connection via OpenNewStream
        var requester1 = CreateTestProbe("r1");
        var requester2 = CreateTestProbe("r2");
        var requester3 = CreateTestProbe("r3");

        pool.Tell(new PoolRouter.EnsureHost(Key30, TestQuicOptions), requester1.Ref);
        pool.Tell(new PoolRouter.EnsureHost(Key30, TestQuicOptions), requester2.Ref);
        pool.Tell(new PoolRouter.EnsureHost(Key30, TestQuicOptions), requester3.Ref);

        // All three OpenNewStream messages are forwarded to controlProbe by the FakeConnectionActor
        var msg1 = controlProbe.ExpectMsg<Http3ConnectionActor.OpenNewStream>(TimeSpan.FromSeconds(5));
        var msg2 = controlProbe.ExpectMsg<Http3ConnectionActor.OpenNewStream>(TimeSpan.FromSeconds(5));
        var msg3 = controlProbe.ExpectMsg<Http3ConnectionActor.OpenNewStream>(TimeSpan.FromSeconds(5));

        Assert.Equal(requester1.Ref, msg1.Requester);
        Assert.Equal(requester2.Ref, msg2.Requester);
        Assert.Equal(requester3.Ref, msg3.Requester);

        // No new connection should have been spawned (no IActorRef message)
        controlProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    [Fact(DisplayName = "QUIC-003: QUIC pool bypasses per-host connection limiter")]
    public void Should_BypassLimiter_WhenQuicConnectionsSpawned()
    {
        var controlProbe = CreateTestProbe("control");
        var pool = CreatePool(controlProbe, Key30, TestQuicOptions);

        // First connection spawned eagerly
        var fakeConn1 = controlProbe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(5));
        var handle1 = CreateHandle(fakeConn1, Key30);
        pool.Tell(new ConnectionActorBase.ConnectionReady(handle1), fakeConn1);

        // Fill all 100 QUIC streams (default MaxConcurrentStreams for HTTP/3)
        for (var i = 0; i < 100; i++)
        {
            pool.Tell(new PoolRouter.EnsureHost(Key30, TestQuicOptions), CreateTestProbe().Ref);
            controlProbe.ExpectMsg<Http3ConnectionActor.OpenNewStream>(TimeSpan.FromSeconds(5));
        }

        // Connection is now at capacity. Next request should spawn a NEW connection
        // (not be blocked by limiter).
        var overflowRequester = CreateTestProbe("overflow");
        pool.Tell(new PoolRouter.EnsureHost(Key30, TestQuicOptions), overflowRequester.Ref);

        // Should spawn a new connection (limiter bypassed for QUIC) — new actor reports its ref
        var fakeConn2 = controlProbe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(5));
        Assert.NotEqual(fakeConn1, fakeConn2);
    }

    [Fact(DisplayName = "QUIC-004: HTTP/1.1 requests still use direct handle (regression)")]
    public void Should_UseDirectHandle_WhenHttp11Request()
    {
        var controlProbe = CreateTestProbe("control");
        var (pool, fakeConn, handle) = SetupReadyPool(controlProbe, Key11);

        // HTTP/1.1 → should receive handle directly, NOT OpenNewStream
        pool.Tell(new PoolRouter.EnsureHost(Key11, TestOptions), TestActor);
        var received = ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));
        Assert.Equal(handle, received);

        // FakeConnectionActor should NOT receive OpenNewStream (nothing forwarded to controlProbe)
        controlProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    [Fact(DisplayName = "QUIC-005: HTTP/2 requests still use direct handle (regression)")]
    public void Should_UseDirectHandle_WhenHttp20Request()
    {
        var controlProbe = CreateTestProbe("control");
        var (pool, fakeConn, handle) = SetupReadyPool(controlProbe, Key20);

        pool.Tell(new PoolRouter.EnsureHost(Key20, TestOptions), TestActor);
        var received = ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));
        Assert.Equal(handle, received);

        controlProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    [Fact(DisplayName = "QUIC-006: QUIC queued requesters served via OpenNewStream when connection becomes ready")]
    public void Should_ServeQueuedRequestersViaOpenNewStream_WhenQuicConnectionBecomesReady()
    {
        var controlProbe = CreateTestProbe("control");
        var pool = CreatePool(controlProbe, Key30, TestQuicOptions);

        // Connection spawned but not yet ready
        var fakeConn = controlProbe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(5));

        // Queue requests before connection is ready
        var r1 = CreateTestProbe("r1");
        var r2 = CreateTestProbe("r2");
        pool.Tell(new PoolRouter.EnsureHost(Key30, TestQuicOptions), r1.Ref);
        pool.Tell(new PoolRouter.EnsureHost(Key30, TestQuicOptions), r2.Ref);

        // No handle yet → both queued
        r1.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
        r2.ExpectNoMsg(TimeSpan.FromMilliseconds(200));

        // Now deliver the handle
        var handle = CreateHandle(fakeConn, Key30);
        pool.Tell(new ConnectionActorBase.ConnectionReady(handle), fakeConn);

        // Both queued requesters should be served via OpenNewStream (forwarded to controlProbe)
        var msg1 = controlProbe.ExpectMsg<Http3ConnectionActor.OpenNewStream>(TimeSpan.FromSeconds(5));
        var msg2 = controlProbe.ExpectMsg<Http3ConnectionActor.OpenNewStream>(TimeSpan.FromSeconds(5));

        var requesters = new[] { msg1.Requester, msg2.Requester };
        Assert.Contains(r1.Ref, requesters);
        Assert.Contains(r2.Ref, requesters);
    }

    [Fact(DisplayName = "QUIC-007: StreamCompleted frees slot and serves queued QUIC requesters")]
    public void Should_FreeSlotAndServeQueued_WhenQuicStreamCompleted()
    {
        var controlProbe = CreateTestProbe("control");
        var (pool, fakeConn, handle) = SetupReadyPool(controlProbe, Key30, TestQuicOptions);

        // Fill to capacity (MaxConcurrentStreams = 100 for HTTP/3)
        for (var i = 0; i < 100; i++)
        {
            pool.Tell(new PoolRouter.EnsureHost(Key30, TestQuicOptions), CreateTestProbe().Ref);
            controlProbe.ExpectMsg<Http3ConnectionActor.OpenNewStream>(TimeSpan.FromSeconds(5));
        }

        // Queue one more — should be queued since at capacity.
        // This also triggers a new connection spawn (all slots full).
        var queued = CreateTestProbe("queued");
        pool.Tell(new PoolRouter.EnsureHost(Key30, TestQuicOptions), queued.Ref);

        // Drain the newly spawned connection actor's Self from controlProbe
        controlProbe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(5));

        // Complete a stream to free a slot
        pool.Tell(new HostPool.StreamCompleted(fakeConn), fakeConn);

        // The queued requester should now be served via OpenNewStream on the original connection
        var openMsg = controlProbe.ExpectMsg<Http3ConnectionActor.OpenNewStream>(TimeSpan.FromSeconds(5));
        Assert.Equal(queued.Ref, openMsg.Requester);
    }
}
