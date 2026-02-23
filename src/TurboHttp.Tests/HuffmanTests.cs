using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests;

public sealed class HuffmanTests
{
    [Fact]
    public void Encode_WwwExample_MatchesRfc7541()
    {
        var input = "www.example.com"u8.ToArray();
        var encoded = HuffmanCodec.Encode(input);
        var expected = new byte[] { 0xf1, 0xe3, 0xc2, 0xe5, 0xf2, 0x3a, 0x6b, 0xa0, 0xab, 0x90, 0xf4, 0xff };
        Assert.Equal(expected, encoded);
    }

    [Fact]
    public void Encode_NoCache_MatchesRfc7541()
    {
        var input = "no-cache"u8.ToArray();
        var encoded = HuffmanCodec.Encode(input);
        var expected = new byte[] { 0xa8, 0xeb, 0x10, 0x64, 0x9c, 0xbf };
        Assert.Equal(expected, encoded);
    }

    [Fact]
    public void Decode_WwwExample_MatchesRfc7541()
    {
        var encoded = new byte[] { 0xf1, 0xe3, 0xc2, 0xe5, 0xf2, 0x3a, 0x6b, 0xa0, 0xab, 0x90, 0xf4, 0xff };
        var decoded = HuffmanCodec.Decode(encoded);
        Assert.Equal("www.example.com", Encoding.UTF8.GetString(decoded));
    }

    [Fact]
    public void Decode_NoCache_MatchesRfc7541()
    {
        var encoded = new byte[] { 0xa8, 0xeb, 0x10, 0x64, 0x9c, 0xbf };
        var decoded = HuffmanCodec.Decode(encoded);
        Assert.Equal("no-cache", Encoding.UTF8.GetString(decoded));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("hello")]
    [InlineData("Hello, World!")]
    [InlineData("GET")]
    [InlineData("content-type")]
    [InlineData("application/json")]
    [InlineData("https")]
    [InlineData("/api/v1/users?page=1&size=50")]
    [InlineData("Mozilla/5.0 (compatible)")]
    [InlineData("0123456789abcdefghijklmnopqrstuvwxyz")]
    public void RoundTrip_EncodeThenDecode_ReturnsOriginal(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var encoded = HuffmanCodec.Encode(bytes);
        var decoded = HuffmanCodec.Decode(encoded);
        Assert.Equal(input, Encoding.UTF8.GetString(decoded));
    }

    [Fact]
    public void Encode_AlwaysCompressesOrEqualCommonHeaders()
    {
        var values = new[] { "gzip, deflate", "text/html", "keep-alive", "200", "no-cache" };
        foreach (var v in values)
        {
            var bytes = Encoding.UTF8.GetBytes(v);
            var encoded = HuffmanCodec.Encode(bytes);
            Assert.True(encoded.Length <= bytes.Length + 1,
                $"'{v}': huffman={encoded.Length} > literal={bytes.Length}");
        }
    }
}