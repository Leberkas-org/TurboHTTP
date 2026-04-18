using System.Net;
using Akka.Streams.Dsl;
using TurboHTTP.Protocol.Caching;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H11;

public sealed class CacheSpec : AcceptanceTestBase
{
    private async Task<HttpResponseMessage> SendAsync(ResponseMap map, HttpRequestMessage request,
        CacheStore store, CachePolicy? policy = null)
    {
        var cache = BidiFlow.FromGraph(new CacheBidiStage(store, policy));
        var fake = ResponseMapFake.Create(map);
        var flow = cache.Atop(fake)
            .Join(Flow.FromFunction<HttpRequestMessage, HttpResponseMessage>(_ => new HttpResponseMessage()));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private static HttpResponseMessage CacheableResponse(string body, string cacheControl,
        string? etag = null, DateTimeOffset? lastModified = null, string? vary = null,
        DateTimeOffset? expires = null)
    {
        var r = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body)
        };
        r.Headers.CacheControl = System.Net.Http.Headers.CacheControlHeaderValue.Parse(cacheControl);
        if (etag is not null)
        {
            r.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue($"\"{etag}\"");
        }

        if (lastModified is not null)
        {
            r.Content.Headers.LastModified = lastModified;
        }

        if (vary is not null)
        {
            r.Headers.TryAddWithoutValidation("Vary", vary);
        }

        if (expires is not null)
        {
            r.Content.Headers.Expires = expires;
            r.Headers.Date = DateTimeOffset.UtcNow;
        }

