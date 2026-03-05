using System.Buffers;
using System.Net;
using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http11;

public sealed class Http11DecoderStageTests : StreamTestBase
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
            .Via(Flow.FromGraph(new Http11DecoderStage()))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§4: Status-Line decoded to StatusCode and Version11")]
    public async Task ST_11_DEC_001_StatusLine_Decoded()
    {
        var response = await DecodeAsync("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version11, response.Version);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§6.1: Content-Length body decoded correctly")]
    public async Task ST_11_DEC_002_ContentLength_Body_Decoded()
    {
        var response = await DecodeAsync("HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello");

        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(5, body.Length);
        Assert.Equal("hello", Encoding.ASCII.GetString(body));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§7.1: Chunked body decoded correctly")]
    public async Task ST_11_DEC_003_ChunkedBody_Decoded()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n0\r\n\r\n");

        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal("hello", Encoding.ASCII.GetString(body));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§4: Two pipelined responses decoded as two messages")]
    public async Task ST_11_DEC_004_Pipelined_Responses_Decoded()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§4: Response header decoded to response.Headers")]
    public async Task ST_11_DEC_005_ResponseHeader_Decoded()
    {
        var response = await DecodeAsync("HTTP/1.1 200 OK\r\nX-Custom: myval\r\nContent-Length: 0\r\n\r\n");

        Assert.True(response.Headers.TryGetValues("X-Custom", out var values));
        Assert.Equal("myval", values.First());
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§6.1: Response split across three TCP chunks reassembled")]
    public async Task ST_11_DEC_006_Fragmented_ThreeChunks_Reassembled()
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
