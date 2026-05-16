using System.Text;
using Akka.Actor;
using Akka.Event;
using Servus.Akka.Transport;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Streams;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11;

public sealed class ServerStateMachineSpec
{
    private sealed class FakeServerOps : IServerStageOperations
    {
        public List<HttpRequestMessage> EmittedRequests { get; } = [];
        public List<ITransportOutbound> EmittedOutbound { get; } = [];
        public ILoggingAdapter Log { get; } = NoLogger.Instance;
        public IActorRef StageActor { get; set; } = ActorRefs.Nobody;

        public void OnRequest(HttpRequestMessage request)
        {
            EmittedRequests.Add(request);
        }

        public void OnOutbound(ITransportOutbound item)
        {
            EmittedOutbound.Add(item);
        }

        public void OnScheduleTimer(string name, TimeSpan delay)
        {
        }

        public void OnCancelTimer(string name)
        {
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void DecodeClientData_should_emit_request_when_complete_get()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(ops);

        var requestData = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n");

        var buffer = TransportBuffer.Rent(requestData.Length);
        requestData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = requestData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        Assert.Single(ops.EmittedRequests);
        var request = ops.EmittedRequests[0];
        Assert.Equal("GET", request.Method.Method);
        Assert.Equal("/", request.RequestUri?.OriginalString);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void OnResponse_should_emit_response_headers()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(ops);

        var requestData = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n");

        var buffer = TransportBuffer.Rent(requestData.Length);
        requestData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = requestData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        var responseBody = "Hello"u8.ToArray();
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(responseBody)
        };
        response.Content.Headers.ContentLength = responseBody.Length;

        sm.OnResponse(response);

        Assert.True(ops.EmittedOutbound.Count >= 1);
        var outbound = ops.EmittedOutbound[0];
        Assert.IsType<TransportData>(outbound);

        var transportData = (TransportData)outbound;
        var responseText = Encoding.ASCII.GetString(transportData.Buffer.Span);
        Assert.Contains("200", responseText);
        Assert.Contains("Content-Length: 5", responseText);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void CanAcceptResponse_should_be_false_when_no_pending_requests()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(ops);

        Assert.False(sm.CanAcceptResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void CanAcceptResponse_should_be_true_after_request_decoded()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(ops);

        var requestData = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n");

        var buffer = TransportBuffer.Rent(requestData.Length);
        requestData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = requestData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        Assert.True(sm.CanAcceptResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.6")]
    public void ShouldCloseAfterResponse_should_be_true_when_connection_close_header()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(ops);

        var requestData = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Connection: close\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n");

        var buffer = TransportBuffer.Rent(requestData.Length);
        requestData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = requestData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void ShouldCloseAfterResponse_should_be_true_when_http_10_request()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(ops);

        var requestData = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.0\r\n" +
            "Host: localhost\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n");

        var buffer = TransportBuffer.Rent(requestData.Length);
        requestData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = requestData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.6")]
    public void OnResponse_should_set_connection_close_header_when_flag_set()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(ops);

        var requestData = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Connection: close\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n");

        var buffer = TransportBuffer.Rent(requestData.Length);
        requestData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = requestData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([])
        };

        sm.OnResponse(response);

        var outbound = ops.EmittedOutbound[0];
        var transportData = (TransportData)outbound;
        var responseText = Encoding.ASCII.GetString(transportData.Buffer.Span);
        Assert.Contains("Connection: close", responseText);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void OnResponse_should_not_include_body_in_transport_data()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(ops);

        var requestData = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n");

        var buffer = TransportBuffer.Rent(requestData.Length);
        requestData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = requestData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("hello world"u8.ToArray())
        };

        sm.OnResponse(response);

        var outboundItems = ops.EmittedOutbound.OfType<TransportData>().ToList();
        Assert.NotEmpty(outboundItems);
        var responseText = Encoding.ASCII.GetString(outboundItems[0].Buffer.Span);
        Assert.Contains("HTTP/1.1 200", responseText);
        Assert.DoesNotContain("hello world", responseText);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void OnBodyMessage_should_emit_body_chunk_as_transport_data()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(ops);

        var requestData = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n");

        var buffer = TransportBuffer.Rent(requestData.Length);
        requestData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = requestData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("hello world"u8.ToArray())
        };

        sm.OnResponse(response);
        var countAfterHeaders = ops.EmittedOutbound.Count;

        var bodyBytes = "hello world"u8.ToArray();
        var owner = System.Buffers.MemoryPool<byte>.Shared.Rent(bodyBytes.Length);
        bodyBytes.CopyTo(owner.Memory.Span);
        sm.OnBodyMessage(new OutboundBodyChunk(owner, bodyBytes.Length));
        sm.OnBodyMessage(new OutboundBodyComplete());

        var bodyItems = ops.EmittedOutbound.Skip(countAfterHeaders).OfType<TransportData>().ToList();
        Assert.NotEmpty(bodyItems);
        var bodyText = Encoding.UTF8.GetString(bodyItems[0].Buffer.Span);
        Assert.Contains("hello world", bodyText);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void CanAcceptResponse_should_be_false_when_outbound_body_pending()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(ops);

        var requestData = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n");

        var buffer = TransportBuffer.Rent(requestData.Length);
        requestData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = requestData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("hello world"u8.ToArray())
        };

        sm.OnResponse(response);

        Assert.False(sm.CanAcceptResponse);

        sm.OnBodyMessage(new OutboundBodyComplete());

        Assert.False(sm.CanAcceptResponse);
    }
}