using System.Buffers;
using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http10;

/// <summary>
/// RFC 1945 §6.1 — Status-Line, §4.2 — Headers compliance tests for Http10DecoderStage.
/// </summary>
public sealed class Http10DecoderStageRfcTests : StreamTestBase
{
    private static (IMemoryOwner<byte>, int) Chunk(string ascii)
    {
        var bytes = Encoding.Latin1.GetBytes(ascii);
        return (new SimpleMemoryOwner(bytes), bytes.Length);
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

    [Fact(Timeout = 10_000, DisplayName = "10D-RFC-001: Status-line HTTP/1.0 200 OK → StatusCode=200, Version=1.0")]
    public async Task _10D_RFC_001_StatusLine_200OK()
    {
        var response = await DecodeAsync("HTTP/1.0 200 OK\r\n\r\n");

        Assert.Equal(200, (int)response.StatusCode);
        Assert.Equal(new Version(1, 0), response.Version);
    }

    [Fact(Timeout = 10_000, DisplayName = "10D-RFC-002: Status-line HTTP/1.0 404 Not Found → StatusCode=404")]
    public async Task _10D_RFC_002_StatusLine_404NotFound()
    {
        var response = await DecodeAsync("HTTP/1.0 404 Not Found\r\n\r\n");

        Assert.Equal(404, (int)response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "10D-RFC-003: Response headers Content-Type and Content-Length correctly parsed")]
    public async Task _10D_RFC_003_ResponseHeaders_Parsed()
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

    [Fact(Timeout = 10_000, DisplayName = "10D-RFC-004: Body with Content-Length correctly read")]
    public async Task _10D_RFC_004_Body_ContentLength_Read()
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

    [Fact(Timeout = 10_000, DisplayName = "10D-RFC-005: Connection-Close — stream ends after body, exactly 1 response emitted")]
    public async Task _10D_RFC_005_ConnectionClose_StreamEndsAfterBody()
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
