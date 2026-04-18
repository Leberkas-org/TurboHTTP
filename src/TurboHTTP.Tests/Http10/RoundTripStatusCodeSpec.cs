using System.Net;
using System.Text;
using Decoder = TurboHTTP.Protocol.Http10.Decoder;

namespace TurboHTTP.Tests.Http10;

public sealed class Http10RoundTripStatusCodeSpec
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
    public void Http10RoundTripStatusCodeSpec_should_decode_200_ok()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void Http10RoundTripStatusCodeSpec_should_decode_201_created()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 201 Created", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.Created, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void Http10RoundTripStatusCodeSpec_should_decode_204_no_content()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 204 No Content", "");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.NoContent, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void Http10RoundTripStatusCodeSpec_should_decode_301_moved_permanently()
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
    public void Http10RoundTripStatusCodeSpec_should_decode_302_found()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 302 Found",
            "Location: http://example.com/resource\r\nContent-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.Found, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void Http10RoundTripStatusCodeSpec_should_decode304_not_modified()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 304 Not Modified",
            "ETag: \"123\"\r\nContent-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.NotModified, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void Http10RoundTripStatusCodeSpec_should_decode_400_bad_request()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 400 Bad Request",
            "Content-Type: text/plain\r\nContent-Length: 11", "Bad Request");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.BadRequest, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void Http10RoundTripStatusCodeSpec_should_decode_401_unauthorized()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 401 Unauthorized",
            "WWW-Authenticate: Basic realm=\"test\"\r\nContent-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.Unauthorized, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void Http10RoundTripStatusCodeSpec_should_decode_404_not_found()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 404 Not Found",
            "Content-Type: text/html\r\nContent-Length: 9", "Not Found");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.NotFound, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void Http10RoundTripStatusCodeSpec_should_decode_500_internal_server_error()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 500 Internal Server Error",
            "Content-Type: text/plain\r\nContent-Length: 21", "Internal Server Error");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.InternalServerError, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void Http10RoundTripStatusCodeSpec_should_decode_503_service_unavailable()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 503 Service Unavailable",
            "Retry-After: 60\r\nContent-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response!.StatusCode);
    }
}