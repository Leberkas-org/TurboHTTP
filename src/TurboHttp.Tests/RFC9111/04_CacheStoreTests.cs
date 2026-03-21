using System.Linq;
using System.Net;
using TurboHttp.Protocol.RFC9111;

namespace TurboHttp.Tests.RFC9111;

/// <summary>
/// RFC 9111 §3 — HTTP cache store tests.
/// Covers cacheable/uncacheable response classification, LRU eviction,
/// Vary header matching, and thread-safety of the in-memory cache.
/// </summary>
/// <remarks>
/// Class under test: <see cref="HttpCacheStore"/>.
/// RFC 9111 §3: A cache must store and retrieve responses keyed by request URI and Vary headers.
/// </remarks>
public sealed class CacheStoreTests
{
    private static readonly DateTimeOffset _baseTime = new(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);


    private static HttpRequestMessage GetRequest(string uri = "http://example.com/resource")
        => new(HttpMethod.Get, uri);

    private static HttpResponseMessage OkResponse(int maxAge = 60)
    {
        var r = new HttpResponseMessage(HttpStatusCode.OK);
        r.Headers.TryAddWithoutValidation("Cache-Control", $"max-age={maxAge}");
        r.Headers.Date = _baseTime;
        return r;
    }


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


    [Fact(DisplayName = "RFC9111-5.2.2.3-CS-030: must-understand + 200 allows storage")]
    public void Should_Store_When_MustUnderstandAnd200()
    {
        var request = GetRequest();
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=60, must-understand");
        response.Headers.Date = _baseTime;

        Assert.True(HttpCacheStore.ShouldStore(request, response));
    }

    [Fact(DisplayName = "RFC9111-5.2.2.3-CS-031: must-understand + 299 prevents storage")]
    public void Should_NotStore_When_MustUnderstandAndUnknownStatus()
    {
        var request = GetRequest();
        var response = new HttpResponseMessage((HttpStatusCode)299);
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=60, must-understand");
        response.Headers.Date = _baseTime;

        Assert.False(HttpCacheStore.ShouldStore(request, response));
    }

    [Fact(DisplayName = "RFC9111-5.2.2.3-CS-032: must-understand absent allows any cacheable status")]
    public void Should_Store_When_NoMustUnderstand()
    {
        var request = GetRequest();
        // 200 is cacheable-by-default; without must-understand, any cacheable status is fine
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=60");
        response.Headers.Date = _baseTime;

        Assert.True(HttpCacheStore.ShouldStore(request, response));
    }

    [Fact(DisplayName = "RFC9111-3.1-CS-033: 206 Partial Content not stored in cache")]
    public void Should_NotStore_When_206PartialContent()
    {
        var request = GetRequest();
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent);
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=60");
        response.Headers.Date = _baseTime;

        Assert.False(HttpCacheStore.ShouldStore(request, response));
    }

    [Fact(DisplayName = "RFC9111-3.1-CS-034: response with Content-Range not stored in cache")]
    public void Should_NotStore_When_ResponseHasContentRange()
    {
        var request = GetRequest();
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new ByteArrayContent([1, 2, 3]);
        response.Content.Headers.ContentRange =
            new System.Net.Http.Headers.ContentRangeHeaderValue(0, 2, 100);
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=60");
        response.Headers.Date = _baseTime;

        Assert.False(HttpCacheStore.ShouldStore(request, response));
    }

    [Fact(DisplayName = "RFC9111-3.1-CS-035: 200 without Content-Range stored normally")]
    public void Should_Store_When_200WithoutContentRange()
    {
        var request = GetRequest();
        var response = OkResponse();

        Assert.True(HttpCacheStore.ShouldStore(request, response));
    }

    [Fact(DisplayName = "RFC9111-3.1-CS-036: Trailers not merged into cached headers")]
    public void Should_NotMergeTrailers_When_CachedWithTrailers()
    {
        var store = new HttpCacheStore();
        var request = GetRequest();

        // Simulate a chunked response with trailing headers
        var response = OkResponse();
        response.TrailingHeaders.TryAddWithoutValidation("Checksum", "abc123");
        response.TrailingHeaders.TryAddWithoutValidation("Signature", "xyz789");

        store.Put(request, response, [1, 2, 3], _baseTime.AddSeconds(-1), _baseTime);

        var entry = store.Get(GetRequest());
        Assert.NotNull(entry);

        // RFC 9111 §3.1: trailers MUST NOT be combined with header fields
        Assert.False(entry.Response.Headers.Contains("Checksum"),
            "Trailer field 'Checksum' must not appear in response headers");
        Assert.False(entry.Response.Headers.Contains("Signature"),
            "Trailer field 'Signature' must not appear in response headers");

        // Verify trailers are still available on TrailingHeaders
        Assert.True(entry.Response.TrailingHeaders.Contains("Checksum"));
        Assert.True(entry.Response.TrailingHeaders.Contains("Signature"));
        Assert.Equal("abc123", entry.Response.TrailingHeaders.GetValues("Checksum").Single());
        Assert.Equal("xyz789", entry.Response.TrailingHeaders.GetValues("Signature").Single());
    }

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
