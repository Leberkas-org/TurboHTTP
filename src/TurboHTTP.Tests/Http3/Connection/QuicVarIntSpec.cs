using TurboHTTP.Protocol.Http3;

namespace TurboHTTP.Tests.Http3.Connection;

public sealed class QuicVarIntSpec
{

    [Theory]
    [Trait("RFC", "RFC9000-16")]
    [InlineData(0, new byte[] { 0x00 })]
    [InlineData(1, new byte[] { 0x01 })]
    [InlineData(37, new byte[] { 0x25 })]
    [InlineData(63, new byte[] { 0x3F })]
    public void QuicVarInt_should_encode_1_byte_when_value_up_to_63(long value, byte[] expected)
    {
        Span<byte> buf = stackalloc byte[8];
        var written = QuicVarInt.Encode(value, buf);

        Assert.Equal(1, written);
        Assert.Equal(expected, buf[..written].ToArray());
    }

    [Theory]
    [Trait("RFC", "RFC9000-16")]
    [InlineData(new byte[] { 0x00 }, 0)]
    [InlineData(new byte[] { 0x01 }, 1)]
    [InlineData(new byte[] { 0x25 }, 37)]
    [InlineData(new byte[] { 0x3F }, 63)]
    public void QuicVarInt_should_decode_1_byte_when_prefix_is_00(byte[] input, long expected)
    {
        var ok = QuicVarInt.TryDecode(input, out var value, out var consumed);

        Assert.True(ok);
        Assert.Equal(expected, value);
        Assert.Equal(1, consumed);
    }


    [Theory]
    [Trait("RFC", "RFC9000-16")]
    [InlineData(64, new byte[] { 0x40, 0x40 })]
    [InlineData(494, new byte[] { 0x41, 0xEE })]
    [InlineData(16383, new byte[] { 0x7F, 0xFF })]
    public void QuicVarInt_should_encode_2_bytes_when_value_64_to_16383(long value, byte[] expected)
    {
        Span<byte> buf = stackalloc byte[8];
        var written = QuicVarInt.Encode(value, buf);

        Assert.Equal(2, written);
        Assert.Equal(expected, buf[..written].ToArray());
    }

    [Theory]
    [Trait("RFC", "RFC9000-16")]
    [InlineData(new byte[] { 0x40, 0x40 }, 64)]
    [InlineData(new byte[] { 0x41, 0xEE }, 494)]
    [InlineData(new byte[] { 0x7F, 0xFF }, 16383)]
    public void QuicVarInt_should_decode_2_bytes_when_prefix_is_01(byte[] input, long expected)
    {
        var ok = QuicVarInt.TryDecode(input, out var value, out var consumed);

        Assert.True(ok);
        Assert.Equal(expected, value);
        Assert.Equal(2, consumed);
    }


    [Theory]
    [Trait("RFC", "RFC9000-16")]
    [InlineData(16384, new byte[] { 0x80, 0x00, 0x40, 0x00 })]
    [InlineData(494878333, new byte[] { 0x9D, 0x7F, 0x3E, 0x7D })]
    [InlineData(1073741823, new byte[] { 0xBF, 0xFF, 0xFF, 0xFF })]
    public void QuicVarInt_should_encode_4_bytes_when_value_up_to_2_pow_30_minus_1(long value, byte[] expected)
    {
        Span<byte> buf = stackalloc byte[8];
        var written = QuicVarInt.Encode(value, buf);

        Assert.Equal(4, written);
        Assert.Equal(expected, buf[..written].ToArray());
    }

    [Theory]
    [Trait("RFC", "RFC9000-16")]
    [InlineData(new byte[] { 0x80, 0x00, 0x40, 0x00 }, 16384)]
    [InlineData(new byte[] { 0x9D, 0x7F, 0x3E, 0x7D }, 494878333)]
    [InlineData(new byte[] { 0xBF, 0xFF, 0xFF, 0xFF }, 1073741823)]
    public void QuicVarInt_should_decode_4_bytes_when_prefix_is_10(byte[] input, long expected)
    {
        var ok = QuicVarInt.TryDecode(input, out var value, out var consumed);

        Assert.True(ok);
        Assert.Equal(expected, value);
        Assert.Equal(4, consumed);
    }


    [Theory]
    [Trait("RFC", "RFC9000-16")]
    [InlineData(1073741824, new byte[] { 0xC0, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00 })]
    [InlineData(151288809941952652, new byte[] { 0xC2, 0x19, 0x7C, 0x5E, 0xFF, 0x14, 0xE8, 0x8C })]
    public void QuicVarInt_should_encode_8_bytes_when_value_up_to_2_pow_62_minus_1(long value, byte[] expected)
    {
        Span<byte> buf = stackalloc byte[8];
        var written = QuicVarInt.Encode(value, buf);

        Assert.Equal(8, written);
        Assert.Equal(expected, buf[..written].ToArray());
    }

    [Theory]
    [Trait("RFC", "RFC9000-16")]
    [InlineData(new byte[] { 0xC0, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00 }, 1073741824)]
    [InlineData(new byte[] { 0xC2, 0x19, 0x7C, 0x5E, 0xFF, 0x14, 0xE8, 0x8C }, 151288809941952652)]
    public void QuicVarInt_should_decode_8_bytes_when_prefix_is_11(byte[] input, long expected)
    {
        var ok = QuicVarInt.TryDecode(input, out var value, out var consumed);

        Assert.True(ok);
        Assert.Equal(expected, value);
        Assert.Equal(8, consumed);
    }

