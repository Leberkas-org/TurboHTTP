using System.Net;
using Akka.Streams.Dsl;
using TurboHttp.Protocol;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Streams;

public sealed class CacheStorageStageTests : StreamTestBase
{
    // ── helpers ────────────────────────────────────────────────────────────────

    /// <summary>Materialises a CacheStorageStage and collects all output responses.</summary>
    private async Task<List<HttpResponseMessage>> RunAsync(
        HttpCacheStore store,
        params HttpResponseMessage[] responses)
    {
        var result = await Source.From(responses)
            .Via(new CacheStorageStage(store))
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        return [.. result];
    }

    /// <summary>Creates a 200 OK response with the given cache-control and an attached request.</summary>
    private static HttpResponseMessage MakeResponse(
        string url,
        HttpMethod method,
        HttpStatusCode status = HttpStatusCode.OK,
        string? cacheControl = "max-age=3600",
        byte[]? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        var response = new HttpResponseMessage(status)
        {
            RequestMessage = request,
            Content = new ByteArrayContent(body ?? Array.Empty<byte>())
        };

        if (cacheControl is not null)
        {
            response.Headers.TryAddWithoutValidation("Cache-Control", cacheControl);
        }

        response.Headers.Date = DateTimeOffset.UtcNow;
        return response;
    }

