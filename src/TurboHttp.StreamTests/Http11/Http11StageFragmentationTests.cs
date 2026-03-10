using System.Buffers;
using System.Net;
using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http11;

/// <summary>
/// RFC 9112 §6 — TCP fragmentation tests through Http11DecoderStage.
/// Verifies that the Akka.Streams decoder stage correctly reassembles
/// HTTP/1.1 responses split across multiple TCP fragments, including
/// chunked transfer-encoding and Content-Length framed bodies.
/// </summary>
public sealed class Http11StageFragmentationTests : StreamTestBase
{
    private static (IMemoryOwner<byte>, int) Chunk(byte[] data)
        => (new SimpleMemoryOwner(data), data.Length);

    private static (IMemoryOwner<byte>, int) Chunk(string ascii)
    {
        var bytes = Encoding.Latin1.GetBytes(ascii);
        return (new SimpleMemoryOwner(bytes), bytes.Length);
    }

    private static List<(IMemoryOwner<byte>, int)> SplitIntoChunks(byte[] data, int[] splitPoints)
    {
        var chunks = new List<(IMemoryOwner<byte>, int)>();
        var offset = 0;
        foreach (var splitPoint in splitPoints)
        {
            var length = splitPoint - offset;
            var chunk = new byte[length];
            Array.Copy(data, offset, chunk, 0, length);
            chunks.Add(Chunk(chunk));
            offset = splitPoint;
        }

        // Remaining
        if (offset < data.Length)
        {
            var remaining = new byte[data.Length - offset];
            Array.Copy(data, offset, remaining, 0, remaining.Length);
            chunks.Add(Chunk(remaining));
        }

        return chunks;
    }

    private static List<(IMemoryOwner<byte>, int)> SplitIntoSingleBytes(byte[] data)
    {
        var chunks = new List<(IMemoryOwner<byte>, int)>();
        foreach (var b in data)
        {
            chunks.Add(Chunk([b]));
        }

        return chunks;
    }

    private static List<(IMemoryOwner<byte>, int)> SplitIntoSmallFragments(byte[] data, int fragmentSize)
    {
        var chunks = new List<(IMemoryOwner<byte>, int)>();
        for (var i = 0; i < data.Length; i += fragmentSize)
        {
            var length = Math.Min(fragmentSize, data.Length - i);
            var chunk = new byte[length];
            Array.Copy(data, i, chunk, 0, length);
            chunks.Add(Chunk(chunk));
        }

        return chunks;
    }

