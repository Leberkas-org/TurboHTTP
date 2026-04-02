using TurboHttp.Protocol.Http3;

namespace TurboHttp.Tests.Http3.Connection;

public sealed class QuicVarIntSpec
{

    [Theory(DisplayName = "RFC9000-16-VI-001: 1-byte encoding for values 0-63")]
    [InlineData(0, new byte[] { 0x00 })]
    [InlineData(1, new byte[] { 0x01 })]
    [InlineData(37, new byte[] { 0x25 })]
    [InlineData(63, new byte[] { 0x3F })]
    public void Should_Encode1Byte_When_ValueUpTo63(long value, byte[] expected)
    {
        Span<byte> buf = stackalloc byte[8];
        var written = QuicVarInt.Encode(value, buf);

        Assert.Equal(1, written);
        Assert.Equal(expected, buf[..written].ToArray());
    }

    [Theory(DisplayName = "RFC9000-16-VI-002: 1-byte decoding for values 0-63")]
    [InlineData(new byte[] { 0x00 }, 0)]
    [InlineData(new byte[] { 0x01 }, 1)]
    [InlineData(new byte[] { 0x25 }, 37)]
    [InlineData(new byte[] { 0x3F }, 63)]
    public void Should_Decode1Byte_When_PrefixIs00(byte[] input, long expected)
    {
        var ok = QuicVarInt.TryDecode(input, out var value, out var consumed);

        Assert.True(ok);
        Assert.Equal(expected, value);
        Assert.Equal(1, consumed);
    }


    [Theory(DisplayName = "RFC9000-16-VI-003: 2-byte encoding for values 64-16383")]
    [InlineData(64, new byte[] { 0x40, 0x40 })]
    [InlineData(494, new byte[] { 0x41, 0xEE })]
    [InlineData(16383, new byte[] { 0x7F, 0xFF })]
    public void Should_Encode2Bytes_When_Value64To16383(long value, byte[] expected)
    {
        Span<byte> buf = stackalloc byte[8];
        var written = QuicVarInt.Encode(value, buf);

        Assert.Equal(2, written);
        Assert.Equal(expected, buf[..written].ToArray());
    }

    [Theory(DisplayName = "RFC9000-16-VI-004: 2-byte decoding for values 64-16383")]
    [InlineData(new byte[] { 0x40, 0x40 }, 64)]
    [InlineData(new byte[] { 0x41, 0xEE }, 494)]
    [InlineData(new byte[] { 0x7F, 0xFF }, 16383)]
    public void Should_Decode2Bytes_When_PrefixIs01(byte[] input, long expected)
    {
        var ok = QuicVarInt.TryDecode(input, out var value, out var consumed);

        Assert.True(ok);
        Assert.Equal(expected, value);
        Assert.Equal(2, consumed);
    }


    [Theory(DisplayName = "RFC9000-16-VI-005: 4-byte encoding for values up to 2^30-1")]
    [InlineData(16384, new byte[] { 0x80, 0x00, 0x40, 0x00 })]
    [InlineData(494878333, new byte[] { 0x9D, 0x7F, 0x3E, 0x7D })]
    [InlineData(1073741823, new byte[] { 0xBF, 0xFF, 0xFF, 0xFF })]
    public void Should_Encode4Bytes_When_ValueUpTo2Pow30Minus1(long value, byte[] expected)
    {
        Span<byte> buf = stackalloc byte[8];
        var written = QuicVarInt.Encode(value, buf);

        Assert.Equal(4, written);
        Assert.Equal(expected, buf[..written].ToArray());
    }

    [Theory(DisplayName = "RFC9000-16-VI-006: 4-byte decoding for values up to 2^30-1")]
    [InlineData(new byte[] { 0x80, 0x00, 0x40, 0x00 }, 16384)]
    [InlineData(new byte[] { 0x9D, 0x7F, 0x3E, 0x7D }, 494878333)]
    [InlineData(new byte[] { 0xBF, 0xFF, 0xFF, 0xFF }, 1073741823)]
    public void Should_Decode4Bytes_When_PrefixIs10(byte[] input, long expected)
    {
        var ok = QuicVarInt.TryDecode(input, out var value, out var consumed);

        Assert.True(ok);
        Assert.Equal(expected, value);
        Assert.Equal(4, consumed);
    }


