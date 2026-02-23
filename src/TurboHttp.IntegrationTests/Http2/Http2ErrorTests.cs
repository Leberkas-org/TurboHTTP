using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.Protocol;

namespace TurboHttp.IntegrationTests.Http2;

/// <summary>
/// Phase 15 — HTTP/2 Integration Tests: Error handling and protocol violations.
/// Tests cover GOAWAY, RST_STREAM, invalid frame types, SETTINGS violations,
/// stream-ID rules, and pseudo-header validation.
/// </summary>
[Collection("Http2Integration")]
public sealed class Http2ErrorTests
{
    private readonly KestrelH2Fixture _fixture;

    public Http2ErrorTests(KestrelH2Fixture fixture)
    {
        _fixture = fixture;
    }

    // ── GOAWAY ────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2-080: Decoder parses GOAWAY with PROTOCOL_ERROR from server")]
    public void Should_ParseGoAway_When_DecoderReceivesGoAwayWithProtocolError()
    {
        var decoder = new Http2Decoder();

        // Build a GOAWAY frame: lastStreamId=0, PROTOCOL_ERROR
        var goAwayBytes = Http2Encoder.EncodeGoAway(0, Http2ErrorCode.ProtocolError, "test error");

        var decoded = decoder.TryDecode(goAwayBytes.AsMemory(), out var result);

        Assert.True(decoded);
        Assert.NotNull(result.GoAway);
        Assert.Equal(Http2ErrorCode.ProtocolError, result.GoAway!.ErrorCode);
    }

    [Fact(DisplayName = "IT-2-081: Decoder parses GOAWAY with ENHANCE_YOUR_CALM from server")]
    public void Should_ParseGoAway_When_DecoderReceivesGoAwayWithEnhanceYourCalm()
    {
        var decoder = new Http2Decoder();

        var goAwayBytes = Http2Encoder.EncodeGoAway(0, Http2ErrorCode.EnhanceYourCalm);

        var decoded = decoder.TryDecode(goAwayBytes.AsMemory(), out var result);

        Assert.True(decoded);
        Assert.NotNull(result.GoAway);
        Assert.Equal(Http2ErrorCode.EnhanceYourCalm, result.GoAway!.ErrorCode);
    }

