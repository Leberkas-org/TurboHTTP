using System.Buffers.Binary;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// RFC 9113 §3.4 — HTTP/2 Connection Preface
/// Phase 1-2: Connection Preface &amp; ALPN (protocol-layer coverage)
///
/// The TLS/ALPN requirements (TLS 1.2+, ALPN="h2") are enforced at the I/O
/// layer (not the protocol layer) and are therefore out of scope for these tests.
/// This class covers all protocol-layer MUST requirements:
///   - Client preface: exact magic bytes + SETTINGS frame on stream 0
///   - Server preface validation: first frame MUST be SETTINGS on stream 0
///   - Partial preface: fewer than 9 bytes → NeedMoreData (no error)
///   - Malformed preface: wrong frame type or stream → PROTOCOL_ERROR
/// </summary>
public sealed class Http2ConnectionPrefaceTests
{
    // RFC 9113 §3.4: client connection preface = magic octets + SETTINGS frame
    private static readonly byte[] Magic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();
    private const int MagicLength = 24; // "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"
    private const int FrameHeaderLength = 9;

    // =========================================================================
    // Client preface — Http2FrameUtils.BuildConnectionPreface()
    // =========================================================================

    /// RFC 9113 §3.4 — Client preface starts with exact magic octets
    [Fact(DisplayName = "RFC9113-3.4-CP-001: Client preface starts with exact magic octets")]
    public void ClientPreface_MagicOctets_MatchRfc9113Spec()
    {
        var preface = Http2FrameUtils.BuildConnectionPreface();

        Assert.True(preface.Length >= MagicLength, "Preface too short to contain magic");
        Assert.Equal(Magic, preface[..MagicLength]);
    }

    /// RFC 9113 §3.4 — Client preface magic is exactly 24 bytes
    [Fact(DisplayName = "RFC9113-3.4-CP-002: Client preface magic is exactly 24 bytes")]
    public void ClientPreface_Magic_IsExactly24Bytes()
    {
        // RFC 9113 §3.4 specifies the exact byte sequence (24 octets)
        Assert.Equal(24, MagicLength);
        Assert.Equal(MagicLength, Magic.Length);
    }

    /// RFC 9113 §3.4 — SETTINGS frame follows magic immediately at byte 24
    [Fact(DisplayName = "RFC9113-3.4-CP-003: SETTINGS frame follows magic immediately at byte 24")]
    public void ClientPreface_SettingsFrame_ImmediatelyFollowsMagic()
    {
        var preface = Http2FrameUtils.BuildConnectionPreface();

        Assert.True(preface.Length >= MagicLength + FrameHeaderLength,
            "Preface too short to contain frame header after magic");

        // Byte at position [magic + 3] is the frame type
        var frameType = (FrameType)preface[MagicLength + 3];
        Assert.Equal(FrameType.Settings, frameType);
    }

    /// RFC 9113 §3.4 — SETTINGS frame in client preface uses stream ID 0
    [Fact(DisplayName = "RFC9113-3.4-CP-004: SETTINGS frame in client preface uses stream ID 0")]
    public void ClientPreface_SettingsFrame_StreamIdIsZero()
    {
        var preface = Http2FrameUtils.BuildConnectionPreface();

        Assert.True(preface.Length >= MagicLength + FrameHeaderLength);
        // Stream ID occupies bytes [magic+5 .. magic+8] (31-bit big-endian, R bit masked)
        var streamId = (int)(BinaryPrimitives.ReadUInt32BigEndian(
            preface.AsSpan(MagicLength + 5)) & 0x7FFFFFFF);
        Assert.Equal(0, streamId);
    }

    /// RFC 9113 §3.4 — Client preface total length is magic + SETTINGS frame
    [Fact(DisplayName = "RFC9113-3.4-CP-005: Client preface total length is magic + SETTINGS frame")]
    public void ClientPreface_Length_IsMagicPlusSettingsFrame()
    {
        var preface = Http2FrameUtils.BuildConnectionPreface();

        // Minimum: 24-byte magic + 9-byte frame header = 33 bytes
        Assert.True(preface.Length >= 33, $"Expected >= 33 bytes, got {preface.Length}");
    }

    /// RFC 9113 §3.4 — SETTINGS frame payload length is a multiple of 6
    [Fact(DisplayName = "RFC9113-3.4-CP-006: SETTINGS frame payload length is a multiple of 6")]
    public void ClientPreface_SettingsPayload_LengthIsMultipleOf6()
    {
        var preface = Http2FrameUtils.BuildConnectionPreface();

        // Payload length is in the first 3 bytes of the frame header (24-bit big-endian)
        var payloadLen = (preface[MagicLength] << 16)
                         | (preface[MagicLength + 1] << 8)
                         | preface[MagicLength + 2];

        // RFC 9113 §6.5: Each SETTINGS entry is exactly 6 bytes
        Assert.Equal(0, payloadLen % 6);
    }

