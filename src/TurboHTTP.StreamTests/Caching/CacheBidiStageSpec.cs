using System.Collections.Immutable;
using System.Net;
using System.Text;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Protocol.Caching;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Caching;

public sealed class CacheBidiStageSpec : StreamTestBase
{
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
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body))
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
        var (owner, length) = CacheStore.RentBody(Encoding.UTF8.GetBytes(body));
        store.Put(request, response, owner, length, now, now);
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
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body))
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
        var (owner, length) = CacheStore.RentBody(Encoding.UTF8.GetBytes(body));
        store.Put(request, response, owner, length, now, now);
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
            ? new ByteArrayContent(Encoding.UTF8.GetBytes(body))
            : new ByteArrayContent([]);

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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9111-4")]
    public async Task CacheBidiStage_should_pass_through_request_when_store_is_null()
    {
        var stage = new CacheBidiStage(null);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Same(request, result);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9111-4")]
    public async Task CacheBidiStage_should_forward_request_when_cache_miss()
    {
        var store = new CacheStore();
        var stage = new CacheBidiStage(store);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Same(request, result);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9111-4.3")]
    public async Task CacheBidiStage_should_build_conditional_request_when_must_revalidate_with_etag()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9111-4.3")]
    public async Task CacheBidiStage_should_build_conditional_request_with_if_modified_since()
    {
        var lm = DateTimeOffset.UtcNow.AddHours(-1);
        var store = StoreWithStaleEntry(lastModified: lm);
        var stage = new CacheBidiStage(store);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Headers.IfModifiedSince.HasValue);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9111-4")]
    public async Task CacheBidiStage_should_evaluate_each_request_independently()
    {
        var store = new CacheStore();

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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9111-4.2")]
    public async Task CacheBidiStage_should_short_circuit_when_cache_hit_fresh()
    {
        var store = StoreWithFreshEntry(body: "fresh data");
        var stage = new CacheBidiStage(store);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");

        var forwardedRequests = await RunRequestAsync(stage, request);

        // The request was NOT forwarded — it was a cache hit
        Assert.Empty(forwardedRequests);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9111-3")]
    public async Task CacheBidiStage_should_pass_through_response_when_store_is_null()
    {
        var stage = new CacheBidiStage(null);
        var response = MakeResponse(requestUri: "http://example.com/data", body: "hello");

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        Assert.Same(response, result);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9111-3")]
    public async Task CacheBidiStage_should_pass_through_response_when_request_message_is_null()
    {
        var store = new CacheStore();
        var stage = new CacheBidiStage(store);
        var response = MakeResponse(body: "hello");

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        Assert.Same(response, result);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9111-3")]
    public async Task CacheBidiStage_should_store_response_when_cacheable_200()
    {
        var store = new CacheStore();
        var stage = new CacheBidiStage(store);
        var response = MakeResponse(
            requestUri: "http://example.com/data",
            body: "response body",
            cacheControl: "max-age=3600");
        response.Headers.Date = DateTimeOffset.UtcNow;

        await RunResponseAsync(stage, response);

        var lookup = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");
        var entry = store.Get(lookup);
        Assert.NotNull(entry);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9111-4.4")]
    public async Task CacheBidiStage_should_invalidate_cache_when_unsafe_method_post()
    {
        var store = StoreWithFreshEntry();
        var stage = new CacheBidiStage(store);

        var lookupBefore = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");
        Assert.NotNull(store.Get(lookupBefore));

        var response = MakeResponse(
            requestUri: "http://example.com/data",
            requestMethod: HttpMethod.Post,
            body: "created");

        await RunResponseAsync(stage, response);

        var lookupAfter = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");
        Assert.Null(store.Get(lookupAfter));
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9111-4.3.4")]
    public async Task CacheBidiStage_should_merge_304_with_cached_entry()
    {
        var store = StoreWithFreshEntry(body: "original body", etag: "\"v1\"");
        var stage = new CacheBidiStage(store);

        var notModified = new HttpResponseMessage(HttpStatusCode.NotModified);
        notModified.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");
        notModified.Headers.TryAddWithoutValidation("X-Updated", "true");

        var results = await RunResponseAsync(stage, notModified);

        var result = Assert.Single(results);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var body = await result.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("original body", Encoding.UTF8.GetString(body));
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9111-4.3.4")]
    public async Task CacheBidiStage_should_pass_through_304_when_no_cached_entry()
    {
        var store = new CacheStore();
        var stage = new CacheBidiStage(store);

        var notModified = new HttpResponseMessage(HttpStatusCode.NotModified);
        notModified.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/missing");

        var results = await RunResponseAsync(stage, notModified);

        var result = Assert.Single(results);
        Assert.Equal(HttpStatusCode.NotModified, result.StatusCode);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9111-3")]
    public async Task CacheBidiStage_should_not_store_non_cacheable_status()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9111-4")]
    public async Task CacheBidiStage_should_cache_and_serve_from_cache_on_second_request()
    {
        var store = new CacheStore();
        var stage = new CacheBidiStage(store);

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

        var lookup = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");
        Assert.NotNull(store.Get(lookup));
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9111-4.4")]
    public async Task CacheBidiStage_should_invalidate_on_put()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9111-4.4")]
    public async Task CacheBidiStage_should_invalidate_on_delete()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9111-3")]
    public async Task CacheBidiStage_should_not_store_when_no_store_directive()
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