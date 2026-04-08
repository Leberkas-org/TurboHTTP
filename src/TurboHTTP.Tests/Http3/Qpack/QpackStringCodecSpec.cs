using System.Buffers;
using System.Text;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Tests.Http3.Qpack;

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
        Span<byte> buffer = new byte[256];
        var span = buffer;

        var written = QpackStringCodec.Encode(value, 7, 0x00, useHuffman: false, ref span);

        var pos = 0;
        var decoded = QpackStringCodec.Decode(buffer[..written], ref pos, 7);

        Assert.Equal(value, decoded);
        Assert.Equal(written, pos);
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
        Span<byte> buffer = new byte[256];
        var span = buffer;

        var written = QpackStringCodec.Encode(value, 7, 0x00, useHuffman: true, ref span);

        // Verify H bit is set in first byte
        Assert.NotEqual(0, buffer[0] & 0x80);

        var pos = 0;
        var decoded = QpackStringCodec.Decode(buffer[..written], ref pos, 7);

        Assert.Equal(value, decoded);
        Assert.Equal(written, pos);
    }

    /// RFC 9204 §4.1.2 — Empty string encoding and decoding
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.2")]
    public void Should_HandleEmptyString()
    {
        Span<byte> buffer = new byte[16];
        var span = buffer;
        var written = QpackStringCodec.Encode(ReadOnlySpan<byte>.Empty, 7, 0x00, ref span);

        Assert.Equal(1, written);
        Assert.Equal(0x00, buffer[0]); // H=0, length=0

        var pos = 0;
        var decoded = QpackStringCodec.Decode(buffer[..written], ref pos, 7);

        Assert.Empty(decoded);
        Assert.Equal(1, pos);
    }

    /// RFC 9204 §4.1.2 — Auto-selects Huffman when shorter
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.2")]
    public void Should_AutoSelectHuffman_When_Shorter()
    {
        var value = Encoding.ASCII.GetBytes("www.example.com");
        Span<byte> autoBuffer = new byte[256];
        Span<byte> plainBuffer = new byte[256];
        var autoSpan = autoBuffer;
        var plainSpan = plainBuffer;

        var autoWritten = QpackStringCodec.Encode(value, 7, 0x00, ref autoSpan);
        var plainWritten = QpackStringCodec.Encode(value, 7, 0x00, useHuffman: false, ref plainSpan);

        // Huffman for ASCII text should be shorter — auto should pick it
        Assert.True(autoWritten <= plainWritten,
            "Auto-encoding should pick the shorter representation");

        // Verify auto-encode decodes correctly
        var pos = 0;
        var decoded = QpackStringCodec.Decode(autoBuffer[..autoWritten], ref pos, 7);
        Assert.Equal(value, decoded);
    }

    /// RFC 9204 §4.1.2 — Decode rejects truncated string data
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.2")]
    public void Should_ThrowQpackException_When_StringTruncated()
    {
        Span<byte> buffer = new byte[256];
        var span = buffer;
        var written = QpackStringCodec.Encode(Encoding.ASCII.GetBytes("hello"), 7, 0x00, useHuffman: false, ref span);

        // Truncate: keep length byte but remove some string data
        var truncated = buffer[..3].ToArray();
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
