using System.Net;
using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages.Decoding;

namespace TurboHttp.StreamTests.Http10;

/// <summary>
/// Tests the HTTP/1.0 response decoder stage per RFC 1945.
/// Verifies that status lines, headers, and bodies are correctly parsed from byte streams.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http10DecoderStage"/>.
/// RFC 1945 §6: HTTP/1.0 response message format and parsing.
/// </remarks>
public sealed class Http10DecoderSpec : StreamTestBase
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-6.1")]
    public async Task Http10Decoder_should_decode_status_line_to_status_code_and_version_when_valid_response()
    {
        var response = await DecodeAsync("HTTP/1.0 200 OK\r\n\r\n");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version10, response.Version);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-6.2")]
    public async Task Http10Decoder_should_decode_response_header_to_response_headers_when_header_present()
    {
        var response = await DecodeAsync("HTTP/1.0 200 OK\r\nX-Custom: test\r\n\r\n");

        Assert.True(response.Headers.TryGetValues("X-Custom", out var values));
        Assert.Equal("test", values.First());
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-7")]
    public async Task Http10Decoder_should_decode_body_correctly_when_delimited_by_content_length()
    {
        var response = await DecodeAsync("HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhello");

        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(5, body.Length);
        Assert.Equal("hello", Encoding.ASCII.GetString(body));
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-6.1")]
    public async Task Http10Decoder_should_decode_404_to_not_found_when_status_code_is_404()
    {
        var response = await DecodeAsync("HTTP/1.0 404 Not Found\r\n\r\n");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-7")]
    public async Task Http10Decoder_should_reassemble_fragmented_response_when_split_across_two_chunks()
    {
        // Body split: first chunk has partial body ("he"), second chunk has remainder ("llo")
        var response = await DecodeAsync("HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhe", "llo");

        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("hello", Encoding.ASCII.GetString(body));
    }
}
