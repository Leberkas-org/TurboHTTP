using System.Buffers;
using System.Text;
using TurboHttp.Protocol.Http11;

namespace TurboHttp.Tests.Http11.Encoder;

/// <summary>
/// Tests Connection header encoding per RFC 9112 §9.
/// Verifies keep-alive default, close opt-out, and upgrade negotiation.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http11Encoder"/>.
/// RFC 9112 §9: Connection header — controls per-hop options including keep-alive and close.
/// </remarks>
public sealed class Http11EncoderConnectionSpec
{
    [Fact]
    [Trait("RFC", "RFC9112-9")]
    public void Http11Encoder_should_default_to_keep_alive_when_http11()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.Contains("Connection: keep-alive\r\n", result);
    }

    [Fact]
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

    [Fact]
    [Trait("RFC", "RFC9112-9")]
    public void Http11Encoder_should_encode_multiple_tokens_when_connection_upgrade()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.Connection.Add("upgrade");
        var result = Encode(request);
        Assert.Contains("Connection: upgrade, keep-alive\r\n", result);
    }

    [Fact]
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

    [Fact]
    [Trait("RFC", "RFC9112-9")]
    public void Http11Encoder_should_set_keep_alive_when_default_connection_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.Contains("Connection: keep-alive\r\n", result);
    }

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    private static string Encode(HttpRequestMessage request)
    {
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var span = buffer.Span;
        var written = Http11Encoder.Encode(request, ref span);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }
}
