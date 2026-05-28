using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11ServerPipeliningSpec
{
    private static IFeatureCollection CreateResponseContext()
    {
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature { StatusCode = 200 });
        var bodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        return features;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_should_decode_two_pipelined_requests_from_single_buffer()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);
        var request = string.Concat(
            "GET / HTTP/1.1\r\n",
            "Host: example.com\r\n",
            "Content-Length: 0\r\n",
            "\r\n",
            "GET /page2 HTTP/1.1\r\n",
            "Host: example.com\r\n",
            "Content-Length: 0\r\n",
            "\r\n");
        var buffer = MakeBuffer(request);

        sm.DecodeClientData(new TransportData(buffer));

        Assert.Equal(2, ops.Requests.Count);
        Assert.Equal("/", ops.Requests[0].Get<IHttpRequestFeature>()?.Path);
        Assert.Equal("/page2", ops.Requests[1].Get<IHttpRequestFeature>()?.Path);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_should_process_responses_fifo_for_pipelined_requests()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);
        var request = string.Concat(
            "GET / HTTP/1.1\r\n",
            "Host: example.com\r\n",
            "Content-Length: 0\r\n",
            "\r\n",
            "GET /page2 HTTP/1.1\r\n",
            "Host: example.com\r\n",
            "Content-Length: 0\r\n",
            "\r\n");
        var buffer = MakeBuffer(request);

        sm.DecodeClientData(new TransportData(buffer));

        var context1 = CreateResponseContext();
        sm.OnResponse(context1);

        var context2 = CreateResponseContext();
        sm.OnResponse(context2);

        Assert.Equal(2, ops.Outbound.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_should_throw_when_responding_without_pending_request()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        var context = CreateResponseContext();

        Assert.Throws<InvalidOperationException>(() => sm.OnResponse(context));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_should_handle_three_pipelined_requests()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);
        var request = string.Concat(
            "GET /page1 HTTP/1.1\r\n",
            "Host: example.com\r\n",
            "Content-Length: 0\r\n",
            "\r\n",
            "GET /page2 HTTP/1.1\r\n",
            "Host: example.com\r\n",
            "Content-Length: 0\r\n",
            "\r\n",
            "GET /page3 HTTP/1.1\r\n",
            "Host: example.com\r\n",
            "Content-Length: 0\r\n",
            "\r\n");
        var buffer = MakeBuffer(request);

        sm.DecodeClientData(new TransportData(buffer));

        Assert.Equal(3, ops.Requests.Count);
        Assert.Equal("/page1", ops.Requests[0].Get<IHttpRequestFeature>()?.Path);
        Assert.Equal("/page2", ops.Requests[1].Get<IHttpRequestFeature>()?.Path);
        Assert.Equal("/page3", ops.Requests[2].Get<IHttpRequestFeature>()?.Path);
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

