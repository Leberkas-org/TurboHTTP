using System.IO.Compression;
using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Streams;

public sealed class DecompressionStageTests : StreamTestBase
{
    // ── helpers ────────────────────────────────────────────────────────────────

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

    // ── pass-through cases ─────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "DECO-001: no Content-Encoding → response passes through unchanged")]
    public async Task DECO_001_NoEncoding_PassThrough()
    {
        var body = "hello world"u8.ToArray();
        var response = MakeResponse(body);

        var results = await RunAsync(response);

        var result = Assert.Single(results);
        var resultBody = await result.Content.ReadAsByteArrayAsync();
        Assert.Equal(body, resultBody);
        Assert.False(result.Content.Headers.Contains("Content-Encoding"));
    }

    [Fact(Timeout = 10_000, DisplayName = "DECO-002: Content-Encoding: identity → response passes through unchanged")]
    public async Task DECO_002_IdentityEncoding_PassThrough()
    {
        var body = "hello world"u8.ToArray();
        var response = MakeResponse(body, "identity");

        var results = await RunAsync(response);

        var result = Assert.Single(results);
        var resultBody = await result.Content.ReadAsByteArrayAsync();
        Assert.Equal(body, resultBody);
    }

    // ── gzip decompression ─────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "DECO-003: Content-Encoding: gzip → body decompressed")]
    public async Task DECO_003_Gzip_Decompressed()
    {
        var original = "gzip compressed response body"u8.ToArray();
        var compressed = GzipCompress(original);
        var response = MakeResponse(compressed, "gzip");

        var results = await RunAsync(response);

        var result = Assert.Single(results);
        var resultBody = await result.Content.ReadAsByteArrayAsync();
        Assert.Equal(original, resultBody);
    }

    [Fact(Timeout = 10_000, DisplayName = "DECO-004: Content-Encoding: x-gzip → body decompressed")]
    public async Task DECO_004_XGzip_Decompressed()
    {
        var original = "x-gzip content"u8.ToArray();
        var compressed = GzipCompress(original);
        var response = MakeResponse(compressed, "x-gzip");

        var results = await RunAsync(response);

        var result = Assert.Single(results);
        var resultBody = await result.Content.ReadAsByteArrayAsync();
        Assert.Equal(original, resultBody);
    }

    // ── deflate decompression ──────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "DECO-005: Content-Encoding: deflate → body decompressed")]
    public async Task DECO_005_Deflate_Decompressed()
    {
        var original = "deflate compressed data"u8.ToArray();
        var compressed = DeflateCompress(original);
        var response = MakeResponse(compressed, "deflate");

        var results = await RunAsync(response);

        var result = Assert.Single(results);
        var resultBody = await result.Content.ReadAsByteArrayAsync();
        Assert.Equal(original, resultBody);
    }

    // ── brotli decompression ───────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "DECO-006: Content-Encoding: br → body decompressed")]
    public async Task DECO_006_Brotli_Decompressed()
    {
        var original = "brotli compressed response"u8.ToArray();
        var compressed = BrotliCompress(original);
        var response = MakeResponse(compressed, "br");

        var results = await RunAsync(response);

        var result = Assert.Single(results);
        var resultBody = await result.Content.ReadAsByteArrayAsync();
        Assert.Equal(original, resultBody);
    }

    // ── header management ──────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "DECO-007: after decompression Content-Encoding header is removed")]
    public async Task DECO_007_ContentEncoding_RemovedAfterDecompression()
    {
        var original = "test body"u8.ToArray();
        var compressed = GzipCompress(original);
        var response = MakeResponse(compressed, "gzip");

        var results = await RunAsync(response);

        var result = Assert.Single(results);
        Assert.False(result.Content.Headers.Contains("Content-Encoding"));
    }

    [Fact(Timeout = 10_000, DisplayName = "DECO-008: after decompression Content-Length is updated to decompressed size")]
    public async Task DECO_008_ContentLength_UpdatedAfterDecompression()
    {
        var original = "content length test body"u8.ToArray();
        var compressed = GzipCompress(original);
        var response = MakeResponse(compressed, "gzip");

        var results = await RunAsync(response);

        var result = Assert.Single(results);
        Assert.Equal(original.Length, result.Content.Headers.ContentLength);
    }

    [Fact(Timeout = 10_000, DisplayName = "DECO-009: other content headers (Content-Type) preserved after decompression")]
    public async Task DECO_009_OtherContentHeaders_Preserved()
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

    // ── multiple responses ─────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "DECO-010: multiple responses with different encodings all decompressed")]
    public async Task DECO_010_MultipleResponses_AllDecompressed()
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
