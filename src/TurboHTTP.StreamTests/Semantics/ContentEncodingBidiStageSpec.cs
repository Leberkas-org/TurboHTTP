using System.Collections.Immutable;
using System.IO.Compression;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Semantics;

/// <summary>
/// Tests for the ContentEncodingBidiStage covering request compression and response decompression.
/// Verifies RFC 9110 §8.4 — Content-Encoding header handling and automatic compression/decompression.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="ContentEncodingBidiStage"/>.
/// RFC 9110 §8.4: Content-Encoding header and transparent decompression of response bodies.
/// Tests request direction compression with various policies and response direction decompression.
/// </remarks>
public sealed class ContentEncodingBidiStageSpec : StreamTestBase
{
    private Task<IImmutableList<HttpRequestMessage>> RunRequestAsync(
        ContentEncodingBidiStage stage,
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
        ContentEncodingBidiStage stage,
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

    // Compression helper methods

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
        using (var deflate = new ZLibStream(output, CompressionMode.Compress))
        {
            deflate.Write(data, 0, data.Length);
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

    // Request direction tests (compression)

    [Trait("RFC", "RFC9110-8.4")]
    [Fact(Timeout = 5000)]
    public async Task ContentEncodingBidiStage_should_pass_through_request_when_no_compression_policy()
    {
        var stage = new ContentEncodingBidiStage(automaticDecompression: false, compressionPolicy: null);
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(new byte[2000])
        };

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Same(request, result);
        Assert.False(result.Content?.Headers.Contains("Content-Encoding") ?? false);
    }

    [Trait("RFC", "RFC9110-8.4")]
    [Fact(Timeout = 5000)]
    public async Task ContentEncodingBidiStage_should_not_compress_when_body_below_threshold()
    {
        var policy = new CompressionPolicy { Encoding = "gzip", MinBodySizeBytes = 1000 };
        var stage = new ContentEncodingBidiStage(compressionPolicy: policy);
        var smallBody = new byte[500];
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(smallBody)
        };

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.False(result.Content?.Headers.Contains("Content-Encoding") ?? false);
    }

