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

public sealed class Http10ServerStateMachineErrorSpec : TestKit
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
    public void DecodeClientData_should_set_ShouldComplete_on_decode_error()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions(), ops);

        var requestBuffer = CreateRequestBuffer("POST / HTTP/1.0\r\nContent-Length: abc\r\n\r\n");

        sm.DecodeClientData(new TransportData(requestBuffer));

        Assert.True(sm.ShouldComplete);
        Assert.Empty(ops.Requests);
    }

    [Fact(Timeout = 5000)]
    public void DecodeClientData_should_not_crash_after_prior_decode_error()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions(), ops);

        var invalidBuffer = CreateRequestBuffer("POST / HTTP/1.0\r\nContent-Length: abc\r\n\r\n");
        sm.DecodeClientData(new TransportData(invalidBuffer));

        var validBuffer = CreateRequestBuffer("GET / HTTP/1.0\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n");
        var ex = Record.Exception(() => sm.DecodeClientData(new TransportData(validBuffer)));

        Assert.Null(ex);
    }

    [Fact(Timeout = 5000)]
    public void Cleanup_should_be_idempotent()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions(), ops);

        var ex1 = Record.Exception(() => sm.Cleanup());
        var ex2 = Record.Exception(() => sm.Cleanup());

        Assert.Null(ex1);
        Assert.Null(ex2);
    }

    [Fact(Timeout = 5000)]
    public async Task Cleanup_should_dispose_deferred_body_owner()
    {
        var inbox = Inbox.Create(Sys);
        var ops = new FakeServerOps { StageActor = inbox.Receiver };
        var sm = new Http10ServerStateMachine(new TurboServerOptions(), ops);
        sm.PreStart();

        var context = await CreateResponseContextWithBody("hello");
        sm.OnResponse(context);

        var msg = await Task.Run(() => inbox.Receive(TimeSpan.FromSeconds(3)));
        var chunk = Assert.IsType<OutboundBodyChunk>(msg);
        sm.OnBodyMessage(chunk);

        var ex = Record.Exception(() => sm.Cleanup());

        Assert.Null(ex);
    }

    [Fact(Timeout = 5000)]
    public void OnBodyMessage_should_ignore_unknown_message_type()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions(), ops);

        var ex = Record.Exception(() => sm.OnBodyMessage("unknown message"));

        Assert.Null(ex);
    }

    [Fact(Timeout = 5000)]
    public void OnBodyMessage_OutboundBodyFailed_should_not_crash_without_prior_response()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions(), ops);

        var failedMsg = new OutboundBodyFailed(new Exception("Body read failed"));
        var ex = Record.Exception(() => sm.OnBodyMessage(failedMsg));

        Assert.Null(ex);
    }
}