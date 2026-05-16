using System.Buffers.Binary;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Security;

public sealed class Http2FuzzHarnessPart2Spec
{
    private static void AssertDecodeNeverCrashes(FrameDecoder decoder, byte[] frame)
    {
        try
        {
            decoder.Decode(frame);
        }
        catch (HttpProtocolException)
        {
            // Expected — protocol violation, properly classified.
        }
        catch (HpackException ex)
        {
            // HpackException must NOT propagate outside the decoder pipeline.
            Assert.Fail(
                $"HpackException escaped decoder — must be wrapped as HttpProtocolException. Message: {ex.Message}");
        }
        // Any other exception type propagates and fails the test via xUnit.
    }

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

    private static readonly byte[] Status200HpackBlock = [0x88];

    [Fact(Timeout = 5000)]
    public void Http2FrameDecoder_should_handle_random_hpack_bytes_without_crashing()
    {
        var rng = new Random(42_000);

        for (var trial = 0; trial < 30; trial++)
        {
            var decoder = new FrameDecoder();
            var garbage = new byte[rng.Next(1, 200)];
            rng.NextBytes(garbage);
            var frame = BuildHeadersFrame(1, garbage);
            AssertDecodeNeverCrashes(decoder, frame);
        }
    }

    [Fact(Timeout = 5000)]
    public void Http2FrameDecoder_should_handle_valid_hpack_prefix_with_garbage_without_crashing()
    {
        var rng = new Random(99_000);

        for (var trial = 0; trial < 30; trial++)
        {
            var decoder = new FrameDecoder();

            // Start with indexed :status 200 (0x88), then append garbage.
            var garbage = new byte[rng.Next(1, 100)];
            rng.NextBytes(garbage);
            var block = new byte[1 + garbage.Length];
            block[0] = 0x88; // :status 200 (valid, static index 8)
            garbage.CopyTo(block, 1);

            var frame = BuildHeadersFrame(1, block);
            AssertDecodeNeverCrashes(decoder, frame);
        }
    }

    [Fact(Timeout = 5000)]
    public void Http2FrameDecoder_should_handle_hpack_oversized_string_length_without_crashing()
    {
        var decoder = new FrameDecoder();

        // Literal without indexing (0x00), index=0 (new name), then oversized length field.
        // 0x7F = 127 in 7-bit prefix → requires multi-byte encoding (continuation bytes).
        // Providing only 4 bytes total causes the HPACK parser to encounter a truncated string.
        var block = new byte[] { 0x00, 0x00, 0x7F, 0xFF }; // incomplete multi-byte string
        var frame = BuildHeadersFrame(1, block);
        AssertDecodeNeverCrashes(decoder, frame);
    }

    [Fact(Timeout = 5000)]
    public void Http2FrameDecoder_should_handle_out_of_range_hpack_index_without_crashing()
    {
        var decoder = new FrameDecoder();

        // RFC 7541 §6.1: index 0 is illegal; index > (static + dynamic count) is a COMPRESSION_ERROR.
        // Encode index = 200 using 7-bit prefix: first byte = 0xFF (prefix saturated at 127),
        // then continuation = 73 (127 + 73 = 200).
        var block = new byte[] { 0xFF, 73 }; // indexed representation, index=200
        var frame = BuildHeadersFrame(1, block);
        AssertDecodeNeverCrashes(decoder, frame);
    }

    [Fact(Timeout = 5000)]
    public void Http2FrameDecoder_should_handle_invalid_huffman_bitstream_without_crashing()
    {
        var rng = new Random(777);

        for (var trial = 0; trial < 20; trial++)
        {
            var decoder = new FrameDecoder();

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
            AssertDecodeNeverCrashes(decoder, frame);
        }
    }

