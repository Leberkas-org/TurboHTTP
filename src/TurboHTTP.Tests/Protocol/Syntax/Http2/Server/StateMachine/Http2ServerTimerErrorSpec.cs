using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Server.StateMachine;

public sealed class Http2ServerTimerErrorSpec
{
    private static IFeatureCollection CreateResponseContext(long streamId = 999)
    {
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature { StatusCode = 200 });
        features.Set<IHttpStreamIdFeature>(new TurboStreamIdFeature(streamId));
        var bodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        return features;
    }

    private static byte[] BuildHeadersFrame(int streamId, bool endStream = true)
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

    private static TransportData WrapAsTransportData(byte[] frameData)
    {
        var buffer = TransportBuffer.Rent(frameData.Length);
        frameData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = frameData.Length;
        return new TransportData(buffer);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void PreStart_should_schedule_keep_alive_timer()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        sm.PreStart();

        // Should have scheduled keep-alive timer
        var keepAliveTimer = ops.ScheduledTimers.FirstOrDefault(t => t.Name == "keep-alive-timeout");
        Assert.NotEqual(default, keepAliveTimer);
        Assert.True(keepAliveTimer.Delay > TimeSpan.Zero, "Keep-alive timeout should be positive");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void OnTimerFired_keep_alive_should_emit_GoAway()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        sm.PreStart();
        ops.Outbound.Clear();

        sm.OnTimerFired("keep-alive-timeout");

        Assert.Single(ops.Outbound);
        var outbound = ops.Outbound[0];
        Assert.IsType<TransportData>(outbound);

        var transportData = (TransportData)outbound;
        var frameData = transportData.Buffer.Span;

        // Frame type should be GOAWAY (0x7)
        Assert.Equal((byte)FrameType.GoAway, frameData[3]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void ShouldComplete_should_always_be_false()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        Assert.False(sm.ShouldComplete);

        sm.PreStart();
        Assert.False(sm.ShouldComplete);

        // Decode a HEADERS frame to open a stream
        var headersFrame = BuildHeadersFrame(streamId: 1, endStream: true);
        var transportData = WrapAsTransportData(headersFrame);
        sm.DecodeClientData(transportData);

        // ShouldComplete should still be false for HTTP/2
        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void DecodeClientData_should_cancel_keep_alive_when_streams_open()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        sm.PreStart();
        ops.CancelledTimers.Clear();

        // Decode a HEADERS frame to open a stream
        var headersFrame = BuildHeadersFrame(streamId: 1, endStream: false);
        var transportData = WrapAsTransportData(headersFrame);
        sm.DecodeClientData(transportData);

        // Keep-alive timer should be cancelled when streams open
        Assert.Contains("keep-alive-timeout", ops.CancelledTimers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.3")]
    public void OnTimerFired_headers_timeout_should_emit_RstStream()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        sm.PreStart();
        ops.Outbound.Clear();

        sm.OnTimerFired("headers-timeout:1");

        Assert.Single(ops.Outbound);
        var outbound = ops.Outbound[0];
        Assert.IsType<TransportData>(outbound);

        var transportData = (TransportData)outbound;
        var frameData = transportData.Buffer.Span;

        // Frame type should be RST_STREAM (0x3)
        Assert.Equal((byte)FrameType.RstStream, frameData[3]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Cleanup_should_be_idempotent()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        sm.PreStart();

        // Should not throw when called multiple times
        sm.Cleanup();
        sm.Cleanup();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void OnResponse_for_unknown_stream_should_not_crash()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        sm.PreStart();

        // Should not throw when responding on unknown stream
        var context = CreateResponseContext();
        sm.OnResponse(context);
    }
}