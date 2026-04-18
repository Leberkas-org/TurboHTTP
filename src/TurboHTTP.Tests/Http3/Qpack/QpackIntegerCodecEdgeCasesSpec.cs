using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Tests.Http3.Qpack;

/// <summary>
/// Edge-case tests for QPACK integer codec to achieve 100% branch coverage.
/// Tests boundary conditions, overflow scenarios, and all prefix bit combinations.
/// </summary>
public sealed class QpackIntegerCodecEdgeCasesSpec
{
    /// RFC 9204 §4.1.1 — Encode value fitting in 1-bit prefix
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.1")]
    public void Should_Encode_When_Value_Fits_In_OneBitPrefix()
    {
        var output = new byte[10].AsSpan();
        var outputRef = output;
        var bytesWritten = QpackIntegerCodec.Encode(0, prefixBits: 1, prefixFlags: 0x00, ref outputRef);

        Assert.Equal(1, bytesWritten);
        Assert.Equal(0x00, output[0]);
    }

    /// RFC 9204 §4.1.1 — Encode value fitting in 8-bit prefix
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.1")]
    public void Should_Encode_When_Value_Fits_In_EightBitPrefix()
    {
        var output = new byte[10].AsSpan();
        var outputRef = output;
        var bytesWritten = QpackIntegerCodec.Encode(200, prefixBits: 8, prefixFlags: 0x00, ref outputRef);

        Assert.Equal(1, bytesWritten);
        Assert.Equal(200, output[0]);
    }

    /// RFC 9204 §4.1.1 — Encode value exceeding prefix requires continuation bytes
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.1")]
    public void Should_Encode_MultiByteInteger_When_Value_Exceeds_Prefix()
    {
        var output = new byte[10].AsSpan();
        var outputRef = output;
        // Prefix mask for 6 bits: 0x3F. Value 127 exceeds 6-bit mask (63), needs continuation.
        var bytesWritten = QpackIntegerCodec.Encode(127, prefixBits: 6, prefixFlags: 0x40, ref outputRef);

        Assert.True(bytesWritten > 1, "Multi-byte encoding required");
        Assert.Equal(0x40 | 0x3F, output[0]); // First byte with prefix flags and mask
    }

    /// RFC 9204 §4.1.1 — Encode large integer requiring multiple continuation bytes
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.1")]
    public void Should_Encode_LargeInteger_With_Multiple_ContinuationBytes()
    {
        var output = new byte[20].AsSpan();
        var outputRef = output;
        // Large value that requires multiple continuation bytes
        var bytesWritten = QpackIntegerCodec.Encode(100000, prefixBits: 8, prefixFlags: 0x00, ref outputRef);

        Assert.True(bytesWritten > 2, "Should require multiple continuation bytes");
        // Decode to verify round-trip
        var pos = 0;
        var decoded = QpackIntegerCodec.Decode(output[..bytesWritten], ref pos, 8);
        Assert.Equal(100000, decoded);
    }

    /// RFC 9204 §4.1.1 — Encode with prefix flags applied
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.1")]
    public void Should_Encode_With_PrefixFlags_Applied()
    {
        var output = new byte[10].AsSpan();
        var outputRef = output;
        // Encode value 10 with 5-bit prefix and prefix flags 0xC0 (both high bits set)
        var bytesWritten = QpackIntegerCodec.Encode(10, prefixBits: 5, prefixFlags: 0xC0, ref outputRef);

        Assert.Equal(1, bytesWritten);
        // Should have 0xC0 | 10 = 0xCA
        Assert.Equal(0xC0 | 10, output[0]);
    }

    /// RFC 9204 §4.1.1 — Decode integer fitting in prefix
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.1")]
    public void Should_Decode_When_Value_Fits_In_Prefix()
    {
        var data = new byte[] { 0x42 }; // Binary: 01000010
        var pos = 0;
        var value = QpackIntegerCodec.Decode(data, ref pos, 6); // 6-bit prefix

        Assert.Equal(0x02, value); // Lower 6 bits: 000010 = 2
        Assert.Equal(1, pos);
    }

    /// RFC 9204 §4.1.1 — Decode multi-byte integer
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.1")]
    public void Should_Decode_MultiByteInteger()
    {
        var data = new byte[] { 0x7F, 0x00 }; // Prefix filled (127), no continuation
        var pos = 0;
        var value = QpackIntegerCodec.Decode(data, ref pos, 7); // 7-bit prefix

        Assert.Equal(127, value);
        Assert.Equal(2, pos);
    }

