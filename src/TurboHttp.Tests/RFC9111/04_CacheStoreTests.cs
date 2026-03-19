using System.Net;
using TurboHttp.Protocol.RFC9111;

namespace TurboHttp.Tests.RFC9111;

public sealed class CacheStoreTests
{
    private static readonly DateTimeOffset _baseTime = new(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

    // ── Helper ───────────────────────────────────────────────────────────────

    private static HttpRequestMessage GetRequest(string uri = "http://example.com/resource")
        => new(HttpMethod.Get, uri);

    private static HttpResponseMessage OkResponse(int maxAge = 60)
    {
        var r = new HttpResponseMessage(HttpStatusCode.OK);
        r.Headers.TryAddWithoutValidation("Cache-Control", $"max-age={maxAge}");
        r.Headers.Date = _baseTime;
        return r;
    }

    // ── IsCacheable ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9111-3.1-CS-001: GET 200 with max-age is cacheable")]
    public void Should_BeCacheable_When_200OkWithMaxAge()
    {
        var response = OkResponse();
        Assert.True(HttpCacheStore.IsCacheable(response));
    }

    [Theory(DisplayName = "RFC9111-3.1-CS-002: cacheable status codes")]
    [InlineData(200)]
    [InlineData(203)]
    [InlineData(204)]
    [InlineData(206)]
    [InlineData(300)]
    [InlineData(301)]
    [InlineData(308)]
    [InlineData(404)]
    [InlineData(410)]
    public void Should_BeCacheable_When_StatusCodeIsCacheable(int statusCode)
    {
        var response = new HttpResponseMessage((HttpStatusCode)statusCode);
        Assert.True(HttpCacheStore.IsCacheable(response));
    }

    [Fact(DisplayName = "RFC9111-3.1-CS-003: 500 status is not cacheable by default")]
    public void Should_NotBeCacheable_When_500InternalServerError()
    {
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        Assert.False(HttpCacheStore.IsCacheable(response));
    }

    // ── ShouldStore ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9111-3-CS-004: GET 200 with max-age should be stored")]
    public void Should_StoreEntry_When_Get200WithMaxAge()
    {
        Assert.True(HttpCacheStore.ShouldStore(GetRequest(), OkResponse()));
    }

    [Fact(DisplayName = "RFC9111-3-CS-005: POST 200 should not be stored (unsafe method)")]
    public void Should_NotStoreEntry_When_Post200UnsafeMethod()
    {
        var post = new HttpRequestMessage(HttpMethod.Post, "http://example.com/resource");
        Assert.False(HttpCacheStore.ShouldStore(post, OkResponse()));
    }

    [Fact(DisplayName = "RFC9111-5.2.1.5-CS-006: no-store on request → should not store")]
    public void Should_NotStoreEntry_When_RequestHasNoStore()
    {
        var request = GetRequest();
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-store");
        Assert.False(HttpCacheStore.ShouldStore(request, OkResponse()));
    }

    [Fact(DisplayName = "RFC9111-5.2.2.5-CS-007: no-store on response → should not store")]
    public void Should_NotStoreEntry_When_ResponseHasNoStore()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Cache-Control", "no-store");
        Assert.False(HttpCacheStore.ShouldStore(GetRequest(), response));
    }

    // ── Get / Put / Invalidate ────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9111-4-CS-008: Get on empty store returns null")]
    public void Should_ReturnNull_When_StoreIsEmpty()
    {
        var store = new HttpCacheStore();
        var result = store.Get(GetRequest());
        Assert.Null(result);
    }

    [Fact(DisplayName = "RFC9111-3-CS-009: Put then Get same URI returns entry")]
    public void Should_ReturnCachedEntry_When_PutThenGetSameUri()
    {
        var store = new HttpCacheStore();
        var request = GetRequest();
        var response = OkResponse();
        var body = new byte[] { 1, 2, 3 };

        store.Put(request, response, body, _baseTime.AddSeconds(-1), _baseTime);

        var entry = store.Get(new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource"));
        Assert.NotNull(entry);
        Assert.Equal(body, entry.Body);
    }

    [Fact(DisplayName = "RFC9111-4.4-CS-010: Invalidate removes entry for URI")]
    public void Should_RemoveEntry_When_Invalidated()
    {
        var store = new HttpCacheStore();
        var request = GetRequest();
        store.Put(request, OkResponse(), [], _baseTime.AddSeconds(-1), _baseTime);

        store.Invalidate(new Uri("http://example.com/resource"));

        Assert.Null(store.Get(GetRequest()));
    }

    // ── Vary ──────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9111-4.1-CS-011: Vary header — different Accept is a cache miss")]
    public void Should_ReturnMiss_When_VaryHeaderAndDifferentAccept()
    {
        var store = new HttpCacheStore();

        var request1 = GetRequest();
        request1.Headers.TryAddWithoutValidation("Accept", "application/json");

        var response = OkResponse();
        response.Headers.TryAddWithoutValidation("Vary", "Accept");

        store.Put(request1, response, [], _baseTime.AddSeconds(-1), _baseTime);

        var request2 = GetRequest();
        request2.Headers.TryAddWithoutValidation("Accept", "text/html");

        Assert.Null(store.Get(request2));
    }

    [Fact(DisplayName = "RFC9111-4.1-CS-012: Vary header — matching Accept is a cache hit")]
    public void Should_ReturnHit_When_VaryHeaderAndMatchingAccept()
    {
        var store = new HttpCacheStore();

        var request1 = GetRequest();
        request1.Headers.TryAddWithoutValidation("Accept", "application/json");

        var response = OkResponse();
        response.Headers.TryAddWithoutValidation("Vary", "Accept");

        store.Put(request1, response, [42], _baseTime.AddSeconds(-1), _baseTime);

        var request2 = GetRequest();
        request2.Headers.TryAddWithoutValidation("Accept", "application/json");

        var entry = store.Get(request2);
        Assert.NotNull(entry);
    }

    [Fact(DisplayName = "RFC9111-4.1-CS-013: Vary: * never matches")]
    public void Should_NeverMatch_When_VaryIsStar()
    {
        var store = new HttpCacheStore();

        var response = OkResponse();
        response.Headers.TryAddWithoutValidation("Vary", "*");

        store.Put(GetRequest(), response, [], _baseTime.AddSeconds(-1), _baseTime);

        Assert.Null(store.Get(GetRequest()));
    }

    // ── LRU eviction ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9111-3-CS-014: LRU eviction when MaxEntries exceeded")]
    public void Should_EvictEntries_When_MaxEntriesExceeded()
    {
        var policy = new CachePolicy { MaxEntries = 2 };
        var store = new HttpCacheStore(policy);

        for (var i = 0; i < 3; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"http://example.com/r{i}");
            store.Put(req, OkResponse(), [], _baseTime.AddSeconds(-1), _baseTime);
        }

        // Store should have at most 2 entries
        Assert.Equal(2, store.Count);
    }
}
