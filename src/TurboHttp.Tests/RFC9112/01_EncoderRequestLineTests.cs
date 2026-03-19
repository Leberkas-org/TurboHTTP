using System.Buffers;
using System.Text;
using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.Tests.RFC9112;

/// <summary>
/// Tests HTTP/1.1 request-line serialization per RFC 9112 §3.
/// Verifies that method, request-target, and HTTP-version are correctly encoded.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http11Encoder"/>.
/// RFC 9112 §3: Request-Line — Method SP Request-Target SP HTTP-Version CRLF.
/// </remarks>
public sealed class Http11EncoderRequestLineTests
{
    [Fact]
    public void Should_ProduceCorrectRequestLine_When_GetRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/index.html");
        var result = Encode(request);
        Assert.StartsWith("GET /index.html HTTP/1.1\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-3-RL-001: Request-line uses HTTP/1.1")]
    public void Should_UseHttp11Version_When_EncodingRequestLine()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.Contains("GET / HTTP/1.1\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-3-RL-002: Lowercase method rejected by HTTP/1.1 encoder")]
    public void Should_Reject_When_LowercaseMethod()
    {
        var request = new HttpRequestMessage(new HttpMethod("get"), "https://example.com/");
        var buffer = new Memory<byte>(new byte[4096]);
        Assert.Throws<ArgumentException>(() =>
        {
            var span = buffer.Span;
            Http11Encoder.Encode(request, ref span);
        });
    }

    [Fact(DisplayName = "RFC9112-3-RL-003: Every request-line ends with CRLF")]
    public void Should_EndWithCRLF_When_EncodingRequestLine()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/test");
        var result = Encode(request);
        Assert.Contains("GET /test HTTP/1.1\r\n", result);
    }

    [Theory(DisplayName = "RFC9112-3-RL-004: All HTTP methods produce correct request-line [{method}]")]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    [InlineData("CONNECT")]
    public void Should_ProduceCorrectRequestLine_When_HttpMethod(string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "https://example.com/resource");
        var result = Encode(request);
        Assert.StartsWith($"{method} /resource HTTP/1.1\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-3-RL-005: OPTIONS * HTTP/1.1 encoded correctly")]
    public void Should_EncodeOptionsStar_When_AsteriskTarget()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "https://example.com/*");
        var result = Encode(request);
        Assert.Contains("OPTIONS * HTTP/1.1\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-3-RL-006: Absolute-URI preserved for proxy request")]
    public void Should_PreserveAbsoluteUri_When_ProxyRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com:8443/path?query=value");
        var result = EncodeAbsolute(request);
        Assert.Contains("GET https://example.com:8443/path?query=value HTTP/1.1\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-3-RL-007: Missing path normalized to /")]
    public void Should_NormalizeToSlash_When_MissingPath()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        var result = Encode(request);
        Assert.Contains("GET / HTTP/1.1\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-3-RL-008: Query string preserved verbatim")]
    public void Should_PreserveQueryString_When_Present()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/search?q=hello+world&lang=en");
        var result = Encode(request);
        Assert.Contains("GET /search?q=hello+world&lang=en HTTP/1.1\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-3-RL-009: Fragment stripped from request-target")]
    public void Should_StripFragment_When_Present()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/page#section");
        var result = Encode(request);
        Assert.Contains("GET /page HTTP/1.1\r\n", result);
        Assert.DoesNotContain("#section", result);
    }

    [Fact(DisplayName = "RFC9112-3-RL-010: Existing percent-encoding not re-encoded")]
    public void Should_PreservePercentEncoding_When_AlreadyEncoded()
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