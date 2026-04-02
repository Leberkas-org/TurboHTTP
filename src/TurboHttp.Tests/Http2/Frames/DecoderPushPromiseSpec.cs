using TurboHttp.Protocol.Http2.Hpack;
using TurboHttp.Protocol.Http2;

namespace TurboHttp.Tests.Http2.Frames;

/// <summary>
/// Tests PUSH_PROMISE frame parsing per RFC 9113 §6.6.
/// Verifies stream reservation, promised stream ID extraction, and header block parsing.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http2FrameDecoder"/>.
/// RFC 9113 §6.6: PUSH_PROMISE reserves a stream and encodes pseudo-headers for the pushed request.
/// </remarks>
public sealed class Http2DecoderPushPromiseSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.6")]
    public void Http2FrameDecoder_should_decode_push_promise_frame()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":path", "/style.css")]);

        var frame = new PushPromiseFrame(1, 2, headerBlock).Serialize();
        var frames = new Http2FrameDecoder().Decode(frame);

        Assert.NotEmpty(frames);
        Assert.IsType<PushPromiseFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.6")]
    public void Http2FrameDecoder_should_extract_promised_stream_id_from_push_promise()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":path", "/asset.js")]);

        var frame = new PushPromiseFrame(1, 4, headerBlock).Serialize();
        var frames = new Http2FrameDecoder().Decode(frame);

        Assert.NotEmpty(frames);
        var ppFrame = Assert.IsType<PushPromiseFrame>(frames[0]);
        Assert.Equal(4, ppFrame.PromisedStreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.6")]
    public void Http2FrameDecoder_should_accept_push_promise_with_empty_header_block()
    {
        var frame = new PushPromiseFrame(1, 2, ReadOnlyMemory<byte>.Empty).Serialize();
        var frames = new Http2FrameDecoder().Decode(frame);

        Assert.NotEmpty(frames);
        Assert.IsType<PushPromiseFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.6")]
    public void Http2FrameDecoder_should_accept_push_promise_on_any_stream_except_zero()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":path", "/resource")]);

        var frame = new PushPromiseFrame(3, 4, headerBlock).Serialize();
        var frames = new Http2FrameDecoder().Decode(frame);

        Assert.NotEmpty(frames);
        var ppFrame = Assert.IsType<PushPromiseFrame>(frames[0]);
        Assert.Equal(3, ppFrame.StreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.6")]
    public void Http2FrameDecoder_should_accept_push_promise_with_large_promised_stream_id()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":path", "/large.css")]);

        var frame = new PushPromiseFrame(1, int.MaxValue - 1, headerBlock).Serialize();
        var frames = new Http2FrameDecoder().Decode(frame);

        Assert.NotEmpty(frames);
        var ppFrame = Assert.IsType<PushPromiseFrame>(frames[0]);
        Assert.Equal(int.MaxValue - 1, ppFrame.PromisedStreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.6")]
    public void Http2FrameDecoder_should_accept_push_promise_with_headers()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode(
        [
            (":method", "GET"),
            (":path", "/style.css"),
            (":scheme", "https"),
            (":authority", "example.com")
        ]);

        var frame = new PushPromiseFrame(1, 2, headerBlock).Serialize();
        var frames = new Http2FrameDecoder().Decode(frame);

        Assert.NotEmpty(frames);
        Assert.IsType<PushPromiseFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.6")]
    public void Http2FrameDecoder_should_accept_push_promise_with_end_headers_flag()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":path", "/resource")]);

        var frame = new PushPromiseFrame(1, 2, headerBlock, endHeaders: true).Serialize();
        var frames = new Http2FrameDecoder().Decode(frame);

        Assert.NotEmpty(frames);
        var ppFrame = Assert.IsType<PushPromiseFrame>(frames[0]);
        Assert.True(ppFrame.EndHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.6")]
    public void Http2FrameDecoder_should_accept_push_promise_without_end_headers_flag()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":path", "/resource")]);

        var frame = new PushPromiseFrame(1, 2, headerBlock, endHeaders: false).Serialize();
        var frames = new Http2FrameDecoder().Decode(frame);

        Assert.NotEmpty(frames);
        var ppFrame = Assert.IsType<PushPromiseFrame>(frames[0]);
        Assert.False(ppFrame.EndHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.6")]
    public void Http2FrameDecoder_should_parse_push_promise_on_stream_zero_without_error()
    {
        // The decoder is a pure wire-format parser; stream ID 0 is a protocol-level violation
        // that a higher-level connection handler must enforce via GOAWAY.
        var headerBlock = new byte[] { 0x82 }; // :method GET from static table
        var payloadLen = 4 + headerBlock.Length;
        var raw = new byte[9 + payloadLen];
        raw[0] = (byte)((payloadLen >> 16) & 0xFF);
        raw[1] = (byte)((payloadLen >> 8) & 0xFF);
        raw[2] = (byte)(payloadLen & 0xFF);
        raw[3] = 0x05; // PUSH_PROMISE
        raw[4] = 0x04; // END_HEADERS
        raw[5] = 0; raw[6] = 0; raw[7] = 0; raw[8] = 0; // stream ID = 0
        raw[9] = 0; raw[10] = 0; raw[11] = 0; raw[12] = 2; // promised stream = 2
        headerBlock.CopyTo(raw.AsSpan(13));

        var frames = new Http2FrameDecoder().Decode(raw);
        Assert.NotEmpty(frames);
        var pp = Assert.IsType<PushPromiseFrame>(frames[0]);
        Assert.Equal(0, pp.StreamId);
    }
}