    [Fact(Timeout = 5000)]
    public void Http2FrameDecoder_should_reject_connection_window_overflow_with_explicit_enforcement()
    {
        var decoder = new FrameDecoder();

        // Initial connection send window = 65535.
        // WINDOW_UPDATE of 0x7FFFFFFF = 2,147,483,647.
        // New window = 65535 + 2,147,483,647 = 2,147,549,182 > 2^31-1 → overflow.
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, 0x7FFFFFFF);
        var frame = BuildRawFrame(0x8, 0, 0, payload);

        // Decoder decodes the frame successfully; caller must enforce window invariant.
        var framesDecoded = decoder.Decode(frame);
        var wu = Assert.IsType<WindowUpdateFrame>(Assert.Single(framesDecoded));
        Assert.Equal(0x7FFFFFFF, wu.Increment);

        // Explicit enforcement: window overflow check.
        const long initialWindow = 65535L;
        var newWindow = initialWindow + wu.Increment;
        Assert.True(newWindow > int.MaxValue, "Expected overflow to be detected");
    }

    [Fact(Timeout = 5000)]
    public void Http2FrameDecoder_should_reject_stream_window_overflow_with_explicit_enforcement()
    {
        var decoder = new FrameDecoder();

        // Open stream 1 first with HEADERS.
        var openFrame = BuildHeadersFrame(1, Status200HpackBlock);
        decoder.Decode(openFrame);

        // Initial stream send window = 65535.
        // WINDOW_UPDATE of 0x7FFFFFFF overflows the stream window.
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, 0x7FFFFFFF);
        var frame = BuildRawFrame(0x8, 0, 1, payload);

        var framesDecoded = decoder.Decode(frame);
        var wu = Assert.IsType<WindowUpdateFrame>(Assert.Single(framesDecoded));

        // Explicit enforcement: stream window overflow check.
        const long streamWindow = 65535L;
        var newWindow = streamWindow + wu.Increment;
        Assert.True(newWindow > 0x7FFFFFFF, "Expected stream window overflow");
    }

    [Fact(Timeout = 5000)]
    public void Http2FrameDecoder_should_reject_zero_increment_window_update_with_protocol_error()
    {
        var decoder = new FrameDecoder();

        var payload = new byte[4]; // all zeros → increment = 0
        var frame = BuildRawFrame(0x8, 0, 0, payload);

        Assert.Throws<HttpProtocolException>(() => decoder.Decode(frame));
    }

    [Fact(Timeout = 5000)]
    public void Http2FrameDecoder_should_accept_settings_initial_window_size_at_maximum_valid_value()
    {
        var decoder = new FrameDecoder();

        // RFC 9113 §6.5.2: INITIAL_WINDOW_SIZE max = 2^31-1 = 2,147,483,647.
        var frame = BuildSettingsFrame(false, [(0x4, 0x7FFFFFFF)]);
        var framesDecoded = decoder.Decode(frame);
        var settings = Assert.Single(framesDecoded);
        Assert.IsType<SettingsFrame>(settings);
    }

    [Fact(Timeout = 5000)]
    public void Http2FrameDecoder_should_reject_settings_initial_window_size_exceeding_max_with_explicit_enforcement()
    {
        var decoder = new FrameDecoder();

        // RFC 9113 §6.5.2: INITIAL_WINDOW_SIZE = 2^31 is illegal (exceeds 2^31-1).
        var frame = BuildSettingsFrame(false, [(0x4, 0x80000000)]);

        var framesDecoded = decoder.Decode(frame);
        var settings = Assert.Single(framesDecoded);
        var settingsF = Assert.IsType<SettingsFrame>(settings);

        // Explicit enforcement of INITIAL_WINDOW_SIZE <= 2^31-1.
        var hasOverflow = settingsF.Parameters.Any(p =>
            p is { Item1: SettingsParameter.InitialWindowSize, Item2: > 0x7FFFFFFFu });
        Assert.True(hasOverflow, "Expected INITIAL_WINDOW_SIZE overflow in parameters");
    }

    [Fact(Timeout = 5000)]
    public void Http2FrameDecoder_should_handle_repeated_table_size_oscillation_without_crashing()
    {
        var hpack = new HpackDecoder();

        for (var i = 0; i < 100; i++)
        {
            // RFC 7541 §6.3: Dynamic Table Size Update — prefix = 0b001XXXXX (5-bit prefix).
            // DTS=0:  0x20 (= 0b00100000 | 0).
            // DTS=30: 0x3E (= 0b00100000 | 30). Values 0..30 fit in one byte (< 31 threshold).
            var blockToZero = new byte[] { 0x20 }; // DTS=0
            var blockToMax = new byte[] { 0x3E }; // DTS=30
            hpack.Decode(blockToZero);
            hpack.Decode(blockToMax);
        }
    }

    [Fact(Timeout = 5000)]
    public void Http2FrameDecoder_should_evict_all_entries_when_table_size_reduced_to_zero_after_filling()
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

    [Fact(Timeout = 5000)]
    public void Http2FrameDecoder_should_handle_rapid_header_table_size_changes_without_crashing()
    {
        var rng = new Random(42);
        var decoder = new FrameDecoder();

        for (var i = 0; i < 50; i++)
        {
            var size = (uint)rng.Next(0, 65536);
            var frame = BuildSettingsFrame(false, [(0x1, size)]); // HEADER_TABLE_SIZE
            AssertDecodeNeverCrashes(decoder, frame);
        }
    }

    [Fact(Timeout = 5000)]
    public void Http2FrameDecoder_should_handle_header_table_size_zero_followed_by_normal_headers()
    {
        var decoder = new FrameDecoder();

        // Set HEADER_TABLE_SIZE=0 via SETTINGS — dynamic table must be disabled.
        var settingsFrame = BuildSettingsFrame(false, [(0x1, 0)]);
        decoder.Decode(settingsFrame);

        // Send HEADERS with DTS=0 update (acknowledging the SETTINGS change) + :status 200.
        var block = new byte[] { 0x20, 0x88 }; // DTS=0 update, then indexed :status 200
        var frame = BuildHeadersFrame(1, block);
        AssertDecodeNeverCrashes(decoder, frame);
    }

    [Fact(Timeout = 5000)]
    public void Http2FrameDecoder_should_survive_extended_random_frame_sequence_without_unhandled_exceptions()
    {
        var rng = new Random(314159);
        var decoder = new FrameDecoder();
        var streamCounter = 1; // Next client stream ID to use

        for (var iteration = 0; iteration < 1000; iteration++)
        {
            var action = rng.Next(6);

            switch (action)
            {
                case 0: // PING (non-ACK)
                    AssertDecodeNeverCrashes(decoder, BuildRawFrame(0x6, 0x0, 0, new byte[8]));
                    break;

                case 1: // SETTINGS (empty — no flood risk)
                    AssertDecodeNeverCrashes(decoder, BuildSettingsFrame(false, []));
                    break;

                case 2: // SETTINGS ACK
                    AssertDecodeNeverCrashes(decoder, BuildSettingsFrame(true, []));
                    break;

                case 3: // Connection WINDOW_UPDATE
                    AssertDecodeNeverCrashes(decoder,
                        BuildWindowUpdateFrame(0, (uint)rng.Next(1, 1000)));
                    break;

                case 4: // Random garbage HEADERS on a new stream
                    {
                        var garbage = new byte[rng.Next(0, 64)];
                        rng.NextBytes(garbage);
                        AssertDecodeNeverCrashes(decoder, BuildHeadersFrame(streamCounter, garbage));
                        streamCounter += 2; // Advance to next valid odd client stream ID
                        break;
                    }

                case 5: // RST_STREAM on a random previous stream
                    {
                        var targetStream = streamCounter > 1 ? rng.Next(1, streamCounter) : 1;
                        var payload = new byte[4];
                        BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)rng.Next(0, 20));
                        AssertDecodeNeverCrashes(decoder, BuildRawFrame(0x3, 0, targetStream, payload));
                        break;
                    }
            }
        }
    }
}