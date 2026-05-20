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
/// Unit tests for HTTP/2 Http2ServerStateMachine flow control behavior.
/// Tests window updates, flow control violations, and stream/connection window management.
/// </summary>
public sealed class Http2ServerFlowControlSpec
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


    private static byte[] BuildDataFrame(int streamId, byte[] data, bool endStream = false)
    {
        const int frameHeaderSize = 9;
        var frameSize = frameHeaderSize + data.Length;
        var frame = new byte[frameSize];

        // Frame header: length (3 bytes), type (1), flags (1), stream ID (4)
        var length = data.Length;
        frame[0] = (byte)(length >> 16);
        frame[1] = (byte)(length >> 8);
        frame[2] = (byte)length;
        frame[3] = (byte)FrameType.Data;

        byte flags = 0;
        if (endStream) flags |= (byte)DataFlags.EndStream;
        frame[4] = flags;

        frame[5] = (byte)(streamId >> 24);
        frame[6] = (byte)(streamId >> 16);
        frame[7] = (byte)(streamId >> 8);
        frame[8] = (byte)streamId;

        data.CopyTo(frame.AsSpan(frameHeaderSize));

        return frame;
    }

    private static byte[] BuildWindowUpdateFrame(int streamId, uint increment)
    {
        const int frameHeaderSize = 9;
        const int windowUpdateSize = 4;
        var frameSize = frameHeaderSize + windowUpdateSize;
        var frame = new byte[frameSize];

        frame[0] = 0;
        frame[1] = 0;
        frame[2] = windowUpdateSize;
        frame[3] = (byte)FrameType.WindowUpdate;
        frame[4] = 0; // No flags

        frame[5] = (byte)(streamId >> 24);
        frame[6] = (byte)(streamId >> 16);
        frame[7] = (byte)(streamId >> 8);
        frame[8] = (byte)streamId;

        // Increment (31 bits, big-endian)
        var incValue = increment & 0x7FFFFFFF;
        frame[9] = (byte)(incValue >> 24);
        frame[10] = (byte)(incValue >> 16);
        frame[11] = (byte)(incValue >> 8);
        frame[12] = (byte)incValue;

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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public void DecodeClientData_with_data_frame_should_emit_window_update_when_threshold_reached()
    {
        // Create SM with small window so we can easily exceed threshold
        const int initialWindowSize = 16384;
        var ops = new FakeServerOps();
        var options = new TurboServerOptions();
        options.Http2.MaxConcurrentStreams = 100;
        options.Http2.InitialConnectionWindowSize = 65535;
        options.Http2.InitialStreamWindowSize = initialWindowSize;
        var sm = new Http2ServerStateMachine(options, ops);

        // Send HEADERS on stream 1 with endStream=false to accept body data
        var headerBlock = EncodeHeaders("POST", "/upload", "example.com");
        var headersFrameData = BuildHeadersFrame(streamId: 1, headerBlock, endStream: false, endHeaders: true);

        var buffer = TransportBuffer.Rent(headersFrameData.Length);
        headersFrameData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = headersFrameData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        // Request should be emitted immediately when headers arrive (with endStream=false)
        Assert.Single(ops.EmittedRequests);
        var request = ops.EmittedRequests[0];
        Assert.IsType<StreamContent>(request.Content);

        ops.EmittedOutbound.Clear();

        // Send first DATA frame (small, under threshold)
        var dataPayload1 = new byte[1000];
        var dataFrameData1 = BuildDataFrame(streamId: 1, dataPayload1, endStream: false);

        var dataBuf1 = TransportBuffer.Rent(dataFrameData1.Length);
        dataFrameData1.CopyTo(dataBuf1.FullMemory.Span);
        dataBuf1.Length = dataFrameData1.Length;

        sm.DecodeClientData(new TransportData(dataBuf1));

        // No window update yet (threshold not exceeded)
        ops.EmittedRequests.Clear();
        var windowUpdates1 = ops.EmittedOutbound.OfType<TransportData>()
            .Where(td => td.Buffer.Span.Length >= 9 && td.Buffer.Span[3] == (byte)FrameType.WindowUpdate)
            .ToList();
        Assert.Empty(windowUpdates1);

        ops.EmittedOutbound.Clear();

        // Send second DATA frame to exceed half the window (threshold for WINDOW_UPDATE)
        // We've sent 1000, stream window is 16384, threshold is 8192, so send 7200 more
        var dataPayload2 = new byte[7200];
        for (var i = 0; i < dataPayload2.Length; i++)
        {
            dataPayload2[i] = (byte)(i % 256);
        }

        var dataFrameData2 = BuildDataFrame(streamId: 1, dataPayload2, endStream: false);

        var dataBuf2 = TransportBuffer.Rent(dataFrameData2.Length);
        dataFrameData2.CopyTo(dataBuf2.FullMemory.Span);
        dataBuf2.Length = dataFrameData2.Length;

        sm.DecodeClientData(new TransportData(dataBuf2));

        // Now verify WINDOW_UPDATE was emitted for stream 1
        Assert.NotEmpty(ops.EmittedOutbound);

        var foundWindowUpdate = false;
        foreach (var item in ops.EmittedOutbound)
        {
            if (item is TransportData td)
            {
                var frameData = td.Buffer.Span;
                if (frameData.Length >= 9 && frameData[3] == (byte)FrameType.WindowUpdate)
                {
                    var sid = (frameData[5] << 24) | (frameData[6] << 16)
                                                   | (frameData[7] << 8) | frameData[8];
                    if (sid == 1)
                    {
                        foundWindowUpdate = true;
                        break;
                    }
                }
            }
        }

        Assert.True(foundWindowUpdate, "Expected WINDOW_UPDATE frame for stream 1 to be emitted");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public void DecodeClientData_with_window_update_should_not_emit_goaway()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        sm.PreStart();
        ops.EmittedOutbound.Clear();

        // Send WINDOW_UPDATE on stream 0 (connection-level)
        var windowUpdateData = BuildWindowUpdateFrame(streamId: 0, increment: 16384);

        var buffer = TransportBuffer.Rent(windowUpdateData.Length);
        windowUpdateData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = windowUpdateData.Length;

        // This should not throw or emit GOAWAY
        sm.DecodeClientData(new TransportData(buffer));

        // Verify no GOAWAY was emitted
        var hasGoAway = false;
        foreach (var item in ops.EmittedOutbound)
        {
            if (item is TransportData td)
            {
                var frameData = td.Buffer.Span;
                if (frameData.Length >= 9 && frameData[3] == (byte)FrameType.GoAway)
                {
                    hasGoAway = true;
                    break;
                }
            }
        }

        Assert.False(hasGoAway, "Expected no GOAWAY frame after successful WINDOW_UPDATE");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public void DecodeClientData_with_multiple_data_frames_should_track_window_correctly()
    {
        const int initialWindowSize = 20000;
        var ops = new FakeServerOps();
        var options = new TurboServerOptions();
        options.Http2.MaxConcurrentStreams = 100;
        options.Http2.InitialConnectionWindowSize = 65535;
        options.Http2.InitialStreamWindowSize = initialWindowSize;
        var sm = new Http2ServerStateMachine(options, ops);

        // Send HEADERS
        var headerBlock = EncodeHeaders("POST", "/", "example.com");
        var headersFrameData = BuildHeadersFrame(streamId: 1, headerBlock, endStream: false, endHeaders: true);

        var buffer = TransportBuffer.Rent(headersFrameData.Length);
        headersFrameData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = headersFrameData.Length;

        sm.DecodeClientData(new TransportData(buffer));
        ops.EmittedOutbound.Clear();

        // Send first DATA frame (5000 bytes)
        var data1 = new byte[5000];
        var frame1Data = BuildDataFrame(streamId: 1, data1, endStream: false);
        var buf1 = TransportBuffer.Rent(frame1Data.Length);
        frame1Data.CopyTo(buf1.FullMemory.Span);
        buf1.Length = frame1Data.Length;
        sm.DecodeClientData(new TransportData(buf1));

        // Send second DATA frame (6000 bytes) - should exceed half window
        var data2 = new byte[6000];
        var frame2Data = BuildDataFrame(streamId: 1, data2, endStream: false);
        var buf2 = TransportBuffer.Rent(frame2Data.Length);
        frame2Data.CopyTo(buf2.FullMemory.Span);
        buf2.Length = frame2Data.Length;
        sm.DecodeClientData(new TransportData(buf2));

        // Should have emitted at least one WINDOW_UPDATE
        var windowUpdateCount = ops.EmittedOutbound.Count(item =>
            item is TransportData { Buffer.Span.Length: >= 9 } td
            && td.Buffer.Span[3] == (byte)FrameType.WindowUpdate);

        Assert.True(windowUpdateCount > 0, "Expected at least one WINDOW_UPDATE frame");
    }
}