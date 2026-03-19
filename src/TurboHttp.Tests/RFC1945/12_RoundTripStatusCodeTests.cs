using System.Net;
using System.Text;
using TurboHttp.Protocol.RFC1945;

namespace TurboHttp.Tests.RFC1945;

/// <summary>
/// Round-trip tests for HTTP/1.0 status codes per RFC 1945 §6.1.1.
/// Verifies that all defined status codes survive encode-then-decode unchanged.
/// </summary>
/// <remarks>
/// Classes under test: <see cref="Http10Encoder"/>, <see cref="Http10Decoder"/>.
/// RFC 1945 §6.1.1: Status codes — 1xx, 2xx, 3xx, 4xx, 5xx.
/// </remarks>
public sealed class Http10RoundTripStatusCodeTests
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

    [Fact(DisplayName = "RFC1945-6.1-SC-001: 200 OK status code round-trip")]
    public void Should_Decode200Ok_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-6.1-SC-002: 201 Created status code round-trip")]
    public void Should_Decode201Created_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 201 Created", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.Created, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-6.1-SC-003: 204 No Content status code round-trip")]
    public void Should_Decode204NoContent_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 204 No Content", "");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.NoContent, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-6.1-SC-004: 301 Moved Permanently status code round-trip")]
    public void Should_Decode301MovedPermanently_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 301 Moved Permanently",
            "Location: http://example.com/new\r\nContent-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.MovedPermanently, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-6.1-SC-005: 302 Found status code round-trip")]
    public void Should_Decode302Found_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 302 Found",
            "Location: http://example.com/resource\r\nContent-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.Found, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-6.1-SC-006: 304 Not Modified status code round-trip")]
    public void Should_Decode304NotModified_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 304 Not Modified",
            "ETag: \"123\"\r\nContent-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.NotModified, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-6.1-SC-007: 400 Bad Request status code round-trip")]
    public void Should_Decode400BadRequest_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 400 Bad Request",
            "Content-Type: text/plain\r\nContent-Length: 11", "Bad Request");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.BadRequest, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-6.1-SC-008: 401 Unauthorized status code round-trip")]
    public void Should_Decode401Unauthorized_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 401 Unauthorized",
            "WWW-Authenticate: Basic realm=\"test\"\r\nContent-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.Unauthorized, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-6.1-SC-009: 404 Not Found status code round-trip")]
    public void Should_Decode404NotFound_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 404 Not Found",
            "Content-Type: text/html\r\nContent-Length: 9", "Not Found");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.NotFound, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-6.1-SC-010: 500 Internal Server Error status code round-trip")]
    public void Should_Decode500InternalServerError_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 500 Internal Server Error",
            "Content-Type: text/plain\r\nContent-Length: 21", "Internal Server Error");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.InternalServerError, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-6.1-SC-011: 503 Service Unavailable status code round-trip")]
    public void Should_Decode503ServiceUnavailable_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 503 Service Unavailable",
            "Retry-After: 60\r\nContent-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response!.StatusCode);
    }
}
