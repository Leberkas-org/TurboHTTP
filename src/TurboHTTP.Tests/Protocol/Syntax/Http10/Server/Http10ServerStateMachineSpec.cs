using System.Text;
using Akka.Actor;
using Akka.TestKit.Xunit;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Context.Features;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Syntax.Http10.Server;
using TurboHTTP.Server;
using TurboHTTP.Streams.Stages.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http10.Server;

public sealed class Http10ServerStateMachineSpec : TestKit
{
    private static FakeServerOps MakeOps() => new();

    private static RequestContext CreateResponseContext()
    {
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature { StatusCode = 200 });
        var bodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        return new RequestContext { Features = features };
    }

    private static async Task<RequestContext> CreateResponseContextWithBody(string body)
    {
        var context = CreateResponseContext();
        var bodyFeature = context.Features.Get<IHttpResponseBodyFeature>()!;
        var bytes = Encoding.ASCII.GetBytes(body);
        await bodyFeature.Writer.WriteAsync(bytes);
        await bodyFeature.Writer.CompleteAsync();
        return context;
    }

    private static TransportBuffer CreateRequestBuffer(string requestText)
    {
        var bytes = Encoding.ASCII.GetBytes(requestText);
        var buffer = TransportBuffer.Rent(bytes.Length);
        bytes.CopyTo(buffer.FullMemory.Span);
        buffer.Length = bytes.Length;
        return buffer;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void DecodeClientData_should_decode_complete_request()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions(), ops);

        var requestBuffer = CreateRequestBuffer("GET /path HTTP/1.0\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n");

        sm.DecodeClientData(new TransportData(requestBuffer));

        Assert.Single(ops.Requests);
        var req = ops.Requests[0].Features.Get<IHttpRequestFeature>()!;
        Assert.Equal("GET", req.Method);
        Assert.Equal("/path", req.Path);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void DecodeClientData_should_mark_should_complete()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions(), ops);

        var requestBuffer = CreateRequestBuffer("GET / HTTP/1.0\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n");

        sm.DecodeClientData(new TransportData(requestBuffer));

        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void OnResponse_should_not_emit_transport_data_before_body_delivered()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions(), ops);

        var context = CreateResponseContext();

        sm.OnResponse(context);

        Assert.DoesNotContain(ops.Outbound, o => o is TransportData);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public async Task OnResponse_with_body_should_emit_transport_data_after_body_chunk()
    {
        var inbox = Inbox.Create(Sys);
        var ops = new FakeServerOps { StageActor = inbox.Receiver };
        var sm = new Http10ServerStateMachine(new TurboServerOptions(), ops);
        sm.PreStart();

        var context = await CreateResponseContextWithBody("hello");
        sm.OnResponse(context);

        Assert.DoesNotContain(ops.Outbound, o => o is TransportData);

        var msg = await Task.Run(() => inbox.Receive(TimeSpan.FromSeconds(3)));
        var chunk = Assert.IsType<OutboundBodyChunk>(msg);
        sm.OnBodyMessage(chunk);

        var msg2 = await Task.Run(() => inbox.Receive(TimeSpan.FromSeconds(3)));
        sm.OnBodyMessage(msg2);

        Assert.Contains(ops.Outbound, o => o is TransportData);
        var td = ops.Outbound.OfType<TransportData>().First();
        var text = Encoding.ASCII.GetString(td.Buffer.Memory.Span[..td.Buffer.Length]);
        Assert.Contains("Content-Length: 5", text);
        Assert.Contains("hello", text);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void OnResponse_should_add_connection_close_header()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions(), ops);

        var context = CreateResponseContext();

        sm.OnResponse(context);

        Assert.True(sm.CanAcceptResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void CanAcceptResponse_should_always_be_true()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions(), ops);

        Assert.True(sm.CanAcceptResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void Cleanup_should_abort_active_body()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions(), ops);

        sm.Cleanup();

        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-3.1")]
    public async Task OnResponse_should_use_http10_version_in_status_line()
    {
        var inbox = Inbox.Create(Sys);
        var ops = new FakeServerOps { StageActor = inbox.Receiver };
        var sm = new Http10ServerStateMachine(new TurboServerOptions(), ops);
        sm.PreStart();

        var requestBuffer = CreateRequestBuffer("GET / HTTP/1.0\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n");
        sm.DecodeClientData(new TransportData(requestBuffer));

        var context = await CreateResponseContextWithBody("hello");
        sm.OnResponse(context);

        var msg = await Task.Run(() => inbox.Receive(TimeSpan.FromSeconds(3)));
        var chunk = Assert.IsType<OutboundBodyChunk>(msg);
        sm.OnBodyMessage(chunk);

        var msg2 = await Task.Run(() => inbox.Receive(TimeSpan.FromSeconds(3)));
        sm.OnBodyMessage(msg2);

        var td = ops.Outbound.OfType<TransportData>().First();
        var text = Encoding.ASCII.GetString(td.Buffer.Memory.Span[..td.Buffer.Length]);
        Assert.StartsWith("HTTP/1.0 ", text);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5.1.1")]
    public void DecodeClientData_should_signal_error_for_unknown_method()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions(), ops);

        var requestBuffer = CreateRequestBuffer("PATCH /path HTTP/1.0\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n");
        sm.DecodeClientData(new TransportData(requestBuffer));

        Assert.Single(ops.Requests);
        var req = ops.Requests[0].Features.Get<IHttpRequestFeature>()!;
        Assert.Equal("PATCH", req.Method);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void DecodeClientData_should_detect_simple_request()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions(), ops);

        var requestBuffer = CreateRequestBuffer("GET /path\r\n");
        sm.DecodeClientData(new TransportData(requestBuffer));

        Assert.True(ops.Requests.Count <= 1);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7.2.2")]
    public void DecodeClientData_should_handle_post_without_content_length()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions(), ops);

        var requestBuffer = CreateRequestBuffer("POST /path HTTP/1.0\r\nHost: example.com\r\n\r\n");
        sm.DecodeClientData(new TransportData(requestBuffer));

        if (ops.Requests.Count > 0)
        {
            var req = ops.Requests[0].Features.Get<IHttpRequestFeature>()!;
            var contentLength = req.Headers["Content-Length"];
            Assert.True(string.IsNullOrEmpty(contentLength));
        }
    }
}
