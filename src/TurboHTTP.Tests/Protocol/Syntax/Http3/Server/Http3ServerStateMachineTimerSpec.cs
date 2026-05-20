using Akka.Actor;
using Akka.Event;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Protocol.Syntax.Http3.Server;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Server;

/// <summary>
/// Unit tests for HTTP/3 Http3ServerStateMachine timer behavior and error recovery.
/// Tests keep-alive timeout, headers-timeout RST emission, cleanup idempotency,
/// and proper request flushing on downstream finish.
/// </summary>
public sealed class Http3ServerStateMachineTimerSpec
{
    private sealed class TrackingServerOps : IServerStageOperations
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<ITransportOutbound> Outbound { get; } = [];
        public Dictionary<string, (string Name, TimeSpan Delay)> ScheduledTimers { get; } = [];
        public List<string> CancelledTimers { get; } = [];
        public ILoggingAdapter Log { get; } = NoLogger.Instance;
        public IActorRef StageActor { get; set; } = ActorRefs.Nobody;

        public void OnRequest(HttpRequestMessage request) => Requests.Add(request);

        public void OnOutbound(ITransportOutbound item) => Outbound.Add(item);

        public void OnScheduleTimer(string name, TimeSpan delay) => ScheduledTimers[name] = (name, delay);

        public void OnCancelTimer(string name)
        {
            ScheduledTimers.Remove(name);
            CancelledTimers.Add(name);
        }
    }

    private static void SendRequest(Http3ServerStateMachine sm, long streamId)
    {
        var ts = new QpackTableSync(0, 0, 0, 0);
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "localhost"),
        };
        var block = ts.Encoder.Encode(headers);
        var frame = new HeadersFrame(block);
        var buf = new byte[frame.SerializedSize];
        var span = buf.AsSpan();
        frame.WriteTo(ref span);

        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId), StreamDirection.Bidirectional));
        var buffer = TransportBuffer.Rent(buf.Length);
        buf.CopyTo(buffer.FullMemory.Span);
        buffer.Length = buf.Length;
        sm.DecodeClientData(new MultiplexedData(buffer, streamId));
        sm.DecodeClientData(new StreamReadCompleted(StreamTarget.FromId(streamId)));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.1.2")]
    public void PreStart_should_schedule_keep_alive_timer()
    {
        var ops = new TrackingServerOps();
        var sm = new Http3ServerStateMachine(ops, keepAliveTimeout: TimeSpan.FromSeconds(130));

        sm.PreStart();

        Assert.True(ops.ScheduledTimers.ContainsKey("keep-alive-timeout"),
            "keep-alive-timeout should be scheduled on PreStart");

        var timerEntry = ops.ScheduledTimers["keep-alive-timeout"];
        Assert.Equal(TimeSpan.FromSeconds(130), timerEntry.Delay);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void ShouldComplete_should_always_be_false()
    {
        var ops = new TrackingServerOps();
        var sm = new Http3ServerStateMachine(ops);

        Assert.False(sm.ShouldComplete, "ShouldComplete should be false after construction");

        sm.PreStart();
        Assert.False(sm.ShouldComplete, "ShouldComplete should be false after PreStart");

        SendRequest(sm, 4);
        Assert.False(sm.ShouldComplete, "ShouldComplete should be false after request");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.1.2")]
    public void Stream_open_should_cancel_keep_alive()
    {
        var ops = new TrackingServerOps();
        var sm = new Http3ServerStateMachine(ops);

        sm.PreStart();

        Assert.True(ops.ScheduledTimers.ContainsKey("keep-alive-timeout"),
            "keep-alive-timeout should be scheduled on PreStart");

        // Open a stream by sending request
        SendRequest(sm, 4);

        Assert.True(ops.CancelledTimers.Contains("keep-alive-timeout"),
            "keep-alive-timeout should be cancelled when stream opens");

        Assert.False(ops.ScheduledTimers.ContainsKey("keep-alive-timeout"),
            "keep-alive-timeout should not be in scheduled timers");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void OnTimerFired_headers_timeout_should_emit_RstStream()
    {
        var ops = new TrackingServerOps();
        var sm = new Http3ServerStateMachine(ops);

        const long streamId = 4;

        sm.PreStart();

        // Open stream but don't send HEADERS (to simulate timeout scenario)
        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId), StreamDirection.Bidirectional));

        // Clear any outbound from PreStart
        ops.Outbound.Clear();

        // Fire headers-timeout timer
        sm.OnTimerFired($"headers-timeout:{streamId}");

        // Should have emitted ResetStream
        var resetStreams = ops.Outbound.OfType<ResetStream>().ToList();
        Assert.NotEmpty(resetStreams);
        Assert.Equal(streamId, resetStreams[0].StreamId.Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void Cleanup_should_be_idempotent()
    {
        var ops = new TrackingServerOps();
        var sm = new Http3ServerStateMachine(ops);

        sm.PreStart();
        SendRequest(sm, 4);

        // First cleanup should succeed
        sm.Cleanup();

        // Second cleanup should not throw
        sm.Cleanup();

        // Both should have completed without exception
        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void OnDownstreamFinished_should_flush_pending()
    {
        var ops = new TrackingServerOps();
        var sm = new Http3ServerStateMachine(ops);

        const long streamId = 4;

        sm.PreStart();

        // Build HEADERS frame
        var ts = new QpackTableSync(0, 0, 0, 0);
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "localhost"),
        };
        var block = ts.Encoder.Encode(headers);
        var frame = new HeadersFrame(block);
        var buf = new byte[frame.SerializedSize];
        var span = buf.AsSpan();
        frame.WriteTo(ref span);

        // Open stream and send HEADERS but NOT StreamReadCompleted
        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId), StreamDirection.Bidirectional));
        var buffer = TransportBuffer.Rent(buf.Length);
        buf.CopyTo(buffer.FullMemory.Span);
        buffer.Length = buf.Length;
        sm.DecodeClientData(new MultiplexedData(buffer, streamId));

        // Request should NOT be emitted yet (no StreamReadCompleted)
        Assert.Empty(ops.Requests);

        // Trigger downstream finished
        sm.OnDownstreamFinished();

        // Request should now be emitted
        Assert.Single(ops.Requests);
        var request = ops.Requests[0];
        Assert.Equal("GET", request.Method.Method);
        Assert.Equal("https://localhost/", request.RequestUri?.ToString());
    }
}
