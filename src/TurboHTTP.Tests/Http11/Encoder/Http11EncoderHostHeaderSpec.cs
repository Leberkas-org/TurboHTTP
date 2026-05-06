using System.Buffers;
using System.Text;

namespace TurboHTTP.Tests.Http11.Encoder;

public sealed class Http11EncoderHostHeaderSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5.4")]
    public void Http11Encoder_should_include_host_header_when_any_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.Contains("Host: example.com\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5.4")]
    public void Http11Encoder_should_emit_host_once_when_encoding()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        var count = System.Text.RegularExpressions.Regex.Matches(result, "Host:").Count;
        Assert.Equal(1, count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5.4")]
    public void Http11Encoder_should_include_port_when_non_standard_port()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:8080/");
        var result = Encode(request);
        Assert.Contains("Host: example.com:8080\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5.4")]
    public void Http11Encoder_should_bracket_ipv6_when_ipv6_host()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://[::1]:8080/");
        var result = Encode(request);
        Assert.Contains("Host: [::1]:8080\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5.4")]
    public void Http11Encoder_should_omit_default_port_when_port_80()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:80/");
        var result = Encode(request);
        Assert.Contains("Host: example.com\r\n", result);
        Assert.DoesNotContain("Host: example.com:80", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5.4")]
    public void Http11Encoder_should_omit_port_when_http_port_80()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:80/");
        var result = Encode(request);
        Assert.Contains("Host: example.com\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5.4")]
    public void Http11Encoder_should_omit_port_when_https_port_443()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/secure");
        var result = Encode(request);
        Assert.Contains("Host: example.com\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5.4")]
    public void Http11Encoder_should_include_port_in_host_when_non_standard_port()
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
        var written = TurboHTTP.Protocol.Http11.Encoder.Encode(request, ref span);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }
}
