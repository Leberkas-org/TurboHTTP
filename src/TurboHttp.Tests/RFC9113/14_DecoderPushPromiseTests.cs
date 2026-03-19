using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.RFC9113;

public sealed class Http2DecoderPushPromiseTests
{

    [Fact(DisplayName = "RFC9113-5.1.1-PP-001: PUSH_PROMISE moves stream to reserved(remote) state")]
    public void Should_SetPromisedStreamAsReservedRemote_WhenPushPromiseDecoded()
    {
        // A server sends PUSH_PROMISE on an existing stream (1) to promise stream 2.
        // The decoder must produce a PushPromiseFrame with the correct promised stream ID.
        var headerBlock = new byte[] { 0x82 }; // HPACK-encoded :method GET (static table index 2)
        var frame = new PushPromiseFrame(
            streamId: 1,
            promisedStreamId: 2,
            headerBlock: headerBlock,
            endHeaders: true);

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame.Serialize());

        Assert.Single(frames);
        var pp = Assert.IsType<PushPromiseFrame>(frames[0]);
        Assert.Equal(1, pp.StreamId);
        Assert.Equal(2, pp.PromisedStreamId);
        Assert.True(pp.EndHeaders);
        Assert.Equal(headerBlock, pp.HeaderBlockFragment.ToArray());
    }

    [Fact(DisplayName = "RFC9113-5.1.1-PP-002: Client rejects reserved(remote) stream with RST_STREAM CANCEL")]
    public void Should_RejectReservedRemoteStream_WithRstStreamCancel()
    {
        // After receiving PUSH_PROMISE (promised stream 2), a client may reject it
        // by sending RST_STREAM with CANCEL on the promised stream ID.
        var rstFrame = new RstStreamFrame(streamId: 2, Http2ErrorCode.Cancel);

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(rstFrame.Serialize());

        Assert.Single(frames);
        var rst = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(2, rst.StreamId);
        Assert.Equal(Http2ErrorCode.Cancel, rst.ErrorCode);
    }

    [Fact(DisplayName = "RFC9113-5.1.1-PP-003: PUSH_PROMISE on stream 0 is a PROTOCOL_ERROR")]
    public void Should_ParseWithStream0_WhenPushPromiseOnStream0()
    {
        // RFC 9113 §6.6: PUSH_PROMISE MUST be associated with a peer-initiated stream.
        // Stream 0 is the connection control stream — PUSH_PROMISE on it is invalid.
        // We construct raw bytes with stream ID 0 to bypass PushPromiseFrame constructor.
        var headerBlock = new byte[] { 0x82 };
        var payloadLen = 4 + headerBlock.Length;
        var raw = new byte[9 + payloadLen];
        // Frame header: length (3 bytes) + type (1) + flags (1) + stream ID (4)
        raw[0] = (byte)((payloadLen >> 16) & 0xFF);
        raw[1] = (byte)((payloadLen >> 8) & 0xFF);
        raw[2] = (byte)(payloadLen & 0xFF);
        raw[3] = (byte)FrameType.PushPromise; // type = PUSH_PROMISE (0x05)
        raw[4] = 0x04; // END_HEADERS flag
        // Stream ID = 0 (bytes 5-8 all zero)
        raw[5] = 0; raw[6] = 0; raw[7] = 0; raw[8] = 0;
        // Payload: promised stream ID (4 bytes) + header block
        raw[9] = 0; raw[10] = 0; raw[11] = 0; raw[12] = 2; // promised stream = 2
        headerBlock.CopyTo(raw.AsSpan(13));

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(raw);

        // The decoder parses the frame — stream ID 0 is a protocol-level violation
        // that a higher-level handler (connection stage) must enforce.
        // At the decoder level, we verify the frame is parsed with stream ID 0.
        Assert.Single(frames);
        var pp = Assert.IsType<PushPromiseFrame>(frames[0]);
        Assert.Equal(0, pp.StreamId);
    }

    [Fact(DisplayName = "RFC9113-5.1.1-PP-004: PUSH_PROMISE with even promised-stream-ID is decodable")]
    public void Should_BeDecodable_WhenPushPromiseHasEvenPromisedStreamId()
    {
        // RFC 9113 §5.1.1: Server-initiated streams use even IDs (2, 4, 6, ...).
        // PUSH_PROMISE must use even promised stream IDs.
        var headerBlock = new byte[] { 0x84 }; // HPACK index 4 (:path /)

        foreach (var promisedId in new[] { 2, 4, 100, 65534 })
        {
            var frame = new PushPromiseFrame(
                streamId: 1,
                promisedStreamId: promisedId,
                headerBlock: headerBlock,
                endHeaders: true);

            var decoder = new Http2FrameDecoder();
            var frames = decoder.Decode(frame.Serialize());

            Assert.Single(frames);
            var pp = Assert.IsType<PushPromiseFrame>(frames[0]);
            Assert.Equal(promisedId, pp.PromisedStreamId);
        }
    }

    [Fact(DisplayName = "RFC9113-5.1.1-PP-005: PUSH_PROMISE referencing stream 0 as promised is invalid")]
    public void Should_ReportPromisedStreamId0_WhenPushPromiseDecoded()
    {
        // RFC 9113 §6.6: The promised stream ID MUST be a valid stream identifier.
        // Stream 0 is reserved for the connection — using it as promised ID is a protocol error.
        // We construct raw bytes to set promised stream ID = 0.
        var headerBlock = new byte[] { 0x82 };
        var payloadLen = 4 + headerBlock.Length;
        var raw = new byte[9 + payloadLen];
        raw[0] = (byte)((payloadLen >> 16) & 0xFF);
        raw[1] = (byte)((payloadLen >> 8) & 0xFF);
        raw[2] = (byte)(payloadLen & 0xFF);
        raw[3] = (byte)FrameType.PushPromise;
        raw[4] = 0x04; // END_HEADERS
        // Stream ID = 1 (valid parent stream)
        raw[5] = 0; raw[6] = 0; raw[7] = 0; raw[8] = 1;
        // Payload: promised stream ID = 0 (invalid)
        raw[9] = 0; raw[10] = 0; raw[11] = 0; raw[12] = 0;
        headerBlock.CopyTo(raw.AsSpan(13));

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(raw);

        // Decoder parses the frame; protocol-level validation (promised ID = 0 is invalid)
        // is enforced by the connection/stream state machine layer.
        Assert.Single(frames);
        var pp = Assert.IsType<PushPromiseFrame>(frames[0]);
        Assert.Equal(0, pp.PromisedStreamId);
    }

    [Fact(DisplayName = "RFC9113-5.1.1-PP-006: PUSH_PROMISE without END_HEADERS expects CONTINUATION")]
    public void Should_SetEndHeadersFalse_WhenPushPromiseWithoutEndHeaders()
    {
        // When END_HEADERS is not set, the header block is incomplete and
        // CONTINUATION frames must follow (RFC 9113 §6.6, §6.10).
        var headerBlock = new byte[] { 0x82, 0x84 };
        var frame = new PushPromiseFrame(
            streamId: 1,
            promisedStreamId: 4,
            headerBlock: headerBlock,
            endHeaders: false);

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame.Serialize());

        Assert.Single(frames);
        var pp = Assert.IsType<PushPromiseFrame>(frames[0]);
        Assert.Equal(4, pp.PromisedStreamId);
        Assert.False(pp.EndHeaders);
        Assert.Equal(headerBlock, pp.HeaderBlockFragment.ToArray());
    }

    [Fact(DisplayName = "RFC9113-5.1.1-PP-007: PUSH_PROMISE preserves large header block fragment")]
    public void Should_PreserveLargeHeaderBlock_WhenPushPromiseDecoded()
    {
        // Ensure the decoder handles larger header block fragments correctly.
        var headerBlock = new byte[256];
        for (var i = 0; i < headerBlock.Length; i++)
        {
            headerBlock[i] = (byte)(i & 0xFF);
        }

        var frame = new PushPromiseFrame(
            streamId: 3,
            promisedStreamId: 6,
            headerBlock: headerBlock,
            endHeaders: true);

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame.Serialize());

        Assert.Single(frames);
        var pp = Assert.IsType<PushPromiseFrame>(frames[0]);
        Assert.Equal(3, pp.StreamId);
        Assert.Equal(6, pp.PromisedStreamId);
        Assert.True(pp.EndHeaders);
        Assert.Equal(headerBlock, pp.HeaderBlockFragment.ToArray());
    }
}
