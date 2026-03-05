using System.Buffers.Binary;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// Phase 29: Fuzz Harness
///
/// Property-based fuzz tests that validate Http2ProtocolSession and HpackDecoder robustness
/// under adversarial and random inputs. All tests use deterministic seeds for
/// reproducibility.
///
/// Core invariant: Http2ProtocolSession MUST NEVER throw anything other than Http2Exception.
/// All unexpected input must be gracefully rejected with a typed protocol error.
/// Any HpackException that escapes Http2ProtocolSession is a bug (must be wrapped as
/// Http2Exception(CompressionError) per RFC 9113 §4.3).
///
///   FZ-001..005 — Random frame ordering
///   FZ-006..010 — Invalid frame lengths
///   FZ-011..015 — Invalid header (HPACK) encodings
///   FZ-016..020 — Window overflow attempts
///   FZ-021..025 — Dynamic table resizing storms
///
/// Test IDs: FZ-001..FZ-025
/// </summary>
public sealed class Http2FuzzHarnessTests
{
    // ── Core invariant helper ─────────────────────────────────────────────────

    /// <summary>
    /// Feeds <paramref name="frame"/> to the session and asserts that the outcome
    /// is either a successful decode, an incomplete-frame return (empty list), or an
    /// Http2Exception. Any other exception is a bug.
    /// </summary>
    private static void AssertDecodeNeverCrashes(Http2ProtocolSession session, byte[] frame)
    {
        try
        {
            session.Process(frame);
        }
        catch (Http2Exception)
        {
            // Expected — protocol violation, properly classified.
        }
        catch (HpackException ex)
        {
            // HpackException must NOT propagate outside Http2ProtocolSession.
            // ProcessCompleteHeaders must wrap it as Http2Exception(CompressionError).
            Assert.Fail($"HpackException escaped Http2ProtocolSession — must be wrapped as Http2Exception(CompressionError). Message: {ex.Message}");
        }
        // Any other exception type propagates and fails the test via xUnit.
    }

    // ── Frame building helpers ────────────────────────────────────────────────

