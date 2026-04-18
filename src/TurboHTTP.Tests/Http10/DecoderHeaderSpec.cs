using System.Net;
using System.Text;
using TurboHTTP.Protocol;
using Decoder = TurboHTTP.Protocol.Http10.Decoder;

namespace TurboHTTP.Tests.Http10;

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
    public void Http10DecoderHeaderSpec_should_parse_single_header()
    {
        var decoder = new Decoder();
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
    public void Http10DecoderHeaderSpec_should_parse_custom_header()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "X-Custom-Header: my-value\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("X-Custom-Header", out var values));
        Assert.Contains("my-value", values);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_parse_all_custom_headers()
    {
        var decoder = new Decoder();
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
    public void Http10DecoderHeaderSpec_should_be_case_insensitive()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "x-custom-header: lower-case\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("X-Custom-Header", out var values)
                    || response.Headers.TryGetValues("x-custom-header", out values));
        Assert.Contains("lower-case", values);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_continue_folded_header()
    {
        var decoder = new Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nX-Folded: first part\r\n continued\r\nContent-Length: 0\r\n\r\n";

        decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(response!.Headers.TryGetValues("X-Folded", out var values));

        var combined = string.Join(" ", values);
        Assert.Contains("first part", combined);
        Assert.Contains("continued", combined);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_trim_whitespace()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "X-Spaced:   trimmed-value   \r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("X-Spaced", out var values));
        Assert.Contains("trimmed-value", values);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_parse_headers()
    {
        var decoder = new Decoder();
        const string raw = "HTTP/1.0 200 OK\nX-Lf-Header: lf-value\nContent-Length: 0\n\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.NotNull(response);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_merge_double_obs_fold()
    {
        var decoder = new Decoder();
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
    public void Http10DecoderHeaderSpec_should_preserve_both_headers()
    {
        var decoder = new Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nX-Dup: first\r\nX-Dup: second\r\nContent-Length: 0\r\n\r\n";

        decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(response!.Headers.TryGetValues("X-Dup", out var values));
        var list = values.ToList();
        Assert.Contains("first", list);
        Assert.Contains("second", list);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_throw_invalid_header()
    {
        var decoder = new Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nBadHeaderNoColon\r\nContent-Length: 0\r\n\r\n";

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecoderError.InvalidHeader, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_match_case_insensitive()
    {
        var decoder = new Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nCONTENT-LENGTH: 5\r\n\r\nHello";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(5, response!.Content.Headers.ContentLength);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_trim_whitespace_2()
    {
        var decoder = new Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nX-Trimmed:    hello world   \r\nContent-Length: 0\r\n\r\n";

        decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(response!.Headers.TryGetValues("X-Trimmed", out var values));
        Assert.Equal("hello world", values.First());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_throw_invalid_field_name()
    {
        var decoder = new Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nBad Name: value\r\nContent-Length: 0\r\n\r\n";

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecoderError.InvalidFieldName, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_accept_tab()
    {
        var decoder = new Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nX-Tab: before\tafter\r\nContent-Length: 0\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.True(response!.Headers.TryGetValues("X-Tab", out var values));
        Assert.Contains("before\tafter", values);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_accept_response()
    {
        var decoder = new Decoder();
        const string raw = "HTTP/1.0 200 OK\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderSpec_should_skip_safely()
    {
        var decoder = new Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nX-Empty:\r\nContent-Length: 0\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }
}