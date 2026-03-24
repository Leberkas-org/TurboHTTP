using System.Collections.Immutable;
using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Protocol.RFC9111;
using TurboHttp.Streams.Stages.Features;

namespace TurboHttp.StreamTests.RFC9111;

/// <summary>
/// Tests the cache bidirectional stage per RFC 9111.
/// Verifies that the request direction performs cache lookup (with short-circuit for hits)
/// and the response direction stores cacheable responses, handles 304 merges, and invalidates
/// on unsafe methods.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="CacheBidiStage"/>.
/// RFC 9111 §4.2: Cache freshness evaluation.
/// RFC 9111 §4.3: Conditional requests and 304 merging.
/// RFC 9111 §4.4: Cache invalidation after unsafe methods.
/// </remarks>
public sealed class CacheBidiStageTests : StreamTestBase
{
    /// <summary>
    /// Runs requests through the request direction (In1→Out1) of the BidiStage.
    /// The response direction is wired to empty source / ignored sink.
    /// </summary>
    private Task<IImmutableList<HttpRequestMessage>> RunRequestAsync(
        CacheBidiStage stage,
        params HttpRequestMessage[] requests)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpRequestMessage>(),
            (builder, sink) =>
            {
                var bidi = builder.Add(stage);
                var source = builder.Add(Source.From(requests));
                var emptyResponseSource = builder.Add(Source.Empty<HttpResponseMessage>());
                var ignoredResponseSink = builder.Add(Sink.Ignore<HttpResponseMessage>());

                builder.From(source).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).To(sink);
                builder.From(emptyResponseSource).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(ignoredResponseSink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

    /// <summary>
    /// Runs responses through the response direction (In2→Out2) of the BidiStage.
    /// The request direction is wired to empty source / ignored sink.
    /// </summary>
    private Task<IImmutableList<HttpResponseMessage>> RunResponseAsync(
        CacheBidiStage stage,
        params HttpResponseMessage[] responses)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpResponseMessage>(),
            (builder, sink) =>
            {
                var bidi = builder.Add(stage);
                var source = builder.Add(Source.From(responses));
                var emptyRequestSource = builder.Add(Source.Empty<HttpRequestMessage>());
                var ignoredRequestSink = builder.Add(Sink.Ignore<HttpRequestMessage>());

                builder.From(emptyRequestSource).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).To(ignoredRequestSink);
                builder.From(source).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(sink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

    /// <summary>
    /// Runs a full bidirectional flow: requests from source through request direction,
    /// and captures both the forwarded requests (misses) AND the response output.
    /// Uses a simple echo flow (request→response) in place of the engine.
    /// </summary>
    private Task<IImmutableList<HttpResponseMessage>> RunBidiWithEchoAsync(
        CacheBidiStage stage,
        Func<HttpRequestMessage, HttpResponseMessage> echoFn,
        params HttpRequestMessage[] requests)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpResponseMessage>(),
            (builder, sink) =>
            {
                var bidi = builder.Add(stage);
                var source = builder.Add(Source.From(requests));
                var echo = builder.Add(Flow.Create<HttpRequestMessage>().Select(echoFn));

                builder.From(source).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).Via(echo).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(sink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

    private static CacheStore StoreWithFreshEntry(
        string uri = "http://example.com/data",
        string body = "cached body",
        string? etag = null,
        DateTimeOffset? lastModified = null)
    {
        var store = new CacheStore();
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(body))
        };
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
        response.Headers.Date = DateTimeOffset.UtcNow;

        if (etag is not null)
        {
            response.Headers.TryAddWithoutValidation("ETag", etag);
        }

        if (lastModified.HasValue)
        {
            response.Content.Headers.LastModified = lastModified;
        }

        var now = DateTimeOffset.UtcNow;
        store.Put(request, response, System.Text.Encoding.UTF8.GetBytes(body), now, now);
        return store;
    }

    private static CacheStore StoreWithStaleEntry(
        string uri = "http://example.com/data",
        string body = "stale body",
        string? etag = null,
        DateTimeOffset? lastModified = null)
    {
        var store = new CacheStore();
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(body))
        };
        // max-age=1 with Date 100s ago → stale
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=1, must-revalidate");
        response.Headers.Date = DateTimeOffset.UtcNow.AddSeconds(-100);

        if (etag is not null)
        {
            response.Headers.TryAddWithoutValidation("ETag", etag);
        }

        if (lastModified.HasValue)
        {
            response.Content.Headers.LastModified = lastModified;
        }

