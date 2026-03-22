using System.Buffers;
using System.Text;
using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.Tests.RFC9112;

/// <summary>
/// Tests mandatory Host header encoding per RFC 9112 §5.4.
/// Verifies that Host is always included and correctly formatted.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http11Encoder"/>.
/// RFC 9112 §5.4: Host header field MUST be sent in all HTTP/1.1 request messages.
/// </remarks>
public sealed class Http11EncoderHostHeaderTests
{
    [Fact(DisplayName = "RFC9112-5.4-HH-001: Host header mandatory in HTTP/1.1")]
    public void Should_IncludeHostHeader_When_AnyRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.Contains("Host: example.com\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-5.4-HH-002: Host header emitted exactly once")]
    public void Should_EmitHostOnce_When_Encoding()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        var count = System.Text.RegularExpressions.Regex.Matches(result, "Host:").Count;
        Assert.Equal(1, count);
    }

    [Fact(DisplayName = "RFC9112-5.4-HH-003: Host with non-standard port includes port")]
    public void Should_IncludePort_When_NonStandardPort()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:8080/");
        var result = Encode(request);
        Assert.Contains("Host: example.com:8080\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-5.4-HH-004: IPv6 host literal bracketed correctly")]
    public void Should_BracketIPv6_When_IPv6Host()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://[::1]:8080/");
        var result = Encode(request);
        Assert.Contains("Host: [::1]:8080\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-5.4-HH-005: Default port 80 omitted from Host header")]
    public void Should_OmitDefaultPort_When_Port80()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:80/");
        var result = Encode(request);
        Assert.Contains("Host: example.com\r\n", result);
        Assert.DoesNotContain("Host: example.com:80", result);
    }

    [Fact(DisplayName = "RFC9112-5.4-HH-006: Default HTTP port 80 omitted from Host")]
    public void Should_OmitPort_When_HttpPort80()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:80/");
        var result = Encode(request);
        Assert.Contains("Host: example.com\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-5.4-HH-007: Default HTTPS port 443 omitted from Host")]
    public void Should_OmitPort_When_HttpsPort443()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/secure");
        var result = Encode(request);
        Assert.Contains("Host: example.com\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-5.4-HH-008: Non-standard port included in Host")]
    public void Should_IncludePortInHost_When_NonStandardPort()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:8080/");
        var result = Encode(request);
        Assert.Contains("Host: example.com:8080\r\n", result);
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