    [Theory(DisplayName = "RFC9000-16-VI-007: 8-byte encoding for values up to 2^62-1")]
    [InlineData(1073741824, new byte[] { 0xC0, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00 })]
    [InlineData(151288809941952652, new byte[] { 0xC2, 0x19, 0x7C, 0x5E, 0xFF, 0x14, 0xE8, 0x8C })]
    public void Should_Encode8Bytes_When_ValueUpTo2Pow62Minus1(long value, byte[] expected)
    {
        Span<byte> buf = stackalloc byte[8];
        var written = QuicVarInt.Encode(value, buf);

        Assert.Equal(8, written);
        Assert.Equal(expected, buf[..written].ToArray());
    }

    [Theory(DisplayName = "RFC9000-16-VI-008: 8-byte decoding for values up to 2^62-1")]
    [InlineData(new byte[] { 0xC0, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00 }, 1073741824)]
    [InlineData(new byte[] { 0xC2, 0x19, 0x7C, 0x5E, 0xFF, 0x14, 0xE8, 0x8C }, 151288809941952652)]
    public void Should_Decode8Bytes_When_PrefixIs11(byte[] input, long expected)
    {
        var ok = QuicVarInt.TryDecode(input, out var value, out var consumed);

        Assert.True(ok);
        Assert.Equal(expected, value);
        Assert.Equal(8, consumed);
    }

    [Fact(DisplayName = "RFC9000-16-VI-009: 8-byte encoding for max value 2^62-1")]
    public void Should_Encode8Bytes_When_MaxValue()
    {
        Span<byte> buf = stackalloc byte[8];
        var written = QuicVarInt.Encode(QuicVarInt.MaxValue, buf);

        Assert.Equal(8, written);
        Assert.Equal(0xFF, buf[0]);
        Assert.Equal(0xFF, buf[1]);
        Assert.Equal(0xFF, buf[7]);
    }


    [Theory(DisplayName = "RFC9000-16-VI-010: Encode-Decode roundtrip preserves value")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(16383)]
    [InlineData(16384)]
    [InlineData(1073741823)]
    [InlineData(1073741824)]
    [InlineData(151288809941952652)]
    public void Should_PreserveValue_When_EncodeThenDecode(long original)
    {
        Span<byte> buf = stackalloc byte[8];
        var written = QuicVarInt.Encode(original, buf);

        var ok = QuicVarInt.TryDecode(buf[..written], out var decoded, out var consumed);

        Assert.True(ok);
        Assert.Equal(original, decoded);
        Assert.Equal(written, consumed);
    }

    [Fact(DisplayName = "RFC9000-16-VI-011: Roundtrip for max value")]
    public void Should_PreserveMaxValue_When_Roundtrip()
    {
        Span<byte> buf = stackalloc byte[8];
        var written = QuicVarInt.Encode(QuicVarInt.MaxValue, buf);

        var ok = QuicVarInt.TryDecode(buf[..written], out var decoded, out var consumed);

        Assert.True(ok);
        Assert.Equal(QuicVarInt.MaxValue, decoded);
        Assert.Equal(written, consumed);
    }


    [Theory(DisplayName = "RFC9000-16-VI-012: EncodedLength returns correct byte count")]
    [InlineData(0, 1)]
    [InlineData(63, 1)]
    [InlineData(64, 2)]
    [InlineData(16383, 2)]
    [InlineData(16384, 4)]
    [InlineData(1073741823, 4)]
    [InlineData(1073741824, 8)]
    public void Should_ReturnCorrectLength_When_ValueGiven(long value, int expectedLength)
    {
        Assert.Equal(expectedLength, QuicVarInt.EncodedLength(value));
    }


