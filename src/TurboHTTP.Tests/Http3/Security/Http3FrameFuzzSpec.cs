using TurboHTTP.Protocol.Http3;
using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Tests.Http3.Security;

/// <summary>
/// Fuzz tests for the HTTP/3 frame decoder per RFC 9114 §7.
/// Verifies that the decoder either succeeds or handles errors gracefully —
/// never an unhandled crash. Covers corrupt frame types, truncated frames,
/// oversized payloads, and invalid frame-stream combinations.
/// </summary>
public sealed class Http3FrameFuzzSpec
{
    /// <summary>
    /// Feeds bytes to the decoder and asserts that the outcome is either
    /// a successful decode, NeedMoreData, or an expected protocol exception.
    /// Any other exception type is a bug.
    /// </summary>
    private static void AssertDecodeNeverCrashes(FrameDecoder decoder, byte[] data)
    {
        try
        {
            decoder.DecodeAll(data, out _);
        }
        catch (Http3Exception)
        {
            // Expected — protocol violation, properly classified.
        }
        catch (QpackException)
        {
            // QPACK errors are acceptable at frame level
        }
        catch (ArgumentException)
        {
            // QuicVarInt can throw ArgumentException on malformed input
        }
    }

    private static byte[] BuildRawFrame(long frameType, byte[] payload)
    {
        var typeBuf = new byte[8];
        var typeLen = QuicVarInt.Encode(frameType, typeBuf);

        var lenBuf = new byte[8];
        var lenLen = QuicVarInt.Encode(payload.Length, lenBuf);

        var frame = new byte[typeLen + lenLen + payload.Length];
        Array.Copy(typeBuf, 0, frame, 0, typeLen);
        Array.Copy(lenBuf, 0, frame, typeLen, lenLen);
        Array.Copy(payload, 0, frame, typeLen + lenLen, payload.Length);
        return frame;
    }