    private async Task<HttpResponseMessage> DecodeFragmentsAsync(
        List<(IMemoryOwner<byte>, int)> fragments)
    {
        var source = Source.From(fragments);
        return await source
            .Via(Flow.FromGraph(new Http11DecoderStage()))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);
    }

    // ── 11F-001: Chunked response over 4 TCP segments → correctly reassembled ───

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§6-11F-001: Chunked response over 4 TCP segments → correctly reassembled")]
    public async Task ST_11F_001_Chunked_Four_Segments_Reassembled()
    {
        const string fullResponse =
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "5\r\nhello\r\n" +
            "1\r\n \r\n" +
            "5\r\nworld\r\n" +
            "0\r\n\r\n";
        var bytes = Encoding.Latin1.GetBytes(fullResponse);

        // Split into 4 segments at meaningful boundaries
        var quarter = bytes.Length / 4;
        var fragments = SplitIntoChunks(bytes, [quarter, quarter * 2, quarter * 3]);

        var response = await DecodeFragmentsAsync(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version11, response.Version);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello world", body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§6-11F-001b: Chunked response — each chunk in separate TCP segment")]
    public async Task ST_11F_001b_Chunked_Each_Chunk_Separate_Segment()
    {
        // Headers in one segment, each chunk data in its own segment, terminator in last
        var fragments = new List<(IMemoryOwner<byte>, int)>
        {
            Chunk("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n"),
            Chunk("3\r\nfoo\r\n"),
            Chunk("3\r\nbar\r\n"),
            Chunk("0\r\n\r\n")
        };

        var response = await DecodeFragmentsAsync(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("foobar", body);
    }

    // ── 11F-002: Header/body boundary on TCP segment boundary → correctly separated ─

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§6-11F-002: Header/body boundary on TCP segment boundary → correctly separated")]
    public async Task ST_11F_002_HeaderBody_Boundary_On_Segment_Boundary()
    {
        const string bodyText = "Response body content here";
        var fullResponse = $"HTTP/1.1 200 OK\r\nContent-Length: {bodyText.Length}\r\n\r\n{bodyText}";
        var bytes = Encoding.Latin1.GetBytes(fullResponse);

        // Split exactly at the header/body boundary (after \r\n\r\n)
        var separatorEnd = fullResponse.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;
        var fragments = SplitIntoChunks(bytes, [separatorEnd]);

        var response = await DecodeFragmentsAsync(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version11, response.Version);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(bodyText, body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§6-11F-002b: Split in middle of \\r\\n\\r\\n separator → header end detected")]
    public async Task ST_11F_002b_Split_Inside_CrLfCrLf()
    {
        const string fullResponse = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nHello";
        var bytes = Encoding.Latin1.GetBytes(fullResponse);

        // Split between the two \r\n pairs (after first \r\n of the \r\n\r\n)
        var separatorStart = fullResponse.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var splitPoint = separatorStart + 2;
        var fragments = SplitIntoChunks(bytes, [splitPoint]);

        var response = await DecodeFragmentsAsync(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello", body);
    }

    // ── 11F-003: Chunk-size line split across 2 segments → correctly parsed ─────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§6-11F-003: Chunk-size line split across 2 segments → correctly parsed")]
    public async Task ST_11F_003_ChunkSize_Split_Across_Two_Segments()
    {
        const string fullResponse =
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "5\r\nhello\r\n0\r\n\r\n";
        var bytes = Encoding.Latin1.GetBytes(fullResponse);

        // Find the chunk-size line "5\r\n" after headers and split right in the middle
        var headersEnd = fullResponse.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;
        // Split between the '5' and '\r\n' — right after the chunk size digit
        var splitPoint = headersEnd + 1; // After '5', before '\r\n'
        var fragments = SplitIntoChunks(bytes, [splitPoint]);

        var response = await DecodeFragmentsAsync(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§6-11F-003b: Multi-digit chunk size split across segments")]
    public async Task ST_11F_003b_MultiDigitChunkSize_Split()
    {
        // Use a hex chunk size "1a" (= 26 bytes) split across segments
        var chunkBody = new string('X', 26);
        var fullResponse =
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            $"1a\r\n{chunkBody}\r\n0\r\n\r\n";
        var bytes = Encoding.Latin1.GetBytes(fullResponse);

        var headersEnd = fullResponse.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;
        // Split between '1' and 'a' of chunk size "1a"
        var splitPoint = headersEnd + 1;
        var fragments = SplitIntoChunks(bytes, [splitPoint]);

        var response = await DecodeFragmentsAsync(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(chunkBody, body);
    }

    // ── 11F-004: Content-Length body in 3 fragments → fully read ─────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§6-11F-004: Content-Length body in 3 fragments → fully read")]
    public async Task ST_11F_004_ContentLength_Body_Three_Fragments()
    {
        const string bodyText = "AAAAABBBBBCCCCC"; // 15 bytes, will be split into 3 fragments of 5
        var fullResponse = $"HTTP/1.1 200 OK\r\nContent-Length: {bodyText.Length}\r\n\r\n{bodyText}";
        var bytes = Encoding.Latin1.GetBytes(fullResponse);

        var headersEnd = fullResponse.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;
        // Split body into 3 fragments of 5 bytes each
        var fragments = SplitIntoChunks(bytes, [headersEnd, headersEnd + 5, headersEnd + 10]);

        var response = await DecodeFragmentsAsync(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version11, response.Version);
        Assert.Equal(bodyText.Length, response.Content.Headers.ContentLength);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(bodyText, body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§6-11F-004b: Large Content-Length body fragmented into many small pieces")]
    public async Task ST_11F_004b_LargeContentLength_ManyFragments()
    {
        var bodyText = new string('Z', 1024);
        var fullResponse = $"HTTP/1.1 200 OK\r\nContent-Length: {bodyText.Length}\r\n\r\n{bodyText}";
        var bytes = Encoding.Latin1.GetBytes(fullResponse);

        // Fragment into 64-byte pieces
        var fragments = SplitIntoSmallFragments(bytes, 64);

        var response = await DecodeFragmentsAsync(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(bodyText, body);
    }

    // ── 11F-005: Very small fragments (1–2 bytes) → decoder handles gracefully ──

    [Fact(Timeout = 30_000, DisplayName = "RFC-9112-§6-11F-005: 1-byte fragments with Content-Length body → decoder handles gracefully")]
    public async Task ST_11F_005_SingleByte_Fragments_ContentLength()
    {
        const string fullResponse = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\n\r\nABC";
        var bytes = Encoding.Latin1.GetBytes(fullResponse);

        var fragments = SplitIntoSingleBytes(bytes);

        var response = await DecodeFragmentsAsync(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version11, response.Version);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("ABC", body);
    }

    [Fact(Timeout = 30_000, DisplayName = "RFC-9112-§6-11F-005b: 1-byte fragments with chunked body → decoder handles gracefully")]
    public async Task ST_11F_005b_SingleByte_Fragments_Chunked()
    {
        const string fullResponse =
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "3\r\nfoo\r\n0\r\n\r\n";
        var bytes = Encoding.Latin1.GetBytes(fullResponse);

        var fragments = SplitIntoSingleBytes(bytes);

        var response = await DecodeFragmentsAsync(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("foo", body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§6-11F-005c: 2-byte fragments → decoder handles gracefully")]
    public async Task ST_11F_005c_TwoByte_Fragments()
    {
        const string fullResponse = "HTTP/1.1 200 OK\r\nContent-Length: 6\r\n\r\nHello!";
        var bytes = Encoding.Latin1.GetBytes(fullResponse);

        var fragments = SplitIntoSmallFragments(bytes, 2);

        var response = await DecodeFragmentsAsync(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello!", body);
    }
}
