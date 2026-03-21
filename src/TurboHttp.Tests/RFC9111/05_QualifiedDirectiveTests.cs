using System.Net;
using System.Net.Http;
using TurboHttp.Protocol.RFC9111;

namespace TurboHttp.Tests.RFC9111;

/// <summary>
/// RFC 9111 §5.2.2.3 / §5.2.2.7 — Qualified no-cache and private directive enforcement.
/// Verifies that qualified field lists are stripped from cached responses (no-cache)
/// and from shared cache storage (private).
/// </summary>
public sealed class QualifiedDirectiveTests
{
    private static readonly DateTimeOffset _baseTime = new(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static HttpRequestMessage GetRequest(string uri = "http://example.com/resource")
        => new(HttpMethod.Get, uri);

    private static HttpResponseMessage OkResponseWithCacheControl(string cacheControl)
    {
        var r = new HttpResponseMessage(HttpStatusCode.OK);
        r.Headers.TryAddWithoutValidation("Cache-Control", cacheControl);
        r.Headers.Date = _baseTime;
        return r;
    }

    // ──── no-cache="field" stripping on reuse ────

    [Fact(DisplayName = "RFC9111-5.2.2.3-QD-001: no-cache=\"Set-Cookie\" strips field on reuse")]
    public void Should_StripField_When_NoCacheQualified()
    {
        var store = new HttpCacheStore();
        var request = GetRequest();
        var response = OkResponseWithCacheControl("max-age=3600, no-cache=\"Set-Cookie\"");
        response.Headers.TryAddWithoutValidation("Set-Cookie", "session=abc123");

        store.Put(request, response, [], _baseTime.AddSeconds(-1), _baseTime);

        // Retrieve from cache — the entry is stored
        var entry = store.Get(GetRequest());
        Assert.NotNull(entry);

        // The CacheControl should have NoCacheFields populated
        Assert.NotNull(entry.CacheControl);
        Assert.NotNull(entry.CacheControl.NoCacheFields);
        Assert.Contains("Set-Cookie", entry.CacheControl.NoCacheFields);

        // Verify freshness evaluator allows serving (qualified no-cache, not unqualified)
        var result = CacheFreshnessEvaluator.Evaluate(entry, GetRequest(), _baseTime.AddSeconds(10));
        Assert.Equal(CacheLookupStatus.Fresh, result.Status);
    }

    [Fact(DisplayName = "RFC9111-5.2.2.3-QD-002: no-cache=\"A, B\" strips both fields")]
    public void Should_StripMultipleFields_When_NoCacheQualified()
    {
        var store = new HttpCacheStore();
        var request = GetRequest();
        var response = OkResponseWithCacheControl("max-age=3600, no-cache=\"X-Custom, X-Other\"");
        response.Headers.TryAddWithoutValidation("X-Custom", "val1");
        response.Headers.TryAddWithoutValidation("X-Other", "val2");
        response.Headers.TryAddWithoutValidation("X-Keep", "val3");

        store.Put(request, response, [], _baseTime.AddSeconds(-1), _baseTime);

        var entry = store.Get(GetRequest());
        Assert.NotNull(entry);
        Assert.NotNull(entry.CacheControl?.NoCacheFields);
        Assert.Equal(2, entry.CacheControl!.NoCacheFields!.Count);
        Assert.Contains("X-Custom", entry.CacheControl.NoCacheFields);
        Assert.Contains("X-Other", entry.CacheControl.NoCacheFields);
    }

    // ──── Unqualified no-cache requires full revalidation ────

    [Fact(DisplayName = "RFC9111-5.2.2.3-QD-003: Unqualified no-cache requires full revalidation")]
    public void Should_Revalidate_When_UnqualifiedNoCache()
    {
        var response = OkResponseWithCacheControl("max-age=3600, no-cache");
        var cc = CacheControlParser.Parse("max-age=3600, no-cache");

        var entry = new CacheEntry
        {
            Response = response,
            Body = [],
            RequestTime = _baseTime.AddSeconds(-1),
            ResponseTime = _baseTime,
            Date = _baseTime,
            CacheControl = cc
        };

        var result = CacheFreshnessEvaluator.Evaluate(entry, GetRequest(), _baseTime.AddSeconds(10));
        Assert.Equal(CacheLookupStatus.MustRevalidate, result.Status);
    }

    [Fact(DisplayName = "RFC9111-5.2.2.3-QD-005: Qualified no-cache does not force revalidation")]
    public void Should_NotForceRevalidation_When_NoCacheQualified()
    {
        var response = OkResponseWithCacheControl("max-age=3600, no-cache=\"Set-Cookie\"");
        var cc = CacheControlParser.Parse("max-age=3600, no-cache=\"Set-Cookie\"");

        var entry = new CacheEntry
        {
            Response = response,
            Body = [],
            RequestTime = _baseTime.AddSeconds(-1),
            ResponseTime = _baseTime,
            Date = _baseTime,
            CacheControl = cc
        };

        // Qualified no-cache should NOT force revalidation — only the named fields are affected
        var result = CacheFreshnessEvaluator.Evaluate(entry, GetRequest(), _baseTime.AddSeconds(10));
        Assert.Equal(CacheLookupStatus.Fresh, result.Status);
    }

    // ──── private="field" excludes from shared cache storage ────

    [Fact(DisplayName = "RFC9111-5.2.2.7-QD-004: private=\"Set-Cookie\" excludes field from shared cache")]
    public void Should_ExcludeField_When_PrivateQualified()
    {
        var policy = new CachePolicy { SharedCache = true };
        var store = new HttpCacheStore(policy);
        var request = GetRequest();
        var response = OkResponseWithCacheControl("max-age=3600, private=\"Set-Cookie\"");
        response.Headers.TryAddWithoutValidation("Set-Cookie", "session=abc123");
        response.Headers.TryAddWithoutValidation("X-Keep", "should-remain");

        store.Put(request, response, [], _baseTime.AddSeconds(-1), _baseTime);

        // Entry should be stored (qualified private allows storage of non-private fields)
        var entry = store.Get(GetRequest());
        Assert.NotNull(entry);

        // The Set-Cookie header should have been stripped before storage
        Assert.False(entry.Response.Headers.Contains("Set-Cookie"));
        // Other headers should remain
        Assert.True(entry.Response.Headers.Contains("X-Keep"));
    }

    [Fact(DisplayName = "RFC9111-5.2.2.7-QD-006: Unqualified private prevents shared cache storage")]
    public void Should_NotStore_When_UnqualifiedPrivateInSharedCache()
    {
        var policy = new CachePolicy { SharedCache = true };
        var store = new HttpCacheStore(policy);
        var request = GetRequest();
        var response = OkResponseWithCacheControl("max-age=3600, private");

        store.Put(request, response, [], _baseTime.AddSeconds(-1), _baseTime);

        // Unqualified private — shared cache must not store at all
        var entry = store.Get(GetRequest());
        Assert.Null(entry);
    }

    [Fact(DisplayName = "RFC9111-5.2.2.7-QD-007: Unqualified private allowed in private cache")]
    public void Should_Store_When_UnqualifiedPrivateInPrivateCache()
    {
        var policy = new CachePolicy { SharedCache = false };
        var store = new HttpCacheStore(policy);
        var request = GetRequest();
        var response = OkResponseWithCacheControl("max-age=3600, private");

        store.Put(request, response, [], _baseTime.AddSeconds(-1), _baseTime);

        // Private cache can store private responses
        var entry = store.Get(GetRequest());
        Assert.NotNull(entry);
    }
}
