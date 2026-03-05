using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC1945;

/// <summary>
/// RFC 1945 Round-Trip Status Code Tests
/// Verifies that HTTP/1.0 status codes are preserved through decode cycle
/// </summary>
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

    [Fact(DisplayName = "RFC1945-RT-S01: 200 OK status code round-trip")]
    public void Should_Decode200Ok_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-RT-S02: 201 Created status code round-trip")]
    public void Should_Decode201Created_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 201 Created", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.Created, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-RT-S03: 204 No Content status code round-trip")]
    public void Should_Decode204NoContent_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 204 No Content", "");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.NoContent, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-RT-S04: 301 Moved Permanently status code round-trip")]
    public void Should_Decode301MovedPermanently_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 301 Moved Permanently",
            "Location: http://example.com/new\r\nContent-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.MovedPermanently, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-RT-S05: 302 Found status code round-trip")]
    public void Should_Decode302Found_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 302 Found",
            "Location: http://example.com/resource\r\nContent-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.Found, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-RT-S06: 304 Not Modified status code round-trip")]
    public void Should_Decode304NotModified_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 304 Not Modified",
            "ETag: \"123\"\r\nContent-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.NotModified, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-RT-S07: 400 Bad Request status code round-trip")]
    public void Should_Decode400BadRequest_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 400 Bad Request",
            "Content-Type: text/plain\r\nContent-Length: 11", "Bad Request");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.BadRequest, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-RT-S08: 401 Unauthorized status code round-trip")]
    public void Should_Decode401Unauthorized_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 401 Unauthorized",
            "WWW-Authenticate: Basic realm=\"test\"\r\nContent-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.Unauthorized, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-RT-S09: 404 Not Found status code round-trip")]
    public void Should_Decode404NotFound_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 404 Not Found",
            "Content-Type: text/html\r\nContent-Length: 9", "Not Found");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.NotFound, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-RT-S10: 500 Internal Server Error status code round-trip")]
    public void Should_Decode500InternalServerError_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 500 Internal Server Error",
            "Content-Type: text/plain\r\nContent-Length: 21", "Internal Server Error");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.InternalServerError, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-RT-S11: 503 Service Unavailable status code round-trip")]
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
