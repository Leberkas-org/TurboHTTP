using System.Net;
using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.IO.Stages;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.RFC1945;

public sealed class Http10DecoderStageTests : StreamTestBase
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

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-6.1-10DS-001: Status-Line decoded to StatusCode and Version")]
    public async Task ST_10_DEC_001_StatusLine_Decoded()
    {
        var response = await DecodeAsync("HTTP/1.0 200 OK\r\n\r\n");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version10, response.Version);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-6.2-10DS-002: Response header decoded to response.Headers")]
    public async Task ST_10_DEC_002_ResponseHeader_Decoded()
    {
        var response = await DecodeAsync("HTTP/1.0 200 OK\r\nX-Custom: test\r\n\r\n");

        Assert.True(response.Headers.TryGetValues("X-Custom", out var values));
        Assert.Equal("test", values.First());
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-7-10DS-003: Body delimited by Content-Length decoded correctly")]
    public async Task ST_10_DEC_003_ContentLength_Body_Decoded()
    {
        var response = await DecodeAsync("HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhello");

        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(5, body.Length);
        Assert.Equal("hello", Encoding.ASCII.GetString(body));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-6.1-10DS-004: 404 response decoded to HttpStatusCode.NotFound")]
    public async Task ST_10_DEC_004_NotFound_StatusCode()
    {
        var response = await DecodeAsync("HTTP/1.0 404 Not Found\r\n\r\n");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-7-10DS-005: Response split across two TCP chunks reassembled")]
    public async Task ST_10_DEC_005_Fragmented_Reassembled()
    {
        // Body split: first chunk has partial body ("he"), second chunk has remainder ("llo")
        var response = await DecodeAsync("HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhe", "llo");

        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("hello", Encoding.ASCII.GetString(body));
    }
}