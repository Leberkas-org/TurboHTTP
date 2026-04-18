using System.Text;
using Decoder = TurboHTTP.Protocol.Http10.Decoder;

namespace TurboHTTP.Tests.Http10;

public sealed class Http10DecoderConnectionSpec
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-8")]
    public void Http10DecoderConnectionSpec_should_default_to_close()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        // HTTP/1.0 default: no Connection header means close
        Assert.False(response!.Headers.TryGetValues("Connection", out _));
        Assert.Equal(new Version(1, 0), response.Version);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-8")]
    public void Http10DecoderConnectionSpec_should_recognize_keepalive()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "Connection: keep-alive\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("Connection", out var values));
        Assert.Contains("keep-alive", values);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-8")]
    public void Http10DecoderConnectionSpec_should_parse_keepalive_params()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "Connection: keep-alive\r\nKeep-Alive: timeout=5, max=100\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("Keep-Alive", out var values));
        var value = values.First();
        Assert.Contains("timeout=5", value);
        Assert.Contains("max=100", value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-8")]
    public void Http10DecoderConnectionSpec_should_signal_close()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "Connection: close\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("Connection", out var values));
        Assert.Contains("close", values);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-8")]
    public void Http10DecoderConnectionSpec_should_not_default_to_keepalive()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        // No Connection header → not keep-alive in HTTP/1.0
        var hasConnection = response!.Headers.TryGetValues("Connection", out var values);
        Assert.True(!hasConnection || !values!.Any(v =>
            v.Equals("keep-alive", StringComparison.OrdinalIgnoreCase)));
    }
}
