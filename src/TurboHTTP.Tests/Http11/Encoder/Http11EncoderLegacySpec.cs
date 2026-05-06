using System.Buffers;
using System.Text;

namespace TurboHTTP.Tests.Http11.Encoder;

public sealed class Http11EncoderLegacySpec
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
    public void Http11Encoder_should_encode_query_string_when_get_with_query_params()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/search?q=hello+world&lang=de");
        var result = Encode(request);
        Assert.Contains("/search?q=hello+world&lang=de", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void Http11Encoder_should_set_content_type_and_length_when_post_json_body()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void Http11Encoder_should_place_body_after_blank_line_when_post_json_body()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void Http11Encoder_should_end_with_blank_line_when_get_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.EndsWith("\r\n\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Encoder_should_set_authorization_header_when_bearer_token()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/protected")
        {
            Headers = { { "Authorization", "Bearer my-secret-token" } }
        };
        var result = Encode(request);
        Assert.Contains("Authorization: Bearer my-secret-token\r\n", result);
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
    public void Http11Encoder_should_include_port_when_non_standard_port()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:8080/");
        var result = Encode(request);
        Assert.Contains("Host: example.com:8080\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11Encoder_should_default_to_keep_alive_when_get_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.Contains("Connection: keep-alive\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11Encoder_should_preserve_connection_close_when_explicitly_set()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers = { { "Connection", "close" } }
        };
        var result = Encode(request);
        Assert.Contains("Connection: close\r\n", result);
        Assert.DoesNotContain("Connection: keep-alive", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void Http11Encoder_should_throw_when_buffer_too_small_for_body()
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
            TurboHTTP.Protocol.Http11.Encoder.Encode(request, ref span);
        });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Encoder_should_throw_when_buffer_too_small_for_headers()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var buffer = new Memory<byte>(new byte[1]);
        Assert.Throws<ArgumentException>(() =>
        {
            var span = buffer.Span;
            TurboHTTP.Protocol.Http11.Encoder.Encode(request, ref span);
        });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void Http11Encoder_should_place_body_after_blank_line_when_post_json_body_alt()
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
        var written = TurboHTTP.Protocol.Http11.Encoder.Encode(request, ref span);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }
}
