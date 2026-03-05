using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC1945;

public sealed class Http10DecoderConnectionTests
{
    private static ReadOnlyMemory<byte> Bytes(string s)
        => Encoding.GetEncoding("ISO-8859-1").GetBytes(s);

    private static ReadOnlyMemory<byte> BuildRawResponse(
        string statusLine,
        string headers,
        string body = "")
    {
        var raw = $"{statusLine}\r\n{headers}\r\n\r\n{body}";
        return Bytes(raw);
    }

    [Fact(DisplayName = "RFC1945-8-CONN-001: HTTP/1.0 default connection is close")]
    public void Should_DefaultToClose_When_NoConnectionHeader()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        // HTTP/1.0 default: no Connection header means close
        Assert.False(response!.Headers.TryGetValues("Connection", out _));
        Assert.Equal(new Version(1, 0), response.Version);
    }

    [Fact(DisplayName = "RFC1945-8-CONN-002: Connection: keep-alive recognized in HTTP/1.0")]
    public void Should_RecognizeKeepAlive_When_ConnectionHeaderPresent()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "Connection: keep-alive\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("Connection", out var values));
        Assert.Contains("keep-alive", values);
    }

    [Fact(DisplayName = "RFC1945-8-CONN-003: Keep-Alive timeout and max parameters parsed")]
    public void Should_ParseKeepAliveParams_When_KeepAliveHeader()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "Connection: keep-alive\r\nKeep-Alive: timeout=5, max=100\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("Keep-Alive", out var values));
        var value = values.First();
        Assert.Contains("timeout=5", value);
        Assert.Contains("max=100", value);
    }

    [Fact(DisplayName = "RFC1945-8-CONN-004: Connection: close signals close after response")]
    public void Should_SignalClose_When_ConnectionCloseHeader()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "Connection: close\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("Connection", out var values));
        Assert.Contains("close", values);
    }

    [Fact(DisplayName = "RFC1945-8-CONN-005: HTTP/1.0 does not default to keep-alive")]
    public void Should_NotDefaultToKeepAlive_When_Http10()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        // No Connection header → not keep-alive in HTTP/1.0
        var hasConnection = response!.Headers.TryGetValues("Connection", out var values);
        Assert.True(!hasConnection || !values!.Any(v =>
            v.Equals("keep-alive", StringComparison.OrdinalIgnoreCase)));
    }
}