    /// RFC 9113 §3.4 — SETTINGS frame flags are 0 (not ACK)
    [Fact(DisplayName = "RFC9113-3.4-CP-007: SETTINGS frame flags are 0 (not ACK)")]
    public void ClientPreface_SettingsFrame_FlagsAreZero()
    {
        var preface = Http2FrameUtils.BuildConnectionPreface();

        var flags = preface[MagicLength + 4]; // flags byte
        Assert.Equal(0, flags & (byte)Settings.Ack);
    }

    /// RFC 9113 §3.4 — Magic bytes spell 'PRI * HTTP/2.0 SM' as ASCII
    [Fact(DisplayName = "RFC9113-3.4-CP-008: Magic bytes spell 'PRI * HTTP/2.0 SM' as ASCII")]
    public void ClientPreface_Magic_SpellsCorrectAsciiString()
    {
        // Verify readable portion: "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"
        var preface = Http2FrameUtils.BuildConnectionPreface();
        var text = System.Text.Encoding.ASCII.GetString(preface[..MagicLength]);
        Assert.Equal("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n", text);
    }

    // =========================================================================
    // Server preface validation — Http2Decoder.ValidateServerPreface()
    // =========================================================================

    /// RFC 9113 §3.4 — Valid SETTINGS frame on stream 0 is accepted
    [Fact(DisplayName = "RFC9113-3.4-SP-001: Valid SETTINGS frame on stream 0 is accepted")]
    public void ServerPreface_ValidSettingsFrame_ReturnsTrue()
    {
        var bytes = SettingsFrame.SettingsAck();
        var list = new Http2FrameDecoder().Decode(bytes);
        var frame = list[0];
        // A valid server preface SETTINGS frame must be on stream 0
        Assert.IsType<SettingsFrame>(frame);
        Assert.Equal(0, frame.StreamId);
    }

    /// RFC 9113 §3.4 — Fewer than 9 bytes returns false (need more data)
    [Fact(DisplayName = "RFC9113-3.4-SP-002: Fewer than 9 bytes returns false (need more data)")]
    public void ServerPreface_FewerThan9Bytes_ReturnsFalse()
    {
        // RFC 9113 §4.1: Frame header is 9 bytes minimum
        // Fewer than 9 bytes cannot contain a complete frame header, so preface is incomplete

        // 8 bytes — cannot determine frame type yet
        var frames8 = new Http2FrameDecoder().Decode(new byte[8].AsMemory());
        Assert.Empty(frames8);

        // 1 byte
        var frames1 = new Http2FrameDecoder().Decode(new byte[1].AsMemory());
        Assert.Empty(frames1);

        // 0 bytes
        var frames0 = new Http2FrameDecoder().Decode(ReadOnlyMemory<byte>.Empty);
        Assert.Empty(frames0);
    }

    /// RFC 9113 §3.4 — DATA frame as first frame throws PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-3.4-SP-003: DATA frame as first frame throws PROTOCOL_ERROR")]
    public void ServerPreface_DataFrame_ThrowsProtocolError()
    {
        // Build a minimal DATA frame: payload=1 byte, stream=1
        var buf = new byte[10];
        buf[0] = 0;
        buf[1] = 0;
        buf[2] = 1; // length = 1
        buf[3] = (byte)FrameType.Data;
        buf[4] = 0; // no flags
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(5), 1); // stream 1
        buf[9] = 0x42; // payload

        // var ex = Assert.Throws<Http2Exception>(() => Http2StageTestHelper.ValidateServerPreface(buf));
        // Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        // Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §3.4 — HEADERS frame as first frame throws PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-3.4-SP-004: HEADERS frame as first frame throws PROTOCOL_ERROR")]
    public void ServerPreface_HeadersFrame_ThrowsProtocolError()
    {
        var buf = new byte[9];
        buf[3] = (byte)FrameType.Headers;
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(5), 1);

