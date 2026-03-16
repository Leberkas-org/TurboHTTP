using System;
using System.Buffers;
using System.Net;
using System.Net.Http;
using System.Threading.Channels;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using TurboHttp.Client;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.StreamTests.IO;

/// <summary>
/// Unit tests for <see cref="HostPoolActor"/> connection lifecycle and stale-state cleanup.
/// </summary>
public sealed class HostPoolActorTests : TestKit
{
    private static readonly HostKey TestKey = new()
    {
        Host = "localhost",
        Port = 8080,
        Scheme = "http",
        Version = HttpVersion.Version11
    };

    private static readonly TcpOptions TestOptions = new() { Host = "localhost", Port = 8080 };

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal fake connection actor: reports <see cref="IActorRef"/> Self to a control
    /// probe on startup, then stays idle (no reconnect logic).
    /// </summary>
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

    /// <summary>Creates a <see cref="HostPoolActor"/> whose children are fakes controlled via
    /// <paramref name="controlProbe"/>. Each new child sends its ref to the probe.</summary>
    private IActorRef CreatePool(TestProbe controlProbe, TimeSpan reconnectInterval)
    {
        var config = new TurboClientOptions
        {
            ReconnectInterval = reconnectInterval,
            IdleTimeout = TimeSpan.FromSeconds(60),
            MaxReconnectAttempts = 3
        };

        var hostConfig = new HostPoolActor.HostPoolConfig(
            TestOptions,
            config,
            TestKey,
            ConnectionFactory: () => Props.Create(() => new FakeConnectionActor(controlProbe.Ref)));

        return Sys.ActorOf(Props.Create(() => new HostPoolActor(hostConfig)));
    }

    /// <summary>Builds a <see cref="ConnectionHandle"/> backed by in-memory channels.</summary>
    private static ConnectionHandle CreateHandle(IActorRef connectionActor)
    {
        var outbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var inbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        return new ConnectionHandle(outbound.Writer, inbound.Reader, TestKey, connectionActor);
    }

    // ── HPA-001: ConnectionFailed clears active handle ────────────────────────

    [Fact(DisplayName = "HPA-001: ConnectionFailed clears active handle; next EnsureHost is queued")]
    public void HPA_001_ConnectionFailed_ClearsActiveHandleAndQueuesNextRequester()
    {
        var controlProbe = CreateTestProbe("control");

        // Long reconnect interval so the scheduled Reconnect does not fire during the test.
        var pool = CreatePool(controlProbe, reconnectInterval: TimeSpan.FromSeconds(60));

        // PreStart spawns one FakeConnectionActor; capture its ref.
        var fakeConn = controlProbe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(5));

        // Simulate the fake actor sending ConnectionReady with a dummy handle.
        var handle = CreateHandle(fakeConn);
        pool.Tell(new ConnectionActor.ConnectionReady(handle), fakeConn);

        // EnsureHost should be served immediately with the active handle.
        pool.Tell(new PoolRouterActor.EnsureHost(TestKey, TestOptions), TestActor);
        var received = ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));
        Assert.Equal(handle, received);

        // Simulate the TCP connection dropping.
        pool.Tell(new HostPoolActor.ConnectionFailed(fakeConn));

        // Active handle must be cleared. A subsequent EnsureHost must NOT get
        // an immediate reply — the requester is queued until a new handle arrives.
        pool.Tell(new PoolRouterActor.EnsureHost(TestKey, TestOptions), TestActor);
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    // ── HPA-002: Queued requester served when new ConnectionReady arrives ─────

    [Fact(DisplayName = "HPA-002: Queued EnsureHost requester is served when reconnected handle arrives")]
    public void HPA_002_QueuedRequester_ServedAfterReconnect()
    {
        var controlProbe = CreateTestProbe("control");
        var pool = CreatePool(controlProbe, reconnectInterval: TimeSpan.FromSeconds(60));

        // Capture the first fake actor spawned at startup.
        var fakeConn = controlProbe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(5));

        // Establish initial handle.
        var handle1 = CreateHandle(fakeConn);
        pool.Tell(new ConnectionActor.ConnectionReady(handle1), fakeConn);

        pool.Tell(new PoolRouterActor.EnsureHost(TestKey, TestOptions), TestActor);
        ExpectMsg<ConnectionHandle>(TimeSpan.FromSeconds(5));

        // Drop the connection — clears _activeHandle.
        pool.Tell(new HostPoolActor.ConnectionFailed(fakeConn));

        // Queue a requester while no handle is available.
        pool.Tell(new PoolRouterActor.EnsureHost(TestKey, TestOptions), TestActor);
        ExpectNoMsg(TimeSpan.FromMilliseconds(200));

        // Simulate a fresh connection coming up and reporting ConnectionReady.
        var handle2 = CreateHandle(fakeConn);
        pool.Tell(new ConnectionActor.ConnectionReady(handle2), fakeConn);

        // The queued requester should now receive the new handle.
        ExpectMsg<ConnectionHandle>(h => h == handle2, TimeSpan.FromSeconds(5));
    }
}
