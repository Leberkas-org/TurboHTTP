using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9112;

public sealed class Http11DecoderChunkedTests
{
    private readonly Http11Decoder _decoder = new();

    [Fact]
    public async Task ChunkedBody_Decodes_Correctly()
    {
        const string chunkedBody = "5\r\nHello\r\n6\r\n World\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);
        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public async Task ChunkedBody_WithExtensions_Ignored()
    {
        const string chunkedBody = "5;ext=value\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);
        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void ChunkedBody_Incomplete_NeedMoreData()
    {
        const string partial = "5\r\nHel";
        var raw = BuildRaw(200, "OK", partial, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out _);
        Assert.False(decoded);
    }

    [Fact]
    public void Decode_InvalidChunkSize_ReturnsError()
    {
        // RFC 7230 §4.1: chunk-size is a hex string. Non-hex characters MUST cause a parse error.
        const string chunkedBody = "xyz\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.InvalidChunkSize, ex.DecodeError);
    }

    [Fact]
    public void Decode_ChunkSizeTooLarge_ReturnsError()
    {
        // RFC 7230 §4.1: A chunk size that overflows the parser's integer type MUST be rejected.
        const string chunkedBody = "999999999999\r\ndata\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.InvalidChunkSize, ex.DecodeError);
    }

    [Fact]
    public void Decode_ChunkedWithTrailer_TrailerHeadersPresent()
    {
        // RFC 7230 §4.1.2: A chunked message may include trailer fields after the last chunk.
        // Trailer headers appear between the final "0\r\n" chunk and the terminating "\r\n".
        const string chunkedBody = "5\r\nHello\r\n0\r\nX-Trailer: value\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.True(responses[0].TrailingHeaders.TryGetValues("X-Trailer", out var values));
        Assert.Equal("value", values.Single());
    }

    [Fact(DisplayName = "RFC7230-4.1: Single chunk body decoded")]
    public async Task SingleChunk_Decoded()
    {
        const string chunkedBody = "5\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("Hello", result);
    }

    [Fact(DisplayName = "RFC7230-4.1: Multiple chunks concatenated")]
    public async Task MultipleChunks_Concatenated()
    {
        const string chunkedBody = "3\r\nfoo\r\n3\r\nbar\r\n3\r\nbaz\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("foobarbaz", result);
    }

    [Fact(DisplayName = "RFC7230-4.1: Chunk extension silently ignored")]
    public async Task ChunkExtension_SilentlyIgnored()
    {
        const string chunkedBody = "5;ext=value\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("Hello", result);
    }

    [Fact(DisplayName = "RFC7230-4.1: Trailer fields after final chunk")]
    public void TrailerFields_AfterFinalChunk_Accessible()
    {
        const string chunkedBody = "5\r\nHello\r\n0\r\nX-Trailer: value\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].TrailingHeaders.TryGetValues("X-Trailer", out var values));
        Assert.Equal("value", values.Single());
    }

    [Fact(DisplayName = "RFC7230-4.1: Non-hex chunk size is parse error")]
    public void NonHex_ChunkSize_IsError()
    {
        const string chunkedBody = "xyz\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.InvalidChunkSize, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC7230-4.1: Missing final chunk is NeedMoreData")]
    public void MissingFinalChunk_NeedMoreData()
    {
        const string partial = "5\r\nHel";
        var raw = BuildRaw(200, "OK", partial, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out _);

        Assert.False(decoded);
    }

    [Fact(DisplayName = "RFC7230-4.1: 0\\r\\n\\r\\n terminates chunked body")]
    public async Task ZeroChunk_TerminatesChunkedBody()
    {
        const string chunkedBody = "5\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("Hello", result);
    }

    [Fact(DisplayName = "RFC7230-4.1: Chunk size overflow is parse error")]
    public void ChunkSize_Overflow_IsError()
    {
        const string chunkedBody = "999999999999\r\ndata\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.InvalidChunkSize, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-6: 1-byte chunk decoded")]
    public async Task OneByte_Chunk_Decoded()
    {
        const string chunkedBody = "1\r\nX\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("X", result);
    }

    [Fact(DisplayName = "RFC9112-6: Uppercase hex chunk size accepted")]
    public async Task Uppercase_HexChunkSize_Accepted()
    {
        const string chunkedBody = "A\r\n0123456789\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("0123456789", result);
    }

    [Fact(DisplayName = "RFC9112-6: Empty chunk (0 data bytes) before terminator accepted")]
    public async Task EmptyChunk_BeforeTerminator_Accepted()
    {
        // Test an empty chunked body: only the terminator chunk (0\r\n\r\n) with no data chunks
        const string chunkedBody = "0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("", result); // Empty body
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
