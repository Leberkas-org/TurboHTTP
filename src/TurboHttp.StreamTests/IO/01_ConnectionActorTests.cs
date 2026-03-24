using System.Buffers;
using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using TurboHttp.Internal;
using TurboHttp.Transport;
using TurboHttp.Pooling;

namespace TurboHttp.StreamTests.IO;

/// <summary>
/// Tests the <see cref="Http1ConnectionActor"/> lifecycle: connect, reconnect, exponential backoff, and idle eviction.
/// Verifies that ConnectionReady is sent to the parent on successful connect and backoff intervals are respected.
/// </summary>
/// <remarks>
/// Actor under test: <see cref="Http1ConnectionActor"/>.
/// Validates actor message handling for the full connection lifecycle.
/// </remarks>
public sealed class  ConnectionActorTests : TestKit
{
    private static readonly RequestEndpoint TestKey = new()
    {
        Host = "localhost",
        Port = 8080,
        Scheme = "http",
        Version = HttpVersion.Version11
    };

    private static readonly TcpOptions TestOptions = new() { Host = "localhost", Port = 8080 };

    /// <summary>
    /// Wraps <see cref="Http1ConnectionActor"/> as a child actor so parent-directed messages
    /// (e.g. <see cref="HostPool.ConnectionFailed"/>, <see cref="ConnectionActorBase.ConnectionReady"/>)
    /// can be intercepted via a <see cref="TestProbe"/>.
    /// </summary>
    private sealed class ParentProxy : ReceiveActor
    {
        public ParentProxy(IActorRef parentProbe, IActorRef clientManager, TurboClientOptions config)
        {
            Context.ActorOf(
                Props.Create(() => new Http1ConnectionActor(TestOptions, clientManager, TestKey, config)),
                "conn");

            ReceiveAny(msg => parentProbe.Tell(msg));
        }
    }

    /// <summary>
    /// Creates a <see cref="Http1ConnectionActor"/> under a <see cref="ParentProxy"/> and waits
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

    [Fact(DisplayName = "CA-001: ConnectionFailed sent to parent when ClientDisconnected received")]
    public void Should_SendConnectionFailedToParent_WhenClientDisconnectedReceived()
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

    [Fact(DisplayName = "CA-002: ConnectionFailed sent to parent when watched runner actor terminates")]
    public void Should_SendConnectionFailedToParent_WhenWatchedRunnerTerminates()
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
        parentProbe.ExpectMsg<ConnectionActorBase.ConnectionReady>(TimeSpan.FromSeconds(5));

        // Terminate the runner — ConnectionActor is watching it and will call Reconnect().
        Sys.Stop(runnerProbe.Ref);

