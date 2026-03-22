using System.Buffers;
using System.Text;
using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.Tests.RFC9112;

/// <summary>
/// Tests legacy encoder compatibility behaviors per RFC 9112.
/// Verifies backward-compatible encoding scenarios and obsolete header handling.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http11Encoder"/>.
/// RFC 9112: Legacy compatibility — encoders must interoperate with older HTTP/1.x agents.
/// </remarks>
public sealed class Http11EncoderLegacyTests
{
    [Fact(DisplayName = "RFC9112-3-LG-001: GET produces correct request-line")]
    public void Should_ProduceCorrectRequestLine_When_GetRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/index.html");
        var result = Encode(request);
        Assert.StartsWith("GET /index.html HTTP/1.1\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-3-LG-002: Query string encoded in request-target")]
    public void Should_EncodeQueryString_When_GetWithQueryParams()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/search?q=hello+world&lang=de");
        var result = Encode(request);
        Assert.Contains("/search?q=hello+world&lang=de", result);
    }

    [Fact(DisplayName = "RFC9112-6-LG-003: POST JSON sets Content-Type and Content-Length")]
    public void Should_SetContentTypeAndLength_When_PostJsonBody()
    {
        const string json = """{"name":"test"}""";
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/users")
        {
            Content = content
        };
        var result = Encode(request);

        Assert.Contains("POST /users HTTP/1.1\r\n", result);
        Assert.Contains("Content-Type: application/json", result);
        Assert.Contains($"Content-Length: {Encoding.UTF8.GetByteCount(json)}", result);
    }

    [Fact(DisplayName = "RFC9112-6-LG-004: Body placed after blank line separator")]
    public void Should_PlaceBodyAfterBlankLine_When_PostJsonBody()
    {
        const string json = """{"x":1}""";
        var content = new StringContent(json);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/data")
        {
            Content = content
        };
        var result = Encode(request);

        var separatorIdx = result.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(separatorIdx > 0);
        Assert.Equal(json, result[(separatorIdx + 4)..]);
    }

    [Fact(DisplayName = "RFC9112-6-LG-005: Bodyless GET ends with blank line")]
    public void Should_EndWithBlankLine_When_GetRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.EndsWith("\r\n\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-5-LG-006: Authorization header encoded")]
    public void Should_SetAuthorizationHeader_When_BearerToken()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/protected")
        {
            Headers = { { "Authorization", "Bearer my-secret-token" } }
        };
        var result = Encode(request);
        Assert.Contains("Authorization: Bearer my-secret-token\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-5.4-LG-007: Default port 80 omitted from Host")]
    public void Should_OmitPort_When_HttpPort80()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:80/");
        var result = Encode(request);
        Assert.Contains("Host: example.com\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-5.4-LG-008: Default port 443 omitted from Host")]
    public void Should_OmitPort_When_HttpsPort443()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/secure");
        var result = Encode(request);
        Assert.Contains("Host: example.com\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-5.4-LG-009: Non-standard port included in Host")]
    public void Should_IncludePort_When_NonStandardPort()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:8080/");
        var result = Encode(request);
        Assert.Contains("Host: example.com:8080\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-9-LG-010: Default Connection: keep-alive")]
    public void Should_DefaultToKeepAlive_When_GetRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.Contains("Connection: keep-alive\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-9-LG-011: Explicit Connection: close preserved")]
    public void Should_PreserveConnectionClose_When_ExplicitlySet()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers = { { "Connection", "close" } }
        };
        var result = Encode(request);
        Assert.Contains("Connection: close\r\n", result);
        Assert.DoesNotContain("Connection: keep-alive", result);
    }

    [Fact(DisplayName = "RFC9112-6-LG-012: ArgumentException when buffer too small for body")]
    public void Should_Throw_When_BufferTooSmallForBody()
    {
        var content = new ByteArrayContent(new byte[3000]);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/data")
        {
            Content = content
        };
        var buffer = new Memory<byte>(new byte[200]);
        Assert.Throws<ArgumentException>(() =>
        {
            var span = buffer.Span;
            Http11Encoder.Encode(request, ref span);
        });
    }

    [Fact(DisplayName = "RFC9112-5-LG-013: ArgumentException when buffer too small for headers")]
    public void Should_Throw_When_BufferTooSmallForHeaders()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var buffer = new Memory<byte>(new byte[1]);
        Assert.Throws<ArgumentException>(() =>
        {
            var span = buffer.Span;
            Http11Encoder.Encode(request, ref span);
        });
    }

    [Fact(DisplayName = "RFC9112-6-LG-014: Body placed after blank line (alt)")]
    public void Should_PlaceBodyAfterBlankLine_When_PostJsonBodyAlt()
    {
        const string json = """{"x":1}""";
        var content = new StringContent(json);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/data")
        {
            Content = content
        };
        var result = Encode(request);

        var separatorIdx = result.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(separatorIdx > 0);
        Assert.Equal(json, result[(separatorIdx + 4)..]);
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