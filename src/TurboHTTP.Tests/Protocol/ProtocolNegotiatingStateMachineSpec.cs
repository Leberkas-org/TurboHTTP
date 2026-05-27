using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol;

public sealed class ProtocolNegotiatingStateMachineSpec
{

    private static TransportConnected MakeConnected(SslApplicationProtocol? alpn = null)
    {
        var security = alpn is not null
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
        Assert.True(ops.ScheduledTimers.Any(t => t.Name == "keep-alive-timeout"),
            "keep-alive-timeout should be scheduled");
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

        Assert.True(ops.ScheduledTimers.Any(t => t.Name == "keep-alive-timeout"),
            "keep-alive-timeout should be scheduled");
    }

    [Fact(Timeout = 5000)]
    public void DecodeClientData_should_select_http11_for_get_request()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new TurboServerOptions(), ops);

        sm.DecodeClientData(MakeConnected());

        var request = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        sm.DecodeClientData(MakeData(request));

        Assert.Single(ops.Requests);
        var ctx = ops.Requests[0];
        var feature = ctx.Get<IHttpRequestFeature>();
        Assert.NotNull(feature);
        Assert.Equal("GET", feature.Method);
    }

    [Fact(Timeout = 5000)]
    public void DecodeClientData_should_select_http11_for_post_request()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new TurboServerOptions(), ops);

        sm.DecodeClientData(MakeConnected());

        var request = "POST / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        sm.DecodeClientData(MakeData(request));

        Assert.Single(ops.Requests);
        var ctx = ops.Requests[0];
        var feature = ctx.Get<IHttpRequestFeature>();
        Assert.NotNull(feature);
        Assert.Equal("POST", feature.Method);
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
        Assert.Empty(ops.Requests);
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

