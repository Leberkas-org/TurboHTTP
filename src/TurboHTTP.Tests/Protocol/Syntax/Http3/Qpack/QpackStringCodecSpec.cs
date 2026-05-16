using System.Text;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Qpack;

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
        var buffer = new byte[256];
        var writer = SpanWriter.Create(buffer);

        var written = QpackStringCodec.Encode(value, 7, 0x00, useHuffman: false, ref writer);

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
        var buffer = new byte[256];
        var writer = SpanWriter.Create(buffer);

        var written = QpackStringCodec.Encode(value, 7, 0x00, useHuffman: true, ref writer);

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
        var buffer = new byte[16];
        var writer = SpanWriter.Create(buffer);
        var written = QpackStringCodec.Encode(ReadOnlySpan<byte>.Empty, 7, 0x00, ref writer);

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
        var autoBuffer = new byte[256];
        var plainBuffer = new byte[256];
        var autoWriter = SpanWriter.Create(autoBuffer);
        var plainWriter = SpanWriter.Create(plainBuffer);

        var autoWritten = QpackStringCodec.Encode(value, 7, 0x00, ref autoWriter);
        var plainWritten = QpackStringCodec.Encode(value, 7, 0x00, useHuffman: false, ref plainWriter);

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
        var buffer = new byte[256];
        var writer = SpanWriter.Create(buffer);
        QpackStringCodec.Encode("hello"u8, 7, 0x00, useHuffman: false, ref writer);

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