        return r;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-5.2.2.1")]
    public async Task Cache_should_serve_max_age_response_from_cache()
    {
        var callCount = 0;
        var map = new ResponseMap()
            .On("/cache/max-age/3600", _ =>
            {
                callCount++;
                return CacheableResponse($"max-age-body-{callCount}", "max-age=3600");
            });

        var store = new CacheStore(CachePolicy.Default);

        var response1 = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/max-age/3600"), store);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrEmpty(body1), "First response body should be non-empty");

        var response2 = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/max-age/3600"), store);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(body1, body2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-5.2.2.2")]
    public async Task Cache_should_force_revalidation_with_no_cache()
    {
        var callCount = 0;
        var map = new ResponseMap()
            .On("/cache/no-cache", _ =>
            {
                callCount++;
                return CacheableResponse($"no-cache-body-{callCount}", "no-cache");
            });

        var store = new CacheStore(CachePolicy.Default);

        var response1 = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/no-cache"), store);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        var response2 = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/no-cache"), store);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(body1, body2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-5.2.2.5")]
    public async Task Cache_should_never_cache_no_store_response()
    {
        var map = new ResponseMap()
            .On("/cache/no-store", _ => CacheableResponse("no-store-resource", "no-store"));

        var store = new CacheStore(CachePolicy.Default);

        var response1 = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/no-store"), store);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        var response2 = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/no-store"), store);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal("no-store-resource", body1);
        Assert.Equal("no-store-resource", body2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-4.3.2")]
    public async Task Cache_should_send_if_none_match_on_etag_revalidation()
    {
        var map = new ResponseMap()
            .On("/cache/etag/test1", _ => CacheableResponse("etag-resource-test1", "max-age=3600",
                etag: "etag-test1"));

        var store = new CacheStore(CachePolicy.Default);

        var response1 = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/etag/test1"), store);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("etag-resource-test1", body1);

        var response2 = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/etag/test1"), store);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(body1, body2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-4.3.2")]
    public async Task Cache_should_send_if_modified_since_on_last_modified_revalidation()
    {
        var map = new ResponseMap()
            .On("/cache/last-modified/doc1", _ => CacheableResponse("last-modified-resource-doc1",
                "max-age=3600", lastModified: DateTimeOffset.UtcNow.AddHours(-1)));

        var store = new CacheStore(CachePolicy.Default);

        var response1 = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/last-modified/doc1"), store);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("last-modified-resource-doc1", body1);

        var response2 = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/last-modified/doc1"), store);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(body1, body2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-4.1")]
    public async Task Cache_should_produce_different_cache_entries_per_vary_header_value()
    {
        var map = new ResponseMap()
            .On("/cache/vary/Accept-Language", req =>
            {
                var lang = req.Headers.AcceptLanguage.FirstOrDefault()?.Value ?? "unknown";
                return CacheableResponse($"vary-Accept-Language:{lang}", "max-age=3600",
                    vary: "Accept-Language");
            });

        var store = new CacheStore(CachePolicy.Default);

        var request1 = new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/vary/Accept-Language");
        request1.Headers.TryAddWithoutValidation("Accept-Language", "en");
        var response1 = await SendAsync(map, request1, store);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("vary-Accept-Language:en", body1);

        var request2 = new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/vary/Accept-Language");
        request2.Headers.TryAddWithoutValidation("Accept-Language", "de");
        var response2 = await SendAsync(map, request2, store);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("vary-Accept-Language:de", body2);

        Assert.NotEqual(body1, body2);

        var request3 = new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/vary/Accept-Language");
        request3.Headers.TryAddWithoutValidation("Accept-Language", "en");
        var response3 = await SendAsync(map, request3, store);
        Assert.Equal(HttpStatusCode.OK, response3.StatusCode);
        var body3 = await response3.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(body1, body3);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-5.2.2.2")]
    public async Task Cache_should_force_revalidation_with_must_revalidate()
    {
        var callCount = 0;
        var map = new ResponseMap()
            .On("/cache/must-revalidate", req =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return CacheableResponse("must-revalidate-body", "must-revalidate, max-age=0",
                        etag: "mr-etag-1");
                }

                if (req.Headers.IfNoneMatch.Any())
                {
                    var r = new HttpResponseMessage(HttpStatusCode.NotModified);
                    r.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"mr-etag-1\"");
                    return r;
                }

                return CacheableResponse("must-revalidate-body-new", "must-revalidate, max-age=0",
                    etag: "mr-etag-2");
            });

        var store = new CacheStore(CachePolicy.Default);

        var response1 = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/must-revalidate"), store);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        var response2 = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/must-revalidate"), store);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(body1, body2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-5.2.2.10")]
    public async Task Cache_should_respect_s_maxage_by_shared_cache()
    {
        var callCount = 0;
        var map = new ResponseMap()
            .On("/cache/s-maxage/3600", _ =>
            {
                callCount++;
                return CacheableResponse($"s-maxage-body-{callCount}", "s-maxage=3600");
            });

        var policy = new CachePolicy { SharedCache = true };
        var store = new CacheStore(policy);

        var response1 = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/s-maxage/3600"), store, policy);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        var response2 = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/s-maxage/3600"), store, policy);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(body1, body2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-5.3")]
    public async Task Cache_should_enable_caching_with_expires_header()
    {
        var callCount = 0;
        var map = new ResponseMap()
            .On("/cache/expires", _ =>
            {
                callCount++;
                return CacheableResponse($"expires-body-{callCount}", "public",
                    expires: DateTimeOffset.UtcNow.AddHours(1));
            });

        var store = new CacheStore(CachePolicy.Default);

        var response1 = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/expires"), store);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        var response2 = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/expires"), store);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(body1, body2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-5.2.2.7")]
    public async Task Cache_should_cache_private_response_by_private_cache()
    {
        var callCount = 0;
        var map = new ResponseMap()
            .On("/cache/private", _ =>
            {
                callCount++;
                return CacheableResponse($"private-body-{callCount}", "private, max-age=3600");
            });

        var store = new CacheStore(CachePolicy.Default);

        var response1 = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/private"), store);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        var response2 = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/private"), store);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(body1, body2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-3.1")]
    public async Task Cache_should_not_cache_private_response_by_shared_cache()
    {
        var callCount = 0;
        var map = new ResponseMap()
            .On("/cache/private", _ =>
            {
                callCount++;
                return CacheableResponse($"private-body-{callCount}", "private, max-age=3600");
            });

        var policy = new CachePolicy { SharedCache = true };
        var store = new CacheStore(policy);

        var response1 = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/private"), store, policy);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        var response2 = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/private"), store, policy);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(body1, body2);
    }
}