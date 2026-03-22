using System.Buffers;
using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using TurboHttp.Client;
using TurboHttp.Internal;
using TurboHttp.Transport;
using TurboHttp.Pooling;

namespace TurboHttp.StreamTests.IO;

/// <summary>
/// Tests QUIC-specific behavior: <see cref="Http3ConnectionActor.OpenNewStream"/>
/// message handling and multi-stream lifecycle management.
/// </summary>
public sealed class ConnectionActorQuicTests : TestKit
{
    private static readonly RequestEndpoint TestKey11 = new()
    {
        Host = "localhost",
        Port = 8080,
        Scheme = "http",
        Version = HttpVersion.Version11
    };

    private static readonly TcpOptions TcpTestOptions = new() { Host = "localhost", Port = 8080 };

    private sealed class ParentProxy : ReceiveActor
    {
        public ParentProxy(IActorRef parentProbe, IActorRef clientManager, TcpOptions options, RequestEndpoint key, TurboClientOptions config)
        {
            Context.ActorOf(
                Props.Create(() => new Http1ConnectionActor(options, clientManager, key, config)),
                "conn");

            ReceiveAny(msg => parentProbe.Tell(msg));
        }
    }

    private (IActorRef proxy, IActorRef connectionActor, TestProbe clientManagerProbe, TestProbe parentProbe)
        CreateTcpActor(TurboClientOptions config)
    {
        var parentProbe = CreateTestProbe("parent");
        var clientManagerProbe = CreateTestProbe("clientManager");

        var proxy = Sys.ActorOf(
            Props.Create(() => new ParentProxy(parentProbe.Ref, clientManagerProbe.Ref, TcpTestOptions, TestKey11, config)));

        clientManagerProbe.ExpectMsg<ClientManager.CreateRunnerWithChannels>(TimeSpan.FromSeconds(5));
        var connectionActor = clientManagerProbe.LastSender;

        return (proxy, connectionActor, clientManagerProbe, parentProbe);
    }

    private static ClientRunner.ClientConnected MakeConnected()
    {
        var outbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var inbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        return new ClientRunner.ClientConnected(
            new IPEndPoint(IPAddress.Loopback, 8080),
            inbound.Reader,
            outbound.Writer);
    }

    [Fact(DisplayName = "CQ-001: OpenNewStream ignored for TCP/TLS connection")]
    public void Should_IgnoreOpenNewStream_WhenTcpConnection()
    {
        var config = new TurboClientOptions
        {
            ReconnectInterval = TimeSpan.FromSeconds(60),
            MaxReconnectAttempts = 3
        };

        var (_, connectionActor, _, parentProbe) = CreateTcpActor(config);

        // Simulate successful connect
        connectionActor.Tell(MakeConnected(), CreateTestProbe().Ref);
        parentProbe.ExpectMsg<ConnectionActorBase.ConnectionReady>(TimeSpan.FromSeconds(5));

        // Send OpenNewStream to a TCP connection — should be silently ignored
        var requester = CreateTestProbe("requester");
        connectionActor.Tell(new Http3ConnectionActor.OpenNewStream(requester.Ref));

        // Requester should NOT receive anything (no handle, no error)
        requester.ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // No crash — actor should still be alive and forward messages
        connectionActor.Tell(new HostPool.StreamCompleted(connectionActor));
        parentProbe.ExpectMsg<HostPool.StreamCompleted>(TimeSpan.FromSeconds(5));
    }

    [Fact(DisplayName = "CQ-002: OpenNewStream message record has correct properties")]
    public void Should_HaveCorrectProperties_WhenOpenNewStreamCreated()
    {
        var requester = CreateTestProbe("requester");
        var msg = new Http3ConnectionActor.OpenNewStream(requester.Ref);

        Assert.Equal(requester.Ref, msg.Requester);
    }

    [Fact(DisplayName = "CQ-003: ConnectionReady message preserves handle reference")]
    public void Should_PreserveHandleReference_WhenConnectionReadyCreated()
    {
        var outbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var inbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var handle = new ConnectionHandle(outbound.Writer, inbound.Reader, TestKey11, TestActor);

        var msg = new ConnectionActorBase.ConnectionReady(handle);

        Assert.Same(handle, msg.Handle);
    }
}
