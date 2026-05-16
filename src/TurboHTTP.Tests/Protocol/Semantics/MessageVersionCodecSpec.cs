using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Protocol.Semantics;

public sealed class MessageVersionCodecSpec
{
    [Theory(Timeout = 5000)]
    [InlineData("HTTP/1.0", "1.0")]
    [InlineData("HTTP/1.1", "1.1")]
    [InlineData("HTTP/2", "2.0")]
    [InlineData("HTTP/3", "3.0")]
    [Trait("RFC", "RFC9110-2.5")]
    public void TryParse_should_map_wire_token_to_HttpVersion(string text, string expectedToString)
    {
        Assert.True(MessageVersionCodec.TryParse(text, out var version));
        Assert.Equal(expectedToString, version.ToString());
    }

    [Theory(Timeout = 5000)]
    [InlineData("HTTP/0.9")]
    [InlineData("HTTP/1.2")]
    [InlineData("http/1.1")]
    [InlineData("HTTP-1.1")]
    [InlineData("")]
    [Trait("RFC", "RFC9110-2.5")]
    public void TryParse_should_reject_unknown_or_malformed_token(string text)
    {
        Assert.False(MessageVersionCodec.TryParse(text, out _));
    }

    [Theory(Timeout = 5000)]
    [InlineData("1.0", "HTTP/1.0")]
    [InlineData("1.1", "HTTP/1.1")]
    [InlineData("2.0", "HTTP/2")]
    [InlineData("3.0", "HTTP/3")]
    [Trait("RFC", "RFC9110-2.5")]
    public void ToWireFormat_should_format_HttpVersion_to_canonical_token(string versionStr, string expected)
    {
        var v = Version.Parse(versionStr);
        Assert.Equal(expected, MessageVersionCodec.ToWireFormat(v));
    }
}