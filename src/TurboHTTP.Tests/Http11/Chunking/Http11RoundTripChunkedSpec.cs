using System.Text;
using TurboHTTP.Protocol.Http11;

namespace TurboHTTP.Tests.Http11.Chunking;

/// <summary>
/// Tests round-trip encoding and decoding of chunked transfer encoding per RFC 9112 §7.1.
/// Verifies that chunked bodies are correctly encoded and decoded end-to-end.
/// </summary>
/// <remarks>
/// Classes under test: <see cref="Http11Encoder"/> and <see cref="Http11Decoder"/>.
/// RFC 9112 §7.1: Chunked transfer coding — chunk-size CRLF chunk-data CRLF … "0" CRLF CRLF.
/// </remarks>
public sealed class Http11RoundTripChunkedSpec
{
    private static ReadOnlyMemory<byte> BuildChunkedResponse(int status, string reason,
        string[] chunks, (string Name, string Value)[]? trailers = null)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {status} {reason}\r\n");
        sb.Append("Transfer-Encoding: chunked\r\n");
        sb.Append("\r\n");
        foreach (var chunk in chunks)
        {
            var chunkLen = Encoding.ASCII.GetByteCount(chunk);
            sb.Append($"{chunkLen:x}\r\n{chunk}\r\n");
        }

        sb.Append("0\r\n");
        if (trailers != null)
        {
            foreach (var (name, value) in trailers)
            {
                sb.Append($"{name}: {value}\r\n");
            }
        }

        sb.Append("\r\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static ReadOnlyMemory<byte> Combine(params ReadOnlyMemory<byte>[] parts)
    {
        var totalLen = parts.Sum(p => p.Length);
        var result = new byte[totalLen];
        var offset = 0;
        foreach (var part in parts)
        {
            part.Span.CopyTo(result.AsSpan(offset));
            offset += part.Length;
        }

        return result;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11RoundTrip_should_assemble_chunked_body_when_chunked_round_trip()
    {
        var decoder = new Http11Decoder();
        var raw = BuildChunkedResponse(200, "OK", ["Hello, ", "World!"]);
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal("Hello, World!", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11RoundTrip_should_concatenate_chunks_when_five_chunks_round_trip()
    {
        var decoder = new Http11Decoder();
        var raw = BuildChunkedResponse(200, "OK", ["one", "two", "three", "four", "five"]);
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal("onetwothreefourfive", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11RoundTrip_should_access_trailer_when_chunked_with_trailer_round_trip()
    {
        var decoder = new Http11Decoder();
        var raw = BuildChunkedResponse(200, "OK",
            ["chunk1", "chunk2"],
            [("X-Checksum", "abc123")]);
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal("chunk1chunk2", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(responses[0].TrailingHeaders.TryGetValues("X-Checksum", out var trailerVals));
        Assert.Equal("abc123", trailerVals.Single());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11RoundTrip_should_decode_one_byte_when_single_byte_chunk_round_trip()
    {
        var decoder = new Http11Decoder();
        var raw = BuildChunkedResponse(200, "OK", ["A"]);
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal("A", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11RoundTrip_should_decode_body_when_uppercase_hex_chunk_size_round_trip()
    {
        const string rawResponse =
            "HTTP/1.1 200 OK\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "\r\n" +
            "A\r\n" +
            "0123456789\r\n" +
            "0\r\n" +
            "\r\n";
        var mem = (ReadOnlyMemory<byte>)Encoding.ASCII.GetBytes(rawResponse);

        var decoder = new Http11Decoder();
        decoder.TryDecode(mem, out var responses);

        Assert.Single(responses);
        Assert.Equal("0123456789", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11RoundTrip_should_concatenate_all_chunks_when_twenty_tiny_chunks_round_trip()
    {
        var chars = Enumerable.Range(0, 20).Select(i => ((char)('a' + i)).ToString()).ToArray();
        var decoder = new Http11Decoder();
        var raw = BuildChunkedResponse(200, "OK", chars);
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        var expected = string.Concat(chars);
        Assert.Equal(expected, await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11RoundTrip_should_preserve_32kb_chunk_when_large_chunk_round_trip()
    {
        var body = new string('X', 32768);
        var decoder = new Http11Decoder(maxBodySize: 32768 + 1024);
        var raw = BuildChunkedResponse(200, "OK", [body]);
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        var decoded = await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(32768, decoded.Length);
        Assert.All(decoded, c => Assert.Equal('X', c));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11RoundTrip_should_decode_body_when_chunk_has_extension_round_trip()
    {
        const string rawResponse =
            "HTTP/1.1 200 OK\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "\r\n" +
            "5;ext=value\r\n" +
            "Hello\r\n" +
            "6;checksum=abc\r\n" +
            " World\r\n" +
            "0\r\n" +
            "\r\n";
        var mem = (ReadOnlyMemory<byte>)Encoding.ASCII.GetBytes(rawResponse);

        var decoder = new Http11Decoder();
        decoder.TryDecode(mem, out var responses);

        Assert.Single(responses);
        Assert.Equal("Hello World", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11RoundTrip_should_decode_both_when_chunked_then_content_length_pipelined()
    {
        var chunked = BuildChunkedResponse(200, "OK", ["chunk-data"]);
        var fixedLen = new StringBuilder();
        fixedLen.Append("HTTP/1.1 201 Created\r\n");
        fixedLen.Append("Content-Length: 5\r\n");
        fixedLen.Append("\r\n");
        fixedLen.Append("fixed");
        var fixedLenMem = (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(fixedLen.ToString());
        var combined = Combine(chunked, fixedLenMem);

        var decoder = new Http11Decoder();
        decoder.TryDecode(combined, out var responses);

        Assert.Equal(2, responses.Count);
        Assert.Equal("chunk-data", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("fixed", await responses[1].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11RoundTrip_should_access_both_trailers_when_two_trailer_headers_round_trip()
    {
        var decoder = new Http11Decoder();
        var raw = BuildChunkedResponse(200, "OK",
            ["part1", "part2"],
            [("X-Digest", "sha256:abc"), ("X-Request-Id", "req-999")]);
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal("part1part2", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(responses[0].TrailingHeaders.TryGetValues("X-Digest", out var digest));
        Assert.Equal("sha256:abc", digest.Single());
        Assert.True(responses[0].TrailingHeaders.TryGetValues("X-Request-Id", out var reqId));
        Assert.Equal("req-999", reqId.Single());
    }
}
