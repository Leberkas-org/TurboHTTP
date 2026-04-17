using System.Net;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Shared;
using TurboHTTP.Protocol.Caching;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.IntegrationTests.H11;

[Collection("H11")]
[Obsolete("Replaced by StreamTests.Acceptance.H11.FeatureInteractionSpec")]
public sealed class FeatureInteractionSpec
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public FeatureInteractionSpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    [Fact(Timeout = 20000)]
    public async Task FeatureInteraction_should_preserve_cookies_across_redirect_hops()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            configure: builder => builder.WithCookies().WithRedirect(),
            system: _systemFixture.System);

        // /cookie/set-and-redirect sets redirect_cookie=from-redirect and 302 → /cookie/echo
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookie/set-and-redirect"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(cts.Token);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("from-redirect", cookies["redirect_cookie"]);
    }

    [Fact(Timeout = 20000)]
    public async Task FeatureInteraction_should_serve_compressed_response_from_cache()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            configure: builder => builder.WithCache(CachePolicy.Default).WithDecompression(),
            system: _systemFixture.System);

        var req1 = new HttpRequestMessage(HttpMethod.Get, "/interaction/cache-gzip");
        var req2 = new HttpRequestMessage(HttpMethod.Get, "/interaction/cache-gzip");

        var res1 = await helper.Client.SendAsync(req1, cts.Token);
        var res2 = await helper.Client.SendAsync(req2, cts.Token);

        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

        var body1 = await res1.Content.ReadAsStringAsync(cts.Token);
        var body2 = await res2.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal(body1, body2);
    }

    [Fact(Timeout = 20000)]
    public async Task FeatureInteraction_should_retry_after_redirect_target_returns_503()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            configure: builder => builder.WithRedirect().WithRetry(new RetryPolicy { MaxRetries = 3 }),
            system: _systemFixture.System);

        var key = Guid.NewGuid().ToString("N");
        // /interaction/redirect-succeed-after/2/{key} → 302 → /retry/succeed-after/2?key={key}
        // First hit: 503, second hit: 200 "success"
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/interaction/redirect-succeed-after/2/{key}"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("success", body);
    }

    [Fact(Timeout = 20000)]
    public async Task FeatureInteraction_should_preserve_cookies_across_retry_cycle()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            configure: builder => builder.WithCookies().WithRetry(new RetryPolicy { MaxRetries = 3 }),
            system: _systemFixture.System);

        // Step 1: set a cookie via a successful request
        var setReq = new HttpRequestMessage(HttpMethod.Get, "/cookie/set/auth-token/abc123");
        await helper.Client.SendAsync(setReq, cts.Token);

        // Step 2: trigger retry cycle (fails once, then succeeds)
        var key = Guid.NewGuid().ToString("N");
        var retryReq = new HttpRequestMessage(HttpMethod.Get, $"/retry/succeed-after/2?key={key}");
        var retryRes = await helper.Client.SendAsync(retryReq, cts.Token);
        Assert.Equal(HttpStatusCode.OK, retryRes.StatusCode);

        // Step 3: verify cookie is still present after retry cycle
        var echoReq = new HttpRequestMessage(HttpMethod.Get, "/cookie/echo");
        var echoRes = await helper.Client.SendAsync(echoReq, cts.Token);
        Assert.Equal(HttpStatusCode.OK, echoRes.StatusCode);

        var json = await echoRes.Content.ReadAsStringAsync(cts.Token);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("abc123", cookies["auth-token"]);
    }

    [Fact(Timeout = 20000)]
    public async Task FeatureInteraction_should_separate_cache_entries_with_vary_header_when_cookies_active()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            configure: builder => builder.WithCache(CachePolicy.Default).WithCookies(),
            system: _systemFixture.System);

        // /cache/vary/Accept-Language returns Vary: Accept-Language + body = vary-Accept-Language:{value}
        var req1 = new HttpRequestMessage(HttpMethod.Get, "/cache/vary/Accept-Language");
        req1.Headers.Add("Accept-Language", "en");

        var req2 = new HttpRequestMessage(HttpMethod.Get, "/cache/vary/Accept-Language");
        req2.Headers.Add("Accept-Language", "de");

        var req3 = new HttpRequestMessage(HttpMethod.Get, "/cache/vary/Accept-Language");
        req3.Headers.Add("Accept-Language", "en");

        var res1 = await helper.Client.SendAsync(req1, cts.Token);
        var res2 = await helper.Client.SendAsync(req2, cts.Token);
        var res3 = await helper.Client.SendAsync(req3, cts.Token);

        var body1 = await res1.Content.ReadAsStringAsync(cts.Token);
        var body2 = await res2.Content.ReadAsStringAsync(cts.Token);
        var body3 = await res3.Content.ReadAsStringAsync(cts.Token);

        // Third request with "en" should hit cache → same body as first
        Assert.Equal(body1, body3);
        // Different Accept-Language → different cache entry → different body
        Assert.NotEqual(body1, body2);
    }

    [Fact(Timeout = 20000)]
    public async Task FeatureInteraction_should_accumulate_cookies_across_redirect_chain()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            configure: builder => builder.WithCookies().WithRedirect(),
            system: _systemFixture.System);

        // /interaction/cookie-hop/1 → sets hop1=val1 → 302 → hop/2 → sets hop2=val2 → 302 → hop/3 → sets hop3=val3 → 302 → /cookie/echo
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/interaction/cookie-hop/1"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(cts.Token);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("val1", cookies["hop1"]);
        Assert.Equal("val2", cookies["hop2"]);
        Assert.Equal("val3", cookies["hop3"]);
    }

    [Fact(Timeout = 20000)]
    public async Task FeatureInteraction_should_bypass_retry_logic_on_cache_hit()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            configure: builder => builder.WithCache(CachePolicy.Default).WithRetry(new RetryPolicy { MaxRetries = 3 }),
            system: _systemFixture.System);

        // /cache/max-age/3600 returns a timestamp body + Cache-Control: max-age=3600
        var res1 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache/max-age/3600"), cts.Token);
        var res2 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache/max-age/3600"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

        var body1 = await res1.Content.ReadAsStringAsync(cts.Token);
        var body2 = await res2.Content.ReadAsStringAsync(cts.Token);
        // Second response should be served from cache → identical body (same timestamp)
        Assert.Equal(body1, body2);
    }
}
