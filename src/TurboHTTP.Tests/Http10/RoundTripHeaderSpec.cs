using System.Text;
using Decoder = TurboHTTP.Protocol.Http10.Decoder;
using Encoder = TurboHTTP.Protocol.Http10.Encoder;

namespace TurboHTTP.Tests.Http10;

public sealed class Http10RoundTripHeaderSpec
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
    public void Http10RoundTripHeaderSpec_should_preserve_content_type_header()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "Content-Type: application/json\r\nContent-Length: 2", "{}");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Content.Headers.ContentType?.MediaType == "application/json");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10RoundTripHeaderSpec_should_preserve_content_length_header()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "Content-Length: 13", "Hello, World!");

        decoder.TryDecode(data, out var response);

        Assert.NotNull(response);
        Assert.Equal(13, response.Content.Headers.ContentLength);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10RoundTripHeaderSpec_should_preserve_custom_header()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "X-Custom-Header: CustomValue\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.Contains("X-Custom-Header"));
        Assert.Equal("CustomValue", response.Headers.GetValues("X-Custom-Header").First());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10RoundTripHeaderSpec_should_preserve_location_header()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 301 Moved Permanently",
            "Location: http://example.com/new-location\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.Contains("Location"));
        Assert.Equal("http://example.com/new-location",
            response.Headers.GetValues("Location").First());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10RoundTripHeaderSpec_should_preserve_multiple_custom_headers()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "X-Header-1: Value1\r\nX-Header-2: Value2\r\nX-Header-3: Value3\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.Contains("X-Header-1"));
        Assert.True(response.Headers.Contains("X-Header-2"));
        Assert.True(response.Headers.Contains("X-Header-3"));
        Assert.Equal("Value1", response.Headers.GetValues("X-Header-1").First());
        Assert.Equal("Value2", response.Headers.GetValues("X-Header-2").First());
        Assert.Equal("Value3", response.Headers.GetValues("X-Header-3").First());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10RoundTripHeaderSpec_should_preserve_server_header()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "Server: TestServer/1.0\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.Contains("Server"));
        Assert.Equal("TestServer/1.0", response.Headers.GetValues("Server").First());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10RoundTripHeaderSpec_should_preserve_date_header()
    {
        var decoder = new Decoder();
        const string dateValue = "Thu, 06 Mar 2026 12:00:00 GMT";
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            $"Date: {dateValue}\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.Contains("Date"));
        Assert.Equal(dateValue, response.Headers.GetValues("Date").First());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10RoundTripHeaderSpec_should_preserve_header_with_special_chars()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "X-Data: value-with-dash_and_underscore\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal("value-with-dash_and_underscore",
            response!.Headers.GetValues("X-Data").First());
    }
}