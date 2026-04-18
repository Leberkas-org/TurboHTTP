using TurboHTTP.Protocol.Http3;
using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Tests.Http3.Security;

/// <summary>
/// Security-focused tests for HTTP/3 per RFC 9114 §10 and RFC 9204 §7.
/// Covers header compression ratio attacks, SETTINGS bombs, control stream
/// starvation, and resource exhaustion scenarios.
/// </summary>
public sealed class Http3SecuritySpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-7.1.3")]
    public void QpackEncoder_should_limit_compression_ratio_when_bomb_via_repeated_references()
    {
        // Attack: Encoder inserts one large entry, then encodes many references.
        // The compression ratio amplification must be bounded by table capacity.
        var encoder = new QpackEncoder(maxTableCapacity: 1024);

        // First encode: large headers to populate the table
        var initial = new List<(string, string)>
        {
            ("x-large-header", new string('A', 200)),
        };
        var initialEncoded = encoder.Encode(initial);
        Assert.True(initialEncoded.Length > 0);

        // Second encode: same header — should use table reference (compressed)
        var secondEncoded = encoder.Encode(initial);
        Assert.True(secondEncoded.Length > 0);

        // Verify table is bounded
        Assert.True(encoder.DynamicTable.CurrentSize <= 1024);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Settings_should_reject_all_reserved_h2_identifiers_in_deserialization()
    {
        // RFC 9114 §7.2.4.1: Identifiers 0x02-0x05 are reserved HTTP/2 settings
        long[] reserved = [
            Http3SettingsIdentifier.ReservedH2EnablePush,
            Http3SettingsIdentifier.ReservedH2MaxConcurrentStreams,
            Http3SettingsIdentifier.ReservedH2InitialWindowSize,
            Http3SettingsIdentifier.ReservedH2MaxFrameSize,
        ];

        foreach (var id in reserved)
        {
            var buf = new byte[16];
            var offset = QuicVarInt.Encode(id, buf);
            offset += QuicVarInt.Encode(0, buf.AsSpan(offset));

            var ex = Assert.Throws<Http3Exception>(
                () => Settings.Deserialize(buf[..offset]));
            Assert.Equal(Http3ErrorCode.SettingsError, ex.ErrorCode);
            Assert.Contains("reserved", ex.Message.ToLowerInvariant());
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Settings_should_handle_many_valid_settings_without_crash()
    {
        // Stress test: 100 unique extension settings
        var buf = new byte[2000];
        var offset = 0;

        for (long i = 0x10; i < 0x74; i++) // 100 unique identifiers starting at 0x10
        {
            offset += QuicVarInt.Encode(i, buf.AsSpan(offset));
            offset += QuicVarInt.Encode(i * 100, buf.AsSpan(offset));
        }

        var settings = Settings.Deserialize(buf[..offset]);

        Assert.Equal(100, settings.AllParameters.Count);
        Assert.Equal(1600, settings[0x10]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Settings_should_handle_max_varint_values()
    {
        // QUIC varint max = 2^62 - 1
        var buf = new byte[32];
        var offset = 0;
        offset += QuicVarInt.Encode(0x100, buf.AsSpan(offset)); // Custom identifier
        offset += QuicVarInt.Encode(QuicVarInt.MaxValue, buf.AsSpan(offset)); // Max value

        var settings = Settings.Deserialize(buf[..offset]);

        Assert.Equal(QuicVarInt.MaxValue, settings[0x100]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Settings_should_roundtrip_serialize_deserialize()
    {
        var original = new Settings();
        original.Set(Http3SettingsIdentifier.QpackMaxTableCapacity, 4096);
        original.Set(Http3SettingsIdentifier.QpackBlockedStreams, 100);
        original.Set(Http3SettingsIdentifier.MaxFieldSectionSize, 16384);
        original.Set(0xFF, 42); // Extension setting

        var serialized = original.Serialize();
        var restored = Settings.Deserialize(serialized);

        Assert.Equal(4096, restored.QpackMaxTableCapacity);
        Assert.Equal(100, restored.QpackBlockedStreams);
        Assert.Equal(16384, restored.MaxFieldSectionSize);
        Assert.Equal(42, restored[0xFF]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4.1")]
    public void RejectForbiddenH2Settings_should_throw_for_each_reserved_id()
    {
        var parameters = new List<(long Identifier, long Value)>
        {
            (Http3SettingsIdentifier.ReservedH2EnablePush, 1),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3SettingsIdentifier.RejectForbiddenH2Settings(parameters));
        Assert.Equal(Http3ErrorCode.SettingsError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4.1")]
    public void RejectForbiddenH2Settings_should_accept_valid_parameters()
    {
        var parameters = new List<(long Identifier, long Value)>
        {
            (Http3SettingsIdentifier.QpackMaxTableCapacity, 4096),
            (Http3SettingsIdentifier.QpackBlockedStreams, 100),
            (Http3SettingsIdentifier.MaxFieldSectionSize, 8192),
            (0xFF, 42), // Extension setting
        };

        // Should not throw
        Http3SettingsIdentifier.RejectForbiddenH2Settings(parameters);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-7")]
    public void QpackDecoder_should_handle_empty_header_block_gracefully()
    {
        var decoder = new QpackDecoder(maxTableCapacity: 4096, maxBlockedStreams: 100);

        // Minimal valid header block: RIC=0, DeltaBase=0
        var block = new byte[] { 0x00, 0x00 };

        var result = decoder.Decode(block);
        Assert.Empty(result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-7")]
    public void QpackDecoder_should_throw_when_header_block_is_too_short()
    {
        var decoder = new QpackDecoder(maxTableCapacity: 4096, maxBlockedStreams: 100);

        // Single byte — not enough for the required prefix
        var block = new byte[] { 0x00 };

        Assert.ThrowsAny<Exception>(() => decoder.Decode(block));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10")]
    public void FrameDecoder_should_decode_settings_frame_from_serialized_bytes()
    {
        using var decoder = new FrameDecoder();

        var settings = new Http3SettingsFrame(new List<(long, long)>
        {
            (Http3SettingsIdentifier.QpackMaxTableCapacity, 4096),
            (Http3SettingsIdentifier.QpackBlockedStreams, 100),
        });

        var serialized = settings.Serialize();
        var status = decoder.TryDecode(serialized, out var frame, out _);

        Assert.Equal(DecodeStatus.Success, status);
        var settingsFrame = Assert.IsType<Http3SettingsFrame>(frame);
        Assert.Equal(2, settingsFrame.Parameters.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10")]
    public void FrameDecoder_should_handle_large_payload_without_crash()
    {
        using var decoder = new FrameDecoder();

        // Large DATA frame (64KB payload)
        var payload = new byte[65536];
        new Random(42).NextBytes(payload);

        var typeBuf = new byte[8];
        var typeLen = QuicVarInt.Encode((long)FrameType.Data, typeBuf);
        var lenBuf = new byte[8];
        var lenLen = QuicVarInt.Encode(payload.Length, lenBuf);

        var frame = new byte[typeLen + lenLen + payload.Length];
        Array.Copy(typeBuf, 0, frame, 0, typeLen);
        Array.Copy(lenBuf, 0, frame, typeLen, lenLen);
        Array.Copy(payload, 0, frame, typeLen + lenLen, payload.Length);

        var status = decoder.TryDecode(frame, out var decoded, out _);
        Assert.Equal(DecodeStatus.Success, status);
        var dataFrame = Assert.IsType<Http3DataFrame>(decoded);
        Assert.Equal(65536, dataFrame.Data.Length);
        dataFrame.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10")]
    public void FrameDecoder_should_skip_rapid_unknown_frame_flood()
    {
        using var decoder = new FrameDecoder();

        // Build 1000 unknown-type frames in a single buffer
        var singleFrame = new byte[] { 0x21, 0x00 }; // type=0x21 (unknown), length=0
        var all = new byte[singleFrame.Length * 1000];
        for (var i = 0; i < 1000; i++)
        {
            singleFrame.CopyTo(all, i * singleFrame.Length);
        }

        var frames = decoder.DecodeAll(all, out var consumed);

        // All frames should be skipped (null frame = unknown type)
        Assert.Empty(frames);
        Assert.Equal(all.Length, consumed);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.6")]
    public void GoAwayFrame_should_reject_negative_stream_id()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Http3GoAwayFrame(-1));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.3")]
    public void CancelPushFrame_should_reject_negative_push_id()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Http3CancelPushFrame(-1));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.7")]
    public void MaxPushIdFrame_should_reject_negative_push_id()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Http3MaxPushIdFrame(-1));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.6")]
    public void GoAwayFrame_should_roundtrip_serialize_decode()
    {
        using var decoder = new FrameDecoder();

        var goaway = new Http3GoAwayFrame(42);
        var serialized = goaway.Serialize();

        var status = decoder.TryDecode(serialized, out var frame, out _);
        Assert.Equal(DecodeStatus.Success, status);
        var decoded = Assert.IsType<Http3GoAwayFrame>(frame);
        Assert.Equal(42, decoded.StreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9000-16")]
    public void QuicVarInt_should_reject_value_exceeding_max()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => QuicVarInt.Encode(QuicVarInt.MaxValue + 1, new byte[8]));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9000-16")]
    public void QuicVarInt_should_return_false_on_empty_input()
    {
        var result = QuicVarInt.TryDecode(ReadOnlySpan<byte>.Empty, out _, out _);
        Assert.False(result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9000-16")]
    public void QuicVarInt_should_roundtrip_all_boundary_values()
    {
        long[] boundaries = [0, 63, 64, 16383, 16384, 1073741823, 1073741824, QuicVarInt.MaxValue];

        foreach (var value in boundaries)
        {
            var buf = new byte[8];
            var written = QuicVarInt.Encode(value, buf);
            var decoded = QuicVarInt.Decode(buf, out var consumed);

            Assert.Equal(value, decoded);
            Assert.Equal(written, consumed);
        }
    }
}
