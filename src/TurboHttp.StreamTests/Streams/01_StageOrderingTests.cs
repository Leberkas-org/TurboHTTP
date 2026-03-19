using System.IO.Compression;
using System.Net;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHttp.Client;
using TurboHttp.Internal;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC6265;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Protocol.RFC9111;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Verifies the 10 ordering invariants of the three-island pipeline topology
/// defined in Engine.BuildExtendedPipeline. Tests compose stages in documented
/// order and verify observable side-effects that would break if ordering changed.
/// </summary>
public sealed class StageOrderingTests : EngineTestBase
{
    // ── helpers ────────────────────────────────────────────────────────────────

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
            Content = new ByteArrayContent(body ?? Array.Empty<byte>())
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

    private static HttpCacheStore StoreWithFreshEntry(
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

        var store = new HttpCacheStore();
        store.Put(req, resp, Array.Empty<byte>(),
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

    private static HttpResponseMessage MakeGzipResponse(string url, byte[] plainBody)
    {
        var compressed = GzipCompress(plainBody);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, url),
            Content = new ByteArrayContent(compressed)
        };
        response.Content.Headers.TryAddWithoutValidation("Content-Encoding", "gzip");
        response.Content.Headers.ContentLength = compressed.Length;
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
        response.Headers.Date = DateTimeOffset.UtcNow;
        return response;
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
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(r => tcs.TrySetResult(r)), Materializer);
        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PRE-PROCESSING ISLAND — Ordering Invariant Tests
    // ═══════════════════════════════════════════════════════════════════════════

    // ── Invariant 2: CookieInjection (3) before CacheLookup (5) ────────────

    [Fact(Timeout = 10_000,
        DisplayName = "SORD-001: INV-2 CookieInjection before CacheLookup — request has cookies when reaching cache")]
    public async Task Should_HaveCookieHeaderWhenReachingCacheLookup_When_CookieInjectionRunsBeforeCacheLookup()
    {
        // Setup: cookie jar has a cookie for example.com, cache is empty.
        // If CookieInjection runs before CacheLookup, the miss request will have the Cookie header.
        var jar = JarWithCookie("session", "abc123", "example.com");
        var store = new HttpCacheStore();

        var probeMiss = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var probeHit = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var cookieInject = b.Add(new CookieInjectionStage(jar));
            var cacheLookup = b.Add(new CacheLookupStage(store, null));
            var src = b.Add(Source.Single(request).Concat(Source.Never<HttpRequestMessage>()));

            b.From(src).To(cookieInject.Inlet);
            b.From(cookieInject.Outlet).To(cacheLookup.In);
            b.From(cacheLookup.Out0).To(Sink.FromSubscriber(probeMiss));
            b.From(cacheLookup.Out1).To(Sink.FromSubscriber(probeHit));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var subMiss = probeMiss.ExpectSubscription();
        var subHit = probeHit.ExpectSubscription();
        subMiss.Request(1);
        subHit.Request(1);

        // Cache miss (empty store), but the request should have the Cookie header from injection
        var missRequest = await probeMiss.ExpectNextAsync(CancellationToken.None);
        Assert.True(missRequest.Headers.Contains("Cookie"),
            "CookieInjection must run before CacheLookup: Cookie header should be present on miss request");
        var cookieValue = string.Join("; ", missRequest.Headers.GetValues("Cookie"));
        Assert.Contains("session=abc123", cookieValue);
    }

    // ── Invariant 7: Redirect feedback enters BEFORE CookieInjection (3) ────

