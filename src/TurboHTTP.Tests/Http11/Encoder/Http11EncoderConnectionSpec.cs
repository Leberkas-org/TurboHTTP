using System.Buffers;
using System.Text;

namespace TurboHTTP.Tests.Http11.Encoder;

public sealed class Http11EncoderConnectionSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11Encoder_should_default_to_keep_alive_when_http11()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.Contains("Connection: keep-alive\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11Encoder_should_encode_connection_close_when_explicitly_set()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers = { { "Connection", "close" } }
        };
        var result = Encode(request);
        Assert.Contains("Connection: close\r\n", result);
        Assert.DoesNotContain("keep-alive", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11Encoder_should_encode_multiple_tokens_when_connection_upgrade()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.Connection.Add("upgrade");
        var result = Encode(request);
        Assert.Contains("Connection: upgrade, keep-alive\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11Encoder_should_strip_connection_specific_headers_when_present()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=5");
        request.Headers.TryAddWithoutValidation("Upgrade", "websocket");
        var result = Encode(request);
        Assert.DoesNotContain("Keep-Alive:", result);
        Assert.DoesNotContain("Upgrade:", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11Encoder_should_set_keep_alive_when_default_connection_header()
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
    [Trait("RFC", "RFC9112-7.4")]
    public void Http11Encoder_should_add_te_to_connection_when_te_header_present()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("TE", "trailers");
        var result = Encode(request);

        // TE header should be written (not stripped)
        Assert.Contains("TE: trailers\r\n", result);
        // "TE" must appear in Connection tokens
        Assert.Contains("TE, keep-alive\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.4")]
    public void Http11Encoder_should_not_duplicate_when_connection_already_has_te()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("TE", "trailers");
        request.Headers.Connection.Add("TE");
        var result = Encode(request);

        // TE header should be written
        Assert.Contains("TE: trailers\r\n", result);
        // Connection should have TE exactly once (plus keep-alive)
        Assert.Contains("Connection: TE, keep-alive\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.4")]
    public void Http11Encoder_should_exclude_chunked_when_te_contains_chunked()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("TE", "trailers, chunked");
        var result = Encode(request);

        // TE header should be written without "chunked"
        Assert.Contains("TE: trailers\r\n", result);
        Assert.DoesNotContain("chunked", result.Replace("Transfer-Encoding", ""));
        // "TE" should be in Connection since "trailers" remains
        Assert.Contains("TE, keep-alive\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.4")]
    public void Http11Encoder_should_omit_te_header_when_only_chunked()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("TE", "chunked");
        var result = Encode(request);

        // TE header should not be written (all values filtered)
        Assert.DoesNotContain("TE:", result);
        // No "TE" in Connection since no TE values remain
        Assert.Contains("Connection: keep-alive\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.4")]
    public void Http11Encoder_should_add_te_to_connection_close_when_te_present()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.Connection.Add("close");
        request.Headers.TryAddWithoutValidation("TE", "trailers");
        var result = Encode(request);

        // Should have "Connection: close, TE" when TE is present and close is set
        Assert.Contains("Connection: close, TE\r\n", result);
        Assert.Contains("TE: trailers\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.4")]
    public void Http11Encoder_should_filter_te_chunked_case_insensitive()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("TE", "CHUNKED");
        var result = Encode(request);

        // TE header should be omitted entirely (case-insensitive match)
        Assert.DoesNotContain("TE:", result);
        // Connection should still have keep-alive
        Assert.Contains("Connection: keep-alive\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.4")]
    public void Http11Encoder_should_preserve_multiple_te_values()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("TE", "trailers, gzip, chunked");
        var result = Encode(request);

        // Should preserve trailers and gzip, filter chunked
        Assert.Contains("TE: trailers, gzip\r\n", result);
        Assert.DoesNotContain(", chunked", result);
        Assert.Contains("TE, keep-alive\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.4")]
    public void Http11Encoder_should_handle_te_with_whitespace()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("TE", "  trailers  ,  chunked  ");
        var result = Encode(request);

        // Should trim and preserve trailers, filter chunked
        Assert.Contains("TE: trailers\r\n", result);
        Assert.Contains("TE, keep-alive\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11Encoder_should_add_custom_connection_value_with_keep_alive()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.Connection.Add("custom-value");
        var result = Encode(request);

        // Should include custom value along with keep-alive
        Assert.Contains("Connection: custom-value, keep-alive\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11Encoder_should_handle_connection_with_multiple_custom_values()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.Connection.Add("custom1");
        request.Headers.Connection.Add("custom2");
        var result = Encode(request);

        // Should include all custom values with keep-alive
        Assert.Contains("Connection: custom1, custom2, keep-alive\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11Encoder_should_exclude_trailers_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("Trailers", "X-Custom");
        var result = Encode(request);

        // Trailers header should be stripped per RFC 9112
        Assert.DoesNotContain("Trailers:", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11Encoder_should_exclude_proxy_connection_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("Proxy-Connection", "keep-alive");
        var result = Encode(request);

        // Proxy-Connection is connection-specific and should be stripped
        Assert.DoesNotContain("Proxy-Connection:", result);
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
