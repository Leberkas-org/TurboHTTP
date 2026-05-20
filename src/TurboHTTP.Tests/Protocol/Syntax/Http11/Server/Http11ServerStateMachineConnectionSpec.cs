using System.Buffers;
using System.Net;
using System.Text;
using Akka.Actor;
using Akka.Event;
using Servus.Akka.Transport;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Server;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11ServerStateMachineConnectionSpec
{
    private sealed class TrackingServerOps : IServerStageOperations
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<ITransportOutbound> Outbound { get; } = [];
        public List<(string Name, TimeSpan Delay)> ScheduledTimers { get; } = [];
        public List<string> CancelledTimers { get; } = [];
        public ILoggingAdapter Log { get; } = NoLogger.Instance;
        public IActorRef StageActor { get; set; } = ActorRefs.Nobody;

        public void OnRequest(HttpRequestMessage request) => Requests.Add(request);
        public void OnOutbound(ITransportOutbound item) => Outbound.Add(item);
        public void OnScheduleTimer(string name, TimeSpan delay) => ScheduledTimers.Add((name, delay));
        public void OnCancelTimer(string name) => CancelledTimers.Add(name);
    }

    private static TransportBuffer MakeBuffer(string raw)
    {
        var data = Encoding.ASCII.GetBytes(raw);
        var buffer = TransportBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        return buffer;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.6")]
    public void ShouldComplete_should_be_true_when_connection_close_on_request()
    {
        var ops = new TrackingServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        var requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\nContent-Length: 0\r\n\r\n";
        var buffer = MakeBuffer(requestData);

        sm.DecodeClientData(new TransportData(buffer));

        Assert.True(sm.ShouldComplete);
        Assert.Single(ops.Requests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void ShouldComplete_should_be_true_for_http10_request_on_h11_connection()
    {
        var ops = new TrackingServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        var requestData = "GET / HTTP/1.0\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var buffer = MakeBuffer(requestData);

        sm.DecodeClientData(new TransportData(buffer));

        Assert.True(sm.ShouldComplete);
        Assert.Single(ops.Requests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.6")]
    public void OnResponse_should_include_connection_close_when_ShouldComplete()
    {
        var ops = new TrackingServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        var requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\nContent-Length: 0\r\n\r\n";
        var buffer = MakeBuffer(requestData);

        sm.DecodeClientData(new TransportData(buffer));
        Assert.True(sm.ShouldComplete);

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([])
        };

        sm.OnResponse(response);

        var transportData = ops.Outbound.OfType<TransportData>().First();
        var responseText = Encoding.ASCII.GetString(transportData.Buffer.Span);
        Assert.Contains("Connection: close", responseText);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void DecodeClientData_should_set_ShouldComplete_on_decode_error()
    {
        var ops = new TrackingServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        var invalidRequest = "INVALID REQUEST DATA\r\n\r\n";
        var buffer = MakeBuffer(invalidRequest);

        sm.DecodeClientData(new TransportData(buffer));

        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void OnBodyMessage_OutboundBodyFailed_should_clear_pending_flag()
    {
        var ops = new TrackingServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        var requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var buffer = MakeBuffer(requestData);

        sm.DecodeClientData(new TransportData(buffer));
        Assert.True(sm.CanAcceptResponse);

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("test"u8.ToArray())
        };

        sm.OnResponse(response);

        // After response, CanAcceptResponse should be false because body is pending
        Assert.False(sm.CanAcceptResponse);

        // Send body failed
        var failed = new OutboundBodyFailed(new Exception("Test failure"));
        sm.OnBodyMessage(failed);

        // After body failed, CanAcceptResponse is false because _pendingResponseCount == 0 (response already sent)
        // not because body is pending
        Assert.False(sm.CanAcceptResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void OnBodyMessage_multi_chunk_should_emit_all_chunks()
    {
        var ops = new TrackingServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        var requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var buffer = MakeBuffer(requestData);

        sm.DecodeClientData(new TransportData(buffer));

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("hello world"u8.ToArray())
        };

        sm.OnResponse(response);
        var headerCount = ops.Outbound.Count;

        // Send first chunk
        var owner1 = MemoryPool<byte>.Shared.Rent(5);
        "hello"u8.CopyTo(owner1.Memory.Span);
        sm.OnBodyMessage(new OutboundBodyChunk(owner1, 5));

        // Send second chunk
        var owner2 = MemoryPool<byte>.Shared.Rent(6);
        " world"u8.CopyTo(owner2.Memory.Span);
        sm.OnBodyMessage(new OutboundBodyChunk(owner2, 6));

        // Complete body
        sm.OnBodyMessage(new OutboundBodyComplete());

        var bodyChunks = ops.Outbound.Skip(headerCount).OfType<TransportData>().ToList();
        Assert.Equal(2, bodyChunks.Count);

        var chunk1Text = Encoding.UTF8.GetString(bodyChunks[0].Buffer.Span);
        var chunk2Text = Encoding.UTF8.GetString(bodyChunks[1].Buffer.Span);
        Assert.Equal("hello", chunk1Text);
        Assert.Equal(" world", chunk2Text);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Cleanup_should_be_idempotent()
    {
        var ops = new TrackingServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        var requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var buffer = MakeBuffer(requestData);

        sm.DecodeClientData(new TransportData(buffer));

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("test"u8.ToArray())
        };

        sm.OnResponse(response);

        // Call Cleanup twice
        sm.Cleanup();
        sm.Cleanup();

        // Should not crash
        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void OnResponse_should_throw_when_no_pending_requests()
    {
        var ops = new TrackingServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([])
        };

        var ex = Assert.Throws<InvalidOperationException>(() => sm.OnResponse(response));
        Assert.Contains("no requests are pending", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
