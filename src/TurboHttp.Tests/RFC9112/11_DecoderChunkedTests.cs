using System.Text;
using TurboHttp.Protocol;
using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.Tests.RFC9112;

/// <summary>
/// Tests chunked transfer encoding decoding per RFC 9112 §7.1.
/// Verifies chunk-size parsing, data accumulation, and trailing CRLF handling.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http11Decoder"/>.
/// RFC 9112 §7.1: Chunked transfer coding — chunk-size CRLF chunk-data CRLF … "0" CRLF CRLF.
/// </remarks>
public sealed class Http11DecoderChunkedTests
{
    private readonly Http11Decoder _decoder = new();

    [Fact]
    public async Task Should_DecodeCorrectly_When_ChunkedBody()
    {
        const string chunkedBody = "5\r\nHello\r\n6\r\n World\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);
        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("Hello World", result);
    }

    [Fact(DisplayName = "RFC9112-7-CH-001: Single chunk body decoded")]
    public async Task Should_Decode_When_SingleChunk()
    {
        const string chunkedBody = "5\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("Hello", result);
    }

    [Fact(DisplayName = "RFC9112-7-CH-002: Multiple chunks concatenated")]
    public async Task Should_Concatenate_When_MultipleChunks()
    {
        const string chunkedBody = "3\r\nfoo\r\n3\r\nbar\r\n3\r\nbaz\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("foobarbaz", result);
    }

    [Fact(DisplayName = "RFC9112-7-CH-003: Chunk extension silently ignored")]
    public async Task Should_SilentlyIgnore_When_ChunkExtension()
    {
        const string chunkedBody = "5;ext=value\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("Hello", result);
    }

    [Fact(DisplayName = "RFC9112-7-CH-004: Trailer fields after final chunk")]
    public void Should_BeAccessible_When_TrailerFieldsAfterFinalChunk()
    {
        const string chunkedBody = "5\r\nHello\r\n0\r\nX-Trailer: value\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].TrailingHeaders.TryGetValues("X-Trailer", out var values));
        Assert.Equal("value", values.Single());
    }

    [Fact(DisplayName = "RFC9112-7-CH-005: Non-hex chunk size is parse error")]
    public void Should_Error_When_NonHexChunkSize()
    {
        const string chunkedBody = "xyz\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkSize, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-7-CH-006: Missing final chunk is NeedMoreData")]
    public void Should_NeedMoreData_When_MissingFinalChunk()
    {
        const string partial = "5\r\nHel";
        var raw = BuildRaw(200, "OK", partial, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out _);

        Assert.False(decoded);
    }

    [Fact(DisplayName = "RFC9112-7-CH-007: 0\\r\\n\\r\\n terminates chunked body")]
    public async Task Should_TerminateBody_When_ZeroChunk()
    {
        const string chunkedBody = "5\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("Hello", result);
    }

    [Fact(DisplayName = "RFC9112-7-CH-008: Chunk size overflow is parse error")]
    public void Should_Error_When_ChunkSizeOverflow()
    {
        const string chunkedBody = "999999999999\r\ndata\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkSize, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-7-CH-009: 1-byte chunk decoded")]
    public async Task Should_Decode_When_OneByteChunk()
    {
        const string chunkedBody = "1\r\nX\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("X", result);
    }

    [Fact(DisplayName = "RFC9112-7-CH-010: Uppercase hex chunk size accepted")]
    public async Task Should_Accept_When_UppercaseHexChunkSize()
    {
        const string chunkedBody = "A\r\n0123456789\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("0123456789", result);
    }

    [Fact(DisplayName = "RFC9112-7-CH-011: Empty chunk (0 data bytes) before terminator accepted")]
    public async Task Should_Accept_When_EmptyChunkBeforeTerminator()
    {
        // Test an empty chunked body: only the terminator chunk (0\r\n\r\n) with no data chunks
        const string chunkedBody = "0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("", result); // Empty body
    }

    [Fact(DisplayName = "RFC9112-7.1.2-CH-020: Trailers not merged into response headers")]
    public void Should_NotMergeTrailers_When_ChunkedWithTrailers()
    {
        const string chunkedBody = "5\r\nHello\r\n0\r\nX-Checksum: abc123\r\nX-Signature: def456\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.False(responses[0].Headers.Contains("X-Checksum"));
        Assert.False(responses[0].Headers.Contains("X-Signature"));
        Assert.False(responses[0].Content.Headers.Contains("X-Checksum"));
        Assert.False(responses[0].Content.Headers.Contains("X-Signature"));
    }

    [Fact(DisplayName = "RFC9112-7.1.2-CH-021: Trailers available in TrailingHeaders")]
    public void Should_HaveTrailers_When_ChunkedWithTrailers()
    {
        const string chunkedBody = "5\r\nHello\r\n0\r\nX-Checksum: abc123\r\nX-Signature: def456\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].TrailingHeaders.TryGetValues("X-Checksum", out var checksumValues));
        Assert.Equal("abc123", checksumValues.Single());
        Assert.True(responses[0].TrailingHeaders.TryGetValues("X-Signature", out var signatureValues));
        Assert.Equal("def456", signatureValues.Single());
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