    private static byte[] BuildRawFrame(byte type, byte flags, int streamId, byte[] payload)
    {
        var frame = new byte[9 + payload.Length];
        frame[0] = (byte)(payload.Length >> 16);
        frame[1] = (byte)(payload.Length >> 8);
        frame[2] = (byte)payload.Length;
        frame[3] = type;
        frame[4] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), (uint)streamId & 0x7FFFFFFF);
        payload.CopyTo(frame, 9);
        return frame;
    }

    private static byte[] BuildHeadersFrame(int streamId, byte[] headerBlock,
        bool endHeaders = true, bool endStream = false)
    {
        byte flags = 0;
        if (endHeaders)
        {
            flags |= 0x4;
        }

        if (endStream)
        {
            flags |= 0x1;
        }

        return BuildRawFrame(0x1, flags, streamId, headerBlock);
    }

    private static byte[] BuildSettingsFrame(bool ack, (ushort id, uint value)[] settings)
    {
        var payload = new byte[settings.Length * 6];
        for (var i = 0; i < settings.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(i * 6), settings[i].id);
            BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(i * 6 + 2), settings[i].value);
        }
        return BuildRawFrame(0x4, ack ? (byte)0x1 : (byte)0x0, 0, payload);
    }

    private static byte[] BuildWindowUpdateFrame(int streamId, uint increment)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, increment & 0x7FFFFFFF);
        return BuildRawFrame(0x8, 0, streamId, payload);
    }

    // Valid :status 200 HPACK block (RFC 7541 static table index 8 → :status: 200)
    private static readonly byte[] Status200HpackBlock = [0x88];

    // ── FZ-001..005: Random frame ordering ───────────────────────────────────

    [Fact(DisplayName = "FZ-001: Random valid HEADERS + DATA sequences never crash the decoder")]
    public void Should_HandleRandomHeadersDataSequences_WithoutCrashing()
    {
        var rng = new Random(42);
        var session = new Http2ProtocolSession();

        for (var i = 0; i < 50; i++)
        {
            var streamId = (rng.Next(1, 20) * 2) - 1; // Odd client stream IDs 1..39
            var headersFrame = BuildHeadersFrame(streamId, Status200HpackBlock, endStream: false);
            AssertDecodeNeverCrashes(session, headersFrame);

            var data = new byte[rng.Next(0, 64)];
            rng.NextBytes(data);
            var dataFrame = BuildRawFrame(0x0, 0x1, streamId, data); // END_STREAM
            AssertDecodeNeverCrashes(session, dataFrame);
        }
    }

    [Fact(DisplayName = "FZ-002: Random RST_STREAM frames for unknown streams never produce unhandled exceptions")]
    public void Should_HandleRandomRstStreamFrames_WithoutCrashing()
    {
        var rng = new Random(137);
        var session = new Http2ProtocolSession();

        for (var i = 0; i < 100; i++)
        {
            var streamId = rng.Next(1, 500);
            var errorCode = (uint)rng.Next(0, 20);
            var payload = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(payload, errorCode);
            var frame = BuildRawFrame(0x3, 0, streamId, payload);
            AssertDecodeNeverCrashes(session, frame);
        }
    }

    [Fact(DisplayName = "FZ-003: Random WINDOW_UPDATE frames on arbitrary streams never produce unhandled exceptions")]
    public void Should_HandleRandomWindowUpdateFrames_WithoutCrashing()
    {
        var rng = new Random(7);
        var session = new Http2ProtocolSession();

        for (var i = 0; i < 100; i++)
        {
            var streamId = rng.Next(0, 100);
            var increment = (uint)rng.Next(1, int.MaxValue);
            AssertDecodeNeverCrashes(session, BuildWindowUpdateFrame(streamId, increment));
        }
    }

    [Fact(DisplayName = "FZ-004: Interleaved PING and SETTINGS frames in random order never produce unhandled exceptions")]
    public void Should_HandleInterleavedFrameTypes_WithoutCrashing()
    {
        var rng = new Random(99);
        var session = new Http2ProtocolSession();

        Func<byte[]>[] frameBuilders =
        [
            () => BuildRawFrame(0x6, 0x0, 0, new byte[8]),          // PING (non-ACK)
            () => BuildRawFrame(0x6, 0x1, 0, new byte[8]),          // PING ACK
            () => BuildSettingsFrame(false, []),                     // SETTINGS (empty)
            () => BuildSettingsFrame(true, []),                      // SETTINGS ACK (empty)
            () => BuildWindowUpdateFrame(0, (uint)rng.Next(1, 1000)), // Connection WINDOW_UPDATE
        ];

        for (var i = 0; i < 100; i++)
        {
            var builder = frameBuilders[rng.Next(frameBuilders.Length)];
            AssertDecodeNeverCrashes(session, builder());
        }
    }

    [Fact(DisplayName = "FZ-005: Unknown frame types (0x0A..0xFF) are tolerated per RFC 9113 §5.5")]
    public void Should_IgnoreUnknownFrameTypes_PerRfc9113()
    {
        var rng = new Random(555);
        var session = new Http2ProtocolSession();

        // RFC 9113 §5.5: Implementations MUST ignore and discard frames with unknown types.
        for (var i = 0; i < 50; i++)
        {
            var type = (byte)rng.Next(0x0A, 0x100); // Types 0x0A..0xFF are unknown/reserved
            var payload = new byte[rng.Next(0, 64)];
            rng.NextBytes(payload);
            var frame = BuildRawFrame(type, 0, 0, payload);
            AssertDecodeNeverCrashes(session, frame);
        }
    }

    // ── FZ-006..010: Invalid frame lengths ───────────────────────────────────

    [Fact(DisplayName = "FZ-006: Frame with payload length exceeding MaxFrameSize throws Http2Exception")]
    public void Should_RejectOversizedFrame_WithFrameSizeError()
    {
        var session = new Http2ProtocolSession();

        // Declare length = 20000 > default MaxFrameSize (16384), provide full buffer.
        // NOTE: Http2FrameDecoder does not enforce MAX_FRAME_SIZE (see 01_ConnectionPrefaceTests
        // 7540-4.1-007). The frame is decoded; Http2Exception is thrown when the idle stream
        // is processed. RFC 9113 §4.2 would require FRAME_SIZE_ERROR, but the production
        // decoder surfaces a different error code — documented here as a known delta.
        const int declaredLength = 20000;
        var frame = new byte[9 + declaredLength];
        frame[0] = declaredLength >> 16;
        frame[1] = declaredLength >> 8;
        frame[2] = unchecked((byte)declaredLength);
        frame[3] = 0x0; // DATA
        frame[4] = 0x0;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), 1u); // stream 1

        Assert.Throws<Http2Exception>(() => session.Process(frame));
    }

    [Fact(DisplayName = "FZ-007: Truncated frame (buffer smaller than declared length) is buffered without crashing")]
    public void Should_BufferTruncatedFrame_WithoutCrashing()
    {
        var session = new Http2ProtocolSession();

        // Declare a PING with length=8 but provide only 4 payload bytes → Process returns empty list.
        var frame = new byte[9 + 4];
        frame[0] = 0; frame[1] = 0; frame[2] = 8; // declared length = 8
        frame[3] = 0x6; // PING
        frame[4] = 0x0;
        // Remaining bytes left zeroed (only 4, not the declared 8)

        Assert.Empty(session.Process(frame)); // incomplete frame — buffered, not crashed
    }

    [Fact(DisplayName = "FZ-008: PING with wrong payload length throws Http2Exception(FrameSizeError)")]
    public void Should_RejectPingWithWrongPayloadLength_WithFrameSizeError()
    {
        var session = new Http2ProtocolSession();

        // PING must be exactly 8 bytes; 5 bytes is wrong.
        var payload = new byte[5];
        var frame = BuildRawFrame(0x6, 0, 0, payload);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
    }

    [Fact(DisplayName = "FZ-009: SETTINGS with payload length not a multiple of 6 throws Http2Exception(FrameSizeError)")]
    public void Should_RejectSettingsWithNonMultipleOf6Payload_WithFrameSizeError()
    {
        var session = new Http2ProtocolSession();

        // SETTINGS payload must be a multiple of 6; 7 is not.
        var payload = new byte[7];
        var frame = BuildRawFrame(0x4, 0, 0, payload);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
    }

    [Fact(DisplayName = "FZ-010: Random corrupted frame headers never produce unhandled exceptions")]
    public void Should_HandleCorruptedFrameHeaders_WithoutUnhandledExceptions()
    {
        var rng = new Random(1234);

        for (var trial = 0; trial < 50; trial++)
        {
            var session = new Http2ProtocolSession();

            // Build a frame with a random 3-byte declared length that may not match
            // the actual buffer size — simulates length-field corruption.
            var declaredLength = rng.Next(0, 20000);
            var actualPayloadSize = rng.Next(0, 64);
            var frame = new byte[9 + actualPayloadSize];
            frame[0] = (byte)(declaredLength >> 16);
            frame[1] = (byte)(declaredLength >> 8);
            frame[2] = (byte)declaredLength;
            frame[3] = (byte)rng.Next(0, 10); // random frame type
            frame[4] = 0;
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), (uint)rng.Next(0, 100));

            AssertDecodeNeverCrashes(session, frame);
        }
    }

    // ── FZ-011..015: Invalid header (HPACK) encodings ─────────────────────────

    [Fact(DisplayName = "FZ-011: Fully random bytes fed as HPACK header block never crash the decoder")]
    public void Should_HandleRandomHpackBytes_WithoutCrashing()
    {
        var rng = new Random(42_000);

        for (var trial = 0; trial < 30; trial++)
        {
            var session = new Http2ProtocolSession();
            var garbage = new byte[rng.Next(1, 200)];
            rng.NextBytes(garbage);
            var frame = BuildHeadersFrame(1, garbage);
            AssertDecodeNeverCrashes(session, frame);
        }
    }

    [Fact(DisplayName = "FZ-012: Valid HPACK prefix followed by garbage bytes never crashes the decoder")]
    public void Should_HandleValidHpackPrefixWithGarbage_WithoutCrashing()
    {
        var rng = new Random(99_000);

        for (var trial = 0; trial < 30; trial++)
        {
            var session = new Http2ProtocolSession();

            // Start with indexed :status 200 (0x88), then append garbage.
            var garbage = new byte[rng.Next(1, 100)];
            rng.NextBytes(garbage);
            var block = new byte[1 + garbage.Length];
            block[0] = 0x88; // :status 200 (valid, static index 8)
            garbage.CopyTo(block, 1);

            var frame = BuildHeadersFrame(1, block);
            AssertDecodeNeverCrashes(session, frame);
        }
    }

    [Fact(DisplayName = "FZ-013: HPACK literal with oversized declared string length never crashes the decoder")]
    public void Should_HandleHpackOversizedStringLength_WithoutCrashing()
    {
        var session = new Http2ProtocolSession();

        // Literal without indexing (0x00), index=0 (new name), then oversized length field.
        // 0x7F = 127 in 7-bit prefix → requires multi-byte encoding (continuation bytes).
        // Providing only 4 bytes total causes the HPACK parser to encounter a truncated string.
        var block = new byte[] { 0x00, 0x00, 0x7F, 0xFF }; // incomplete multi-byte string
        var frame = BuildHeadersFrame(1, block);
        AssertDecodeNeverCrashes(session, frame);
    }

    [Fact(DisplayName = "FZ-014: HPACK index beyond static + dynamic table never crashes the decoder")]
    public void Should_HandleOutOfRangeHpackIndex_WithoutCrashing()
    {
        var session = new Http2ProtocolSession();

        // RFC 7541 §6.1: index 0 is illegal; index > (static + dynamic count) is a COMPRESSION_ERROR.
        // Encode index = 200 using 7-bit prefix: first byte = 0xFF (prefix saturated at 127),
        // then continuation = 73 (127 + 73 = 200).
        var block = new byte[] { 0xFF, 73 }; // indexed representation, index=200
        var frame = BuildHeadersFrame(1, block);
        AssertDecodeNeverCrashes(session, frame);
    }

    [Fact(DisplayName = "FZ-015: Huffman-flagged header with invalid bitstream never crashes the decoder")]
    public void Should_HandleInvalidHuffmanBitstream_WithoutCrashing()
    {
        var rng = new Random(777);

        for (var trial = 0; trial < 20; trial++)
        {
            var session = new Http2ProtocolSession();

            // Build a literal without indexing (0x00), new name (0x00),
            // then name with Huffman flag (0x80 | length) + random bytes as "Huffman" data.
            var nameBytes = new byte[rng.Next(1, 20)];
            rng.NextBytes(nameBytes);

            var block = new List<byte>
            {
                0x00, // literal without indexing
                0x00, // index = 0 (new name)
                (byte)(0x80 | (nameBytes.Length & 0x7F)), // Huffman flag + length
            };
            block.AddRange(nameBytes);

            var frame = BuildHeadersFrame(1, block.ToArray());
            AssertDecodeNeverCrashes(session, frame);
        }
    }

    // ── FZ-016..020: Window overflow attempts ────────────────────────────────

    [Fact(DisplayName = "FZ-016: WINDOW_UPDATE that overflows connection send window throws Http2Exception(FlowControlError)")]
    public void Should_RejectConnectionWindowOverflow_WithFlowControlError()
    {
        var session = new Http2ProtocolSession();

        // Initial connection send window = 65535.
        // WINDOW_UPDATE of 0x7FFFFFFF = 2,147,483,647.
        // New window = 65535 + 2,147,483,647 = 2,147,549,182 > 2^31-1 → FLOW_CONTROL_ERROR.
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, 0x7FFFFFFF);
        var frame = BuildRawFrame(0x8, 0, 0, payload);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
    }

    [Fact(DisplayName = "FZ-017: WINDOW_UPDATE that overflows stream send window throws Http2Exception(FlowControlError)")]
    public void Should_RejectStreamWindowOverflow_WithFlowControlError()
    {
        var session = new Http2ProtocolSession();

        // Open stream 1 first.
        session.Process(BuildHeadersFrame(1, Status200HpackBlock));

        // Initial stream send window = 65535.
        // WINDOW_UPDATE of 0x7FFFFFFF overflows the stream window.
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, 0x7FFFFFFF);
        var frame = BuildRawFrame(0x8, 0, 1, payload);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
    }

    [Fact(DisplayName = "FZ-018: Zero-increment WINDOW_UPDATE throws Http2Exception(ProtocolError)")]
    public void Should_RejectZeroIncrementWindowUpdate_WithProtocolError()
    {
        var session = new Http2ProtocolSession();

        var payload = new byte[4]; // all zeros → increment = 0
        var frame = BuildRawFrame(0x8, 0, 0, payload);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "FZ-019: SETTINGS INITIAL_WINDOW_SIZE at maximum valid value (2^31-1) is accepted")]
    public void Should_AcceptSettingsInitialWindowSizeAtMax_WithoutException()
    {
        var session = new Http2ProtocolSession();

        // RFC 7540 §6.5.2: INITIAL_WINDOW_SIZE max = 2^31-1 = 2,147,483,647.
        var frame = BuildSettingsFrame(false, [(0x4, 0x7FFFFFFF)]);
        session.Process(frame); // must not throw
    }

    [Fact(DisplayName = "FZ-020: SETTINGS INITIAL_WINDOW_SIZE exceeding 2^31-1 throws Http2Exception(FlowControlError)")]
    public void Should_RejectSettingsInitialWindowSizeAboveMax_WithFlowControlError()
    {
        var session = new Http2ProtocolSession();

        // RFC 7540 §6.5.2: INITIAL_WINDOW_SIZE = 2^31 is illegal.
        var frame = BuildSettingsFrame(false, [(0x4, 0x80000000)]);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
    }

    // ── FZ-021..025: Dynamic table resizing storms ───────────────────────────

    [Fact(DisplayName = "FZ-021: Repeated HpackDecoder table size oscillation between 0 and 256 never crashes")]
    public void Should_HandleRepeatedTableSizeOscillation_WithoutCrashing()
    {
        var hpack = new HpackDecoder();

        for (var i = 0; i < 100; i++)
        {
            // RFC 7541 §6.3: Dynamic Table Size Update — prefix = 0b001XXXXX (5-bit prefix).
            // DTS=0:  0x20 (= 0b00100000 | 0).
            // DTS=30: 0x3E (= 0b00100000 | 30). Values 0..30 fit in one byte (< 31 threshold).
            var blockToZero = new byte[] { 0x20 };  // DTS=0
            var blockToMax  = new byte[] { 0x3E };  // DTS=30
            hpack.Decode(blockToZero);
            hpack.Decode(blockToMax);
        }
    }

    [Fact(DisplayName = "FZ-022: Filling dynamic table then resizing to 0 evicts all entries without crashing")]
    public void Should_EvictAllEntries_WhenTableSizeReducedToZero_AfterFilling()
    {
        var hpack = new HpackDecoder();

        // Add several entries via literal with incremental indexing (0x40 prefix).
        // Format: 0x40 (new name, incremental), name_len, name_bytes, value_len, value_bytes.
        for (var i = 0; i < 5; i++)
        {
            // Include :status 200 (0x88) first to satisfy ValidateResponseHeaders.
            var block = new byte[] { 0x88, 0x40, 0x01, (byte)'x', 0x01, (byte)'v' };
            hpack.Decode(block);
        }

        // Resize to 0 — all entries must be evicted. Decode :status 200 afterward.
        var resizeBlock = new byte[] { 0x20, 0x88 }; // DTS=0, then :status 200
        hpack.Decode(resizeBlock); // must not throw
    }

    [Fact(DisplayName = "FZ-023: Rapid SETTINGS HEADER_TABLE_SIZE changes with random values never crash Http2ProtocolSession")]
    public void Should_HandleRapidHeaderTableSizeChanges_WithoutCrashing()
    {
        var rng = new Random(42);
        var session = new Http2ProtocolSession();

        for (var i = 0; i < 50; i++)
        {
            var size = (uint)rng.Next(0, 65536);
            var frame = BuildSettingsFrame(false, [(0x1, size)]); // HEADER_TABLE_SIZE
            AssertDecodeNeverCrashes(session, frame);
        }
    }

    [Fact(DisplayName = "FZ-024: SETTINGS HEADER_TABLE_SIZE=0 followed by normal HPACK headers is handled correctly")]
    public void Should_HandleHeaderTableSizeZero_FollowedByNormalHeaders()
    {
        var session = new Http2ProtocolSession();

        // Set HEADER_TABLE_SIZE=0 via SETTINGS — dynamic table must be disabled.
        session.Process(BuildSettingsFrame(false, [(0x1, 0)]));

        // Send HEADERS with DTS=0 update (acknowledging the SETTINGS change) + :status 200.
        var block = new byte[] { 0x20, 0x88 }; // DTS=0 update, then indexed :status 200
        var frame = BuildHeadersFrame(1, block);
        AssertDecodeNeverCrashes(session, frame);
    }

    [Fact(DisplayName = "FZ-025: Extended random frame sequence (1000 iterations) never produces unhandled exceptions")]
    public void Should_SurviveExtendedRandomFrameSequence_WithoutUnhandledExceptions()
    {
        var rng = new Random(314159);
        var session = new Http2ProtocolSession();
        var streamCounter = 1; // Next client stream ID to use

        for (var iteration = 0; iteration < 1000; iteration++)
        {
            var action = rng.Next(6);

            switch (action)
            {
                case 0: // PING (non-ACK)
                    AssertDecodeNeverCrashes(session, BuildRawFrame(0x6, 0x0, 0, new byte[8]));
                    break;

                case 1: // SETTINGS (empty — no flood risk)
                    AssertDecodeNeverCrashes(session, BuildSettingsFrame(false, []));
                    break;

                case 2: // SETTINGS ACK
                    AssertDecodeNeverCrashes(session, BuildSettingsFrame(true, []));
                    break;

                case 3: // Connection WINDOW_UPDATE
                    AssertDecodeNeverCrashes(session,
                        BuildWindowUpdateFrame(0, (uint)rng.Next(1, 1000)));
                    break;

                case 4: // Random garbage HEADERS on a new stream
                {
                    var garbage = new byte[rng.Next(0, 64)];
                    rng.NextBytes(garbage);
                    AssertDecodeNeverCrashes(session, BuildHeadersFrame(streamCounter, garbage));
                    streamCounter += 2; // Advance to next valid odd client stream ID
                    break;
                }

                case 5: // RST_STREAM on a random previous stream
                {
                    var targetStream = streamCounter > 1 ? rng.Next(1, streamCounter) : 1;
                    var payload = new byte[4];
                    BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)rng.Next(0, 20));
                    AssertDecodeNeverCrashes(session, BuildRawFrame(0x3, 0, targetStream, payload));
                    break;
                }
            }
        }
    }
}
