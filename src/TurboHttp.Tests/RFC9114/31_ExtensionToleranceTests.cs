using TurboHttp.Protocol.RFC9000;
using TurboHttp.Protocol.RFC9114;

namespace TurboHttp.Tests.RFC9114;

/// <summary>
/// Extension tolerance tests per RFC 9114 §9.
/// Implementations MUST ignore unknown frame types and unknown settings
/// to allow future extensions without breaking existing deployments.
/// Reserved GREASE identifiers (0x1f * N + 0x21) exercise this requirement.
/// </summary>
public sealed class ExtensionToleranceTests
{
    // ───────────────────── Unknown Frame Types ─────────────────────

    [Theory(DisplayName = "RFC-9114-9-ext-001: Unknown frame types are ignored, not connection error")]
    [InlineData(0x02)]  // HTTP/2 PRIORITY — reserved, no HTTP/3 equivalent
    [InlineData(0x07)]  // HTTP/2 GOAWAY — not a frame type in HTTP/3 (0x07 is settings ID only)
    [InlineData(0x08)]  // HTTP/2 WINDOW_UPDATE — reserved, no HTTP/3 equivalent
    [InlineData(0x09)]  // HTTP/2 CONTINUATION — reserved, no HTTP/3 equivalent
    [InlineData(0x0E)]  // Unassigned
    [InlineData(0x10)]  // Unassigned
    [InlineData(0xFF)]  // Arbitrary unknown
    [InlineData(0x1234)] // Large unknown type (multi-byte varint)
    public void UnknownFrameType_IsIgnored_NotConnectionError(long unknownType)
    {
        var payload = new byte[] { 0xDE, 0xAD };
        var buf = new byte[32];
        var offset = 0;
        offset += QuicVarInt.Encode(unknownType, buf.AsSpan(offset));
        offset += QuicVarInt.Encode(payload.Length, buf.AsSpan(offset));
        payload.CopyTo(buf.AsSpan(offset));
        offset += payload.Length;

        var decoder = new Http3FrameDecoder();
        var status = decoder.TryDecode(buf.AsSpan(0, offset), out var frame, out var consumed);

        Assert.Equal(Http3DecodeStatus.Success, status);
        Assert.Null(frame); // Unknown type → skipped (null sentinel)
        Assert.Equal(offset, consumed);
    }

    [Theory(DisplayName = "RFC-9114-9-ext-002: GREASE frame types (0x1f*N+0x21) are ignored")]
    [InlineData(0)]     // 0x1f*0+0x21 = 0x21
    [InlineData(1)]     // 0x1f*1+0x21 = 0x40
    [InlineData(2)]     // 0x1f*2+0x21 = 0x5f
    [InlineData(10)]    // 0x1f*10+0x21 = 0x155
    [InlineData(100)]   // 0x1f*100+0x21 = 0xC55
    [InlineData(1000)]  // 0x1f*1000+0x21 = 0x7A39
    public void GreaseFrameType_IsIgnored(int n)
    {
        var greaseType = 0x1fL * n + 0x21;
        var payload = new byte[] { 0x01, 0x02, 0x03 };
        var buf = new byte[32];
        var offset = 0;
        offset += QuicVarInt.Encode(greaseType, buf.AsSpan(offset));
        offset += QuicVarInt.Encode(payload.Length, buf.AsSpan(offset));
        payload.CopyTo(buf.AsSpan(offset));
        offset += payload.Length;

        var decoder = new Http3FrameDecoder();
        var status = decoder.TryDecode(buf.AsSpan(0, offset), out var frame, out var consumed);

        Assert.Equal(Http3DecodeStatus.Success, status);
        Assert.Null(frame);
        Assert.Equal(offset, consumed);
    }

    [Fact(DisplayName = "RFC-9114-9-ext-003: Unknown frame with zero-length payload is ignored")]
    public void UnknownFrameType_ZeroPayload_IsIgnored()
    {
        var buf = new byte[16];
        var offset = 0;
        offset += QuicVarInt.Encode(0xABCD, buf.AsSpan(offset)); // Unknown type
        offset += QuicVarInt.Encode(0, buf.AsSpan(offset));       // Zero-length payload

        var decoder = new Http3FrameDecoder();
        var status = decoder.TryDecode(buf.AsSpan(0, offset), out var frame, out var consumed);

        Assert.Equal(Http3DecodeStatus.Success, status);
        Assert.Null(frame);
        Assert.Equal(offset, consumed);
    }

