using Akka.Event;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Protocol.Syntax.Http2.Options;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Server;
using AkkaActor = Akka.Actor;

using TurboHTTP.Server;



/// <summary>
/// Unit tests for HTTP/2 SessionManager CONTINUATION state machine.
/// Tests fragmented header blocks, timer management, and stream correlation.
/// </summary>
public sealed class Http2ContinuationStateSpec
{
    private sealed class TrackingServerOps : IServerStageOperations
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<ITransportOutbound> Outbound { get; } = [];
        public List<(string Name, TimeSpan Delay)> ScheduledTimers { get; } = [];
        public List<string> CancelledTimers { get; } = [];
        public ILoggingAdapter Log { get; } = NoLogger.Instance;
        public AkkaActor.IActorRef StageActor { get; set; } = AkkaActor.ActorRefs.Nobody;

        public void OnRequest(TurboHttpContext context)
        {
        }

        public void OnOutbound(ITransportOutbound item)
        {
            Outbound.Add(item);
        }

        public void OnScheduleTimer(string name, TimeSpan delay)
        {
            ScheduledTimers.Add((name, delay));
        }

        public void OnCancelTimer(string name)
        {
            CancelledTimers.Add(name);
        }
    }

    private static byte[] BuildHeadersFrame(
        int streamId,
        ReadOnlyMemory<byte> headerBlock,
        bool endStream = false,
        bool endHeaders = true)
    {
        const int frameHeaderSize = 9;
        var frameSize = frameHeaderSize + headerBlock.Length;
        var frame = new byte[frameSize];

        // Frame header: length (3 bytes), type (1), flags (1), stream ID (4)
        var length = headerBlock.Length;
        frame[0] = (byte)(length >> 16);
        frame[1] = (byte)(length >> 8);
        frame[2] = (byte)length;
        frame[3] = (byte)FrameType.Headers;

        byte flags = 0;
        if (endStream) flags |= (byte)Headers.EndStream;
        if (endHeaders) flags |= (byte)Headers.EndHeaders;
        frame[4] = flags;

        frame[5] = (byte)(streamId >> 24);
        frame[6] = (byte)(streamId >> 16);
        frame[7] = (byte)(streamId >> 8);
        frame[8] = (byte)streamId;

        headerBlock.Span.CopyTo(frame.AsSpan(frameHeaderSize));

        return frame;
    }

    private static byte[] BuildContinuationFrame(
        int streamId,
        ReadOnlyMemory<byte> headerBlock,
        bool endHeaders = true)
    {
        const int frameHeaderSize = 9;
        var frameSize = frameHeaderSize + headerBlock.Length;
        var frame = new byte[frameSize];

        // Frame header: length (3 bytes), type (1), flags (1), stream ID (4)
        var length = headerBlock.Length;
        frame[0] = (byte)(length >> 16);
        frame[1] = (byte)(length >> 8);
        frame[2] = (byte)length;
        frame[3] = (byte)FrameType.Continuation;
        frame[4] = endHeaders ? (byte)0x04 : (byte)0;

        frame[5] = (byte)(streamId >> 24);
        frame[6] = (byte)(streamId >> 16);
        frame[7] = (byte)(streamId >> 8);
        frame[8] = (byte)streamId;

        headerBlock.Span.CopyTo(frame.AsSpan(frameHeaderSize));

        return frame;
    }

    private static ReadOnlyMemory<byte> EncodeStandardHeaders()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<HpackHeader>
        {
            new(":method", "GET"),
            new(":path", "/"),
            new(":scheme", "https"),
            new(":authority", "example.com"),
        };

        var buffer = new byte[4096];
        var span = buffer.AsSpan();
        var written = encoder.Encode(headers, ref span, useHuffman: false);

        return new Memory<byte>(buffer, 0, written);
    }

    private static TransportBuffer WrapFrame(byte[] frame)
    {
        var buffer = TransportBuffer.Rent(frame.Length);
        frame.CopyTo(buffer.FullMemory.Span);
        buffer.Length = frame.Length;
        return buffer;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.10")]
    public void Headers_without_EndHeaders_then_Continuation_should_emit_request()
    {
        var ops = new TrackingServerOps();
        var encoderOptions = new Http2ServerEncoderOptions();
        var decoderOptions = new Http2ServerDecoderOptions();
        var sm = new Http2ServerSessionManager(encoderOptions, decoderOptions, ops);

        sm.PreStart();
        ops.Outbound.Clear(); // Clear initial SETTINGS frame
        ops.ScheduledTimers.Clear();

        // Encode complete header block
        var headerBlock = EncodeStandardHeaders();

        // Split at midpoint
        var splitPoint = headerBlock.Length / 2;
        var firstHalf = headerBlock[..splitPoint];
        var secondHalf = headerBlock[splitPoint..];

        // Send first half without END_HEADERS
        var headersFrame = BuildHeadersFrame(
            streamId: 1,
            firstHalf,
            endStream: false,
            endHeaders: false);
        sm.DecodeClientData(WrapFrame(headersFrame));

        // No request yet
        Assert.Empty(ops.Requests);

        // Send second half with END_HEADERS
        var continuationFrame = BuildContinuationFrame(
            streamId: 1,
            secondHalf,
            endHeaders: true);
        sm.DecodeClientData(WrapFrame(continuationFrame));

        // Now request should be emitted
        Assert.Single(ops.Requests);
        var request = ops.Requests[0];
        Assert.Equal("GET", request.Method.Method);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.10")]
    public void Continuation_on_wrong_stream_should_throw_protocol_error()
    {
        var ops = new TrackingServerOps();
        var encoderOptions = new Http2ServerEncoderOptions();
        var decoderOptions = new Http2ServerDecoderOptions();
        var sm = new Http2ServerSessionManager(encoderOptions, decoderOptions, ops);

        sm.PreStart();
        ops.Outbound.Clear(); // Clear initial SETTINGS frame

        var headerBlock = EncodeStandardHeaders();
        var splitPoint = headerBlock.Length / 2;

        // Send HEADERS on stream 1 without END_HEADERS
        var headersFrame = BuildHeadersFrame(
            streamId: 1,
            headerBlock[..splitPoint],
            endStream: false,
            endHeaders: false);
        sm.DecodeClientData(WrapFrame(headersFrame));

        // Send CONTINUATION on stream 3 (wrong stream)
        // This should throw a protocol exception at the frame decoder level
        var continuationFrame = BuildContinuationFrame(
            streamId: 3,
            headerBlock[splitPoint..],
            endHeaders: true);

        // RFC 9113 §6.10 requires a CONTINUATION on the same stream
        // The FrameDecoder catches this before SessionManager processing
        var ex = Assert.Throws<HttpProtocolException>(() =>
        {
            sm.DecodeClientData(WrapFrame(continuationFrame));
        });

        Assert.Contains("RFC 9113 §6.10", ex.Message);
        Assert.Contains("stream", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.10")]
    public void Headers_with_EndHeaders_true_should_emit_request_immediately()
    {
        var ops = new TrackingServerOps();
        var encoderOptions = new Http2ServerEncoderOptions();
        var decoderOptions = new Http2ServerDecoderOptions();
        var sm = new Http2ServerSessionManager(encoderOptions, decoderOptions, ops);

        sm.PreStart();
        ops.Outbound.Clear();
        ops.ScheduledTimers.Clear();

        var headerBlock = EncodeStandardHeaders();

        // Send complete HEADERS with END_HEADERS
        var headersFrame = BuildHeadersFrame(
            streamId: 1,
            headerBlock,
            endStream: true,
            endHeaders: true);
        sm.DecodeClientData(WrapFrame(headersFrame));

        // Request should be emitted immediately
        Assert.Single(ops.Requests);
        var request = ops.Requests[0];
        Assert.Equal("GET", request.Method.Method);

        // No timer should be scheduled (END_HEADERS was set)
        Assert.Empty(ops.ScheduledTimers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.10")]
    public void Headers_without_EndHeaders_should_schedule_headers_timeout()
    {
        var ops = new TrackingServerOps();
        var encoderOptions = new Http2ServerEncoderOptions();
        var decoderOptions = new Http2ServerDecoderOptions();
        var sm = new Http2ServerSessionManager(encoderOptions, decoderOptions, ops);

        sm.PreStart();
        ops.ScheduledTimers.Clear();

        var headerBlock = EncodeStandardHeaders();
        var splitPoint = headerBlock.Length / 2;

        // Send HEADERS without END_HEADERS
        var headersFrame = BuildHeadersFrame(
            streamId: 1,
            headerBlock[..splitPoint],
            endStream: false,
            endHeaders: false);
        sm.DecodeClientData(WrapFrame(headersFrame));

        // Timer should be scheduled
        Assert.Single(ops.ScheduledTimers);
        var (timerName, delay) = ops.ScheduledTimers[0];

        // Timer name should start with "headers-timeout:"
        Assert.StartsWith("headers-timeout:", timerName);

        // Timer delay should be reasonable (30 seconds as per implementation)
        Assert.Equal(TimeSpan.FromSeconds(30), delay);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.10")]
    public void Continuation_with_EndHeaders_should_cancel_headers_timeout()
    {
        var ops = new TrackingServerOps();
        var encoderOptions = new Http2ServerEncoderOptions();
        var decoderOptions = new Http2ServerDecoderOptions();
        var sm = new Http2ServerSessionManager(encoderOptions, decoderOptions, ops);

        sm.PreStart();
        ops.ScheduledTimers.Clear();
        ops.CancelledTimers.Clear();

        var headerBlock = EncodeStandardHeaders();
        var splitPoint = headerBlock.Length / 2;

        // Send HEADERS without END_HEADERS
        var headersFrame = BuildHeadersFrame(
            streamId: 1,
            headerBlock[..splitPoint],
            endStream: false,
            endHeaders: false);
        sm.DecodeClientData(WrapFrame(headersFrame));

        // Timer was scheduled
        Assert.Single(ops.ScheduledTimers);
        var (scheduledTimerName, _) = ops.ScheduledTimers[0];

        // Send CONTINUATION with END_HEADERS
        var continuationFrame = BuildContinuationFrame(
            streamId: 1,
            headerBlock[splitPoint..],
            endHeaders: true);
        sm.DecodeClientData(WrapFrame(continuationFrame));

        // Timer should be cancelled
        Assert.Single(ops.CancelledTimers);
        var cancelledTimerName = ops.CancelledTimers[0];

        // Cancelled timer should match the scheduled timer
        Assert.Equal(scheduledTimerName, cancelledTimerName);

        // Request should be emitted
        Assert.Single(ops.Requests);
    }
}



