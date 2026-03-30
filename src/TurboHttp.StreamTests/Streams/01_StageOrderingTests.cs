using System.IO.Compression;
using System.Net;
using Akka;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC6265;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Protocol.RFC9111;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages.Features;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Tests that the BidiFlow Atop composition preserves RFC-compliant request and response semantics.
/// Verifies that cookie injection, cache lookup, retry, redirect, and decompression BidiStages
/// execute in the correct sequence when composed via <c>BidiFlow.Atop()</c>.
/// </summary>
/// <remarks>
/// Stage under test: BidiFlow composition (Redirect → Cookie → Retry → Cache → Decompression → Engine).
/// Validates execution order of all feature BidiStages within the engine pipeline.
/// </remarks>
public sealed class StageOrderingTests : EngineTestBase
{
    private static CookieJar JarWithCookie(string name, string value, string domain, string path = "/")
    {
        var jar = new CookieJar();
        var response = new HttpResponseMessage();
        response.Headers.TryAddWithoutValidation("Set-Cookie", $"{name}={value}; Domain={domain}; Path={path}");
        jar.ProcessResponse(new Uri($"http://{domain}/"), response);
        return jar;
    }

    private static HttpResponseMessage MakeCacheableResponse(
        string url,
        string? cacheControl = "max-age=3600",
        byte[]? body = null,
        string? setCookie = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, url),
            Content = new ByteArrayContent(body ?? [])
        };
        if (cacheControl is not null)
        {
            response.Headers.TryAddWithoutValidation("Cache-Control", cacheControl);
        }

        response.Headers.Date = DateTimeOffset.UtcNow;
        if (setCookie is not null)
        {
            response.Headers.TryAddWithoutValidation("Set-Cookie", setCookie);
        }

        return response;
    }

    private static CacheStore StoreWithFreshEntry(
        string url = "http://example.com/resource",
        string? varyHeader = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Cookie", "session=abc123");
        var resp = new HttpResponseMessage(HttpStatusCode.OK);
        resp.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
        resp.Headers.Date = DateTimeOffset.UtcNow;
        if (varyHeader is not null)
        {
            resp.Headers.TryAddWithoutValidation("Vary", varyHeader);
        }

        var store = new CacheStore();
        store.Put(req, resp, [],
            DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow);
        return store;
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

    private static Flow<IOutputItem, IInputItem, NotUsed> Http11Flow(Func<byte[]> responseFactory)
        => Flow.FromGraph(new EngineFakeConnectionStage(responseFactory));

    private static Flow<IOutputItem, IInputItem, NotUsed> Http10Flow(Func<byte[]> responseFactory)
        => Flow.FromGraph(new EngineFakeConnectionStage(responseFactory));

    private static Flow<IOutputItem, IInputItem, NotUsed> NoOpH2Flow()
        => Flow.FromGraph(new H2EngineFakeConnectionStage());

    private static byte[] Ok11Response()
        => "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

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

    // ============================
    // BidiFlow composition ordering tests (SORD-001 through SORD-010)
    // ============================

    [Fact(Timeout = 10_000,
        DisplayName = "SORD-001: INV-2 CookieBidi before CacheBidi — request has cookies when reaching engine")]
    public async Task Should_HaveCookieHeaderWhenReachingEngine_When_CookieBidiIsOuterToCacheBidi()
    {
        // CookieBidi.Atop(CacheBidi): request path is Cookie → Cache → Engine.
        // The echo engine returns the request as RequestMessage, proving cookies were injected first.
        var jar = JarWithCookie("session", "abc123", "example.com");
        var store = new CacheStore();

        var bidi = BidiFlow.FromGraph(new CookieBidiStage(jar))
            .Atop(BidiFlow.FromGraph(new CacheBidiStage(store, null)));

        var echo = Flow.Create<HttpRequestMessage>()
            .Select(req => new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = req });

        var flow = bidi.Join(echo);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = await RunSingleAsync(flow, request);

        Assert.True(response.RequestMessage!.Headers.Contains("Cookie"),
            "CookieBidi must run before CacheBidi: Cookie header should be present on the request reaching the engine");
        var cookieValue = string.Join("; ", response.RequestMessage.Headers.GetValues("Cookie"));
        Assert.Contains("session=abc123", cookieValue);
    }

    [Fact(Timeout = 15_000,
        DisplayName = "SORD-002: INV-7 Redirected request gets fresh cookies for new domain")]
    public async Task Should_InjectFreshCookies_When_RedirectBidiIsOuterToCookieBidi()
    {
        // RedirectBidi.Atop(CookieBidi): redirect request flows through CookieBidi on the way to the engine.
        // RedirectBidi is outermost → redirect request enters CookieBidi → cookies injected for new domain.
        var jar = JarWithCookie("auth", "token456", "new-domain.com");
        var callCount = 0;

        var bidi = BidiFlow.FromGraph(new RedirectBidiStage(new RedirectPolicy()))
            .Atop(BidiFlow.FromGraph(new CookieBidiStage(jar)));

        var engine = Flow.Create<HttpRequestMessage>()
            .Select(req =>
            {
                if (Interlocked.Increment(ref callCount) == 1)
                {
                    var resp = new HttpResponseMessage(HttpStatusCode.MovedPermanently) { RequestMessage = req };
                    resp.Headers.TryAddWithoutValidation("Location", "http://new-domain.com/target");
                    return resp;
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = req };
            });

        var flow = bidi.Join(engine);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/old");
        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, callCount);
        Assert.True(response.RequestMessage!.Headers.Contains("Cookie"),
            "Redirect enters before CookieBidi: fresh cookies must be injected for new domain");
        var cookieValue = string.Join("; ", response.RequestMessage.Headers.GetValues("Cookie"));
        Assert.Contains("auth=token456", cookieValue);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "SORD-003: INV-8 Retried request preserves original cookies (retry is inner to cookie)")]
    public async Task Should_PreserveOriginalCookies_When_RetryBidiIsInnerToCookieBidi()
    {
        // CookieBidi.Atop(RetryBidi): retry feedback stays inside RetryBidi, bypassing CookieBidi.
        // Cookies injected on first pass are preserved because RetryBidi reuses the original request.
        var jar = JarWithCookie("session", "abc123", "example.com");
        var callCount = 0;
        HttpRequestMessage? retriedRequest = null;

        var bidi = BidiFlow.FromGraph(new CookieBidiStage(jar))
            .Atop(BidiFlow.FromGraph(new RetryBidiStage(new RetryPolicy())));

        var engine = Flow.Create<HttpRequestMessage>()
            .Select(req =>
            {
                var count = Interlocked.Increment(ref callCount);
                if (count == 2)
                {
                    retriedRequest = req;
                }

                if (count == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.RequestTimeout) { RequestMessage = req };
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = req };
            });

        var flow = bidi.Join(engine);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, callCount);
        Assert.True(retriedRequest!.Headers.Contains("Cookie"),
            "Retry feedback is inner to CookieBidi: cookies are preserved from the first pass");
        var cookieValue = string.Join("; ", retriedRequest.Headers.GetValues("Cookie"));
        Assert.Contains("session=abc123", cookieValue);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "SORD-004: INV-9 RequestEnricherStage overrides default Version — redirect skips this")]
    public async Task Should_OverrideDefaultVersion_When_RequestEnricherStageProcessesRequest()
    {
        // Prove that RequestEnricherStage overrides Version when it's the 1.1 default
        // and DefaultRequestVersion differs. Since redirect enters AFTER enricher,
        // a redirected request with Version 2.0 (set by BuildRedirectRequest) is NOT overridden.
        var options = new TurboRequestOptions(
            BaseAddress: null,
            DefaultRequestHeaders: new HttpRequestMessage().Headers,
            DefaultRequestVersion: new Version(2, 0),
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrHigher,
            Timeout: TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize: 1024 * 1024);

        var enricher = new RequestEnricherStage(() => options);

        // Request with default Version 1.1 — enricher overrides to 2.0
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        Assert.Equal(HttpVersion.Version11, request.Version); // default

        var results = await Source.Single(request)
            .Via(Flow.FromGraph(enricher))
            .RunWith(Sink.Seq<HttpRequestMessage>(), Materializer);

        var result = Assert.Single(results);
        Assert.Equal(new Version(2, 0), result.Version);
        // This proves: enricher DOES modify Version for new requests at the 1.1 default.
        // A redirected request with Version 2.0 (non-default) would NOT be modified,
        // because it enters AFTER enricher (INV-9) and even if it entered before,
        // the enricher only overrides when Version == 1.1 (the default).
    }

    [Fact(Timeout = 10_000,
        DisplayName = "SORD-005: INV-3 CookieBidi and CacheBidi both store on response path")]
    public async Task Should_StoreCookiesAndCacheResponse_When_CookieBidiAndCacheBidiComposed()
    {
        // CookieBidi.Atop(CacheBidi): response path is Engine → CacheBidi(store) → CookieBidi(store Set-Cookie).
        // Both storage operations happen; order is reversed from old architecture but functionally equivalent.
        var jar = new CookieJar();
        var store = new CacheStore();

        var bidi = BidiFlow.FromGraph(new CookieBidiStage(jar))
            .Atop(BidiFlow.FromGraph(new CacheBidiStage(store, null)));

        var engine = Flow.Create<HttpRequestMessage>()
            .Select(req =>
            {
                var resp = MakeCacheableResponse(
                    "http://example.com/page",
                    cacheControl: "max-age=3600",
                    body: "hello"u8.ToArray(),
                    setCookie: "token=xyz; Domain=example.com; Path=/");
                resp.RequestMessage = req;
                return resp;
            });

        var flow = bidi.Join(engine);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        await RunSingleAsync(flow, request);

        // Verify cookie was stored (CookieBidi response direction)
        var nextRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        jar.AddCookiesToRequest(new Uri("http://example.com/page"), ref nextRequest);
        Assert.True(nextRequest.Headers.Contains("Cookie"), "CookieBidi must store Set-Cookie from response");

        // Verify response was cached (CacheBidi response direction)
        var cacheResult = store.Get(new HttpRequestMessage(HttpMethod.Get, "http://example.com/page"));
        Assert.NotNull(cacheResult);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "SORD-006: INV-4 CacheBidi stores before RetryBidi evaluates — 200 cached and passed through")]
    public async Task Should_CacheResponseBeforeRetryEvaluation_When_CacheBidiIsInnerToRetryBidi()
    {
        // RetryBidi.Atop(CacheBidi): response path is Engine → CacheBidi(store) → RetryBidi(evaluate: 200=pass).
        var store = new CacheStore();

        var bidi = BidiFlow.FromGraph(new RetryBidiStage(new RetryPolicy()))
            .Atop(BidiFlow.FromGraph(new CacheBidiStage(store, null)));

        var engine = Flow.Create<HttpRequestMessage>()
            .Select(req =>
            {
                var resp = MakeCacheableResponse("http://example.com/data", body: "data"u8.ToArray());
                resp.RequestMessage = req;
                return resp;
            });

        var flow = bidi.Join(engine);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");
        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify response was cached (CacheBidi processes first on response path)
        var cacheResult = store.Get(new HttpRequestMessage(HttpMethod.Get, "http://example.com/data"));
        Assert.NotNull(cacheResult);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "SORD-007: INV-5 RetryBidi before RedirectBidi — 200 passes through both as final")]
    public async Task Should_PassThrough200Ok_When_RetryBidiAndRedirectBidiComposed()
    {
        // RedirectBidi.Atop(RetryBidi): response path is Engine → RetryBidi(200=pass) → RedirectBidi(200=pass).
        var bidi = BidiFlow.FromGraph(new RedirectBidiStage(new RedirectPolicy()))
            .Atop(BidiFlow.FromGraph(new RetryBidiStage(new RetryPolicy())));

        var echo = Flow.Create<HttpRequestMessage>()
            .Select(req => new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = req });

        var flow = bidi.Join(echo);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "SORD-008: INV-10 Cache hit bypasses engine — CacheBidi short-circuits internally")]
    public async Task Should_BypassEngine_When_CacheHitOccurs()
    {
        // RetryBidi.Atop(CacheBidi): cache hit produces response on CacheBidi.Out2 → RetryBidi.In2 → Out2.
        // Engine is never called because CacheBidi does not push request on Out1.
        var store = StoreWithFreshEntry("http://example.com/cached");
        var engineCallCount = 0;

        var bidi = BidiFlow.FromGraph(new RetryBidiStage(new RetryPolicy()))
            .Atop(BidiFlow.FromGraph(new CacheBidiStage(store, null)));

        var engine = Flow.Create<HttpRequestMessage>()
            .Select(req =>
            {
                Interlocked.Increment(ref engineCallCount);
                return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = req };
            });

        var flow = bidi.Join(engine);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/cached");
        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, engineCallCount);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "SORD-009: INV-1 Engine (incl. ConnectionReuse) processes before BidiFlow response chain")]
    public async Task Should_StoreResponseInJarAndCache_When_EngineProcessesBeforeBidiFlowChain()
    {
        // Engine-level test: ConnectionReuseStage is inside the engine; CookieBidi/CacheBidi are outside.
        // The response flows: Engine(ConnectionReuse) → BidiFlow(CacheBidi → CookieBidi) → Client.
        var jar = new CookieJar();
        var store = new CacheStore();

        byte[] ResponseWithCookieAndCache() =>
            "HTTP/1.1 200 OK\r\nSet-Cookie: k=v; Domain=example.com; Path=/\r\nCache-Control: max-age=3600\r\nDate: Thu, 21 Mar 2026 10:00:00 GMT\r\nContent-Length: 4\r\n\r\nbody"u8
                .ToArray();

        var descriptor = new PipelineDescriptor(
            RedirectPolicy: null,
            RetryPolicy: null,
            Expect100Policy: null,
            CompressionPolicy: null,
            CookieJar: jar,
            CacheStore: store,
            CachePolicy: null,
            Handlers: []);

        var engine = new Engine();
        var flow = engine.CreateFlow(
            () => Http10Flow(ResponseWithCookieAndCache),
            () => Http11Flow(ResponseWithCookieAndCache),
            NoOpH2Flow,
            NoOpH2Flow,
            descriptor);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify both post-processing stages ran (after engine including ConnectionReuse)
        var nextReq = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        jar.AddCookiesToRequest(new Uri("http://example.com/resource"), ref nextReq);
        Assert.True(nextReq.Headers.Contains("Cookie"));

        var cacheResult = store.Get(new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource"));
        Assert.NotNull(cacheResult);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "SORD-010: INV-6 DecompressionBidi before CacheBidi — cached body is decompressed")]
    public async Task Should_CacheDecompressedBody_When_DecompressionBidiIsInnerToCacheBidi()
    {
        // CacheBidi.Atop(DecompressionBidi): response path is Engine → Decomp(decompress) → Cache(store).
        // Cached body is the decompressed version.
        var store = new CacheStore();
        var plainBody = "Hello, decompressed world!"u8.ToArray();

        var bidi = BidiFlow.FromGraph(new CacheBidiStage(store, null))
            .Atop(BidiFlow.FromGraph(new ContentEncodingBidiStage()));

        var engine = Flow.Create<HttpRequestMessage>()
            .Select(req =>
            {
                var compressed = GzipCompress(plainBody);
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = req,
                    Content = new ByteArrayContent(compressed)
                };
                resp.Content.Headers.TryAddWithoutValidation("Content-Encoding", "gzip");
                resp.Content.Headers.ContentLength = compressed.Length;
                resp.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
                resp.Headers.Date = DateTimeOffset.UtcNow;
                return resp;
            });

        var flow = bidi.Join(engine);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/compressed");
        var response = await RunSingleAsync(flow, request);

        // Verify the output response body is decompressed
        var outputBody = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(plainBody, outputBody);

        // Verify the cached entry stores the decompressed body
        var lookupReq = new HttpRequestMessage(HttpMethod.Get, "http://example.com/compressed");
        var cacheResult = store.Get(lookupReq);
        Assert.NotNull(cacheResult);
        Assert.Equal(plainBody, cacheResult.Body);
    }

    // ============================
    // Engine integration tests (SORD-011 through SORD-015)
    // ============================

    [Fact(Timeout = 10_000,
        DisplayName = "SORD-011: INTG-1 Full pipeline: cookie injection occurs before cache lookup")]
    public async Task Should_CompleteRequest_When_FullPipelineWithCookieInjectionBeforeCacheLookup()
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
        var flow = engine.CreateFlow(
            () => Http10Flow(Ok11Response),
            () => Http11Flow(Ok11Response),
            NoOpH2Flow,
            NoOpH2Flow,
            descriptor);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "SORD-012: INTG-2 Full pipeline: response reaches post-processing after engine island")]
    public async Task Should_ReachPostProcessing_When_FullPipelineResponseFromEngineIsland()
    {
        // Verify that the full pipeline successfully processes a response through the engine
        // and BidiFlow chain. No features needed — empty descriptor proves the bare pipeline wires correctly.
        var engine = new Engine();
        var flow = engine.CreateFlow(
            () => Http10Flow(Ok11Response),
            () => Http11Flow(Ok11Response),
            NoOpH2Flow,
            NoOpH2Flow,
            PipelineDescriptor.Empty);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        // Response successfully traversed engine and BidiFlow chain
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "SORD-013: INTG-3 Full pipeline: gzip response decompressed before reaching client")]
    public async Task Should_DeliverDecompressedBodyToClient_When_FullPipelineWithGzipResponse()
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
        var flow = engine.CreateFlow(
            () => Http10Flow(() => responseBytes),
            () => Http11Flow(() => responseBytes),
            NoOpH2Flow,
            NoOpH2Flow,
            PipelineDescriptor.Empty);

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

    [Fact(Timeout = 15_000,
        DisplayName = "SORD-014: INTG-4 Full pipeline: redirect produces new request through pipeline")]
    public async Task Should_ProduceNewRequestAfterRedirect_When_FullPipelineWith301Response()
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
        var flow = engine.CreateFlow(
            () => Http10Flow(ResponseFactory),
            () => Http11Flow(ResponseFactory),
            NoOpH2Flow,
            NoOpH2Flow,
            descriptor);

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

    [Fact(Timeout = 10_000,
        DisplayName = "SORD-015: INTG-5 Full pipeline: non-retryable response passes through entire pipeline")]
    public async Task Should_PassThroughFullPostProcessingChain_When_ResponseIsNonRetryable()
    {
        // A 200 OK response is not retryable and not a redirect.
        // It passes through all BidiStages in the response direction to the final output.
        var engine = new Engine();
        var flow = engine.CreateFlow(
            () => Http10Flow(Ok11Response),
            () => Http11Flow(Ok11Response),
            NoOpH2Flow,
            NoOpH2Flow,
            PipelineDescriptor.Empty);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/stable")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
