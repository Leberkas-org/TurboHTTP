using System.Text;
using Encoder = TurboHTTP.Protocol.Http10.Encoder;

namespace TurboHTTP.Tests.Http10;

/// <summary>
/// Tests HTTP/1.0 absolute-form URI encoding per RFC 1945 §5.1.2.
/// Verifies that the Encode method correctly formats request URIs
/// in both origin-form (default) and absolute-form (proxy mode).
/// </summary>
[Trait("RFC", "RFC1945-5.1.2")]
public sealed class Http10EncoderAbsoluteFormSpec
{
    private static Span<byte> MakeBuffer(int size = 8192) => new byte[size];

    private static string Encode(HttpRequestMessage request, bool absoluteForm = false)
    {
        var buffer = MakeBuffer();
        var written = Encoder.Encode(request, ref buffer, absoluteForm);
        return Encoding.ASCII.GetString(buffer[..written]);
    }

    private static string ExtractRequestLine(string encoded)
    {
        var lines = encoded.Split("\r\n");
        return lines[0];
    }

    [Fact(Timeout = 5000)]
    public void Encode_should_use_origin_form_by_default()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path?query=value");
        var result = Encode(request, absoluteForm: false);
        var requestLine = ExtractRequestLine(result);

        Assert.Contains("GET /path?query=value HTTP/1.0", requestLine);
    }

    [Fact(Timeout = 5000)]
    public void Encode_should_use_absolute_form_when_requested()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path?query=value");
        var result = Encode(request, absoluteForm: true);
        var requestLine = ExtractRequestLine(result);

        Assert.Contains("GET http://example.com/path?query=value HTTP/1.0", requestLine);
    }

    [Fact(Timeout = 5000)]
    public void Encode_absolute_form_should_include_port_for_non_default()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:8080/path");
        var result = Encode(request, absoluteForm: true);
        var requestLine = ExtractRequestLine(result);

        Assert.Contains("GET http://example.com:8080/path HTTP/1.0", requestLine);
    }

    [Fact(Timeout = 5000)]
    public void Encode_absolute_form_should_strip_userinfo()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://user:password@example.com/path");
        var result = Encode(request, absoluteForm: true);

        Assert.DoesNotContain("user", result);
        Assert.DoesNotContain("password", result);
        Assert.Contains("example.com", result);
    }

    [Fact(Timeout = 5000)]
    public void Encode_origin_form_should_use_slash_when_path_is_empty()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com");
        var result = Encode(request, absoluteForm: false);
        var requestLine = ExtractRequestLine(result);

        Assert.Contains("GET / HTTP/1.0", requestLine);
    }

    [Fact(Timeout = 5000)]
    public void Encode_should_reject_lowercase_method()
    {
        var request = new HttpRequestMessage(new HttpMethod("get"), "http://example.com/");

        var threw = false;
        try
        {
            var buffer = MakeBuffer();
            Encoder.Encode(request, ref buffer);
        }
        catch (ArgumentException)
        {
            threw = true;
        }

        Assert.True(threw);
    }

    [Fact(Timeout = 5000)]
    public void Encode_should_reject_mixed_case_method()
    {
        var request = new HttpRequestMessage(new HttpMethod("Get"), "http://example.com/");

        var threw = false;
        try
        {
            var buffer = MakeBuffer();
            Encoder.Encode(request, ref buffer);
        }
        catch (ArgumentException)
        {
            threw = true;
        }

        Assert.True(threw);
    }

    [Fact(Timeout = 5000)]
    public void Encode_should_accept_uppercase_method()
    {
        var request = new HttpRequestMessage(new HttpMethod("POST"), "http://example.com/");

        var threw = false;
        try
        {
            var buffer = MakeBuffer();
            Encoder.Encode(request, ref buffer);
        }
        catch (ArgumentException)
        {
            threw = false; // Should not throw
        }

        Assert.False(threw);
    }

    [Fact(Timeout = 5000)]
    public void Encode_should_set_content_length_zero_for_post_without_body()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/");
        var result = Encode(request);

        Assert.Contains("Content-Length: 0", result);
    }

    [Fact(Timeout = 5000)]
    public void Encode_should_set_content_length_zero_for_put_without_body()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "http://example.com/");
        var result = Encode(request);

        Assert.Contains("Content-Length: 0", result);
    }

    [Fact(Timeout = 5000)]
    public void Encode_should_set_content_length_zero_for_patch_without_body()
    {
        var request = new HttpRequestMessage(new HttpMethod("PATCH"), "http://example.com/");
        var result = Encode(request);

        Assert.Contains("Content-Length: 0", result);
    }

    [Fact(Timeout = 5000)]
    public void Encode_should_not_set_content_length_for_get_without_body()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var result = Encode(request);

        Assert.DoesNotContain("Content-Length:", result);
    }

    [Fact(Timeout = 5000)]
    public void Encode_should_always_include_connection_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var result = Encode(request);

        Assert.Contains("Connection: Keep-Alive", result);
    }

    [Fact(Timeout = 5000)]
    public void Encode_should_not_duplicate_connection_header_when_user_provided()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("Connection", "Keep-Alive");

        var result = Encode(request);
        var connectionCount = result.Split("Connection:").Length - 1;

        Assert.Equal(1, connectionCount);
    }

    [Fact(Timeout = 5000)]
    public void Encode_should_preserve_user_provided_connection_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("Connection", "close");

        var result = Encode(request);

        Assert.Contains("Connection: close", result);
    }

    [Fact(Timeout = 5000)]
    public void Encode_should_handle_content_with_multiple_headers()
    {
        var content = new StringContent("test body");
        content.Headers.TryAddWithoutValidation("X-Content-Header", "value1");

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = content
        };

        var result = Encode(request);

        Assert.Contains("X-Content-Header: value1", result);
        Assert.Contains("test body", result);
    }

    [Fact(Timeout = 5000)]
    public void Encode_absolute_form_with_https_scheme()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");
        var result = Encode(request, absoluteForm: true);
        var requestLine = ExtractRequestLine(result);

        Assert.Contains("GET https://example.com/path HTTP/1.0", requestLine);
    }

    [Fact(Timeout = 5000)]
    public void Encode_absolute_form_with_https_and_non_default_port()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com:8443/path");
        var result = Encode(request, absoluteForm: true);
        var requestLine = ExtractRequestLine(result);

        Assert.Contains("GET https://example.com:8443/path HTTP/1.0", requestLine);
    }
}
