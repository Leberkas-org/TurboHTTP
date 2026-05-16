using System.Net;
using System.Text;
using TurboHTTP.Protocol.LineBased;

namespace TurboHTTP.Tests.Protocol.LineBased;

public sealed class RequestLineParserSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void TryParse_should_parse_simple_get_request()
    {
        var line = "GET / HTTP/1.1\r\n"u8.ToArray();

        Assert.True(RequestLineParser.TryParse(line, out var method, out var target, out var version,
            out var consumed));
        Assert.Equal("GET", method.Method);
        Assert.Equal("/", target);
        Assert.Equal(HttpVersion.Version11, version);
        Assert.Equal(16, consumed);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void TryParse_should_parse_http10_post()
    {
        var line = "POST /submit HTTP/1.0\r\n"u8.ToArray();
        Assert.True(RequestLineParser.TryParse(line, out var method, out var target, out var version, out _));

        Assert.Equal("POST", method.Method);
        Assert.Equal("/submit", target);
        Assert.Equal(HttpVersion.Version10, version);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void TryParse_should_parse_options_asterisk()
    {
        var line = "OPTIONS * HTTP/1.1\r\n"u8.ToArray();
        Assert.True(RequestLineParser.TryParse(line, out var method, out var target, out _, out _));
        Assert.Equal("OPTIONS", method.Method);
        Assert.Equal("*", target);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void TryParse_should_parse_absolute_form()
    {
        var line = "GET http://example.com/path HTTP/1.1\r\n"u8.ToArray();
        Assert.True(RequestLineParser.TryParse(line, out _, out var target, out _, out _));
        Assert.Equal("http://example.com/path", target);
    }

    [Theory(Timeout = 5000)]
    [InlineData("")]
    [InlineData("INCOMPLETE")]
    [InlineData("GET / HTTP/1.1")]
    [Trait("RFC", "RFC9112-3")]
    public void TryParse_should_return_false_when_incomplete(string raw)
    {
        Assert.False(RequestLineParser.TryParse(Encoding.ASCII.GetBytes(raw), out _, out _, out _, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void TryParse_should_throw_when_method_invalid()
    {
        var line = "GE T / HTTP/1.1\r\n"u8.ToArray();
        Assert.Throws<ArgumentException>(() => RequestLineParser.TryParse(line, out _, out _, out _, out _));
    }
}