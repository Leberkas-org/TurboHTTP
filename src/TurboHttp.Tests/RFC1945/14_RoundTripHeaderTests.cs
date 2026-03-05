using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC1945;

/// <summary>
/// RFC 1945 Round-Trip Header Tests
/// Verifies that custom headers survive encode→decode cycle
/// </summary>
public sealed class Http10RoundTripHeaderTests
{
    private static Memory<byte> MakeBuffer(int size = 8192) => new byte[size];

    private static (byte[] Buffer, int Written) EncodeRequest(HttpRequestMessage request)
    {
        Memory<byte> buffer = new byte[65536];
        var written = Http10Encoder.Encode(request, ref buffer);
        return (buffer.ToArray(), written);
    }

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

    [Fact(DisplayName = "RFC1945-RT-H01: Content-Type header preserved")]
    public void Should_PreserveContentTypeHeader_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "Content-Type: application/json\r\nContent-Length: 2", "{}");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Content.Headers.ContentType?.MediaType == "application/json");
    }

    [Fact(DisplayName = "RFC1945-RT-H02: Content-Length header preserved")]
    public void Should_PreserveContentLengthHeader_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "Content-Length: 13", "Hello, World!");

        decoder.TryDecode(data, out var response);

        Assert.NotNull(response);
        Assert.Equal(13, response.Content.Headers.ContentLength);
    }

    [Fact(DisplayName = "RFC1945-RT-H03: Custom X-Custom header preserved")]
    public void Should_PreserveCustomHeader_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "X-Custom-Header: CustomValue\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.Contains("X-Custom-Header"));
        Assert.Equal("CustomValue", response.Headers.GetValues("X-Custom-Header").First());
    }

    [Fact(DisplayName = "RFC1945-RT-H04: Location header preserved in redirect")]
    public void Should_PreserveLocationHeader_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 301 Moved Permanently",
            "Location: http://example.com/new-location\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.Contains("Location"));
        Assert.Equal("http://example.com/new-location",
            response.Headers.GetValues("Location").First());
    }

    [Fact(DisplayName = "RFC1945-RT-H05: Multiple custom headers preserved")]
    public void Should_PreserveMultipleCustomHeaders_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
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

    [Fact(DisplayName = "RFC1945-RT-H06: Server header preserved")]
    public void Should_PreserveServerHeader_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "Server: TestServer/1.0\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.Contains("Server"));
        Assert.Equal("TestServer/1.0", response.Headers.GetValues("Server").First());
    }

    [Fact(DisplayName = "RFC1945-RT-H07: Date header preserved")]
    public void Should_PreserveDateHeader_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var dateValue = "Thu, 06 Mar 2026 12:00:00 GMT";
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            $"Date: {dateValue}\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.Contains("Date"));
        Assert.Equal(dateValue, response.Headers.GetValues("Date").First());
    }

    [Fact(DisplayName = "RFC1945-RT-H08: Header values with special characters preserved")]
    public void Should_PreserveHeaderWithSpecialChars_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "X-Data: value-with-dash_and_underscore\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal("value-with-dash_and_underscore",
            response!.Headers.GetValues("X-Data").First());
    }
}
