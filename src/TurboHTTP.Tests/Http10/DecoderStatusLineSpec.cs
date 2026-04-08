using System.Net;
using System.Text;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Http10;

namespace TurboHTTP.Tests.Http10;

/// <summary>
/// Tests HTTP/1.0 response status-line parsing per RFC 1945 §6.1.
/// Verifies version, status code, and reason phrase extraction.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http10Decoder"/>.
/// RFC 1945 §6.1: Status-Line — HTTP-Version SP Status-Code SP Reason-Phrase CRLF.
/// </remarks>
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

    private static ReadOnlyMemory<byte> BuildRawResponse(
        string statusLine,
        string headers,
        byte[] body)
    {
        var headerPart = Encoding.ASCII.GetBytes($"{statusLine}\r\n{headers}\r\n\r\n");
        var result = new byte[headerPart.Length + body.Length];
        headerPart.CopyTo(result, 0);
        body.CopyTo(result, headerPart.Length);
        return result;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void Http10DecoderStatusLineSpec_should_parse200ok()
    {
        var decoder = new Http10Decoder();
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
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 404 Not Found", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.NotFound, response!.StatusCode);
        Assert.Equal("Not Found", response.ReasonPhrase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    
    public void Http10DecoderStatusLineSpec_should_parse500internalservererror()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 500 Internal Server Error", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.InternalServerError, response!.StatusCode);
        Assert.Equal("Internal Server Error", response.ReasonPhrase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    
    public void Http10DecoderStatusLineSpec_should_parse301movedpermanently()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 301 Moved Permanently",
            "Location: http://example.com/new\r\nContent-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.MovedPermanently, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    
    public void Http10DecoderStatusLineSpec_should_preservemultiwordreasonphrase()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 Very Long Reason Phrase Here", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal("Very Long Reason Phrase Here", response!.ReasonPhrase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    
    public void Http10DecoderStatusLineSpec_should_setversiontohttp10()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal(new Version(1, 0), response!.Version);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    
    public void Http10DecoderStatusLineSpec_should_throwdecoderexception()
    {
        var decoder = new Http10Decoder();
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
        var decoder = new Http10Decoder();
        var data = BuildRawResponse($"HTTP/1.0 {code} Reason", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal((HttpStatusCode)code, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    
    public void Http10DecoderStatusLineSpec_should_acceptunknownstatuscode()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 299 Custom", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal((HttpStatusCode)299, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    
    public void Http10DecoderStatusLineSpec_should_rejectstatuscode()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 99 TooLow", "Content-Length: 0");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(data, out _));
        Assert.Equal(HttpDecoderError.InvalidStatusLine, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    
    public void Http10DecoderStatusLineSpec_should_rejectstatuscode_2()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 1000 TooHigh", "Content-Length: 0");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(data, out _));
        Assert.Equal(HttpDecoderError.InvalidStatusLine, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    
    public void Http10DecoderStatusLineSpec_should_acceptlfonlylineendings()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\nContent-Length: 5\n\nHello";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    
    public void Http10DecoderStatusLineSpec_should_acceptemptyreasonphrase()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 \r\nContent-Length: 0\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
        Assert.Equal("", response.ReasonPhrase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    
    public void Http10DecoderStatusLineSpec_should_treatashttp09()
    {
        // RFC 1945 §3.1: data without HTTP/ prefix is a Simple-Response (HTTP/0.9)
        var decoder = new Http10Decoder();
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