    [Fact]
    [Trait("RFC", "RFC9000-16")]
    public void QuicVarInt_should_encode_8_bytes_when_max_value()
    {
        Span<byte> buf = stackalloc byte[8];
        var written = QuicVarInt.Encode(QuicVarInt.MaxValue, buf);

        Assert.Equal(8, written);
        Assert.Equal(0xFF, buf[0]);
        Assert.Equal(0xFF, buf[1]);
        Assert.Equal(0xFF, buf[7]);
    }


    [Theory]
    [Trait("RFC", "RFC9000-16")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(16383)]
    [InlineData(16384)]
    [InlineData(1073741823)]
    [InlineData(1073741824)]
    [InlineData(151288809941952652)]
    public void QuicVarInt_should_preserve_value_when_encode_then_decode(long original)
    {
        Span<byte> buf = stackalloc byte[8];
        var written = QuicVarInt.Encode(original, buf);

        var ok = QuicVarInt.TryDecode(buf[..written], out var decoded, out var consumed);

        Assert.True(ok);
        Assert.Equal(original, decoded);
        Assert.Equal(written, consumed);
    }

    [Fact]
    [Trait("RFC", "RFC9000-16")]
    public void QuicVarInt_should_preserve_max_value_when_roundtrip()
    {
        Span<byte> buf = stackalloc byte[8];
        var written = QuicVarInt.Encode(QuicVarInt.MaxValue, buf);

        var ok = QuicVarInt.TryDecode(buf[..written], out var decoded, out var consumed);

        Assert.True(ok);
        Assert.Equal(QuicVarInt.MaxValue, decoded);
        Assert.Equal(written, consumed);
    }


    [Theory]
    [Trait("RFC", "RFC9000-16")]
    [InlineData(0, 1)]
    [InlineData(63, 1)]
    [InlineData(64, 2)]
    [InlineData(16383, 2)]
    [InlineData(16384, 4)]
    [InlineData(1073741823, 4)]
    [InlineData(1073741824, 8)]
    public void QuicVarInt_should_return_correct_length_when_value_given(long value, int expectedLength)
    {
        Assert.Equal(expectedLength, QuicVarInt.EncodedLength(value));
    }


    [Fact]
    [Trait("RFC", "RFC9000-16")]
    public void QuicVarInt_should_throw_when_value_exceeds_max()
    {
        var buf = new byte[8];
        Assert.Throws<ArgumentOutOfRangeException>(() => QuicVarInt.Encode(QuicVarInt.MaxValue + 1, buf));
    }

    [Fact]
    [Trait("RFC", "RFC9000-16")]
    public void QuicVarInt_should_throw_when_negative_value()
    {
        var buf = new byte[8];
        Assert.Throws<ArgumentOutOfRangeException>(() => QuicVarInt.Encode(-1, buf));
    }

    [Fact]
    [Trait("RFC", "RFC9000-16")]
    public void QuicVarInt_should_return_false_when_empty_input()
    {
        var ok = QuicVarInt.TryDecode(ReadOnlySpan<byte>.Empty, out _, out _);
        Assert.False(ok);
    }

    [Fact]
    [Trait("RFC", "RFC9000-16")]
    public void QuicVarInt_should_return_false_when_truncated_2_byte()
    {
        var ok = QuicVarInt.TryDecode([0x40], out _, out _);
        Assert.False(ok);
    }

    [Fact]
    [Trait("RFC", "RFC9000-16")]
    public void QuicVarInt_should_return_false_when_truncated_4_byte()
    {
        var ok = QuicVarInt.TryDecode([0x80, 0x00], out _, out _);
        Assert.False(ok);
    }

    [Fact]
    [Trait("RFC", "RFC9000-16")]
    public void QuicVarInt_should_return_false_when_truncated_8_byte()
    {
        var ok = QuicVarInt.TryDecode([0xC0, 0x00, 0x00, 0x00], out _, out _);
        Assert.False(ok);
    }

    [Fact]
    [Trait("RFC", "RFC9000-16")]
    public void QuicVarInt_should_throw_when_decode_insufficient_data()
    {
        Assert.Throws<ArgumentException>(() => QuicVarInt.Decode([], out _));
    }

    [Fact]
    [Trait("RFC", "RFC9000-16")]
    public void QuicVarInt_should_throw_when_destination_too_small()
    {
        var buf = new byte[1];
        Assert.Throws<ArgumentException>(() => QuicVarInt.Encode(16384, buf));
    }


    [Fact]
    [Trait("RFC", "RFC9000-A.1")]
    public void QuicVarInt_should_match_rfc_example_when_value_37()
    {
        Span<byte> buf = stackalloc byte[8];
        QuicVarInt.Encode(37, buf);
        Assert.Equal(0x25, buf[0]);
    }

    [Fact]
    [Trait("RFC", "RFC9000-A.1")]
    public void QuicVarInt_should_match_rfc_example_when_value_15293()
    {
        Span<byte> buf = stackalloc byte[8];
        var written = QuicVarInt.Encode(15293, buf);
        Assert.Equal(2, written);
        Assert.Equal(new byte[] { 0x7B, 0xBD }, buf[..2].ToArray());
    }

    [Fact]
    [Trait("RFC", "RFC9000-A.1")]
    public void QuicVarInt_should_match_rfc_example_when_value_494878333()
    {
        Span<byte> buf = stackalloc byte[8];
        var written = QuicVarInt.Encode(494878333, buf);
        Assert.Equal(4, written);

        Assert.Equal(new byte[] { 0x9D, 0x7F, 0x3E, 0x7D }, buf[..4].ToArray());
    }
}
