using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Tests.Http3.Qpack;

/// <summary>
/// Tests for QPACK integer encoding/decoding per RFC 9204 §4.1.1.
/// The integer representation is identical to HPACK (RFC 7541 §5.1)
/// but exposed as a standalone codec for QPACK use.
/// </summary>
public sealed class QpackIntegerCodecSpec
{
    // Maximum bytes a QPACK integer can occupy (1 prefix + ceil(31/7) continuation = 6 bytes max for int.MaxValue)
    private const int MaxEncodedSize = 16;

    /// RFC 9204 §4.1.1 — Value fits in 5-bit prefix (single byte)
    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.1")]
    [InlineData(0, 5, 0x00)]
    [InlineData(10, 5, 0x00)]
    [InlineData(30, 5, 0x00)]
    public void Should_EncodeSingleByte_When_ValueFitsInPrefix5(int value, int prefixBits, byte prefixFlags)
    {
        var buf = new byte[MaxEncodedSize];
        var span = buf.AsSpan();
        var written = QpackIntegerCodec.Encode(value, prefixBits, prefixFlags, ref span);

        Assert.Equal(1, written);
        Assert.Equal((byte)(prefixFlags | value), buf[0]);
    }

    /// RFC 9204 §4.1.1 — Value fits in 6-bit prefix (single byte)
    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.1")]
    [InlineData(0, 6, 0x40)]
    [InlineData(62, 6, 0x40)]
    public void Should_EncodeSingleByte_When_ValueFitsInPrefix6(int value, int prefixBits, byte prefixFlags)
    {
        var buf = new byte[MaxEncodedSize];
        var span = buf.AsSpan();
        var written = QpackIntegerCodec.Encode(value, prefixBits, prefixFlags, ref span);

        Assert.Equal(1, written);
        Assert.Equal((byte)(prefixFlags | value), buf[0]);
    }

    /// RFC 9204 §4.1.1 — Value fits in 7-bit prefix (single byte)
    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.1")]
    [InlineData(0, 7, 0x80)]
    [InlineData(126, 7, 0x80)]
    public void Should_EncodeSingleByte_When_ValueFitsInPrefix7(int value, int prefixBits, byte prefixFlags)
    {
        var buf = new byte[MaxEncodedSize];
        var span = buf.AsSpan();
        var written = QpackIntegerCodec.Encode(value, prefixBits, prefixFlags, ref span);

        Assert.Equal(1, written);
        Assert.Equal((byte)(prefixFlags | value), buf[0]);
    }

    /// RFC 9204 §4.1.1 — Value fits in 8-bit prefix (single byte)
    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.1")]
    [InlineData(0, 8, 0x00)]
    [InlineData(254, 8, 0x00)]
    public void Should_EncodeSingleByte_When_ValueFitsInPrefix8(int value, int prefixBits, byte prefixFlags)
    {
        var buf = new byte[MaxEncodedSize];
        var span = buf.AsSpan();
        var written = QpackIntegerCodec.Encode(value, prefixBits, prefixFlags, ref span);

        Assert.Equal(1, written);
        Assert.Equal((byte)(prefixFlags | value), buf[0]);
    }

    /// RFC 9204 §4.1.1 — Multi-byte encoding when value exceeds prefix capacity
    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.1")]
    [InlineData(31, 5, 0x00)]
    [InlineData(63, 6, 0x40)]
    [InlineData(127, 7, 0x80)]
    [InlineData(255, 8, 0x00)]
    [InlineData(1337, 5, 0x00)]
    [InlineData(65535, 6, 0x00)]
    public void Should_EncodeMultipleBytes_When_ValueExceedsPrefix(int value, int prefixBits, byte prefixFlags)
    {
        var buf = new byte[MaxEncodedSize];
        var span = buf.AsSpan();
        var written = QpackIntegerCodec.Encode(value, prefixBits, prefixFlags, ref span);

        Assert.True(written > 1, $"Expected multi-byte encoding for value {value} with {prefixBits}-bit prefix");

        // Verify first byte has all prefix bits set
        var mask = (1 << prefixBits) - 1;
        Assert.Equal(mask, buf[0] & mask);
    }

    /// RFC 9204 §4.1.1 — Roundtrip encode/decode preserves value for all prefix lengths
    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.1")]
    [InlineData(0, 5, 0x00)]
    [InlineData(10, 5, 0x00)]
    [InlineData(30, 5, 0x00)]
    [InlineData(31, 5, 0x00)]
    [InlineData(1337, 5, 0x00)]
    [InlineData(0, 6, 0x40)]
    [InlineData(62, 6, 0x40)]
    [InlineData(63, 6, 0x40)]
    [InlineData(500, 6, 0x40)]
    [InlineData(0, 7, 0x80)]
    [InlineData(126, 7, 0x80)]
    [InlineData(127, 7, 0x80)]
    [InlineData(10000, 7, 0x80)]
    [InlineData(0, 8, 0x00)]
    [InlineData(254, 8, 0x00)]
    [InlineData(255, 8, 0x00)]
    [InlineData(65535, 8, 0x00)]
    [InlineData(1_000_000, 5, 0x20)]
    public void Should_RoundtripCorrectly_When_EncodedThenDecoded(int value, int prefixBits, byte prefixFlags)
    {
        var buf = new byte[MaxEncodedSize];
        var span = buf.AsSpan();
        var written = QpackIntegerCodec.Encode(value, prefixBits, prefixFlags, ref span);

        var pos = 0;
        var decoded = QpackIntegerCodec.Decode(buf.AsSpan(0, written), ref pos, prefixBits);

        Assert.Equal(value, decoded);
        Assert.Equal(written, pos);
    }

    /// RFC 9204 §4.1.1 — Decode rejects truncated multi-byte integers
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.1")]
    public void Should_ThrowQpackException_When_IntegerTruncated()
    {
        // Encode a multi-byte value, then truncate
        var buf = new byte[MaxEncodedSize];
        var span = buf.AsSpan();
        var written = QpackIntegerCodec.Encode(1337, 5, 0x00, ref span);

        // Take only first 2 bytes (truncated)
        var truncated = buf[..2];
        var pos = 0;

        Assert.Throws<QpackException>(() => QpackIntegerCodec.Decode(truncated, ref pos, 5));
    }

    /// RFC 9204 §4.1.1 — Decode rejects empty input
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.1")]
    public void Should_ThrowQpackException_When_InputEmpty()
    {
        var pos = 0;
        Assert.Throws<QpackException>(() => QpackIntegerCodec.Decode(ReadOnlySpan<byte>.Empty, ref pos, 5));
    }
}
