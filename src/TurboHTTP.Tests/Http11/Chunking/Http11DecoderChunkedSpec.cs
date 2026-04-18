using System.Text;
using TurboHTTP.Protocol;
using Decoder = TurboHTTP.Protocol.Http11.Decoder;

namespace TurboHTTP.Tests.Http11.Chunking;

public sealed class Http11DecoderChunkedSpec
{
    private readonly Decoder _decoder = new();

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_decode_correctly_when_chunked_body()
    {
        const string chunkedBody = "5\r\nHello\r\n6\r\n World\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);
        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_decode_when_single_chunk()
    {
        const string chunkedBody = "5\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_concatenate_when_multiple_chunks()
    {
        const string chunkedBody = "3\r\nfoo\r\n3\r\nbar\r\n3\r\nbaz\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("foobarbaz", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_silently_ignore_when_chunk_extension()
    {
        const string chunkedBody = "5;ext=value\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public void Http11Decoder_should_be_accessible_when_trailer_fields_after_final_chunk()
    {
        const string chunkedBody = "5\r\nHello\r\n0\r\nX-Trailer: value\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].TrailingHeaders.TryGetValues("X-Trailer", out var values));
        Assert.Equal("value", values.Single());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public void Http11Decoder_should_error_when_non_hex_chunk_size()
    {
        const string chunkedBody = "xyz\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkSize, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public void Http11Decoder_should_need_more_data_when_missing_final_chunk()
    {
        const string partial = "5\r\nHel";
        var raw = BuildRaw(200, "OK", partial, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out _);

        Assert.False(decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_terminate_body_when_zero_chunk()
    {
        const string chunkedBody = "5\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public void Http11Decoder_should_error_when_chunk_size_overflow()
    {
        const string chunkedBody = "999999999999\r\ndata\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkSize, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_decode_when_one_byte_chunk()
    {
        const string chunkedBody = "1\r\nX\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("X", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_accept_when_uppercase_hex_chunk_size()
    {
        const string chunkedBody = "A\r\n0123456789\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("0123456789", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11Decoder_should_accept_when_empty_chunk_before_terminator()
    {
        // Test an empty chunked body: only the terminator chunk (0\r\n\r\n) with no data chunks
        const string chunkedBody = "0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("", result); // Empty body
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public void Http11Decoder_should_not_merge_trailers_when_chunked_with_trailers()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public void Http11Decoder_should_have_trailers_when_chunked_with_trailers()
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
