using System.Net;
using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages.Decoding;

namespace TurboHttp.StreamTests.RFC9112;

/// <summary>
/// Tests the HTTP/1.1 response decoder stage per RFC 9112.
/// Verifies that status lines, headers, content-length bodies, and chunked transfer encoding are correctly parsed.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http11DecoderStage"/>.
/// RFC 9112 §4–§7: HTTP/1.1 response message format, header parsing, and message body framing.
/// </remarks>
public sealed class Http11DecoderStageTests : StreamTestBase
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
            .Via(Flow.FromGraph(new Http11DecoderStage()))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-4-11DS-001: Status-Line decoded to StatusCode and Version11")]
    public async Task Should_DecodeStatusLine_WhenHttp11Response()
    {
        var response = await DecodeAsync("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version11, response.Version);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-6.1-11DS-002: Content-Length body decoded correctly")]
    public async Task Should_DecodeContentLengthBody_WhenPresent()
    {
        var response = await DecodeAsync("HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello");

        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(5, body.Length);
        Assert.Equal("hello", Encoding.ASCII.GetString(body));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-7.1-11DS-003: Chunked body decoded correctly")]
    public async Task Should_DecodeChunkedBody_WhenTransferEncodingChunked()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n0\r\n\r\n");

        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal("hello", Encoding.ASCII.GetString(body));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-4-11DS-004: Two pipelined responses decoded as two messages")]
    public async Task Should_DecodePipelinedResponses_WhenTwoResponsesInStream()
    {
        var source = Source.From([
            Chunk("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\nHTTP/1.1 201 Created\r\nContent-Length: 0\r\n\r\n")
        ]);

        var responses = await source
            .Via(Flow.FromGraph(new Http11DecoderStage()))
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        Assert.Equal(2, responses.Count);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.Created, responses[1].StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-4-11DS-005: Response header decoded to response.Headers")]
    public async Task Should_DecodeResponseHeader_WhenPresent()
    {
        var response = await DecodeAsync("HTTP/1.1 200 OK\r\nX-Custom: myval\r\nContent-Length: 0\r\n\r\n");

        Assert.True(response.Headers.TryGetValues("X-Custom", out var values));
        Assert.Equal("myval", values.First());
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-6.1-11DS-006: Response split across three TCP chunks reassembled")]
    public async Task Should_ReassembleFragmentedResponse_WhenSplitAcrossThreeChunks()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhe",
            "ll",
            "o");

        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("hello", Encoding.ASCII.GetString(body));
    }
}