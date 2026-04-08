using System.Net;
using System.Text;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Http10;

namespace TurboHTTP.Tests.Http10;

/// <summary>
/// Tests HTTP/1.0 response header parsing per RFC 1945 §4.2.
/// Verifies field names, values, folded headers, and empty header blocks.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http10Decoder"/>.
/// RFC 1945 §4.2: Message headers — name ':' value, optional folding.
/// </remarks>
public sealed class Http10DecoderHeaderSpec
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
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_parsesingleheader()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "Content-Type: text/plain\r\nContent-Length: 5", "Hello");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.NotNull(response);
        Assert.NotNull(response.Content);

        Assert.True(response.Content.Headers.TryGetValues("Content-Type", out var values));
        Assert.Contains("text/plain", values);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_parsecustomheader()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "X-Custom-Header: my-value\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("X-Custom-Header", out var values));
        Assert.Contains("my-value", values);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_parseallcustomheaders()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "X-Header-A: value-a\r\nX-Header-B: value-b\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("X-Header-A", out var a));
        Assert.True(response.Headers.TryGetValues("X-Header-B", out var b));
        Assert.Contains("value-a", a);
        Assert.Contains("value-b", b);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_becaseinsensitive()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "x-custom-header: lower-case\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("X-Custom-Header", out var values)
                    || response.Headers.TryGetValues("x-custom-header", out values));
        Assert.Contains("lower-case", values);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_continuefoldedheader()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nX-Folded: first part\r\n continued\r\nContent-Length: 0\r\n\r\n";

        decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(response!.Headers.TryGetValues("X-Folded", out var values));

        var combined = string.Join(" ", values);
        Assert.Contains("first part", combined);
        Assert.Contains("continued", combined);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_trimwhitespace()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "X-Spaced:   trimmed-value   \r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("X-Spaced", out var values));
        Assert.Contains("trimmed-value", values);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_parseheaders()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\nX-Lf-Header: lf-value\nContent-Length: 0\n\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.NotNull(response);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_mergedoubleobsfold()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nX-Multi: part1\r\n part2\r\n part3\r\nContent-Length: 0\r\n\r\n";

        decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(response!.Headers.TryGetValues("X-Multi", out var values));
        var combined = string.Join("", values);
        Assert.Contains("part1", combined);
        Assert.Contains("part2", combined);
        Assert.Contains("part3", combined);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_preservebothheaders()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nX-Dup: first\r\nX-Dup: second\r\nContent-Length: 0\r\n\r\n";

        decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(response!.Headers.TryGetValues("X-Dup", out var values));
        var list = values.ToList();
        Assert.Contains("first", list);
        Assert.Contains("second", list);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_throwinvalidheader()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nBadHeaderNoColon\r\nContent-Length: 0\r\n\r\n";

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecoderError.InvalidHeader, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_matchcaseinsensitive()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nCONTENT-LENGTH: 5\r\n\r\nHello";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(5, response!.Content.Headers.ContentLength);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_trimwhitespace_2()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nX-Trimmed:    hello world   \r\nContent-Length: 0\r\n\r\n";

        decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(response!.Headers.TryGetValues("X-Trimmed", out var values));
        Assert.Equal("hello world", values.First());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_throwinvalidfieldname()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nBad Name: value\r\nContent-Length: 0\r\n\r\n";

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecoderError.InvalidFieldName, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_accepttab()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nX-Tab: before\tafter\r\nContent-Length: 0\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.True(response!.Headers.TryGetValues("X-Tab", out var values));
        Assert.Contains("before\tafter", values);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_acceptresponse()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_skipsafely()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nX-Empty:\r\nContent-Length: 0\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }
}
