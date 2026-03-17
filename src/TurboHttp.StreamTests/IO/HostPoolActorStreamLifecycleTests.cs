using System;
using System.Buffers;
using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using TurboHttp.Client;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.StreamTests.IO;

/// <summary>
/// Unit tests for <see cref="HostPoolActor"/> stream lifecycle handlers (TASK-9-012):
/// <see cref="HostPoolActor.StreamCompleted"/>, <see cref="HostPoolActor.StreamAcquired"/>,
/// <see cref="HostPoolActor.UpdateMaxConcurrentStreams"/>, and ServeQueuedRequesters.
/// </summary>
public sealed class HostPoolActorStreamLifecycleTests : TestKit
{
    private static readonly TcpOptions TestOptions = new() { Host = "localhost", Port = 8080 };

    private static readonly HostKey Key10 = new()
    {
        Host = "localhost", Port = 8080, Scheme = "http", Version = HttpVersion.Version10
    };

    private static readonly HostKey Key11 = new()
    {
        Host = "localhost", Port = 8080, Scheme = "http", Version = HttpVersion.Version11
    };

    private static readonly HostKey Key20 = new()
    {
        Host = "localhost", Port = 8080, Scheme = "http", Version = HttpVersion.Version20
    };

    // ── Helpers ─────────────────────────────────────────────────────────────

    private sealed class FakeConnectionActor : ReceiveActor
    {
        private readonly IActorRef _controlProbe;

        public FakeConnectionActor(IActorRef controlProbe)
        {
            _controlProbe = controlProbe;
        }

        protected override void PreStart()
        {
            _controlProbe.Tell(Self);
        }
    }

    private IActorRef CreatePool(TestProbe controlProbe, HostKey key)
    {
        var config = new TurboClientOptions
        {
            ReconnectInterval = TimeSpan.FromSeconds(60),
            IdleTimeout = TimeSpan.FromSeconds(60),
            MaxReconnectAttempts = 3
        };

        var hostConfig = new HostPoolActor.HostPoolConfig(
            TestOptions,
            config,
            key,
            ConnectionFactory: () => Props.Create(() => new FakeConnectionActor(controlProbe.Ref)));

        return Sys.ActorOf(Props.Create(() => new HostPoolActor(hostConfig)));
    }

    private static ConnectionHandle CreateHandle(IActorRef connectionActor, HostKey key)
    {
        var outbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var inbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        return new ConnectionHandle(outbound.Writer, inbound.Reader, key, connectionActor);
    }

