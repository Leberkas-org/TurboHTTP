using System.Net;
using TurboHttp.Protocol.Caching;

namespace TurboHttp.Tests.Caching;

/// <summary>
/// RFC 9111 §5.2.2.3 / §5.2.2.7 — Qualified no-cache and private directive enforcement.
/// Verifies that qualified field lists are stripped from cached responses (no-cache)
/// and from shared cache storage (private).
/// </summary>
/// <remarks>
/// Class under test: <see cref="CacheStore"/>, <see cref="CacheFreshnessEvaluator"/>.
/// RFC 9111 §5.2.2.3: Qualified no-cache strips named fields on reuse.
/// RFC 9111 §5.2.2.7: Qualified private strips named fields from shared cache storage.
/// </remarks>
public sealed class CacheQualifiedDirectiveSpec
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


    [Trait("RFC", "RFC9111-5.2.2.3")]
    [Fact]
    public void CacheQualifiedDirective_should_strip_field_when_no_cache_qualified()
    {
        var store = new CacheStore();
        var request = GetRequest();
        var response = OkResponseWithCacheControl("max-age=3600, no-cache=\"Set-Cookie\"");
        response.Headers.TryAddWithoutValidation("Set-Cookie", "session=abc123");

        store.Put(request, response, [], _baseTime.AddSeconds(-1), _baseTime);

        var entry = store.Get(GetRequest());
        Assert.NotNull(entry);

        Assert.NotNull(entry.CacheControl);
        Assert.NotNull(entry.CacheControl.NoCacheFields);
        Assert.Contains("Set-Cookie", entry.CacheControl.NoCacheFields);

        var result = CacheFreshnessEvaluator.Evaluate(entry, GetRequest(), _baseTime.AddSeconds(10));
        Assert.Equal(CacheLookupStatus.Fresh, result.Status);
    }

    [Trait("RFC", "RFC9111-5.2.2.3")]
    [Fact]
    public void CacheQualifiedDirective_should_strip_multiple_fields_when_no_cache_qualified()
    {
        var store = new CacheStore();
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

    [Trait("RFC", "RFC9111-5.2.2.3")]
    [Fact]
    public void CacheQualifiedDirective_should_require_revalidation_when_unqualified_no_cache()
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

    [Trait("RFC", "RFC9111-5.2.2.3")]
    [Fact]
    public void CacheQualifiedDirective_should_not_force_revalidation_when_no_cache_qualified()
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

    [Trait("RFC", "RFC9111-5.2.2.7")]
    [Fact]
    public void CacheQualifiedDirective_should_exclude_field_when_private_qualified_in_shared_cache()
    {
        var policy = new CachePolicy { SharedCache = true };
        var store = new CacheStore(policy);
        var request = GetRequest();
        var response = OkResponseWithCacheControl("max-age=3600, private=\"Set-Cookie\"");
        response.Headers.TryAddWithoutValidation("Set-Cookie", "session=abc123");
        response.Headers.TryAddWithoutValidation("X-Keep", "should-remain");

        store.Put(request, response, [], _baseTime.AddSeconds(-1), _baseTime);

        var entry = store.Get(GetRequest());
        Assert.NotNull(entry);

        // The Set-Cookie header should have been stripped before storage
        Assert.False(entry.Response.Headers.Contains("Set-Cookie"));
        // Other headers should remain
        Assert.True(entry.Response.Headers.Contains("X-Keep"));
    }

    [Trait("RFC", "RFC9111-5.2.2.7")]
    [Fact]
    public void CacheQualifiedDirective_should_not_store_when_unqualified_private_in_shared_cache()
    {
        var policy = new CachePolicy { SharedCache = true };
        var store = new CacheStore(policy);
        var request = GetRequest();
        var response = OkResponseWithCacheControl("max-age=3600, private");

        store.Put(request, response, [], _baseTime.AddSeconds(-1), _baseTime);

        // Unqualified private — shared cache must not store at all
        var entry = store.Get(GetRequest());
        Assert.Null(entry);
    }

    [Trait("RFC", "RFC9111-5.2.2.7")]
    [Fact]
    public void CacheQualifiedDirective_should_store_when_unqualified_private_in_private_cache()
    {
        var policy = new CachePolicy { SharedCache = false };
        var store = new CacheStore(policy);
        var request = GetRequest();
        var response = OkResponseWithCacheControl("max-age=3600, private");

        store.Put(request, response, [], _baseTime.AddSeconds(-1), _baseTime);

        // Private cache can store private responses
        var entry = store.Get(GetRequest());
        Assert.NotNull(entry);
    }
}
