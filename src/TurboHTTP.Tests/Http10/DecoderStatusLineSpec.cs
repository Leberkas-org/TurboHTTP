using System.Net;
using System.Text;
using TurboHTTP.Protocol;
using Decoder = TurboHTTP.Protocol.Http10.Decoder;

namespace TurboHTTP.Tests.Http10;

public sealed class Http10DecoderStatusLineSpec
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
    [Trait("RFC", "RFC1945-6")]
    public void Http10DecoderStatusLineSpec_should_parse200ok()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("OK", response.ReasonPhrase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void Http10DecoderStatusLineSpec_should_parse404notfound()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 404 Not Found", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.NotFound, response!.StatusCode);
        Assert.Equal("Not Found", response.ReasonPhrase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void Http10DecoderStatusLineSpec_should_parse500_internal_server_error()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 500 Internal Server Error", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.InternalServerError, response!.StatusCode);
        Assert.Equal("Internal Server Error", response.ReasonPhrase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void Http10DecoderStatusLineSpec_should_parse301_moved_permanently()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 301 Moved Permanently",
            "Location: http://example.com/new\r\nContent-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.MovedPermanently, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void Http10DecoderStatusLineSpec_should_preserve_multi_word_reason_phrase()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 Very Long Reason Phrase Here", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal("Very Long Reason Phrase Here", response!.ReasonPhrase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void Http10DecoderStatusLineSpec_should_set_version_to_http10()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal(new Version(1, 0), response!.Version);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void Http10DecoderStatusLineSpec_should_throw_decoder_exception()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 ABC BadCode", "Content-Length: 0");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(data, out _));
        Assert.Equal(HttpDecoderError.InvalidStatusLine, ex.DecodeError);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(202)]
    [InlineData(204)]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(304)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(501)]
    [InlineData(502)]
    [InlineData(503)]
    public void Http10DecoderStatusLineSpec_should_parse_status_code_correctly_when_common_status_code(int code)
    {
        var decoder = new Decoder();
        var data = BuildRawResponse($"HTTP/1.0 {code} Reason", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal((HttpStatusCode)code, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void Http10DecoderStatusLineSpec_should_accept_unknown_status_code()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 299 Custom", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal((HttpStatusCode)299, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void Http10DecoderStatusLineSpec_should_reject_status_code()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 99 TooLow", "Content-Length: 0");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(data, out _));
        Assert.Equal(HttpDecoderError.InvalidStatusLine, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void Http10DecoderStatusLineSpec_should_reject_statu_scode_2()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 1000 TooHigh", "Content-Length: 0");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(data, out _));
        Assert.Equal(HttpDecoderError.InvalidStatusLine, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void Http10DecoderStatusLineSpec_should_accept_lf_only_line_endings()
    {
        var decoder = new Decoder();
        const string raw = "HTTP/1.0 200 OK\nContent-Length: 5\n\nHello";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void Http10DecoderStatusLineSpec_should_accept_empty_reason_phrase()
    {
        var decoder = new Decoder();
        const string raw = "HTTP/1.0 200 \r\nContent-Length: 0\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
        Assert.Equal("", response.ReasonPhrase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void Http10DecoderStatusLineSpec_should_treat_as_http09()
    {
        var decoder = new Decoder();
        var data = Bytes("\r\n\r\n");

        var result = decoder.TryDecode(data, out var response);
        Assert.False(result);
        Assert.Null(response);

        // EOF completes as HTTP/0.9 with body = "\r\n\r\n"
        result = decoder.TryDecodeEof(out response);
        Assert.True(result);
        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Version(0, 9), response.Version);
    }
}