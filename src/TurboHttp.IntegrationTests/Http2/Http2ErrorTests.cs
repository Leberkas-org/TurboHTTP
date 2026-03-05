using System.Buffers.Binary;
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
        var session = new Http2IntegrationSession();

        // Build a GOAWAY frame: lastStreamId=0, PROTOCOL_ERROR
        var goAwayBytes = Http2FrameUtils.EncodeGoAway(0, Http2ErrorCode.ProtocolError, "test error");

        var frames = session.Process(goAwayBytes.AsMemory());

        Assert.NotEmpty(frames);
        Assert.NotNull(session.GoAwayFrame);
        Assert.Equal(Http2ErrorCode.ProtocolError, session.GoAwayFrame!.ErrorCode);
    }

    [Fact(DisplayName = "IT-2-081: Decoder parses GOAWAY with ENHANCE_YOUR_CALM from server")]
    public void Should_ParseGoAway_When_DecoderReceivesGoAwayWithEnhanceYourCalm()
    {
        var session = new Http2IntegrationSession();

        var goAwayBytes = Http2FrameUtils.EncodeGoAway(0, Http2ErrorCode.EnhanceYourCalm);

        var frames = session.Process(goAwayBytes.AsMemory());

        Assert.NotEmpty(frames);
        Assert.NotNull(session.GoAwayFrame);
        Assert.Equal(Http2ErrorCode.EnhanceYourCalm, session.GoAwayFrame!.ErrorCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2-082: Client sends GOAWAY NO_ERROR — connection then closed cleanly")]
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
        var session = new Http2IntegrationSession();

        var rstBytes = Http2FrameUtils.EncodeRstStream(1, Http2ErrorCode.Cancel);

        var frames = session.Process(rstBytes.AsMemory());

        Assert.NotEmpty(frames);
        Assert.Single(session.RstStreams);
        Assert.Equal(1, session.RstStreams[0].StreamId);
        Assert.Equal(Http2ErrorCode.Cancel, session.RstStreams[0].Error);
    }

    [Fact(DisplayName = "IT-2-084: Decoder parses RST_STREAM with STREAM_CLOSED error code")]
    public void Should_ParseRstStream_When_DecoderReceivesRstStreamStreamClosed()
    {
        var session = new Http2IntegrationSession();

        var rstBytes = Http2FrameUtils.EncodeRstStream(3, Http2ErrorCode.StreamClosed);

        var frames = session.Process(rstBytes.AsMemory());

        Assert.NotEmpty(frames);
        Assert.Single(session.RstStreams);
        Assert.Equal(3, session.RstStreams[0].StreamId);
        Assert.Equal(Http2ErrorCode.StreamClosed, session.RstStreams[0].Error);
    }

    // ── Invalid Stream IDs ────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2-085: DATA frame on stream ID 0 → decoder throws Http2Exception PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_DataFrameOnStream0()
    {
        var session = new Http2IntegrationSession();

        // Build a DATA frame with stream ID 0 (invalid per RFC 7540 §6.1).
        var frameBytes = new byte[9 + 4];
        Http2FrameTestWriter.WriteDataFrame(frameBytes, streamId: 0, "test"u8, endStream: false);

        var ex = Assert.Throws<Http2Exception>(() =>
            session.Process(frameBytes.AsMemory()));

        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "IT-2-086: HEADERS frame with even stream ID (server not promised) → PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_HeadersFrameHasEvenStreamId()
    {
        // The decoder rejects HEADERS on even stream IDs that were not pre-announced via PUSH_PROMISE.
        var session = new Http2IntegrationSession();

        // Build a minimal HEADERS frame with stream ID 2 (even — server-initiated, no PUSH_PROMISE).
        // We need a valid HPACK header block: at minimum an indexed :status header.
        // HPACK index 8 = ":status: 200" in the static table.
        var headerBlock = new byte[] { 0x88 }; // indexed header, index 8 = :status 200

        var frameBytes = new byte[9 + headerBlock.Length];
        Http2FrameTestWriter.WriteHeadersFrame(
            frameBytes,
            streamId: 2,
            headerBlock: headerBlock,
            endStream: true,
            endHeaders: true);

        var ex = Assert.Throws<Http2Exception>(() =>
            session.Process(frameBytes.AsMemory()));

        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "IT-2-087: HEADERS on previously closed stream → decoder throws STREAM_CLOSED (RFC 7540 §6.2)")]
    public void Should_ThrowStreamClosed_When_HeadersSentOnClosedStream()
    {
        var session = new Http2IntegrationSession();

        // First, create a closed stream by sending HEADERS with END_STREAM + END_HEADERS.
        // HPACK index 8 = :status 200.
        var headerBlock = new byte[] { 0x88 };
        var frame1 = new byte[9 + headerBlock.Length];
        Http2FrameTestWriter.WriteHeadersFrame(frame1, streamId: 1, headerBlock, endStream: true, endHeaders: true);
        session.Process(frame1.AsMemory()); // closes stream 1

        // RFC 7540 §6.2: HEADERS on a closed stream is a connection error of type STREAM_CLOSED.
        var frame2 = new byte[9 + headerBlock.Length];
        Http2FrameTestWriter.WriteHeadersFrame(frame2, streamId: 1, headerBlock, endStream: true, endHeaders: true);

        var ex = Assert.Throws<Http2Exception>(() =>
            session.Process(frame2.AsMemory()));

        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    // ── SETTINGS Violations ───────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2-088: SETTINGS with ENABLE_PUSH=2 → decoder throws PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_EnablePushSetToInvalidValue()
    {
        var session = new Http2IntegrationSession();

        // Build SETTINGS frame with ENABLE_PUSH=2 (invalid; only 0 or 1 are valid).
        var settingsBytes = Http2FrameUtils.EncodeSettings(
        [
            (SettingsParameter.EnablePush, 2u)
        ]);

        var ex = Assert.Throws<Http2Exception>(() =>
            session.Process(settingsBytes.AsMemory()));

        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "IT-2-089: SETTINGS ACK with non-empty payload → decoder throws FRAME_SIZE_ERROR")]
    public void Should_ThrowFrameSizeError_When_SettingsAckHasNonEmptyPayload()
    {
        var session = new Http2IntegrationSession();

        // Build a SETTINGS ACK frame with a non-empty payload (invalid).
        var frameBytes = new byte[9 + 6]; // 9 header + 6 payload
        // Frame header: length=6, type=SETTINGS(0x4), flags=ACK(0x1), streamId=0
        frameBytes[0] = 0; frameBytes[1] = 0; frameBytes[2] = 6; // length = 6
        frameBytes[3] = (byte)FrameType.Settings;
        frameBytes[4] = (byte)Settings.Ack;
        // stream ID = 0 (already zero from array init)

        var ex = Assert.Throws<Http2Exception>(() =>
            session.Process(frameBytes.AsMemory()));

        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
    }

    // ── WINDOW_UPDATE ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2-090: WINDOW_UPDATE increment of 0 → decoder throws PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_WindowUpdateIncrementIsZero()
    {
        var session = new Http2IntegrationSession();

        // Build a WINDOW_UPDATE frame with increment = 0 (forbidden per RFC 7540 §6.9).
        var frameBytes = new byte[9 + 4];
        frameBytes[0] = 0; frameBytes[1] = 0; frameBytes[2] = 4; // length = 4
        frameBytes[3] = (byte)FrameType.WindowUpdate;
        // flags = 0, stream = 0, increment = 0 (all zero from array init)

        var ex = Assert.Throws<Http2Exception>(() =>
            session.Process(frameBytes.AsMemory()));

        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    // ── Unknown Frame Type ────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2-091: Unknown frame type 0xFF — decoder ignores it (RFC 7540 §4.1)")]
    public void Should_IgnoreUnknownFrameType_When_Received()
    {
        // RFC 7540 §4.1: Implementations MUST ignore unknown frame types.
        var session = new Http2IntegrationSession();

        // Build a frame with type 0xFF (unknown), length 4, stream 1.
        var frameBytes = new byte[9 + 4];
        frameBytes[0] = 0; frameBytes[1] = 0; frameBytes[2] = 4; // length = 4
        frameBytes[3] = 0xFF; // unknown type
        // flags = 0, stream ID = 1
        BinaryPrimitives.WriteUInt32BigEndian(frameBytes.AsSpan(5), 1);

        // Should not throw — unknown frames are silently ignored.
        session.Process(frameBytes.AsMemory());
        // No responses, no control frames, no error.
        Assert.Empty(session.Responses);
        Assert.Null(session.GoAwayFrame);
    }

    // ── Server-initiated RST ──────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2-092: Server-initiated RST_STREAM decoded cleanly — no exception from decoder")]
    public void Should_DecodeRstStreamCleanly_When_ServerSendsIt()
    {
        var session = new Http2IntegrationSession();

        var rstBytes = Http2FrameUtils.EncodeRstStream(1, Http2ErrorCode.RefusedStream);

        // Should not throw — RST_STREAM is a valid frame type.
        var frames = session.Process(rstBytes.AsMemory());

        Assert.NotEmpty(frames);
        Assert.Single(session.RstStreams);
        Assert.Equal(Http2ErrorCode.RefusedStream, session.RstStreams[0].Error);
    }

    // ── Response without :status ──────────────────────────────────────────────

    [Fact(DisplayName = "IT-2-093: Response HEADERS without :status → decoder throws PROTOCOL_ERROR (RFC 9113 §8.3.2)")]
    public void Should_ThrowProtocolError_When_StatusPseudoHeaderMissing()
    {
        // RFC 9113 §8.3.2: Responses MUST include exactly one :status pseudo-header.
        // Absence of :status is a PROTOCOL_ERROR — the connection must be terminated.
        var session = new Http2IntegrationSession();

        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var headerBlock = hpackEncoder.Encode([("content-type", "text/plain")]); // no :status

        var frameBytes = new byte[9 + headerBlock.Length];
        Http2FrameTestWriter.WriteHeadersFrame(
            frameBytes, streamId: 1, headerBlock.Span,
            endStream: true, endHeaders: true);

        var ex = Assert.Throws<Http2Exception>(() =>
            session.Process(frameBytes.AsMemory()));

        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    // ── Frame size exceeded ───────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2-094: Frame payload exceeds MAX_FRAME_SIZE → decoder throws FRAME_SIZE_ERROR")]
    public void Should_ThrowFrameSizeError_When_PayloadExceedsMaxFrameSize()
    {
        var session = new Http2IntegrationSession();

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
            session.Process(frameBytes.AsMemory()));

        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
    }
}
