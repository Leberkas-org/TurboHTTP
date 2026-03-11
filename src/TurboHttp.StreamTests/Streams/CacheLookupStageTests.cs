using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHttp.Protocol;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Streams;

public sealed class CacheLookupStageTests : StreamTestBase
{
    // ── helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Materialises a CacheLookupStage with manual subscriber probes, gives each outlet
    /// <paramref name="demandEach"/> demand, and returns the probes ready for assertions.
    /// The source is concatenated with Source.Never to prevent premature stream completion.
    /// </summary>
    private (TestSubscriber.ManualProbe<HttpRequestMessage> miss,
             TestSubscriber.ManualProbe<HttpResponseMessage> hit) Run(
        HttpCacheStore store,
        CachePolicy? policy,
        int demandEach,
        params HttpRequestMessage[] requests)
    {
        var probeMiss = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var probeHit  = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var stage = b.Add(new CacheLookupStage(store, policy));
            var src   = b.Add(Source.From(requests).Concat(Source.Never<HttpRequestMessage>()));

            b.From(src).To(stage.In);
            b.From(stage.Out0).To(Sink.FromSubscriber(probeMiss));
            b.From(stage.Out1).To(Sink.FromSubscriber(probeHit));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var subMiss = probeMiss.ExpectSubscription();
        var subHit  = probeHit.ExpectSubscription();

        subMiss.Request(demandEach);
        subHit.Request(demandEach);

        return (probeMiss, probeHit);
    }

    /// <summary>Builds a store containing a fresh (max-age=3600) GET entry for the given URL.</summary>
    private static HttpCacheStore StoreWithFreshEntry(string url = "http://example.com/resource")
    {
        var req  = new HttpRequestMessage(HttpMethod.Get, url);
        var resp = new HttpResponseMessage(HttpStatusCode.OK);
        resp.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
        resp.Headers.Date = DateTimeOffset.UtcNow;

        var store = new HttpCacheStore();
        store.Put(req, resp, Array.Empty<byte>(),
            DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow);
        return store;
    }

    /// <summary>
    /// Builds a store containing a stale must-revalidate GET entry for the given URL
    /// (max-age=1, Date=100 seconds ago).
    /// </summary>
    private static HttpCacheStore StoreWithStaleEntry(
        string url = "http://example.com/resource",
        string? etag = "\"abc123\"",
        DateTimeOffset? lastModified = null)
    {
        var req  = new HttpRequestMessage(HttpMethod.Get, url);
        var resp = new HttpResponseMessage(HttpStatusCode.OK);
        resp.Headers.TryAddWithoutValidation("Cache-Control", "max-age=1, must-revalidate");
        resp.Headers.Date = DateTimeOffset.UtcNow.AddSeconds(-100);
        if (etag is not null)
        {
            resp.Headers.TryAddWithoutValidation("ETag", etag);
        }
        if (lastModified.HasValue)
        {
            resp.Content.Headers.LastModified = lastModified;
        }

        var store = new HttpCacheStore();
        store.Put(req, resp, Array.Empty<byte>(),
            DateTimeOffset.UtcNow.AddSeconds(-101), DateTimeOffset.UtcNow.AddSeconds(-100));
        return store;
    }