    /// <summary>
    /// Creates a pool, waits for the eagerly-spawned connection, makes it ready, and returns all pieces.
    /// </summary>
    private (IActorRef Pool, IActorRef FakeConn, ConnectionHandle Handle) SetupReadyPool(
        TestProbe controlProbe, HostKey key)
    {
        var pool = CreatePool(controlProbe, key);
        var fakeConn = controlProbe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(5));
        var handle = CreateHandle(fakeConn, key);
        pool.Tell(new ConnectionActor.ConnectionReady(handle), fakeConn);
        return (pool, fakeConn, handle);
    }

    // ── SLC-001: StreamCompleted frees slot, queued requester served ────────

    [Fact(DisplayName = "SLC-001: StreamCompleted frees slot and serves queued requester")]
    public void SLC_001_StreamCompleted_FreesSlot_ServesQueuedRequester()
    {
        var controlProbe = CreateTestProbe("control");
        // HTTP/1.0: MaxConcurrentStreams = 1 → fills with a single MarkBusy.
        var (pool, fakeConn, handle) = SetupReadyPool(controlProbe, Key10);

        // Fill the single slot.
        pool.Tell(new PoolRouterActor.EnsureHost(Key10, TestOptions), TestActor);
        ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));

        // Queue a second requester — slot is full.
        var requesterProbe = CreateTestProbe("requester");
        pool.Tell(new PoolRouterActor.EnsureHost(Key10, TestOptions), requesterProbe.Ref);

        // A new connection is spawned (limiter allows). Capture but don't make ready.
        controlProbe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(5));

        // StreamCompleted frees the slot on the original connection.
        pool.Tell(new HostPoolActor.StreamCompleted(fakeConn));

        // The queued requester should now be served from the original connection.
        var received = requesterProbe.ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));
        Assert.Equal(handle, received);
    }

    // ── SLC-002: StreamCompleted with no eligible connection → queue unchanged ──

    [Fact(DisplayName = "SLC-002: StreamCompleted with no eligible connection leaves queue unchanged")]
    public void SLC_002_StreamCompleted_NoEligibleConnection_QueueUnchanged()
    {
        var controlProbe = CreateTestProbe("control");
        // HTTP/1.0: MaxConcurrentStreams = 1.
        var (pool, fakeConn, _) = SetupReadyPool(controlProbe, Key10);

        // Fill the single slot with TWO MarkBusy calls:
        // 1) via EnsureHost
        pool.Tell(new PoolRouterActor.EnsureHost(Key10, TestOptions), TestActor);
        ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));

        // 2) via StreamAcquired (simulates a second in-flight stream, even though HTTP/1.0 normally has 1)
        pool.Tell(new HostPoolActor.StreamAcquired(fakeConn));

        // Queue a requester.
        var requesterProbe = CreateTestProbe("requester");
        pool.Tell(new PoolRouterActor.EnsureHost(Key10, TestOptions), requesterProbe.Ref);

        // Consume spawned connection (limiter allows another).
        controlProbe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(5));

        // One StreamCompleted: PendingRequests goes from 2→1, but MaxConcurrentStreams=1, so still no slot.
        pool.Tell(new HostPoolActor.StreamCompleted(fakeConn));

        // Requester should NOT be served — still at capacity.
        requesterProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    // ── SLC-003: Multiple queued requesters drained in FIFO order ──────────

    [Fact(DisplayName = "SLC-003: Multiple queued requesters drained in FIFO order")]
    public void SLC_003_MultipleQueuedRequesters_DrainedFIFO()
    {
        var controlProbe = CreateTestProbe("control");
        // HTTP/1.1: MaxConcurrentStreams = 6.
        var (pool, fakeConn, handle) = SetupReadyPool(controlProbe, Key11);

        // Fill all 6 slots.
        for (var i = 0; i < 6; i++)
        {
            pool.Tell(new PoolRouterActor.EnsureHost(Key11, TestOptions), TestActor);
            ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));
        }

        // Queue 3 requesters (no slots available).
        var probes = new TestProbe[3];
        for (var i = 0; i < 3; i++)
        {
            probes[i] = CreateTestProbe($"requester-{i}");
            pool.Tell(new PoolRouterActor.EnsureHost(Key11, TestOptions), probes[i].Ref);
        }

        // Free 3 slots via StreamCompleted.
        for (var i = 0; i < 3; i++)
        {
            pool.Tell(new HostPoolActor.StreamCompleted(fakeConn));
        }

        // All 3 queued requesters should be served in FIFO order.
        for (var i = 0; i < 3; i++)
        {
            var received = probes[i].ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));
            Assert.Equal(handle, received);
        }
    }

    // ── SLC-004: UpdateMaxConcurrentStreams with increased limit serves queued ──

    [Fact(DisplayName = "SLC-004: UpdateMaxConcurrentStreams with increased limit serves queued requesters")]
    public void SLC_004_UpdateMaxConcurrentStreams_IncreasedLimit_ServesQueued()
    {
        var controlProbe = CreateTestProbe("control");
        // HTTP/2: initial MaxConcurrentStreams = 100 (default from ConnectionHandle).
        var (pool, fakeConn, handle) = SetupReadyPool(controlProbe, Key20);

        // First, reduce max to 1 so we can fill it easily.
        pool.Tell(new HostPoolActor.UpdateMaxConcurrentStreams(fakeConn, 1));

        // Fill the single slot.
        pool.Tell(new PoolRouterActor.EnsureHost(Key20, TestOptions), TestActor);
        ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));

        // Queue a requester — slot is full (max=1, pending=1).
        var requesterProbe = CreateTestProbe("requester");
        pool.Tell(new PoolRouterActor.EnsureHost(Key20, TestOptions), requesterProbe.Ref);

        // Consume spawned connection if any.
        controlProbe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(5));

        // Requester should be queued.
        requesterProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));

        // Increase max to 5 — this should serve the queued requester.
        pool.Tell(new HostPoolActor.UpdateMaxConcurrentStreams(fakeConn, 5));

        var received = requesterProbe.ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));
        Assert.Equal(handle, received);
    }

    // ── SLC-005: StreamAcquired marks connection busy ──────────────────────

    [Fact(DisplayName = "SLC-005: StreamAcquired marks connection busy, reducing available slots")]
    public void SLC_005_StreamAcquired_MarksBusy()
    {
        var controlProbe = CreateTestProbe("control");
        // HTTP/1.0: MaxConcurrentStreams = 1.
        var (pool, fakeConn, _) = SetupReadyPool(controlProbe, Key10);

        // StreamAcquired fills the single slot (without going through EnsureHost).
        pool.Tell(new HostPoolActor.StreamAcquired(fakeConn));

        // EnsureHost should NOT get an immediate reply — slot is full.
        pool.Tell(new PoolRouterActor.EnsureHost(Key10, TestOptions), TestActor);
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }
}
