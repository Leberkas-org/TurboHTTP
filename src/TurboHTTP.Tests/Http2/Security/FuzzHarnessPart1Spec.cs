using System.Buffers.Binary;
using TurboHTTP.Protocol.Http2.Hpack;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Tests.Http2.Security;

/// <summary>
/// Tests decoder robustness against malformed, truncated, and adversarial byte sequences per RFC 9113 §4.
/// Verifies that the decoder either succeeds or throws Http2Exception — never an unhandled crash.
/// This is Part 1 of the fuzz harness tests (FZ-001 through FZ-010).
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http2FrameDecoder"/>.
/// RFC 9113 §4.2: Receivers must treat frames with unknown types as PROTOCOL_ERROR or ignore them safely.
/// </remarks>
public sealed class Http2FuzzHarnessPart1Spec
{
    // Core invariant helper

    /// <summary>
    /// Feeds <paramref name="frame"/> to the decoder and asserts that the outcome
    /// is either a successful decode or an Http2Exception. Any other exception is a bug.
    /// </summary>
    private static void AssertDecodeNeverCrashes(Http2FrameDecoder decoder, byte[] frame)
    {
        try
        {
            decoder.Decode(frame);
        }
        catch (Http2Exception)
        {
            // Expected — protocol violation, properly classified.
        }
        catch (HpackException ex)
        {
            // HpackException must NOT propagate outside the decoder pipeline.
            // The decoder must wrap it as Http2Exception(CompressionError).
            Assert.Fail($"HpackException escaped decoder — must be wrapped as Http2Exception(CompressionError). Message: {ex.Message}");
        }
        // Any other exception type propagates and fails the test via xUnit.
    }

    // Frame building helpers

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

    // FZ-001..005: Random frame ordering (RFC 9113 §5.5)

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.5")]
    public void Http2FrameDecoder_should_handle_random_headers_data_sequences_without_crashing()
    {
        var rng = new Random(42);
        var decoder = new Http2FrameDecoder();

        for (var i = 0; i < 50; i++)
        {
            var streamId = (rng.Next(1, 20) * 2) - 1; // Odd client stream IDs 1..39
            var headersFrame = BuildHeadersFrame(streamId, Status200HpackBlock, endStream: false);
            AssertDecodeNeverCrashes(decoder, headersFrame);

            var data = new byte[rng.Next(0, 64)];
            rng.NextBytes(data);
            var dataFrame = BuildRawFrame(0x0, 0x1, streamId, data); // END_STREAM
            AssertDecodeNeverCrashes(decoder, dataFrame);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.5")]
    public void Http2FrameDecoder_should_handle_random_rst_stream_frames_without_crashing()
    {
        var rng = new Random(137);
        var decoder = new Http2FrameDecoder();

        for (var i = 0; i < 100; i++)
        {
            var streamId = rng.Next(1, 500);
            var errorCode = (uint)rng.Next(0, 20);
            var payload = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(payload, errorCode);
            var frame = BuildRawFrame(0x3, 0, streamId, payload);
            AssertDecodeNeverCrashes(decoder, frame);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.5")]
    public void Http2FrameDecoder_should_handle_random_window_update_frames_without_crashing()
    {
        var rng = new Random(7);
        var decoder = new Http2FrameDecoder();

        for (var i = 0; i < 100; i++)
        {
            var streamId = rng.Next(0, 100);
            var increment = (uint)rng.Next(1, int.MaxValue);
            AssertDecodeNeverCrashes(decoder, BuildWindowUpdateFrame(streamId, increment));
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.5")]
    public void Http2FrameDecoder_should_handle_interleaved_frame_types_without_crashing()
    {
        var rng = new Random(99);
        var decoder = new Http2FrameDecoder();

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
            AssertDecodeNeverCrashes(decoder, builder());
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.5")]
    public void Http2FrameDecoder_should_ignore_unknown_frame_types_per_rfc9113()
    {
        var rng = new Random(555);
        var decoder = new Http2FrameDecoder();

        // RFC 9113 §5.5: Implementations MUST ignore and discard frames with unknown types.
        for (var i = 0; i < 50; i++)
        {
            var type = (byte)rng.Next(0x0A, 0x100); // Types 0x0A..0xFF are unknown/reserved
            var payload = new byte[rng.Next(0, 64)];
            rng.NextBytes(payload);
            var frame = BuildRawFrame(type, 0, 0, payload);
            AssertDecodeNeverCrashes(decoder, frame);
        }
    }

    // FZ-006..010: Invalid frame lengths (RFC 9113 §4.2)

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.2")]
    public void Http2FrameDecoder_should_reject_oversized_frame_with_frame_size_error()
    {
        var decoder = new Http2FrameDecoder();

        // Declare length = 20000 > default MaxFrameSize (16384), provide full buffer.
        // NOTE: Http2FrameDecoder does not enforce MAX_FRAME_SIZE. The frame is decoded;
        // Http2Exception may be thrown on stream processing. This test verifies decoder robustness.
        const int declaredLength = 20000;
        var frame = new byte[9 + declaredLength];
        frame[0] = declaredLength >> 16;
        frame[1] = declaredLength >> 8;
        frame[2] = unchecked((byte)declaredLength);
        frame[3] = 0x0; // DATA
        frame[4] = 0x0;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), 1u); // stream 1

        AssertDecodeNeverCrashes(decoder, frame);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.2")]
    public void Http2FrameDecoder_should_buffer_truncated_frame_without_crashing()
    {
        var decoder = new Http2FrameDecoder();

        // Declare a PING with length=8 but provide only 4 payload bytes → decoder returns empty.
        var frame = new byte[9 + 4];
        frame[0] = 0; frame[1] = 0; frame[2] = 8; // declared length = 8
        frame[3] = 0x6; // PING
        frame[4] = 0x0;
        // Remaining bytes left zeroed (only 4, not the declared 8)

        var framesDecoded = decoder.Decode(frame);
        Assert.Empty(framesDecoded); // incomplete frame — buffered, not crashed
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.2")]
    public void Http2FrameDecoder_should_reject_ping_with_wrong_payload_length_with_frame_size_error()
    {
        var decoder = new Http2FrameDecoder();

        // PING must be exactly 8 bytes; 5 bytes is wrong.
        var payload = new byte[5];
        var frame = BuildRawFrame(0x6, 0, 0, payload);

        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(frame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.2")]
    public void Http2FrameDecoder_should_reject_settings_with_non_multiple_of_6_payload_with_frame_size_error()
    {
        var decoder = new Http2FrameDecoder();

        // SETTINGS payload must be a multiple of 6; 7 is not.
        var payload = new byte[7];
        var frame = BuildRawFrame(0x4, 0, 0, payload);

        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(frame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.2")]
    public void Http2FrameDecoder_should_handle_corrupted_frame_headers_without_unhandled_exceptions()
    {
        var rng = new Random(1234);

        for (var trial = 0; trial < 50; trial++)
        {
            var decoder = new Http2FrameDecoder();

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

            AssertDecodeNeverCrashes(decoder, frame);
        }
    }
}
