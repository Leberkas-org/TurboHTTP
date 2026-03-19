using System.Text;
using TurboHttp.Protocol;
using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.Tests.RFC9112;

/// <summary>
/// Tests chunk-extension parsing per RFC 9112 §7.1.1.
/// Verifies that chunk-ext tokens are ignored and do not interfere with body decoding.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http11Decoder"/>.
/// RFC 9112 §7.1.1: chunk-ext = *( ";" chunk-ext-name [ "=" chunk-ext-val ] ) — MUST be ignored.
/// </remarks>
public sealed class Http11DecoderChunkExtensionTests
{
    private readonly Http11Decoder _decoder = new();

    [Fact(DisplayName = "RFC9112-7-CE-001: No extension — body decoded correctly")]
    public async Task Should_DecodeBody_When_NoExtensionPresent()
    {
        const string chunkedBody = "5\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-7-CE-002: No extension — hex chunk size decoded correctly")]
    public async Task Should_DecodeBody_When_HexChunkSizeAndNoExtension()
    {
        const string chunkedBody = "a\r\n0123456789\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("0123456789", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-7-CE-003: No extension — multiple chunks concatenated")]
    public async Task Should_ConcatenateChunks_When_MultipleChunksAndNoExtension()
    {
        const string chunkedBody = "3\r\nfoo\r\n3\r\nbar\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("foobar", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-7-CE-004: No extension — empty body with terminator only")]
    public async Task Should_DecodeEmptyBody_When_OnlyTerminatorChunk()
    {
        const string chunkedBody = "0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-7-CE-005: No extension — trailer fields preserved")]
    public void Should_PreserveTrailerFields_When_NoExtensionAndTrailerPresent()
    {
        const string chunkedBody = "3\r\nfoo\r\n0\r\nX-Trailer: value\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        _decoder.TryDecode(raw, out var responses);

        Assert.True(responses[0].TrailingHeaders.TryGetValues("X-Trailer", out var values));
        Assert.Equal("value", values.Single());
    }

    [Fact(DisplayName = "RFC9112-7-CE-006: Name-only extension — accepted and body intact")]
    public async Task Should_AcceptExtension_When_NameOnlyNoValue()
    {
        const string chunkedBody = "5;myext\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-7-CE-007: Extension with token value — accepted")]
    public async Task Should_AcceptExtension_When_NameEqualsTokenValue()
    {
        const string chunkedBody = "5;ext=value\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-7-CE-008: Extension with quoted string value — accepted")]
    public async Task Should_AcceptExtension_When_NameEqualsQuotedValue()
    {
        const string chunkedBody = "5;ext=\"quoted\"\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-7-CE-009: Extension with empty quoted value — accepted")]
    public async Task Should_AcceptExtension_When_EmptyQuotedValue()
    {
        const string chunkedBody = "5;ext=\"\"\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-7-CE-010: Quoted value with backslash escape — accepted")]
    public async Task Should_AcceptExtension_When_QuotedValueWithEscape()
    {
        const string chunkedBody = "5;ext=\"a\\\\b\"\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-7-CE-011: BWS (space) before extension name — accepted")]
    public async Task Should_AcceptExtension_When_BWSBeforeName()
    {
        const string chunkedBody = "5; ext=val\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-7-CE-012: BWS (spaces) around equals sign — accepted")]
    public async Task Should_AcceptExtension_When_BWSAroundEqualsSign()
    {
        const string chunkedBody = "5;ext = val\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-7-CE-013: BWS using tab character — accepted")]
    public async Task Should_AcceptExtension_When_BWSIsTabCharacter()
    {
        var chunkedBody = "5;ext\t=\tval\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-7-CE-014: Extension name starting with '!' token char — accepted")]
    public async Task Should_AcceptExtension_When_NameStartsWithExclamation()
    {
        const string chunkedBody = "5;!ext=val\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-7-CE-015: Extension with '#' token char in name — accepted")]
    public async Task Should_AcceptExtension_When_NameContainsHashChar()
    {
        const string chunkedBody = "5;#name\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-7-CE-016: Two name-only extensions — accepted")]
    public async Task Should_AcceptExtensions_When_TwoNameOnlyExtensions()
    {
        const string chunkedBody = "5;a;b\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-7-CE-017: Two name=value extensions — accepted")]
    public async Task Should_AcceptExtensions_When_TwoNameValueExtensions()
    {
        const string chunkedBody = "5;a=1;b=2\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-7-CE-018: Extensions on multiple chunks — all accepted")]
    public async Task Should_AcceptExtensions_When_ExtensionsOnMultipleChunks()
    {
        const string chunkedBody = "3;a=1\r\nfoo\r\n3;b=2\r\nbar\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("foobar", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-7-CE-019: Mixed name-only and name=value extensions — accepted")]
    public async Task Should_AcceptExtensions_When_MixedNameOnlyAndNameValue()
    {
        const string chunkedBody = "5;flag;key=val\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-7-CE-020: Extension on terminator chunk (size=0) — accepted")]
    public async Task Should_AcceptExtension_When_ExtensionOnTerminatorChunk()
    {
        const string chunkedBody = "5;ext=val\r\nHello\r\n0;end=true\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-7-CE-021: BWS with no name following — rejected")]
    public void Should_ThrowInvalidChunkExtension_When_BWSWithNoNameFollowing()
    {
        const string chunkedBody = "5; \r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-7-CE-022: Double semicolon (empty name) — rejected")]
    public void Should_ThrowInvalidChunkExtension_When_DoubleSemicolon()
    {
        const string chunkedBody = "5;;b=val\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-7-CE-023: Unclosed quoted string value — rejected")]
    public void Should_ThrowInvalidChunkExtension_When_UnclosedQuote()
    {
        const string chunkedBody = "5;name=\"val\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-7-CE-024: Empty token value after equals — rejected")]
    public void Should_ThrowInvalidChunkExtension_When_EmptyTokenValue()
    {
        // "name=" with nothing after the equals
        const string chunkedBody = "5;name=\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-7-CE-025: Extension name starts with '=' — rejected")]
    public void Should_ThrowInvalidChunkExtension_When_NameStartsWithEquals()
    {
        const string chunkedBody = "5;=val\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-7-CE-026: Space embedded in extension name — rejected")]
    public void Should_ThrowInvalidChunkExtension_When_SpaceEmbeddedInName()
    {
        // "na me=val": "na" parsed as name, then space consumed as BWS, then 'm' not '=' or ';'
        const string chunkedBody = "5;na me=val\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-7-CE-027: '@' character in token value — rejected")]
    public void Should_ThrowInvalidChunkExtension_When_AtSignInTokenValue()
    {
        const string chunkedBody = "5;name=@val\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-7-CE-028: Trailing invalid char after token value — rejected")]
    public void Should_ThrowInvalidChunkExtension_When_TrailingInvalidCharAfterValue()
    {
        // "name=val@" — '@' after valid token value "val"
        const string chunkedBody = "5;name=val@\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-7-CE-029: '@' character in extension name — rejected")]
    public void Should_ThrowInvalidChunkExtension_When_AtSignInName()
    {
        const string chunkedBody = "5;@name=val\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-7-CE-030: '/' character in extension name — rejected")]
    public void Should_ThrowInvalidChunkExtension_When_SlashInName()
    {
        const string chunkedBody = "5;na/me=val\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-7-CE-031: '[' character in extension name — rejected")]
    public void Should_ThrowInvalidChunkExtension_When_LeftBracketInName()
    {
        const string chunkedBody = "5;na[me\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-7-CE-032: Text after name without equals or semicolon — rejected")]
    public void Should_ThrowInvalidChunkExtension_When_TrailingTextWithNoEqualsOrSemicolon()
    {
        // "name val" — space after name, then 'v' which is not '=' or ';'
        const string chunkedBody = "5;name val\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-7-CE-033: NUL byte in extension name — rejected")]
    public void Should_ThrowInvalidChunkExtension_When_NulByteInName()
    {
        // Inject a NUL byte in the extension name
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 200 OK\r\n");
        sb.Append("Transfer-Encoding: chunked\r\n");
        sb.Append("\r\n");
        var prefix = Encoding.ASCII.GetBytes(sb.ToString());
        var chunkLine = new byte[] { (byte)'5', (byte)';', (byte)'n', 0, (byte)'m', (byte)'\r', (byte)'\n' };
        var chunkData = "Hello\r\n0\r\n\r\n"u8.ToArray();
        var raw = new byte[prefix.Length + chunkLine.Length + chunkData.Length];
        prefix.CopyTo(raw, 0);
        chunkLine.CopyTo(raw, prefix.Length);
        chunkData.CopyTo(raw, prefix.Length + chunkLine.Length);

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-7-CE-034: Second extension invalid in multi-extension — rejected")]
    public void Should_ThrowInvalidChunkExtension_When_SecondExtensionHasInvalidName()
    {
        // good=val ; =bad — second extension name is missing (starts with '=')
        const string chunkedBody = "5;good=val;=bad\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-7-CE-035: Semicolon on second chunk is invalid — rejected")]
    public void Should_ThrowInvalidChunkExtension_When_SecondChunkHasInvalidExtension()
    {
        // First chunk is fine; second chunk has an invalid extension
        const string chunkedBody = "3;valid\r\nfoo\r\n3;=bad\r\nbar\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    private static ReadOnlyMemory<byte> BuildRaw(int code, string reason, string rawBody,
        params (string Name, string Value)[] headers)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {code} {reason}\r\n");
        foreach (var (name, value) in headers)
        {
            sb.Append($"{name}: {value}\r\n");
        }

        sb.Append("\r\n");
        sb.Append(rawBody);
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
