using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9112;

public sealed class Http11RoundTripChunkedTests
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

    [Fact(DisplayName = "RFC9112-6: HTTP/1.1 GET → 200 chunked response round-trip")]
    public async Task Should_AssembleChunkedBody_When_ChunkedRoundTrip()
    {
        var decoder = new Http11Decoder();
        var raw = BuildChunkedResponse(200, "OK", ["Hello, ", "World!"]);
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal("Hello, World!", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-6: HTTP/1.1 GET → response with 5 chunks round-trip")]
    public async Task Should_ConcatenateChunks_When_FiveChunksRoundTrip()
    {
        var decoder = new Http11Decoder();
        var raw = BuildChunkedResponse(200, "OK", ["one", "two", "three", "four", "five"]);
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal("onetwothreefourfive", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-6: HTTP/1.1 chunked response with trailer round-trip")]
    public async Task Should_AccessTrailer_When_ChunkedWithTrailerRoundTrip()
    {
        var decoder = new Http11Decoder();
        var raw = BuildChunkedResponse(200, "OK",
            ["chunk1", "chunk2"],
            [("X-Checksum", "abc123")]);
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal("chunk1chunk2", await responses[0].Content.ReadAsStringAsync());
        Assert.True(responses[0].TrailingHeaders.TryGetValues("X-Checksum", out var trailerVals));
        Assert.Equal("abc123", trailerVals.Single());
    }

    [Fact(DisplayName = "RFC9112-6: Single 1-byte chunk decoded correctly")]
    public async Task Should_DecodeOneByte_When_SingleByteChunkRoundTrip()
    {
        var decoder = new Http11Decoder();
        var raw = BuildChunkedResponse(200, "OK", ["A"]);
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal("A", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-6: Uppercase hex chunk size decoded correctly")]
    public async Task Should_DecodeBody_When_UppercaseHexChunkSizeRoundTrip()
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
        Assert.Equal("0123456789", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-6: 20 single-character chunks concatenated correctly")]
    public async Task Should_ConcatenateAllChunks_When_TwentyTinyChunksRoundTrip()
    {
        var chars = Enumerable.Range(0, 20).Select(i => ((char)('a' + i)).ToString()).ToArray();
        var decoder = new Http11Decoder();
        var raw = BuildChunkedResponse(200, "OK", chars);
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        var expected = string.Concat(chars);
        Assert.Equal(expected, await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-6: 32KB single chunk decoded correctly")]
    public async Task Should_Preserve32KbChunk_When_LargeChunkRoundTrip()
    {
        var body = new string('X', 32768);
        var decoder = new Http11Decoder(maxBodySize: 32768 + 1024);
        var raw = BuildChunkedResponse(200, "OK", [body]);
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        var decoded = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal(32768, decoded.Length);
        Assert.All(decoded, c => Assert.Equal('X', c));
    }

    [Fact(DisplayName = "RFC9112-6: Chunk with extension token — body decoded correctly")]
    public async Task Should_DecodeBody_When_ChunkHasExtensionRoundTrip()
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
        Assert.Equal("Hello World", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-6: Pipelined chunked then Content-Length response decoded")]
    public async Task Should_DecodeBoth_When_ChunkedThenContentLengthPipelined()
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
        Assert.Equal("chunk-data", await responses[0].Content.ReadAsStringAsync());
        Assert.Equal("fixed", await responses[1].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-6: Chunked body with two trailer headers round-trip")]
    public async Task Should_AccessBothTrailers_When_TwoTrailerHeadersRoundTrip()
    {
        var decoder = new Http11Decoder();
        var raw = BuildChunkedResponse(200, "OK",
            ["part1", "part2"],
            [("X-Digest", "sha256:abc"), ("X-Request-Id", "req-999")]);
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal("part1part2", await responses[0].Content.ReadAsStringAsync());
        Assert.True(responses[0].TrailingHeaders.TryGetValues("X-Digest", out var digest));
        Assert.Equal("sha256:abc", digest.Single());
        Assert.True(responses[0].TrailingHeaders.TryGetValues("X-Request-Id", out var reqId));
        Assert.Equal("req-999", reqId.Single());
    }
}
