using System.Buffers;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9112;

public sealed class Http11EncoderRequestLineTests
{
    [Fact]
    public void Get_ProducesCorrectRequestLine()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/index.html");
        var result = Encode(request);
        Assert.StartsWith("GET /index.html HTTP/1.1\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-4: Request-line uses HTTP/1.1")]
    public void Test_9112_RequestLine_UsesHttp11()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.Contains("GET / HTTP/1.1\r\n", result);
    }

    [Fact(DisplayName = "RFC7230-3.1.1: Lowercase method rejected by HTTP/1.1 encoder")]
    public void Test_Lowercase_Method_Rejected()
    {
        var request = new HttpRequestMessage(new HttpMethod("get"), "https://example.com/");
        var buffer = new Memory<byte>(new byte[4096]);
        Assert.Throws<ArgumentException>(() =>
        {
            var span = buffer.Span;
            Http11Encoder.Encode(request, ref span);
        });
    }

    [Fact(DisplayName = "RFC7230-3.1.1: Every request-line ends with CRLF")]
    public void Test_RequestLine_Ends_With_CRLF()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/test");
        var result = Encode(request);
        Assert.Contains("GET /test HTTP/1.1\r\n", result);
    }

    [Theory(DisplayName = "RFC9112-4: All HTTP methods produce correct request-line [{method}]")]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    [InlineData("CONNECT")]
    public void Test_All_Methods(string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "https://example.com/resource");
        var result = Encode(request);
        Assert.StartsWith($"{method} /resource HTTP/1.1\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-4: OPTIONS * HTTP/1.1 encoded correctly")]
    public void Test_OPTIONS_Star()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "https://example.com/*");
        var result = Encode(request);
        Assert.Contains("OPTIONS * HTTP/1.1\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-4: Absolute-URI preserved for proxy request")]
    public void Test_Absolute_URI_For_Proxy()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com:8443/path?query=value");
        var result = EncodeAbsolute(request);
        Assert.Contains("GET https://example.com:8443/path?query=value HTTP/1.1\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-4: Missing path normalized to /")]
    public void Test_Missing_Path_Normalized()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        var result = Encode(request);
        Assert.Contains("GET / HTTP/1.1\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-4: Query string preserved verbatim")]
    public void Test_Query_String_Preserved()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/search?q=hello+world&lang=en");
        var result = Encode(request);
        Assert.Contains("GET /search?q=hello+world&lang=en HTTP/1.1\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-4: Fragment stripped from request-target")]
    public void Test_Fragment_Stripped()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/page#section");
        var result = Encode(request);
        Assert.Contains("GET /page HTTP/1.1\r\n", result);
        Assert.DoesNotContain("#section", result);
    }

    [Fact(DisplayName = "RFC9112-4: Existing percent-encoding not re-encoded")]
    public void Test_Percent_Encoding_Not_Re_Encoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path%20with%20spaces");
        var result = Encode(request);
        Assert.Contains("GET /path%20with%20spaces HTTP/1.1\r\n", result);
    }

    private static string Encode(HttpRequestMessage request)
    {
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var span = buffer.Span;
        var written = Http11Encoder.Encode(request, ref span);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }

    private static string EncodeAbsolute(HttpRequestMessage request)
    {
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var span = buffer.Span;
        var written = Http11Encoder.Encode(request, ref span, absoluteForm: true);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }
}