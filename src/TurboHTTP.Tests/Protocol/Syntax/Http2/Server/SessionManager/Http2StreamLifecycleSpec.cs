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
/// Unit tests for HTTP/2 SessionManager stream lifecycle and max concurrent streams.
/// Tests stream creation, closure, RST_STREAM handling, and max concurrent stream limits.
/// </summary>
public sealed class Http2StreamLifecycleSpec
{
    private static IFeatureCollection CreateResponseContext(long streamId = 99)
    {
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature { StatusCode = 200 });
        var bodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<IHttpStreamIdFeature>(new TurboStreamIdFeature(streamId));
        return features;
    }


    private static byte[] BuildHeadersFrame(int streamId, bool endStream = false)
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<HpackHeader>
        {
            new(":method", "GET"),
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

    private static byte[] BuildRstStreamFrame(int streamId, uint errorCode)
    {
        const int h = 9;
        var frame = new byte[h + 4];
        frame[0] = 0;
        frame[1] = 0;
        frame[2] = 4;
        frame[3] = (byte)FrameType.RstStream;
        frame[4] = 0;
        frame[5] = (byte)(streamId >> 24);
        frame[6] = (byte)(streamId >> 16);
        frame[7] = (byte)(streamId >> 8);
        frame[8] = (byte)streamId;
        frame[9] = (byte)(errorCode >> 24);
        frame[10] = (byte)(errorCode >> 16);
        frame[11] = (byte)(errorCode >> 8);
        frame[12] = (byte)errorCode;
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
    [Trait("RFC", "RFC9113-5.1.2")]
    public void Should_accept_streams_up_to_max_concurrent()
    {
        var ops = new FakeServerOps();
        var encoderOptions = new Http2ServerEncoderOptions();
        var decoderOptions = new Http2ServerDecoderOptions { MaxConcurrentStreams = 2 };
        var sm = new Http2ServerSessionManager(encoderOptions, decoderOptions, ops);

        sm.PreStart();
        ops.Outbound.Clear(); // Clear initial SETTINGS frame
        ops.ScheduledTimers.Clear();

        // Step 1: Send HEADERS on stream 1 with endStream=true
        var headers1 = BuildHeadersFrame(streamId: 1, endStream: true);
        sm.DecodeClientData(WrapFrame(headers1));

        // Stream 1 should be accepted
        Assert.Single(ops.Requests);
        Assert.Equal(1, sm.ActiveStreamCount);

        // Step 2: Send HEADERS on stream 3 with endStream=true
        var headers3 = BuildHeadersFrame(streamId: 3, endStream: true);
        sm.DecodeClientData(WrapFrame(headers3));

        // Stream 3 should be accepted (we're at max=2 concurrent)
        Assert.Equal(2, ops.Requests.Count);
        Assert.Equal(2, sm.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public void Should_refuse_stream_above_max_concurrent()
    {
        var ops = new FakeServerOps();
        var encoderOptions = new Http2ServerEncoderOptions();
        var decoderOptions = new Http2ServerDecoderOptions { MaxConcurrentStreams = 1 };
        var sm = new Http2ServerSessionManager(encoderOptions, decoderOptions, ops);

        sm.PreStart();
        ops.Outbound.Clear(); // Clear initial SETTINGS frame
        ops.ScheduledTimers.Clear();

        // Step 1: Send HEADERS on stream 1 with endStream=false (stream stays open)
        var headers1 = BuildHeadersFrame(streamId: 1, endStream: false);
        sm.DecodeClientData(WrapFrame(headers1));

        // Stream 1 should be accepted and stay open
        Assert.Single(ops.Requests);
        Assert.Equal(1, sm.ActiveStreamCount);

        // Clear outbound to detect the RST_STREAM for stream 3
        ops.Outbound.Clear();

        // Step 2: Send HEADERS on stream 3 (should be refused)
        var headers3 = BuildHeadersFrame(streamId: 3, endStream: false);
        sm.DecodeClientData(WrapFrame(headers3));

        // No new request should be emitted for stream 3
        Assert.Single(ops.Requests);

        // RST_STREAM should be emitted
        Assert.NotEmpty(ops.Outbound);
        var foundRstStream = false;
        foreach (var outbound in ops.Outbound)
        {
            if (outbound is TransportData { Buffer.Length: >= 9 } td)
            {
                var span = td.Buffer.FullMemory.Span;
                var frameType = (FrameType)span[3];
                if (frameType == FrameType.RstStream)
                {
                    foundRstStream = true;
                    break;
                }
            }
        }

        Assert.True(foundRstStream, "RST_STREAM frame not found");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void RstStream_on_active_stream_should_close_it()
    {
        var ops = new FakeServerOps();
        var encoderOptions = new Http2ServerEncoderOptions();
        var decoderOptions = new Http2ServerDecoderOptions();
        var sm = new Http2ServerSessionManager(encoderOptions, decoderOptions, ops);

        sm.PreStart();
        ops.Outbound.Clear();
        ops.ScheduledTimers.Clear();

        // Step 1: Send HEADERS on stream 1
        var headersFrame = BuildHeadersFrame(streamId: 1, endStream: false);
        sm.DecodeClientData(WrapFrame(headersFrame));

        // Stream 1 should be active
        Assert.Single(ops.Requests);
        Assert.Equal(1, sm.ActiveStreamCount);

        // Step 2: Send RST_STREAM on stream 1
        var rstFrame = BuildRstStreamFrame(streamId: 1, errorCode: 0u);
        sm.DecodeClientData(WrapFrame(rstFrame));

        // Stream should be closed
        Assert.Equal(0, sm.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void RstStream_on_closed_stream_should_not_crash()
    {
        var ops = new FakeServerOps();
        var encoderOptions = new Http2ServerEncoderOptions();
        var decoderOptions = new Http2ServerDecoderOptions();
        var sm = new Http2ServerSessionManager(encoderOptions, decoderOptions, ops);

        sm.PreStart();
        ops.Outbound.Clear();
        ops.ScheduledTimers.Clear();

        // Send RST_STREAM on stream 99 (never opened)
        var rstFrame = BuildRstStreamFrame(streamId: 99, errorCode: 0u);

        // Should not throw
        sm.DecodeClientData(WrapFrame(rstFrame));

        // No request should be emitted
        Assert.Empty(ops.Requests);
        Assert.Equal(0, sm.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void Headers_with_EndStream_true_should_emit_request_immediately()
    {
        var ops = new FakeServerOps();
        var encoderOptions = new Http2ServerEncoderOptions();
        var decoderOptions = new Http2ServerDecoderOptions();
        var sm = new Http2ServerSessionManager(encoderOptions, decoderOptions, ops);

        sm.PreStart();
        ops.Outbound.Clear();
        ops.ScheduledTimers.Clear();

        // Send HEADERS with endStream=true
        var headersFrame = BuildHeadersFrame(streamId: 1, endStream: true);
        sm.DecodeClientData(WrapFrame(headersFrame));

        // Exactly one request should be emitted
        Assert.Single(ops.Requests);
        var context = ops.Requests[0];

        // Request should have stream ID set
        var streamIdFeature = context.Get<IHttpStreamIdFeature>();
        Assert.NotNull(streamIdFeature);
        Assert.Equal(1, streamIdFeature.StreamId);
    }

    [Fact(Timeout = 5000)]
    public void Cleanup_should_be_idempotent()
    {
        var ops = new FakeServerOps();
        var encoderOptions = new Http2ServerEncoderOptions();
        var decoderOptions = new Http2ServerDecoderOptions();
        var sm = new Http2ServerSessionManager(encoderOptions, decoderOptions, ops);

        sm.PreStart();
        ops.Outbound.Clear();
        ops.ScheduledTimers.Clear();

        // Send HEADERS on stream 1
        var headersFrame = BuildHeadersFrame(streamId: 1, endStream: false);
        sm.DecodeClientData(WrapFrame(headersFrame));

        // Stream should be active
        Assert.Equal(1, sm.ActiveStreamCount);

        // First cleanup
        sm.Cleanup();
        Assert.Equal(0, sm.ActiveStreamCount);

        // Second cleanup (should not crash)
        sm.Cleanup();
        Assert.Equal(0, sm.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    public void OnResponse_for_unknown_stream_should_not_crash()
    {
        var ops = new FakeServerOps();
        var encoderOptions = new Http2ServerEncoderOptions();
        var decoderOptions = new Http2ServerDecoderOptions();
        var sm = new Http2ServerSessionManager(encoderOptions, decoderOptions, ops);

        sm.PreStart();
        ops.Outbound.Clear();
        ops.ScheduledTimers.Clear();

        // Should not throw when responding on unknown stream
        var context = CreateResponseContext();
        sm.OnResponse(context);

        // No crash, test passes
    }
}