    [Fact(DisplayName = "IT-2-082: Client sends GOAWAY NO_ERROR — connection then closed cleanly")]
    public async Task Should_CloseCleanly_When_ClientSendsGoAwayNoError()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        // Complete a request.
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping"));
        await conn.SendAndReceiveAsync(request);
        // Send GOAWAY then dispose.
        await conn.SendGoAwayAsync(1, Http2ErrorCode.NoError);
        // No exception expected.
    }

    // ── RST_STREAM ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2-083: Decoder parses RST_STREAM with CANCEL error code")]
    public void Should_ParseRstStream_When_DecoderReceivesRstStreamCancel()
    {
        var decoder = new Http2Decoder();

        var rstBytes = Http2Encoder.EncodeRstStream(1, Http2ErrorCode.Cancel);

        var decoded = decoder.TryDecode(rstBytes.AsMemory(), out var result);

        Assert.True(decoded);
        Assert.Single(result.RstStreams);
        Assert.Equal(1, result.RstStreams[0].StreamId);
        Assert.Equal(Http2ErrorCode.Cancel, result.RstStreams[0].Error);
    }

    [Fact(DisplayName = "IT-2-084: Decoder parses RST_STREAM with STREAM_CLOSED error code")]
    public void Should_ParseRstStream_When_DecoderReceivesRstStreamStreamClosed()
    {
        var decoder = new Http2Decoder();

        var rstBytes = Http2Encoder.EncodeRstStream(3, Http2ErrorCode.StreamClosed);

        var decoded = decoder.TryDecode(rstBytes.AsMemory(), out var result);

        Assert.True(decoded);
        Assert.Single(result.RstStreams);
        Assert.Equal(3, result.RstStreams[0].StreamId);
        Assert.Equal(Http2ErrorCode.StreamClosed, result.RstStreams[0].Error);
    }

    // ── Invalid Stream IDs ────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2-085: DATA frame on stream ID 0 → decoder throws Http2Exception PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_DataFrameOnStream0()
    {
        var decoder = new Http2Decoder();

        // Build a DATA frame with stream ID 0 (invalid per RFC 7540 §6.1).
        var frameBytes = new byte[9 + 4];
        Http2FrameWriter.WriteDataFrame(frameBytes, streamId: 0, "test"u8, endStream: false);

        var ex = Assert.Throws<Http2Exception>(() =>
            decoder.TryDecode(frameBytes.AsMemory(), out _));

        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "IT-2-086: HEADERS frame with even stream ID (server not promised) → PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_HeadersFrameHasEvenStreamId()
    {
        // The decoder rejects HEADERS on even stream IDs that were not pre-announced via PUSH_PROMISE.
        var decoder = new Http2Decoder();

        // Build a minimal HEADERS frame with stream ID 2 (even — server-initiated, no PUSH_PROMISE).
        // We need a valid HPACK header block: at minimum an indexed :status header.
        // HPACK index 8 = ":status: 200" in the static table.
        var headerBlock = new byte[] { 0x88 }; // indexed header, index 8 = :status 200

        var frameBytes = new byte[9 + headerBlock.Length];
        Http2FrameWriter.WriteHeadersFrame(
            frameBytes,
            streamId: 2,
            headerBlock: headerBlock,
            endStream: true,
            endHeaders: true);

        var ex = Assert.Throws<Http2Exception>(() =>
            decoder.TryDecode(frameBytes.AsMemory(), out _));

        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "IT-2-087: HEADERS on previously closed stream → decoder throws PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_HeadersSentOnClosedStream()
    {
        var decoder = new Http2Decoder();

        // First, create a closed stream by sending HEADERS with END_STREAM + END_HEADERS.
        // HPACK index 8 = :status 200.
        var headerBlock = new byte[] { 0x88 };
        var frame1 = new byte[9 + headerBlock.Length];
        Http2FrameWriter.WriteHeadersFrame(frame1, streamId: 1, headerBlock, endStream: true, endHeaders: true);
        decoder.TryDecode(frame1.AsMemory(), out _); // closes stream 1

        // Now send another HEADERS on stream 1 (closed) — should throw PROTOCOL_ERROR.
        var frame2 = new byte[9 + headerBlock.Length];
        Http2FrameWriter.WriteHeadersFrame(frame2, streamId: 1, headerBlock, endStream: true, endHeaders: true);

        var ex = Assert.Throws<Http2Exception>(() =>
            decoder.TryDecode(frame2.AsMemory(), out _));

        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    // ── SETTINGS Violations ───────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2-088: SETTINGS with ENABLE_PUSH=2 → decoder throws PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_EnablePushSetToInvalidValue()
    {
        var decoder = new Http2Decoder();

        // Build SETTINGS frame with ENABLE_PUSH=2 (invalid; only 0 or 1 are valid).
        var settingsBytes = Http2Encoder.EncodeSettings(
        [
            (SettingsParameter.EnablePush, 2u)
        ]);

        var ex = Assert.Throws<Http2Exception>(() =>
            decoder.TryDecode(settingsBytes.AsMemory(), out _));

        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "IT-2-089: SETTINGS ACK with non-empty payload → decoder throws FRAME_SIZE_ERROR")]
    public void Should_ThrowFrameSizeError_When_SettingsAckHasNonEmptyPayload()
    {
        var decoder = new Http2Decoder();

        // Build a SETTINGS ACK frame with a non-empty payload (invalid).
        var frameBytes = new byte[9 + 6]; // 9 header + 6 payload
        // Frame header: length=6, type=SETTINGS(0x4), flags=ACK(0x1), streamId=0
        frameBytes[0] = 0; frameBytes[1] = 0; frameBytes[2] = 6; // length = 6
        frameBytes[3] = (byte)FrameType.Settings;
        frameBytes[4] = (byte)SettingsFlags.Ack;
        // stream ID = 0 (already zero from array init)

        var ex = Assert.Throws<Http2Exception>(() =>
            decoder.TryDecode(frameBytes.AsMemory(), out _));

        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
    }

    // ── WINDOW_UPDATE ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2-090: WINDOW_UPDATE increment of 0 → decoder throws PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_WindowUpdateIncrementIsZero()
    {
        var decoder = new Http2Decoder();

        // Build a WINDOW_UPDATE frame with increment = 0 (forbidden per RFC 7540 §6.9).
        var frameBytes = new byte[9 + 4];
        frameBytes[0] = 0; frameBytes[1] = 0; frameBytes[2] = 4; // length = 4
        frameBytes[3] = (byte)FrameType.WindowUpdate;
        // flags = 0, stream = 0, increment = 0 (all zero from array init)

        var ex = Assert.Throws<Http2Exception>(() =>
            decoder.TryDecode(frameBytes.AsMemory(), out _));

        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    // ── Unknown Frame Type ────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2-091: Unknown frame type 0xFF — decoder ignores it (RFC 7540 §4.1)")]
    public void Should_IgnoreUnknownFrameType_When_Received()
    {
        // RFC 7540 §4.1: Implementations MUST ignore unknown frame types.
        var decoder = new Http2Decoder();

        // Build a frame with type 0xFF (unknown), length 4, stream 1.
        var frameBytes = new byte[9 + 4];
        frameBytes[0] = 0; frameBytes[1] = 0; frameBytes[2] = 4; // length = 4
        frameBytes[3] = 0xFF; // unknown type
        // flags = 0, stream ID = 1
        BinaryPrimitives.WriteUInt32BigEndian(frameBytes.AsSpan(5), 1);

        // Should not throw — unknown frames are silently ignored.
        decoder.TryDecode(frameBytes.AsMemory(), out var result);
        // No responses, no control frames, no error.
        Assert.Empty(result.Responses);
        Assert.Null(result.GoAway);
    }

    // ── Server-initiated RST ──────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2-092: Server-initiated RST_STREAM decoded cleanly — no exception from decoder")]
    public void Should_DecodeRstStreamCleanly_When_ServerSendsIt()
    {
        var decoder = new Http2Decoder();

        var rstBytes = Http2Encoder.EncodeRstStream(1, Http2ErrorCode.RefusedStream);

        // Should not throw — RST_STREAM is a valid frame type.
        var decoded = decoder.TryDecode(rstBytes.AsMemory(), out var result);

        Assert.True(decoded);
        Assert.Single(result.RstStreams);
        Assert.Equal(Http2ErrorCode.RefusedStream, result.RstStreams[0].Error);
    }

    // ── Response without :status ──────────────────────────────────────────────

    [Fact(DisplayName = "IT-2-093: Response HEADERS without :status — decoder defaults to 200 OK")]
    public void Should_DefaultTo200_When_StatusPseudoHeaderMissing()
    {
        // RFC 7540 §8.1.2.4: Responses must include :status.
        // Current decoder falls back to 200 if :status is absent (permissive behavior).
        var decoder = new Http2Decoder();

        // A minimal literal header block with just content-type (no :status).
        // Using HPACK literal header representation (index 0, not indexed):
        // 0x00 = new name, not indexed
        // 0x0C = name length 12 = "content-type"
        // 0x04 = value length 4 = "text"
        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var headerBlock = hpackEncoder.Encode([("content-type", "text/plain")]);

        var frameBytes = new byte[9 + headerBlock.Length];
        Http2FrameWriter.WriteHeadersFrame(
            frameBytes, streamId: 1, headerBlock.Span,
            endStream: true, endHeaders: true);

        var decoded = decoder.TryDecode(frameBytes.AsMemory(), out var result);

        Assert.True(decoded);
        Assert.Single(result.Responses);
        // Decoder falls back to 200 when :status is absent.
        Assert.Equal(HttpStatusCode.OK, result.Responses[0].Response.StatusCode);
    }

    // ── Frame size exceeded ───────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2-094: Frame payload exceeds MAX_FRAME_SIZE → decoder throws FRAME_SIZE_ERROR")]
    public void Should_ThrowFrameSizeError_When_PayloadExceedsMaxFrameSize()
    {
        var decoder = new Http2Decoder();

        // The default MAX_FRAME_SIZE is 16384. Build a complete DATA frame with 16385 bytes
        // of payload (header size check runs only once the full payload bytes are available).
        var payloadLength = 16385; // intentionally a variable to allow (byte) cast without CS0221
        var frameBytes = new byte[9 + payloadLength];
        frameBytes[0] = (byte)(payloadLength >> 16);
        frameBytes[1] = (byte)(payloadLength >> 8);
        frameBytes[2] = (byte)payloadLength;
        frameBytes[3] = (byte)FrameType.Data;
        frameBytes[4] = 0;
        BinaryPrimitives.WriteUInt32BigEndian(frameBytes.AsSpan(5), 1); // stream 1
        // Payload bytes are already zero from array init.

        var ex = Assert.Throws<Http2Exception>(() =>
            decoder.TryDecode(frameBytes.AsMemory(), out _));

        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
    }
}
