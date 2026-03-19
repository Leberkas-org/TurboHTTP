using System.Buffers;
using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using TurboHttp.Client;
using TurboHttp.Internal;
using TurboHttp.IO;
using TurboHttp.IO.Stages;
using TurboHttp.Lifecycle;

namespace TurboHttp.StreamTests.IO;

/// <summary>
/// Unit tests for <see cref="ConnectionActor"/> reconnect, backoff, and failure notification behaviour.
/// </summary>
public sealed class ConnectionActorTests : TestKit
{
    private static readonly RequestEndpoint TestKey = new()
    {
        Host = "localhost",
        Port = 8080,
        Scheme = "http",
        Version = HttpVersion.Version11
    };

    private static readonly TcpOptions TestOptions = new() { Host = "localhost", Port = 8080 };

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Wraps <see cref="ConnectionActor"/> as a child actor so parent-directed messages
    /// (e.g. <see cref="HostPool.ConnectionFailed"/>, <see cref="ConnectionActor.ConnectionReady"/>)
    /// can be intercepted via a <see cref="TestProbe"/>.
    /// </summary>
    private sealed class ParentProxy : ReceiveActor
    {
        public ParentProxy(IActorRef parentProbe, IActorRef clientManager, TurboClientOptions config)
        {
            Context.ActorOf(
                Props.Create(() => new ConnectionActor(TestOptions, clientManager, TestKey, config)),
                "conn");

            ReceiveAny(msg => parentProbe.Tell(msg));
        }
    }

    /// <summary>
    /// Creates a <see cref="ConnectionActor"/> under a <see cref="ParentProxy"/> and waits
    /// for the initial connect message to arrive at the client-manager probe.
    /// Returns references needed to drive each test.
    /// </summary>
    private (IActorRef proxy, IActorRef connectionActor, TestProbe clientManagerProbe, TestProbe parentProbe)
        CreateActor(TurboClientOptions config)
    {
        var parentProbe = CreateTestProbe("parent");
        var clientManagerProbe = CreateTestProbe("clientManager");

        var proxy = Sys.ActorOf(
            Props.Create(() => new ParentProxy(parentProbe.Ref, clientManagerProbe.Ref, config)));

        // Wait for the initial CreateRunnerWithChannels that PreStart triggers.
        clientManagerProbe.ExpectMsg<ClientManager.CreateRunnerWithChannels>(TimeSpan.FromSeconds(5));
        var connectionActor = clientManagerProbe.LastSender;

        return (proxy, connectionActor, clientManagerProbe, parentProbe);
    }

    /// <summary>Builds a <see cref="ClientRunner.ClientConnected"/> with in-memory channels.</summary>
    private static ClientRunner.ClientConnected MakeConnected()
    {
        var outbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var inbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        return new ClientRunner.ClientConnected(
            new IPEndPoint(IPAddress.Loopback, 8080),
            inbound.Reader,
            outbound.Writer);
    }

    // ── CA-001: ConnectionFailed sent on ClientDisconnected ──────────────────

    [Fact(DisplayName = "CA-001: ConnectionFailed sent to parent when ClientDisconnected received")]
    public void CA_001_ConnectionFailed_SentOnDisconnect()
    {
        var config = new TurboClientOptions
        {
            ReconnectInterval = TimeSpan.FromSeconds(60), // keep reconnect from firing during test
            MaxReconnectAttempts = 3
        };

        var (_, connectionActor, _, parentProbe) = CreateActor(config);

        connectionActor.Tell(new ClientRunner.ClientDisconnected(new IPEndPoint(IPAddress.Loopback, 8080)));

        var failed = parentProbe.ExpectMsg<HostPool.ConnectionFailed>(TimeSpan.FromSeconds(5));
        Assert.Equal(connectionActor, failed.Connection);
    }

    // ── CA-002: ConnectionFailed sent when watched runner terminates ──────────