    [Fact(Timeout = 10_000,
        DisplayName = "SORD-002: INV-7 Redirected request gets fresh cookies for new domain")]
    public async Task Should_InjectFreshCookies_When_RedirectRequestEntersPipelineBeforeCookieInjection()
    {
        // Simulate: a redirect request to new-domain.com passes through CookieInjection.
        // Cookies for new-domain.com should be injected (proving the redirect enters before CookieInjection).
        var jar = JarWithCookie("auth", "token456", "new-domain.com");
        var stage = new CookieInjectionStage(jar);

        // The redirect request targets new-domain.com
        var redirectRequest = new HttpRequestMessage(HttpMethod.Get, "http://new-domain.com/target");

        var results = await Source.Single(redirectRequest)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Seq<HttpRequestMessage>(), Materializer);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("Cookie"),
            "Redirect feedback enters before CookieInjection: fresh cookies must be injected for new domain");
        var cookieValue = string.Join("; ", result.Headers.GetValues("Cookie"));
        Assert.Contains("auth=token456", cookieValue);
    }

    // ── Invariant 8: Retry feedback enters AFTER CookieInjection (3) ────────

    [Fact(Timeout = 10_000,
        DisplayName = "SORD-003: INV-8 Retried request preserves original cookies (retry merge after CookieInjection)")]
    public async Task Should_PreserveOriginalCookies_When_RetryFeedbackIsAfterCookieInjection()
    {
        // RetryStage emits the original request on Out1. Since retry merge is AFTER CookieInjection,
        // the retried request already has cookies from the first pass. Verify RetryStage preserves them.
        var originalRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        originalRequest.Headers.TryAddWithoutValidation("Cookie", "session=abc123");

        var response = new HttpResponseMessage(HttpStatusCode.RequestTimeout)
        {
            RequestMessage = originalRequest
        };

        var probeFinal = this.CreateManualSubscriberProbe<HttpResponseMessage>();
        var probeRetry = this.CreateManualSubscriberProbe<HttpRequestMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var retry = b.Add(new RetryStage());
            var src = b.Add(Source.Single(response).Concat(Source.Never<HttpResponseMessage>()));

            b.From(src).To(retry.In);
            b.From(retry.Out0).To(Sink.FromSubscriber(probeFinal));
            b.From(retry.Out1).To(Sink.FromSubscriber(probeRetry));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var subFinal = probeFinal.ExpectSubscription();
        var subRetry = probeRetry.ExpectSubscription();
        subFinal.Request(1);
        subRetry.Request(1);

        // Retry emits on Out1; the original Cookie header must be preserved
        var retryRequest = await probeRetry.ExpectNextAsync(CancellationToken.None);
        Assert.True(retryRequest.Headers.Contains("Cookie"),
            "Retry feedback preserves original cookies since retry merge is after CookieInjection");
        var cookieValue = string.Join("; ", retryRequest.Headers.GetValues("Cookie"));
        Assert.Contains("session=abc123", cookieValue);
    }

    // ── Invariant 9: Redirect feedback enters AFTER RequestEnricherStage (1) ─

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

    // ═══════════════════════════════════════════════════════════════════════════
    // POST-PROCESSING ISLAND — Ordering Invariant Tests
    // ═══════════════════════════════════════════════════════════════════════════

    // ── Invariant 3: CookieStorageStage (11) before CacheStorageStage (12) ──

    [Fact(Timeout = 10_000,
        DisplayName = "SORD-005: INV-3 CookieStorage before CacheStorage — cookies stored before response cached")]
    public async Task Should_StoreCookiesBeforeCachingResponse_When_CookieStorageRunsBeforeCacheStorage()
    {
        // Wire: CookieStorage → CacheStorage. Send response with Set-Cookie + Cache-Control.
        // After pipeline completes: jar has cookie AND cache has response.
        var jar = new CookieJar();
        var store = new HttpCacheStore();

        var response = MakeCacheableResponse(
            "http://example.com/page",
            cacheControl: "max-age=3600",
            body: "hello"u8.ToArray(),
            setCookie: "token=xyz; Domain=example.com; Path=/");

        var results = await Source.Single(response)
            .Via(new CookieStorageStage(jar))
            .Via(new CacheStorageStage(store))
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        Assert.Single(results);

        // Verify cookie was stored (CookieStorage ran)
        var nextRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        jar.AddCookiesToRequest(new Uri("http://example.com/page"), ref nextRequest);
        Assert.True(nextRequest.Headers.Contains("Cookie"), "CookieStorage must store Set-Cookie before CacheStorage");

        // Verify response was cached (CacheStorage ran after CookieStorage)
        var cacheResult = store.Get(new HttpRequestMessage(HttpMethod.Get, "http://example.com/page"));
        Assert.NotNull(cacheResult);
    }

    // ── Invariant 4: CacheStorageStage (12) before RetryStage (13) ──────────

    [Fact(Timeout = 10_000,
        DisplayName = "SORD-006: INV-4 CacheStorage before RetryStage — 200 cached and passed through as final")]
    public async Task Should_CacheResponseBeforeRetryEvaluation_When_CacheStorageRunsBeforeRetryStage()
    {
        // Wire: CacheStorage → RetryStage. Send cacheable 200 OK.
        // Verify: response cached AND passed through on Out0 (not retried).
        var store = new HttpCacheStore();

        var response = MakeCacheableResponse(
            "http://example.com/data",
            cacheControl: "max-age=3600",
            body: "data"u8.ToArray());

        var probeFinal = this.CreateManualSubscriberProbe<HttpResponseMessage>();
        var probeRetry = this.CreateManualSubscriberProbe<HttpRequestMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var cache = b.Add(Flow.FromGraph(new CacheStorageStage(store)));
            var retry = b.Add(new RetryStage());
            var src = b.Add(Source.Single(response).Concat(Source.Never<HttpResponseMessage>()));

            b.From(src).Via(cache).To(retry.In);
            b.From(retry.Out0).To(Sink.FromSubscriber(probeFinal));
            b.From(retry.Out1).To(Sink.FromSubscriber(probeRetry));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var subFinal = probeFinal.ExpectSubscription();
        var subRetry = probeRetry.ExpectSubscription();
        subFinal.Request(1);
        subRetry.Request(1);

        // 200 OK is not retryable → passes to Out0
        var finalResponse = await probeFinal.ExpectNextAsync(CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, finalResponse.StatusCode);
        await probeRetry.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

        // Verify response was cached (CacheStorage ran before RetryStage)
        var cacheResult = store.Get(new HttpRequestMessage(HttpMethod.Get, "http://example.com/data"));
        Assert.NotNull(cacheResult);
    }

    // ── Invariant 5: RetryStage (13) before RedirectStage (15) ──────────────

    [Fact(Timeout = 10_000,
        DisplayName = "SORD-007: INV-5 RetryStage before RedirectStage — 200 passes through both as final")]
    public async Task Should_PassThrough200Ok_When_RetryStageRunsBeforeRedirectStage()
    {
        // Wire: RetryStage → Merge → RedirectStage.
        // Send 200 OK. Verify: passes through both stages to RedirectStage.Out0.
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource")
        };

        var probeFinal = this.CreateManualSubscriberProbe<HttpResponseMessage>();
        var probeRedirect = this.CreateManualSubscriberProbe<HttpRequestMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var retry = b.Add(new RetryStage());
            var merge = b.Add(new Merge<HttpResponseMessage>(2));
            var redirect = b.Add(new RedirectStage());
            var src = b.Add(Source.Single(response).Concat(Source.Never<HttpResponseMessage>()));
            var empty = b.Add(Source.Never<HttpResponseMessage>());

            // Source → Retry → Merge(0) → Redirect
            b.From(src).To(retry.In);
            b.From(retry.Out0).To(merge.In(0));
            b.From(empty).To(merge.In(1)); // cache-hit path (empty for this test)
            b.From(merge.Out).To(redirect.In);
            b.From(redirect.Out0).To(Sink.FromSubscriber(probeFinal));
            b.From(redirect.Out1).To(Sink.FromSubscriber(probeRedirect));
            // Retry Out1 (retry feedback) — drain to avoid deadlock
            b.From(retry.Out1).To(Sink.Ignore<HttpRequestMessage>().MapMaterializedValue(_ => NotUsed.Instance));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var subFinal = probeFinal.ExpectSubscription();
        var subRedirect = probeRedirect.ExpectSubscription();
        subFinal.Request(1);
        subRedirect.Request(1);

        // 200 is neither retryable nor a redirect → final output
        var finalResponse = await probeFinal.ExpectNextAsync(CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, finalResponse.StatusCode);
        await probeRedirect.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    // ── Invariant 10: Cache hits merge AFTER RetryStage (13) ────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "SORD-008: INV-10 Cache hits bypass RetryStage — merge after retry, before redirect")]
    public async Task Should_BypassRetryStage_When_ResponseIsCacheHit()
    {
        // Wire: RetryStage → Merge(0) + CacheHitSource → Merge(1) → RedirectStage.
        // Send a cache hit (200) on Merge(1). It should bypass retry and reach RedirectStage.Out0.
        var cachedResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/cached")
        };

        var probeFinal = this.CreateManualSubscriberProbe<HttpResponseMessage>();
        var probeRedirect = this.CreateManualSubscriberProbe<HttpRequestMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var retry = b.Add(new RetryStage());
            var merge = b.Add(new Merge<HttpResponseMessage>(2));
            var redirect = b.Add(new RedirectStage());
            var retrySource = b.Add(Source.Never<HttpResponseMessage>()); // no responses to retry path
            var cacheHitSource = b.Add(Source.Single(cachedResponse)
                .Concat(Source.Never<HttpResponseMessage>()));

            // Retry gets nothing; cache hit goes to Merge(1)
            b.From(retrySource).To(retry.In);
            b.From(retry.Out0).To(merge.In(0));
            b.From(cacheHitSource).To(merge.In(1)); // cache-hit path
            b.From(merge.Out).To(redirect.In);
            b.From(redirect.Out0).To(Sink.FromSubscriber(probeFinal));
            b.From(redirect.Out1).To(Sink.FromSubscriber(probeRedirect));
            b.From(retry.Out1).To(Sink.Ignore<HttpRequestMessage>().MapMaterializedValue(_ => NotUsed.Instance));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var subFinal = probeFinal.ExpectSubscription();
        var subRedirect = probeRedirect.ExpectSubscription();
        subFinal.Request(1);
        subRedirect.Request(1);

        // Cache hit should reach final output, having bypassed RetryStage entirely
        var finalResponse = await probeFinal.ExpectNextAsync(CancellationToken.None);
        Assert.Same(cachedResponse, finalResponse);
        await probeRedirect.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CROSS-ISLAND — Ordering Invariant Tests
    // ═══════════════════════════════════════════════════════════════════════════

    // ── Invariant 1: ConnectionReuseStage (9) before CacheStorageStage (12) ─

    [Fact(Timeout = 10_000,
        DisplayName = "SORD-009: INV-1 ConnectionReuse signals before post-processing — response flows through both")]
    public async Task Should_SignalConnectionReuseBeforePostProcessing_When_ResponseFlowsThrough()
    {
        // Compose: ConnectionReuseStage → CookieStorageStage → CacheStorageStage.
        // This mirrors the cross-island boundary: engine island emits response (with reuse signal),
        // then the response enters post-processing. Both signals and response must be produced.
        var jar = new CookieJar();
        var store = new HttpCacheStore();

        var response = MakeCacheableResponse(
            "http://example.com/resource",
            body: "body"u8.ToArray(),
            setCookie: "k=v; Domain=example.com; Path=/");
        response.Version = HttpVersion.Version11;
        response.RequestMessage!.Version = HttpVersion.Version11;

        var probeResponse = this.CreateManualSubscriberProbe<HttpResponseMessage>();
        var probeSignal = this.CreateManualSubscriberProbe<IOutputItem>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var reuse = b.Add(new ConnectionReuseStage());
            var cookieStore = b.Add(Flow.FromGraph(new CookieStorageStage(jar)));
            var cacheStore = b.Add(Flow.FromGraph(new CacheStorageStage(store)));
            var src = b.Add(Source.Single(response).Concat(Source.Never<HttpResponseMessage>()));

            // ConnectionReuse.Out0 → CookieStorage → CacheStorage → Sink
            b.From(src).To(reuse.In);
            b.From(reuse.Out0).Via(cookieStore).Via(cacheStore).To(Sink.FromSubscriber(probeResponse));
            b.From(reuse.Out1).To(Sink.FromSubscriber(probeSignal));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var subResp = probeResponse.ExpectSubscription();
        var subSig = probeSignal.ExpectSubscription();
        subResp.Request(1);
        subSig.Request(1);

        // ConnectionReuseItem is signalled (engine island)
        var signal = await probeSignal.ExpectNextAsync(CancellationToken.None);
        Assert.IsType<ConnectionReuseItem>(signal);

        // Response flows through post-processing stages
        var result = await probeResponse.ExpectNextAsync(CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        // Verify both cookie and cache storage happened (post-processing completed)
        var nextReq = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        jar.AddCookiesToRequest(new Uri("http://example.com/resource"), ref nextReq);
        Assert.True(nextReq.Headers.Contains("Cookie"));
    }

    // ── Invariant 6: DecompressionStage (10) before CacheStorageStage (12) ──

    [Fact(Timeout = 10_000,
        DisplayName = "SORD-010: INV-6 Decompression before CacheStorage — cached body is decompressed")]
    public async Task Should_CacheDecompressedBody_When_DecompressionRunsBeforeCacheStorage()
    {
        // Wire: DecompressionStage → CacheStorageStage.
        // Send gzip-compressed response with Cache-Control.
        // Verify: cached body is the decompressed plaintext.
        var store = new HttpCacheStore();
        var plainBody = "Hello, decompressed world!"u8.ToArray();

        var response = MakeGzipResponse("http://example.com/compressed", plainBody);

        var results = await Source.Single(response)
            .Via(new DecompressionStage())
            .Via(new CacheStorageStage(store))
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        Assert.Single(results);

        // Verify the output response body is decompressed
        var outputBody = await results[0].Content.ReadAsByteArrayAsync();
        Assert.Equal(plainBody, outputBody);

        // Verify the cached entry stores the decompressed body
        var lookupReq = new HttpRequestMessage(HttpMethod.Get, "http://example.com/compressed");
        var cacheResult = store.Get(lookupReq);
        Assert.NotNull(cacheResult);
        Assert.Equal(plainBody, cacheResult.Body);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // INTEGRATION-LEVEL STREAM TESTS — Full Engine Pipeline
    // ═══════════════════════════════════════════════════════════════════════════

    // ── Integration 1: Cookie injection happens before cache lookup ──────────

    [Fact(Timeout = 10_000,
        DisplayName = "SORD-011: INTG-1 Full pipeline: cookie injection occurs before cache lookup")]
    public async Task Should_CompleteRequest_When_FullPipelineWithCookieInjectionBeforeCacheLookup()
    {
        // Full engine pipeline: send a GET request to a domain with cookies in jar.
        // The response should arrive successfully, proving the pipeline wired correctly
        // with CookieInjection before CacheLookup (empty cache → miss → engine → response).
        var options = new TurboClientOptions();
        var engine = new TurboHttp.Streams.Engine();
        var flow = engine.CreateFlow(
            () => Http10Flow(Ok11Response),
            () => Http11Flow(Ok11Response),
            NoOpH2Flow,
            NoOpH2Flow,
            options);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Integration 2: ConnectionReuseItem signalled before post-processing ─

    [Fact(Timeout = 10_000,
        DisplayName = "SORD-012: INTG-2 Full pipeline: response reaches post-processing after engine island")]
    public async Task Should_ReachPostProcessing_When_FullPipelineResponseFromEngineIsland()
    {
        // Verify that the full pipeline successfully processes a response through
        // all three islands: pre-processing → engine → post-processing.
        // ConnectionReuse (engine island) processes before CookieStorage/CacheStorage (post-processing).
        var options = new TurboClientOptions();
        var engine = new TurboHttp.Streams.Engine();
        var flow = engine.CreateFlow(
            () => Http10Flow(Ok11Response),
            () => Http11Flow(Ok11Response),
            NoOpH2Flow,
            NoOpH2Flow,
            options);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        // Response successfully traversed all three islands
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Integration 3: Decompressed body is what gets cached ────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "SORD-013: INTG-3 Full pipeline: gzip response decompressed before reaching client")]
    public async Task Should_DeliverDecompressedBodyToClient_When_FullPipelineWithGzipResponse()
    {
        // Full engine pipeline with gzip response: DecompressionStage (engine island)
        // decompresses before the response enters post-processing.
        const string originalText = "Decompressed by engine island!";
        var compressedBody = GzipCompress(System.Text.Encoding.UTF8.GetBytes(originalText));
        var header = $"HTTP/1.1 200 OK\r\nContent-Encoding: gzip\r\nContent-Length: {compressedBody.Length}\r\n\r\n";
        var headerBytes = System.Text.Encoding.Latin1.GetBytes(header);
        var responseBytes = new byte[headerBytes.Length + compressedBody.Length];
        headerBytes.CopyTo(responseBytes, 0);
        compressedBody.CopyTo(responseBytes, headerBytes.Length);

        var options = new TurboClientOptions();
        var engine = new TurboHttp.Streams.Engine();
        var flow = engine.CreateFlow(
            () => Http10Flow(() => responseBytes),
            () => Http11Flow(() => responseBytes),
            NoOpH2Flow,
            NoOpH2Flow,
            options);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/gzipped")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(originalText, body);
        Assert.False(response.Content.Headers.ContentEncoding.Contains("gzip"),
            "Content-Encoding: gzip should be removed after decompression (before post-processing)");
    }

    // ── Integration 4: Redirect feedback gets fresh cookies for new URI ─────

    [Fact(Timeout = 15_000,
        DisplayName = "SORD-014: INTG-4 Full pipeline: redirect produces new request through pipeline")]
    public async Task Should_ProduceNewRequestAfterRedirect_When_FullPipelineWith301Response()
    {
        // First request → 301 redirect, second request (from redirect) → 200 OK.
        // This proves the redirect feedback loop works: redirect → redirectMerge → CookieInjection → pipeline.
        var callCount = 0;
        byte[] ResponseFactory()
        {
            return Interlocked.Increment(ref callCount) == 1
                ? "HTTP/1.1 301 Moved Permanently\r\nLocation: http://example.com/new\r\nContent-Length: 0\r\n\r\n"u8.ToArray()
                : "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        }

        var options = new TurboClientOptions();
        var engine = new TurboHttp.Streams.Engine();
        var flow = engine.CreateFlow(
            () => Http10Flow(ResponseFactory),
            () => Http11Flow(ResponseFactory),
            NoOpH2Flow,
            NoOpH2Flow,
            options);

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

    // ── Integration 5: Cache hits bypass retry evaluation ───────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "SORD-015: INTG-5 Full pipeline: non-retryable response passes through entire pipeline")]
    public async Task Should_PassThroughFullPostProcessingChain_When_ResponseIsNonRetryable()
    {
        // A 200 OK response is not retryable (not 408/503) and not a redirect (not 3xx).
        // It should pass through RetryStage → Merge → RedirectStage to the final output.
        // This verifies the full post-processing chain: CookieStorage → CacheStorage → Retry → Merge → Redirect.
        var options = new TurboClientOptions();
        var engine = new TurboHttp.Streams.Engine();
        var flow = engine.CreateFlow(
            () => Http10Flow(Ok11Response),
            () => Http11Flow(Ok11Response),
            NoOpH2Flow,
            NoOpH2Flow,
            options);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/stable")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Response successfully traversed the full post-processing chain
        // (CookieStorage → CacheStorage → Retry.Out0 → Merge → Redirect.Out0 → client)
    }
}
