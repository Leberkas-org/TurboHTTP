using System.Net;
using TurboHTTP.Features.Caching;

namespace TurboHTTP.Tests.Features.Caching;

public sealed class CacheQualifiedDirectiveSpec
{
    private static readonly DateTimeOffset BaseTime = new(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static HttpRequestMessage GetRequest(string uri = "http://example.com/resource")
        => new(HttpMethod.Get, uri);

    private static HttpResponseMessage OkResponseWithCacheControl(string cacheControl)
    {
        var r = new HttpResponseMessage(HttpStatusCode.OK);
        r.Headers.TryAddWithoutValidation("Cache-Control", cacheControl);
        r.Headers.Date = BaseTime;
        return r;
    }

    private static void Put(Cache store, HttpRequestMessage request, HttpResponseMessage response,
        byte[] body, DateTimeOffset requestTime, DateTimeOffset responseTime)
    {
        var (owner, length) = Cache.RentBody(body);
        store.Put(request, response, owner, length, requestTime, responseTime);
    }

    [Trait("RFC", "RFC9111-5.2.2.3")]
    [Fact]
    public void CacheQualifiedDirective_should_strip_field_when_no_cache_qualified()
    {
        var store = new Cache();
        var request = GetRequest();
        var response = OkResponseWithCacheControl("max-age=3600, no-cache=\"Set-Cookie\"");
        response.Headers.TryAddWithoutValidation("Set-Cookie", "session=abc123");

        var (owner, length) = Cache.RentBody([]);
        store.Put(request, response, owner, length, BaseTime.AddSeconds(-1), BaseTime);

        var entry = store.Get(GetRequest());
        Assert.NotNull(entry);

        Assert.NotNull(entry.CacheControl);
        Assert.NotNull(entry.CacheControl.NoCacheFields);
        Assert.Contains("Set-Cookie", entry.CacheControl.NoCacheFields);

        var result = CacheFreshnessEvaluator.Evaluate(entry, GetRequest(), BaseTime.AddSeconds(10));
        Assert.Equal(CacheLookupStatus.Fresh, result.Status);
    }

    [Trait("RFC", "RFC9111-5.2.2.3")]
    [Fact]
    public void CacheQualifiedDirective_should_strip_multiple_fields_when_no_cache_qualified()
    {
        var store = new Cache();
        var request = GetRequest();
        var response = OkResponseWithCacheControl("max-age=3600, no-cache=\"X-Custom, X-Other\"");
        response.Headers.TryAddWithoutValidation("X-Custom", "val1");
        response.Headers.TryAddWithoutValidation("X-Other", "val2");
        response.Headers.TryAddWithoutValidation("X-Keep", "val3");

        var (owner, length) = Cache.RentBody([]);
        store.Put(request, response, owner, length, BaseTime.AddSeconds(-1), BaseTime);

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

        var (bodyOwner1, bodyLength1) = Cache.RentBody([]);
        var entry = new CacheEntry
        {
            Response = response,
            BodyOwner = bodyOwner1,
            BodyLength = bodyLength1,
            RequestTime = BaseTime.AddSeconds(-1),
            ResponseTime = BaseTime,
            Date = BaseTime,
            CacheControl = cc
        };

        var result = CacheFreshnessEvaluator.Evaluate(entry, GetRequest(), BaseTime.AddSeconds(10));
        Assert.Equal(CacheLookupStatus.MustRevalidate, result.Status);
    }

    [Trait("RFC", "RFC9111-5.2.2.3")]
    [Fact]
    public void CacheQualifiedDirective_should_not_force_revalidation_when_no_cache_qualified()
    {
        var response = OkResponseWithCacheControl("max-age=3600, no-cache=\"Set-Cookie\"");
        var cc = CacheControlParser.Parse("max-age=3600, no-cache=\"Set-Cookie\"");

        var (bodyOwner2, bodyLength2) = Cache.RentBody([]);
        var entry = new CacheEntry
        {
            Response = response,
            BodyOwner = bodyOwner2,
            BodyLength = bodyLength2,
            RequestTime = BaseTime.AddSeconds(-1),
            ResponseTime = BaseTime,
            Date = BaseTime,
            CacheControl = cc
        };

        // Qualified no-cache should NOT force revalidation — only the named fields are affected
        var result = CacheFreshnessEvaluator.Evaluate(entry, GetRequest(), BaseTime.AddSeconds(10));
        Assert.Equal(CacheLookupStatus.Fresh, result.Status);
    }

    [Trait("RFC", "RFC9111-5.2.2.7")]
    [Fact]
    public void CacheQualifiedDirective_should_exclude_field_when_private_qualified_in_shared_cache()
    {
        var policy = new CachePolicy { SharedCache = true };
        var store = new Cache(policy);
        var request = GetRequest();
        var response = OkResponseWithCacheControl("max-age=3600, private=\"Set-Cookie\"");
        response.Headers.TryAddWithoutValidation("Set-Cookie", "session=abc123");
        response.Headers.TryAddWithoutValidation("X-Keep", "should-remain");

        var (owner, length) = Cache.RentBody([]);
        store.Put(request, response, owner, length, BaseTime.AddSeconds(-1), BaseTime);

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
        var store = new Cache(policy);
        var request = GetRequest();
        var response = OkResponseWithCacheControl("max-age=3600, private");

        var (owner, length) = Cache.RentBody([]);
        store.Put(request, response, owner, length, BaseTime.AddSeconds(-1), BaseTime);

        // Unqualified private — shared cache must not store at all
        var entry = store.Get(GetRequest());
        Assert.Null(entry);
    }

    [Trait("RFC", "RFC9111-5.2.2.7")]
    [Fact]
    public void CacheQualifiedDirective_should_store_when_unqualified_private_in_private_cache()
    {
        var policy = new CachePolicy { SharedCache = false };
        var store = new Cache(policy);
        var request = GetRequest();
        var response = OkResponseWithCacheControl("max-age=3600, private");

        var (owner, length) = Cache.RentBody([]);
        store.Put(request, response, owner, length, BaseTime.AddSeconds(-1), BaseTime);

        // Private cache can store private responses
        var entry = store.Get(GetRequest());
        Assert.NotNull(entry);
    }
}