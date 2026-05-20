using System.Net;
using System.Text;
using Akka.Actor;
using Akka.Event;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Server;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11ServerConnectionPersistenceSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void ServerStateMachine_should_default_to_persistent_connection_for_http11()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);
        var buffer = MakeBuffer("GET / HTTP/1.1\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n");

        sm.DecodeClientData(new TransportData(buffer));

        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void ServerStateMachine_should_close_connection_after_http10_request()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);
        var buffer = MakeBuffer("GET / HTTP/1.0\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n");

        sm.DecodeClientData(new TransportData(buffer));

        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void ServerStateMachine_should_close_connection_when_connection_close_header()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);
        var buffer =
            MakeBuffer("GET / HTTP/1.1\r\nHost: example.com\r\nConnection: close\r\nContent-Length: 0\r\n\r\n");

        sm.DecodeClientData(new TransportData(buffer));

        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void ServerStateMachine_should_track_pending_requests_via_can_accept_response()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);
        var buffer = MakeBuffer("GET / HTTP/1.1\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n");

        sm.DecodeClientData(new TransportData(buffer));

        Assert.True(sm.CanAcceptResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.6")]
    public void ServerStateMachine_should_inject_connection_close_when_flagged()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);
        var buffer = MakeBuffer("GET / HTTP/1.0\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n");

        sm.DecodeClientData(new TransportData(buffer));

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("")
        };
        sm.OnResponse(response);

        Assert.Single(ops.EmittedOutbound);
        var outbound = ops.EmittedOutbound[0];
        Assert.IsType<TransportData>(outbound);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void ServerStateMachine_should_clear_pending_requests_on_cleanup()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);
        var buffer = MakeBuffer("GET / HTTP/1.1\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n");

        sm.DecodeClientData(new TransportData(buffer));
        Assert.True(sm.CanAcceptResponse);

        sm.Cleanup();

        Assert.False(sm.CanAcceptResponse);
    }

    private sealed class FakeServerOps : IServerStageOperations
    {
        public List<ITransportOutbound> EmittedOutbound { get; } = [];
        public ILoggingAdapter Log { get; } = NoLogger.Instance;
        public IActorRef StageActor { get; set; } = ActorRefs.Nobody;

        public void OnRequest(HttpRequestMessage request)
        {
        }

        public void OnOutbound(ITransportOutbound item) => EmittedOutbound.Add(item);

        public void OnScheduleTimer(string name, TimeSpan delay)
        {
        }

        public void OnCancelTimer(string name)
        {
        }
    }

    private static TransportBuffer MakeBuffer(string raw)
    {
        var data = Encoding.ASCII.GetBytes(raw);
        var buffer = TransportBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        return buffer;
    }
}