    /// <summary>Populates a store with a fresh GET entry (max-age=3600) for the given URL.</summary>
    private static HttpCacheStore StoreWithEntry(
        string url,
        string cacheControl = "max-age=3600",
        string? etag = null,
        byte[]? body = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        var resp = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body ?? Array.Empty<byte>())
        };
        resp.Headers.TryAddWithoutValidation("Cache-Control", cacheControl);
        resp.Headers.Date = DateTimeOffset.UtcNow;

        if (etag is not null)
        {
            resp.Headers.TryAddWithoutValidation("ETag", etag);
        }

        var store = new HttpCacheStore();
        store.Put(req, resp, body ?? Array.Empty<byte>(),
            DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow);
        return store;
    }

    // ── 2xx storage ────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "CSTR-001: 2xx cacheable response → stored in cache")]
    public async Task CSTR_001_CacheableResponse_StoredInCache()
    {
        const string url = "http://example.com/resource";
        var store = new HttpCacheStore();
        var response = MakeResponse(url, HttpMethod.Get, cacheControl: "max-age=3600");

        await RunAsync(store, response);

        var entry = store.Get(response.RequestMessage!);
        Assert.NotNull(entry);
        Assert.Equal(HttpStatusCode.OK, entry.Response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "CSTR-002: 2xx cacheable response → passed through downstream unchanged")]
    public async Task CSTR_002_CacheableResponse_PassedThroughDownstream()
    {
        const string url = "http://example.com/resource";
        var store = new HttpCacheStore();
        var response = MakeResponse(url, HttpMethod.Get, cacheControl: "max-age=3600");

        var results = await RunAsync(store, response);

        Assert.Single(results);
        Assert.Same(response, results[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "CSTR-003: 2xx with no-store directive → not stored in cache")]
    public async Task CSTR_003_NoStoreCacheControl_NotStored()
    {
        const string url = "http://example.com/resource";
        var store = new HttpCacheStore();
        var response = MakeResponse(url, HttpMethod.Get, cacheControl: "no-store");

        await RunAsync(store, response);

        Assert.Equal(0, store.Count);
    }

    [Fact(Timeout = 10_000, DisplayName = "CSTR-004: 2xx with body → body stored in cache entry")]
    public async Task CSTR_004_CacheableResponseWithBody_BodyStoredInEntry()
    {
        const string url = "http://example.com/resource";
        var store = new HttpCacheStore();
        var bodyBytes = "hello world"u8.ToArray();
        var response = MakeResponse(url, HttpMethod.Get, cacheControl: "max-age=600", body: bodyBytes);

        await RunAsync(store, response);

        var entry = store.Get(response.RequestMessage!);
        Assert.NotNull(entry);
        Assert.Equal(bodyBytes, entry.Body);
    }

    // ── 304 Not Modified ──────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "CSTR-005: 304 Not Modified with cached entry → merged 200 pushed downstream")]
    public async Task CSTR_005_NotModified_MergesWithCachedEntry_Pushes200()
    {
        const string url = "http://example.com/resource";
        var store = StoreWithEntry(url, etag: "\"v1\"");

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var notModified = new HttpResponseMessage(HttpStatusCode.NotModified)
        {
            RequestMessage = request
        };
        notModified.Headers.TryAddWithoutValidation("ETag", "\"v1\"");

        var results = await RunAsync(store, notModified);

        Assert.Single(results);
        Assert.Equal(HttpStatusCode.OK, results[0].StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "CSTR-006: 304 Not Modified → merged response headers override cached headers")]
    public async Task CSTR_006_NotModified_NewHeadersOverrideCached()
    {
        const string url = "http://example.com/resource";
        var store = StoreWithEntry(url, etag: "\"v1\"");

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var notModified = new HttpResponseMessage(HttpStatusCode.NotModified)
        {
            RequestMessage = request
        };
        notModified.Headers.TryAddWithoutValidation("ETag", "\"v2\"");

        var results = await RunAsync(store, notModified);

        Assert.Single(results);
        Assert.Equal(HttpStatusCode.OK, results[0].StatusCode);
        // The newer ETag from the 304 should be present
        Assert.Contains("v2", string.Join("", results[0].Headers.GetValues("ETag")));
    }

    [Fact(Timeout = 10_000, DisplayName = "CSTR-007: 304 Not Modified without cached entry → original 304 passed through")]
    public async Task CSTR_007_NotModified_NoCachedEntry_PassesThrough()
    {
        const string url = "http://example.com/resource";
        var store = new HttpCacheStore();

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var notModified = new HttpResponseMessage(HttpStatusCode.NotModified)
        {
            RequestMessage = request
        };

        var results = await RunAsync(store, notModified);

        Assert.Single(results);
        Assert.Equal(HttpStatusCode.NotModified, results[0].StatusCode);
    }

    // ── unsafe method invalidation ────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "CSTR-008: POST response → cached entry for URI invalidated")]
    public async Task CSTR_008_PostResponse_InvalidatesCachedEntry()
    {
        const string url = "http://example.com/resource";
        var store = StoreWithEntry(url);
        Assert.Equal(1, store.Count);

        var response = MakeResponse(url, HttpMethod.Post, status: HttpStatusCode.OK);
        await RunAsync(store, response);

        Assert.Equal(0, store.Count);
    }

    [Fact(Timeout = 10_000, DisplayName = "CSTR-009: PUT response → cached entry for URI invalidated")]
    public async Task CSTR_009_PutResponse_InvalidatesCachedEntry()
    {
        const string url = "http://example.com/resource";
        var store = StoreWithEntry(url);
        Assert.Equal(1, store.Count);

        var response = MakeResponse(url, HttpMethod.Put, status: HttpStatusCode.NoContent, cacheControl: null);
        await RunAsync(store, response);

        Assert.Equal(0, store.Count);
    }

    [Fact(Timeout = 10_000, DisplayName = "CSTR-010: DELETE response → cached entry for URI invalidated")]
    public async Task CSTR_010_DeleteResponse_InvalidatesCachedEntry()
    {
        const string url = "http://example.com/resource";
        var store = StoreWithEntry(url);
        Assert.Equal(1, store.Count);

        var response = MakeResponse(url, HttpMethod.Delete, status: HttpStatusCode.NoContent, cacheControl: null);
        await RunAsync(store, response);

        Assert.Equal(0, store.Count);
    }

    [Fact(Timeout = 10_000, DisplayName = "CSTR-011: PATCH response → cached entry for URI invalidated")]
    public async Task CSTR_011_PatchResponse_InvalidatesCachedEntry()
    {
        const string url = "http://example.com/resource";
        var store = StoreWithEntry(url);
        Assert.Equal(1, store.Count);

        var response = MakeResponse(url, HttpMethod.Patch, status: HttpStatusCode.OK, cacheControl: null);
        await RunAsync(store, response);

        Assert.Equal(0, store.Count);
    }

    // ── null request message ──────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "CSTR-012: null RequestMessage → response passed through without exception")]
    public async Task CSTR_012_NullRequestMessage_PassesThroughSafely()
    {
        var store = new HttpCacheStore();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = null
        };

        var results = await RunAsync(store, response);

        Assert.Single(results);
        Assert.Same(response, results[0]);
        Assert.Equal(0, store.Count);
    }
}
