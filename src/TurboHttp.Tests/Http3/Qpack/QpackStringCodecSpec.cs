using System.Buffers;
using System.Text;
using TurboHttp.Protocol.Http3.Qpack;

namespace TurboHttp.Tests.Http3.Qpack;

/// <summary>
/// Tests for QPACK string literal encoding/decoding per RFC 9204 §4.1.2.
/// Covers plain strings, Huffman-encoded strings, and empty strings.
/// </summary>
public sealed class QpackStringCodecSpec
{
    /// RFC 9204 §4.1.2 — Plain string roundtrip
    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.2")]
    [InlineData("hello")]
    [InlineData("GET")]
    [InlineData("/index.html")]
    [InlineData("content-type")]
    public void Should_RoundtripPlainString(string text)
    {
        var value = Encoding.ASCII.GetBytes(text);
        var buffer = new ArrayBufferWriter<byte>();

        QpackStringCodec.Encode(value, 7, 0x00, useHuffman: false, buffer);

        var pos = 0;
        var decoded = QpackStringCodec.Decode(buffer.WrittenSpan, ref pos, 7);

        Assert.Equal(value, decoded);
        Assert.Equal(buffer.WrittenCount, pos);
    }

    /// RFC 9204 §4.1.2 — Huffman-encoded string roundtrip
    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.2")]
    [InlineData("www.example.com")]
    [InlineData("custom-header")]
    [InlineData("application/json")]
    [InlineData("no-cache")]
    public void Should_RoundtripHuffmanString(string text)
    {
        var value = Encoding.ASCII.GetBytes(text);
        var buffer = new ArrayBufferWriter<byte>();

        QpackStringCodec.Encode(value, 7, 0x00, useHuffman: true, buffer);

        // Verify H bit is set in first byte
        Assert.NotEqual(0, buffer.WrittenSpan[0] & 0x80);

        var pos = 0;
        var decoded = QpackStringCodec.Decode(buffer.WrittenSpan, ref pos, 7);

        Assert.Equal(value, decoded);
        Assert.Equal(buffer.WrittenCount, pos);
    }

    /// RFC 9204 §4.1.2 — Empty string encoding and decoding
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.2")]
    public void Should_HandleEmptyString()
    {
        var buffer = new ArrayBufferWriter<byte>();
        QpackStringCodec.Encode(ReadOnlySpan<byte>.Empty, 7, 0x00, buffer);

        Assert.Equal(1, buffer.WrittenCount);
        Assert.Equal(0x00, buffer.WrittenSpan[0]); // H=0, length=0

        var pos = 0;
        var decoded = QpackStringCodec.Decode(buffer.WrittenSpan, ref pos, 7);

        Assert.Empty(decoded);
        Assert.Equal(1, pos);
    }

    /// RFC 9204 §4.1.2 — Auto-selects Huffman when shorter
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.2")]
    public void Should_AutoSelectHuffman_When_Shorter()
    {
        var value = Encoding.ASCII.GetBytes("www.example.com");
        var bufferAuto = new ArrayBufferWriter<byte>();
        var bufferPlain = new ArrayBufferWriter<byte>();

        QpackStringCodec.Encode(value, 7, 0x00, bufferAuto);
        QpackStringCodec.Encode(value, 7, 0x00, useHuffman: false, bufferPlain);

        // Huffman for ASCII text should be shorter — auto should pick it
        Assert.True(bufferAuto.WrittenCount <= bufferPlain.WrittenCount,
            "Auto-encoding should pick the shorter representation");

        // Verify auto-encode decodes correctly
        var pos = 0;
        var decoded = QpackStringCodec.Decode(bufferAuto.WrittenSpan, ref pos, 7);
        Assert.Equal(value, decoded);
    }

    /// RFC 9204 §4.1.2 — Decode rejects truncated string data
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.2")]
    public void Should_ThrowQpackException_When_StringTruncated()
    {
        var buffer = new ArrayBufferWriter<byte>();
        QpackStringCodec.Encode(Encoding.ASCII.GetBytes("hello"), 7, 0x00, useHuffman: false, buffer);

        // Truncate: keep length byte but remove some string data
        var truncated = buffer.WrittenSpan[..3].ToArray();
        var pos = 0;

        Assert.Throws<QpackException>(() => QpackStringCodec.Decode(truncated, ref pos, 7));
    }

    /// RFC 9204 §4.1.2 — Decode rejects empty input
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.2")]
    public void Should_ThrowQpackException_When_InputEmpty()
    {
        var pos = 0;
        Assert.Throws<QpackException>(() => QpackStringCodec.Decode(ReadOnlySpan<byte>.Empty, ref pos, 7));
    }
}
