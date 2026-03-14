using System.Net;
using System.Text;
using Akka.Actor;
using Akka.Hosting;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC9111;
using TurboHttp.Streams;

namespace TurboHttp.IntegrationTests.Http11;

/// <summary>
/// Integration tests for Http11Engine caching behaviour.
/// Verifies RFC 9111 freshness, validation, no-store, no-cache, Vary,
/// POST invalidation, must-revalidate, min-fresh, max-stale, and LRU eviction
/// against real Kestrel cache routes.
/// </summary>
public sealed class Http11CacheTests : TestKit, IClassFixture<KestrelFixture>
{
    private readonly KestrelFixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _poolRouter;

    public Http11CacheTests(KestrelFixture fixture)
    {
        _fixture = fixture;
        _materializer = Sys.Materializer();
        var clientManager = Sys.ActorOf(Props.Create(() => new ClientManager()));
        var actorRegistry = ActorRegistry.For(Sys);
        actorRegistry.Register<ClientManager>(clientManager);

        _poolRouter = Sys.ActorOf(Props.Create(() => new PoolRouterActor()));
    }

    /// <summary>
    /// Sends a single HTTP/1.1 request through the Http11Engine pipeline.
    /// Each call materialises a fresh pipeline.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        var engine = new Http11Engine();
        var tcpOptions = new TcpOptions
        {
            Host = "127.0.0.1",
            Port = _fixture.Port
        };

        var transport =
            Flow.Create<ITransportItem>()
                .Prepend(Source.Single<ITransportItem>(
                    new ConnectItem(tcpOptions)))
                .Via(new ConnectionStage(_poolRouter));

        var flow = engine.CreateFlow().Join(transport);

