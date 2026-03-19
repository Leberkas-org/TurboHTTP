using System.Buffers;
using System.Text;
using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.Tests.RFC9112;

public sealed class Http11EncoderHeaderTests
{
    [Fact(DisplayName = "RFC9112-5-HD-001: Header field format is Name: SP value CRLF")]
    public void Should_FormatHeader_When_CustomHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers = { { "X-Custom", "test-value" } }
        };
        var result = Encode(request);
        Assert.Contains("X-Custom: test-value\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-5-HD-002: No spurious whitespace added to header values")]
    public void Should_OmitSpuriousWhitespace_When_EncodingHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers = { { "X-Test", "value" } }
        };
        var result = Encode(request);
        Assert.Contains("X-Test: value\r\n", result);
        Assert.DoesNotContain("X-Test:  value", result);
    }

    [Fact(DisplayName = "RFC9112-5-HD-003: Header name casing preserved in output")]
    public void Should_PreserveCasing_When_EncodingHeaderName()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("X-Custom-Header", "value");
        var result = Encode(request);
        Assert.Contains("X-Custom-Header: value\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-5-HD-004: NUL byte in header value throws exception")]
    public void Should_Throw_When_NulByteInHeaderValue()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("X-Bad", "value\0bad");
        var buffer = new Memory<byte>(new byte[4096]);

        Assert.Throws<ArgumentException>(() =>
        {
            var span = buffer.Span;
            Http11Encoder.Encode(request, ref span);
        });
    }

    [Fact(DisplayName = "RFC9112-5-HD-005: Content-Type with charset parameter preserved")]
    public void Should_PreserveCharsetParameter_When_ContentType()
    {
        var content = new StringContent("test", Encoding.UTF8, "text/html");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/")
        {
            Content = content
        };
        var result = Encode(request);
        Assert.Contains("Content-Type: text/html; charset=utf-8\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-5-HD-006: All custom headers appear in output")]
    public void Should_IncludeAllHeaders_When_MultipleCustomHeaders()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers =
            {
                { "X-First", "value1" },
                { "X-Second", "value2" },
                { "X-Third", "value3" }
            }
        };
        var result = Encode(request);
        Assert.Contains("X-First: value1\r\n", result);
        Assert.Contains("X-Second: value2\r\n", result);
        Assert.Contains("X-Third: value3\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-5-HD-007: Accept-Encoding gzip,deflate encoded")]
    public void Should_EncodeAcceptEncoding_When_GzipDeflate()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
        var result = Encode(request);
        Assert.Contains("Accept-Encoding: gzip, deflate\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-5-HD-008: Authorization header preserved verbatim")]
    public void Should_PreserveAuthorization_When_BearerToken()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers = { { "Authorization", "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9" } }
        };
        var result = Encode(request);
        Assert.Contains("Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9\r\n", result);
    }

    [Fact]
    public void Should_SetAuthorizationHeader_When_BearerToken()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/protected")
        {
            Headers = { { "Authorization", "Bearer my-secret-token" } }
        };
        var result = Encode(request);
        Assert.Contains("Authorization: Bearer my-secret-token\r\n", result);
    }

    private static string Encode(HttpRequestMessage request)
    {
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var span = buffer.Span;
        var written = Http11Encoder.Encode(request, ref span);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }
}