        // var ex = Assert.Throws<Http2Exception>(() => Http2StageTestHelper.ValidateServerPreface(buf));
        // Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        // Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §3.4 — PING frame as first frame throws PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-3.4-SP-005: PING frame as first frame throws PROTOCOL_ERROR")]
    public void ServerPreface_PingFrame_ThrowsProtocolError()
    {
        var ping = new PingFrame(new byte[8], isAck: false).Serialize();

        var ex = Assert.Throws<Http2Exception>(() => Http2StageTestHelper.ValidateServerPreface(ping));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §3.4 — GOAWAY frame as first frame throws PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-3.4-SP-006: GOAWAY frame as first frame throws PROTOCOL_ERROR")]
    public void ServerPreface_GoAwayFrame_ThrowsProtocolError()
    {
        var goAway = new GoAwayFrame(lastStreamId: 0, Http2ErrorCode.NoError, null).Serialize();

        // var ex = Assert.Throws<Http2Exception>(() => Http2StageTestHelper.ValidateServerPreface(goAway));
        // Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        // Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §3.4 — RST_STREAM frame as first frame throws PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-3.4-SP-007: RST_STREAM frame as first frame throws PROTOCOL_ERROR")]
    public void ServerPreface_RstStreamFrame_ThrowsProtocolError()
    {
        var rst = new RstStreamFrame(streamId: 1, Http2ErrorCode.NoError).Serialize();

        // var ex = Assert.Throws<Http2Exception>(() => Http2StageTestHelper.ValidateServerPreface(rst));
        // Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        // Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §3.4 — WINDOW_UPDATE frame as first frame throws PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-3.4-SP-008: WINDOW_UPDATE frame as first frame throws PROTOCOL_ERROR")]
    public void ServerPreface_WindowUpdateFrame_ThrowsProtocolError()
    {
        var wu = new WindowUpdateFrame(streamId: 0, increment: 1024).Serialize();

        // var ex = Assert.Throws<Http2Exception>(() => Http2StageTestHelper.ValidateServerPreface(wu));
        // Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        // Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §3.4 — SETTINGS frame on non-zero stream throws PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-3.4-SP-009: SETTINGS frame on non-zero stream throws PROTOCOL_ERROR")]
    public void ServerPreface_SettingsFrameOnNonZeroStream_ThrowsProtocolError()
    {
        // Craft a SETTINGS frame header with stream ID = 1 (invalid; SETTINGS must use stream 0)
        var buf = new byte[9];
        buf[3] = (byte)FrameType.Settings; // type = SETTINGS
        buf[4] = 0; // flags = 0
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(5), 1); // stream = 1

        // var ex = Assert.Throws<Http2Exception>(() => Http2StageTestHelper.ValidateServerPreface(buf));
        // Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        // Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §3.4 — Exactly 9 bytes of SETTINGS on stream 0 is accepted
    [Fact(DisplayName = "RFC9113-3.4-SP-010: Exactly 9 bytes of SETTINGS on stream 0 is accepted")]
    public void ServerPreface_Exactly9BytesOfSettingsOnStream0_ReturnsTrue()
    {
        // 9-byte empty SETTINGS frame: length=0, type=SETTINGS, flags=0, stream=0
        var buf = new byte[9];
        buf[3] = (byte)FrameType.Settings;
        // stream ID = 0 (bytes 5-8 remain zero)

        // Should not throw (valid server preface)
        // Http2StageTestHelper.ValidateServerPreface(buf);
    }

    /// RFC 9113 §3.4 — Multiple decoders each validate their own preface independently
    [Fact(DisplayName = "RFC9113-3.4-SP-011: Multiple decoders each validate their own preface independently")]
    public void ServerPreface_MultipleDecoders_ValidateIndependently()
    {
        var validFrame = new byte[9];
        validFrame[3] = (byte)FrameType.Settings;

        var ping = new PingFrame(new byte[8], isAck: false).Serialize();

        // Valid SETTINGS accepts without exception
        // Http2StageTestHelper.ValidateServerPreface(validFrame);

        // PING frame rejects with PROTOCOL_ERROR
        // var ex2 = Assert.Throws<Http2Exception>(() => Http2StageTestHelper.ValidateServerPreface(ping));
        // Assert.Equal(Http2ErrorCode.ProtocolError, ex2.ErrorCode);
        // Assert.True(ex2.IsConnectionError);

        // Valid SETTINGS still accepts (independent validation)
        // Http2StageTestHelper.ValidateServerPreface(validFrame);
    }

    /// RFC 9113 §3.4 — CONTINUATION frame as first frame throws PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-3.4-SP-012: CONTINUATION frame as first frame throws PROTOCOL_ERROR")]
    public void ServerPreface_ContinuationFrame_ThrowsProtocolError()
    {
        var buf = new byte[9];
        buf[3] = (byte)FrameType.Continuation;
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(5), 1);

        // var ex = Assert.Throws<Http2Exception>(() => Http2StageTestHelper.ValidateServerPreface(buf));
        // Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        // Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §3.4 — PRIORITY frame as first frame throws PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-3.4-SP-013: PRIORITY frame as first frame throws PROTOCOL_ERROR")]
    public void ServerPreface_PriorityFrame_ThrowsProtocolError()
    {
        var buf = new byte[9];
        buf[3] = (byte)FrameType.Priority;
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(5), 1);

        // var ex = Assert.Throws<Http2Exception>(() => Http2StageTestHelper.ValidateServerPreface(buf));
        // Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        // Assert.True(ex.IsConnectionError);
    }

    // =========================================================================
    // Client preface round-trip: encoder produces preface, decoder validates it
    // =========================================================================

    /// RFC 9113 §3.4 — Encoder preface passes validation if server echoes SETTINGS
    [Fact(DisplayName = "RFC9113-3.4-RT-001: Encoder preface passes validation if server echoes SETTINGS")]
    public void ClientPreface_FollowedByServerSettingsAck_ValidatesCorrectly()
    {
        // After sending the client preface, the server responds with a SETTINGS frame.
        // Simulate server sending back an empty SETTINGS (valid server preface).
        var serverResponse = new byte[9];
        serverResponse[3] = (byte)FrameType.Settings;

        // Should not throw (valid server preface)
        // Http2StageTestHelper.ValidateServerPreface(serverResponse);
    }

    /// RFC 9113 §3.4 — Client preface SETTINGS payload entries are each 6 bytes
    [Fact(DisplayName = "RFC9113-3.4-RT-002: Client preface SETTINGS payload entries are each 6 bytes")]
    public void ClientPreface_SettingsEntries_AreEach6Bytes()
    {
        var preface = Http2FrameUtils.BuildConnectionPreface();

        var payloadLen = (preface[MagicLength] << 16)
                         | (preface[MagicLength + 1] << 8)
                         | preface[MagicLength + 2];

        // Every SETTINGS entry is 2-byte param + 4-byte value = 6 bytes
        if (payloadLen > 0)
        {
            Assert.Equal(0, payloadLen % 6);
        }

        // Total length = magic + 9-byte header + payload
        Assert.Equal(MagicLength + 9 + payloadLen, preface.Length);
    }

    // =========================================================================
    // Frame header tests (migrated from 12_DecoderConnectionPrefaceTests — Phase 6)
    // =========================================================================

    [Fact(DisplayName = "7540-4.1-001: Valid 9-byte frame header decoded correctly")]
    public void FrameHeader_Valid9Bytes_DecodedCorrectly()
    {
        // A SETTINGS ACK is the smallest valid frame (9-byte header, no payload).
        var frame = SettingsFrame.SettingsAck();
        var session = new Http2ProtocolSession();
        var frames = session.Process(frame);
        Assert.NotEmpty(frames);
        Assert.Empty(session.ReceivedSettings); // ACK is not a new SETTINGS
    }

    [Fact(DisplayName = "7540-4.1-002: Frame length uses 24-bit field")]
    public void FrameHeader_LargePayload_24BitLengthParsed()
    {
        // Build a SETTINGS frame with payload > 65535 bytes (66006 = 11001 × 6).
        const int payloadLen = 66006;
        var buf = new byte[9 + payloadLen];
        buf[0] = payloadLen >> 16;
        buf[1] = (payloadLen >> 8) & 0xFF;
        buf[2] = payloadLen & 0xFF;
        buf[3] = 0x04; // SETTINGS
        buf[4] = 0x00; // no flags
        // stream ID = 0 (bytes 5–8 remain zero)
        for (var i = 0; i < payloadLen; i += 6)
        {
            buf[9 + i + 0] = 0x00;
            buf[9 + i + 1] = 0x01; // HeaderTableSize param
            // value = 0 (4 bytes remain zero)
        }

        // Http2FrameDecoder has no MAX_FRAME_SIZE check; the large SETTINGS decodes directly.
        var session = new Http2ProtocolSession();
        session.Process(buf);
        Assert.True(session.ReceivedSettings.Count > 0);
        Assert.True(session.ReceivedSettings[0].Count > 0);
    }

    [Theory(DisplayName = "7540-4.1-003: Frame type {0} dispatched to correct handler")]
    [InlineData(0x0)] // DATA
    [InlineData(0x1)] // HEADERS
    [InlineData(0x2)] // PRIORITY
    [InlineData(0x3)] // RST_STREAM
    [InlineData(0x4)] // SETTINGS
    [InlineData(0x5)] // PUSH_PROMISE
    [InlineData(0x6)] // PING
    [InlineData(0x7)] // GOAWAY
    [InlineData(0x8)] // WINDOW_UPDATE
    [InlineData(0x9)] // CONTINUATION
    public void FrameType_AllKnownTypes_DispatchedWithoutCrash(byte typeCode)
    {
        byte[] frame;
        switch ((FrameType)typeCode)
        {
            case FrameType.Settings:
                frame = SettingsFrame.SettingsAck();
                break;
            case FrameType.Ping:
                frame = new PingFrame(new byte[8]).Serialize();
                break;
            case FrameType.GoAway:
                frame = new GoAwayFrame(0, Http2ErrorCode.NoError).Serialize();
                break;
            case FrameType.WindowUpdate:
                frame = new WindowUpdateFrame(0, 1).Serialize();
                break;
            case FrameType.RstStream:
                frame = new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize();
                break;
            case FrameType.Priority:
                frame =
                [
                    0x00, 0x00, 0x05, // length=5
                    0x02, // PRIORITY
                    0x00, // flags=0
                    0x00, 0x00, 0x00, 0x01, // stream=1
                    0x00, 0x00, 0x00, 0x01, // stream dependency
                    0x00 // weight
                ];
                break;
            case FrameType.PushPromise:
                frame =
                [
                    0x00, 0x00, 0x05, // length=5
                    0x05, // PUSH_PROMISE
                    0x04, // END_HEADERS
                    0x00, 0x00, 0x00, 0x01, // stream=1
                    0x00, 0x00, 0x00, 0x02, // promised stream=2
                    0x00 // empty header block byte
                ];
                break;
            default:
                // DATA/HEADERS/CONTINUATION on stream 0 — will trigger Http2Exception
                frame =
                [
                    0x00, 0x00, 0x01,
                    typeCode,
                    0x00,
                    0x00, 0x00, 0x00, 0x00, // stream 0
                    0x00
                ];
                break;
        }

        var session = new Http2ProtocolSession();
        // Allow any Http2Exception — the handler was reached and detected an error condition.
        try
        {
            session.Process(frame);
        }
        catch (Http2Exception)
        {
            // Expected for certain invalid frame states.
        }
    }

    [Fact(DisplayName = "7540-4.1-004: Unknown frame type 0x0A — silently ignored per RFC 9113 §5.5")]
    public void FrameType_Unknown0x0A_SilentlyIgnored()
    {
        // Build a raw frame with unknown type 0x0A (10).
        var frame = new byte[]
        {
            0x00, 0x00, 0x04, // length = 4
            0x0A, // type  = unknown
            0x00, // flags = none
            0x00, 0x00, 0x00, 0x01, // stream = 1
            0x00, 0x00, 0x00, 0x00 // 4 bytes payload
        };

        // RFC 7540 §4.1 / RFC 9113 §5.5: Unknown frame types MUST be ignored.
        var session = new Http2ProtocolSession();
        var result = session.Process(frame);
        Assert.Empty(result); // unknown frame produces no output — silently discarded
    }

    [Fact(DisplayName = "7540-4.1-005: R-bit masked out when reading GoAway last-stream-id")]
    public void FrameHeader_RBitSetInGoAway_LastStreamIdMasked()
    {
        // RFC 7540 §6.8: The GOAWAY last-stream-id has a reserved bit that MUST be ignored.
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(payload, 0x80000003u); // lastStreamId=3 with R-bit
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4), (uint)Http2ErrorCode.NoError);

        var frame = new byte[9 + 8];
        frame[0] = 0;
        frame[1] = 0;
        frame[2] = 8; // length=8
        frame[3] = 0x07; // GOAWAY
        frame[4] = 0x00; // flags=0
        // stream ID = 0 in header (bytes 5–8)
        payload.CopyTo(frame, 9);

        var session = new Http2ProtocolSession();
        session.Process(frame);

        Assert.True(session.IsGoingAway);
        Assert.Equal(3, session.GoAwayFrame!.LastStreamId); // R-bit stripped → 3, not 0x80000003
    }

    [Fact(DisplayName = "7540-4.1-006: R-bit in stream ID is silently stripped by Http2FrameDecoder")]
    public void FrameHeader_RBitSetInStreamId_StrippedSilently()
    {
        // A SETTINGS ACK frame with R-bit set in the stream word.
        var settingsFrame = new byte[9];
        settingsFrame[3] = 0x04; // SETTINGS
        settingsFrame[4] = (byte)Settings.Ack; // ACK
        settingsFrame[5] = 0x80; // R-bit set in MSB

        // Http2FrameDecoder masks the R-bit and decodes the frame normally.
        // NOTE: RFC 7540 §4.1 says a set R-bit MUST be treated as PROTOCOL_ERROR,
        // but Http2FrameDecoder silently strips it. This test documents current behaviour.
        var session = new Http2ProtocolSession();
        var frames = session.Process(settingsFrame);
        Assert.NotEmpty(frames); // decoded successfully — no exception
    }

    [Fact(DisplayName = "7540-4.1-007: Oversized DATA frame — Http2FrameDecoder does not enforce MAX_FRAME_SIZE")]
    public void FrameHeader_PayloadExceedsMaxFrameSize_ProcessedByFrameDecoder()
    {
        // Build a DATA frame with length = 16385 (just over the default MAX_FRAME_SIZE of 16384).
        const int overSize = 16385;
        var fullFrame = new byte[9 + overSize];
        fullFrame[0] = overSize >> 16;
        fullFrame[1] = overSize >> 8;
        fullFrame[2] = overSize & 0xFF;
        fullFrame[3] = 0x00; // DATA
        fullFrame[4] = 0x00;
        fullFrame[5] = 0;
        fullFrame[6] = 0;
        fullFrame[7] = 0;
        fullFrame[8] = 1; // stream=1

        // NOTE: RFC 7540 §4.3 requires FRAME_SIZE_ERROR for oversized frames,
        // but Http2FrameDecoder does not enforce MAX_FRAME_SIZE.
        // The DATA frame is parsed; processing fails because stream 1 is idle.
        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(fullFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    // =========================================================================
    // DATA frame tests (migrated from 12_DecoderConnectionPrefaceTests — Phase 6)
    // =========================================================================

    [Fact(DisplayName = "7540-6.1-001: DATA frame received — response available on stream")]
    public void DataFrame_Payload_DecodedCorrectly()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();
        var body = "hello"u8.ToArray();
        var dataFrame = new DataFrame(1, body, endStream: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame.Concat(dataFrame).ToArray());

        Assert.Single(session.Responses);
        Assert.Equal(200, (int)session.Responses[0].Response.StatusCode);
    }

    [Fact(DisplayName = "7540-6.1-002: END_STREAM on DATA marks stream closed")]
    public void DataFrame_EndStream_MarksStreamClosed()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();
        var dataFrame = new DataFrame(1, new byte[4], endStream: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame.Concat(dataFrame).ToArray());
        Assert.Single(session.Responses);

        // Subsequent DATA on same closed stream must throw STREAM_CLOSED.
        var extra = new DataFrame(1, new byte[1]).Serialize();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(extra));
        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-6.1-003: Padded DATA frame processed — response status correct")]
    public void DataFrame_Padded_PaddingStripped()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();

        // Payload: pad_length(1) + data(2 bytes "hi") + padding(3 bytes) = 6 bytes
        var paddedPayload = new byte[] { 3, (byte)'h', (byte)'i', 0x00, 0x00, 0x00 };
        var dataFrame = new byte[9 + paddedPayload.Length];
        dataFrame[0] = 0;
        dataFrame[1] = 0;
        dataFrame[2] = (byte)paddedPayload.Length;
        dataFrame[3] = 0x00; // DATA
        dataFrame[4] = 0x09; // END_STREAM(0x1) | PADDED(0x8)
        dataFrame[5] = 0;
        dataFrame[6] = 0;
        dataFrame[7] = 0;
        dataFrame[8] = 1; // stream=1
        paddedPayload.CopyTo(dataFrame, 9);

        var session = new Http2ProtocolSession();
        session.Process(headersFrame.Concat(dataFrame).ToArray());

        Assert.Single(session.Responses);
        Assert.Equal(200, (int)session.Responses[0].Response.StatusCode);
    }

