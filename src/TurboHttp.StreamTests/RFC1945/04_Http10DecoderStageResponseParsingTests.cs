using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.RFC1945;

/// <summary>
/// RFC-tagged tests for the HTTP/1.0 response decoder stage per RFC 1945.
/// Verifies status code parsing, header field handling, and body framing as mandated by the specification.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http10DecoderStage"/>.
/// RFC 1945 §6: HTTP/1.0 response message format, status lines, and header fields.
/// </remarks>
public sealed class Http10DecoderStageResponseParsingTests : StreamTestBase
{
    private static IInputItem Chunk(string ascii)
    {
        var bytes = Encoding.Latin1.GetBytes(ascii);
        return new DataItem(new SimpleMemoryOwner(bytes), bytes.Length) { Key = RequestEndpoint.Default };
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

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-6.1-10DS-001: Status-line HTTP/1.0 200 OK → StatusCode=200, Version=1.0")]
    public async Task Should_ParseStatusCode200AndVersion10_When_StatusLineIs200OK()
    {
        var response = await DecodeAsync("HTTP/1.0 200 OK\r\n\r\n");

        Assert.Equal(200, (int)response.StatusCode);
        Assert.Equal(new Version(1, 0), response.Version);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-6.1-10DS-002: Status-line HTTP/1.0 404 Not Found → StatusCode=404")]
    public async Task Should_ParseStatusCode404_When_StatusLineIs404NotFound()
    {
        var response = await DecodeAsync("HTTP/1.0 404 Not Found\r\n\r\n");

        Assert.Equal(404, (int)response.StatusCode);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "RFC1945-6.2-10DS-003: Response headers Content-Type and Content-Length correctly parsed")]
    public async Task Should_ParseContentTypeAndContentLength_When_ResponseHeadersPresent()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-7-10DS-004: Body with Content-Length correctly read")]
    public async Task Should_ReadBodyCorrectly_When_ContentLengthHeaderPresent()
    {
        const string raw =
            "HTTP/1.0 200 OK\r\n" +
            "Content-Length: 13\r\n" +
            "\r\n" +
            "Hello, World!";

        var response = await DecodeAsync(raw);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello, World!", body);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "RFC1945-6-10DS-005: Connection-Close — stream ends after body, exactly 1 response emitted")]
    public async Task Should_EmitExactlyOneResponse_When_ConnectionClosesAfterBody()
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
        var body = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
    }
}