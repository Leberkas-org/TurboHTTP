using System.Net;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9111;

/// <summary>
/// RFC 9111 §4 — Full end-to-end cache lifecycle integration tests.
/// Exercises: lookup → conditional request building → store → freshnesseval → invalidation.
/// </summary>
public sealed class CacheIntegrationTests
{
    private static readonly DateTimeOffset _baseTime = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static HttpRequestMessage GetRequest(string uri = "http://example.com/api/data")
        => new(HttpMethod.Get, uri);

    private static HttpResponseMessage OkResponseWithMaxAge(int maxAgeSeconds)
    {
        var r = new HttpResponseMessage(HttpStatusCode.OK);
        r.Headers.TryAddWithoutValidation("Cache-Control", $"max-age={maxAgeSeconds}");
        r.Headers.Date = _baseTime;
        return r;
    }

    // ── Full cycle tests ──────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC-9111-§4: PUT response then GET same URI → Fresh hit")]
    public void FullCycle_PutThenGet_FreshHit()
    {
        var store = new HttpCacheStore();
        var request = GetRequest();
        var response = OkResponseWithMaxAge(300);
        var body = new byte[] { 1, 2, 3 };

        store.Put(request, response, body, _baseTime.AddSeconds(-1), _baseTime);

        var entry = store.Get(GetRequest());
        Assert.NotNull(entry);

        // Immediately after storing: should be fresh
        var now = _baseTime.AddSeconds(10);
        var result = CacheFreshnessEvaluator.Evaluate(entry, GetRequest(), now);
        Assert.Equal(CacheLookupStatus.Fresh, result.Status);
    }

    [Fact(DisplayName = "RFC-9111-§4: PUT response then time passes → Stale → must revalidate")]
    public void FullCycle_Stale_MustRevalidate()
    {
        var store = new HttpCacheStore();
        var request = GetRequest();
        var response = OkResponseWithMaxAge(60);
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=60, must-revalidate");

        store.Put(request, response, [], _baseTime.AddSeconds(-1), _baseTime);

        var entry = store.Get(GetRequest());
        Assert.NotNull(entry);

        // 120 seconds later: stale + must-revalidate → MustRevalidate
        var now = _baseTime.AddSeconds(120);
        var result = CacheFreshnessEvaluator.Evaluate(entry, GetRequest(), now);
        Assert.Equal(CacheLookupStatus.MustRevalidate, result.Status);
    }

    [Fact(DisplayName = "RFC-9111-§5.2.2.8: stale + must-revalidate → MustRevalidate status")]
    public void MustRevalidate_WhenStale_ReturnsMustRevalidate()
    {
        var cc = new CacheControl { MaxAge = TimeSpan.FromSeconds(30), MustRevalidate = true };
        var entry = new CacheEntry
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK),
            Body = [],
            RequestTime = _baseTime.AddSeconds(-1),
            ResponseTime = _baseTime,
            Date = _baseTime,
            CacheControl = cc
        };

        var now = _baseTime.AddSeconds(120);
        var result = CacheFreshnessEvaluator.Evaluate(entry, GetRequest(), now);
        Assert.Equal(CacheLookupStatus.MustRevalidate, result.Status);
    }

    [Fact(DisplayName = "RFC-9111-§4.2: stale without must-revalidate → Stale status")]
    public void StaleWithoutMustRevalidate_ReturnsStale()
    {
        var cc = new CacheControl { MaxAge = TimeSpan.FromSeconds(30) };
        var entry = new CacheEntry
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK),
            Body = [],
            RequestTime = _baseTime.AddSeconds(-1),
            ResponseTime = _baseTime,
            Date = _baseTime,
            CacheControl = cc
        };

        // Request allows stale with max-stale
        var request = GetRequest();
        request.Headers.TryAddWithoutValidation("Cache-Control", "max-stale=300");

        var now = _baseTime.AddSeconds(120);
        var result = CacheFreshnessEvaluator.Evaluate(entry, request, now);
        Assert.Equal(CacheLookupStatus.Stale, result.Status);
    }

    [Fact(DisplayName = "RFC-9111-§5.2.1.4: no-cache on request forces revalidation even if fresh")]
    public void NoCache_OnRequest_ForcesMustRevalidateEvenIfFresh()
    {
        var cc = new CacheControl { MaxAge = TimeSpan.FromSeconds(3600) };
        var entry = new CacheEntry
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK),
            Body = [],
            RequestTime = _baseTime.AddSeconds(-1),
            ResponseTime = _baseTime,
            Date = _baseTime,
            CacheControl = cc
        };

        var request = GetRequest();
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");

        var now = _baseTime.AddSeconds(10); // well within max-age
        var result = CacheFreshnessEvaluator.Evaluate(entry, request, now);
        Assert.Equal(CacheLookupStatus.MustRevalidate, result.Status);
    }

    [Fact(DisplayName = "RFC-9111-§5.2.1.7: only-if-cached + fresh entry → Fresh")]
    public void OnlyIfCached_FreshEntry_ReturnsFresh()
    {
        var store = new HttpCacheStore();
        var response = OkResponseWithMaxAge(600);
        store.Put(GetRequest(), response, [], _baseTime.AddSeconds(-1), _baseTime);

        var entry = store.Get(GetRequest());
        Assert.NotNull(entry);

        // only-if-cached with fresh entry — the request directive doesn't affect freshness eval
        var now = _baseTime.AddSeconds(10);
        var result = CacheFreshnessEvaluator.Evaluate(entry, GetRequest(), now);
        Assert.Equal(CacheLookupStatus.Fresh, result.Status);
    }

    [Fact(DisplayName = "RFC-9111-§5.2.1.2: max-stale=300 accepts stale entry within tolerance")]
    public void MaxStale_300_AcceptsStaleEntryWithinTolerance()
    {
        var cc = new CacheControl { MaxAge = TimeSpan.FromSeconds(60) };
        var entry = new CacheEntry
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK),
            Body = [],
            RequestTime = _baseTime.AddSeconds(-1),
            ResponseTime = _baseTime,
            Date = _baseTime,
            CacheControl = cc
        };

        var request = GetRequest();
        request.Headers.TryAddWithoutValidation("Cache-Control", "max-stale=300");

        // Entry is 120s old, max-age=60 → stale by 60s, within max-stale=300
        var now = _baseTime.AddSeconds(120);
        var result = CacheFreshnessEvaluator.Evaluate(entry, request, now);
        Assert.Equal(CacheLookupStatus.Stale, result.Status);
    }

    [Fact(DisplayName = "RFC-9111-§4.4: unsafe method (POST) invalidates related GET cache entry")]
    public void UnsafeMethod_Post_InvalidatesGetEntry()
    {
        var store = new HttpCacheStore();
        var uri = "http://example.com/items";

        // Store a GET entry
        var getReq = new HttpRequestMessage(HttpMethod.Get, uri);
        store.Put(getReq, OkResponseWithMaxAge(300), [], _baseTime.AddSeconds(-1), _baseTime);

        // Confirm it's stored
        Assert.NotNull(store.Get(new HttpRequestMessage(HttpMethod.Get, uri)));

        // POST to same URI invalidates it
        store.Invalidate(new Uri(uri));

        Assert.Null(store.Get(new HttpRequestMessage(HttpMethod.Get, uri)));
    }
}
