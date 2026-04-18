using System.Text;
using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Tests.Http3.Qpack;

public sealed class QpackStringCodecSpec
{
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.2")]
    public void Should_AutoSelectHuffman_When_Shorter()
    {
        var value = "www.example.com"u8.ToArray();
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.2")]
    public void Should_ThrowQpackException_When_StringTruncated()
    {
        Span<byte> buffer = new byte[256];
        var span = buffer;
        QpackStringCodec.Encode("hello"u8, 7, 0x00, useHuffman: false, ref span);

        // Truncate: keep length byte but remove some string data
        var truncated = buffer[..3].ToArray();
        var pos = 0;

        Assert.Throws<QpackException>(() => QpackStringCodec.Decode(truncated, ref pos, 7));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.2")]
    public void Should_ThrowQpackException_When_InputEmpty()
    {
        var pos = 0;
        Assert.Throws<QpackException>(() => QpackStringCodec.Decode(ReadOnlySpan<byte>.Empty, ref pos, 7));
    }
}