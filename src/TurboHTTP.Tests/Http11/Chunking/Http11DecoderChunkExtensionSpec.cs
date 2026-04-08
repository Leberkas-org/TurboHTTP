using System.Text;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Http11;

namespace TurboHTTP.Tests.Http11.Chunking;

/// <summary>
/// Tests chunk-extension parsing per RFC 9112 §7.1.1.
/// Verifies that chunk-ext tokens are ignored and do not interfere with body decoding.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http11Decoder"/>.
/// RFC 9112 §7.1.1: chunk-ext = *( ";" chunk-ext-name [ "=" chunk-ext-val ] ) — MUST be ignored.
/// </remarks>
public sealed class Http11DecoderChunkExtensionSpec
{
    private readonly Http11Decoder _decoder = new();

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_decode_body_when_no_extension_present()
    {
        const string chunkedBody = "5\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_decode_body_when_hex_chunk_size_and_no_extension()
    {
        const string chunkedBody = "a\r\n0123456789\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("0123456789", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_concatenate_chunks_when_multiple_chunks_and_no_extension()
    {
        const string chunkedBody = "3\r\nfoo\r\n3\r\nbar\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("foobar", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_decode_empty_body_when_only_terminator_chunk()
    {
        const string chunkedBody = "0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("RFC", "RFC9112-7")]
    public void Http11Decoder_should_preserve_trailer_fields_when_no_extension_and_trailer_present()
    {
        const string chunkedBody = "3\r\nfoo\r\n0\r\nX-Trailer: value\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        _decoder.TryDecode(raw, out var responses);

        Assert.True(responses[0].TrailingHeaders.TryGetValues("X-Trailer", out var values));
        Assert.Equal("value", values.Single());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_accept_extension_when_name_only_no_value()
    {
        const string chunkedBody = "5;myext\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_accept_extension_when_name_equals_token_value()
    {
        const string chunkedBody = "5;ext=value\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_accept_extension_when_name_equals_quoted_value()
    {
        const string chunkedBody = "5;ext=\"quoted\"\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_accept_extension_when_empty_quoted_value()
    {
        const string chunkedBody = "5;ext=\"\"\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_accept_extension_when_quoted_value_with_escape()
    {
        const string chunkedBody = "5;ext=\"a\\\\b\"\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_accept_extension_when_bws_before_name()
    {
        const string chunkedBody = "5; ext=val\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_accept_extension_when_bws_around_equals_sign()
    {
        const string chunkedBody = "5;ext = val\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_accept_extension_when_bws_is_tab_character()
    {
        var chunkedBody = "5;ext\t=\tval\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_accept_extension_when_name_starts_with_exclamation()
    {
        const string chunkedBody = "5;!ext=val\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_accept_extension_when_name_contains_hash_char()
    {
        const string chunkedBody = "5;#name\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_accept_extensions_when_two_name_only_extensions()
    {
        const string chunkedBody = "5;a;b\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_accept_extensions_when_two_name_value_extensions()
    {
        const string chunkedBody = "5;a=1;b=2\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_accept_extensions_when_extensions_on_multiple_chunks()
    {
        const string chunkedBody = "3;a=1\r\nfoo\r\n3;b=2\r\nbar\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("foobar", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_accept_extensions_when_mixed_name_only_and_name_value()
    {
        const string chunkedBody = "5;flag;key=val\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_accept_extension_when_extension_on_terminator_chunk()
    {
        const string chunkedBody = "5;ext=val\r\nHello\r\n0;end=true\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("RFC", "RFC9112-7")]
    public void Http11Decoder_should_throw_invalid_chunk_extension_when_bws_with_no_name_following()
    {
        const string chunkedBody = "5; \r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact]
    [Trait("RFC", "RFC9112-7")]
    public void Http11Decoder_should_throw_invalid_chunk_extension_when_double_semicolon()
    {
        const string chunkedBody = "5;;b=val\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact]
    [Trait("RFC", "RFC9112-7")]
    public void Http11Decoder_should_throw_invalid_chunk_extension_when_unclosed_quote()
    {
        const string chunkedBody = "5;name=\"val\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact]
    [Trait("RFC", "RFC9112-7")]
    public void Http11Decoder_should_throw_invalid_chunk_extension_when_empty_token_value()
    {
        // "name=" with nothing after the equals
        const string chunkedBody = "5;name=\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact]
    [Trait("RFC", "RFC9112-7")]
    public void Http11Decoder_should_throw_invalid_chunk_extension_when_name_starts_with_equals()
    {
        const string chunkedBody = "5;=val\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact]
    [Trait("RFC", "RFC9112-7")]
    public void Http11Decoder_should_throw_invalid_chunk_extension_when_space_embedded_in_name()
    {
        // "na me=val": "na" parsed as name, then space consumed as BWS, then 'm' not '=' or ';'
        const string chunkedBody = "5;na me=val\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact]
    [Trait("RFC", "RFC9112-7")]
    public void Http11Decoder_should_throw_invalid_chunk_extension_when_at_sign_in_token_value()
    {
        const string chunkedBody = "5;name=@val\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact]
    [Trait("RFC", "RFC9112-7")]
    public void Http11Decoder_should_throw_invalid_chunk_extension_when_trailing_invalid_char_after_value()
    {
        // "name=val@" — '@' after valid token value "val"
        const string chunkedBody = "5;name=val@\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact]
    [Trait("RFC", "RFC9112-7")]
    public void Http11Decoder_should_throw_invalid_chunk_extension_when_at_sign_in_name()
    {
        const string chunkedBody = "5;@name=val\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact]
    [Trait("RFC", "RFC9112-7")]
    public void Http11Decoder_should_throw_invalid_chunk_extension_when_slash_in_name()
    {
        const string chunkedBody = "5;na/me=val\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact]
    [Trait("RFC", "RFC9112-7")]
    public void Http11Decoder_should_throw_invalid_chunk_extension_when_left_bracket_in_name()
    {
        const string chunkedBody = "5;na[me\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact]
    [Trait("RFC", "RFC9112-7")]
    public void Http11Decoder_should_throw_invalid_chunk_extension_when_trailing_text_with_no_equals_or_semicolon()
    {
        // "name val" — space after name, then 'v' which is not '=' or ';'
        const string chunkedBody = "5;name val\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact]
    [Trait("RFC", "RFC9112-7")]
    public void Http11Decoder_should_throw_invalid_chunk_extension_when_nul_byte_in_name()
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

    [Fact]
    [Trait("RFC", "RFC9112-7")]
    public void Http11Decoder_should_throw_invalid_chunk_extension_when_second_extension_has_invalid_name()
    {
        // good=val ; =bad — second extension name is missing (starts with '=')
        const string chunkedBody = "5;good=val;=bad\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkExtension, ex.DecodeError);
    }

    [Fact]
    [Trait("RFC", "RFC9112-7")]
    public void Http11Decoder_should_throw_invalid_chunk_extension_when_second_chunk_has_invalid_extension()
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
