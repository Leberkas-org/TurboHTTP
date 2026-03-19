using System.Net;
using System.Text;
using TurboHttp.Protocol;
using TurboHttp.Protocol.RFC1945;

namespace TurboHttp.Tests.RFC1945;

/// <summary>
/// Tests HTTP/1.0 response status-line parsing per RFC 1945 §6.1.
/// Verifies version, status code, and reason phrase extraction.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http10Decoder"/>.
/// RFC 1945 §6.1: Status-Line — HTTP-Version SP Status-Code SP Reason-Phrase CRLF.
/// </remarks>
public sealed class Http10DecoderStatusLineTests
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

    [Fact(DisplayName = "RFC1945-6-SL-001: Status-Line format for HTTP/1.0 200 OK")]
    public void Should_Parse200Ok_When_DecodingStatusLine()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("OK", response.ReasonPhrase);
    }

    [Fact(DisplayName = "RFC1945-6-SL-002: Status-Line format for HTTP/1.0 404 Not Found")]
    public void Should_Parse404NotFound_When_DecodingStatusLine()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 404 Not Found", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.NotFound, response!.StatusCode);
        Assert.Equal("Not Found", response.ReasonPhrase);
    }

    [Fact(DisplayName = "RFC1945-6-SL-003: Status-Line format for HTTP/1.0 500 Internal Server Error")]
    public void Should_Parse500InternalServerError_When_DecodingStatusLine()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 500 Internal Server Error", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.InternalServerError, response!.StatusCode);
        Assert.Equal("Internal Server Error", response.ReasonPhrase);
    }

    [Fact(DisplayName = "RFC1945-6-SL-004: Status-Line format for HTTP/1.0 301 Moved Permanently")]
    public void Should_Parse301MovedPermanently_When_DecodingStatusLine()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 301 Moved Permanently",
            "Location: http://example.com/new\r\nContent-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.MovedPermanently, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-6-SL-005: Status-Line reason phrase with multiple words preserved")]
    public void Should_PreserveMultiWordReasonPhrase_When_DecodingStatusLine()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 Very Long Reason Phrase Here", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal("Very Long Reason Phrase Here", response!.ReasonPhrase);
    }

    [Fact(DisplayName = "RFC1945-6-SL-006: Status-Line HTTP version is 1.0")]
    public void Should_SetVersionToHttp10_When_DecodingStatusLine()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal(new Version(1, 0), response!.Version);
    }

    [Fact(DisplayName = "RFC1945-6-SL-007: Invalid status code rejected")]
    public void Should_ThrowDecoderException_When_StatusCodeIsInvalid()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 ABC BadCode", "Content-Length: 0");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(data, out _));
        Assert.Equal(HttpDecoderError.InvalidStatusLine, ex.DecodeError);
    }

    [Theory(DisplayName = "RFC1945-6-SL-008: Common RFC1945 status codes parsed")]
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
    public void Should_ParseStatusCodeCorrectly_When_CommonStatusCode(int code)
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse($"HTTP/1.0 {code} Reason", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal((HttpStatusCode)code, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-6-SL-009: Unknown status code 299 accepted")]
    public void Should_AcceptUnknownStatusCode_When_299()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 299 Custom", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal((HttpStatusCode)299, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-6-SL-010: Status code 99 (too low) rejected")]
    public void Should_RejectStatusCode_When_99()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 99 TooLow", "Content-Length: 0");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(data, out _));
        Assert.Equal(HttpDecoderError.InvalidStatusLine, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC1945-6-SL-011: Status code 1000 (too high) rejected")]
    public void Should_RejectStatusCode_When_1000()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 1000 TooHigh", "Content-Length: 0");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(data, out _));
        Assert.Equal(HttpDecoderError.InvalidStatusLine, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC1945-6-SL-012: LF-only line endings accepted in HTTP/1.0")]
    public void Should_AcceptLfOnlyLineEndings_When_Http10()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\nContent-Length: 5\n\nHello";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-6-SL-013: Empty reason phrase after status code accepted")]
    public void Should_AcceptEmptyReasonPhrase_When_StatusCodeOnly()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 \r\nContent-Length: 0\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
        Assert.Equal("", response.ReasonPhrase);
    }

    [Fact(DisplayName = "RFC1945-6-SL-014: Only header separator without status-line rejected")]
    public void Should_ThrowDecoderException_When_OnlyHeaderSeparatorPresent()
    {
        var decoder = new Http10Decoder();
        var data = Bytes("\r\n\r\n");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(data, out _));
        Assert.Equal(HttpDecoderError.InvalidStatusLine, ex.DecodeError);
    }
}
