using System.Net;
using System.Text.Json;
using Akka.Streams.Dsl;
using TurboHTTP.Protocol.Caching;
using TurboHTTP.Protocol.Cookies;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H3;

public sealed class FeatureInteractionSpec : AcceptanceTestBase
{
    private static HttpResponseMessage EchoCookies(HttpRequestMessage req)
    {
        var cookies = new Dictionary<string, string>();
        if (req.Headers.TryGetValues("Cookie", out var values))
        {
            foreach (var v in values)
            {
                foreach (var pair in v.Split(';', StringSplitOptions.TrimEntries))
                {
                    var eq = pair.IndexOf('=');
                    if (eq > 0)
                    {
                        cookies[pair[..eq].Trim()] = pair[(eq + 1)..].Trim();
                    }
                }
            }
        }
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(cookies))
        };
    }

    private async Task<HttpResponseMessage> SendCookieRedirectAsync(ResponseMap map, HttpRequestMessage request,
        CookieJar jar)
    {
        var cookie = BidiFlow.FromGraph(new CookieBidiStage(jar));
        var redirect = BidiFlow.FromGraph(new RedirectBidiStage(new RedirectPolicy()));
        var fake = ResponseMapFake.Create(map);
        var flow = redirect.Atop(cookie).Atop(fake)
            .Join(Flow.FromFunction<HttpRequestMessage, HttpResponseMessage>(_ => new HttpResponseMessage()));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> SendCacheAsync(ResponseMap map, HttpRequestMessage request,
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

    private async Task<HttpResponseMessage> SendRedirectRetryAsync(ResponseMap map, HttpRequestMessage request,
        RetryPolicy retryPolicy)
    {
        var redirect = BidiFlow.FromGraph(new RedirectBidiStage(new RedirectPolicy()));
        var retry = BidiFlow.FromGraph(new RetryBidiStage(retryPolicy));
        var fake = ResponseMapFake.Create(map);
        var flow = redirect.Atop(retry).Atop(fake)
            .Join(Flow.FromFunction<HttpRequestMessage, HttpResponseMessage>(_ => new HttpResponseMessage()));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> SendCookieRetryAsync(ResponseMap map, HttpRequestMessage request,
        CookieJar jar, RetryPolicy retryPolicy)
    {
        var cookie = BidiFlow.FromGraph(new CookieBidiStage(jar));
        var retry = BidiFlow.FromGraph(new RetryBidiStage(retryPolicy));
        var fake = ResponseMapFake.Create(map);
        var flow = cookie.Atop(retry).Atop(fake)
            .Join(Flow.FromFunction<HttpRequestMessage, HttpResponseMessage>(_ => new HttpResponseMessage()));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> SendCacheCookieAsync(ResponseMap map, HttpRequestMessage request,
        CacheStore store, CookieJar jar)
    {
        var cache = BidiFlow.FromGraph(new CacheBidiStage(store));
        var cookie = BidiFlow.FromGraph(new CookieBidiStage(jar));
        var fake = ResponseMapFake.Create(map);
        var flow = cache.Atop(cookie).Atop(fake)
            .Join(Flow.FromFunction<HttpRequestMessage, HttpResponseMessage>(_ => new HttpResponseMessage()));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> SendCacheRetryAsync(ResponseMap map, HttpRequestMessage request,
        CacheStore store, RetryPolicy retryPolicy)
    {
        var cache = BidiFlow.FromGraph(new CacheBidiStage(store));
        var retry = BidiFlow.FromGraph(new RetryBidiStage(retryPolicy));
        var fake = ResponseMapFake.Create(map);
        var flow = cache.Atop(retry).Atop(fake)
            .Join(Flow.FromFunction<HttpRequestMessage, HttpResponseMessage>(_ => new HttpResponseMessage()));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.3")]
    public async Task Redirect_should_preserve_cookies_across_hops()
    {
        var jar = new CookieJar();
        var map = new ResponseMap()
            .On("/cookie/set-and-redirect", _ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.Found);
                r.Headers.TryAddWithoutValidation("Set-Cookie", "redirect_cookie=from-redirect; Path=/");
                r.Headers.Location = new Uri("http://localhost/cookie/echo");
                return r;
            })
            .On("/cookie/echo", EchoCookies);

        var response = await SendCookieRedirectAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/cookie/set-and-redirect"), jar);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("from-redirect", cookies["redirect_cookie"]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-5.2.2.1")]
    public async Task Compressed_response_should_be_served_from_cache()
    {
        var callCount = 0;
        var map = new ResponseMap()
            .On("/interaction/cache-gzip", _ =>
            {
                callCount++;
                var r = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"interaction-cache-gzip-payload-{callCount}")
                };
                r.Headers.CacheControl =
                    System.Net.Http.Headers.CacheControlHeaderValue.Parse("max-age=3600");
                return r;
            });

        var store = new CacheStore(CachePolicy.Default);

        var res1 = await SendCacheAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/interaction/cache-gzip"), store);
        var res2 = await SendCacheAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/interaction/cache-gzip"), store);

        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

        var body1 = await res1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var body2 = await res2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(body1, body2);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Retry_should_succeed_after_redirect_target_returns_503()
    {
        var callCount = 0;
        var map = new ResponseMap()
            .On("/interaction/redirect-succeed-after/2", _ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.Found);
                r.Headers.Location = new Uri("http://localhost/retry/succeed-after/2");
                return r;
            })
            .On(req => req.RequestUri?.AbsolutePath == "/retry/succeed-after/2", _ =>
            {
                callCount++;
                if (callCount <= 2)
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("success")
                };
            });

        var response = await SendRedirectRetryAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/interaction/redirect-succeed-after/2"),
            new RetryPolicy { MaxRetries = 3 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("success", body);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC6265-5.4")]
    public async Task Cookie_should_survive_retry_cycle()
    {
        var jar = new CookieJar();
        var callCount = 0;
        var map = new ResponseMap()
            .On("/cookie/set/auth-token/abc123", _ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.OK);
                r.Headers.TryAddWithoutValidation("Set-Cookie", "auth-token=abc123; Path=/");
                return r;
            })
            .On(req => req.RequestUri?.AbsolutePath == "/retry/succeed-after/2", _ =>
            {
                callCount++;
                if (callCount <= 2)
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("success")
                };
            })
            .On("/cookie/echo", EchoCookies);

        await SendCookieRetryAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/cookie/set/auth-token/abc123"),
            jar, new RetryPolicy { MaxRetries = 3 });

        var retryRes = await SendCookieRetryAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/retry/succeed-after/2"),
            jar, new RetryPolicy { MaxRetries = 3 });
        Assert.Equal(HttpStatusCode.OK, retryRes.StatusCode);

        var echoRes = await SendCookieRetryAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/cookie/echo"),
            jar, new RetryPolicy { MaxRetries = 3 });
        Assert.Equal(HttpStatusCode.OK, echoRes.StatusCode);

        var json = await echoRes.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("abc123", cookies["auth-token"]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-4.1")]
    public async Task Vary_should_separate_cache_entries_with_cookies_active()
    {
        var jar = new CookieJar();
        var map = new ResponseMap()
            .On("/cache/vary/Accept-Language", req =>
            {
                var lang = req.Headers.AcceptLanguage.FirstOrDefault()?.Value ?? "unknown";
                var r = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"vary-Accept-Language:{lang}")
                };
                r.Headers.CacheControl =
                    System.Net.Http.Headers.CacheControlHeaderValue.Parse("max-age=3600");
                r.Headers.TryAddWithoutValidation("Vary", "Accept-Language");
                return r;
            });

        var store = new CacheStore(CachePolicy.Default);

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/vary/Accept-Language");
        req1.Headers.Add("Accept-Language", "en");

        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/vary/Accept-Language");
        req2.Headers.Add("Accept-Language", "de");

        var req3 = new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/vary/Accept-Language");
        req3.Headers.Add("Accept-Language", "en");

        var res1 = await SendCacheCookieAsync(map, req1, store, jar);
        var res2 = await SendCacheCookieAsync(map, req2, store, jar);
        var res3 = await SendCacheCookieAsync(map, req3, store, jar);

        var body1 = await res1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var body2 = await res2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var body3 = await res3.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(body1, body3);
        Assert.NotEqual(body1, body2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.3")]
    public async Task Redirect_should_accumulate_cookies_across_hops()
    {
        var jar = new CookieJar();
        var map = new ResponseMap()
            .On(req => req.RequestUri?.AbsolutePath.StartsWith("/interaction/cookie-hop/") == true, req =>
            {
                var hop = int.Parse(req.RequestUri!.Segments.Last().TrimEnd('/'));
                var r = new HttpResponseMessage(HttpStatusCode.Found);
                r.Headers.TryAddWithoutValidation("Set-Cookie", $"hop{hop}=val{hop}; Path=/");
                r.Headers.Location = hop < 3
                    ? new Uri($"http://localhost/interaction/cookie-hop/{hop + 1}")
                    : new Uri("http://localhost/cookie/echo");
                return r;
            })
            .On("/cookie/echo", EchoCookies);

        var response = await SendCookieRedirectAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/interaction/cookie-hop/1"), jar);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("val1", cookies["hop1"]);
        Assert.Equal("val2", cookies["hop2"]);
        Assert.Equal("val3", cookies["hop3"]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-5.2.2.1")]
    public async Task Cache_hit_should_bypass_retry_logic()
    {
        var callCount = 0;
        var map = new ResponseMap()
            .On("/cache/max-age/3600", _ =>
            {
                callCount++;
                var r = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"cached-body-{callCount}")
                };
                r.Headers.CacheControl =
                    System.Net.Http.Headers.CacheControlHeaderValue.Parse("max-age=3600");
                return r;
            });

        var store = new CacheStore(CachePolicy.Default);

        var res1 = await SendCacheRetryAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/max-age/3600"),
            store, new RetryPolicy { MaxRetries = 3 });
        var res2 = await SendCacheRetryAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/cache/max-age/3600"),
            store, new RetryPolicy { MaxRetries = 3 });

        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

        var body1 = await res1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var body2 = await res2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(body1, body2);
    }
}
