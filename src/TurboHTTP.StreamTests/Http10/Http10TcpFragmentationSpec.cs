using System.Net;
using System.Text;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages.Decoding;

namespace TurboHTTP.StreamTests.Http10;

/// <summary>
/// Tests the HTTP/1.0 decoder stage's handling of TCP-fragmented responses per RFC 1945.
/// Verifies that the decoder correctly reassembles messages split across multiple byte chunks.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http10DecoderStage"/>.
/// RFC 1945 §6: HTTP/1.0 response message framing and parsing across partial TCP segments.
/// </remarks>
public sealed class Http10TcpFragmentationSpec : StreamTestBase
{
    private static IInputItem Chunk(byte[] data)
        => NetworkBuffer.FromArray(data);

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
        return data.Select(b => Chunk([b])).ToList();
    }

    private async Task<HttpResponseMessage> DecodeFragmentsAsync(
        List<IInputItem> fragments)
    {
        var source = Source.From(fragments);
        return await source
            .Via(Flow.FromGraph(new Http10DecoderStage()))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-6")]
    public async Task Http10TcpFragmentation_should_reassemble_response_when_split_into_three_fragments()
    {
        const string fullResponse =
            "HTTP/1.0 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 13\r\n\r\nHello, World!";
        var bytes = Encoding.Latin1.GetBytes(fullResponse);

        // Split into 3 roughly equal fragments
        var third = bytes.Length / 3;
        var fragments = SplitIntoChunks(bytes, [third, third * 2]);

        var response = await DecodeFragmentsAsync(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version10, response.Version);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello, World!", body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-6.2")]
    public async Task Http10TcpFragmentation_should_parse_headers_when_split_across_two_fragments()
    {
        const string fullResponse =
            "HTTP/1.0 200 OK\r\nServer: TurboHttp\r\nX-Custom: test-value\r\nContent-Length: 4\r\n\r\nData";
        var bytes = Encoding.Latin1.GetBytes(fullResponse);

        // Split in the middle of the headers (after "Server: TurboHttp\r\n")
        var splitPoint = fullResponse.IndexOf("X-Custom", StringComparison.Ordinal);
        var fragments = SplitIntoChunks(bytes, [splitPoint]);

        var response = await DecodeFragmentsAsync(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("TurboHttp", response.Headers.GetValues("Server").Single());
        Assert.Equal("test-value", response.Headers.GetValues("X-Custom").Single());
        Assert.Equal(4, response.Content.Headers.ContentLength);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Data", body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-7")]
    public async Task Http10TcpFragmentation_should_complete_body_when_body_arrives_in_separate_fragment()
    {
        const string bodyText = "This is the body content that arrives separately";
        var fullResponse = $"HTTP/1.0 200 OK\r\nContent-Length: {bodyText.Length}\r\n\r\n{bodyText}";
        var bytes = Encoding.Latin1.GetBytes(fullResponse);

        // Split exactly at the header/body boundary (after \r\n\r\n)
        var separatorEnd = fullResponse.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;
        var fragments = SplitIntoChunks(bytes, [separatorEnd]);

        var response = await DecodeFragmentsAsync(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(bodyText, body);
    }

    [Fact(Timeout = 30_000)]
    [Trait("RFC", "RFC1945-6")]
    public async Task Http10TcpFragmentation_should_handle_gracefully_when_single_byte_fragments()
    {
        const string fullResponse = "HTTP/1.0 200 OK\r\nContent-Length: 3\r\n\r\nABC";
        var bytes = Encoding.Latin1.GetBytes(fullResponse);

        var fragments = SplitIntoSingleBytes(bytes);

        var response = await DecodeFragmentsAsync(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version10, response.Version);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("ABC", body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-6")]
    public async Task Http10TcpFragmentation_should_detect_header_end_when_fragment_boundary_inside_crlf_crlf()
    {
        const string fullResponse = "HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nHello";
        var bytes = Encoding.Latin1.GetBytes(fullResponse);

        // Find the \r\n\r\n separator and split right in the middle of it
        var separatorStart = fullResponse.IndexOf("\r\n\r\n", StringComparison.Ordinal);

        // Split between the two \r\n pairs (after first \r\n, before second \r\n)
        var splitPoint = separatorStart + 2; // after first \r\n of the \r\n\r\n
        var fragments = SplitIntoChunks(bytes, [splitPoint]);

        var response = await DecodeFragmentsAsync(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello", body);
    }
}
