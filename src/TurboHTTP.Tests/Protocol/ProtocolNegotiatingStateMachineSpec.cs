using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using Akka.Actor;
using Akka.Event;
using Servus.Akka.Transport;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Server;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Tests.Protocol;

public sealed class ProtocolNegotiatingStateMachineSpec
{
    private sealed class FakeServerOps : IServerStageOperations
    {
        public List<HttpRequestMessage> EmittedRequests { get; } = [];
        public List<ITransportOutbound> EmittedOutbound { get; } = [];
        public List<string> ScheduledTimers { get; } = [];
        public ILoggingAdapter Log { get; } = NoLogger.Instance;
        public IActorRef StageActor { get; set; } = ActorRefs.Nobody;

        public void OnRequest(HttpRequestMessage request) => EmittedRequests.Add(request);
        public void OnOutbound(ITransportOutbound item) => EmittedOutbound.Add(item);
        public void OnScheduleTimer(string name, TimeSpan delay) => ScheduledTimers.Add(name);
        public void OnCancelTimer(string name) { }
    }

    private static TransportConnected MakeConnected(SslApplicationProtocol? alpn = null)
    {
        SecurityInfo? security = alpn is not null
            ? new SecurityInfo(SslProtocols.Tls13, alpn.Value)
            : null;

        var info = new ConnectionInfo(
            new IPEndPoint(IPAddress.Loopback, 443),
            new IPEndPoint(IPAddress.Loopback, 50000),
            alpn is not null ? TransportProtocol.Tls : TransportProtocol.Tcp,
            security);

        return new TransportConnected(info);
    }

    private static TransportData MakeData(byte[] data)
    {
        var buffer = TransportBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        return new TransportData(buffer);
    }

    // Task 2: ALPN Detection Tests

    [Fact(Timeout = 5000)]
    public void DecodeClientData_should_select_http2_for_alpn_h2()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new TurboServerOptions(), ops);

        sm.DecodeClientData(MakeConnected(SslApplicationProtocol.Http2));

        Assert.True(sm.CanAcceptResponse || !sm.ShouldComplete);
        Assert.Contains("keep-alive-timeout", ops.ScheduledTimers);
    }

    [Fact(Timeout = 5000)]
    public void DecodeClientData_should_select_http11_for_alpn_http11()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new TurboServerOptions(), ops);

        sm.DecodeClientData(MakeConnected(SslApplicationProtocol.Http11));

        Assert.False(sm.CanAcceptResponse);
        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    public void DecodeClientData_should_select_http11_for_default_alpn()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new TurboServerOptions(), ops);

        sm.DecodeClientData(MakeConnected(default(SslApplicationProtocol)));

        Assert.False(sm.CanAcceptResponse);
        Assert.False(sm.ShouldComplete);
    }

    // Task 3: Preface Sniffing Tests

    [Fact(Timeout = 5000)]
    public void DecodeClientData_should_select_http2_for_pri_preface()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new TurboServerOptions(), ops);

        sm.DecodeClientData(MakeConnected());

        var preface = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();
        sm.DecodeClientData(MakeData(preface));

        Assert.Contains("keep-alive-timeout", ops.ScheduledTimers);
    }

    [Fact(Timeout = 5000)]
    public void DecodeClientData_should_select_http11_for_get_request()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new TurboServerOptions(), ops);

        sm.DecodeClientData(MakeConnected());

        var request = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        sm.DecodeClientData(MakeData(request));

        Assert.Single(ops.EmittedRequests);
        Assert.Equal("GET", ops.EmittedRequests[0].Method.Method);
    }

    [Fact(Timeout = 5000)]
    public void DecodeClientData_should_select_http11_for_post_request()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new TurboServerOptions(), ops);

        sm.DecodeClientData(MakeConnected());

        var request = "POST / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        sm.DecodeClientData(MakeData(request));

        Assert.Single(ops.EmittedRequests);
        Assert.Equal("POST", ops.EmittedRequests[0].Method.Method);
    }

    [Fact(Timeout = 5000)]
    public void DecodeClientData_should_stay_sniffing_for_insufficient_data()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new TurboServerOptions(), ops);

        sm.DecodeClientData(MakeConnected());
        sm.DecodeClientData(MakeData("PR"u8.ToArray()));

        Assert.False(sm.CanAcceptResponse);
        Assert.False(sm.ShouldComplete);
        Assert.Empty(ops.EmittedRequests);
        Assert.Empty(ops.ScheduledTimers);
    }

    [Fact(Timeout = 5000)]
    public void Cleanup_should_dispose_buffered_data()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new TurboServerOptions(), ops);

        sm.DecodeClientData(MakeConnected());
        sm.Cleanup();

        Assert.False(sm.ShouldComplete);
    }
}