        var failed = parentProbe.ExpectMsg<HostPool.ConnectionFailed>(TimeSpan.FromSeconds(5));
        Assert.Equal(connectionActor, failed.Connection);
    }

    [Fact(DisplayName = "CA-003: No reconnect attempts after disconnect — HostPool manages reconnection")]
    public void Should_NotReconnect_AfterDisconnect()
    {
        var config = new TurboClientOptions
        {
            ReconnectInterval = TimeSpan.FromMilliseconds(50),
            MaxReconnectAttempts = 5
        };

        var (_, connectionActor, clientManagerProbe, parentProbe) = CreateActor(config);

        connectionActor.Tell(new ClientRunner.ClientDisconnected(new IPEndPoint(IPAddress.Loopback, 8080)));
        parentProbe.ExpectMsg<HostPool.ConnectionFailed>(TimeSpan.FromSeconds(5));

        // HostPool manages reconnection by spawning a fresh replacement actor.
        // This actor should NOT schedule its own reconnect attempt.
        clientManagerProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    [Fact(DisplayName = "CA-004: No reconnect attempts after runner terminates")]
    public void Should_NotReconnect_WhenRunnerTerminates()
    {
        var config = new TurboClientOptions
        {
            ReconnectInterval = TimeSpan.FromMilliseconds(50),
            MaxReconnectAttempts = 2
        };

        var (_, connectionActor, clientManagerProbe, parentProbe) = CreateActor(config);

        // Simulate successful connect
        var runnerProbe = CreateTestProbe("runner-ca004");
        connectionActor.Tell(MakeConnected(), runnerProbe.Ref);
        parentProbe.ExpectMsg<ConnectionActorBase.ConnectionReady>(TimeSpan.FromSeconds(5));

        // Kill runner → Terminated → Reconnect → ConnectionFailed
        Sys.Stop(runnerProbe.Ref);
        parentProbe.ExpectMsg<HostPool.ConnectionFailed>(TimeSpan.FromSeconds(5));

        // No reconnect from the actor itself.
        clientManagerProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    [Fact(DisplayName = "CA-006: StreamCompleted forwarded to parent")]
    public void Should_ForwardStreamCompletedToParent_WhenStreamCompletedReceived()
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

    [Fact(DisplayName = "CA-007: StreamAcquired forwarded to parent")]
    public void Should_ForwardStreamAcquiredToParent_WhenStreamAcquiredReceived()
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

    [Fact(DisplayName = "CA-008: UpdateMaxConcurrentStreams forwarded to parent")]
    public void Should_ForwardUpdateMaxConcurrentStreamsToParent_WhenUpdateMaxConcurrentStreamsReceived()
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

    [Fact(DisplayName = "CA-009: Both channel writers completed when reconnect is triggered")]
    public void Should_CompleteBothChannelWriters_WhenReconnectTriggered()
    {
        var config = new TurboClientOptions
        {
            ReconnectInterval = TimeSpan.FromSeconds(60), // prevent reconnect from firing during test
            MaxReconnectAttempts = 3
        };

        var parentProbe = CreateTestProbe("parent-ca009");
        var clientManagerProbe = CreateTestProbe("clientManager-ca009");

        Sys.ActorOf(Props.Create(() => new ParentProxy(parentProbe.Ref, clientManagerProbe.Ref, config)));

        // Capture the initial CreateRunnerWithChannels to get channel references
        var createMsg = clientManagerProbe.ExpectMsg<ClientManager.CreateRunnerWithChannels>(TimeSpan.FromSeconds(5));
        var connectionActor = clientManagerProbe.LastSender;

        // Trigger disconnect — this calls Reconnect() inside the actor
        connectionActor.Tell(new ClientRunner.ClientDisconnected(new IPEndPoint(IPAddress.Loopback, 8080)));

        // Wait for ConnectionFailed to propagate through ParentProxy to parentProbe.
        // By the time this returns, Reconnect() has already run (it sends ConnectionFailed before returning).
        parentProbe.ExpectMsg<HostPool.ConnectionFailed>(TimeSpan.FromSeconds(5));

        // Both channel writers must be completed so old pump tasks can exit cleanly.
        // OutboundChannel (_in in ConnectionActor): MoveChannelToStream exits via Completion check.
        Assert.False(createMsg.OutboundChannel.Writer.TryWrite(default),
            "OutboundChannel (_in) writer should be completed after Reconnect()");
        // InboundChannel (_out in ConnectionActor): ConnectionStage InboundReader sees end-of-stream.
        Assert.False(createMsg.InboundChannel.Writer.TryWrite(default),
            "InboundChannel (_out) writer should be completed after Reconnect()");
    }

    [Fact(DisplayName = "CA-005: ConnectionReady sent to parent on initial connect")]
    public void Should_SendConnectionReadyToParent_WhenInitialConnectSucceeds()
    {
        var config = new TurboClientOptions
        {
            ReconnectInterval = TimeSpan.FromMilliseconds(10),
            MaxReconnectAttempts = 3
        };

        var (_, connectionActor, clientManagerProbe, parentProbe) = CreateActor(config);

        // Simulate the initial connection succeeding
        connectionActor.Tell(MakeConnected(), TestActor);

        // Parent should receive ConnectionReady with a fresh ConnectionHandle
        var ready = parentProbe.ExpectMsg<ConnectionActorBase.ConnectionReady>(TimeSpan.FromSeconds(5));
        Assert.NotNull(ready.Handle);
    }
}