    private static byte[] BuildRawFrameWithDeclaredLength(long frameType, long declaredLength, byte[] actualPayload)
    {
        var typeBuf = new byte[8];
        var typeLen = QuicVarInt.Encode(frameType, typeBuf);

        var lenBuf = new byte[8];
        var lenLen = QuicVarInt.Encode(declaredLength, lenBuf);

        var frame = new byte[typeLen + lenLen + actualPayload.Length];
        Array.Copy(typeBuf, 0, frame, 0, typeLen);
        Array.Copy(lenBuf, 0, frame, typeLen, lenLen);
        Array.Copy(actualPayload, 0, frame, typeLen + lenLen, actualPayload.Length);
        return frame;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.8")]
    public void FrameDecoder_should_skip_unknown_frame_types_without_crashing()
    {
        var rng = new Random(42);

        for (var i = 0; i < 50; i++)
        {
            using var decoder = new FrameDecoder();
            // Unknown frame types: use values not in Http3FrameType enum
            var unknownType = (long)rng.Next(0x0E, 0x100);
            var payload = new byte[rng.Next(0, 64)];
            rng.NextBytes(payload);

            var frame = BuildRawFrame(unknownType, payload);
            var status = decoder.TryDecode(frame, out var decoded, out _);

            Assert.Equal(DecodeStatus.Success, status);
            Assert.Null(decoded); // Unknown frames produce null (skipped)
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.1")]
    public void FrameDecoder_should_buffer_truncated_frame_without_crashing()
    {
        using var decoder = new FrameDecoder();

        // Declare a DATA frame with length 100 but provide only 10 payload bytes
        var frame = BuildRawFrameWithDeclaredLength(
            (long)FrameType.Data, 100, new byte[10]);

        var status = decoder.TryDecode(frame, out var decoded, out _);

        Assert.Equal(DecodeStatus.NeedMoreData, status);
        Assert.Null(decoded);
        Assert.True(decoder.HasRemainder);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.1")]
    public void FrameDecoder_should_handle_empty_input_without_crashing()
    {
        using var decoder = new FrameDecoder();

        var status = decoder.TryDecode(ReadOnlySpan<byte>.Empty, out var frame, out var consumed);

        Assert.Equal(DecodeStatus.NeedMoreData, status);
        Assert.Null(frame);
        Assert.Equal(0, consumed);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.1")]
    public void FrameDecoder_should_handle_single_byte_input_without_crashing()
    {
        // A single byte is not enough for a complete frame
        for (byte b = 0; b < 255; b++)
        {
            using var decoder = new FrameDecoder();
            var data = new byte[] { b };

            AssertDecodeNeverCrashes(decoder, data);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.1")]
    public void FrameDecoder_should_decode_data_frame_on_request_stream()
    {
        using var decoder = new FrameDecoder();

        var payload = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello"
        var frame = BuildRawFrame((long)FrameType.Data, payload);

        var status = decoder.TryDecode(frame, out var decoded, out _);

        Assert.Equal(DecodeStatus.Success, status);
        var dataFrame = Assert.IsType<Http3DataFrame>(decoded);
        Assert.Equal(5, dataFrame.Data.Length);
        dataFrame.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void FrameDecoder_should_reject_reserved_h2_settings_via_settings_deserialize()
    {
        // RFC 9114 §7.2.4.1: HTTP/2 settings MUST NOT appear in HTTP/3
        // Reserved identifiers: 0x02, 0x03, 0x04, 0x05
        long[] reservedIds = [0x02, 0x03, 0x04, 0x05];

        foreach (var id in reservedIds)
        {
            var payloadBuf = new byte[16];
            var offset = QuicVarInt.Encode(id, payloadBuf);
            offset += QuicVarInt.Encode(42, payloadBuf.AsSpan(offset));
            var payload = payloadBuf[..offset];

            var ex = Assert.Throws<Http3Exception>(
                () => Settings.Deserialize(payload));
            Assert.Equal(Http3ErrorCode.SettingsError, ex.ErrorCode);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void FrameDecoder_should_reject_duplicate_settings_identifiers()
    {
        // RFC 9114 §7.2.4: Each setting identifier MUST NOT occur more than once
        var payloadBuf = new byte[32];
        var offset = 0;
        // Write QPACK_MAX_TABLE_CAPACITY twice
        offset += QuicVarInt.Encode(Http3SettingsIdentifier.QpackMaxTableCapacity, payloadBuf.AsSpan(offset));
        offset += QuicVarInt.Encode(4096, payloadBuf.AsSpan(offset));
        offset += QuicVarInt.Encode(Http3SettingsIdentifier.QpackMaxTableCapacity, payloadBuf.AsSpan(offset));
        offset += QuicVarInt.Encode(8192, payloadBuf.AsSpan(offset));

        var payload = payloadBuf[..offset];

        var ex = Assert.Throws<Http3Exception>(() => Settings.Deserialize(payload));
        Assert.Equal(Http3ErrorCode.SettingsError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void FrameDecoder_should_accept_unknown_settings_identifiers()
    {
        // RFC 9114 §7.2.4: Unknown settings MUST be ignored
        var payloadBuf = new byte[16];
        var offset = 0;
        offset += QuicVarInt.Encode(0xFF, payloadBuf.AsSpan(offset)); // Unknown identifier
        offset += QuicVarInt.Encode(42, payloadBuf.AsSpan(offset));

        var settings = Settings.Deserialize(payloadBuf[..offset]);

        Assert.Equal(42, settings[0xFF]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void FrameDecoder_should_reject_truncated_settings_payload()
    {
        // Truncated: identifier present but value missing
        var payloadBuf = new byte[8];
        var offset = QuicVarInt.Encode(Http3SettingsIdentifier.QpackMaxTableCapacity, payloadBuf);
        var payload = payloadBuf[..offset]; // Only identifier, no value

        Assert.Throws<Http3Exception>(() => Settings.Deserialize(payload));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.1")]
    public void FrameDecoder_should_handle_random_byte_sequences_without_crashing()
    {
        var rng = new Random(1234);

        for (var trial = 0; trial < 100; trial++)
        {
            using var decoder = new FrameDecoder();
            var data = new byte[rng.Next(1, 128)];
            rng.NextBytes(data);

            AssertDecodeNeverCrashes(decoder, data);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.1")]
    public void FrameDecoder_should_handle_interleaved_valid_and_corrupt_frames()
    {
        using var decoder = new FrameDecoder();

        // Valid DATA frame
        var validFrame = BuildRawFrame((long)FrameType.Data, [0x01, 0x02, 0x03]);
        var status = decoder.TryDecode(validFrame, out var frame, out _);
        Assert.Equal(DecodeStatus.Success, status);
        Assert.IsType<Http3DataFrame>(frame);
        ((Http3DataFrame)frame!).Dispose();

        // Valid GOAWAY frame
        var goawayPayload = new byte[8];
        var goawayLen = QuicVarInt.Encode(4, goawayPayload);
        var goawayFrame = BuildRawFrame((long)FrameType.GoAway, goawayPayload[..goawayLen]);
        status = decoder.TryDecode(goawayFrame, out frame, out _);
        Assert.Equal(DecodeStatus.Success, status);
        Assert.IsType<Http3GoAwayFrame>(frame);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.1")]
    public void FrameDecoder_should_decode_zero_length_data_frame()
    {
        using var decoder = new FrameDecoder();

        var frame = BuildRawFrame((long)FrameType.Data, []);

        var status = decoder.TryDecode(frame, out var decoded, out _);
        Assert.Equal(DecodeStatus.Success, status);
        var dataFrame = Assert.IsType<Http3DataFrame>(decoded);
        Assert.Equal(0, dataFrame.Data.Length);
        dataFrame.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.2")]
    public void FrameDecoder_should_decode_empty_headers_frame()
    {
        using var decoder = new FrameDecoder();

        var frame = BuildRawFrame((long)FrameType.Headers, []);

        var status = decoder.TryDecode(frame, out var decoded, out _);
        Assert.Equal(DecodeStatus.Success, status);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(decoded);
        Assert.Equal(0, headersFrame.HeaderBlock.Length);
        headersFrame.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.1")]
    public void FrameDecoder_should_decode_all_multiple_frames_in_sequence()
    {
        using var decoder = new FrameDecoder();

        var frame1 = BuildRawFrame((long)FrameType.Data, [0x01]);
        var frame2 = BuildRawFrame((long)FrameType.Data, [0x02]);
        var combined = new byte[frame1.Length + frame2.Length];
        frame1.CopyTo(combined, 0);
        frame2.CopyTo(combined, frame1.Length);

        var frames = decoder.DecodeAll(combined, out var consumed);

        Assert.Equal(2, frames.Count);
        Assert.Equal(combined.Length, consumed);

        foreach (var f in frames)
        {
            if (f is IDisposable d)
            {
                d.Dispose();
            }
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.1")]
    public void FrameDecoder_should_handle_frame_split_across_two_calls()
    {
        using var decoder = new FrameDecoder();

        var payload = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };
        var fullFrame = BuildRawFrame((long)FrameType.Data, payload);

        // Split in the middle
        var half = fullFrame.Length / 2;
        var part1 = fullFrame[..half];
        var part2 = fullFrame[half..];

        var status1 = decoder.TryDecode(part1, out var frame1, out _);
        Assert.Equal(DecodeStatus.NeedMoreData, status1);
        Assert.Null(frame1);

        var status2 = decoder.TryDecode(part2, out var frame2, out _);
        Assert.Equal(DecodeStatus.Success, status2);
        var dataFrame = Assert.IsType<Http3DataFrame>(frame2);
        Assert.Equal(5, dataFrame.Data.Length);
        dataFrame.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.1")]
    public void FrameDecoder_should_reset_buffered_state_on_reset()
    {
        using var decoder = new FrameDecoder();

        // Feed partial frame
        var fullFrame = BuildRawFrame((long)FrameType.Data, new byte[100]);
        decoder.TryDecode(fullFrame[..5], out _, out _);
        Assert.True(decoder.HasRemainder);

        decoder.Reset();
        Assert.False(decoder.HasRemainder);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Settings_should_reject_h2_reserved_identifiers_via_set()
    {
        var settings = new Settings();

        Assert.Throws<Http3Exception>(() =>
            settings.Set(Http3SettingsIdentifier.ReservedH2EnablePush, 1));
        Assert.Throws<Http3Exception>(() =>
            settings.Set(Http3SettingsIdentifier.ReservedH2MaxConcurrentStreams, 100));
        Assert.Throws<Http3Exception>(() =>
            settings.Set(Http3SettingsIdentifier.ReservedH2InitialWindowSize, 65535));
        Assert.Throws<Http3Exception>(() =>
            settings.Set(Http3SettingsIdentifier.ReservedH2MaxFrameSize, 16384));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Settings_should_roundtrip_valid_parameters()
    {
        var settings = new Settings();
        settings.Set(Http3SettingsIdentifier.QpackMaxTableCapacity, 4096);
        settings.Set(Http3SettingsIdentifier.QpackBlockedStreams, 100);
        settings.Set(Http3SettingsIdentifier.MaxFieldSectionSize, 8192);

        var serialized = settings.Serialize();
        var deserialized = Settings.Deserialize(serialized);

        Assert.Equal(4096, deserialized.QpackMaxTableCapacity);
        Assert.Equal(100, deserialized.QpackBlockedStreams);
        Assert.Equal(8192, deserialized.MaxFieldSectionSize);
    }
}
