using System.Net;
using System.Text;
using Akka.Actor;
using Akka.Event;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11ServerPipeliningSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_should_decode_two_pipelined_requests_from_single_buffer()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(ops);
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
        var sm = new Http11ServerStateMachine(ops);
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

        var response1 = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Response 1")
        };
        sm.OnResponse(response1);

        var response2 = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Response 2")
        };
        sm.OnResponse(response2);

        Assert.Equal(2, ops.EmittedOutbound.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_should_throw_when_responding_without_pending_request()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(ops);

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("")
        };

        Assert.Throws<InvalidOperationException>(() => sm.OnResponse(response));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_should_handle_three_pipelined_requests()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(ops);
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

        public void OnRequest(HttpRequestMessage request) => EmittedRequests.Add(request);
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