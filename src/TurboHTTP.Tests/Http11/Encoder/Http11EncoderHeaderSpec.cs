using System.Buffers;
using System.Text;

namespace TurboHTTP.Tests.Http11.Encoder;

public sealed class Http11EncoderHeaderSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Encoder_should_format_header_when_custom_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers = { { "X-Custom", "test-value" } }
        };
        var result = Encode(request);
        Assert.Contains("X-Custom: test-value\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Encoder_should_omit_spurious_whitespace_when_encoding_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers = { { "X-Test", "value" } }
        };
        var result = Encode(request);
        Assert.Contains("X-Test: value\r\n", result);
        Assert.DoesNotContain("X-Test:  value", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Encoder_should_preserve_casing_when_encoding_header_name()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("X-Custom-Header", "value");
        var result = Encode(request);
        Assert.Contains("X-Custom-Header: value\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Encoder_should_throw_when_nul_byte_in_header_value()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("X-Bad", "value\0bad");
        var buffer = new Memory<byte>(new byte[4096]);

        Assert.Throws<ArgumentException>(() =>
        {
            var span = buffer.Span;
            Protocol.Http11.Encoder.Encode(request, ref span);
        });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Encoder_should_preserve_charset_parameter_when_content_type()
    {
        var content = new StringContent("test", Encoding.UTF8, "text/html");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/")
        {
            Content = content
        };
        var result = Encode(request);
        Assert.Contains("Content-Type: text/html; charset=utf-8\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Encoder_should_include_all_headers_when_multiple_custom_headers()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Encoder_should_encode_accept_encoding_when_gzip_deflate()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
        var result = Encode(request);
        Assert.Contains("Accept-Encoding: gzip, deflate\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Encoder_should_preserve_authorization_when_bearer_token()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers = { { "Authorization", "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9" } }
        };
        var result = Encode(request);
        Assert.Contains("Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9\r\n", result);
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
    [Trait("RFC", "RFC9112-2.2")]
    public void Http11Encoder_should_not_contain_bare_cr_when_encoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/path?q=1")
        {
            Content = new StringContent("body", Encoding.UTF8, "text/plain"),
            Headers =
            {
                { "X-Custom", "value" },
                { "Accept", "application/json" },
                { "Authorization", "Bearer token123" }
            }
        };

        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var span = buffer.Span;
        var written = Protocol.Http11.Encoder.Encode(request, ref span);
        var bytes = buffer.Span[..written];

        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'\r')
            {
                Assert.True(i + 1 < bytes.Length && bytes[i + 1] == (byte)'\n',
                    $"Bare CR found at byte offset {i} — CR must always be followed by LF (RFC 9112 §2.2)");
            }
        }
    }

    private static string Encode(HttpRequestMessage request)
    {
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var span = buffer.Span;
        var written = Protocol.Http11.Encoder.Encode(request, ref span);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }
}
