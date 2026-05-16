using System.Text;
using TurboHTTP.Protocol.LineBased;

namespace TurboHTTP.Tests.Protocol.LineBased;

public sealed class HeaderFieldParserSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5")]
    public void TryParse_should_parse_simple_name_value()
    {
        var line = "Host: example.com"u8.ToArray();
        Assert.True(HeaderFieldParser.TryParse(line, out var name, out var value));
        Assert.Equal("Host", name);
        Assert.Equal("example.com", value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.5")]
    public void TryParse_should_trim_ows_around_value()
    {
        var line = "Host:    example.com   "u8.ToArray();
        Assert.True(HeaderFieldParser.TryParse(line, out _, out var value));
        Assert.Equal("example.com", value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5")]
    public void TryParse_should_preserve_name_case()
    {
        var line = "Content-Length: 42"u8.ToArray();
        Assert.True(HeaderFieldParser.TryParse(line, out var name, out _));
        Assert.Equal("Content-Length", name);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5")]
    public void TryParse_should_accept_empty_value()
    {
        var line = "X-Empty:"u8.ToArray();
        Assert.True(HeaderFieldParser.TryParse(line, out var name, out var value));
        Assert.Equal("X-Empty", name);
        Assert.Equal("", value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5")]
    public void TryParse_should_accept_value_with_tabs()
    {
        var line = "X-Tab:\tvalue\there"u8.ToArray();
        Assert.True(HeaderFieldParser.TryParse(line, out _, out var value));
        Assert.Equal("value\there", value);
    }

    [Theory(Timeout = 5000)]
    [InlineData("NoColon")]
    [InlineData(":NoName")]
    [InlineData("Bad Name: value")]
    [Trait("RFC", "RFC9110-5")]
    public void TryParse_should_reject_invalid_lines(string raw)
    {
        var line = Encoding.ASCII.GetBytes(raw);
        Assert.False(HeaderFieldParser.TryParse(line, out _, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5")]
    public void TryParse_should_reject_obs_fold_start()
    {
        var line = " continuation value"u8.ToArray();
        Assert.False(HeaderFieldParser.TryParse(line, out _, out _));
    }

    [Fact(Timeout = 5000)]
    public void TryParse_should_reject_empty_input()
    {
        Assert.False(HeaderFieldParser.TryParse(ReadOnlySpan<byte>.Empty, out _, out _));
    }
}