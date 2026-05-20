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

public sealed class Http11ServerStateMachineTimerSpec
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
    [Trait("RFC", "RFC9112-6.5")]
    public void OnTimerFired_request_headers_should_set_ShouldComplete()
    {
        var ops = new TrackingServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        sm.OnTimerFired("request-headers");

        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void OnTimerFired_keep_alive_should_set_ShouldComplete()
    {
        var ops = new TrackingServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        sm.OnTimerFired("keep-alive");

        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.5")]
    public void DecodeClientData_should_schedule_request_headers_timer()
    {
        var ops = new TrackingServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        // Feed partial request data (no final \r\n\r\n) to trigger NeedMore state
        // This keeps the decoder in incomplete state, allowing timer scheduling
        var partialRequest = "GET / HTTP/1.1\r\nHost: localhost\r\n";
        var buffer = MakeBuffer(partialRequest);

        sm.DecodeClientData(new TransportData(buffer));

        Assert.Contains(ops.ScheduledTimers, t => t.Name == "request-headers");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.5")]
    public void DecodeClientData_should_cancel_request_headers_timer_when_complete()
    {
        var ops = new TrackingServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        // First, feed partial request to schedule timer
        var partialRequest = "GET / HTTP/1.1\r\nHost: localhost\r\n";
        var buffer1 = MakeBuffer(partialRequest);
        sm.DecodeClientData(new TransportData(buffer1));

        // Then feed completion to cancel timer
        var completion = "\r\n";
        var buffer2 = MakeBuffer(completion);
        sm.DecodeClientData(new TransportData(buffer2));

        Assert.Contains(ops.CancelledTimers, t => t == "request-headers");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.1")]
    public void OnResponse_should_schedule_keep_alive_timer_after_204_body_completes()
    {
        var ops = new TrackingServerOps();

        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        // Decode a complete request first
        var requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var buffer = MakeBuffer(requestData);
        sm.DecodeClientData(new TransportData(buffer));

        // Verify we have a pending request
        Assert.Single(ops.Requests);
        Assert.True(sm.CanAcceptResponse);

        // Send a 204 No Content response (has EmptyContent automatically)
        var response = new HttpResponseMessage(HttpStatusCode.NoContent);

        sm.OnResponse(response);

        // Clear timers to isolate the keep-alive timer from request-headers timer
        var timersBeforeBodyComplete = ops.ScheduledTimers.ToList();

        // Complete the body (even though it's empty)
        sm.OnBodyMessage(new OutboundBodyComplete());

        // Check that keep-alive timer was scheduled after body completion
        var newTimers = ops.ScheduledTimers.Skip(timersBeforeBodyComplete.Count).ToList();
        Assert.Contains(newTimers, t => t.Name == "keep-alive");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void OnBodyMessage_complete_should_schedule_keep_alive_timer()
    {
        var ops = new TrackingServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        // Decode a request
        var requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var buffer = MakeBuffer(requestData);
        sm.DecodeClientData(new TransportData(buffer));

        // Send response with body
        var responseBody = "Hello"u8.ToArray();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(responseBody)
        };
        response.Content.Headers.ContentLength = responseBody.Length;

        sm.OnResponse(response);

        // Send body chunks and completion
        var bodyBytes = "Hello"u8.ToArray();
        var owner = System.Buffers.MemoryPool<byte>.Shared.Rent(bodyBytes.Length);
        bodyBytes.CopyTo(owner.Memory.Span);
        sm.OnBodyMessage(new OutboundBodyChunk(owner, bodyBytes.Length));

        // Complete the body — this should schedule keep-alive timer
        sm.OnBodyMessage(new OutboundBodyComplete());

        Assert.Contains(ops.ScheduledTimers, t => t.Name == "keep-alive");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.5")]
    public void Cleanup_should_cancel_all_timers()
    {
        var ops = new TrackingServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        // Decode a partial request to activate request-headers timer
        var partialRequest = "GET / HTTP/1.1\r\nHost: localhost\r\n";
        var buffer = MakeBuffer(partialRequest);
        sm.DecodeClientData(new TransportData(buffer));

        Assert.Contains(ops.ScheduledTimers, t => t.Name == "request-headers");

        // Now call Cleanup — should cancel both timers
        sm.Cleanup();

        Assert.Contains(ops.CancelledTimers, t => t == "request-headers");
        Assert.Contains(ops.CancelledTimers, t => t == "keep-alive");
    }
}
