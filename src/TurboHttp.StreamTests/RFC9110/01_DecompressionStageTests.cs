using System.IO.Compression;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.RFC9110;

/// <summary>
/// Tests the content encoding decompression stage per RFC 9110.
/// Verifies that gzip, deflate, and brotli encoded response bodies are correctly decompressed.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="DecompressionStage"/>.
/// RFC 9110 §8.4: Content-Encoding header and transparent decompression of response bodies.
/// </remarks>
public sealed class DecompressionStageTests : StreamTestBase
{
    private async Task<IReadOnlyList<HttpResponseMessage>> RunAsync(
        params HttpResponseMessage[] responses)
    {
        return await Source.From(responses)
            .Via(Flow.FromGraph(new DecompressionStage()))
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);
    }

    private static byte[] GzipCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress))
        {
            gzip.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    private static byte[] DeflateCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionMode.Compress))
        {
            zlib.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    private static byte[] BrotliCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var br = new BrotliStream(output, CompressionMode.Compress))
        {
            br.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    private static HttpResponseMessage MakeResponse(byte[] body, string? contentEncoding = null)
    {
        var content = new ByteArrayContent(body);
        if (contentEncoding is not null)
        {
            content.Headers.TryAddWithoutValidation("Content-Encoding", contentEncoding);
        }
        return new HttpResponseMessage { Content = content };
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-8.4-DCMP-001: no Content-Encoding → response passes through unchanged")]
    public async Task Should_PassThroughUnchanged_When_NoContentEncoding()
    {
        var body = "hello world"u8.ToArray();
        var response = MakeResponse(body);

        var results = await RunAsync(response);

        var result = Assert.Single(results);
        var resultBody = await result.Content.ReadAsByteArrayAsync();
        Assert.Equal(body, resultBody);
        Assert.False(result.Content.Headers.Contains("Content-Encoding"));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-8.4-DCMP-002: Content-Encoding: identity → response passes through unchanged")]
    public async Task Should_PassThroughUnchanged_When_ContentEncodingIsIdentity()
    {
        var body = "hello world"u8.ToArray();
        var response = MakeResponse(body, "identity");

        var results = await RunAsync(response);

        var result = Assert.Single(results);
        var resultBody = await result.Content.ReadAsByteArrayAsync();
        Assert.Equal(body, resultBody);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-8.4-DCMP-003: Content-Encoding: gzip → body decompressed")]
    public async Task Should_Decompress_When_ContentEncodingIsGzip()
    {
        var original = "gzip compressed response body"u8.ToArray();
        var compressed = GzipCompress(original);
        var response = MakeResponse(compressed, "gzip");

        var results = await RunAsync(response);

        var result = Assert.Single(results);
        var resultBody = await result.Content.ReadAsByteArrayAsync();
        Assert.Equal(original, resultBody);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-8.4-DCMP-004: Content-Encoding: x-gzip → body decompressed")]
    public async Task Should_Decompress_When_ContentEncodingIsXGzip()
    {
        var original = "x-gzip content"u8.ToArray();
        var compressed = GzipCompress(original);
        var response = MakeResponse(compressed, "x-gzip");

        var results = await RunAsync(response);

        var result = Assert.Single(results);
        var resultBody = await result.Content.ReadAsByteArrayAsync();
        Assert.Equal(original, resultBody);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-8.4-DCMP-005: Content-Encoding: deflate → body decompressed")]
    public async Task Should_Decompress_When_ContentEncodingIsDeflate()
    {
        var original = "deflate compressed data"u8.ToArray();
        var compressed = DeflateCompress(original);
        var response = MakeResponse(compressed, "deflate");

        var results = await RunAsync(response);

        var result = Assert.Single(results);
        var resultBody = await result.Content.ReadAsByteArrayAsync();
        Assert.Equal(original, resultBody);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-8.4-DCMP-006: Content-Encoding: br → body decompressed")]
    public async Task Should_Decompress_When_ContentEncodingIsBrotli()
    {
        var original = "brotli compressed response"u8.ToArray();
        var compressed = BrotliCompress(original);
        var response = MakeResponse(compressed, "br");

        var results = await RunAsync(response);

        var result = Assert.Single(results);
        var resultBody = await result.Content.ReadAsByteArrayAsync();
        Assert.Equal(original, resultBody);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-8.4-DCMP-007: after decompression Content-Encoding header is removed")]
    public async Task Should_RemoveContentEncodingHeader_When_DecompressionApplied()
    {
        var original = "test body"u8.ToArray();
        var compressed = GzipCompress(original);
        var response = MakeResponse(compressed, "gzip");

        var results = await RunAsync(response);

        var result = Assert.Single(results);
        Assert.False(result.Content.Headers.Contains("Content-Encoding"));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-8.4-DCMP-008: after decompression Content-Length is updated to decompressed size")]
    public async Task Should_UpdateContentLength_When_DecompressionApplied()
    {
        var original = "content length test body"u8.ToArray();
        var compressed = GzipCompress(original);
        var response = MakeResponse(compressed, "gzip");

        var results = await RunAsync(response);

        var result = Assert.Single(results);
        Assert.Equal(original.Length, result.Content.Headers.ContentLength);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-8.4-DCMP-009: other content headers (Content-Type) preserved after decompression")]
    public async Task Should_PreserveContentTypeHeader_When_DecompressionApplied()
    {
        var original = "{\"key\":\"value\"}"u8.ToArray();
        var compressed = GzipCompress(original);
        var content = new ByteArrayContent(compressed);
        content.Headers.TryAddWithoutValidation("Content-Encoding", "gzip");
        content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
        var response = new HttpResponseMessage { Content = content };

        var results = await RunAsync(response);

        var result = Assert.Single(results);
        Assert.True(result.Content.Headers.Contains("Content-Type"));
        var contentType = string.Join("", result.Content.Headers.GetValues("Content-Type"));
        Assert.Contains("application/json", contentType);
    }

    [Fact(DisplayName = "RFC9110-8.4-DCMP-011: upstream failure → stage absorbs it, downstream not faulted")]
    public void Should_AbsorbUpstreamFailure_WhenUpstreamFails()
    {
        var publisher = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var subscriber = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        Source.FromPublisher(publisher)
            .Via(Flow.FromGraph(new DecompressionStage()))
            .RunWith(Sink.FromSubscriber(subscriber), Materializer);

        var pubSub = publisher.ExpectSubscription();
        var clientSub = subscriber.ExpectSubscription();
        clientSub.Request(10);

        // Fail upstream — stage must absorb, downstream must NOT see error
        pubSub.SendError(new Exception("upstream boom"));

        subscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-8.4-DCMP-010: multiple responses with different encodings all decompressed")]
    public async Task Should_DecompressAll_When_MultipleResponsesWithDifferentEncodings()
    {
        var body1 = "first response"u8.ToArray();
        var body2 = "second response"u8.ToArray();
        var body3 = "plain response"u8.ToArray();

        var resp1 = MakeResponse(GzipCompress(body1), "gzip");
        var resp2 = MakeResponse(BrotliCompress(body2), "br");
        var resp3 = MakeResponse(body3); // no encoding

        var results = await RunAsync(resp1, resp2, resp3);

        Assert.Equal(3, results.Count);
        Assert.Equal(body1, await results[0].Content.ReadAsByteArrayAsync());
        Assert.Equal(body2, await results[1].Content.ReadAsByteArrayAsync());
        Assert.Equal(body3, await results[2].Content.ReadAsByteArrayAsync());
    }
}