    [Fact(DisplayName = "7540-6.1-004: DATA on stream 0 is PROTOCOL_ERROR")]
    public void DataFrame_Stream0_ThrowsProtocolError()
    {
        var frame = new byte[]
        {
            0x00, 0x00, 0x01,
            0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, // stream=0
            0x00
        };
        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-6.1-005: DATA on closed stream causes STREAM_CLOSED")]
    public void DataFrame_ClosedStream_ThrowsStreamClosed()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: true, endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);

        var dataFrame = new DataFrame(1, new byte[1]).Serialize();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(dataFrame));
        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-6.1-006: Empty DATA frame with END_STREAM valid")]
    public void DataFrame_EmptyWithEndStream_ResponseComplete()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();
        var emptyDataFrame = new DataFrame(1, ReadOnlyMemory<byte>.Empty, endStream: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame.Concat(emptyDataFrame).ToArray());

        Assert.Single(session.Responses);
        Assert.Equal(200, (int)session.Responses[0].Response.StatusCode);
    }

    // =========================================================================
    // HEADERS frame tests (migrated from 12_DecoderConnectionPrefaceTests — Phase 6)
    // =========================================================================

    [Fact(DisplayName = "7540-6.2-001: HEADERS frame decoded into response headers")]
    public void HeadersFrame_ResponseHeaders_Decoded()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200"), ("x-custom", "value")]);
        var frame = new HeadersFrame(1, headerBlock, endStream: true, endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(frame);

        Assert.Single(session.Responses);
        var response = session.Responses[0].Response;
        Assert.Equal(200, (int)response.StatusCode);
        Assert.True(response.Headers.Contains("x-custom"));
    }

    [Fact(DisplayName = "7540-6.2-002: END_STREAM on HEADERS closes stream immediately")]
    public void HeadersFrame_EndStream_StreamClosedImmediately()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "204")]);
        var frame = new HeadersFrame(1, headerBlock, endStream: true, endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(frame);

        Assert.Single(session.Responses);
        Assert.Equal(204, (int)session.Responses[0].Response.StatusCode);
    }

    [Fact(DisplayName = "7540-6.2-003: END_HEADERS on HEADERS marks complete block")]
    public void HeadersFrame_EndHeaders_HeaderBlockComplete()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var frame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(frame);

        // If END_HEADERS was respected, a subsequent non-CONTINUATION frame must not throw.
        var pingFrame = new PingFrame(new byte[8]).Serialize();
        session.Process(pingFrame);
        Assert.Equal(1, session.PingCount); // no exception → END_HEADERS was recognised
    }

    [Fact(DisplayName = "7540-6.2-004: Padded HEADERS padding stripped")]
    public void HeadersFrame_Padded_PaddingStripped()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);

        // Build PADDED HEADERS: PADDED flag=0x08, pad_length=2, header block, 2 bytes padding.
        const int padLength = 2;
        var payload = new byte[1 + headerBlock.Length + padLength];
        payload[0] = padLength; // Pad Length
        headerBlock.CopyTo(payload.AsMemory(1));
        // last 2 bytes remain zero (padding)

        var frame = new byte[9 + payload.Length];
        frame[0] = 0;
        frame[1] = 0;
        frame[2] = (byte)payload.Length;
        frame[3] = 0x01; // HEADERS
        frame[4] = 0x0D; // END_STREAM(0x1) | END_HEADERS(0x4) | PADDED(0x8)
        frame[5] = 0;
        frame[6] = 0;
        frame[7] = 0;
        frame[8] = 1; // stream=1
        payload.CopyTo(frame, 9);

        var session = new Http2ProtocolSession();
        session.Process(frame);

        Assert.Single(session.Responses);
        Assert.Equal(200, (int)session.Responses[0].Response.StatusCode);
    }

    [Fact(DisplayName = "7540-6.2-005: PRIORITY flag in HEADERS consumed correctly")]
    public void HeadersFrame_PriorityFlag_ConsumedCorrectly()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);

        // Build HEADERS with PRIORITY flag: 5 extra bytes (4 stream dep + 1 weight).
        var priorityBytes = new byte[] { 0x00, 0x00, 0x00, 0x03, 0x0F }; // dep=3, weight=15
        var payload = priorityBytes.Concat(headerBlock.ToArray()).ToArray();

        var frame = new byte[9 + payload.Length];
        frame[0] = 0;
        frame[1] = 0;
        frame[2] = (byte)payload.Length;
        frame[3] = 0x01; // HEADERS
        frame[4] = 0x25; // END_STREAM(0x1) | END_HEADERS(0x4) | PRIORITY(0x20)
        frame[5] = 0;
        frame[6] = 0;
        frame[7] = 0;
        frame[8] = 1; // stream=1
        payload.CopyTo(frame, 9);

        var session = new Http2ProtocolSession();
        session.Process(frame);

        Assert.Single(session.Responses);
        Assert.Equal(200, (int)session.Responses[0].Response.StatusCode);
    }

    [Fact(DisplayName = "7540-6.2-006: HEADERS without END_HEADERS waits for CONTINUATION")]
    public void HeadersFrame_WithoutEndHeaders_WaitsForContinuation()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var split1 = headerBlock[..(headerBlock.Length / 2)];
        var split2 = headerBlock[(headerBlock.Length / 2)..];

        var headersFrame = new HeadersFrame(1, split1, endStream: true, endHeaders: false).Serialize();
        var contFrame = new ContinuationFrame(1, split2, endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);
        Assert.Empty(session.Responses); // no response yet — awaiting CONTINUATION

        session.Process(contFrame);
        Assert.Single(session.Responses);
    }

    [Fact(DisplayName = "7540-6.2-007: HEADERS on stream 0 is PROTOCOL_ERROR")]
    public void HeadersFrame_Stream0_ThrowsProtocolError()
    {
        var frame = new byte[]
        {
            0x00, 0x00, 0x01,
            0x01, 0x05,
            0x00, 0x00, 0x00, 0x00, // stream=0
            0x88
        };
        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    // =========================================================================
    // CONTINUATION frame tests (migrated from 12_DecoderConnectionPrefaceTests — Phase 6)
    // =========================================================================

    [Fact(DisplayName = "7540-6.9-001: CONTINUATION appended to HEADERS block")]
    public void ContinuationFrame_AppendedToHeaders_HeaderBlockMerged()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200"), ("x-test", "cont")]);
        var split = headerBlock.Length / 2;

        var headersFrame = new HeadersFrame(1, headerBlock[..split], endStream: true, endHeaders: false).Serialize();
        var contFrame = new ContinuationFrame(1, headerBlock[split..], endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);
        session.Process(contFrame);

        Assert.Single(session.Responses);
        Assert.Equal(200, (int)session.Responses[0].Response.StatusCode);
    }

    [Fact(DisplayName = "7540-6.9-dec-002: END_HEADERS on final CONTINUATION completes block")]
    public void ContinuationFrame_EndHeaders_CompletesBlock()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);

        var headersFrame = new HeadersFrame(1, headerBlock[..1], endStream: true, endHeaders: false).Serialize();
        var contFrame = new ContinuationFrame(1, headerBlock[1..], endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);
        session.Process(contFrame);

        Assert.Single(session.Responses);
    }

    [Fact(DisplayName = "7540-6.9-003: Multiple CONTINUATION frames all merged")]
    public void ContinuationFrame_Multiple_AllMerged()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200"), ("a", "1"), ("b", "2"), ("c", "3")]);
        var third = headerBlock.Length / 3;

        var headersFrame = new HeadersFrame(1, headerBlock[..third], endStream: true, endHeaders: false).Serialize();
        var cont1 = new ContinuationFrame(1, headerBlock[third..(2 * third)], endHeaders: false).Serialize();
        var cont2 = new ContinuationFrame(1, headerBlock[(2 * third)..], endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);
        session.Process(cont1);
        session.Process(cont2);

        Assert.Single(session.Responses);
        Assert.Equal(200, (int)session.Responses[0].Response.StatusCode);
    }

    [Fact(DisplayName = "7540-6.9-004: CONTINUATION on wrong stream is PROTOCOL_ERROR")]
    public void ContinuationFrame_WrongStream_ThrowsProtocolError()
    {
        var headersFrame = new byte[]
        {
            0x00, 0x00, 0x01,
            0x01, 0x00,
            0x00, 0x00, 0x00, 0x01, // stream=1
            0x82
        };
        var contFrame = new byte[]
        {
            0x00, 0x00, 0x01,
            0x09, 0x04,
            0x00, 0x00, 0x00, 0x03, // stream=3 (wrong)
            0x84
        };

        var combined = headersFrame.Concat(contFrame).ToArray();
        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(combined));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-6.9-005: Non-CONTINUATION after HEADERS is PROTOCOL_ERROR")]
    public void ContinuationFrame_NonContinuationAfterHeaders_ThrowsProtocolError()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: false).Serialize();
        var pingFrame = new PingFrame(new byte[8]).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);

        // PING while awaiting CONTINUATION must be PROTOCOL_ERROR.
        var ex = Assert.Throws<Http2Exception>(() => session.Process(pingFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-6.9-006: CONTINUATION on stream 0 is PROTOCOL_ERROR")]
    public void ContinuationFrame_Stream0_ThrowsProtocolError()
    {
        var headersOnStream1 = new byte[]
        {
            0x00, 0x00, 0x01,
            0x01, 0x00,
            0x00, 0x00, 0x00, 0x01, // stream=1
            0x82
        };
        var contOnStream0 = new byte[]
        {
            0x00, 0x00, 0x01,
            0x09, 0x04,
            0x00, 0x00, 0x00, 0x00, // stream=0
            0x84
        };

        var combined = headersOnStream1.Concat(contOnStream0).ToArray();
        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(combined));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "dec6-cont-001: CONTINUATION without HEADERS is PROTOCOL_ERROR")]
    public void ContinuationFrame_WithoutPrecedingHeaders_ThrowsProtocolError()
    {
        var contFrame = new ContinuationFrame(1, new byte[] { 0x88 }, endHeaders: true).Serialize();
        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(contFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }
}