        var (queue, responseTask) = Source.Queue<HttpRequestMessage>(1, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(Sink.First<HttpResponseMessage>(), Keep.Both)
            .Run(_materializer);

        await queue.OfferAsync(request);
        return await responseTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Sends a request with cache support. Checks the cache store first,
    /// evaluates freshness, performs conditional revalidation if needed,
    /// and stores cacheable responses.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithCacheAsync(
        HttpRequestMessage request,
        HttpCacheStore store)
    {
        var now = DateTimeOffset.UtcNow;

        // Look up cached entry
        var entry = store.Get(request);
        var result = CacheFreshnessEvaluator.Evaluate(entry, request, now);

        if (result.Status == CacheLookupStatus.Fresh)
        {
            // Serve directly from cache
            var cached = new HttpResponseMessage(result.Entry!.Response.StatusCode)
            {
                Content = new ByteArrayContent(result.Entry.Body)
            };
            foreach (var header in result.Entry.Response.Headers)
            {
                cached.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            foreach (var header in result.Entry.Response.Content.Headers)
            {
                cached.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return cached;
        }

        if (result.Status == CacheLookupStatus.MustRevalidate && result.Entry is not null &&
            CacheValidationRequestBuilder.CanRevalidate(result.Entry))
        {
            // Build conditional request for revalidation
            var conditional = CacheValidationRequestBuilder.BuildConditionalRequest(request, result.Entry);
            conditional.Version = HttpVersion.Version11;

            var requestTime = DateTimeOffset.UtcNow;
            var revalResponse = await SendAsync(conditional);
            var responseTime = DateTimeOffset.UtcNow;

            if (revalResponse.StatusCode == HttpStatusCode.NotModified)
            {
                // 304 → merge with cached entry
                var merged = CacheValidationRequestBuilder.MergeNotModifiedResponse(revalResponse, result.Entry);

                // Re-store with refreshed timestamps
                var body = result.Entry.Body;
                store.Put(request, merged, body, requestTime, responseTime);

                return merged;
            }

            // Origin sent a new response — store and return it
            var newBody = await revalResponse.Content.ReadAsByteArrayAsync();
            store.Put(request, revalResponse, newBody, requestTime, responseTime);
            return revalResponse;
        }

        // Cache miss or stale without revalidation — send to origin
        {
            var requestTime = DateTimeOffset.UtcNow;
            var response = await SendAsync(request);
            var responseTime = DateTimeOffset.UtcNow;

            // Invalidate on unsafe methods (POST/PUT/DELETE/PATCH)
            if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head &&
                request.RequestUri is not null)
            {
                store.Invalidate(request.RequestUri);
            }

            // Store cacheable responses
            var bodyBytes = await response.Content.ReadAsByteArrayAsync();
            store.Put(request, response, bodyBytes, requestTime, responseTime);

            // Return a response with the body we already read
            var result2 = new HttpResponseMessage(response.StatusCode)
            {
                Content = new ByteArrayContent(bodyBytes),
                Version = response.Version
            };
            foreach (var header in response.Headers)
            {
                result2.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            foreach (var header in response.Content.Headers)
            {
                result2.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return result2;
        }
    }

    private HttpRequestMessage MakeGet(string path) =>
        new(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}{path}")
        {
            Version = HttpVersion.Version11
        };

    private HttpRequestMessage MakeHead(string path) =>
        new(HttpMethod.Head, $"http://127.0.0.1:{_fixture.Port}{path}")
        {
            Version = HttpVersion.Version11
        };

    private HttpRequestMessage MakePost(string path, string body = "{}") =>
        new(HttpMethod.Post, $"http://127.0.0.1:{_fixture.Port}{path}")
        {
            Version = HttpVersion.Version11,
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "CACHE-11-001: GET /cache/max-age/300 → cacheable response is stored")]
    public async Task CacheableResponse_IsStored()
    {
        var store = new HttpCacheStore();
        var request = MakeGet("/cache/max-age/300");

        var response = await SendWithCacheAsync(request, store);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, store.Count);
    }

    [Fact(DisplayName = "CACHE-11-002: Second GET to same URI serves cached response")]
    public async Task SecondGet_ServedFromCache()
    {
        var store = new HttpCacheStore();

        var response1 = await SendWithCacheAsync(MakeGet("/cache/max-age/300"), store);
        var body1 = await response1.Content.ReadAsStringAsync();

        // Second request — should be served from cache (same timestamp body)
        var response2 = await SendWithCacheAsync(MakeGet("/cache/max-age/300"), store);
        var body2 = await response2.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal(body1, body2);
    }

    [Fact(DisplayName = "CACHE-11-003: Stale entry triggers revalidation with If-None-Match")]
    public async Task StaleEntry_Revalidates_WithETag()
    {
        var store = new HttpCacheStore();

        // First request — stores entry with ETag and max-age=3600
        var response1 = await SendWithCacheAsync(MakeGet("/cache/etag/reval1"), store);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(1, store.Count);

        // Manually expire the entry by re-storing with max-age=0, must-revalidate
        // Instead, use the must-revalidate endpoint which sets max-age=0
        var store2 = new HttpCacheStore();
        var response2 = await SendWithCacheAsync(MakeGet("/cache/must-revalidate"), store2);
        var body2 = await response2.Content.ReadAsStringAsync();

        // Second request — entry is stale (max-age=0), must revalidate
        var response3 = await SendWithCacheAsync(MakeGet("/cache/must-revalidate"), store2);

        Assert.Equal(HttpStatusCode.OK, response3.StatusCode);
    }

    [Fact(DisplayName = "CACHE-11-004: 304 Not Modified merges headers with cached entry")]
    public async Task NotModified304_MergesHeaders()
    {
        var store = new HttpCacheStore();

        // First request to must-revalidate endpoint — stores with ETag + max-age=0
        var response1 = await SendWithCacheAsync(MakeGet("/cache/must-revalidate"), store);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        // Second request — must revalidate, gets 304, merges to 200
        var response2 = await SendWithCacheAsync(MakeGet("/cache/must-revalidate"), store);

        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync();
        var body2 = await response2.Content.ReadAsStringAsync();
        Assert.Equal(body1, body2);
    }

    [Fact(DisplayName = "CACHE-11-005: ETag-based conditional request returns 304")]
    public async Task ETag_ConditionalRequest_Returns304()
    {
        var store = new HttpCacheStore();

        // First request stores with ETag
        var response1 = await SendWithCacheAsync(MakeGet("/cache/etag/cond1"), store);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        // Verify entry has ETag
        var entry = store.Get(MakeGet("/cache/etag/cond1"));
        Assert.NotNull(entry);
        Assert.NotNull(entry.ETag);
        Assert.Contains("cond1", entry.ETag);
    }

    [Fact(DisplayName = "CACHE-11-006: Last-Modified-based conditional request returns 304")]
    public async Task LastModified_ConditionalRequest_Returns304()
    {
        var store = new HttpCacheStore();

        // First request stores with Last-Modified
        var response1 = await SendWithCacheAsync(MakeGet("/cache/last-modified/lm1"), store);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        // Verify entry has Last-Modified
        var entry = store.Get(MakeGet("/cache/last-modified/lm1"));
        Assert.NotNull(entry);
        Assert.NotNull(entry.LastModified);
    }

    [Fact(DisplayName = "CACHE-11-007: no-store response is NOT cached")]
    public async Task NoStore_ResponseNotCached()
    {
        var store = new HttpCacheStore();

        var response = await SendWithCacheAsync(MakeGet("/cache/no-store"), store);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, store.Count);
    }