    // ── cache miss ─────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "CACHE-001: cache miss → request forwarded to Out0 unchanged")]
    public async Task CACHE_001_CacheMiss_ForwardsToOut0()
    {
        var store   = new HttpCacheStore();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");

        var (miss, hit) = Run(store, null, 1, request);

        Assert.Same(request, miss.ExpectNext());
        hit.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "CACHE-002: POST request → cache miss → forwarded to Out0")]
    public async Task CACHE_002_PostRequest_CacheMiss_ForwardsToOut0()
    {
        var store   = new HttpCacheStore();
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/resource");

        var (miss, hit) = Run(store, null, 1, request);

        Assert.Same(request, miss.ExpectNext());
        hit.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    // ── cache hit (fresh) ──────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "CACHE-003: fresh cache entry → cached response emitted on Out1")]
    public async Task CACHE_003_FreshEntry_EmitsOnOut1()
    {
        var store   = StoreWithFreshEntry();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");

        var (miss, hit) = Run(store, null, 1, request);

        var response = hit.ExpectNext();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        miss.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "CACHE-004: fresh cache entry → Out1 emits the exact stored response object")]
    public async Task CACHE_004_FreshEntry_SameResponseObject()
    {
        const string url  = "http://example.com/resource";
        var req  = new HttpRequestMessage(HttpMethod.Get, url);
        var resp = new HttpResponseMessage(HttpStatusCode.OK);
        resp.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
        resp.Headers.Date = DateTimeOffset.UtcNow;

        var store = new HttpCacheStore();
        store.Put(req, resp, Array.Empty<byte>(),
            DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow);

        var (miss, hit) = Run(store, null, 1, new HttpRequestMessage(HttpMethod.Get, url));

        Assert.Same(resp, hit.ExpectNext());
        miss.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    // ── must-revalidate ────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "CACHE-005: stale must-revalidate with ETag → If-None-Match added on Out0")]
    public async Task CACHE_005_MustRevalidate_WithETag_AddsIfNoneMatch()
    {
        var store   = StoreWithStaleEntry(etag: "\"v1\"");
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");

        var (miss, hit) = Run(store, null, 1, request);

        var conditional = miss.ExpectNext();
        Assert.True(conditional.Headers.Contains("If-None-Match"));
        Assert.Contains("v1", string.Join("", conditional.Headers.GetValues("If-None-Match")));
        hit.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "CACHE-006: stale must-revalidate with Last-Modified → If-Modified-Since on Out0")]
    public async Task CACHE_006_MustRevalidate_WithLastModified_AddsIfModifiedSince()
    {
        var lastModified = DateTimeOffset.UtcNow.AddDays(-7);
        var store        = StoreWithStaleEntry(etag: null, lastModified: lastModified);
        var request      = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");

        var (miss, hit) = Run(store, null, 1, request);

        var conditional = miss.ExpectNext();
        Assert.NotNull(conditional.Headers.IfModifiedSince);
        hit.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "CACHE-007: stale must-revalidate with no validators → plain request forwarded to Out0")]
    public async Task CACHE_007_MustRevalidate_NoValidators_PlainRequestOut0()
    {
        const string url = "http://example.com/resource";
        var req  = new HttpRequestMessage(HttpMethod.Get, url);
        var resp = new HttpResponseMessage(HttpStatusCode.OK);
        resp.Headers.TryAddWithoutValidation("Cache-Control", "max-age=1, must-revalidate");
        resp.Headers.Date = DateTimeOffset.UtcNow.AddSeconds(-100);

        var store = new HttpCacheStore();
        store.Put(req, resp, Array.Empty<byte>(),
            DateTimeOffset.UtcNow.AddSeconds(-101), DateTimeOffset.UtcNow.AddSeconds(-100));

        var (miss, hit) = Run(store, null, 1, new HttpRequestMessage(HttpMethod.Get, url));

        var forwarded = miss.ExpectNext();
        Assert.False(forwarded.Headers.Contains("If-None-Match"));
        Assert.Null(forwarded.Headers.IfModifiedSince);
        hit.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    // ── request Cache-Control directives ──────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "CACHE-008: request no-cache → forces MustRevalidate even for fresh entry")]
    public async Task CACHE_008_RequestNoCache_ForcesMustRevalidate()
    {
        var store   = StoreWithFreshEntry();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");

        var (miss, hit) = Run(store, null, 1, request);

        miss.ExpectNext(); // revalidation request forwarded
        hit.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    // ── multiple requests ──────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "CACHE-009: two sequential misses → both forwarded to Out0")]
    public async Task CACHE_009_TwoMisses_BothToOut0()
    {
        var store = new HttpCacheStore();
        var req1  = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var req2  = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");

        var (miss, hit) = Run(store, null, 2, req1, req2);

        Assert.Same(req1, miss.ExpectNext());
        Assert.Same(req2, miss.ExpectNext());
        hit.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "CACHE-010: two sequential hits → both served on Out1")]
    public async Task CACHE_010_TwoHits_BothToOut1()
    {
        const string url1 = "http://example.com/a";
        const string url2 = "http://example.com/b";
        var store = new HttpCacheStore();

        void Seed(string url)
        {
            var r = new HttpRequestMessage(HttpMethod.Get, url);
            var s = new HttpResponseMessage(HttpStatusCode.OK);
            s.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
            s.Headers.Date = DateTimeOffset.UtcNow;
            store.Put(r, s, Array.Empty<byte>(),
                DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow);
        }

        Seed(url1);
        Seed(url2);

        var (miss, hit) = Run(store, null, 2,
            new HttpRequestMessage(HttpMethod.Get, url1),
            new HttpRequestMessage(HttpMethod.Get, url2));

        hit.ExpectNext();
        hit.ExpectNext();
        miss.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "CACHE-011: mixed miss then hit → miss on Out0, hit on Out1")]
    public async Task CACHE_011_MixedMissAndHit_CorrectRouting()
    {
        const string urlMiss = "http://example.com/miss";
        const string urlHit  = "http://example.com/hit";

        var store  = StoreWithFreshEntry(urlHit);
        var reqMiss = new HttpRequestMessage(HttpMethod.Get, urlMiss);
        var reqHit  = new HttpRequestMessage(HttpMethod.Get, urlHit);

        var (miss, hit) = Run(store, null, 2, reqMiss, reqHit);

        Assert.Same(reqMiss, miss.ExpectNext());
        Assert.Equal(HttpStatusCode.OK, hit.ExpectNext().StatusCode);
    }

    // ── policy ────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "CACHE-012: null policy → defaults to CachePolicy.Default, fresh entries served from cache")]
    public async Task CACHE_012_NullPolicy_UsesDefault()
    {
        var store   = StoreWithFreshEntry();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");

        var (miss, hit) = Run(store, null, 1, request);

        hit.ExpectNext();
        miss.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }
}