    [Fact(DisplayName = "CA-002: ConnectionFailed sent to parent when watched runner actor terminates")]
    public void CA_002_ConnectionFailed_SentOnTerminated()
    {
        var config = new TurboClientOptions
        {
            ReconnectInterval = TimeSpan.FromSeconds(60),
            MaxReconnectAttempts = 3
        };

        var (_, connectionActor, _, parentProbe) = CreateActor(config);

        // Simulate successful connect: send ClientConnected from a probe actor so ConnectionActor
        // sets _runner = probe.Ref and watches it.
        var runnerProbe = CreateTestProbe("runner");
        connectionActor.Tell(MakeConnected(), runnerProbe.Ref);
        parentProbe.ExpectMsg<ConnectionActor.ConnectionReady>(TimeSpan.FromSeconds(5));

        // Terminate the runner — ConnectionActor is watching it and will call Reconnect().
        Sys.Stop(runnerProbe.Ref);

        var failed = parentProbe.ExpectMsg<HostPool.ConnectionFailed>(TimeSpan.FromSeconds(5));
        Assert.Equal(connectionActor, failed.Connection);
    }

    // ── CA-003: Exponential backoff — second delay is larger than first ───────

    [Fact(DisplayName = "CA-003: Backoff delay increases exponentially between reconnect attempts")]
    public void CA_003_BackoffDelay_Increases()
    {
        var config = new TurboClientOptions
        {
            ReconnectInterval = TimeSpan.FromMilliseconds(100),
            MaxReconnectAttempts = 5
        };

        var (_, connectionActor, clientManagerProbe, parentProbe) = CreateActor(config);

        // First disconnect — _reconnectAttempt=0, delay = 100ms * 2^0 = 100ms
        var t0 = DateTime.UtcNow;
        connectionActor.Tell(new ClientRunner.ClientDisconnected(new IPEndPoint(IPAddress.Loopback, 8080)));
        parentProbe.ExpectMsg<HostPool.ConnectionFailed>(TimeSpan.FromSeconds(5));
        clientManagerProbe.ExpectMsg<ClientManager.CreateRunnerWithChannels>(TimeSpan.FromSeconds(5));
        var delay1 = DateTime.UtcNow - t0;

        // Second disconnect — _reconnectAttempt=1, delay = 100ms * 2^1 = 200ms
        var t1 = DateTime.UtcNow;
        connectionActor.Tell(new ClientRunner.ClientDisconnected(new IPEndPoint(IPAddress.Loopback, 8080)));
        parentProbe.ExpectMsg<HostPool.ConnectionFailed>(TimeSpan.FromSeconds(5));
        clientManagerProbe.ExpectMsg<ClientManager.CreateRunnerWithChannels>(TimeSpan.FromSeconds(5));
        var delay2 = DateTime.UtcNow - t1;

        // Minimum expected delays: 100ms and 200ms respectively.
        Assert.True(delay1 >= TimeSpan.FromMilliseconds(80),
            $"Expected delay1 >= 80ms, got {delay1.TotalMilliseconds:F1}ms");
        Assert.True(delay2 >= TimeSpan.FromMilliseconds(160),
            $"Expected delay2 >= 160ms, got {delay2.TotalMilliseconds:F1}ms");
        Assert.True(delay2 > delay1,
            $"Expected delay2 ({delay2.TotalMilliseconds:F1}ms) > delay1 ({delay1.TotalMilliseconds:F1}ms)");
    }

    // ── CA-004: Stops reconnecting after MaxReconnectAttempts ─────────────────