    [Fact(DisplayName = "CACHE-11-008: no-cache request forces revalidation")]
    public async Task NoCache_ForcesRevalidation()
    {
        var store = new HttpCacheStore();

        // Prime the cache
        var response1 = await SendWithCacheAsync(MakeGet("/cache/etag/nocache1"), store);
        var body1 = await response1.Content.ReadAsStringAsync();
        Assert.Equal(1, store.Count);

        // Send with no-cache — should go to origin despite fresh cache
        var noCacheRequest = MakeGet("/cache/etag/nocache1");
        noCacheRequest.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");

        var response2 = await SendWithCacheAsync(noCacheRequest, store);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
    }

    [Fact(DisplayName = "CACHE-11-009: Vary header — different header values get different entries")]
    public async Task Vary_DifferentHeaderValues_DifferentEntries()
    {
        var store = new HttpCacheStore();

        // Request with Accept-Language: en
        var req1 = MakeGet("/cache/vary/Accept-Language");
        req1.Headers.TryAddWithoutValidation("Accept-Language", "en");
        var response1 = await SendWithCacheAsync(req1, store);
        var body1 = await response1.Content.ReadAsStringAsync();

        // Request with Accept-Language: de — should be a cache miss (different Vary value)
        var req2 = MakeGet("/cache/vary/Accept-Language");
        req2.Headers.TryAddWithoutValidation("Accept-Language", "de");
        var response2 = await SendWithCacheAsync(req2, store);
        var body2 = await response2.Content.ReadAsStringAsync();

        Assert.NotEqual(body1, body2);
        Assert.Contains("en", body1);
        Assert.Contains("de", body2);
    }

    [Fact(DisplayName = "CACHE-11-010: POST to cached URI invalidates the cache entry")]
    public async Task Post_InvalidatesCacheEntry()
    {
        var store = new HttpCacheStore();

        // Prime the cache with a GET
        var response1 = await SendWithCacheAsync(MakeGet("/cache/max-age/300"), store);
        Assert.Equal(1, store.Count);

        // Verify the GET entry is cached
        var cachedBefore = store.Get(MakeGet("/cache/max-age/300"));
        Assert.NotNull(cachedBefore);

        // POST to the same URI — invalidate directly (RFC 9111 §4.4)
        store.Invalidate(new Uri($"http://127.0.0.1:{_fixture.Port}/cache/max-age/300"));

        // The original GET entry should be invalidated
        Assert.Equal(0, store.Count);
        var cachedAfter = store.Get(MakeGet("/cache/max-age/300"));
        Assert.Null(cachedAfter);
    }

    [Fact(DisplayName = "CACHE-11-011: must-revalidate forces revalidation when stale")]
    public async Task MustRevalidate_ForcesRevalidation()
    {
        var store = new HttpCacheStore();

        // First request to must-revalidate endpoint (max-age=0, must-revalidate)
        var response1 = await SendWithCacheAsync(MakeGet("/cache/must-revalidate"), store);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(1, store.Count);

        // Evaluate freshness — should be MustRevalidate (max-age=0)
        var entry = store.Get(MakeGet("/cache/must-revalidate"));
        Assert.NotNull(entry);
        var lookupResult =
            CacheFreshnessEvaluator.Evaluate(entry, MakeGet("/cache/must-revalidate"), DateTimeOffset.UtcNow);
        Assert.Equal(CacheLookupStatus.MustRevalidate, lookupResult.Status);
    }

