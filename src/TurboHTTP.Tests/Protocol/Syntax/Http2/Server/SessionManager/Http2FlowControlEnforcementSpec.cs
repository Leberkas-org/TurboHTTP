using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Context.Features;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Protocol.Syntax.Http2.Options;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;
using TurboHTTP.Streams.Stages.Server;


namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Server.SessionManager;

/// <summary>
/// Unit tests for HTTP/2 SessionManager flow control enforcement.
/// Tests WINDOW_UPDATE on stream 0, DATA on closed streams, and empty DATA with END_STREAM.
/// </summary>
public sealed class Http2FlowControlEnforcementSpec
{
    private static RequestContext CreateResponseContext(long streamId)
    {
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature { StatusCode = 200 });
        var bodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<IHttpStreamIdFeature>(new TurboStreamIdFeature(streamId));
        return new RequestContext { Features = features };
    }


    private static byte[] BuildHeadersFrame(int streamId, bool endStream = false)
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<HpackHeader>
        {
            new(":method", "POST"),
            new(":path", "/"),
            new(":scheme", "https"),
            new(":authority", "localhost"),
        };

        var buf = new byte[4096];
        var span = buf.AsSpan();
        var written = encoder.Encode(headers, ref span, useHuffman: false);
        var block = new Memory<byte>(buf, 0, written);

        const int h = 9;
        var frame = new byte[h + block.Length];
        var len = block.Length;
        frame[0] = (byte)(len >> 16);
        frame[1] = (byte)(len >> 8);
        frame[2] = (byte)len;
        frame[3] = (byte)FrameType.Headers;
        byte flags = 0x04; // END_HEADERS
        if (endStream) flags |= 0x01; // END_STREAM
        frame[4] = flags;
        frame[5] = (byte)(streamId >> 24);
        frame[6] = (byte)(streamId >> 16);
        frame[7] = (byte)(streamId >> 8);
        frame[8] = (byte)streamId;
        block.Span.CopyTo(frame.AsSpan(h));
        return frame;
    }

    private static byte[] BuildDataFrame(int streamId, int dataLength, bool endStream = false)
    {
        const int h = 9;
        var frame = new byte[h + dataLength];
        frame[0] = (byte)(dataLength >> 16);
        frame[1] = (byte)(dataLength >> 8);
        frame[2] = (byte)dataLength;
        frame[3] = (byte)FrameType.Data;
        frame[4] = endStream ? (byte)0x01 : (byte)0;
        frame[5] = (byte)(streamId >> 24);
        frame[6] = (byte)(streamId >> 16);
        frame[7] = (byte)(streamId >> 8);
        frame[8] = (byte)streamId;
        return frame;
    }

    private static byte[] BuildWindowUpdateFrame(int streamId, int increment)
    {
        const int h = 9;
        var frame = new byte[h + 4];
        frame[0] = 0;
        frame[1] = 0;
        frame[2] = 4;
        frame[3] = (byte)FrameType.WindowUpdate;
        frame[4] = 0;
        frame[5] = (byte)(streamId >> 24);
        frame[6] = (byte)(streamId >> 16);
        frame[7] = (byte)(streamId >> 8);
        frame[8] = (byte)streamId;
        frame[9] = (byte)(increment >> 24);
        frame[10] = (byte)(increment >> 16);
        frame[11] = (byte)(increment >> 8);
        frame[12] = (byte)increment;
        return frame;
    }

    private static TransportBuffer WrapFrame(byte[] frame)
    {
        var buffer = TransportBuffer.Rent(frame.Length);
        frame.CopyTo(buffer.FullMemory.Span);
        buffer.Length = frame.Length;
        return buffer;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void WindowUpdate_on_stream_0_should_not_crash()
    {
        var ops = new FakeServerOps();
        var encoderOptions = new Http2ServerEncoderOptions();
        var decoderOptions = new Http2ServerDecoderOptions();
        var sm = new Http2ServerSessionManager(encoderOptions, decoderOptions, ops);

        sm.PreStart();
        ops.Outbound.Clear(); // Clear initial SETTINGS frame

        // Send WINDOW_UPDATE on stream 0 (connection level)
        var windowUpdateFrame = BuildWindowUpdateFrame(streamId: 0, increment: 1024);

        // Should not throw
        sm.DecodeClientData(WrapFrame(windowUpdateFrame));

        // No exception means the test passed
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Data_on_closed_stream_should_emit_RstStream()
    {
        var ops = new FakeServerOps();
        var encoderOptions = new Http2ServerEncoderOptions();
        var decoderOptions = new Http2ServerDecoderOptions();
        var sm = new Http2ServerSessionManager(encoderOptions, decoderOptions, ops);

        sm.PreStart();
        ops.Outbound.Clear(); // Clear initial SETTINGS frame
        ops.ScheduledTimers.Clear();

        // Step 1: Open stream 1 with HEADERS(endStream=true)
        var headersFrame = BuildHeadersFrame(streamId: 1, endStream: true);
        sm.DecodeClientData(WrapFrame(headersFrame));

        // Request should be emitted
        Assert.Single(ops.Requests);
        var requestContext = ops.Requests[0];
        var requestStreamIdFeature = requestContext.Features.Get<IHttpStreamIdFeature>();
        var streamId = requestStreamIdFeature?.StreamId ?? 1;

        // Step 2: Send response with ContentLength=0 to close the stream
        requestContext.Response.StatusCode = 200;
        requestContext.Response.ContentLength = 0;
        sm.OnResponse(requestContext);

        // Stream 1 should be closed after response with no body
        Assert.Equal(0, sm.ActiveStreamCount);

        // Clear outbound to count only new frames
        ops.Outbound.Clear();

        // Step 3: Send DATA on closed stream 1
        var dataFrame = BuildDataFrame(streamId: 1, dataLength: 5, endStream: false);
        sm.DecodeClientData(WrapFrame(dataFrame));

        // RST_STREAM should be emitted
        // The RST_STREAM frame is emitted via OnOutbound
        Assert.NotEmpty(ops.Outbound);

        // Find the RST_STREAM frame in the outbound buffer
        var foundRstStream = false;
        foreach (var outbound in ops.Outbound)
        {
            if (outbound is TransportData { Buffer.Length: >= 9 } td)
            {
                var span = td.Buffer.FullMemory.Span;
                // Frame type is at byte 3
                var frameType = (FrameType)span[3];
                if (frameType == FrameType.RstStream)
                {
                    foundRstStream = true;
                    break;
                }
            }
        }

        Assert.True(foundRstStream, "RST_STREAM frame not found in outbound");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.1")]
    public void Empty_data_with_EndStream_should_complete_request_body()
    {
        var ops = new FakeServerOps();
        var encoderOptions = new Http2ServerEncoderOptions();
        var decoderOptions = new Http2ServerDecoderOptions();
        var sm = new Http2ServerSessionManager(encoderOptions, decoderOptions, ops);

        sm.PreStart();
        ops.Outbound.Clear(); // Clear initial SETTINGS frame
        ops.ScheduledTimers.Clear();

        // Open stream 1 with HEADERS(endStream=false)
        var headersFrame = BuildHeadersFrame(streamId: 1, endStream: false);
        sm.DecodeClientData(WrapFrame(headersFrame));

        // Request should be emitted
        Assert.Single(ops.Requests);
        var request = ops.Requests[0];

        // Stream 1 should be open
        Assert.Equal(1, sm.ActiveStreamCount);

        // Send empty DATA(endStream=true) on stream 1 - this completes the request body
        var dataFrame = BuildDataFrame(streamId: 1, dataLength: 0, endStream: true);
        sm.DecodeClientData(WrapFrame(dataFrame));

        // Stream 1 should still be open (waiting for response to be sent)
        Assert.Equal(1, sm.ActiveStreamCount);

        // Request should still be the same
        Assert.Equal(request, ops.Requests[0]);
    }
}