    [Fact(DisplayName = "CA-004: Stops reconnecting after MaxReconnectAttempts reached")]
    public void CA_004_StopsAfterMaxAttempts()
    {
        var config = new TurboClientOptions
        {
            ReconnectInterval = TimeSpan.FromMilliseconds(10),
            MaxReconnectAttempts = 2
        };

        var (_, connectionActor, clientManagerProbe, parentProbe) = CreateActor(config);

        // Disconnect 1 — attempt=0, schedules reconnect in 10ms
        connectionActor.Tell(new ClientRunner.ClientDisconnected(new IPEndPoint(IPAddress.Loopback, 8080)));
        parentProbe.ExpectMsg<HostPool.ConnectionFailed>(TimeSpan.FromSeconds(5));
        clientManagerProbe.ExpectMsg<ClientManager.CreateRunnerWithChannels>(TimeSpan.FromSeconds(5));

        // Disconnect 2 — attempt=1, schedules reconnect in 20ms
        connectionActor.Tell(new ClientRunner.ClientDisconnected(new IPEndPoint(IPAddress.Loopback, 8080)));
        parentProbe.ExpectMsg<HostPool.ConnectionFailed>(TimeSpan.FromSeconds(5));
        clientManagerProbe.ExpectMsg<ClientManager.CreateRunnerWithChannels>(TimeSpan.FromSeconds(5));

        // Disconnect 3 — attempt=2 >= MaxReconnectAttempts=2 → gives up, no further reconnect
        connectionActor.Tell(new ClientRunner.ClientDisconnected(new IPEndPoint(IPAddress.Loopback, 8080)));
        parentProbe.ExpectMsg<HostPool.ConnectionFailed>(TimeSpan.FromSeconds(5));
        clientManagerProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    // ── CA-006: StreamCompleted forwarded to parent ────────────────────────────

    [Fact(DisplayName = "CA-006: StreamCompleted forwarded to parent")]
    public void CA_006_StreamCompleted_ForwardedToParent()
    {
        var config = new TurboClientOptions
        {
            ReconnectInterval = TimeSpan.FromSeconds(60),
            MaxReconnectAttempts = 3
        };

        var (_, connectionActor, _, parentProbe) = CreateActor(config);

        connectionActor.Tell(new HostPool.StreamCompleted(connectionActor));

        var msg = parentProbe.ExpectMsg<HostPool.StreamCompleted>(TimeSpan.FromSeconds(5));
        Assert.Equal(connectionActor, msg.Connection);
    }

    // ── CA-007: StreamAcquired forwarded to parent ───────────────────────────

    [Fact(DisplayName = "CA-007: StreamAcquired forwarded to parent")]
    public void CA_007_StreamAcquired_ForwardedToParent()
    {
        var config = new TurboClientOptions
        {
            ReconnectInterval = TimeSpan.FromSeconds(60),
            MaxReconnectAttempts = 3
        };

        var (_, connectionActor, _, parentProbe) = CreateActor(config);

        connectionActor.Tell(new HostPool.StreamAcquired(connectionActor));

        var msg = parentProbe.ExpectMsg<HostPool.StreamAcquired>(TimeSpan.FromSeconds(5));
        Assert.Equal(connectionActor, msg.Connection);
    }

    // ── CA-008: UpdateMaxConcurrentStreams forwarded to parent ────────────────

    [Fact(DisplayName = "CA-008: UpdateMaxConcurrentStreams forwarded to parent")]
    public void CA_008_UpdateMaxConcurrentStreams_ForwardedToParent()
    {
        var config = new TurboClientOptions
        {
            ReconnectInterval = TimeSpan.FromSeconds(60),
            MaxReconnectAttempts = 3
        };

        var (_, connectionActor, _, parentProbe) = CreateActor(config);

        connectionActor.Tell(new HostPool.UpdateMaxConcurrentStreams(connectionActor, 128));

        var msg = parentProbe.ExpectMsg<HostPool.UpdateMaxConcurrentStreams>(TimeSpan.FromSeconds(5));
        Assert.Equal(connectionActor, msg.Connection);
        Assert.Equal(128, msg.MaxStreams);
    }

    // ── CA-005: ConnectionReady sent after reconnect succeeds ─────────────────

    [Fact(DisplayName = "CA-005: ConnectionReady sent to parent after successful reconnect")]
    public void CA_005_ConnectionReady_SentAfterReconnect()
    {
        var config = new TurboClientOptions
        {
            ReconnectInterval = TimeSpan.FromMilliseconds(10),
            MaxReconnectAttempts = 3
        };

        var (_, connectionActor, clientManagerProbe, parentProbe) = CreateActor(config);

        // Disconnect triggers a reconnect attempt
        connectionActor.Tell(new ClientRunner.ClientDisconnected(new IPEndPoint(IPAddress.Loopback, 8080)));
        parentProbe.ExpectMsg<HostPool.ConnectionFailed>(TimeSpan.FromSeconds(5));

        // Wait for the reconnect attempt (CreateRunnerWithChannels sent to clientManager again)
        clientManagerProbe.ExpectMsg<ClientManager.CreateRunnerWithChannels>(TimeSpan.FromSeconds(5));
        var connectionActorRef = clientManagerProbe.LastSender;

        // Simulate the reconnect succeeding
        connectionActorRef.Tell(MakeConnected(), TestActor);

        // Parent should receive ConnectionReady with a fresh ConnectionHandle
        var ready = parentProbe.ExpectMsg<ConnectionActor.ConnectionReady>(TimeSpan.FromSeconds(5));
        Assert.NotNull(ready.Handle);
    }
}
