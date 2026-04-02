using System.Net;
using System.Text.Json;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.Protocol.Semantics;
using TurboHttp.Protocol.Caching;

namespace TurboHttp.IntegrationTests.H10;

[Collection("H10")]
public sealed class FeatureInteractionSpec
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public FeatureInteractionSpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    [Fact(Timeout = 30000)]
    public async Task FeatureInteraction_should_preserve_cookies_across_redirect_hops()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 0),
            configure: builder => builder.WithCookies().WithRedirect(),
            system: _systemFixture.System);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookie/set-and-redirect"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(cts.Token);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("from-redirect", cookies["redirect_cookie"]);
    }

    [Fact(Timeout = 30000)]
    public async Task FeatureInteraction_should_serve_compressed_response_from_cache()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 0),
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

    [Fact(Timeout = 30000)]
    public async Task FeatureInteraction_should_retry_after_redirect_when_target_returns_503()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 0),
            configure: builder => builder.WithRedirect().WithRetry(new RetryPolicy { MaxRetries = 3 }),
            system: _systemFixture.System);

        var key = Guid.NewGuid().ToString("N");
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/interaction/redirect-succeed-after/2/{key}"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("success", body);
    }

    [Fact(Timeout = 30000)]
    public async Task FeatureInteraction_should_preserve_cookie_across_retry_cycle()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 0),
            configure: builder => builder.WithCookies().WithRetry(new RetryPolicy { MaxRetries = 3 }),
            system: _systemFixture.System);

        var setReq = new HttpRequestMessage(HttpMethod.Get, "/cookie/set/auth-token/abc123");
        await helper.Client.SendAsync(setReq, cts.Token);

        var key = Guid.NewGuid().ToString("N");
        var retryReq = new HttpRequestMessage(HttpMethod.Get, $"/retry/succeed-after/2?key={key}");
        var retryRes = await helper.Client.SendAsync(retryReq, cts.Token);
        Assert.Equal(HttpStatusCode.OK, retryRes.StatusCode);

        var echoReq = new HttpRequestMessage(HttpMethod.Get, "/cookie/echo");
        var echoRes = await helper.Client.SendAsync(echoReq, cts.Token);
        Assert.Equal(HttpStatusCode.OK, echoRes.StatusCode);

        var json = await echoRes.Content.ReadAsStringAsync(cts.Token);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("abc123", cookies["auth-token"]);
    }

    [Fact(Timeout = 30000)]
    public async Task FeatureInteraction_should_separate_cache_entries_with_vary_header_when_cookies_active()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 0),
            configure: builder => builder.WithCache(CachePolicy.Default).WithCookies(),
            system: _systemFixture.System);

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

        Assert.Equal(body1, body3);
        Assert.NotEqual(body1, body2);
    }

    [Fact(Timeout = 30000)]
    public async Task FeatureInteraction_should_accumulate_cookies_across_redirect_chain()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 0),
            configure: builder => builder.WithCookies().WithRedirect(),
            system: _systemFixture.System);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/interaction/cookie-hop/1"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(cts.Token);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("val1", cookies["hop1"]);
        Assert.Equal("val2", cookies["hop2"]);
        Assert.Equal("val3", cookies["hop3"]);
    }

    [Fact(Timeout = 30000)]
    public async Task FeatureInteraction_should_bypass_retry_logic_on_cache_hit()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 0),
            configure: builder => builder.WithCache(CachePolicy.Default).WithRetry(new RetryPolicy { MaxRetries = 3 }),
            system: _systemFixture.System);

        var res1 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache/max-age/3600"), cts.Token);
        var res2 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache/max-age/3600"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

        var body1 = await res1.Content.ReadAsStringAsync(cts.Token);
        var body2 = await res2.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal(body1, body2);
    }
}
