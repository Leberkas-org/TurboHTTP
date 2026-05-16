using System.Net;
using System.Text;
using TurboHTTP.Protocol.LineBased;

namespace TurboHTTP.Tests.Protocol.LineBased;

public sealed class StatusLineParserSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void TryParse_should_parse_standard_status_line()
    {
        var line = "HTTP/1.1 200 OK\r\n"u8.ToArray();
        Assert.True(StatusLineParser.TryParse(line, out var version, out var status, out var reason, out var consumed));

        Assert.Equal(HttpVersion.Version11, version);
        Assert.Equal(200, status);
        Assert.Equal("OK", reason);
        Assert.Equal(17, consumed);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void TryParse_should_accept_empty_reason()
    {
        var line = "HTTP/1.0 204 \r\n"u8.ToArray();
        Assert.True(StatusLineParser.TryParse(line, out _, out var status, out var reason, out _));
        Assert.Equal(204, status);
        Assert.Equal("", reason);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void TryParse_should_accept_multiword_reason()
    {
        var line = "HTTP/1.1 500 Internal Server Error\r\n"u8.ToArray();
        Assert.True(StatusLineParser.TryParse(line, out _, out _, out var reason, out _));
        Assert.Equal("Internal Server Error", reason);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void TryParse_should_parse_http10_response()
    {
        var line = "HTTP/1.0 301 Moved Permanently\r\n"u8.ToArray();
        Assert.True(StatusLineParser.TryParse(line, out var version, out var status, out _, out _));
        Assert.Equal(HttpVersion.Version10, version);
        Assert.Equal(301, status);
    }

    [Theory(Timeout = 5000)]
    [InlineData("HTTP/1.1\r\n")]
    [InlineData("HTTP/1.1 200\r\n")]
    [InlineData("HTTP/1.1 BAD OK\r\n")]
    [InlineData("HTTP/1.1 200 OK")]
    [InlineData("HTTP/1.1 99 Too Low\r\n")]
    [InlineData("HTTP/1.1 600 Too High\r\n")]
    [Trait("RFC", "RFC9112-4")]
    public void TryParse_should_reject_malformed_status_line(string raw)
    {
        var data = Encoding.ASCII.GetBytes(raw);
        Assert.False(StatusLineParser.TryParse(data, out _, out _, out _, out _));
    }
}