using System.Net;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages.Decoding;
using TextEncoding = System.Text.Encoding;

namespace TurboHttp.StreamTests.Http11.Decoding;

/// <summary>
/// Tests the HTTP/1.1 decoder stage's handling of TCP-fragmented responses per RFC 9112.
/// Verifies that the decoder correctly reassembles messages split across multiple byte chunks.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http11DecoderStage"/>.
/// RFC 9112 §2.2: HTTP/1.1 message parsing robustness across partial TCP segments.
/// </remarks>
public sealed class Http11TcpFragmentationReassemblySpec : StreamTestBase
{
    private static IInputItem Chunk(byte[] data)
        => NetworkBuffer.FromArray(data);

    private static IInputItem Chunk(string ascii)
    {
        var bytes = TextEncoding.Latin1.GetBytes(ascii);
        return NetworkBuffer.FromArray(bytes);
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11TcpFragmentation_should_reassemble_chunked_response_when_four_tcp_segments()
    {
        const string fullResponse =
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "5\r\nhello\r\n" +
            "1\r\n \r\n" +
            "5\r\nworld\r\n" +
            "0\r\n\r\n";
        var bytes = TextEncoding.Latin1.GetBytes(fullResponse);

        // Split into 4 segments at meaningful boundaries
        var quarter = bytes.Length / 4;
        var fragments = SplitIntoChunks(bytes, [quarter, quarter * 2, quarter * 3]);

        var response = await DecodeFragmentsAsync(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version11, response.Version);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello world", body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11TcpFragmentation_should_reassemble_chunked_response_when_each_chunk_in_separate_segment()
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
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("foobar", body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11TcpFragmentation_should_separate_header_and_body_when_boundary_on_segment_boundary()
    {
        const string bodyText = "Response body content here";
        var fullResponse = $"HTTP/1.1 200 OK\r\nContent-Length: {bodyText.Length}\r\n\r\n{bodyText}";
        var bytes = TextEncoding.Latin1.GetBytes(fullResponse);

        // Split exactly at the header/body boundary (after \r\n\r\n)
        var separatorEnd = fullResponse.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;
        var fragments = SplitIntoChunks(bytes, [separatorEnd]);

        var response = await DecodeFragmentsAsync(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version11, response.Version);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(bodyText, body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11TcpFragmentation_should_detect_header_end_when_split_inside_crlf_crlf()
    {
        const string fullResponse = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nHello";
        var bytes = TextEncoding.Latin1.GetBytes(fullResponse);

        // Split between the two \r\n pairs (after first \r\n of the \r\n\r\n)
        var separatorStart = fullResponse.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var splitPoint = separatorStart + 2;
        var fragments = SplitIntoChunks(bytes, [splitPoint]);

        var response = await DecodeFragmentsAsync(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello", body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11TcpFragmentation_should_parse_chunk_size_when_split_across_two_segments()
    {
        const string fullResponse =
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "5\r\nhello\r\n0\r\n\r\n";
        var bytes = TextEncoding.Latin1.GetBytes(fullResponse);

        // Find the chunk-size line "5\r\n" after headers and split right in the middle
        var headersEnd = fullResponse.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;
        // Split between the '5' and '\r\n' — right after the chunk size digit
        var splitPoint = headersEnd + 1; // After '5', before '\r\n'
        var fragments = SplitIntoChunks(bytes, [splitPoint]);

        var response = await DecodeFragmentsAsync(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello", body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11TcpFragmentation_should_parse_multi_digit_chunk_size_when_split_across_segments()
    {
        // Use a hex chunk size "1a" (= 26 bytes) split across segments
        var chunkBody = new string('X', 26);
        var fullResponse =
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            $"1a\r\n{chunkBody}\r\n0\r\n\r\n";
        var bytes = TextEncoding.Latin1.GetBytes(fullResponse);

        var headersEnd = fullResponse.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;
        // Split between '1' and 'a' of chunk size "1a"
        var splitPoint = headersEnd + 1;
        var fragments = SplitIntoChunks(bytes, [splitPoint]);

        var response = await DecodeFragmentsAsync(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(chunkBody, body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11TcpFragmentation_should_read_content_length_body_when_three_fragments()
    {
        const string bodyText = "AAAAABBBBBCCCCC"; // 15 bytes, will be split into 3 fragments of 5
        var fullResponse = $"HTTP/1.1 200 OK\r\nContent-Length: {bodyText.Length}\r\n\r\n{bodyText}";
        var bytes = TextEncoding.Latin1.GetBytes(fullResponse);

        var headersEnd = fullResponse.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;
        // Split body into 3 fragments of 5 bytes each
        var fragments = SplitIntoChunks(bytes, [headersEnd, headersEnd + 5, headersEnd + 10]);

        var response = await DecodeFragmentsAsync(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version11, response.Version);
        Assert.Equal(bodyText.Length, response.Content.Headers.ContentLength);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(bodyText, body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11TcpFragmentation_should_read_large_content_length_body_when_many_fragments()
    {
        var bodyText = new string('Z', 1024);
        var fullResponse = $"HTTP/1.1 200 OK\r\nContent-Length: {bodyText.Length}\r\n\r\n{bodyText}";
        var bytes = TextEncoding.Latin1.GetBytes(fullResponse);

        // Fragment into 64-byte pieces
        var fragments = SplitIntoSmallFragments(bytes, 64);

        var response = await DecodeFragmentsAsync(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(bodyText, body);
    }

    [Fact(Timeout = 30_000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11TcpFragmentation_should_handle_content_length_body_when_single_byte_fragments()
    {
        const string fullResponse = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\n\r\nABC";
        var bytes = TextEncoding.Latin1.GetBytes(fullResponse);

        var fragments = SplitIntoSingleBytes(bytes);

        var response = await DecodeFragmentsAsync(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version11, response.Version);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("ABC", body);
    }

    [Fact(Timeout = 30_000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11TcpFragmentation_should_handle_chunked_body_when_single_byte_fragments()
    {
        const string fullResponse =
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "3\r\nfoo\r\n0\r\n\r\n";
        var bytes = TextEncoding.Latin1.GetBytes(fullResponse);

        var fragments = SplitIntoSingleBytes(bytes);

        var response = await DecodeFragmentsAsync(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("foo", body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11TcpFragmentation_should_handle_body_when_two_byte_fragments()
    {
        const string fullResponse = "HTTP/1.1 200 OK\r\nContent-Length: 6\r\n\r\nHello!";
        var bytes = TextEncoding.Latin1.GetBytes(fullResponse);

        var fragments = SplitIntoSmallFragments(bytes, 2);

        var response = await DecodeFragmentsAsync(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello!", body);
    }
}