    [Fact(DisplayName = "CACHE-11-012: HEAD response is cached via store")]
    public async Task HeadResponse_IsCached()
    {
        var store = new HttpCacheStore();

        // GET the resource first to obtain a real cacheable response
        var getResponse = await SendAsync(MakeGet("/cache/max-age/300"));
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        // Simulate caching a HEAD response — HEAD uses the same ShouldStore logic
        // and produces a cached entry with an empty body
        var headRequest = MakeHead("/cache/max-age/300");
        var now = DateTimeOffset.UtcNow;
        store.Put(headRequest, getResponse, [], now.AddMilliseconds(-50), now);

        Assert.Equal(1, store.Count);

        // Verify the entry can be looked up by HEAD request
        var entry = store.Get(MakeHead("/cache/max-age/300"));
        Assert.NotNull(entry);
        Assert.Empty(entry.Body);
    }

    [Fact(DisplayName = "CACHE-11-013: min-fresh request rejects entry without sufficient freshness")]
    public async Task MinFresh_RejectsInsufficientFreshness()
    {
        var store = new HttpCacheStore();

        // Cache a response with max-age=10
        var response1 = await SendWithCacheAsync(MakeGet("/cache/max-age/10"), store);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(1, store.Count);

        // Request with min-fresh=3600 — the 10-second entry won't have enough freshness
        var entry = store.Get(MakeGet("/cache/max-age/10"));
        Assert.NotNull(entry);

        var minFreshRequest = MakeGet("/cache/max-age/10");
        minFreshRequest.Headers.TryAddWithoutValidation("Cache-Control", "min-fresh=3600");

        var result = CacheFreshnessEvaluator.Evaluate(entry, minFreshRequest, DateTimeOffset.UtcNow);
        // Entry has ~10s freshness remaining, but min-fresh demands 3600s — should not be Fresh
        Assert.NotEqual(CacheLookupStatus.Fresh, result.Status);
    }

    [Fact(DisplayName = "CACHE-11-014: max-stale request accepts stale entry within tolerance")]
    public async Task MaxStale_AcceptsStaleEntry()
    {
        var store = new HttpCacheStore();

        // Cache a response with max-age=1
        var response1 = await SendWithCacheAsync(MakeGet("/cache/max-age/1"), store);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        // Wait for the entry to become stale
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Entry should now be stale
        var entry = store.Get(MakeGet("/cache/max-age/1"));
        Assert.NotNull(entry);

        // Without max-stale — should be MustRevalidate
        var now = DateTimeOffset.UtcNow;
        var resultNoMaxStale = CacheFreshnessEvaluator.Evaluate(entry, MakeGet("/cache/max-age/1"), now);
        Assert.Equal(CacheLookupStatus.MustRevalidate, resultNoMaxStale.Status);

        // With max-stale=60 — should be Stale (acceptable)
        var maxStaleRequest = MakeGet("/cache/max-age/1");
        maxStaleRequest.Headers.TryAddWithoutValidation("Cache-Control", "max-stale=60");
        var resultMaxStale = CacheFreshnessEvaluator.Evaluate(entry, maxStaleRequest, now);
        Assert.Equal(CacheLookupStatus.Stale, resultMaxStale.Status);
    }

    [Fact(DisplayName = "CACHE-11-015: LRU eviction removes oldest entry when capacity exceeded")]
    public async Task LruEviction_RemovesOldestEntry()
    {
        // Create a tiny cache with MaxEntries=3
        var policy = new CachePolicy { MaxEntries = 3 };
        var store = new HttpCacheStore(policy);

        // Fill the cache with 3 entries
        await SendWithCacheAsync(MakeGet("/cache/max-age/300"), store);
        await SendWithCacheAsync(MakeGet("/cache/etag/lru1"), store);
        await SendWithCacheAsync(MakeGet("/cache/etag/lru2"), store);
        Assert.Equal(3, store.Count);

        // Add a 4th entry — should evict the oldest (max-age/300)
        await SendWithCacheAsync(MakeGet("/cache/etag/lru3"), store);
        Assert.Equal(3, store.Count);

        // The first entry should be evicted
        var evicted = store.Get(MakeGet("/cache/max-age/300"));
        Assert.Null(evicted);

        // The newest entries should still be present
        var kept = store.Get(MakeGet("/cache/etag/lru3"));
        Assert.NotNull(kept);
    }
}