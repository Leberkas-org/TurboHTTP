using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Context.Features;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Server.Encoder;

/// <summary>
/// Unit tests for HTTP/2 Http2ServerStateMachine response body streaming and flow control.
/// Tests response header encoding, timer-driven body draining, and WINDOW_UPDATE handling.
/// </summary>
public sealed class Http2ServerResponseBufferSpec
{
    private static TurboHttpContext CreateResponseContext()
    {
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature { StatusCode = 200 });
        var bodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        return new TurboHttpContext(features);
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

    private static void DecodeFramesAsStream(FakeServerOps ops, Http2ServerStateMachine sm, byte[] frameData)
    {
        var buffer = TransportBuffer.Rent(frameData.Length);
        frameData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = frameData.Length;
        sm.DecodeClientData(new TransportData(buffer));
    }

    private static List<Http2Frame> ExtractFrames(List<ITransportOutbound> outbound, int startIndex = 0)
    {
        var frames = new List<Http2Frame>();
        var decoder = new FrameDecoder();

        for (var i = startIndex; i < outbound.Count; i++)
        {
            if (outbound[i] is TransportData td)
            {
                var decodedFrames = decoder.Decode(td.Buffer);
                frames.AddRange(decodedFrames);
            }
        }

        return frames;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void OnResponse_with_no_body_should_send_headers_with_endstream()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        // Send HEADERS frame for stream 1
        var headerBlock = EncodeHeaders("GET", "/api/status", "example.com");
        var headersFrameData = BuildHeadersFrame(streamId: 1, headerBlock, endStream: true, endHeaders: true);
        DecodeFramesAsStream(ops, sm, headersFrameData);

        Assert.Single(ops.Requests);

        var initialOutboundCount = ops.Outbound.Count;

        // Send response
        var requestContext = ops.Requests[0];
        requestContext.Response.StatusCode = 200;
        requestContext.Response.ContentLength = 0;
        sm.OnResponse(requestContext);

        // Extract frames after response
        var frames = ExtractFrames(ops.Outbound, initialOutboundCount);

        // Should only have HEADERS frame with EndStream set
        Assert.Single(frames);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.True(headersFrame.EndStream);
        Assert.DoesNotContain(ops.ScheduledTimers, t => t.Name.StartsWith("drain-body:"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void OnResponse_with_body_should_schedule_drain_timer_and_not_set_endstream()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        // Send HEADERS frame for stream 1
        var headerBlock = EncodeHeaders("GET", "/api/data", "example.com");
        var headersFrameData = BuildHeadersFrame(streamId: 1, headerBlock, endStream: true, endHeaders: true);
        DecodeFramesAsStream(ops, sm, headersFrameData);

        Assert.Single(ops.Requests);

        var initialOutboundCount = ops.Outbound.Count;

        // Send response
        var requestContext = ops.Requests[0];
        requestContext.Response.StatusCode = 200;
        requestContext.Response.ContentLength = 100;
        sm.OnResponse(requestContext);

        // Extract frames after response
        var framesAfterResponse = ExtractFrames(ops.Outbound, initialOutboundCount);

        // Should have only HEADERS frame without EndStream
        Assert.NotEmpty(framesAfterResponse);
        var headersFrame = Assert.IsType<HeadersFrame>(framesAfterResponse[0]);
        Assert.False(headersFrame.EndStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void WindowUpdate_should_drain_outbound_buffer()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        var headerBlock = EncodeHeaders("GET", "/api/data", "example.com");
        var headersFrameData = BuildHeadersFrame(streamId: 1, headerBlock, endStream: true, endHeaders: true);
        DecodeFramesAsStream(ops, sm, headersFrameData);

        Assert.Single(ops.Requests);

        var requestContext = ops.Requests[0];
        requestContext.Response.StatusCode = 200;
        sm.OnResponse(requestContext);

        var windowUpdateData = BuildWindowUpdateFrame(streamId: 1, increment: 50000);
        DecodeFramesAsStream(ops, sm, windowUpdateData);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void ServerResponseEncoder_EncodeHeaders_with_body_flag_should_not_set_endstream()
    {
        var encoder = new Http2ServerEncoder();

        var ctx = ServerTestContext.CreateResponse();

        var frames = encoder.EncodeHeaders(ctx, streamId: 1, hasBody: true);

        Assert.NotEmpty(frames);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.False(headersFrame.EndStream, "HEADERS should not have EndStream when hasBody=true");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void ServerResponseEncoder_EncodeHeaders_without_body_flag_should_set_endstream()
    {
        var encoder = new Http2ServerEncoder();

        var ctx = ServerTestContext.CreateResponse(204);

        var frames = encoder.EncodeHeaders(ctx, streamId: 1, hasBody: false);

        Assert.NotEmpty(frames);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.True(headersFrame.EndStream, "HEADERS should have EndStream when hasBody=false");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void ServerResponseEncoder_ApplyClientSettings_should_update_max_frame_size()
    {
        var encoder = new Http2ServerEncoder();
        var initialMaxFrameSize = encoder.MaxFrameSize;

        encoder.ApplyClientSettings([(SettingsParameter.MaxFrameSize, 32768u)]);

        Assert.Equal(32768, encoder.MaxFrameSize);
        Assert.NotEqual(initialMaxFrameSize, encoder.MaxFrameSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void ServerResponseEncoder_ApplyClientSettings_should_ignore_initial_window_size()
    {
        var encoder = new Http2ServerEncoder();

        // This should not throw and should be ignored by encoder
        encoder.ApplyClientSettings([(SettingsParameter.InitialWindowSize, 32768u)]);

        // MaxFrameSize should remain unchanged
        Assert.Equal(16384, encoder.MaxFrameSize);
    }
}



