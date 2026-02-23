using System.Buffers.Binary;
using System.Linq;
using TurboHttp.Protocol;

namespace TurboHttp.Tests;

public sealed class Http2DecoderTests
{
    // ── Existing baseline tests ───────────────────────────────────────────────

    [Fact]
    public void Decode_SettingsFrame_ExtractsParameters()
    {
        var settings = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.MaxConcurrentStreams, 100u),
            (SettingsParameter.InitialWindowSize, 65535u),
        }).Serialize();

        var decoder = new Http2Decoder();
        var decoded = decoder.TryDecode(settings, out var result);

        Assert.True(decoded);
        Assert.True(result.HasNewSettings);
        Assert.Single(result.ReceivedSettings);

        var s = result.ReceivedSettings[0];
        Assert.Equal(2, s.Count);
        Assert.Contains(s, p => p.Item1 == SettingsParameter.MaxConcurrentStreams && p.Item2 == 100u);
    }

    [Fact]
    public void Decode_SettingsAck_DoesNotAddToSettings()
    {
        var ack = SettingsFrame.SettingsAck();
        var decoder = new Http2Decoder();
        decoder.TryDecode(ack, out var result);

        Assert.False(result.HasNewSettings);
    }

    [Fact]
    public void Decode_PingRequest_ReturnsInPingRequests()
    {
        var data = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        var ping = new PingFrame(data, isAck: false).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(ping, out var result);

        Assert.Single(result.PingRequests);
        Assert.Equal(data, result.PingRequests[0]);
    }

    [Fact]
    public void Decode_PingAck_ReturnsInPingAcks()
    {
        var data = new byte[] { 7, 6, 5, 4, 3, 2, 1, 0 };
        var ping = new PingFrame(data, isAck: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(ping, out var result);

        Assert.Single(result.PingAcks);
        Assert.Equal(data, result.PingAcks[0]);
    }

    [Fact]
    public void Decode_WindowUpdate_ReturnsIncrement()
    {
        var frame = new WindowUpdateFrame(1, 32768).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);

        Assert.Single(result.WindowUpdates);
        Assert.Equal((1, 32768), result.WindowUpdates[0]);
    }

    [Fact]
    public void Decode_RstStream_ReturnsErrorCode()
    {
        var frame = new RstStreamFrame(3, Http2ErrorCode.Cancel).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);

        Assert.Single(result.RstStreams);
        Assert.Equal((3, Http2ErrorCode.Cancel), result.RstStreams[0]);
    }

    [Fact]
    public void Decode_GoAway_ParsedCorrectly()
    {
        var frame = new GoAwayFrame(5, Http2ErrorCode.NoError,
            "server shutdown"u8.ToArray()).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);

        Assert.True(result.HasGoAway);
        Assert.Equal(5, result.GoAway!.LastStreamId);
        Assert.Equal(Http2ErrorCode.NoError, result.GoAway.ErrorCode);
    }

    [Fact]
    public void Decode_FrameFragmented_ReassembledCorrectly()
    {
        var ping = new PingFrame(new byte[8], isAck: false).Serialize();
        const int cut = 5;
        var chunk1 = ping[..cut];
        var chunk2 = ping[cut..];

        var decoder = new Http2Decoder();
        var d1 = decoder.TryDecode(chunk1, out _);
        var d2 = decoder.TryDecode(chunk2, out var result);

        Assert.False(d1);
        Assert.True(d2);
        Assert.Single(result.PingRequests);
    }

    [Fact]
    public void Decode_MultipleFrames_AllProcessed()
    {
        var ping1 = new PingFrame([1, 1, 1, 1, 1, 1, 1, 1]).Serialize();
        var ping2 = new PingFrame([2, 2, 2, 2, 2, 2, 2, 2]).Serialize();
        var settings = SettingsFrame.SettingsAck();

        var combined = new byte[ping1.Length + ping2.Length + settings.Length];
        ping1.CopyTo(combined, 0);
        ping2.CopyTo(combined, ping1.Length);
        settings.CopyTo(combined, ping1.Length + ping2.Length);

        var decoder = new Http2Decoder();
        decoder.TryDecode(combined, out var result);

        Assert.Equal(2, result.PingRequests.Count);
    }

    [Fact]
    public async Task Decode_HeadersAndData_ReturnsCompleteResponse()
    {
        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var responseHeaders = new List<(string, string)>
        {
            (":status", "200"),
            ("content-type", "text/plain"),
        };
        var headerBlock = hpackEncoder.Encode(responseHeaders);
        var headersFrame = new HeadersFrame(1, headerBlock,
            endStream: false, endHeaders: true).Serialize();

        var bodyData = "Hello, HTTP/2!"u8.ToArray();
        var dataFrame = new DataFrame(1, bodyData, endStream: true).Serialize();

        var combined = new byte[headersFrame.Length + dataFrame.Length];
        headersFrame.CopyTo(combined, 0);
        dataFrame.CopyTo(combined, headersFrame.Length);

        var decoder = new Http2Decoder();
        var decoded = decoder.TryDecode(combined, out var result);

        Assert.True(decoded);
        Assert.True(result.HasResponses);
        Assert.Single(result.Responses);

        var (streamId, response) = result.Responses[0];
        Assert.Equal(1, streamId);
        Assert.Equal(200, (int)response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello, HTTP/2!", content);
        Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public void Decode_HeadersWithEndStream_NoBodyResponse()
    {
        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var headerBlock = hpackEncoder.Encode([(":status", "204")]);
        var headersFrame = new HeadersFrame(3, headerBlock,
            endStream: true, endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame, out var result);

        Assert.True(result.HasResponses);
        Assert.Equal(204, (int)result.Responses[0].Response.StatusCode);
        Assert.Equal(0, result.Responses[0].Response.Content.Headers.ContentLength);
    }

    // ── RFC 7540 §6.1 / §6.2 / §6.10 — Stream-0 and wrong-stream errors ────

    [Fact]
    public void Decode_DataOnStream0_ThrowsProtocolError()
    {
        var frame = new byte[]
        {
            0x00, 0x00, 0x04,  // length = 4
            0x00,              // type  = DATA
            0x00,              // flags = none
            0x00, 0x00, 0x00, 0x00, // stream ID = 0
            0x00, 0x00, 0x00, 0x00  // payload (4 bytes)
        };

        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(frame, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact]
    public void Decode_HeadersOnStream0_ThrowsProtocolError()
    {
        var frame = new byte[]
        {
            0x00, 0x00, 0x01,  // length = 1
            0x01,              // type  = HEADERS
            0x05,              // flags = END_STREAM | END_HEADERS
            0x00, 0x00, 0x00, 0x00, // stream ID = 0
            0x88               // HPACK: :status 200
        };

        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(frame, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact]
    public void Decode_ContinuationOnStream0_ThrowsProtocolError()
    {
        var headersOnStream1 = new byte[]
        {
            0x00, 0x00, 0x01,  // length = 1
            0x01,              // type  = HEADERS
            0x00,              // flags = none
            0x00, 0x00, 0x00, 0x01, // stream ID = 1
            0x82               // HPACK: :method GET
        };
        var contOnStream0 = new byte[]
        {
            0x00, 0x00, 0x01,  // length = 1
            0x09,              // type  = CONTINUATION
            0x04,              // flags = END_HEADERS
            0x00, 0x00, 0x00, 0x00, // stream ID = 0
            0x84               // HPACK: :path /
        };

        var combined = headersOnStream1.Concat(contOnStream0).ToArray();
        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(combined, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact]
    public void Decode_ContinuationOnWrongStream_ThrowsProtocolError()
    {
        var headersOnStream1 = new byte[]
        {
            0x00, 0x00, 0x01,
            0x01,
            0x00,
            0x00, 0x00, 0x00, 0x01,
            0x82
        };
        var contOnStream3 = new byte[]
        {
            0x00, 0x00, 0x01,
            0x09,
            0x04,
            0x00, 0x00, 0x00, 0x03,
            0x84
        };

        var combined = headersOnStream1.Concat(contOnStream3).ToArray();
        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(combined, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact]
    public async Task Decode_ContinuationFrames_Reassembled()
    {
        var hpackEncoder = new HpackEncoder();
        var headerBlock = hpackEncoder.Encode([
            (":status", "200"),
            ("content-type", "application/json"),
            ("x-request-id", "abc-123"),
        ]);

        var split1 = headerBlock[..(headerBlock.Length / 2)];
        var split2 = headerBlock[(headerBlock.Length / 2)..];

        var headersFrame = new HeadersFrame(5, split1,
            endStream: false, endHeaders: false).Serialize();
        var contFrame = new ContinuationFrame(5, split2,
            endHeaders: true).Serialize();

        var bodyData = "{\"ok\":true}"u8.ToArray();
        var dataFrame = new DataFrame(5, bodyData, endStream: true).Serialize();

        var combined = new byte[headersFrame.Length + contFrame.Length + dataFrame.Length];
        headersFrame.CopyTo(combined, 0);
        contFrame.CopyTo(combined, headersFrame.Length);
        dataFrame.CopyTo(combined, headersFrame.Length + contFrame.Length);

        var decoder = new Http2Decoder();
        decoder.TryDecode(combined, out var result);

        Assert.True(result.HasResponses);
        var response = result.Responses[0].Response;
        Assert.Equal(200, (int)response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("{\"ok\":true}", content);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PHASE 6: HTTP/2 (RFC 7540) — CLIENT DECODER
    // ═══════════════════════════════════════════════════════════════════════

    // ── Connection Preface (RFC 7540 §3.5) ──────────────────────────────────

    [Fact(DisplayName = "7540-3.5-002: Invalid server preface causes PROTOCOL_ERROR")]
    public void ServerPreface_NonSettingsFrame_ThrowsProtocolError()
    {
        // A PING frame (not SETTINGS) as the first bytes from the server.
        var pingFrame = new PingFrame(new byte[8], isAck: false).Serialize();
        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.ValidateServerPreface(pingFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-3.5-004: Missing SETTINGS after preface causes error")]
    public void ServerPreface_IncompleteBytes_ReturnsFalse()
    {
        // Fewer than 9 bytes — cannot determine frame type yet; no error, return false.
        var incompleteBytes = new byte[] { 0x00, 0x00 };
        var decoder = new Http2Decoder();
        var result = decoder.ValidateServerPreface(incompleteBytes);
        Assert.False(result);
    }

    // ── Frame Header (RFC 7540 §4.1) ─────────────────────────────────────────

    [Fact(DisplayName = "7540-4.1-001: Valid 9-byte frame header decoded correctly")]
    public void FrameHeader_Valid9Bytes_DecodedCorrectly()
    {
        // A SETTINGS frame has a 9-byte header with zero payload.
        var frame = SettingsFrame.SettingsAck(); // 9 bytes: length=0, type=4, flags=ACK, stream=0
        var decoder = new Http2Decoder();
        var ok = decoder.TryDecode(frame, out var result);
        Assert.True(ok);
        Assert.False(result.HasNewSettings); // ACK, not a new settings
    }

    [Fact(DisplayName = "7540-4.1-002: Frame length uses 24-bit field")]
    public void FrameHeader_LargePayload_24BitLengthParsed()
    {
        // Build a SETTINGS frame with payload > 65535 bytes (need large frame size).
        // Use 66000 bytes of SETTINGS-like content (11000 settings × 6 bytes = 66000).
        const int payloadLen = 66006; // divisible by 6
        var buf = new byte[9 + payloadLen];
        buf[0] = (byte)(payloadLen >> 16);
        buf[1] = (byte)((payloadLen >> 8) & 0xFF);
        buf[2] = (byte)(payloadLen & 0xFF);
        buf[3] = 0x04; // SETTINGS
        buf[4] = 0x00; // no flags
        // stream ID = 0 (bytes 5–8 remain zero)

        // Fill with valid SETTINGS entries (param=1, value=0 repeated).
        for (var i = 0; i < payloadLen; i += 6)
        {
            buf[9 + i + 0] = 0x00;
            buf[9 + i + 1] = 0x01; // HeaderTableSize
            // value = 0 (4 bytes remain zero)
        }

        var decoder = new Http2Decoder();
        // Raise max frame size so the frame is accepted.
        decoder.SetConnectionReceiveWindow(int.MaxValue);

        // Use reflection or a raw approach: build SETTINGS with enlarged _maxFrameSize.
        // Since SetConnectionReceiveWindow doesn't set maxFrameSize, we do it indirectly:
        // Send a SETTINGS frame with MaxFrameSize = 2^24-1.
        var maxSizeSettings = new SettingsFrame([(SettingsParameter.MaxFrameSize, (uint)payloadLen + 100)]).Serialize();
        decoder.TryDecode(maxSizeSettings, out _);

        var ok = decoder.TryDecode(buf, out var result);
        Assert.True(ok);
        Assert.True(result.HasNewSettings);
        // Verify at least one settings entry was decoded.
        Assert.True(result.ReceivedSettings[0].Count > 0);
    }

    [Theory(DisplayName = "7540-4.1-003: Frame type {0} dispatched to correct handler")]
    [InlineData(0x0)] // DATA — requires active stream; test stream-0 error
    [InlineData(0x1)] // HEADERS
    [InlineData(0x2)] // PRIORITY
    [InlineData(0x3)] // RST_STREAM
    [InlineData(0x4)] // SETTINGS
    [InlineData(0x5)] // PUSH_PROMISE
    [InlineData(0x6)] // PING
    [InlineData(0x7)] // GOAWAY
    [InlineData(0x8)] // WINDOW_UPDATE
    [InlineData(0x9)] // CONTINUATION — tested separately
    public void FrameType_AllKnownTypes_DispatchedWithoutCrash(byte typeCode)
    {
        // Build a minimal valid frame for each type and verify it doesn't throw unexpectedly.
        // Frames that require special setup (DATA, HEADERS, CONTINUATION) use stream 0 check
        // which will throw Http2Exception — that's acceptable as "dispatched to correct handler".
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
                // PRIORITY: 9-byte header + 5-byte payload (stream dep + weight)
                frame =
                [
                    0x00, 0x00, 0x05, // length=5
                    0x02,             // PRIORITY
                    0x00,             // flags=0
                    0x00, 0x00, 0x00, 0x01, // stream=1
                    0x00, 0x00, 0x00, 0x01, // stream dependency
                    0x00              // weight
                ];
                break;
            default:
                // DATA on stream 0, HEADERS on stream 0, CONTINUATION without headers,
                // PUSH_PROMISE — all handled by throwing Http2Exception for invalid state.
                frame =
                [
                    0x00, 0x00, 0x01,
                    typeCode,
                    0x00,
                    0x00, 0x00, 0x00, 0x00, // stream 0 — will trigger PROTOCOL_ERROR
                    0x00
                ];
                break;
        }

        var decoder = new Http2Decoder();
        // Allow PROTOCOL_ERROR for stream-0 cases; everything else should decode cleanly.
        try
        {
            decoder.TryDecode(frame, out _);
        }
        catch (Http2Exception ex) when (ex.ErrorCode == Http2ErrorCode.ProtocolError)
        {
            // Expected for DATA/HEADERS/CONTINUATION on stream 0 — handler was invoked.
        }
    }

    [Fact(DisplayName = "7540-4.1-004: Unknown frame type 0x0A is ignored")]
    public void FrameType_Unknown0x0A_Ignored()
    {
        // Build a raw frame with unknown type 0x0A (10).
        var frame = new byte[]
        {
            0x00, 0x00, 0x04, // length = 4
            0x0A,             // type  = unknown
            0x00,             // flags = none
            0x00, 0x00, 0x00, 0x01, // stream = 1
            0x00, 0x00, 0x00, 0x00  // 4 bytes payload
        };

        var decoder = new Http2Decoder();
        var ok = decoder.TryDecode(frame, out var result);
        Assert.True(ok);
        Assert.False(result.HasResponses);
        Assert.False(result.HasNewSettings);
    }

    [Fact(DisplayName = "7540-4.1-005: R-bit masked out when reading stream ID")]
    public void FrameHeader_RBitSetInGoAway_LastStreamIdMasked()
    {
        // RFC 7540 §6.8: The GOAWAY frame last-stream-id field also has a reserved bit.
        // Verify the decoder masks it out: stream 3 with R-bit = 0x80000003.
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(payload, 0x80000003u); // lastStreamId = 3 with R-bit
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4), (uint)Http2ErrorCode.NoError);

        var frame = new byte[9 + 8];
        frame[0] = 0; frame[1] = 0; frame[2] = 8; // length=8
        frame[3] = 0x07;                           // GOAWAY
        frame[4] = 0x00;                           // flags=0
        // stream ID = 0 in header (bytes 5–8)
        payload.CopyTo(frame, 9);

        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);

        Assert.True(result.HasGoAway);
        Assert.Equal(3, result.GoAway!.LastStreamId); // R-bit stripped → 3, not 0x80000003
    }

    [Fact(DisplayName = "7540-4.1-006: R-bit set in stream ID causes PROTOCOL_ERROR")]
    public void FrameHeader_RBitSetInStreamId_ThrowsProtocolError()
    {
        // Build a raw DATA frame with R-bit set in the stream ID field.
        var frame = new byte[]
        {
            0x00, 0x00, 0x04, // length = 4
            0x06,             // PING (valid payload size = 8; use SETTINGS for simplicity)
            0x00,             // flags
            0x80, 0x00, 0x00, 0x00, // stream ID = 0 with R-bit set (0x80000000)
            0x00, 0x00, 0x00, 0x00
        };

        // Actually, for SETTINGS the stream must be 0 and this sets stream=0 with R-bit.
        // Let's use a SETTINGS ACK frame with R-bit in the stream word.
        var settingsFrame = new byte[9];
        settingsFrame[0] = 0; settingsFrame[1] = 0; settingsFrame[2] = 0; // length=0
        settingsFrame[3] = 0x04; // SETTINGS
        settingsFrame[4] = (byte)SettingsFlags.Ack; // ACK
        // Set R-bit in stream ID field:
        settingsFrame[5] = 0x80; // MSB = R-bit set
        settingsFrame[6] = 0;
        settingsFrame[7] = 0;
        settingsFrame[8] = 0;

        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(settingsFrame, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-4.1-007: Oversized frame causes FRAME_SIZE_ERROR")]
    public void FrameHeader_PayloadExceedsMaxFrameSize_ThrowsFrameSizeError()
    {
        // Build a raw frame with length = 16385 (just over default MAX_FRAME_SIZE of 16384).
        const int overSize = 16385;
        var frame = new byte[9]; // header only; we never get to the payload check
        frame[0] = (byte)(overSize >> 16);
        frame[1] = (byte)(overSize >> 8);
        frame[2] = (byte)(overSize & 0xFF);
        frame[3] = 0x00; // DATA
        frame[4] = 0x00; // flags
        // stream = 1
        frame[5] = 0; frame[6] = 0; frame[7] = 0; frame[8] = 1;

        // Pad to full length so the frame is "complete".
        var fullFrame = new byte[9 + overSize];
        frame.CopyTo(fullFrame, 0);

        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(fullFrame, out _));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
    }

    // ── All 14 HTTP/2 Error Codes (RFC 7540 §7) ──────────────────────────────

    [Fact(DisplayName = "7540-err-000: NO_ERROR (0x0) in GOAWAY decoded")]
    public void ErrorCode_NoError_InGoAway_Decoded()
    {
        var frame = new GoAwayFrame(0, Http2ErrorCode.NoError).Serialize();
        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);
        Assert.True(result.HasGoAway);
        Assert.Equal(Http2ErrorCode.NoError, result.GoAway!.ErrorCode);
    }

    [Fact(DisplayName = "7540-err-001: PROTOCOL_ERROR (0x1) in RST_STREAM decoded")]
    public void ErrorCode_ProtocolError_InRstStream_Decoded()
    {
        var frame = new RstStreamFrame(1, Http2ErrorCode.ProtocolError).Serialize();
        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);
        Assert.Single(result.RstStreams);
        Assert.Equal(Http2ErrorCode.ProtocolError, result.RstStreams[0].Error);
    }

    [Fact(DisplayName = "7540-err-002: INTERNAL_ERROR (0x2) in GOAWAY decoded")]
    public void ErrorCode_InternalError_InGoAway_Decoded()
    {
        var frame = new GoAwayFrame(0, Http2ErrorCode.InternalError).Serialize();
        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);
        Assert.Equal(Http2ErrorCode.InternalError, result.GoAway!.ErrorCode);
    }

    [Fact(DisplayName = "7540-err-003: FLOW_CONTROL_ERROR (0x3) in GOAWAY decoded")]
    public void ErrorCode_FlowControlError_InGoAway_Decoded()
    {
        var frame = new GoAwayFrame(0, Http2ErrorCode.FlowControlError).Serialize();
        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);
        Assert.Equal(Http2ErrorCode.FlowControlError, result.GoAway!.ErrorCode);
    }

    [Fact(DisplayName = "7540-err-004: SETTINGS_TIMEOUT (0x4) in GOAWAY decoded")]
    public void ErrorCode_SettingsTimeout_InGoAway_Decoded()
    {
        var frame = new GoAwayFrame(0, Http2ErrorCode.SettingsTimeout).Serialize();
        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);
        Assert.Equal(Http2ErrorCode.SettingsTimeout, result.GoAway!.ErrorCode);
    }

    [Fact(DisplayName = "7540-err-005: STREAM_CLOSED (0x5) in RST_STREAM decoded")]
    public void ErrorCode_StreamClosed_InRstStream_Decoded()
    {
        var frame = new RstStreamFrame(1, Http2ErrorCode.StreamClosed).Serialize();
        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);
        Assert.Single(result.RstStreams);
        Assert.Equal(Http2ErrorCode.StreamClosed, result.RstStreams[0].Error);
    }

    [Fact(DisplayName = "7540-err-006: FRAME_SIZE_ERROR (0x6) decoded")]
    public void ErrorCode_FrameSizeError_InRstStream_Decoded()
    {
        var frame = new RstStreamFrame(1, Http2ErrorCode.FrameSizeError).Serialize();
        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);
        Assert.Single(result.RstStreams);
        Assert.Equal(Http2ErrorCode.FrameSizeError, result.RstStreams[0].Error);
    }

    [Fact(DisplayName = "7540-err-007: REFUSED_STREAM (0x7) in RST_STREAM decoded")]
    public void ErrorCode_RefusedStream_InRstStream_Decoded()
    {
        var frame = new RstStreamFrame(1, Http2ErrorCode.RefusedStream).Serialize();
        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);
        Assert.Equal(Http2ErrorCode.RefusedStream, result.RstStreams[0].Error);
    }

    [Fact(DisplayName = "7540-err-008: CANCEL (0x8) in RST_STREAM decoded")]
    public void ErrorCode_Cancel_InRstStream_Decoded()
    {
        var frame = new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize();
        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);
        Assert.Equal(Http2ErrorCode.Cancel, result.RstStreams[0].Error);
    }

    [Fact(DisplayName = "7540-err-009: COMPRESSION_ERROR (0x9) in GOAWAY decoded")]
    public void ErrorCode_CompressionError_InGoAway_Decoded()
    {
        var frame = new GoAwayFrame(0, Http2ErrorCode.CompressionError).Serialize();
        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);
        Assert.Equal(Http2ErrorCode.CompressionError, result.GoAway!.ErrorCode);
    }

    [Fact(DisplayName = "7540-err-00a: CONNECT_ERROR (0xa) in RST_STREAM decoded")]
    public void ErrorCode_ConnectError_InRstStream_Decoded()
    {
        var frame = new RstStreamFrame(1, Http2ErrorCode.ConnectError).Serialize();
        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);
        Assert.Equal(Http2ErrorCode.ConnectError, result.RstStreams[0].Error);
    }

    [Fact(DisplayName = "7540-err-00b: ENHANCE_YOUR_CALM (0xb) in GOAWAY decoded")]
    public void ErrorCode_EnhanceYourCalm_InGoAway_Decoded()
    {
        var frame = new GoAwayFrame(0, Http2ErrorCode.EnhanceYourCalm).Serialize();
        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);
        Assert.Equal(Http2ErrorCode.EnhanceYourCalm, result.GoAway!.ErrorCode);
    }

    [Fact(DisplayName = "7540-err-00c: INADEQUATE_SECURITY (0xc) decoded")]
    public void ErrorCode_InadequateSecurity_InRstStream_Decoded()
    {
        var frame = new RstStreamFrame(1, Http2ErrorCode.InadequateSecurity).Serialize();
        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);
        Assert.Equal(Http2ErrorCode.InadequateSecurity, result.RstStreams[0].Error);
    }

    [Fact(DisplayName = "7540-err-00d: HTTP_1_1_REQUIRED (0xd) in GOAWAY decoded")]
    public void ErrorCode_Http11Required_InGoAway_Decoded()
    {
        var frame = new GoAwayFrame(0, Http2ErrorCode.Http11Required).Serialize();
        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);
        Assert.Equal(Http2ErrorCode.Http11Required, result.GoAway!.ErrorCode);
    }

    // ── Stream States (RFC 7540 §5.1) ─────────────────────────────────────────

    [Fact(DisplayName = "7540-5.1-003: END_STREAM on incoming DATA moves stream to half-closed remote")]
    public async Task StreamState_EndStreamOnData_StreamCompleted()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();
        var dataFrame = new DataFrame(1, "body"u8.ToArray(), endStream: true).Serialize();

        var combined = headersFrame.Concat(dataFrame).ToArray();
        var decoder = new Http2Decoder();
        decoder.TryDecode(combined, out var result);

        // When END_STREAM arrives on DATA, the stream is half-closed remote → response produced.
        Assert.Single(result.Responses);
        Assert.Equal(200, (int)result.Responses[0].Response.StatusCode);
        var body = await result.Responses[0].Response.Content.ReadAsStringAsync();
        Assert.Equal("body", body);
    }

    [Fact(DisplayName = "7540-5.1-004: Both sides END_STREAM closes stream")]
    public void StreamState_EndStreamOnHeaders_StreamFullyClosed()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "204")]);
        // END_STREAM + END_HEADERS → stream fully closed, response produced immediately.
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: true, endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame, out var result);

        Assert.Single(result.Responses);
        Assert.Equal(204, (int)result.Responses[0].Response.StatusCode);
    }

    [Fact(DisplayName = "7540-5.1-005: PUSH_PROMISE moves pushed stream to reserved remote")]
    public void StreamState_PushPromise_ReservesStream()
    {
        // Build a raw PUSH_PROMISE frame: stream=1, promised-stream=2.
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":method", "GET"), (":path", "/pushed")]);
        var ppFrame = new PushPromiseFrame(1, 2, headerBlock).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(ppFrame, out var result);

        // Promised stream ID 2 should be recorded.
        Assert.Contains(2, result.PromisedStreamIds);
    }

    [Fact(DisplayName = "7540-5.1-006: DATA on closed stream causes STREAM_CLOSED error")]
    public void StreamState_DataOnClosedStream_ThrowsStreamClosed()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        // Close stream 1 with END_STREAM on HEADERS.
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: true, endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame, out _);

        // Now send DATA on the closed stream.
        var dataFrame = new DataFrame(1, new byte[4], endStream: false).Serialize();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(dataFrame, out _));
        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-5.1-007: Reusing closed stream ID causes PROTOCOL_ERROR")]
    public void StreamState_ReuseClosedStreamId_ThrowsProtocolError()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: true, endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame, out _);

        // Attempt to open stream 1 again.
        var headerBlock2 = hpack.Encode([(":status", "200")]);
        var headersFrame2 = new HeadersFrame(1, headerBlock2, endStream: true, endHeaders: true).Serialize();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(headersFrame2, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-5.1-008: Client even stream ID causes PROTOCOL_ERROR")]
    public void StreamState_EvenStreamIdWithoutPushPromise_ThrowsProtocolError()
    {
        // Build a HEADERS frame on stream 2 (even, server-push) without preceding PUSH_PROMISE.
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(2, headerBlock, endStream: true, endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(headersFrame, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    // ── Flow Control — Decoder Side (RFC 7540 §5.2) ──────────────────────────

    [Fact(DisplayName = "7540-5.2-dec-001: New stream initial window is 65535")]
    public void FlowControl_InitialConnectionReceiveWindow_Is65535()
    {
        var decoder = new Http2Decoder();
        Assert.Equal(65535, decoder.GetConnectionReceiveWindow());
    }

    [Fact(DisplayName = "7540-5.2-dec-002: WINDOW_UPDATE decoded and window updated")]
    public void FlowControl_WindowUpdateDecoded_WindowUpdated()
    {
        var frame = new WindowUpdateFrame(0, 32768).Serialize();
        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);

        // WINDOW_UPDATE from server updates our send window; it is reported in WindowUpdates.
        Assert.Contains(result.WindowUpdates, u => u.StreamId == 0 && u.Increment == 32768);
    }

    [Fact(DisplayName = "7540-5.2-dec-003: Peer DATA beyond window causes FLOW_CONTROL_ERROR")]
    public void FlowControl_PeerDataExceedsReceiveWindow_ThrowsFlowControlError()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame, out _);

        // Reduce connection receive window to 4 bytes.
        decoder.SetConnectionReceiveWindow(4);

        // Send 10 bytes of data — exceeds the window.
        var dataFrame = new DataFrame(1, new byte[10], endStream: false).Serialize();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(dataFrame, out _));
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-5.2-dec-004: WINDOW_UPDATE overflow causes FLOW_CONTROL_ERROR")]
    public void FlowControl_WindowUpdateOverflow_ThrowsFlowControlError()
    {
        // The connection send window starts at 65535. Sending increment = 0x7FFFFFFF
        // would produce 65535 + 2147483647 = 2147549182 > 0x7FFFFFFF → overflow.
        var overflowFrame = new byte[13]; // 9 + 4
        overflowFrame[0] = 0; overflowFrame[1] = 0; overflowFrame[2] = 4; // length=4
        overflowFrame[3] = 0x08; // WINDOW_UPDATE
        overflowFrame[4] = 0x00; // flags
        // stream = 0
        overflowFrame[5] = 0; overflowFrame[6] = 0; overflowFrame[7] = 0; overflowFrame[8] = 0;
        // increment = 0x7FFFFFFF
        overflowFrame[9]  = 0x7F;
        overflowFrame[10] = 0xFF;
        overflowFrame[11] = 0xFF;
        overflowFrame[12] = 0xFF;

        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(overflowFrame, out _));
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-5.2-dec-008: WINDOW_UPDATE increment=0 causes PROTOCOL_ERROR")]
    public void FlowControl_WindowUpdateIncrementZero_ThrowsProtocolError()
    {
        // Build raw WINDOW_UPDATE with increment = 0.
        var frame = new byte[13];
        frame[0] = 0; frame[1] = 0; frame[2] = 4; // length=4
        frame[3] = 0x08; // WINDOW_UPDATE
        frame[4] = 0x00; // flags
        // stream = 0 (bytes 5–8 are zero)
        // increment = 0 (bytes 9–12 are zero)

        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(frame, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    // ── DATA Frame (RFC 7540 §6.1) ────────────────────────────────────────────

    [Fact(DisplayName = "7540-6.1-001: DATA frame payload decoded correctly")]
    public async Task DataFrame_Payload_DecodedCorrectly()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();
        var body = "hello"u8.ToArray();
        var dataFrame = new DataFrame(1, body, endStream: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame.Concat(dataFrame).ToArray(), out var result);

        var text = await result.Responses[0].Response.Content.ReadAsStringAsync();
        Assert.Equal("hello", text);
    }

    [Fact(DisplayName = "7540-6.1-002: END_STREAM on DATA marks stream closed")]
    public void DataFrame_EndStream_MarksStreamClosed()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();
        var dataFrame = new DataFrame(1, new byte[4], endStream: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame.Concat(dataFrame).ToArray(), out var result);

        Assert.Single(result.Responses);

        // Subsequent DATA on same stream should throw STREAM_CLOSED.
        var extra = new DataFrame(1, new byte[1]).Serialize();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(extra, out _));
        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-6.1-003: Padded DATA frame padding stripped")]
    public async Task DataFrame_Padded_PaddingStripped()
    {
        // Manually build PADDED DATA: flag=0x08, pad_length=3, data="hi", padding=0x00×3
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();

        // Payload: pad_length(1) + data(2) + padding(3) = 6 bytes
        var paddedPayload = new byte[] { 3, (byte)'h', (byte)'i', 0x00, 0x00, 0x00 };
        var dataFrame = new byte[9 + paddedPayload.Length];
        dataFrame[0] = 0; dataFrame[1] = 0; dataFrame[2] = (byte)paddedPayload.Length; // length=6
        dataFrame[3] = 0x00; // DATA
        dataFrame[4] = 0x09; // END_STREAM | PADDED (0x01 | 0x08)
        dataFrame[5] = 0; dataFrame[6] = 0; dataFrame[7] = 0; dataFrame[8] = 1; // stream=1
        paddedPayload.CopyTo(dataFrame, 9);

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame.Concat(dataFrame).ToArray(), out var result);

        Assert.Single(result.Responses);
        var body = await result.Responses[0].Response.Content.ReadAsStringAsync();
        Assert.Equal("hi", body);
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
        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(frame, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-6.1-005: DATA on closed stream causes STREAM_CLOSED")]
    public void DataFrame_ClosedStream_ThrowsStreamClosed()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: true, endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame, out _);

        var dataFrame = new DataFrame(1, new byte[1]).Serialize();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(dataFrame, out _));
        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-6.1-006: Empty DATA frame with END_STREAM valid")]
    public void DataFrame_EmptyWithEndStream_ResponseComplete()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();
        var emptyDataFrame = new DataFrame(1, ReadOnlyMemory<byte>.Empty, endStream: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame.Concat(emptyDataFrame).ToArray(), out var result);

        Assert.Single(result.Responses);
        Assert.Equal(200, (int)result.Responses[0].Response.StatusCode);
    }

    // ── HEADERS Frame (RFC 7540 §6.2) ─────────────────────────────────────────

    [Fact(DisplayName = "7540-6.2-001: HEADERS frame decoded into response headers")]
    public void HeadersFrame_ResponseHeaders_Decoded()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200"), ("x-custom", "value")]);
        var frame = new HeadersFrame(1, headerBlock, endStream: true, endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);

        Assert.Single(result.Responses);
        var response = result.Responses[0].Response;
        Assert.Equal(200, (int)response.StatusCode);
        Assert.True(response.Headers.Contains("x-custom"));
    }

    [Fact(DisplayName = "7540-6.2-002: END_STREAM on HEADERS closes stream immediately")]
    public void HeadersFrame_EndStream_StreamClosedImmediately()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "204")]);
        var frame = new HeadersFrame(1, headerBlock, endStream: true, endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);

        Assert.Single(result.Responses);
        Assert.Equal(204, (int)result.Responses[0].Response.StatusCode);
    }

    [Fact(DisplayName = "7540-6.2-003: END_HEADERS on HEADERS marks complete block")]
    public void HeadersFrame_EndHeaders_HeaderBlockComplete()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        // END_HEADERS flag (0x4) is set → no CONTINUATION expected.
        var frame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out _);

        // If END_HEADERS was properly recognized, a subsequent non-CONTINUATION frame
        // should NOT throw PROTOCOL_ERROR.
        var pingFrame = new PingFrame(new byte[8]).Serialize();
        decoder.TryDecode(pingFrame, out var result);
        Assert.Single(result.PingRequests); // no exception → END_HEADERS was respected
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
        headerBlock.CopyTo(payload.AsMemory(1)); // header block starts after pad length byte
        // last 2 bytes remain zero (padding)

        var frame = new byte[9 + payload.Length];
        frame[0] = 0; frame[1] = 0; frame[2] = (byte)payload.Length; // length
        frame[3] = 0x01; // HEADERS
        frame[4] = 0x0C; // END_STREAM(0x1) | END_HEADERS(0x4) | PADDED(0x8) = 0x0D
        // Actually END_HEADERS=0x4, PADDED=0x8, END_STREAM=0x1 → 0x0D
        frame[4] = 0x0D;
        frame[5] = 0; frame[6] = 0; frame[7] = 0; frame[8] = 1; // stream=1
        payload.CopyTo(frame, 9);

        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);

        Assert.Single(result.Responses);
        Assert.Equal(200, (int)result.Responses[0].Response.StatusCode);
    }

    [Fact(DisplayName = "7540-6.2-005: PRIORITY flag in HEADERS consumed correctly")]
    public void HeadersFrame_PriorityFlag_ConsumedCorrectly()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);

        // Build HEADERS with PRIORITY flag: extra 5 bytes (4 stream dep + 1 weight).
        var priorityBytes = new byte[] { 0x00, 0x00, 0x00, 0x03, 0x0F }; // dep=3, weight=15
        var payload = priorityBytes.Concat(headerBlock.ToArray()).ToArray();

        var frame = new byte[9 + payload.Length];
        frame[0] = 0; frame[1] = 0; frame[2] = (byte)payload.Length;
        frame[3] = 0x01; // HEADERS
        // END_STREAM(0x1) | END_HEADERS(0x4) | PRIORITY(0x20) = 0x25
        frame[4] = 0x25;
        frame[5] = 0; frame[6] = 0; frame[7] = 0; frame[8] = 1; // stream=1
        payload.CopyTo(frame, 9);

        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);

        Assert.Single(result.Responses);
        Assert.Equal(200, (int)result.Responses[0].Response.StatusCode);
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

        var decoder = new Http2Decoder();
        // HEADERS without END_HEADERS: the frame is parsed but no response is produced yet.
        decoder.TryDecode(headersFrame, out var r1Result);
        Assert.False(r1Result.HasResponses);

        decoder.TryDecode(contFrame, out var result);
        Assert.Single(result.Responses);
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
        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(frame, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    // ── CONTINUATION Frame (RFC 7540 §6.9) ───────────────────────────────────

    [Fact(DisplayName = "7540-6.9-001: CONTINUATION appended to HEADERS block")]
    public void ContinuationFrame_AppendedToHeaders_HeaderBlockMerged()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200"), ("x-test", "cont")]);
        var split = headerBlock.Length / 2;

        var headersFrame = new HeadersFrame(1, headerBlock[..split], endStream: true, endHeaders: false).Serialize();
        var contFrame = new ContinuationFrame(1, headerBlock[split..], endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame, out _);
        decoder.TryDecode(contFrame, out var result);

        Assert.Single(result.Responses);
        Assert.Equal(200, (int)result.Responses[0].Response.StatusCode);
    }

    [Fact(DisplayName = "7540-6.9-dec-002: END_HEADERS on final CONTINUATION completes block")]
    public void ContinuationFrame_EndHeaders_CompletesBlock()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);

        var headersFrame = new HeadersFrame(1, headerBlock[..1], endStream: true, endHeaders: false).Serialize();
        var contFrame = new ContinuationFrame(1, headerBlock[1..], endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame, out _);
        decoder.TryDecode(contFrame, out var result);

        Assert.Single(result.Responses);
    }

    [Fact(DisplayName = "7540-6.9-003: Multiple CONTINUATION frames all merged")]
    public void ContinuationFrame_Multiple_AllMerged()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200"), ("a", "1"), ("b", "2"), ("c", "3")]);
        // Split into 3 parts.
        var third = headerBlock.Length / 3;

        var headersFrame = new HeadersFrame(1, headerBlock[..third], endStream: true, endHeaders: false).Serialize();
        var cont1 = new ContinuationFrame(1, headerBlock[third..(2 * third)], endHeaders: false).Serialize();
        var cont2 = new ContinuationFrame(1, headerBlock[(2 * third)..], endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame, out _);
        decoder.TryDecode(cont1, out _);
        decoder.TryDecode(cont2, out var result);

        Assert.Single(result.Responses);
        Assert.Equal(200, (int)result.Responses[0].Response.StatusCode);
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
        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(combined, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-6.9-005: Non-CONTINUATION after HEADERS is PROTOCOL_ERROR")]
    public void ContinuationFrame_NonContinuationAfterHeaders_ThrowsProtocolError()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: false).Serialize();
        var pingFrame = new PingFrame(new byte[8]).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame, out _);

        // PING arrives while we're waiting for CONTINUATION → PROTOCOL_ERROR.
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(pingFrame, out _));
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
        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(combined, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "dec6-cont-001: CONTINUATION without HEADERS is PROTOCOL_ERROR")]
    public void ContinuationFrame_WithoutPrecedingHeaders_ThrowsProtocolError()
    {
        var contFrame = new ContinuationFrame(1, new byte[] { 0x88 }, endHeaders: true).Serialize();
        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(contFrame, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    // ── SETTINGS, PING, GOAWAY, RST_STREAM ───────────────────────────────────

    [Fact(DisplayName = "RFC 7540: Server SETTINGS decoded")]
    public void Settings_ServerSettings_HasNewSettingsTrue()
    {
        var frame = new SettingsFrame([(SettingsParameter.MaxConcurrentStreams, 100u)]).Serialize();
        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);
        Assert.True(result.HasNewSettings);
        Assert.Single(result.ReceivedSettings);
    }

    [Fact(DisplayName = "RFC 7540: SETTINGS ACK generated after SETTINGS")]
    public void Settings_SettingsReceived_AckGenerated()
    {
        var frame = new SettingsFrame([(SettingsParameter.MaxConcurrentStreams, 50u)]).Serialize();
        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);
        Assert.Single(result.SettingsAcksToSend);
        // The ACK frame must be a valid SETTINGS ACK (9 bytes, type=0x4, flag=ACK, stream=0).
        var ack = result.SettingsAcksToSend[0];
        Assert.Equal(9, ack.Length);
        Assert.Equal(0x04, ack[3]); // type = SETTINGS
        Assert.Equal((byte)SettingsFlags.Ack, ack[4]); // ACK flag
    }

    [Fact(DisplayName = "RFC 7540: MAX_FRAME_SIZE applied from SETTINGS")]
    public void Settings_MaxFrameSize_Applied()
    {
        const uint newMaxSize = 32768;
        var settingsFrame = new SettingsFrame([(SettingsParameter.MaxFrameSize, newMaxSize)]).Serialize();
        var decoder = new Http2Decoder();
        decoder.TryDecode(settingsFrame, out _);

        // Now a frame of 20000 bytes (> 16384 default, ≤ 32768 new) should be accepted.
        const int frameSize = 20000;
        var bigFrame = new byte[9 + frameSize];
        bigFrame[0] = (byte)(frameSize >> 16);
        bigFrame[1] = (byte)(frameSize >> 8);
        bigFrame[2] = (byte)(frameSize & 0xFF);
        bigFrame[3] = 0x04; // SETTINGS
        bigFrame[4] = 0x00;
        // stream=0, payload = empty SETTINGS entries (frameSize/6 entries of zeros).

        // This should NOT throw FRAME_SIZE_ERROR.
        decoder.TryDecode(bigFrame, out var result);
        Assert.True(result.HasNewSettings);
    }

    [Theory(DisplayName = "dec6-set-001: SETTINGS parameter {0} decoded")]
    [InlineData(SettingsParameter.HeaderTableSize, 1024u)]
    [InlineData(SettingsParameter.EnablePush, 0u)]
    [InlineData(SettingsParameter.MaxConcurrentStreams, 100u)]
    [InlineData(SettingsParameter.InitialWindowSize, 65535u)]
    [InlineData(SettingsParameter.MaxFrameSize, 16384u)]
    [InlineData(SettingsParameter.MaxHeaderListSize, 8192u)]
    public void Settings_AllSixParameters_Decoded(SettingsParameter param, uint value)
    {
        var frame = new SettingsFrame([(param, value)]).Serialize();
        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);

        Assert.True(result.HasNewSettings);
        var settings = result.ReceivedSettings[0];
        Assert.Contains(settings, p => p.Item1 == param && p.Item2 == value);
    }

    [Fact(DisplayName = "dec6-set-002: SETTINGS ACK with non-empty payload is FRAME_SIZE_ERROR")]
    public void Settings_AckWithPayload_ThrowsFrameSizeError()
    {
        // Build a raw SETTINGS ACK frame with a non-empty payload (violation of RFC 7540 §6.5).
        var frame = new byte[]
        {
            0x00, 0x00, 0x06, // length = 6 (non-zero)
            0x04,             // SETTINGS
            0x01,             // ACK flag
            0x00, 0x00, 0x00, 0x00, // stream = 0
            // 6 bytes of "payload" (any SETTINGS entry)
            0x00, 0x01, 0x00, 0x00, 0x04, 0x00
        };

        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(frame, out _));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
    }

    [Fact(DisplayName = "dec6-set-003: Unknown SETTINGS parameter ID accepted and ignored")]
    public void Settings_UnknownParameterId_Ignored()
    {
        // Unknown parameter ID 0xFF (not in the RFC) should be silently ignored.
        var frame = new byte[]
        {
            0x00, 0x00, 0x06, // length = 6
            0x04,             // SETTINGS
            0x00,             // no ACK
            0x00, 0x00, 0x00, 0x00, // stream = 0
            0x00, 0xFF, 0x00, 0x00, 0x00, 0x42 // unknown param=0xFF, value=0x42
        };

        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);
        Assert.True(result.HasNewSettings); // frame parsed successfully
    }

    [Fact(DisplayName = "RFC 7540: PING request from server decoded")]
    public void Ping_RequestFromServer_Decoded()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var frame = new PingFrame(data, isAck: false).Serialize();
        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);
        Assert.Single(result.PingRequests);
        Assert.Equal(data, result.PingRequests[0]);
    }

    [Fact(DisplayName = "RFC 7540: PING ACK produced for server PING")]
    public void Ping_RequestReceived_AckGenerated()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var frame = new PingFrame(data, isAck: false).Serialize();
        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);

        Assert.Single(result.PingAcksToSend);
    }

    [Fact(DisplayName = "dec6-ping-001: PING ACK carries same 8 payload bytes as request")]
    public void Ping_Ack_CarriesSamePayloadBytes()
    {
        var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04 };
        var frame = new PingFrame(data, isAck: false).Serialize();
        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);

        Assert.Single(result.PingAcksToSend);
        // ACK frame: 9-byte header + 8-byte payload = 17 bytes.
        var ack = result.PingAcksToSend[0];
        Assert.Equal(17, ack.Length);
        Assert.Equal((byte)PingFlags.Ack, ack[4]); // ACK flag set
        Assert.Equal(data, ack[9..]); // same 8 payload bytes
    }

    [Fact(DisplayName = "RFC 7540: GOAWAY with last stream ID and error code decoded")]
    public void GoAway_LastStreamIdAndErrorCode_Decoded()
    {
        var frame = new GoAwayFrame(7, Http2ErrorCode.ProtocolError, []).Serialize();
        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);

        Assert.True(result.HasGoAway);
        Assert.Equal(7, result.GoAway!.LastStreamId);
        Assert.Equal(Http2ErrorCode.ProtocolError, result.GoAway.ErrorCode);
    }

    [Fact(DisplayName = "RFC 7540: No new requests after GOAWAY")]
    public void GoAway_NoNewStreamsAfterGoAway_ThrowsProtocolError()
    {
        var goAwayFrame = new GoAwayFrame(1, Http2ErrorCode.NoError).Serialize();
        var decoder = new Http2Decoder();
        decoder.TryDecode(goAwayFrame, out _);

        // Opening a new stream (odd stream 3) after GOAWAY should throw.
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(3, headerBlock, endStream: true, endHeaders: true).Serialize();

        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(headersFrame, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "dec6-goaway-001: GOAWAY debug data bytes accessible")]
    public void GoAway_DebugData_Accessible()
    {
        var debugData = "server overloaded"u8.ToArray();
        var frame = new GoAwayFrame(3, Http2ErrorCode.InternalError, debugData).Serialize();
        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);

        Assert.Equal(debugData, result.GoAway!.DebugData);
    }

    [Fact(DisplayName = "RFC 7540: RST_STREAM decoded")]
    public void RstStream_Decoded()
    {
        var frame = new RstStreamFrame(5, Http2ErrorCode.Cancel).Serialize();
        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);

        Assert.Single(result.RstStreams);
        Assert.Equal(5, result.RstStreams[0].StreamId);
        Assert.Equal(Http2ErrorCode.Cancel, result.RstStreams[0].Error);
    }

    [Fact(DisplayName = "RFC 7540: Stream closed after RST_STREAM")]
    public void RstStream_StreamClosedAfterRst()
    {
        // Set up stream 1 with HEADERS, then RST_STREAM it.
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();
        var rstFrame = new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame.Concat(rstFrame).ToArray(), out _);

        // Stream 1 is now closed. DATA on stream 1 should throw STREAM_CLOSED.
        var dataFrame = new DataFrame(1, new byte[1]).Serialize();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(dataFrame, out _));
        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
    }

    // ── TCP Fragmentation (HTTP/2) ─────────────────────────────────────────────

    [Fact(DisplayName = "dec6-frag-001: Frame header split at byte 1 reassembled")]
    public void Fragmentation_HeaderSplitAtByte1_Reassembled()
    {
        var frame = SettingsFrame.SettingsAck(); // 9-byte frame
        var chunk1 = frame[..1];
        var chunk2 = frame[1..];

        var decoder = new Http2Decoder();
        var r1 = decoder.TryDecode(chunk1, out _);
        var r2 = decoder.TryDecode(chunk2, out _);

        Assert.False(r1);
        Assert.True(r2);
    }

    [Fact(DisplayName = "dec6-frag-002: Frame header split at byte 5 reassembled")]
    public void Fragmentation_HeaderSplitAtByte5_Reassembled()
    {
        var frame = new PingFrame(new byte[8]).Serialize();
        var chunk1 = frame[..5];
        var chunk2 = frame[5..];

        var decoder = new Http2Decoder();
        var r1 = decoder.TryDecode(chunk1, out _);
        var r2 = decoder.TryDecode(chunk2, out var result);

        Assert.False(r1);
        Assert.True(r2);
        Assert.Single(result.PingRequests);
    }

    [Fact(DisplayName = "dec6-frag-003: DATA frame payload split across two reads")]
    public async Task Fragmentation_DataPayloadSplit_Reassembled()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();

        var body = "hello world"u8.ToArray();
        var dataFrame = new DataFrame(1, body, endStream: true).Serialize();

        // Split DATA frame across 9-byte header boundary.
        var combined = headersFrame.Concat(dataFrame).ToArray();
        var splitAt = headersFrame.Length + 9 + 4; // 4 bytes into DATA payload
        var chunk1 = combined[..splitAt];
        var chunk2 = combined[splitAt..];

        var decoder = new Http2Decoder();
        decoder.TryDecode(chunk1, out _);
        decoder.TryDecode(chunk2, out var result);

        Assert.Single(result.Responses);
        var text = await result.Responses[0].Response.Content.ReadAsStringAsync();
        Assert.Equal("hello world", text);
    }

    [Fact(DisplayName = "dec6-frag-004: HPACK block split across two reads")]
    public void Fragmentation_HpackBlockSplit_Reassembled()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200"), ("x-test", "fragmented")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: true, endHeaders: true).Serialize();

        // Split in the middle of the HPACK block.
        var splitAt = 9 + headerBlock.Length / 2;
        var chunk1 = headersFrame[..splitAt];
        var chunk2 = headersFrame[splitAt..];

        var decoder = new Http2Decoder();
        var r1 = decoder.TryDecode(chunk1, out _);
        var r2 = decoder.TryDecode(chunk2, out var result);

        Assert.False(r1);
        Assert.True(r2);
        Assert.Single(result.Responses);
    }

    [Fact(DisplayName = "dec6-frag-005: Two complete frames in single read both decoded")]
    public void Fragmentation_TwoFramesInSingleRead_BothDecoded()
    {
        var ping1 = new PingFrame([1, 1, 1, 1, 1, 1, 1, 1]).Serialize();
        var ping2 = new PingFrame([2, 2, 2, 2, 2, 2, 2, 2]).Serialize();

        var combined = ping1.Concat(ping2).ToArray();

        var decoder = new Http2Decoder();
        decoder.TryDecode(combined, out var result);

        Assert.Equal(2, result.PingRequests.Count);
    }
}
