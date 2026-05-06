using TurboHTTP.Protocol.Http3;

namespace TurboHTTP.Tests.Http3.Frames;

public sealed class ExtensionToleranceSpec
{
    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-9")]
    [InlineData(0x02)] // HTTP/2 PRIORITY — reserved, no HTTP/3 equivalent
    [InlineData(0x07)] // HTTP/2 GOAWAY — not a frame type in HTTP/3 (0x07 is settings ID only)
    [InlineData(0x08)] // HTTP/2 WINDOW_UPDATE — reserved, no HTTP/3 equivalent
    [InlineData(0x09)] // HTTP/2 CONTINUATION — reserved, no HTTP/3 equivalent
    [InlineData(0x0E)] // Unassigned
    [InlineData(0x10)] // Unassigned
    [InlineData(0xFF)] // Arbitrary unknown
    [InlineData(0x1234)] // Large unknown type (multi-byte varint)
    public void FrameDecoder_should_ignore_unknown_frame_types(long unknownType)
    {
        var payload = "\u07ad"u8.ToArray();
        var buf = new byte[32];
        var offset = 0;
        offset += QuicVarInt.Encode(unknownType, buf.AsSpan(offset));
        offset += QuicVarInt.Encode(payload.Length, buf.AsSpan(offset));
        payload.CopyTo(buf.AsSpan(offset));
        offset += payload.Length;

        var decoder = new FrameDecoder();
        var status = decoder.TryDecode(buf.AsSpan(0, offset), out var frame, out var consumed);

        Assert.Equal(DecodeStatus.Success, status);
        Assert.Null(frame); // Unknown type → skipped (null sentinel)
        Assert.Equal(offset, consumed);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-9")]
    [InlineData(0)] // 0x1f*0+0x21 = 0x21
    [InlineData(1)] // 0x1f*1+0x21 = 0x40
    [InlineData(2)] // 0x1f*2+0x21 = 0x5f
    [InlineData(10)] // 0x1f*10+0x21 = 0x155
    [InlineData(100)] // 0x1f*100+0x21 = 0xC55
    [InlineData(1000)] // 0x1f*1000+0x21 = 0x7A39
    public void FrameDecoder_should_ignore_grease_frame_types(int n)
    {
        var greaseType = 0x1fL * n + 0x21;
        var payload = new byte[] { 0x01, 0x02, 0x03 };
        var buf = new byte[32];
        var offset = 0;
        offset += QuicVarInt.Encode(greaseType, buf.AsSpan(offset));
        offset += QuicVarInt.Encode(payload.Length, buf.AsSpan(offset));
        payload.CopyTo(buf.AsSpan(offset));
        offset += payload.Length;

        var decoder = new FrameDecoder();
        var status = decoder.TryDecode(buf.AsSpan(0, offset), out var frame, out var consumed);

        Assert.Equal(DecodeStatus.Success, status);
        Assert.Null(frame);
        Assert.Equal(offset, consumed);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-9")]
    public void FrameDecoder_should_ignore_unknown_frame_type_with_zero_payload()
    {
        var buf = new byte[16];
        var offset = 0;
        offset += QuicVarInt.Encode(0xABCD, buf.AsSpan(offset)); // Unknown type
        offset += QuicVarInt.Encode(0, buf.AsSpan(offset)); // Zero-length payload

        var decoder = new FrameDecoder();
        var status = decoder.TryDecode(buf.AsSpan(0, offset), out var frame, out var consumed);

        Assert.Equal(DecodeStatus.Success, status);
        Assert.Null(frame);
        Assert.Equal(offset, consumed);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-9")]
    public void FrameDecoder_should_filter_unknown_frames_in_decodeall()
    {
        // Build: DATA + unknown(0xFF) + GOAWAY
        var data = new DataFrame(new byte[] { 0xCA, 0xFE });
        var goaway = new GoAwayFrame(42);

        var unknownBuf = new byte[16];
        var uOffset = 0;
        uOffset += QuicVarInt.Encode(0xFF, unknownBuf.AsSpan(uOffset));
        uOffset += QuicVarInt.Encode(2, unknownBuf.AsSpan(uOffset));
        unknownBuf[uOffset++] = 0xAA;
        unknownBuf[uOffset++] = 0xBB;

        var totalSize = data.SerializedSize + uOffset + goaway.SerializedSize;
        var wire = new byte[totalSize];
        var pos = 0;

        var span = wire.AsSpan(pos);
        pos += data.WriteTo(ref span);

        unknownBuf.AsSpan(0, uOffset).CopyTo(wire.AsSpan(pos));
        pos += uOffset;

        span = wire.AsSpan(pos);
        goaway.WriteTo(ref span);

        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(wire, out var consumed);

        Assert.Equal(2, frames.Count); // Unknown frame filtered out
        Assert.IsType<DataFrame>(frames[0]);
        Assert.IsType<GoAwayFrame>(frames[1]);
        Assert.Equal(totalSize, consumed);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-9")]
    public void FrameDecoder_should_ignore_multiple_consecutive_unknown_frames()
    {
        var buf = new byte[64];
        var offset = 0;

        // Three unknown frames in a row
        for (var i = 0; i < 3; i++)
        {
            offset += QuicVarInt.Encode(0x1fL * i + 0x21, buf.AsSpan(offset)); // GREASE type
            offset += QuicVarInt.Encode(1, buf.AsSpan(offset)); // 1-byte payload
            buf[offset++] = (byte)(0x10 + i);
        }

        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(buf.AsSpan(0, offset), out var consumed);

        Assert.Empty(frames); // All unknown → all filtered
        Assert.Equal(offset, consumed);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-9")]
    [InlineData(0x08)] // Unassigned
    [InlineData(0x10)] // Unassigned
    [InlineData(0x33)] // Arbitrary unknown
    [InlineData(0xFF)] // Arbitrary unknown
    [InlineData(0x1234)] // Large unknown ID
    public void Settings_should_ignore_unknown_setting_ids(long unknownId)
    {
        var settings = new Settings();
        settings.Set(unknownId, 42);

        Assert.Equal(42, settings[unknownId]);

        // Round-trip through serialize/deserialize
        var payload = settings.Serialize();
        var restored = Settings.Deserialize(payload);

        Assert.Equal(42, restored[unknownId]);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-9")]
    [InlineData(0)] // 0x21
    [InlineData(1)] // 0x40
    [InlineData(5)] // 0xBA
    [InlineData(100)] // 0xC55
    public void Settings_should_preserve_grease_setting_ids(int n)
    {
        var greaseId = 0x1fL * n + 0x21;

        var settings = new Settings();
        settings.Set(greaseId, 0); // GREASE settings typically have value 0

        var payload = settings.Serialize();
        var restored = Settings.Deserialize(payload);

        Assert.Equal(0, restored[greaseId]);
        Assert.Single(restored.AllParameters);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-9")]
    public void Settings_should_allow_mixed_known_and_unknown_settings()
    {
        var settings = new Settings();
        settings.Set(SettingsIdentifier.MaxFieldSectionSize, 8192);
        settings.Set(0x21, 0); // GREASE
        settings.Set(SettingsIdentifier.QpackMaxTableCapacity, 4096);
        settings.Set(0xBEEF, 999); // Unknown extension
        settings.Set(SettingsIdentifier.QpackBlockedStreams, 16);

        var payload = settings.Serialize();
        var restored = Settings.Deserialize(payload);

        Assert.Equal(8192, restored.MaxFieldSectionSize);
        Assert.Equal(4096, restored.QpackMaxTableCapacity);
        Assert.Equal(16, restored.QpackBlockedStreams);
        Assert.Equal(0, restored[0x21]);
        Assert.Equal(999, restored[0xBEEF]);
        Assert.Equal(5, restored.AllParameters.Count);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-9")]
    [InlineData(0x02)] // SETTINGS_ENABLE_PUSH
    [InlineData(0x03)] // SETTINGS_MAX_CONCURRENT_STREAMS
    [InlineData(0x04)] // SETTINGS_INITIAL_WINDOW_SIZE
    [InlineData(0x05)] // SETTINGS_MAX_FRAME_SIZE
    public void Settings_should_still_reject_reserved_http2_setting_ids(long reservedId)
    {
        // Extension tolerance does NOT apply to specifically reserved HTTP/2 identifiers
        var settings = new Settings();
        Assert.Throws<Http3Exception>(() => settings.Set(reservedId, 0));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-9")]
    public void FrameDecoder_should_reassemble_partial_unknown_frame()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var buf = new byte[32];
        var offset = 0;
        offset += QuicVarInt.Encode(0x21, buf.AsSpan(offset)); // GREASE type
        offset += QuicVarInt.Encode(payload.Length, buf.AsSpan(offset));
        payload.CopyTo(buf.AsSpan(offset));
        offset += payload.Length;

        // Split at midpoint
        var mid = offset / 2;
        var part1 = buf.AsSpan(0, mid);
        var part2 = buf.AsSpan(mid, offset - mid);

        var decoder = new FrameDecoder();

        var status = decoder.TryDecode(part1, out var frame, out _);
        Assert.Equal(DecodeStatus.NeedMoreData, status);
        Assert.True(decoder.HasRemainder);

        status = decoder.TryDecode(part2, out frame, out _);
        Assert.Equal(DecodeStatus.Success, status);
        Assert.Null(frame); // Still unknown → null
        Assert.False(decoder.HasRemainder);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-9")]
    public void FrameDecoder_should_decode_known_frame_after_unknown_frame()
    {
        // Unknown frame followed by DATA frame
        var buf = new byte[64];
        var offset = 0;

        // Unknown frame (GREASE 0x40 = 0x1f*1+0x21)
        offset += QuicVarInt.Encode(0x40, buf.AsSpan(offset));
        offset += QuicVarInt.Encode(2, buf.AsSpan(offset));
        buf[offset++] = 0xAA;
        buf[offset++] = 0xBB;

        // DATA frame
        var dataFrame = new DataFrame(new byte[] { 0xCA, 0xFE });
        var span = buf.AsSpan(offset);
        offset += dataFrame.WriteTo(ref span);

        var decoder = new FrameDecoder();

        // First decode: unknown frame → null
        var status = decoder.TryDecode(buf.AsSpan(0, offset), out var frame, out var consumed);
        Assert.Equal(DecodeStatus.Success, status);
        Assert.Null(frame);

        // Second decode: DATA frame → valid
        status = decoder.TryDecode(buf.AsSpan(consumed, offset - consumed), out frame, out _);
        Assert.Equal(DecodeStatus.Success, status);
        Assert.NotNull(frame);
        var data = Assert.IsType<DataFrame>(frame);
        Assert.Equal(new byte[] { 0xCA, 0xFE }, data.Data.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-9")]
    public void FrameDecoder_should_skip_unknown_frame_with_large_payload()
    {
        var largePayload = new byte[1024];
        new Random(42).NextBytes(largePayload);

        var buf = new byte[1040];
        var offset = 0;
        offset += QuicVarInt.Encode(0x5F, buf.AsSpan(offset)); // GREASE 0x1f*2+0x21
        offset += QuicVarInt.Encode(largePayload.Length, buf.AsSpan(offset));
        largePayload.CopyTo(buf.AsSpan(offset));
        offset += largePayload.Length;

        var decoder = new FrameDecoder();
        var status = decoder.TryDecode(buf.AsSpan(0, offset), out var frame, out var consumed);

        Assert.Equal(DecodeStatus.Success, status);
        Assert.Null(frame);
        Assert.Equal(offset, consumed);
    }
}