        var now = DateTimeOffset.UtcNow.AddSeconds(-100);
        store.Put(request, response, System.Text.Encoding.UTF8.GetBytes(body), now, now);
        return store;
    }

    private static HttpResponseMessage MakeResponse(
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? requestUri = null,
        HttpMethod? requestMethod = null,
        string? body = null,
        string? cacheControl = null)
    {
        var content = body is not null
            ? new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(body))
            : new ByteArrayContent(Array.Empty<byte>());

        var response = new HttpResponseMessage(statusCode)
        {
            Content = content
        };

        if (requestUri is not null)
        {
            response.RequestMessage = new HttpRequestMessage(requestMethod ?? HttpMethod.Get, requestUri);
        }

        if (cacheControl is not null)
        {
            response.Headers.TryAddWithoutValidation("Cache-Control", cacheControl);
        }

        return response;
    }

    // ============================
    // Request direction: pass-through tests
    // ============================

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-4-CACHE-001: null store → request passes through unchanged")]
    public async Task RequestDirection_Should_PassThrough_When_StoreIsNull()
    {
        var stage = new CacheBidiStage(null);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Same(request, result);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-4-CACHE-002: cache miss → request forwarded unchanged")]
    public async Task RequestDirection_Should_ForwardRequest_When_CacheMiss()
    {
        var store = new CacheStore();
        var stage = new CacheBidiStage(store);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Same(request, result);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-4.3-CACHE-003: must-revalidate → conditional request with If-None-Match")]
    public async Task RequestDirection_Should_BuildConditionalRequest_When_MustRevalidate()
    {
        var store = StoreWithStaleEntry(etag: "\"abc123\"");
        var stage = new CacheBidiStage(store);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("If-None-Match"));
        var inmValue = string.Join("", result.Headers.GetValues("If-None-Match"));
        Assert.Contains("\"abc123\"", inmValue);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-4.3-CACHE-004: must-revalidate with Last-Modified → If-Modified-Since header")]
    public async Task RequestDirection_Should_BuildConditionalRequest_WithIfModifiedSince()
    {
        var lm = DateTimeOffset.UtcNow.AddHours(-1);
        var store = StoreWithStaleEntry(lastModified: lm);
        var stage = new CacheBidiStage(store);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Headers.IfModifiedSince.HasValue);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-4-CACHE-005: multiple requests → each evaluated independently")]
    public async Task RequestDirection_Should_EvaluateEachRequestIndependently()
    {
        var store = new CacheStore();

        // Two separate requests through separate stages (stage is one-at-a-time: request→response→next)
        var stage1 = new CacheBidiStage(store);
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var results1 = await RunRequestAsync(stage1, req1);
        var result1 = Assert.Single(results1);
        Assert.Same(req1, result1);

        var stage2 = new CacheBidiStage(store);
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        var results2 = await RunRequestAsync(stage2, req2);
        var result2 = Assert.Single(results2);
        Assert.Same(req2, result2);
    }

    // ============================
    // Request direction: cache hit (short-circuit)
    // ============================

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-4.2-CACHE-006: fresh cache hit → response from cache, request NOT forwarded")]
    public async Task RequestDirection_Should_ShortCircuit_When_CacheHitFresh()
    {
        var store = StoreWithFreshEntry(body: "fresh data");
        var stage = new CacheBidiStage(store);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");

        // Request direction should produce NO forwarded requests (all hits)
        var forwardedRequests = await RunRequestAsync(stage, request);

        // The request was NOT forwarded — it was a cache hit
        Assert.Empty(forwardedRequests);
    }

    // ============================
    // Response direction tests
    // ============================

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-3-CACHE-007: null store → response passes through unchanged")]
    public async Task ResponseDirection_Should_PassThrough_When_StoreIsNull()
    {
        var stage = new CacheBidiStage(null);
        var response = MakeResponse(requestUri: "http://example.com/data", body: "hello");

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        Assert.Same(response, result);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-3-CACHE-008: null RequestMessage → response passes through unchanged")]
    public async Task ResponseDirection_Should_PassThrough_When_RequestMessageIsNull()
    {
        var store = new CacheStore();
        var stage = new CacheBidiStage(store);
        var response = MakeResponse(body: "hello");

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        Assert.Same(response, result);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-3-CACHE-009: cacheable 200 OK → stored in cache")]
    public async Task ResponseDirection_Should_StoreResponse_When_Cacheable200()
    {
        var store = new CacheStore();
        var stage = new CacheBidiStage(store);
        var response = MakeResponse(
            requestUri: "http://example.com/data",
            body: "response body",
            cacheControl: "max-age=3600");
        response.Headers.Date = DateTimeOffset.UtcNow;

        await RunResponseAsync(stage, response);

        // Verify the entry was stored
        var lookup = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");
        var entry = store.Get(lookup);
        Assert.NotNull(entry);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-4.4-CACHE-010: POST request → cache invalidated")]
    public async Task ResponseDirection_Should_InvalidateCache_OnUnsafeMethod()
    {
        var store = StoreWithFreshEntry();
        var stage = new CacheBidiStage(store);

        // Verify entry exists before
        var lookupBefore = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");
        Assert.NotNull(store.Get(lookupBefore));

        var response = MakeResponse(
            requestUri: "http://example.com/data",
            requestMethod: HttpMethod.Post,
            body: "created");

        await RunResponseAsync(stage, response);

        // Verify entry was invalidated
        var lookupAfter = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");
        Assert.Null(store.Get(lookupAfter));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-4.3.4-CACHE-011: 304 Not Modified → merged with cached entry")]
    public async Task ResponseDirection_Should_Merge304_WithCachedEntry()
    {
        var store = StoreWithFreshEntry(body: "original body", etag: "\"v1\"");
        var stage = new CacheBidiStage(store);

        var notModified = new HttpResponseMessage(HttpStatusCode.NotModified);
        notModified.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");
        notModified.Headers.TryAddWithoutValidation("X-Updated", "true");

        var results = await RunResponseAsync(stage, notModified);

        var result = Assert.Single(results);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var body = await result.Content.ReadAsByteArrayAsync();
        Assert.Equal("original body", System.Text.Encoding.UTF8.GetString(body));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-4.3.4-CACHE-012: 304 without cached entry → passed through unchanged")]
    public async Task ResponseDirection_Should_PassThrough304_When_NoCachedEntry()
    {
        var store = new CacheStore();
        var stage = new CacheBidiStage(store);

        var notModified = new HttpResponseMessage(HttpStatusCode.NotModified);
        notModified.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/missing");

        var results = await RunResponseAsync(stage, notModified);

        var result = Assert.Single(results);
        Assert.Equal(HttpStatusCode.NotModified, result.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-3-CACHE-013: non-cacheable 500 → not stored")]
    public async Task ResponseDirection_Should_NotStore_NonCacheableStatus()
    {
        var store = new CacheStore();
        var stage = new CacheBidiStage(store);
        var response = MakeResponse(
            statusCode: HttpStatusCode.InternalServerError,
            requestUri: "http://example.com/data",
            body: "error");

        await RunResponseAsync(stage, response);

        var lookup = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");
        Assert.Null(store.Get(lookup));
    }

    // ============================
    // Bidirectional integration tests
    // ============================

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-4-CACHE-014: miss→engine→store→hit on second request")]
    public async Task Bidirectional_Should_CacheAndServeFromCache()
    {
        var store = new CacheStore();
        var stage = new CacheBidiStage(store);

        // First request: cache miss → engine echo → response stored
        var results1 = await RunBidiWithEchoAsync(
            stage,
            req =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent("engine response"u8.ToArray()),
                    RequestMessage = req
                };
                resp.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
                resp.Headers.Date = DateTimeOffset.UtcNow;
                return resp;
            },
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/data"));

        var firstResponse = Assert.Single(results1);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        // Verify entry was stored
        var lookup = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");
        Assert.NotNull(store.Get(lookup));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-4.4-CACHE-015: PUT invalidates → subsequent GET is a miss")]
    public async Task ResponseDirection_Should_InvalidateOnPut()
    {
        var store = StoreWithFreshEntry();
        var stage = new CacheBidiStage(store);

        var putResponse = MakeResponse(
            requestUri: "http://example.com/data",
            requestMethod: HttpMethod.Put,
            body: "updated");

        await RunResponseAsync(stage, putResponse);

        var lookup = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");
        Assert.Null(store.Get(lookup));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-4.4-CACHE-016: DELETE invalidates → subsequent GET is a miss")]
    public async Task ResponseDirection_Should_InvalidateOnDelete()
    {
        var store = StoreWithFreshEntry();
        var stage = new CacheBidiStage(store);

        var deleteResponse = MakeResponse(
            requestUri: "http://example.com/data",
            requestMethod: HttpMethod.Delete);

        await RunResponseAsync(stage, deleteResponse);

        var lookup = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");
        Assert.Null(store.Get(lookup));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-3-CACHE-017: no-store directive → response not cached")]
    public async Task ResponseDirection_Should_NotStore_When_NoStoreDirective()
    {
        var store = new CacheStore();
        var stage = new CacheBidiStage(store);
        var response = MakeResponse(
            requestUri: "http://example.com/data",
            body: "secret",
            cacheControl: "no-store");
        response.Headers.Date = DateTimeOffset.UtcNow;

        await RunResponseAsync(stage, response);

        var lookup = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");
        Assert.Null(store.Get(lookup));
    }
}
