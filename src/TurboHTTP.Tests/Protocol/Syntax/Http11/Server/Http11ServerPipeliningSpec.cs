using System.Text;
using Akka.Actor;
using Akka.Event;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Context.Features;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Server;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11ServerPipeliningSpec
{
    private static TurboHttpContext CreateResponseContext()
    {
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature { StatusCode = 200 });
        var bodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<ITurboResponseBodyFeature>(bodyFeature);
        return new TurboHttpContext(features);
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

        Assert.Equal(2, ops.EmittedRequests.Count);
        Assert.Equal("/", ops.EmittedRequests[0].RequestUri?.OriginalString);
        Assert.Equal("/page2", ops.EmittedRequests[1].RequestUri?.OriginalString);
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

        Assert.Equal(2, ops.EmittedOutbound.Count);
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

        Assert.Equal(3, ops.EmittedRequests.Count);
        Assert.Equal("/page1", ops.EmittedRequests[0].RequestUri?.OriginalString);
        Assert.Equal("/page2", ops.EmittedRequests[1].RequestUri?.OriginalString);
        Assert.Equal("/page3", ops.EmittedRequests[2].RequestUri?.OriginalString);
    }

    private sealed class FakeServerOps : IServerStageOperations
    {
        public List<HttpRequestMessage> EmittedRequests { get; } = [];
        public List<ITransportOutbound> EmittedOutbound { get; } = [];
        public ILoggingAdapter Log { get; } = NoLogger.Instance;
        public IActorRef StageActor { get; set; } = ActorRefs.Nobody;

        public void OnRequest(TurboHttpContext context) { /* OnRequest called */ }
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
