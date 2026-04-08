using System.Buffers;
using System.Text;
using TurboHTTP.Protocol.Http11;

namespace TurboHTTP.Tests.Http11.Encoder;

/// <summary>
/// Tests HTTP/1.1 request-line serialization per RFC 9112 §3.
/// Verifies that method, request-target, and HTTP-version are correctly encoded.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http11Encoder"/>.
/// RFC 9112 §3: Request-Line — Method SP Request-Target SP HTTP-Version CRLF.
/// </remarks>
public sealed class Http11EncoderRequestLineSpec
{
    [Fact]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_produce_correct_request_line_when_get_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/index.html");
        var result = Encode(request);
        Assert.StartsWith("GET /index.html HTTP/1.1\r\n", result);
    }

    [Fact]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_use_http11_version_when_encoding_request_line()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.Contains("GET / HTTP/1.1\r\n", result);
    }

    [Fact]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_reject_when_lowercase_method()
    {
        var request = new HttpRequestMessage(new HttpMethod("get"), "https://example.com/");
        var buffer = new Memory<byte>(new byte[4096]);
        Assert.Throws<ArgumentException>(() =>
        {
            var span = buffer.Span;
            Http11Encoder.Encode(request, ref span);
        });
    }

    [Fact]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_end_with_crlf_when_encoding_request_line()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/test");
        var result = Encode(request);
        Assert.Contains("GET /test HTTP/1.1\r\n", result);
    }

    [Theory]
    [Trait("RFC", "RFC9112-3")]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    public void Http11Encoder_should_produce_correct_request_line_when_http_method(string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "https://example.com/resource");
        var result = Encode(request);
        Assert.StartsWith($"{method} /resource HTTP/1.1\r\n", result);
    }

    [Fact]
    [Trait("RFC", "RFC9110-9.3.6")]
    public void Http11Encoder_should_include_port_443_when_connect_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Connect, "https://example.com/");
        var result = Encode(request);
        Assert.StartsWith("CONNECT example.com:443 HTTP/1.1\r\n", result);
    }

    [Fact]
    [Trait("RFC", "RFC9110-9.3.6")]
    public void Http11Encoder_should_include_port_80_when_connect_http()
    {
        var request = new HttpRequestMessage(HttpMethod.Connect, "http://example.com/");
        var result = Encode(request);
        Assert.StartsWith("CONNECT example.com:80 HTTP/1.1\r\n", result);
    }

    [Fact]
    [Trait("RFC", "RFC9110-9.3.6")]
    public void Http11Encoder_should_include_port_when_connect_custom_port()
    {
        var request = new HttpRequestMessage(HttpMethod.Connect, "http://example.com:8080/");
        var result = Encode(request);
        Assert.StartsWith("CONNECT example.com:8080 HTTP/1.1\r\n", result);
    }

    [Fact]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_encode_options_star_when_asterisk_target()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "https://example.com/*");
        var result = Encode(request);
        Assert.Contains("OPTIONS * HTTP/1.1\r\n", result);
    }

    [Fact]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_preserve_absolute_uri_when_proxy_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com:8443/path?query=value");
        var result = EncodeAbsolute(request);
        Assert.Contains("GET https://example.com:8443/path?query=value HTTP/1.1\r\n", result);
    }

    [Fact]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_normalize_to_slash_when_missing_path()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        var result = Encode(request);
        Assert.Contains("GET / HTTP/1.1\r\n", result);
    }

    [Fact]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_preserve_query_string_when_present()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/search?q=hello+world&lang=en");
        var result = Encode(request);
        Assert.Contains("GET /search?q=hello+world&lang=en HTTP/1.1\r\n", result);
    }

    [Fact]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_strip_fragment_when_present()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/page#section");
        var result = Encode(request);
        Assert.Contains("GET /page HTTP/1.1\r\n", result);
        Assert.DoesNotContain("#section", result);
    }

    [Fact]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_preserve_percent_encoding_when_already_encoded()
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
