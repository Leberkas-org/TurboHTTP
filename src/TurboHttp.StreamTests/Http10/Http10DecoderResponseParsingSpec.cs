using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages.Decoding;

namespace TurboHttp.StreamTests.Http10;

/// <summary>
/// RFC-tagged tests for the HTTP/1.0 response decoder stage per RFC 1945.
/// Verifies status code parsing, header field handling, and body framing as mandated by the specification.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http10DecoderStage"/>.
/// RFC 1945 §6: HTTP/1.0 response message format, status lines, and header fields.
/// </remarks>
public sealed class Http10DecoderResponseParsingSpec : StreamTestBase
{
    private static IInputItem Chunk(string ascii)
    {
        var bytes = Encoding.Latin1.GetBytes(ascii);
        return NetworkBuffer.FromArray(bytes);
    }

    private async Task<HttpResponseMessage> DecodeAsync(params string[] chunks)
    {
        var source = Source.From(chunks.Select(Chunk));
        return await source
            .Via(Flow.FromGraph(new Http10DecoderStage()))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);
    }

    private async Task<IReadOnlyList<HttpResponseMessage>> DecodeAllAsync(params string[] chunks)
    {
        var source = Source.From(chunks.Select(Chunk));
        return await source
            .Via(Flow.FromGraph(new Http10DecoderStage()))
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-6.1")]
    public async Task Http10DecoderResponseParsing_should_parse_status_code_200_and_version_10_when_status_line_is_200ok()
    {
        var response = await DecodeAsync("HTTP/1.0 200 OK\r\n\r\n");

        Assert.Equal(200, (int)response.StatusCode);
        Assert.Equal(new Version(1, 0), response.Version);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-6.1")]
    public async Task Http10DecoderResponseParsing_should_parse_status_code_404_when_status_line_is_404_not_found()
    {
        var response = await DecodeAsync("HTTP/1.0 404 Not Found\r\n\r\n");

        Assert.Equal(404, (int)response.StatusCode);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-6.2")]
    public async Task Http10DecoderResponseParsing_should_parse_content_type_and_content_length_when_headers_present()
    {
        const string raw =
            "HTTP/1.0 200 OK\r\n" +
            "Content-Type: text/html\r\n" +
            "Content-Length: 4\r\n" +
            "\r\n" +
            "test";

        var response = await DecodeAsync(raw);

        Assert.Equal(200, (int)response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(4L, response.Content.Headers.ContentLength);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-7")]
    public async Task Http10DecoderResponseParsing_should_read_body_correctly_when_content_length_header_present()
    {
        const string raw =
            "HTTP/1.0 200 OK\r\n" +
            "Content-Length: 13\r\n" +
            "\r\n" +
            "Hello, World!";

        var response = await DecodeAsync(raw);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello, World!", body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-6")]
    public async Task Http10DecoderResponseParsing_should_emit_exactly_one_response_when_connection_closes_after_body()
    {
        // HTTP/1.0 has no persistent connections; after the body the connection closes.
        // The decoder stage must emit exactly one response and then complete cleanly.
        const string raw =
            "HTTP/1.0 200 OK\r\n" +
            "Content-Type: text/plain\r\n" +
            "Content-Length: 5\r\n" +
            "\r\n" +
            "hello";

        var responses = await DecodeAllAsync(raw);

        Assert.Single(responses);
        var body = await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello", body);
    }
}