    [Fact(DisplayName = "RFC-9114-9-ext-004: DecodeAll filters unknown frames from result list")]
    public void DecodeAll_FiltersUnknownFrames()
    {
        // Build: DATA + unknown(0xFF) + GOAWAY
        var data = new Http3DataFrame(new byte[] { 0xCA, 0xFE });
        var goaway = new Http3GoAwayFrame(42);

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
        pos += goaway.WriteTo(ref span);

        var decoder = new Http3FrameDecoder();
        var frames = decoder.DecodeAll(wire, out var consumed);

        Assert.Equal(2, frames.Count); // Unknown frame filtered out
        Assert.IsType<Http3DataFrame>(frames[0]);
        Assert.IsType<Http3GoAwayFrame>(frames[1]);
        Assert.Equal(totalSize, consumed);
    }

    [Fact(DisplayName = "RFC-9114-9-ext-005: Multiple consecutive unknown frames are all ignored")]
    public void MultipleConsecutiveUnknownFrames_AllIgnored()
    {
        var buf = new byte[64];
        var offset = 0;

        // Three unknown frames in a row
        for (var i = 0; i < 3; i++)
        {
            offset += QuicVarInt.Encode(0x1fL * i + 0x21, buf.AsSpan(offset)); // GREASE type
            offset += QuicVarInt.Encode(1, buf.AsSpan(offset));                  // 1-byte payload
            buf[offset++] = (byte)(0x10 + i);
        }

        var decoder = new Http3FrameDecoder();
        var frames = decoder.DecodeAll(buf.AsSpan(0, offset), out var consumed);

        Assert.Empty(frames); // All unknown → all filtered
        Assert.Equal(offset, consumed);
    }

    // ───────────────────── Unknown Settings ─────────────────────

    [Theory(DisplayName = "RFC-9114-9-ext-006: Unknown settings are ignored, not connection error")]
    [InlineData(0x08)]    // Unassigned
    [InlineData(0x10)]    // Unassigned
    [InlineData(0x33)]    // Arbitrary unknown
    [InlineData(0xFF)]    // Arbitrary unknown
    [InlineData(0x1234)]  // Large unknown ID
    public void UnknownSetting_IsIgnored_NotConnectionError(long unknownId)
    {
        var settings = new Http3Settings();
        settings.Set(unknownId, 42);

        Assert.Equal(42, settings[unknownId]);

        // Round-trip through serialize/deserialize
        var payload = settings.Serialize();
        var restored = Http3Settings.Deserialize(payload);

        Assert.Equal(42, restored[unknownId]);
    }

    [Theory(DisplayName = "RFC-9114-9-ext-007: GREASE setting identifiers (0x1f*N+0x21) are preserved")]
    [InlineData(0)]     // 0x21
    [InlineData(1)]     // 0x40
    [InlineData(5)]     // 0xBA
    [InlineData(100)]   // 0xC55
    public void GreaseSettingId_IsPreserved(int n)
    {
        var greaseId = 0x1fL * n + 0x21;

        var settings = new Http3Settings();
        settings.Set(greaseId, 0); // GREASE settings typically have value 0

        var payload = settings.Serialize();
        var restored = Http3Settings.Deserialize(payload);

        Assert.Equal(0, restored[greaseId]);
        Assert.Single(restored.AllParameters);
    }

    [Fact(DisplayName = "RFC-9114-9-ext-008: Mixed known and unknown settings coexist")]
    public void MixedKnownAndUnknownSettings_Coexist()
    {
        var settings = new Http3Settings();
        settings.Set(Http3SettingId.MaxFieldSectionSize, 8192);
        settings.Set(0x21, 0);       // GREASE
        settings.Set(Http3SettingId.QpackMaxTableCapacity, 4096);
        settings.Set(0xBEEF, 999);   // Unknown extension
        settings.Set(Http3SettingId.QpackBlockedStreams, 16);

        var payload = settings.Serialize();
        var restored = Http3Settings.Deserialize(payload);

        Assert.Equal(8192, restored.MaxFieldSectionSize);
        Assert.Equal(4096, restored.QpackMaxTableCapacity);
        Assert.Equal(16, restored.QpackBlockedStreams);
        Assert.Equal(0, restored[0x21]);
        Assert.Equal(999, restored[0xBEEF]);
        Assert.Equal(5, restored.AllParameters.Count);
    }

