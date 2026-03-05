using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC1945;

/// <summary>
/// RFC 1945 §5.1 — Request-Line encoding tests.
/// Verifies HTTP/1.0 request-line format: METHOD request-target HTTP-version CRLF
/// </summary>
public sealed class Http10EncoderRequestLineTests
{
    private static Memory<byte> MakeBuffer(int size = 8192) => new byte[size];

    private static string Encode(HttpRequestMessage request, int bufferSize = 8192)
    {
        var buffer = MakeBuffer(bufferSize);
        var written = Http10Encoder.Encode(request, ref buffer);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }

    private static (string requestLine, string[] headerLines, byte[] body) ParseRaw(HttpRequestMessage request,
        int bufferSize = 8192)
    {
        var buffer = MakeBuffer(bufferSize);
        var written = Http10Encoder.Encode(request, ref buffer);
        var raw = Encoding.ASCII.GetString(buffer.Span[..written]);

        var separatorIndex = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var headerSection = raw[..separatorIndex];
        var bodyString = raw[(separatorIndex + 4)..];

        var lines = headerSection.Split("\r\n");
        var requestLine = lines[0];
        var headerLines = lines[1..];

        return (requestLine, headerLines, Encoding.ASCII.GetBytes(bodyString));
    }

    [Fact]
    public void RequestLine_Get_IsCorrectlyFormatted()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Equal("GET /path HTTP/1.0", requestLine);
    }

    [Fact]
    public void RequestLine_Head_IsCorrectlyFormatted()
    {
        var request = new HttpRequestMessage(HttpMethod.Head, "http://example.com/resource");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Equal("HEAD /resource HTTP/1.0", requestLine);
    }

    [Fact]
    public void RequestLine_Post_IsCorrectlyFormatted()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Content = new StringContent("data")
        };
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Equal("POST /submit HTTP/1.0", requestLine);
    }

    [Fact]
    public void RequestLine_ContainsExactlyOneSpaceBetweenParts()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var (requestLine, _, _) = ParseRaw(request);

        var parts = requestLine.Split(' ');
        Assert.Equal(3, parts.Length);
    }

    [Fact]
    public void RequestLine_ProtocolVersionIsHttp10()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.EndsWith("HTTP/1.0", requestLine);
    }

    [Fact]
    public void RequestLine_EndsWithCrLf()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var raw = Encode(request);

        Assert.StartsWith("GET / HTTP/1.0\r\n", raw);
    }

    [Fact]
    public void RequestLine_WithQueryString_IncludesQueryInUri()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/search?q=hello&page=2");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Equal("GET /search?q=hello&page=2 HTTP/1.0", requestLine);
    }

    [Fact]
    public void RequestLine_RootPath_IsForwardSlash()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Equal("GET / HTTP/1.0", requestLine);
    }

    [Fact]
    public void RequestLine_DeepPath_IsPreserved()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a/b/c/d");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Equal("GET /a/b/c/d HTTP/1.0", requestLine);
    }

    [Fact(DisplayName = "1945-enc-001: Request-line uses HTTP/1.0")]
    public void Should_UseHttp10Version_When_EncodingRequestLine()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Equal("GET /path HTTP/1.0", requestLine);
    }

    [Fact(DisplayName = "1945-enc-007: Path-and-query preserved in request-line")]
    public void Should_PreservePathAndQuery_When_EncodingRequestLine()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api/data?key=val&x=1");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Equal("GET /api/data?key=val&x=1 HTTP/1.0", requestLine);
    }

    [Fact(DisplayName = "1945-5.1-004: Lowercase method rejected by HTTP/1.0 encoder")]
    public void Should_ThrowArgumentException_When_MethodIsLowercase()
    {
        var request = new HttpRequestMessage(new HttpMethod("get"), "http://example.com/");
        var buffer = MakeBuffer();

        Assert.Throws<ArgumentException>(() => Http10Encoder.Encode(request, ref buffer));
    }

    [Fact(DisplayName = "1945-5.1-005: Absolute URI encoded in request-line")]
    public void Should_EncodeAbsoluteUri_When_AbsoluteFormRequested()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path?q=1");
        var buffer = MakeBuffer();
        var written = Http10Encoder.Encode(request, ref buffer, absoluteForm: true);
        var raw = Encoding.ASCII.GetString(buffer.Span[..written]);

        Assert.StartsWith("GET http://example.com/path?q=1 HTTP/1.0\r\n", raw);
    }

    [Theory(DisplayName = "enc1-m-001: All HTTP methods produce correct uppercase request-line")]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    public void Should_ProduceCorrectRequestLine_When_UsingHttpMethod(string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "http://example.com/res");
        if (method is "POST" or "PUT" or "PATCH")
        {
            request.Content = new StringContent("body");
        }

        var (requestLine, _, _) = ParseRaw(request);

        Assert.Equal($"{method} /res HTTP/1.0", requestLine);
    }

    [Fact(DisplayName = "enc1-uri-001: Missing path normalized to /")]
    public void Should_NormalizeToSlash_When_PathIsMissing()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Equal("GET / HTTP/1.0", requestLine);
    }

    [Fact(DisplayName = "enc1-uri-002: Query string preserved in request-target")]
    public void Should_PreserveQueryString_When_EncodingRequestTarget()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/?a=1&b=2&c=3");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Contains("?a=1&b=2&c=3", requestLine);
    }

    [Fact(DisplayName = "enc1-uri-003: Percent-encoded chars not double-encoded")]
    public void Should_NotDoubleEncode_When_PathContainsPercentEncoding()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri("http://example.com/path%20with%20spaces"));
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Contains("%20", requestLine);
        Assert.DoesNotContain("%2520", requestLine);
    }

    [Fact(DisplayName = "enc1-uri-004: URI fragment stripped from request-target")]
    public void Should_StripFragment_When_UriContainsFragment()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page#section");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.DoesNotContain("#", requestLine);
        Assert.DoesNotContain("section", requestLine);
    }
}
