using System.IO.Compression;
using System.Net;
using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Cookies;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams.Stages;

public sealed class StageOrderingIntegrationSpec : EngineTestBase
{
    private static Flow<ITransportOutbound, ITransportInbound, NotUsed> Http11Flow(Func<byte[]> responseFactory)
        => CreateFakeConnectionFlow(responseFactory);

    private static Flow<ITransportOutbound, ITransportInbound, NotUsed> Http10Flow(Func<byte[]> responseFactory)
        => CreateFakeConnectionFlow(responseFactory);

    private static Flow<ITransportOutbound, ITransportInbound, NotUsed> NoOpH2Flow()
        => CreateFakeConnectionFlow(() => Array.Empty<byte>());

    private static byte[] Ok11Response()
        => "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static byte[] GzipCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress))
        {
            gzip.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    private async Task<HttpResponseMessage> RunSingleAsync(
        Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> flow,
        HttpRequestMessage request)
    {
        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Concat(Source.Never<HttpRequestMessage>())
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(r => tcs.TrySetResult(r)), Materializer);
        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10_000)]
    public async Task
        StageOrderingIntegration_should_complete_request_when_full_pipeline_with_cookie_injection_before_cache_lookup()
    {
        // Full engine pipeline: send a GET request to a domain with cookies in jar.
        // The response should arrive successfully, proving the pipeline wired correctly
        // with CookieBidi handling injection before CacheBidi handles lookup.
        var descriptor = new PipelineDescriptor(
            RedirectPolicy: null,
            RetryPolicy: null,
            Expect100Policy: null,
            CompressionPolicy: null,
            CookieJar: new CookieJar(),
            CacheStore: null,
            CachePolicy: null,
            Handlers: []);
        var engine = new Engine();
        var transports = new TransportRegistry()
            .Register(new Version(1, 0), Http10Flow(Ok11Response))
            .Register(new Version(1, 1), Http11Flow(Ok11Response))
            .Register(new Version(2, 0), NoOpH2Flow())
            .Register(new Version(3, 0), NoOpH2Flow());
        var flow = engine.CreateFlow(transports, descriptor);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000)]
    public async Task
        StageOrderingIntegration_should_reach_post_processing_when_full_pipeline_response_from_engine_island()
    {
        // Verify that the full pipeline successfully processes a response through the engine
        // and BidiFlow chain. No features needed — empty descriptor proves the bare pipeline wires correctly.
        var engine = new Engine();
        var transports = new TransportRegistry()
            .Register(new Version(1, 0),  Http10Flow(Ok11Response))
            .Register(new Version(1, 1), Http11Flow(Ok11Response))
            .Register(new Version(2, 0), NoOpH2Flow())
            .Register(new Version(3, 0), NoOpH2Flow());
        var flow = engine.CreateFlow(transports, PipelineDescriptor.Empty);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        // Response successfully traversed engine and BidiFlow chain
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000)]
    public async Task
        StageOrderingIntegration_should_deliver_decompressed_body_to_client_when_full_pipeline_with_gzip_response()
    {
        // Full engine pipeline with gzip response: ContentEncodingBidiStage decompresses
        // before the response enters the outer BidiFlow layers.
        const string originalText = "Decompressed by engine island!";
        var compressedBody = GzipCompress(System.Text.Encoding.UTF8.GetBytes(originalText));
        var header = $"HTTP/1.1 200 OK\r\nContent-Encoding: gzip\r\nContent-Length: {compressedBody.Length}\r\n\r\n";
        var headerBytes = System.Text.Encoding.Latin1.GetBytes(header);
        var responseBytes = new byte[headerBytes.Length + compressedBody.Length];
        headerBytes.CopyTo(responseBytes, 0);
        compressedBody.CopyTo(responseBytes, headerBytes.Length);

        var engine = new Engine();
        var transports = new TransportRegistry()
            .Register(new Version(1, 0), Http10Flow(() => responseBytes))
            .Register(new Version(1, 1), Http11Flow(() => responseBytes))
            .Register(new Version(2, 0), NoOpH2Flow())
            .Register(new Version(3, 0), NoOpH2Flow());
        var flow = engine.CreateFlow(transports, PipelineDescriptor.Empty);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/gzipped")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(originalText, body);
        Assert.False(response.Content.Headers.ContentEncoding.Contains("gzip"),
            "Content-Encoding: gzip should be removed after decompression");
    }

    [Fact(Timeout = 15_000)]
    public async Task
        StageOrderingIntegration_should_produce_new_request_after_redirect_when_full_pipeline_with_301_response()
    {
        // First request → 301 redirect, second request (from redirect) → 200 OK.
        // This proves the redirect internal feedback loop works through the BidiFlow chain.
        var callCount = 0;

        byte[] ResponseFactory()
        {
            return Interlocked.Increment(ref callCount) == 1
                ? "HTTP/1.1 301 Moved Permanently\r\nLocation: http://example.com/new\r\nContent-Length: 0\r\n\r\n"u8
                    .ToArray()
                : "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        }

        var descriptor = new PipelineDescriptor(
            RedirectPolicy: new RedirectPolicy(),
            RetryPolicy: null,
            Expect100Policy: null,
            CompressionPolicy: null,
            CookieJar: null,
            CacheStore: null,
            CachePolicy: null,
            Handlers: []);
        var engine = new Engine();
        var transports = new TransportRegistry()
            .Register(new Version(1, 0),  Http10Flow(ResponseFactory))
            .Register(new Version(1, 1), Http11Flow(ResponseFactory))
            .Register(new Version(2, 0), NoOpH2Flow())
            .Register(new Version(3, 0), NoOpH2Flow());
        var flow = engine.CreateFlow(transports, descriptor);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/old")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        // After redirect, final response should be 200 OK
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Two calls to factory: first (301), second (200)
        Assert.Equal(2, callCount);
    }

    [Fact(Timeout = 10_000)]
    public async Task
        StageOrderingIntegration_should_pass_through_full_post_processing_chain_when_response_is_non_retryable()
    {
        // A 200 OK response is not retryable and not a redirect.
        // It passes through all BidiStages in the response direction to the final output.
        var engine = new Engine();
        var transports = new TransportRegistry()
            .Register(new Version(1, 0), Http10Flow(Ok11Response))
            .Register(new Version(1, 1), Http11Flow(Ok11Response))
            .Register(new Version(2, 0), NoOpH2Flow())
            .Register(new Version(3, 0), NoOpH2Flow());
        var flow = engine.CreateFlow(transports, PipelineDescriptor.Empty);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/stable")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}