    [Fact(DisplayName = "RFC9000-16-VI-013: Encode throws on value exceeding max")]
    public void Should_Throw_When_ValueExceedsMax()
    {
        var buf = new byte[8];
        Assert.Throws<ArgumentOutOfRangeException>(() => QuicVarInt.Encode(QuicVarInt.MaxValue + 1, buf));
    }

    [Fact(DisplayName = "RFC9000-16-VI-014: Encode throws on negative value")]
    public void Should_Throw_When_NegativeValue()
    {
        var buf = new byte[8];
        Assert.Throws<ArgumentOutOfRangeException>(() => QuicVarInt.Encode(-1, buf));
    }

    [Fact(DisplayName = "RFC9000-16-VI-015: TryDecode returns false on empty input")]
    public void Should_ReturnFalse_When_EmptyInput()
    {
        var ok = QuicVarInt.TryDecode(ReadOnlySpan<byte>.Empty, out _, out _);
        Assert.False(ok);
    }

    [Fact(DisplayName = "RFC9000-16-VI-016: TryDecode returns false on truncated 2-byte")]
    public void Should_ReturnFalse_When_Truncated2Byte()
    {
        var ok = QuicVarInt.TryDecode(new byte[] { 0x40 }, out _, out _);
        Assert.False(ok);
    }

    [Fact(DisplayName = "RFC9000-16-VI-017: TryDecode returns false on truncated 4-byte")]
    public void Should_ReturnFalse_When_Truncated4Byte()
    {
        var ok = QuicVarInt.TryDecode(new byte[] { 0x80, 0x00 }, out _, out _);
        Assert.False(ok);
    }

    [Fact(DisplayName = "RFC9000-16-VI-018: TryDecode returns false on truncated 8-byte")]
    public void Should_ReturnFalse_When_Truncated8Byte()
    {
        var ok = QuicVarInt.TryDecode(new byte[] { 0xC0, 0x00, 0x00, 0x00 }, out _, out _);
        Assert.False(ok);
    }

    [Fact(DisplayName = "RFC9000-16-VI-019: Decode throws on insufficient data")]
    public void Should_Throw_When_DecodeInsufficientData()
    {
        Assert.Throws<ArgumentException>(() => QuicVarInt.Decode(Array.Empty<byte>(), out _));
    }

    [Fact(DisplayName = "RFC9000-16-VI-020: Encode throws when destination too small")]
    public void Should_Throw_When_DestinationTooSmall()
    {
        var buf = new byte[1];
        Assert.Throws<ArgumentException>(() => QuicVarInt.Encode(16384, buf));
    }


    [Fact(DisplayName = "RFC9000-A.1-VI-021: RFC example value 37 encodes to 0x25")]
    public void Should_MatchRfcExample_When_Value37()
    {
        Span<byte> buf = stackalloc byte[8];
        QuicVarInt.Encode(37, buf);
        Assert.Equal(0x25, buf[0]);
    }

    [Fact(DisplayName = "RFC9000-A.1-VI-022: RFC example value 15293 encodes to 0x7bbd")]
    public void Should_MatchRfcExample_When_Value15293()
    {
        Span<byte> buf = stackalloc byte[8];
        var written = QuicVarInt.Encode(15293, buf);
        Assert.Equal(2, written);
        Assert.Equal(new byte[] { 0x7B, 0xBD }, buf[..2].ToArray());
    }

    [Fact(DisplayName = "RFC9000-A.1-VI-023: RFC example value 494878333 encodes to 8-byte")]
    public void Should_MatchRfcExample_When_Value494878333()
    {
        // 494878333 < 2^30 = 1073741824, so 4-byte encoding
        Span<byte> buf = stackalloc byte[8];
        var written = QuicVarInt.Encode(494878333, buf);
        Assert.Equal(4, written);

        // RFC 9000 Appendix A: 494878333 → 0x9d7f3e7d
        Assert.Equal(new byte[] { 0x9D, 0x7F, 0x3E, 0x7D }, buf[..4].ToArray());
    }
}
