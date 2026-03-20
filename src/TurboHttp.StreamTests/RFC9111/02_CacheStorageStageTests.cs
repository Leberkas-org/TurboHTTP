using System.IO;
using System.Net;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHttp.Protocol.RFC9111;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.RFC9111;

/// <summary>
/// Tests the cache storage stage per RFC 9111.
/// Verifies that cacheable responses are stored correctly and non-cacheable responses pass through unmodified.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="CacheStorageStage"/>.
/// RFC 9111 §3: Storing responses, cache-control directives, and Vary header handling.
/// </remarks>
public sealed class CacheStorageStageTests : StreamTestBase
{
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

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-3-CSTR-001: 2xx cacheable response → stored in cache")]
    public async Task Should_StoreInCache_When_ResponseIsCacheable2xx()
    {
        const string url = "http://example.com/resource";
        var store = new HttpCacheStore();
        var response = MakeResponse(url, HttpMethod.Get, cacheControl: "max-age=3600");

        await RunAsync(store, response);

        var entry = store.Get(response.RequestMessage!);
        Assert.NotNull(entry);
        Assert.Equal(HttpStatusCode.OK, entry.Response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-3-CSTR-002: 2xx cacheable response → passed through downstream unchanged")]
    public async Task Should_PassThroughDownstream_When_ResponseIsCacheable2xx()
    {
        const string url = "http://example.com/resource";
        var store = new HttpCacheStore();
        var response = MakeResponse(url, HttpMethod.Get, cacheControl: "max-age=3600");

        var results = await RunAsync(store, response);

        Assert.Single(results);
        Assert.Same(response, results[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-3-CSTR-003: 2xx with no-store directive → not stored in cache")]
    public async Task Should_NotStoreInCache_When_NoStoreCacheControlPresent()
    {
        const string url = "http://example.com/resource";
        var store = new HttpCacheStore();
        var response = MakeResponse(url, HttpMethod.Get, cacheControl: "no-store");

        await RunAsync(store, response);

        Assert.Equal(0, store.Count);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-3-CSTR-004: 2xx with body → body stored in cache entry")]
    public async Task Should_StoreBodyInCacheEntry_When_ResponseIsCacheableWith2xxBody()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-3-CSTR-005: 304 Not Modified with cached entry → merged 200 pushed downstream")]
    public async Task Should_MergeCachedEntryAndPush200_When_304NotModifiedReceived()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-3-CSTR-006: 304 Not Modified → merged response headers override cached headers")]
    public async Task Should_OverrideCachedHeadersWithNew_When_304NotModifiedReceived()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-3-CSTR-007: 304 Not Modified without cached entry → original 304 passed through")]
    public async Task Should_PassThrough304_When_NoCachedEntryExists()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-3-CSTR-008: POST response → cached entry for URI invalidated")]
    public async Task Should_InvalidateCachedEntry_When_PostResponseReceived()
    {
        const string url = "http://example.com/resource";
        var store = StoreWithEntry(url);
        Assert.Equal(1, store.Count);

        var response = MakeResponse(url, HttpMethod.Post, status: HttpStatusCode.OK);
        await RunAsync(store, response);

        Assert.Equal(0, store.Count);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-3-CSTR-009: PUT response → cached entry for URI invalidated")]
    public async Task Should_InvalidateCachedEntry_When_PutResponseReceived()
    {
        const string url = "http://example.com/resource";
        var store = StoreWithEntry(url);
        Assert.Equal(1, store.Count);

        var response = MakeResponse(url, HttpMethod.Put, status: HttpStatusCode.NoContent, cacheControl: null);
        await RunAsync(store, response);

        Assert.Equal(0, store.Count);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-3-CSTR-010: DELETE response → cached entry for URI invalidated")]
    public async Task Should_InvalidateCachedEntry_When_DeleteResponseReceived()
    {
        const string url = "http://example.com/resource";
        var store = StoreWithEntry(url);
        Assert.Equal(1, store.Count);

        var response = MakeResponse(url, HttpMethod.Delete, status: HttpStatusCode.NoContent, cacheControl: null);
        await RunAsync(store, response);

        Assert.Equal(0, store.Count);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-3-CSTR-011: PATCH response → cached entry for URI invalidated")]
    public async Task Should_InvalidateCachedEntry_When_PatchResponseReceived()
    {
        const string url = "http://example.com/resource";
        var store = StoreWithEntry(url);
        Assert.Equal(1, store.Count);

        var response = MakeResponse(url, HttpMethod.Patch, status: HttpStatusCode.OK, cacheControl: null);
        await RunAsync(store, response);

        Assert.Equal(0, store.Count);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-3-CSTR-012: null RequestMessage → response passed through without exception")]
    public async Task Should_PassThroughSafely_When_RequestMessageIsNull()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-3-CSTR-013: sync fast-path — ByteArrayContent body stored correctly")]
    public async Task Should_StoreBodyCorrectly_When_SyncFastPathWithByteArrayContent()
    {
        const string url = "http://example.com/sync-body";
        var store = new HttpCacheStore();
        var bodyBytes = "sync fast-path body"u8.ToArray();
        var response = MakeResponse(url, HttpMethod.Get, cacheControl: "max-age=600", body: bodyBytes);

        var results = await RunAsync(store, response);

        Assert.Single(results);
        var entry = store.Get(response.RequestMessage!);
        Assert.NotNull(entry);
        Assert.Equal(bodyBytes, entry.Body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-3-CSTR-014: sync fast-path — response pushed downstream after body stored")]
    public async Task Should_PushResponseAfterBodyStored_When_SyncFastPathUsed()
    {
        const string url = "http://example.com/sync-order";
        var store = new HttpCacheStore();
        var bodyBytes = "order check"u8.ToArray();
        var response = MakeResponse(url, HttpMethod.Get, cacheControl: "max-age=600", body: bodyBytes);

        var results = await RunAsync(store, response);

        // Body must be stored before downstream sees the response
        Assert.Single(results);
        Assert.Same(response, results[0]);
        var entry = store.Get(response.RequestMessage!);
        Assert.NotNull(entry);
        Assert.Equal(bodyBytes, entry.Body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-3-CSTR-015: async path — StreamContent body stored correctly")]
    public async Task Should_StoreBodyCorrectly_When_AsyncPathWithStreamContent()
    {
        const string url = "http://example.com/async-body";
        var store = new HttpCacheStore();
        var bodyBytes = "async stream body"u8.ToArray();

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StreamContent(new MemoryStream(bodyBytes))
        };
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
        response.Headers.Date = DateTimeOffset.UtcNow;

        var results = await RunAsync(store, response);

        Assert.Single(results);
        var entry = store.Get(request);
        Assert.NotNull(entry);
        Assert.Equal(bodyBytes, entry.Body);
    }

    [Fact(DisplayName = "RFC9111-3-CSTR-017: upstream failure → stage absorbs it, downstream not faulted")]
    public void Should_AbsorbUpstreamFailure_WhenUpstreamFails()
    {
        var store = new HttpCacheStore();
        var publisher = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var subscriber = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        Source.FromPublisher(publisher)
            .Via(new CacheStorageStage(store))
            .RunWith(Sink.FromSubscriber(subscriber), Materializer);

        var pubSub = publisher.ExpectSubscription();
        var clientSub = subscriber.ExpectSubscription();
        clientSub.Request(10);

        // Fail upstream — stage must absorb, downstream must NOT see error
        pubSub.SendError(new Exception("upstream boom"));

        subscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-3-CSTR-016: async path — response pushed downstream after body read")]
    public async Task Should_PushResponseAfterBodyRead_When_AsyncPathUsed()
    {
        const string url = "http://example.com/async-order";
        var store = new HttpCacheStore();
        var bodyBytes = "async order check"u8.ToArray();

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StreamContent(new MemoryStream(bodyBytes))
        };
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
        response.Headers.Date = DateTimeOffset.UtcNow;

        var results = await RunAsync(store, response);

        Assert.Single(results);
        Assert.Same(response, results[0]);
        var entry = store.Get(request);
        Assert.NotNull(entry);
        Assert.Equal(bodyBytes, entry.Body);
    }
}
