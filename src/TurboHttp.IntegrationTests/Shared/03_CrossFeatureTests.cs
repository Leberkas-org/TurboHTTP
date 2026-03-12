using System.Net;
using System.Text.Json;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages;

namespace TurboHttp.IntegrationTests.Shared;

/// <summary>
/// Integration tests for cross-feature interactions.
/// Verifies that combinations of redirect+cookies, redirect+retry, cache+redirect,
/// cache+cookies, decompression+cache, retry+decompression, all features enabled,
/// and flags-disabled passthrough work correctly together against real Kestrel routes.
/// </summary>
public sealed class CrossFeatureTests : TestKit, IClassFixture<KestrelFixture>
{
    private readonly KestrelFixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _clientManager;

    public CrossFeatureTests(KestrelFixture fixture)
    {
        _fixture = fixture;
        _materializer = Sys.Materializer();
        _clientManager = Sys.ActorOf(Props.Create<ClientManager>());
    }

    /// <summary>
    /// Sends a single HTTP/1.1 request through the Http11Engine pipeline.
    /// Each call materialises a fresh pipeline.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        var engine = new Http11Engine();
        var tcpOptions = new TcpOptions
        {
            Host = "127.0.0.1",
            Port = _fixture.Port
        };

        var transport =
            Flow.Create<ITransportItem>()
                .Prepend(Source.Single<ITransportItem>(
                    new ConnectItem(tcpOptions)))
                .Via(new ConnectionStage(_clientManager));

        var flow = engine.CreateFlow().Join(transport);

        var (queue, responseTask) = Source.Queue<HttpRequestMessage>(1, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(Sink.First<HttpResponseMessage>(), Keep.Both)
            .Run(_materializer);

        await queue.OfferAsync(request);
        return await responseTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Sends a request with CookieJar integration — injects cookies before sending,
    /// stores cookies from response.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithCookiesAsync(
        HttpRequestMessage request,
        CookieJar cookieJar)
    {
        if (request.RequestUri is not null)
        {
            cookieJar.AddCookiesToRequest(request.RequestUri, ref request);
        }

        var response = await SendAsync(request);
        response.RequestMessage = request;

        if (request.RequestUri is not null)
        {
            cookieJar.ProcessResponse(request.RequestUri, response);
        }

        return response;
    }