    /// RFC 9204 §4.1.1 — Decode integer with continuation bytes (shift >= 7)
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.1")]
    public void Should_Decode_IntegerWithContinuationBytes()
    {
        // Encode 1337 and then decode it
        var encoded = new byte[10];
        var output = encoded.AsSpan();
        var outputRef = output;
        var bytesWritten = QpackIntegerCodec.Encode(1337, prefixBits: 6, prefixFlags: 0x40, ref outputRef);

        var pos = 0;
        var decoded = QpackIntegerCodec.Decode(encoded, ref pos, 6);
        Assert.Equal(1337, decoded);
        Assert.Equal(bytesWritten, pos);
    }

    /// RFC 9204 §4.1.1 — Decode throws when encountering unexpected end (no stop bit)
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.1")]
    public void Should_Throw_When_Decode_Integer_Truncated()
    {
        var data = new byte[] { 0xFF, 0xFF }; // Continuation bytes without stop bit
        var pos = 0;

        var ex = Assert.Throws<QpackException>(() =>
            QpackIntegerCodec.Decode(data, ref pos, 8));

        Assert.Contains("truncated", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// RFC 9204 §4.1.1 — Decode throws when integer overflows (shift >= 62)
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.1")]
    public void Should_Throw_When_Integer_Shift_Overflow()
    {
        // Craft data that triggers shift >= 62
        var data = new byte[20];
        data[0] = 0xFF; // Full prefix
        for (var i = 1; i < 15; i++)
        {
            data[i] = 0xFF; // Continuation bytes with high bit set
        }
        data[15] = 0x7F; // Last byte without continuation

        var pos = 0;
        var ex = Assert.Throws<QpackException>(() =>
            QpackIntegerCodec.Decode(data, ref pos, 8));

        Assert.Contains("overflow", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// RFC 9204 §4.1.1 — Decode throws when integer value exceeds maximum
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.1")]
    public void Should_Throw_When_Integer_Value_Exceeds_Maximum()
    {
        // Create an integer that exceeds MaxIntegerValue during decoding
        var data = new byte[20];
        data[0] = 0xFF; // Full prefix (255)
        // Add continuation bytes that accumulate to a very large value
        for (var i = 1; i < 10; i++)
        {
            data[i] = 0xFF; // Continuation with high bit
        }
        data[10] = 0x7F; // Stop bit

        var pos = 0;
        var ex = Assert.Throws<QpackException>(() =>
            QpackIntegerCodec.Decode(data, ref pos, 8));

        Assert.Contains("overflow", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// RFC 9204 §4.1.1 — Encode throws on negative value
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.1")]
    public void Should_Throw_On_Encode_Negative_Value()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            var output = new byte[10].AsSpan();
            QpackIntegerCodec.Encode(-1, prefixBits: 8, prefixFlags: 0x00, ref output);
        });

        Assert.Equal("value", ex.ParamName);
    }

    /// RFC 9204 §4.1.1 — Encode throws on invalid prefix bits (0)
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.1")]
    public void Should_Throw_On_Encode_Invalid_PrefixBits_Zero()
    {
        var output = new byte[10].AsSpan();

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            var output2 = new byte[10].AsSpan();
            QpackIntegerCodec.Encode(100, prefixBits: 0, prefixFlags: 0x00, ref output2);
        });

        Assert.Equal("prefixBits", ex.ParamName);
    }

    /// RFC 9204 §4.1.1 — Encode throws on invalid prefix bits (9)
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.1")]
    public void Should_Throw_On_Encode_Invalid_PrefixBits_TooLarge()
    {
        var output = new byte[10].AsSpan();

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            var output2 = new byte[10].AsSpan();
            QpackIntegerCodec.Encode(100, prefixBits: 9, prefixFlags: 0x00, ref output2);
        });

        Assert.Equal("prefixBits", ex.ParamName);
    }

    /// RFC 9204 §4.1.1 — Decode throws on invalid prefix bits (0)
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.1")]
    public void Should_Throw_On_Decode_Invalid_PrefixBits_Zero()
    {
        var data = new byte[] { 0x42 };
        var pos = 0;

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            QpackIntegerCodec.Decode(data, ref pos, 0));

        Assert.Equal("prefixBits", ex.ParamName);
    }

    /// RFC 9204 §4.1.1 — Decode throws on invalid prefix bits (9)
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.1")]
    public void Should_Throw_On_Decode_Invalid_PrefixBits_TooLarge()
    {
        var data = new byte[] { 0x42 };
        var pos = 0;

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            QpackIntegerCodec.Decode(data, ref pos, 9));

        Assert.Equal("prefixBits", ex.ParamName);
    }

    /// RFC 9204 §4.1.1 — Decode throws when position at or past length
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.1")]
    public void Should_Throw_On_Decode_Position_At_End()
    {
        var data = new byte[] { 0x42 };
        var pos = 1;

        var ex = Assert.Throws<QpackException>(() =>
            QpackIntegerCodec.Decode(data, ref pos, 8));

        Assert.Contains("Unexpected end", ex.Message);
    }
}