    [Trait("RFC", "RFC9110-8.4")]
    [Fact(Timeout = 5000)]
    public async Task ContentEncodingBidiStage_should_compress_gzip_when_body_exceeds_threshold()
    {
        var policy = new CompressionPolicy { Encoding = "gzip", MinBodySizeBytes = 1000 };
        var stage = new ContentEncodingBidiStage(compressionPolicy: policy);
        var largeBody = new byte[2000];
        for (var i = 0; i < largeBody.Length; i++)
        {
            largeBody[i] = (byte)(i % 256);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(largeBody)
        };

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Content?.Headers.Contains("Content-Encoding") ?? false);
        var encoding = string.Join("", result.Content!.Headers.ContentEncoding);
        Assert.Equal("gzip", encoding);
    }

    [Trait("RFC", "RFC9110-8.4")]
    [Fact(Timeout = 5000)]
    public async Task ContentEncodingBidiStage_should_compress_deflate_when_body_exceeds_threshold()
    {
        var policy = new CompressionPolicy { Encoding = "deflate", MinBodySizeBytes = 1000 };
        var stage = new ContentEncodingBidiStage(compressionPolicy: policy);
        var largeBody = new byte[2000];

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(largeBody)
        };

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        var encoding = string.Join("", result.Content!.Headers.ContentEncoding);
        Assert.Equal("deflate", encoding);
    }

    [Trait("RFC", "RFC9110-8.4")]
    [Fact(Timeout = 5000)]
    public async Task ContentEncodingBidiStage_should_compress_brotli_when_body_exceeds_threshold()
    {
        var policy = new CompressionPolicy { Encoding = "br", MinBodySizeBytes = 1000 };
        var stage = new ContentEncodingBidiStage(compressionPolicy: policy);
        var largeBody = new byte[2000];

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(largeBody)
        };

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        var encoding = string.Join("", result.Content!.Headers.ContentEncoding);
        Assert.Equal("br", encoding);
    }

    [Trait("RFC", "RFC9110-8.4")]
    [Fact(Timeout = 5000)]
    public async Task ContentEncodingBidiStage_should_not_compress_when_no_content()
    {
        var policy = new CompressionPolicy { Encoding = "gzip", MinBodySizeBytes = 1000 };
        var stage = new ContentEncodingBidiStage(compressionPolicy: policy);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Null(result.Content?.Headers.ContentEncoding);
    }

    [Trait("RFC", "RFC9110-8.4")]
    [Fact(Timeout = 5000)]
    public async Task ContentEncodingBidiStage_should_compress_at_exact_threshold()
    {
        var policy = new CompressionPolicy { Encoding = "gzip", MinBodySizeBytes = 1000 };
        var stage = new ContentEncodingBidiStage(compressionPolicy: policy);
        var exactBody = new byte[1000];

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(exactBody)
        };

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Content?.Headers.Contains("Content-Encoding") ?? false);
    }

    [Trait("RFC", "RFC9110-8.4")]
    [Fact(Timeout = 5000)]
    public async Task ContentEncodingBidiStage_should_handle_multiple_requests_with_mixed_sizes()
    {
        var policy = new CompressionPolicy { Encoding = "gzip", MinBodySizeBytes = 1000 };
        var stage = new ContentEncodingBidiStage(compressionPolicy: policy);
        var smallBody = new byte[500];
        var largeBody = new byte[2000];

        var req1 = new HttpRequestMessage(HttpMethod.Post, "http://example.com/a")
        {
            Content = new ByteArrayContent(smallBody)
        };
        var req2 = new HttpRequestMessage(HttpMethod.Post, "http://example.com/b")
        {
            Content = new ByteArrayContent(largeBody)
        };

        var results = await RunRequestAsync(stage, req1, req2);

        Assert.Equal(2, results.Count);
        Assert.False(results[0].Content?.Headers.Contains("Content-Encoding") ?? false);
        Assert.True(results[1].Content?.Headers.Contains("Content-Encoding") ?? false);
    }

    [Trait("RFC", "RFC9110-8.4")]
    [Fact(Timeout = 5000)]
    public async Task ContentEncodingBidiStage_should_preserve_other_content_headers_when_compressing()
    {
        var policy = new CompressionPolicy { Encoding = "gzip", MinBodySizeBytes = 1000 };
        var stage = new ContentEncodingBidiStage(compressionPolicy: policy);
        var largeBody = new byte[2000];

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(largeBody)
        };
        request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
        request.Content.Headers.TryAddWithoutValidation("X-Custom-Header", "test-value");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Content?.Headers.Contains("Content-Type") ?? false);
        Assert.True(result.Content?.Headers.Contains("X-Custom-Header") ?? false);
        Assert.True(result.Content?.Headers.Contains("Content-Encoding") ?? false);
    }

    // Response direction tests (decompression)

    [Trait("RFC", "RFC9110-8.4")]
    [Fact(Timeout = 5000)]
    public async Task ContentEncodingBidiStage_should_pass_through_response_when_no_content_encoding()
    {
        var stage = new ContentEncodingBidiStage(automaticDecompression: true);
        var body = "hello world"u8.ToArray();
        var response = MakeResponse(body);

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        var resultBody = await result.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(body, resultBody);
        Assert.False(result.Content.Headers.Contains("Content-Encoding"));
    }

    [Trait("RFC", "RFC9110-8.4")]
    [Fact(Timeout = 5000)]
    public async Task ContentEncodingBidiStage_should_pass_through_response_when_decompression_disabled()
    {
        var stage = new ContentEncodingBidiStage(automaticDecompression: false);
        var original = "gzip data"u8.ToArray();
        var compressed = GzipCompress(original);
        var response = MakeResponse(compressed, "gzip");

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        var resultBody = await result.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(compressed, resultBody);
        Assert.True(result.Content.Headers.Contains("Content-Encoding"));
    }

    [Trait("RFC", "RFC9110-8.4")]
    [Fact(Timeout = 5000)]
    public async Task ContentEncodingBidiStage_should_decompress_gzip_response()
    {
        var stage = new ContentEncodingBidiStage(automaticDecompression: true);
        var original = "gzip compressed response body"u8.ToArray();
        var compressed = GzipCompress(original);
        var response = MakeResponse(compressed, "gzip");

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        var resultBody = await result.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(original, resultBody);
    }

    [Trait("RFC", "RFC9110-8.4")]
    [Fact(Timeout = 5000)]
    public async Task ContentEncodingBidiStage_should_decompress_x_gzip_response()
    {
        var stage = new ContentEncodingBidiStage(automaticDecompression: true);
        var original = "x-gzip content"u8.ToArray();
        var compressed = GzipCompress(original);
        var response = MakeResponse(compressed, "x-gzip");

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        var resultBody = await result.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(original, resultBody);
    }

    [Trait("RFC", "RFC9110-8.4")]
    [Fact(Timeout = 5000)]
    public async Task ContentEncodingBidiStage_should_decompress_deflate_response()
    {
        var stage = new ContentEncodingBidiStage(automaticDecompression: true);
        var original = "deflate compressed data"u8.ToArray();
        var compressed = DeflateCompress(original);
        var response = MakeResponse(compressed, "deflate");

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        var resultBody = await result.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(original, resultBody);
    }

    [Trait("RFC", "RFC9110-8.4")]
    [Fact(Timeout = 5000)]
    public async Task ContentEncodingBidiStage_should_decompress_brotli_response()
    {
        var stage = new ContentEncodingBidiStage(automaticDecompression: true);
        var original = "brotli compressed response"u8.ToArray();
        var compressed = BrotliCompress(original);
        var response = MakeResponse(compressed, "br");

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        var resultBody = await result.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(original, resultBody);
    }

    [Trait("RFC", "RFC9110-8.4")]
    [Fact(Timeout = 5000)]
    public async Task ContentEncodingBidiStage_should_remove_content_encoding_header_after_decompression()
    {
        var stage = new ContentEncodingBidiStage(automaticDecompression: true);
        var original = "test body"u8.ToArray();
        var compressed = GzipCompress(original);
        var response = MakeResponse(compressed, "gzip");

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        Assert.False(result.Content.Headers.Contains("Content-Encoding"));
    }

    [Trait("RFC", "RFC9110-8.4")]
    [Fact(Timeout = 5000)]
    public async Task ContentEncodingBidiStage_should_preserve_content_type_after_decompression()
    {
        var stage = new ContentEncodingBidiStage(automaticDecompression: true);
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

    [Trait("RFC", "RFC9110-8.4")]
    [Fact(Timeout = 5000)]
    public async Task ContentEncodingBidiStage_should_handle_identity_encoding_as_pass_through()
    {
        var stage = new ContentEncodingBidiStage(automaticDecompression: true);
        var body = "identity encoded"u8.ToArray();
        var response = MakeResponse(body, "identity");

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        var resultBody = await result.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(body, resultBody);
    }

    [Trait("RFC", "RFC9110-8.4")]
    [Fact(Timeout = 5000)]
    public async Task ContentEncodingBidiStage_should_handle_multiple_responses_with_different_encodings()
    {
        var stage = new ContentEncodingBidiStage(automaticDecompression: true);
        var body1 = "first response"u8.ToArray();
        var body2 = "second response"u8.ToArray();
        var body3 = "plain response"u8.ToArray();

        var resp1 = MakeResponse(GzipCompress(body1), "gzip");
        var resp2 = MakeResponse(BrotliCompress(body2), "br");
        var resp3 = MakeResponse(body3);

        var results = await RunResponseAsync(stage, resp1, resp2, resp3);

        Assert.Equal(3, results.Count);
        Assert.Equal(body1, await results[0].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(body2, await results[1].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(body3, await results[2].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }
}