    // ───────────────────── Reserved HTTP/2 Settings (MUST reject) ─────────────────────

    [Theory(DisplayName = "RFC-9114-9-ext-009: Reserved HTTP/2 settings still rejected per §7.2.4.1")]
    [InlineData(0x02)] // SETTINGS_ENABLE_PUSH
    [InlineData(0x03)] // SETTINGS_MAX_CONCURRENT_STREAMS
    [InlineData(0x04)] // SETTINGS_INITIAL_WINDOW_SIZE
    [InlineData(0x05)] // SETTINGS_MAX_FRAME_SIZE
    public void ReservedH2Settings_StillRejected(long reservedId)
    {
        // Extension tolerance does NOT apply to specifically reserved HTTP/2 identifiers
        var settings = new Http3Settings();
        Assert.Throws<Http3Exception>(() => settings.Set(reservedId, 0));
    }

    // ───────────────────── Partial Unknown Frames ─────────────────────

    [Fact(DisplayName = "RFC-9114-9-ext-010: Partial unknown frame reassembles across calls")]
    public void PartialUnknownFrame_ReassemblesAcrossCalls()
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

        var decoder = new Http3FrameDecoder();

        var status = decoder.TryDecode(part1, out var frame, out _);
        Assert.Equal(Http3DecodeStatus.NeedMoreData, status);
        Assert.True(decoder.HasRemainder);

        status = decoder.TryDecode(part2, out frame, out _);
        Assert.Equal(Http3DecodeStatus.Success, status);
        Assert.Null(frame); // Still unknown → null
        Assert.False(decoder.HasRemainder);
    }

    [Fact(DisplayName = "RFC-9114-9-ext-011: Known frame after unknown frame is decoded correctly")]
    public void KnownFrameAfterUnknown_DecodedCorrectly()
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
        var dataFrame = new Http3DataFrame(new byte[] { 0xCA, 0xFE });
        var span = buf.AsSpan(offset);
        offset += dataFrame.WriteTo(ref span);

        var decoder = new Http3FrameDecoder();

        // First decode: unknown frame → null
        var status = decoder.TryDecode(buf.AsSpan(0, offset), out var frame, out var consumed);
        Assert.Equal(Http3DecodeStatus.Success, status);
        Assert.Null(frame);

        // Second decode: DATA frame → valid
        status = decoder.TryDecode(buf.AsSpan(consumed, offset - consumed), out frame, out _);
        Assert.Equal(Http3DecodeStatus.Success, status);
        Assert.NotNull(frame);
        var data = Assert.IsType<Http3DataFrame>(frame);
        Assert.Equal(new byte[] { 0xCA, 0xFE }, data.Data.ToArray());
    }

    [Fact(DisplayName = "RFC-9114-9-ext-012: Unknown frame with large payload is skipped correctly")]
    public void UnknownFrame_LargePayload_SkippedCorrectly()
    {
        var largePayload = new byte[1024];
        new Random(42).NextBytes(largePayload);

        var buf = new byte[1040];
        var offset = 0;
        offset += QuicVarInt.Encode(0x5F, buf.AsSpan(offset)); // GREASE 0x1f*2+0x21
        offset += QuicVarInt.Encode(largePayload.Length, buf.AsSpan(offset));
        largePayload.CopyTo(buf.AsSpan(offset));
        offset += largePayload.Length;

        var decoder = new Http3FrameDecoder();
        var status = decoder.TryDecode(buf.AsSpan(0, offset), out var frame, out var consumed);

        Assert.Equal(Http3DecodeStatus.Success, status);
        Assert.Null(frame);
        Assert.Equal(offset, consumed);
    }
}
