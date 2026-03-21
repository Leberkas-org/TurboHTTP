using System.Net;
using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages.Decoding;

namespace TurboHttp.StreamTests.RFC9112;

/// <summary>
/// Tests the HTTP/1.1 decoder stage's handling of TCP-fragmented responses per RFC 9112.
/// Verifies that the decoder correctly reassembles messages split across multiple byte chunks.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http11DecoderStage"/>.
/// RFC 9112 §2.2: HTTP/1.1 message parsing robustness across partial TCP segments.
/// </remarks>
public sealed class Http11TcpFragmentationReassemblyTests : StreamTestBase
{
    private static IInputItem Chunk(byte[] data)
        => new DataItem(new SimpleMemoryOwner(data), data.Length) { Key = RequestEndpoint.Default };

    private static IInputItem Chunk(string ascii)
    {
        var bytes = Encoding.Latin1.GetBytes(ascii);
        return new DataItem(new SimpleMemoryOwner(bytes), bytes.Length) { Key = RequestEndpoint.Default };
    }

    private static List<IInputItem> SplitIntoChunks(byte[] data, int[] splitPoints)
    {
        var chunks = new List<IInputItem>();
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

    private static List<IInputItem> SplitIntoSingleBytes(byte[] data)
    {
        var chunks = new List<IInputItem>();
        foreach (var b in data)
        {
            chunks.Add(Chunk([b]));
        }

        return chunks;
    }

    private static List<IInputItem> SplitIntoSmallFragments(byte[] data, int fragmentSize)
    {
        var chunks = new List<IInputItem>();
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
        List<IInputItem> fragments)
    {
        var source = Source.From(fragments);
        return await source
            .Via(Flow.FromGraph(new Http11DecoderStage()))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9112-6-11FR-001: Chunked response over 4 TCP segments → correctly reassembled")]
    public async Task Should_ReassembleChunkedResponse_WhenFourTcpSegments()
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

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9112-6-11FR-001b: Chunked response — each chunk in separate TCP segment")]
    public async Task Should_ReassembleChunkedResponse_WhenEachChunkInSeparateSegment()
    {
        // Headers in one segment, each chunk data in its own segment, terminator in last
        var fragments = new List<IInputItem>
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

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9112-6-11FR-002: Header/body boundary on TCP segment boundary → correctly separated")]
    public async Task Should_SeparateHeaderAndBody_WhenBoundaryOnSegmentBoundary()
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

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9112-6-11FR-002b: Split in middle of \\r\\n\\r\\n separator → header end detected")]
    public async Task Should_DetectHeaderEnd_WhenSplitInsideCrLfCrLf()
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

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9112-6-11FR-003: Chunk-size line split across 2 segments → correctly parsed")]
    public async Task Should_ParseChunkSize_WhenSplitAcrossTwoSegments()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-6-11FR-003b: Multi-digit chunk size split across segments")]
    public async Task Should_ParseMultiDigitChunkSize_WhenSplitAcrossSegments()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-6-11FR-004: Content-Length body in 3 fragments → fully read")]
    public async Task Should_ReadContentLengthBody_WhenThreeFragments()
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

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9112-6-11FR-004b: Large Content-Length body fragmented into many small pieces")]
    public async Task Should_ReadLargeContentLengthBody_WhenManyFragments()
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

    [Fact(Timeout = 30_000,
        DisplayName = "RFC9112-6-11FR-005: 1-byte fragments with Content-Length body → decoder handles gracefully")]
    public async Task Should_HandleContentLengthBody_WhenSingleByteFragments()
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

    [Fact(Timeout = 30_000,
        DisplayName = "RFC9112-6-11FR-005b: 1-byte fragments with chunked body → decoder handles gracefully")]
    public async Task Should_HandleChunkedBody_WhenSingleByteFragments()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-6-11FR-005c: 2-byte fragments → decoder handles gracefully")]
    public async Task Should_HandleBody_WhenTwoByteFragments()
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