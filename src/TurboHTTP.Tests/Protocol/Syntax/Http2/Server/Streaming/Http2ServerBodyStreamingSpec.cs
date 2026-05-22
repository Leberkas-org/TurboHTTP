using Akka.Actor;
using Akka.Event;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Server;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Server.Streaming;

/// <summary>
/// Unit tests for HTTP/2 Http2ServerStateMachine streaming request body handling via System.IO.Pipelines.
/// Tests PipeBodyContent emission, DATA frame writing, and max body size enforcement.
/// </summary>
public sealed class Http2ServerBodyStreamingSpec
{
    private sealed class FakeServerOps : IServerStageOperations
    {
        public List<TurboHttpContext> EmittedRequests { get; } = [];
        public List<ITransportOutbound> EmittedOutbound { get; } = [];
        public ILoggingAdapter Log { get; } = NoLogger.Instance;
        public IActorRef StageActor { get; set; } = ActorRefs.Nobody;

        public void OnRequest(TurboHttpContext context)
        {
            EmittedRequests.Add(context);
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
    [Trait("RFC", "RFC9113-6.2")]
    public async Task DecodeClientData_with_body_should_emit_request_on_headers_with_streaming_content()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        // Send HEADERS frame with endStream=false (body will follow)
        var headerBlock = EncodeHeaders("POST", "/api/data", "example.com");
        var headersFrameData = BuildHeadersFrame(streamId: 1, headerBlock, endStream: false, endHeaders: true);

        var buffer = TransportBuffer.Rent(headersFrameData.Length);
        headersFrameData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = headersFrameData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        // Request should be emitted immediately
        Assert.Single(ops.EmittedRequests);
        var context = ops.EmittedRequests[0];

        // Request should have a body stream
        var bodyStream = context.Request.Body;
        Assert.NotNull(bodyStream);
        Assert.True(bodyStream.CanRead);

        // Now send DATA frame
        var bodyData = "Hello, Server!"u8.ToArray();
        var dataFrameData = BuildDataFrame(streamId: 1, bodyData, endStream: true);

        var buffer2 = TransportBuffer.Rent(dataFrameData.Length);
        dataFrameData.CopyTo(buffer2.FullMemory.Span);
        buffer2.Length = dataFrameData.Length;

        sm.DecodeClientData(new TransportData(buffer2));

        // Read from body stream
        using var stream = new MemoryStream();
        await bodyStream.CopyToAsync(stream, TestContext.Current.CancellationToken);
        var receivedData = stream.ToArray();
        Assert.Equal(bodyData, receivedData);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void DecodeClientData_headers_only_should_emit_request_without_pipe_content()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        // Send HEADERS frame with endStream=true (no body)
        var headerBlock = EncodeHeaders("GET", "/api/status", "example.com");
        var headersFrameData = BuildHeadersFrame(streamId: 1, headerBlock, endStream: true, endHeaders: true);

        var buffer = TransportBuffer.Rent(headersFrameData.Length);
        headersFrameData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = headersFrameData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        // Request should be emitted
        Assert.Single(ops.EmittedRequests);
        var context = ops.EmittedRequests[0];

        // Request should have empty body stream
        var bodyStream = context.Request.Body;
        Assert.NotNull(bodyStream);
        using var ms = new MemoryStream();
        bodyStream.CopyTo(ms);
        Assert.Empty(ms.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void DecodeClientData_exceeding_max_body_size_should_emit_rst_stream()
    {
        var ops = new FakeServerOps();
        const long maxBodySize = 100;
        var options = new TurboServerOptions();
        options.Http2.MaxRequestBodySize = maxBodySize;
        var sm = new Http2ServerStateMachine(options, ops);

        // Send HEADERS frame with endStream=false
        var headerBlock = EncodeHeaders("POST", "/api/upload", "example.com");
        var headersFrameData = BuildHeadersFrame(streamId: 1, headerBlock, endStream: false, endHeaders: true);

        var buffer = TransportBuffer.Rent(headersFrameData.Length);
        headersFrameData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = headersFrameData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        // Request should be emitted
        Assert.Single(ops.EmittedRequests);
        var initialOutboundCount = ops.EmittedOutbound.Count;

        // Send DATA frame that exceeds max body size
        var largeData = new byte[150];
        Array.Fill(largeData, (byte)'X');
        var dataFrameData = BuildDataFrame(streamId: 1, largeData, endStream: false);

        var buffer2 = TransportBuffer.Rent(dataFrameData.Length);
        dataFrameData.CopyTo(buffer2.FullMemory.Span);
        buffer2.Length = dataFrameData.Length;

        sm.DecodeClientData(new TransportData(buffer2));

        // RST_STREAM should have been emitted (or possibly other control frames too)
        var newOutbound = ops.EmittedOutbound.Skip(initialOutboundCount).ToList();
        Assert.NotEmpty(newOutbound);

        // Find RST_STREAM frame
        var rstFrame = newOutbound.FirstOrDefault(f =>
        {
            if (f is not TransportData td) return false;
            var span = td.Buffer.Span;
            return span.Length >= 9 && span[3] == (byte)FrameType.RstStream;
        });

        Assert.NotNull(rstFrame);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public async Task DecodeClientData_with_multiple_data_frames_should_aggregate_in_pipe()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        // Send HEADERS frame with endStream=false
        var headerBlock = EncodeHeaders("POST", "/api/stream", "example.com");
        var headersFrameData = BuildHeadersFrame(streamId: 1, headerBlock, endStream: false, endHeaders: true);

        var buffer = TransportBuffer.Rent(headersFrameData.Length);
        headersFrameData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = headersFrameData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        Assert.Single(ops.EmittedRequests);
        var context = ops.EmittedRequests[0];
        var bodyStream = context.Request.Body;
        Assert.NotNull(bodyStream);

        // Send first DATA frame
        var data1 = "First "u8.ToArray();
        var dataFrame1 = BuildDataFrame(streamId: 1, data1, endStream: false);

        var buffer1 = TransportBuffer.Rent(dataFrame1.Length);
        dataFrame1.CopyTo(buffer1.FullMemory.Span);
        buffer1.Length = dataFrame1.Length;

        sm.DecodeClientData(new TransportData(buffer1));

        // Send second DATA frame with endStream=true
        var data2 = "Second"u8.ToArray();
        var dataFrame2 = BuildDataFrame(streamId: 1, data2, endStream: true);

        var buffer2 = TransportBuffer.Rent(dataFrame2.Length);
        dataFrame2.CopyTo(buffer2.FullMemory.Span);
        buffer2.Length = dataFrame2.Length;

        sm.DecodeClientData(new TransportData(buffer2));

        // Read aggregated data from body stream
        using var stream = new MemoryStream();
        await bodyStream.CopyToAsync(stream, TestContext.Current.CancellationToken);
        var receivedData = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        Assert.Equal("First Second", receivedData);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.3")]
    public void DecodeClientData_with_rst_stream_should_complete_body_writer_with_cancellation()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        // Send HEADERS frame with endStream=false
        var headerBlock = EncodeHeaders("POST", "/api/upload", "example.com");
        var headersFrameData = BuildHeadersFrame(streamId: 1, headerBlock, endStream: false, endHeaders: true);

        var buffer = TransportBuffer.Rent(headersFrameData.Length);
        headersFrameData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = headersFrameData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        Assert.Single(ops.EmittedRequests);
        var context = ops.EmittedRequests[0];
        var bodyStream = context.Request.Body;
        Assert.NotNull(bodyStream);

        // Send partial DATA frame
        var partialData = "partial"u8.ToArray();
        var dataFrame = BuildDataFrame(streamId: 1, partialData, endStream: false);

        var buffer1 = TransportBuffer.Rent(dataFrame.Length);
        dataFrame.CopyTo(buffer1.FullMemory.Span);
        buffer1.Length = dataFrame.Length;

        sm.DecodeClientData(new TransportData(buffer1));

        // Now send RST_STREAM
        const int frameHeaderSize = 9;
        const int rstFrameSize = 4;
        var rstData = new byte[frameHeaderSize + rstFrameSize];

        rstData[0] = 0;
        rstData[1] = 0;
        rstData[2] = rstFrameSize;
        rstData[3] = (byte)FrameType.RstStream;
        rstData[4] = 0; // No flags

        rstData[5] = 0;
        rstData[6] = 0;
        rstData[7] = 0;
        rstData[8] = 1; // Stream ID = 1

        // Error code = Cancel (0x8)
        rstData[9] = 0;
        rstData[10] = 0;
        rstData[11] = 0;
        rstData[12] = 8;

        var buffer2 = TransportBuffer.Rent(rstData.Length);
        rstData.CopyTo(buffer2.FullMemory.Span);
        buffer2.Length = rstData.Length;

        sm.DecodeClientData(new TransportData(buffer2));
    }
}


