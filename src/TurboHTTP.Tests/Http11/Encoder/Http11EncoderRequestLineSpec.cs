using System.Buffers;
using System.Text;

namespace TurboHTTP.Tests.Http11.Encoder;

public sealed class Http11EncoderRequestLineSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_produce_correct_request_line_when_get_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/index.html");
        var result = Encode(request);
        Assert.StartsWith("GET /index.html HTTP/1.1\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_use_http11_version_when_encoding_request_line()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.Contains("GET / HTTP/1.1\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_reject_when_lowercase_method()
    {
        var request = new HttpRequestMessage(new HttpMethod("get"), "https://example.com/");
        var buffer = new Memory<byte>(new byte[4096]);
        Assert.Throws<ArgumentException>(() =>
        {
            var span = buffer.Span;
            Protocol.Http11.Encoder.Encode(request, ref span);
        });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_end_with_crlf_when_encoding_request_line()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/test");
        var result = Encode(request);
        Assert.Contains("GET /test HTTP/1.1\r\n", result);
    }

    [Theory(Timeout = 5000)]
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.3.6")]
    public void Http11Encoder_should_include_port_443_when_connect_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Connect, "https://example.com/");
        var result = Encode(request);
        Assert.StartsWith("CONNECT example.com:443 HTTP/1.1\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.3.6")]
    public void Http11Encoder_should_include_port_80_when_connect_http()
    {
        var request = new HttpRequestMessage(HttpMethod.Connect, "http://example.com/");
        var result = Encode(request);
        Assert.StartsWith("CONNECT example.com:80 HTTP/1.1\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.3.6")]
    public void Http11Encoder_should_include_port_when_connect_custom_port()
    {
        var request = new HttpRequestMessage(HttpMethod.Connect, "http://example.com:8080/");
        var result = Encode(request);
        Assert.StartsWith("CONNECT example.com:8080 HTTP/1.1\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_encode_options_star_when_asterisk_target()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "https://example.com/*");
        var result = Encode(request);
        Assert.Contains("OPTIONS * HTTP/1.1\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_preserve_absolute_uri_when_proxy_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com:8443/path?query=value");
        var result = EncodeAbsolute(request);
        Assert.Contains("GET https://example.com:8443/path?query=value HTTP/1.1\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_normalize_to_slash_when_missing_path()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        var result = Encode(request);
        Assert.Contains("GET / HTTP/1.1\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_preserve_query_string_when_present()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/search?q=hello+world&lang=en");
        var result = Encode(request);
        Assert.Contains("GET /search?q=hello+world&lang=en HTTP/1.1\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_strip_fragment_when_present()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/page#section");
        var result = Encode(request);
        Assert.Contains("GET /page HTTP/1.1\r\n", result);
        Assert.DoesNotContain("#section", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_preserve_percent_encoding_when_already_encoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path%20with%20spaces");
        var result = Encode(request);
        Assert.Contains("GET /path%20with%20spaces HTTP/1.1\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.3.6")]
    public void Http11Encoder_should_use_authority_form_for_connect_method()
    {
        var request = new HttpRequestMessage(HttpMethod.Connect, "http://proxy.example.com:8080/");
        var result = Encode(request);
        Assert.StartsWith("CONNECT proxy.example.com:8080 HTTP/1.1\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_use_absolute_form_for_proxy_requests()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:8080/path?query=1");
        var result = EncodeAbsolute(request);
        Assert.StartsWith("GET http://example.com:8080/path?query=1 HTTP/1.1\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_strip_userinfo_in_absolute_form()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://user:password@example.com/path");
        var result = EncodeAbsolute(request);
        Assert.DoesNotContain("user", result);
        Assert.DoesNotContain("password", result);
        Assert.Contains("example.com", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_handle_ipv6_address_in_host_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://[::1]/path");
        var result = Encode(request);
        Assert.Contains("Host: [::1]\r\n", result);
        Assert.StartsWith("GET /path HTTP/1.1\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.3.6")]
    public void Http11Encoder_should_handle_ipv6_in_connect_authority()
    {
        var request = new HttpRequestMessage(HttpMethod.Connect, "http://[2001:db8::1]:443/");
        var result = Encode(request);
        Assert.StartsWith("CONNECT [2001:db8::1]:443 HTTP/1.1\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_preserve_multiple_query_params()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/search?q=hello&sort=asc&limit=10");
        var result = Encode(request);
        Assert.Contains("GET /search?q=hello&sort=asc&limit=10 HTTP/1.1\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_accept_mixed_case_custom_method()
    {
        var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), "https://example.com/");
        var result = Encode(request);
        Assert.Contains("PROPFIND / HTTP/1.1\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_reject_method_with_lowercase_letters()
    {
        var request = new HttpRequestMessage(new HttpMethod("Post"), "https://example.com/");
        var buffer = new Memory<byte>(new byte[4096]);
        Assert.Throws<ArgumentException>(() =>
        {
            var span = buffer.Span;
            Protocol.Http11.Encoder.Encode(request, ref span);
        });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void Http11Encoder_should_handle_options_with_absolute_path()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "https://example.com/api");
        var result = Encode(request);
        Assert.Contains("OPTIONS /api HTTP/1.1\r\n", result);
    }

    private static string Encode(HttpRequestMessage request)
    {
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var span = buffer.Span;
        var written = Protocol.Http11.Encoder.Encode(request, ref span);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }

    private static string EncodeAbsolute(HttpRequestMessage request)
    {
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var span = buffer.Span;
        var written = Protocol.Http11.Encoder.Encode(request, ref span, absoluteForm: true);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }
}