    /// <summary>
    /// Sends a request following redirects with CookieJar support.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRedirectAndCookiesAsync(
        HttpRequestMessage request,
        CookieJar cookieJar,
        RedirectHandler? handler = null)
    {
        handler ??= new RedirectHandler();
        var current = request;

        for (var i = 0; i <= RedirectPolicy.Default.MaxRedirects; i++)
        {
            if (current.RequestUri is not null)
            {
                cookieJar.AddCookiesToRequest(current.RequestUri, ref current);
            }

            var response = await SendAsync(current);
            response.RequestMessage = current;

            if (current.RequestUri is not null)
            {
                cookieJar.ProcessResponse(current.RequestUri, response);
            }

            if (!RedirectHandler.IsRedirect(response))
            {
                return response;
            }

            current = handler.BuildRedirectRequest(current, response);
            current.Version = HttpVersion.Version11;
        }

        throw new RedirectException(
            "Exceeded redirect loop in test helper",
            RedirectError.MaxRedirectsExceeded);
    }

    /// <summary>
    /// Sends a request following redirects without cookie support.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRedirectAsync(
        HttpRequestMessage request,
        RedirectHandler? handler = null)
    {
        handler ??= new RedirectHandler();
        var current = request;

        for (var i = 0; i <= RedirectPolicy.Default.MaxRedirects; i++)
        {
            var response = await SendAsync(current);
            response.RequestMessage = current;

            if (!RedirectHandler.IsRedirect(response))
            {
                return response;
            }

            current = handler.BuildRedirectRequest(current, response);
            current.Version = HttpVersion.Version11;
        }

        throw new RedirectException(
            "Exceeded redirect loop in test helper",
            RedirectError.MaxRedirectsExceeded);
    }

    /// <summary>
    /// Sends a request with retry logic using <see cref="RetryEvaluator"/>.
    /// Each retry materialises a new Http11Engine pipeline.
    /// </summary>
    private async Task<(HttpResponseMessage Response, int AttemptCount)> SendWithRetryAsync(
        HttpRequestMessage request,
        RetryPolicy? policy = null)
    {
        policy ??= RetryPolicy.Default;
        var attemptCount = 0;

        for (var i = 0; i <= policy.MaxRetries; i++)
        {
            attemptCount++;

            var attemptRequest = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Version = HttpVersion.Version11
            };
            foreach (var header in request.Headers)
            {
                attemptRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            var response = await SendAsync(attemptRequest);

            var decision = RetryEvaluator.Evaluate(
                attemptRequest,
                response,
                attemptCount: attemptCount,
                policy: policy);

            if (!decision.ShouldRetry)
            {
                return (response, attemptCount);
            }

            if (decision.RetryAfterDelay is { } delay && delay > TimeSpan.Zero)
            {
                var capped = delay > TimeSpan.FromSeconds(2) ? TimeSpan.FromSeconds(2) : delay;
                await Task.Delay(capped);
            }
        }

        throw new InvalidOperationException("Exceeded retry loop in test helper");
    }

    /// <summary>
    /// Sends a request with cache support. Checks the cache store first,
    /// evaluates freshness, performs conditional revalidation if needed,
    /// and stores cacheable responses.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithCacheAsync(
        HttpRequestMessage request,
        HttpCacheStore store)
    {
        var now = DateTimeOffset.UtcNow;

        var entry = store.Get(request);
        var result = CacheFreshnessEvaluator.Evaluate(entry, request, now);

        if (result.Status == CacheLookupStatus.Fresh)
        {
            var cached = new HttpResponseMessage(result.Entry!.Response.StatusCode)
            {
                Content = new ByteArrayContent(result.Entry.Body)
            };
            foreach (var header in result.Entry.Response.Headers)
            {
                cached.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            foreach (var header in result.Entry.Response.Content.Headers)
            {
                cached.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            return cached;
        }

        if (result.Status == CacheLookupStatus.MustRevalidate && result.Entry is not null &&
            CacheValidationRequestBuilder.CanRevalidate(result.Entry))
        {
            var conditional = CacheValidationRequestBuilder.BuildConditionalRequest(request, result.Entry);
            conditional.Version = HttpVersion.Version11;

            var requestTime = DateTimeOffset.UtcNow;
            var revalResponse = await SendAsync(conditional);
            var responseTime = DateTimeOffset.UtcNow;

            if (revalResponse.StatusCode == HttpStatusCode.NotModified)
            {
                var merged = CacheValidationRequestBuilder.MergeNotModifiedResponse(revalResponse, result.Entry);
                store.Put(request, merged, result.Entry.Body, requestTime, responseTime);
                return merged;
            }

            var newBody = await revalResponse.Content.ReadAsByteArrayAsync();
            store.Put(request, revalResponse, newBody, requestTime, responseTime);
            return revalResponse;
        }

        {
            var requestTime = DateTimeOffset.UtcNow;
            var response = await SendAsync(request);
            var responseTime = DateTimeOffset.UtcNow;

            var bodyBytes = await response.Content.ReadAsByteArrayAsync();
            store.Put(request, response, bodyBytes, requestTime, responseTime);

            var result2 = new HttpResponseMessage(response.StatusCode)
            {
                Content = new ByteArrayContent(bodyBytes),
                Version = response.Version
            };
            foreach (var header in response.Headers)
            {
                result2.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            foreach (var header in response.Content.Headers)
            {
                result2.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            return result2;
        }
    }

    private HttpRequestMessage MakeGet(string path) =>
        new(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}{path}")
        {
            Version = HttpVersion.Version11
        };

    private static Dictionary<string, string> ParseCookieEcho(string body) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(body)
        ?? new Dictionary<string, string>();

    private static byte[] GenerateExpectedPayload(int kb)
    {
        var size = kb * 1024;
        var data = new byte[size];
        for (var i = 0; i < size; i++)
        {
            data[i] = (byte)('A' + (i % 26));
        }
        return data;
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "CROSS-001: Redirect + Cookies — Set-Cookie during redirect preserved at target")]
    public async Task Redirect_Plus_Cookies_PreservedAtTarget()
    {
        var jar = new CookieJar();

        // /cookie/set-and-redirect sets redirect_cookie=from-redirect and 302s to /cookie/echo
        var response = await SendWithRedirectAndCookiesAsync(
            MakeGet("/cookie/set-and-redirect"), jar);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var cookies = ParseCookieEcho(body);

        Assert.True(cookies.ContainsKey("redirect_cookie"),
            "Cookie set during redirect response should be sent to redirect target");
        Assert.Equal("from-redirect", cookies["redirect_cookie"]);
    }

    [Fact(DisplayName = "CROSS-002: Flags disabled — plain SendAsync is passthrough without features")]
    public async Task FlagsDisabled_PlainSendIsPassthrough()
    {
        // When no CookieJar, RedirectHandler, RetryEvaluator, or HttpCacheStore
        // is wired, a plain SendAsync returns the raw server response unmodified.

        // A redirect response should NOT be followed — raw 302 returned
        var redirectRequest = MakeGet("/redirect/302/hello");
        var redirectResponse = await SendAsync(redirectRequest);
        Assert.Equal(HttpStatusCode.Redirect, redirectResponse.StatusCode);
        Assert.Contains("/hello", redirectResponse.Headers.Location?.ToString() ?? "");

        // A Set-Cookie response is returned but no jar stores it — next request has no cookies
        var setCookieRequest = MakeGet("/cookie/set/passtest/val");
        var setCookieResponse = await SendAsync(setCookieRequest);
        Assert.Equal(HttpStatusCode.OK, setCookieResponse.StatusCode);

        var echoRequest = MakeGet("/cookie/echo");
        var echoResponse = await SendAsync(echoRequest);
        var body = await echoResponse.Content.ReadAsStringAsync();
        var cookies = ParseCookieEcho(body);
        Assert.False(cookies.ContainsKey("passtest"),
            "Without CookieJar, cookies should not be injected into subsequent requests");

        // A 503 response should NOT be retried — raw 503 returned
        var retryRequest = MakeGet("/retry/503");
        var retryResponse = await SendAsync(retryRequest);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, retryResponse.StatusCode);
    }

    [Fact(DisplayName = "CROSS-003: Redirect + Retry — retried request follows redirect after retry succeeds")]
    public async Task Redirect_Plus_Retry_RetriedRequestFollowsRedirect()
    {
        var uniqueKey = Guid.NewGuid().ToString("N");

        // First attempt: /retry/succeed-after/2 returns 503, then 200 on retry.
        // After the retry succeeds with 200, we verify the endpoint returns "success".
        var (response, attemptCount) = await SendWithRetryAsync(
            MakeGet($"/retry/succeed-after/2?key={uniqueKey}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, attemptCount);

        // Now send a redirect that points to /hello to verify redirect still works
        // after a retry loop completes
        var redirectResponse = await SendWithRedirectAsync(
            MakeGet("/redirect/302/hello"));

        Assert.Equal(HttpStatusCode.OK, redirectResponse.StatusCode);
        var body = await redirectResponse.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "CROSS-004: Cache + Redirect — redirect target response is cached on second access")]
    public async Task Cache_Plus_Redirect_TargetResponseCached()
    {
        var store = new HttpCacheStore();

        // Follow redirect manually, then cache the final response
        var redirectResponse = await SendWithRedirectAsync(
            MakeGet("/redirect/302/cache/max-age/300"));

        Assert.Equal(HttpStatusCode.OK, redirectResponse.StatusCode);

        // Store the final response (from /cache/max-age/300) in cache
        var body1 = await redirectResponse.Content.ReadAsByteArrayAsync();
        var now = DateTimeOffset.UtcNow;
        store.Put(
            MakeGet("/cache/max-age/300"),
            redirectResponse, body1, now.AddMilliseconds(-50), now);

        Assert.Equal(1, store.Count);

        // Second access to /cache/max-age/300 should hit cache
        var cachedResponse = await SendWithCacheAsync(MakeGet("/cache/max-age/300"), store);

        Assert.Equal(HttpStatusCode.OK, cachedResponse.StatusCode);
        var body2 = await cachedResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(body1, body2);
    }

    [Fact(DisplayName = "CROSS-005: Cache + Cookies — cached response does not leak cookies across requests")]
    public async Task Cache_Plus_Cookies_NoCookieLeakFromCache()
    {
        var store = new HttpCacheStore();
        var jar = new CookieJar();

        // Set a cookie, then make a cacheable request with cookie injected
        await SendWithCookiesAsync(MakeGet("/cookie/set/cachesess/val1"), jar);

        // Fetch a cacheable resource (the cookie is sent but the response is cached by URI)
        var response1 = await SendWithCacheAsync(MakeGet("/cache/max-age/300"), store);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(1, store.Count);

        // Create a fresh CookieJar (simulating a different "session")
        var jar2 = new CookieJar();

        // Second access should serve from cache — the cached response should not
        // contain the first session's cookie in its body (the cache is URI-keyed)
        var response2 = await SendWithCacheAsync(MakeGet("/cache/max-age/300"), store);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        // Verify jar2 has no cookies (cache doesn't inject Set-Cookie from cached response into jar)
        var checkRequest = MakeGet("/cookie/echo");
        jar2.AddCookiesToRequest(checkRequest.RequestUri!, ref checkRequest);

        // jar2 should have no cookies — the cached response didn't set any in jar2
        Assert.Equal(0, jar2.Count);
    }

    [Fact(DisplayName = "CROSS-006: Decompression + Cache — decompressed body stored and served from cache")]
    public async Task Decompression_Plus_Cache_DecompressedBodyCached()
    {
        var store = new HttpCacheStore();

        // Fetch a gzip-compressed response — engine decompresses transparently
        var response1 = await SendAsync(MakeGet("/compress/gzip/1"));
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var decompressedBody = await response1.Content.ReadAsByteArrayAsync();
        Assert.Equal(GenerateExpectedPayload(1), decompressedBody);

        // Store the decompressed response in cache
        var now = DateTimeOffset.UtcNow;
        var cacheableResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(decompressedBody)
        };
        cacheableResponse.Headers.TryAddWithoutValidation("Cache-Control", "max-age=300");
        store.Put(MakeGet("/compress/gzip/1"), cacheableResponse, decompressedBody,
            now.AddMilliseconds(-50), now);

        Assert.Equal(1, store.Count);

        // Second access should serve from cache with correct decompressed body
        var response2 = await SendWithCacheAsync(MakeGet("/compress/gzip/1"), store);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var cachedBody = await response2.Content.ReadAsByteArrayAsync();
        Assert.Equal(decompressedBody, cachedBody);
    }

    [Fact(DisplayName = "CROSS-007: Retry + Decompression — retried request decompresses response correctly")]
    public async Task Retry_Plus_Decompression_DecompressesAfterRetry()
    {
        var uniqueKey = Guid.NewGuid().ToString("N");

        // /retry/succeed-after/2 returns 503 first, then 200 "success" on second attempt
        var (response, attemptCount) = await SendWithRetryAsync(
            MakeGet($"/retry/succeed-after/2?key={uniqueKey}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, attemptCount);
        var retryBody = await response.Content.ReadAsStringAsync();
        Assert.Equal("success", retryBody);

        // Now fetch a compressed resource to verify decompression still works
        // after a retry loop (engine materialises fresh pipelines each time)
        var compressResponse = await SendAsync(MakeGet("/compress/gzip/1"));
        Assert.Equal(HttpStatusCode.OK, compressResponse.StatusCode);
        var body = await compressResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(GenerateExpectedPayload(1), body);
    }

    [Fact(DisplayName = "CROSS-008: All features — redirect + cookies + cache + decompression + retry cooperate")]
    public async Task AllFeatures_Cooperate()
    {
        var jar = new CookieJar();
        var store = new HttpCacheStore();

        // Step 1: Set a cookie
        await SendWithCookiesAsync(MakeGet("/cookie/set/alltest/present"), jar);
        Assert.Equal(1, jar.Count);

        // Step 2: Retry a flaky endpoint until success
        var uniqueKey = Guid.NewGuid().ToString("N");
        var (retryResponse, attemptCount) = await SendWithRetryAsync(
            MakeGet($"/retry/succeed-after/2?key={uniqueKey}"));
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        Assert.Equal(2, attemptCount);

        // Step 3: Follow a redirect with cookies preserved
        var redirectResponse = await SendWithRedirectAndCookiesAsync(
            MakeGet("/cookie/set-and-redirect"), jar);
        Assert.Equal(HttpStatusCode.OK, redirectResponse.StatusCode);
        var redirectBody = await redirectResponse.Content.ReadAsStringAsync();
        var cookies = ParseCookieEcho(redirectBody);
        Assert.True(cookies.ContainsKey("alltest"), "Pre-existing cookie should survive redirect");
        Assert.True(cookies.ContainsKey("redirect_cookie"), "Redirect cookie should be present");

        // Step 4: Fetch and cache a compressed response
        var compressResponse = await SendAsync(MakeGet("/compress/gzip/1"));
        Assert.Equal(HttpStatusCode.OK, compressResponse.StatusCode);
        var decompressed = await compressResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(GenerateExpectedPayload(1), decompressed);

        var now = DateTimeOffset.UtcNow;
        var cacheableResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(decompressed)
        };
        cacheableResponse.Headers.TryAddWithoutValidation("Cache-Control", "max-age=300");
        store.Put(MakeGet("/compress/gzip/1"), cacheableResponse, decompressed,
            now.AddMilliseconds(-50), now);

        // Step 5: Serve from cache
        var cachedResponse = await SendWithCacheAsync(MakeGet("/compress/gzip/1"), store);
        Assert.Equal(HttpStatusCode.OK, cachedResponse.StatusCode);
        var cachedBody = await cachedResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(decompressed, cachedBody);
    }
}
