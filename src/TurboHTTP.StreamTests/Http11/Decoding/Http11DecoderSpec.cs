using System.Net;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages.Decoding;
using TextEncoding = System.Text.Encoding;

namespace TurboHTTP.StreamTests.Http11.Decoding;

/// <summary>
/// Tests the HTTP/1.1 response decoder stage per RFC 9112.
/// Verifies that status lines, headers, content-length bodies, and chunked transfer encoding are correctly parsed.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http11DecoderStage"/>.
/// RFC 9112 §4–§7: HTTP/1.1 response message format, header parsing, and message body framing.
/// </remarks>
public sealed class Http11DecoderSpec : StreamTestBase
{
    private static IInputItem Chunk(string ascii)
    {
        var bytes = TextEncoding.Latin1.GetBytes(ascii);
        return NetworkBuffer.FromArray(bytes);
    }

    private async Task<HttpResponseMessage> DecodeAsync(params string[] chunks)
    {
        var source = Source.From(chunks.Select(Chunk));
        return await source
            .Via(Flow.FromGraph(new Http11DecoderStage()))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-4")]
    public async Task Http11Decoder_should_decode_status_line_when_http11_response()
    {
        var response = await DecodeAsync("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version11, response.Version);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-6.1")]
    public async Task Http11Decoder_should_decode_content_length_body_when_present()
    {
        var response = await DecodeAsync("HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello");

        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(5, body.Length);
        Assert.Equal("hello", TextEncoding.ASCII.GetString(body));
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-7.1")]
    public async Task Http11Decoder_should_decode_chunked_body_when_transfer_encoding_chunked()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n0\r\n\r\n");

        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello", TextEncoding.ASCII.GetString(body));
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-4")]
    public async Task Http11Decoder_should_decode_pipelined_responses_when_two_responses_in_stream()
    {
        var source = Source.From([
            Chunk("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\nHTTP/1.1 201 Created\r\nContent-Length: 0\r\n\r\n")
        ]);

        // Take(2) ensures the stream completes after collecting both responses
        // instead of waiting for upstream completion (which Sink.Seq requires).
        var responses = await source
            .Via(Flow.FromGraph(new Http11DecoderStage()))
            .Take(2)
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        Assert.Equal(2, responses.Count);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.Created, responses[1].StatusCode);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-4")]
    public async Task Http11Decoder_should_decode_response_header_when_present()
    {
        var response = await DecodeAsync("HTTP/1.1 200 OK\r\nX-Custom: myval\r\nContent-Length: 0\r\n\r\n");

        Assert.True(response.Headers.TryGetValues("X-Custom", out var values));
        Assert.Equal("myval", values.First());
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-6.1")]
    public async Task Http11Decoder_should_reassemble_fragmented_response_when_split_across_three_chunks()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhe",
            "ll",
            "o");

        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("hello", TextEncoding.ASCII.GetString(body));
    }
}
