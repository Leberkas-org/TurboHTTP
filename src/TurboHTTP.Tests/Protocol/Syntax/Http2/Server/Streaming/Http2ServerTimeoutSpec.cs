using Akka.Actor;
using Akka.Event;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Server;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Server.Streaming;

/// <summary>
/// Unit tests for HTTP/2 Http2ServerStateMachine timeout protection.
/// Tests keep-alive, headers timeout, and body data rate enforcement.
/// </summary>
public sealed class Http2ServerTimeoutSpec
{
    private sealed class FakeServerOps : IServerStageOperations
    {
        public List<HttpRequestMessage> EmittedRequests { get; } = [];
        public List<ITransportOutbound> EmittedOutbound { get; } = [];
        public List<(string Name, TimeSpan Delay)> Timers { get; } = [];
        public List<string> CancelledTimers { get; } = [];
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
            // Remove any existing timer with the same name
            Timers.RemoveAll(t => t.Name == name);
            Timers.Add((name, delay));
        }

        public void OnCancelTimer(string name)
        {
            CancelledTimers.Add(name);
            Timers.RemoveAll(t => t.Name == name);
        }
    }

    private static byte[] BuildHeadersFrame(int streamId, ReadOnlyMemory<byte> headerBlock, bool endStream = false,
        bool endHeaders = true)
    {
        const int frameHeaderSize = 9;
        var frameSize = frameHeaderSize + headerBlock.Length;
        var frame = new byte[frameSize];

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

    private static byte[] BuildDataFrame(int streamId, byte[] data, bool endStream = false)
    {
        const int frameHeaderSize = 9;
        var frameSize = frameHeaderSize + data.Length;
        var frame = new byte[frameSize];

        var length = data.Length;
        frame[0] = (byte)(length >> 16);
        frame[1] = (byte)(length >> 8);
        frame[2] = (byte)length;
        frame[3] = (byte)FrameType.Data;
        frame[4] = endStream ? (byte)DataFlags.EndStream : (byte)0;

        frame[5] = (byte)(streamId >> 24);
        frame[6] = (byte)(streamId >> 16);
        frame[7] = (byte)(streamId >> 8);
        frame[8] = (byte)streamId;

        data.CopyTo(frame.AsSpan(frameHeaderSize));

        return frame;
    }

    private static ReadOnlyMemory<byte> EncodeHeaders(string method, string path, string authority = "localhost")
    {
        var encoder = new HpackEncoder(useHuffman: true);
        var headers = new List<HpackHeader>
        {
            new(":method", method),
            new(":path", path),
            new(":scheme", "https"),
            new(":authority", authority),
        };

        var buffer = new byte[4096];
        var span = buffer.AsSpan();
        var written = encoder.Encode(headers, ref span, useHuffman: true);

        return new Memory<byte>(buffer, 0, written);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.4")]
    public void PreStart_should_schedule_keep_alive_timeout()
    {
        var ops = new FakeServerOps();
        var options = new TurboServerOptions();
        options.Http2.KeepAliveTimeout = TimeSpan.FromSeconds(130);
        var sm = new Http2ServerStateMachine(options, ops);

        sm.PreStart();

        // Should have scheduled keep-alive timer
        Assert.Single(ops.Timers);
        var timer = ops.Timers[0];
        Assert.Equal("keep-alive-timeout", timer.Name);
        Assert.Equal(TimeSpan.FromSeconds(130), timer.Delay);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.4")]
    public void KeepAlive_timeout_should_emit_goaway()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        sm.PreStart();
        ops.EmittedOutbound.Clear();

        // Fire the keep-alive timeout
        sm.OnTimerFired("keep-alive-timeout");

        // Should emit a GOAWAY frame
        Assert.Single(ops.EmittedOutbound);
        Assert.IsType<TransportData>(ops.EmittedOutbound[0]);
        var transportData = (TransportData)ops.EmittedOutbound[0];
        var frameType = transportData.Buffer.Span[3];
        Assert.Equal((byte)FrameType.GoAway, frameType);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.4")]
    public void KeepAlive_should_cancel_on_stream_open()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        sm.PreStart();
        ops.CancelledTimers.Clear();
        ops.Timers.Clear();

        // Send HEADERS to open a stream
        var headerBlock = EncodeHeaders("GET", "/", "example.com");
        var headersFrameData = BuildHeadersFrame(streamId: 1, headerBlock, endStream: true, endHeaders: true);

        var buffer = TransportBuffer.Rent(headersFrameData.Length);
        headersFrameData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = headersFrameData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        // Keep-alive should be cancelled
        Assert.Contains("keep-alive-timeout", ops.CancelledTimers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.4")]
    public void Headers_timeout_should_rst_stream_on_continuation_timeout()
    {
        var ops = new FakeServerOps();
        var options = new TurboServerOptions();
        options.Http2.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
        var sm = new Http2ServerStateMachine(options, ops);

        sm.PreStart();

        // Send HEADERS without EndHeaders (waiting for CONTINUATION)
        var headerBlock = EncodeHeaders("GET", "/", "example.com");
        var partSize = headerBlock.Length / 2;
        var headersFrameData = BuildHeadersFrame(
            streamId: 1,
            headerBlock[..partSize],
            endStream: false,
            endHeaders: false);

        var buffer = TransportBuffer.Rent(headersFrameData.Length);
        headersFrameData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = headersFrameData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        // Headers timeout should be scheduled
        var headersTimer = ops.Timers.FirstOrDefault(t => t.Name.StartsWith("headers-timeout:"));
        Assert.NotNull(headersTimer.Name);
        Assert.Equal("headers-timeout:1", headersTimer.Name);
        Assert.Equal(TimeSpan.FromSeconds(30), headersTimer.Delay);

        ops.EmittedOutbound.Clear();

        // Fire the headers timeout
        sm.OnTimerFired("headers-timeout:1");

        // Should emit a RST_STREAM frame
        Assert.Single(ops.EmittedOutbound);
        Assert.IsType<TransportData>(ops.EmittedOutbound[0]);
        var transportData = (TransportData)ops.EmittedOutbound[0];
        var frameType = transportData.Buffer.Span[3];
        Assert.Equal((byte)FrameType.RstStream, frameType);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.4")]
    public void Headers_timeout_should_cancel_on_endheaders()
    {
        var ops = new FakeServerOps();
        var options = new TurboServerOptions();
        options.Http2.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
        var sm = new Http2ServerStateMachine(options, ops);

        sm.PreStart();

        // Send HEADERS without EndHeaders
        var headerBlock = EncodeHeaders("GET", "/", "example.com");
        var partSize = headerBlock.Length / 2;
        var headersFrameData = BuildHeadersFrame(
            streamId: 1,
            headerBlock[..partSize],
            endStream: false,
            endHeaders: false);

        var buffer = TransportBuffer.Rent(headersFrameData.Length);
        headersFrameData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = headersFrameData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        ops.CancelledTimers.Clear();

        // Send CONTINUATION with EndHeaders
        var continuationData = BuildContinuationFrame(streamId: 1, headerBlock[partSize..], endHeaders: true);
        buffer = TransportBuffer.Rent(continuationData.Length);
        continuationData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = continuationData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        // Headers timeout should be cancelled
        Assert.Contains("headers-timeout:1", ops.CancelledTimers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.4")]
    public void Body_rate_check_should_schedule_on_data_frame()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        sm.PreStart();

        // Send HEADERS (no body yet)
        var headerBlock = EncodeHeaders("POST", "/", "example.com");
        var headersFrameData = BuildHeadersFrame(streamId: 1, headerBlock, endStream: false, endHeaders: true);

        var buffer = TransportBuffer.Rent(headersFrameData.Length);
        headersFrameData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = headersFrameData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        ops.Timers.Clear();

        // Send DATA frame
        var data = new byte[100];
        var dataFrameData = BuildDataFrame(streamId: 1, data, endStream: false);

        buffer = TransportBuffer.Rent(dataFrameData.Length);
        dataFrameData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = dataFrameData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        // Body rate check timer should be scheduled
        var rateTimer = ops.Timers.FirstOrDefault(t => t.Name == "body-rate-check");
        Assert.NotNull(rateTimer.Name);
        Assert.Equal("body-rate-check", rateTimer.Name);
        Assert.Equal(TimeSpan.FromSeconds(1), rateTimer.Delay);
    }

    private static byte[] BuildContinuationFrame(int streamId, ReadOnlyMemory<byte> headerBlock, bool endHeaders = true)
    {
        const int frameHeaderSize = 9;
        var frameSize = frameHeaderSize + headerBlock.Length;
        var frame = new byte[frameSize];

        var length = headerBlock.Length;
        frame[0] = (byte)(length >> 16);
        frame[1] = (byte)(length >> 8);
        frame[2] = (byte)length;
        frame[3] = (byte)FrameType.Continuation;
        frame[4] = endHeaders ? (byte)ContinuationFlags.EndHeaders : (byte)0;

        frame[5] = (byte)(streamId >> 24);
        frame[6] = (byte)(streamId >> 16);
        frame[7] = (byte)(streamId >> 8);
        frame[8] = (byte)streamId;

        headerBlock.Span.CopyTo(frame.AsSpan(frameHeaderSize));

        return frame;
    }
}