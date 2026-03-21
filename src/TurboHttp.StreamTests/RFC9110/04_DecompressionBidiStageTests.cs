using System.Collections.Immutable;
using System.IO.Compression;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages.Features;

namespace TurboHttp.StreamTests.RFC9110;

/// <summary>
/// Tests the bidirectional decompression stage per RFC 9110.
/// Verifies that the request direction passes messages through unchanged and the response
/// direction decompresses gzip/deflate/brotli bodies, removes Content-Encoding, and updates Content-Length.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="DecompressionBidiStage"/>.
/// RFC 9110 §8.4: Content-Encoding header and transparent decompression of response bodies.
/// </remarks>
public sealed class DecompressionBidiStageTests : StreamTestBase
{
    private Task<IImmutableList<HttpRequestMessage>> RunRequestAsync(
        DecompressionBidiStage stage,
        params HttpRequestMessage[] requests)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpRequestMessage>(),
            (builder, sink) =>
            {
                var bidi = builder.Add(stage);
                var source = builder.Add(Source.From(requests));
                var emptyResponseSource = builder.Add(Source.Empty<HttpResponseMessage>());
                var ignoredResponseSink = builder.Add(Sink.Ignore<HttpResponseMessage>());

                builder.From(source).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).To(sink);
                builder.From(emptyResponseSource).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(ignoredResponseSink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

    private Task<IImmutableList<HttpResponseMessage>> RunResponseAsync(
        DecompressionBidiStage stage,
        params HttpResponseMessage[] responses)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpResponseMessage>(),
            (builder, sink) =>
            {
                var bidi = builder.Add(stage);
                var source = builder.Add(Source.From(responses));
                var emptyRequestSource = builder.Add(Source.Empty<HttpRequestMessage>());
                var ignoredRequestSink = builder.Add(Sink.Ignore<HttpRequestMessage>());

                builder.From(emptyRequestSource).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).To(ignoredRequestSink);
                builder.From(source).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(sink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
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

    // ============================
    // Request direction tests (pass-through)
    // ============================

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-8.4-DBIDI-001: request passes through unchanged")]
    public async Task RequestDirection_Should_PassThrough()
    {
        var stage = new DecompressionBidiStage();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Custom", "test-value");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Same(request, result);
        Assert.Equal(HttpMethod.Get, result.Method);
        Assert.True(result.Headers.Contains("X-Custom"));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-8.4-DBIDI-002: multiple requests all pass through unchanged")]
    public async Task RequestDirection_Should_PassThroughAll_ForMultipleRequests()
    {
        var stage = new DecompressionBidiStage();
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var req2 = new HttpRequestMessage(HttpMethod.Post, "http://example.com/b");

        var results = new List<HttpRequestMessage>(await RunRequestAsync(stage, req1, req2));

        Assert.Equal(2, results.Count);
        Assert.Same(req1, results[0]);
        Assert.Same(req2, results[1]);
    }

    // ============================
    // Response direction tests (decompression)
    // ============================

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-8.4-DBIDI-003: no Content-Encoding → response passes through unchanged")]
    public async Task ResponseDirection_Should_PassThrough_When_NoContentEncoding()
    {
        var stage = new DecompressionBidiStage();
        var body = "hello world"u8.ToArray();
        var response = MakeResponse(body);

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        var resultBody = await result.Content.ReadAsByteArrayAsync();
        Assert.Equal(body, resultBody);
        Assert.False(result.Content.Headers.Contains("Content-Encoding"));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-8.4-DBIDI-004: Content-Encoding: identity → response passes through unchanged")]
    public async Task ResponseDirection_Should_PassThrough_When_Identity()
    {
        var stage = new DecompressionBidiStage();
        var body = "hello world"u8.ToArray();
        var response = MakeResponse(body, "identity");

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        var resultBody = await result.Content.ReadAsByteArrayAsync();
        Assert.Equal(body, resultBody);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-8.4-DBIDI-005: Content-Encoding: gzip → body decompressed")]
    public async Task ResponseDirection_Should_Decompress_Gzip()
    {
        var stage = new DecompressionBidiStage();
        var original = "gzip compressed response body"u8.ToArray();
        var compressed = GzipCompress(original);
        var response = MakeResponse(compressed, "gzip");

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        var resultBody = await result.Content.ReadAsByteArrayAsync();
        Assert.Equal(original, resultBody);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-8.4-DBIDI-006: Content-Encoding: x-gzip → body decompressed")]
    public async Task ResponseDirection_Should_Decompress_XGzip()
    {
        var stage = new DecompressionBidiStage();
        var original = "x-gzip content"u8.ToArray();
        var compressed = GzipCompress(original);
        var response = MakeResponse(compressed, "x-gzip");

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        var resultBody = await result.Content.ReadAsByteArrayAsync();
        Assert.Equal(original, resultBody);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-8.4-DBIDI-007: Content-Encoding: deflate → body decompressed")]
    public async Task ResponseDirection_Should_Decompress_Deflate()
    {
        var stage = new DecompressionBidiStage();
        var original = "deflate compressed data"u8.ToArray();
        var compressed = DeflateCompress(original);
        var response = MakeResponse(compressed, "deflate");

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        var resultBody = await result.Content.ReadAsByteArrayAsync();
        Assert.Equal(original, resultBody);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-8.4-DBIDI-008: Content-Encoding: br → body decompressed")]
    public async Task ResponseDirection_Should_Decompress_Brotli()
    {
        var stage = new DecompressionBidiStage();
        var original = "brotli compressed response"u8.ToArray();
        var compressed = BrotliCompress(original);
        var response = MakeResponse(compressed, "br");

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        var resultBody = await result.Content.ReadAsByteArrayAsync();
        Assert.Equal(original, resultBody);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-8.4-DBIDI-009: Content-Encoding header removed after decompression")]
    public async Task ResponseDirection_Should_RemoveContentEncodingHeader()
    {
        var stage = new DecompressionBidiStage();
        var original = "test body"u8.ToArray();
        var compressed = GzipCompress(original);
        var response = MakeResponse(compressed, "gzip");

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        Assert.False(result.Content.Headers.Contains("Content-Encoding"));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-8.4-DBIDI-010: Content-Length updated to decompressed size")]
    public async Task ResponseDirection_Should_UpdateContentLength()
    {
        var stage = new DecompressionBidiStage();
        var original = "content length test body"u8.ToArray();
        var compressed = GzipCompress(original);
        var response = MakeResponse(compressed, "gzip");

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        Assert.Equal(original.Length, result.Content.Headers.ContentLength);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-8.4-DBIDI-011: Content-Type preserved after decompression")]
    public async Task ResponseDirection_Should_PreserveContentType()
    {
        var stage = new DecompressionBidiStage();
        var original = "{\"key\":\"value\"}"u8.ToArray();
        var compressed = GzipCompress(original);
        var content = new ByteArrayContent(compressed);
        content.Headers.TryAddWithoutValidation("Content-Encoding", "gzip");
        content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
        var response = new HttpResponseMessage { Content = content };

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        Assert.True(result.Content.Headers.Contains("Content-Type"));
        var contentType = string.Join("", result.Content.Headers.GetValues("Content-Type"));
        Assert.Contains("application/json", contentType);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-8.4-DBIDI-012: multiple responses with different encodings all decompressed")]
    public async Task ResponseDirection_Should_DecompressAll_DifferentEncodings()
    {
        var stage = new DecompressionBidiStage();
        var body1 = "first response"u8.ToArray();
        var body2 = "second response"u8.ToArray();
        var body3 = "plain response"u8.ToArray();

        var resp1 = MakeResponse(GzipCompress(body1), "gzip");
        var resp2 = MakeResponse(BrotliCompress(body2), "br");
        var resp3 = MakeResponse(body3);

        var results = await RunResponseAsync(stage, resp1, resp2, resp3);

        Assert.Equal(3, results.Count);
        Assert.Equal(body1, await results[0].Content.ReadAsByteArrayAsync());
        Assert.Equal(body2, await results[1].Content.ReadAsByteArrayAsync());
        Assert.Equal(body3, await results[2].Content.ReadAsByteArrayAsync());
    }
}
