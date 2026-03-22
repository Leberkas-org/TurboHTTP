using System.Buffers;
using System.Text;
using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.Tests.RFC9112;

/// <summary>
/// Tests Connection header encoding per RFC 9112 §9.
/// Verifies keep-alive default, close opt-out, and upgrade negotiation.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http11Encoder"/>.
/// RFC 9112 §9: Connection header — controls per-hop options including keep-alive and close.
/// </remarks>
public sealed class Http11EncoderConnectionTests
{
    [Fact(DisplayName = "RFC9112-9-CN-001: Connection keep-alive default in HTTP/1.1")]
    public void Should_DefaultToKeepAlive_When_Http11()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.Contains("Connection: keep-alive\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-9-CN-002: Connection close encoded when set")]
    public void Should_EncodeConnectionClose_When_ExplicitlySet()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers = { { "Connection", "close" } }
        };
        var result = Encode(request);
        Assert.Contains("Connection: close\r\n", result);
        Assert.DoesNotContain("keep-alive", result);
    }

    [Fact(DisplayName = "RFC9112-9-CN-003: Multiple Connection tokens encoded")]
    public void Should_EncodeMultipleTokens_When_ConnectionUpgrade()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.Connection.Add("upgrade");
        var result = Encode(request);
        Assert.Contains("Connection: upgrade, keep-alive\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-9-CN-004: Connection-specific headers stripped")]
    public void Should_StripConnectionSpecificHeaders_When_Present()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=5");
        request.Headers.TryAddWithoutValidation("Upgrade", "websocket");
        var result = Encode(request);
        Assert.DoesNotContain("Keep-Alive:", result);
        Assert.DoesNotContain("Upgrade:", result);
    }

    [Fact(DisplayName = "RFC9112-9-CN-005: Default Connection header is keep-alive")]
    public void Should_SetKeepAlive_When_DefaultConnectionHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.Contains("Connection: keep-alive\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-9-CN-006: Explicit Connection close preserved")]
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

    // ── RFC 9112 §7.4: TE + Connection Header Auto-Addition ──────────────────

    [Fact(DisplayName = "RFC9112-7.4-TE-001: TE header auto-adds TE to Connection")]
    public void Should_AddTEToConnection_When_TEHeaderPresent()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("TE", "trailers");
        var result = Encode(request);

        // TE header should be written (not stripped)
        Assert.Contains("TE: trailers\r\n", result);
        // "TE" must appear in Connection tokens
        Assert.Contains("TE, keep-alive\r\n", result);
    }

    [Fact(DisplayName = "RFC9112-7.4-TE-002: Connection already has TE — no duplicate")]
    public void Should_NotDuplicate_When_ConnectionAlreadyHasTE()
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

    [Fact(DisplayName = "RFC9112-7.4-TE-003: Chunked excluded from TE field")]
    public void Should_ExcludeChunked_When_TEContainsChunked()
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

    [Fact(DisplayName = "RFC9112-7.4-TE-004: TE with only chunked is omitted entirely")]
    public void Should_OmitTeHeader_When_OnlyChunked()
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
