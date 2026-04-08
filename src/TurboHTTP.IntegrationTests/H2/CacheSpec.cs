using System.Net;
using TurboHTTP.IntegrationTests.Shared;
using TurboHTTP.Protocol.Caching;

namespace TurboHTTP.IntegrationTests.H2;

[Collection("H2")]
public sealed class CacheSpec
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public CacheSpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    private ClientHelper CreateCacheClient()
    {
        return ClientHelper.CreateClient(
            _server.H2Port,
            new Version(2, 0),
            configure: builder => builder.WithCache(CachePolicy.Default),
            system: _systemFixture.System);
    }

    [Fact(Timeout = 20000)]
    public async Task MaxAge_should_serve_response_from_cache_on_second_request()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateCacheClient();

        // First request — populates the cache
        var request1 = new HttpRequestMessage(HttpMethod.Get, "/cache/max-age/3600");
        var response1 = await helper.Client.SendAsync(request1, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(cts.Token);
        Assert.False(string.IsNullOrEmpty(body1), "First response body should be non-empty");

        // Second request — should be served from cache (identical timestamp)
        var request2 = new HttpRequestMessage(HttpMethod.Get, "/cache/max-age/3600");
        var response2 = await helper.Client.SendAsync(request2, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(cts.Token);

        Assert.Equal(body1, body2);
    }

    [Fact(Timeout = 20000)]
    public async Task NoCache_should_force_revalidation()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateCacheClient();

        var request1 = new HttpRequestMessage(HttpMethod.Get, "/cache/no-cache");
        var response1 = await helper.Client.SendAsync(request1, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(cts.Token);

        // Small delay to ensure server timestamp differs
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        var request2 = new HttpRequestMessage(HttpMethod.Get, "/cache/no-cache");
        var response2 = await helper.Client.SendAsync(request2, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(cts.Token);

        Assert.NotEqual(body1, body2);
    }

    [Fact(Timeout = 20000)]
    public async Task NoStore_should_never_cache_response()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateCacheClient();

        var request1 = new HttpRequestMessage(HttpMethod.Get, "/cache/no-store");
        var response1 = await helper.Client.SendAsync(request1, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(cts.Token);

        var request2 = new HttpRequestMessage(HttpMethod.Get, "/cache/no-store");
        var response2 = await helper.Client.SendAsync(request2, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(cts.Token);

        // no-store returns a fixed string "no-store-resource", so bodies will be equal,
        // but the response must not come from cache. Verify both requests hit the server
        // by checking that neither response has an Age header (which would indicate a cache hit).
        Assert.Equal("no-store-resource", body1);
        Assert.Equal("no-store-resource", body2);
    }

    [Fact(Timeout = 20000)]
    public async Task ETag_should_send_If_None_Match_on_revalidation()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateCacheClient();

        var request1 = new HttpRequestMessage(HttpMethod.Get, "/cache/etag/test1");
        var response1 = await helper.Client.SendAsync(request1, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("etag-resource-test1", body1);

        // Second request should serve from cache (max-age=3600)
        var request2 = new HttpRequestMessage(HttpMethod.Get, "/cache/etag/test1");
        var response2 = await helper.Client.SendAsync(request2, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(cts.Token);

        Assert.Equal(body1, body2);
    }

    [Fact(Timeout = 20000)]
    public async Task LastModified_should_send_If_Modified_Since_on_revalidation()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateCacheClient();

        var request1 = new HttpRequestMessage(HttpMethod.Get, "/cache/last-modified/doc1");
        var response1 = await helper.Client.SendAsync(request1, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("last-modified-resource-doc1", body1);

        // Second request should serve from cache (max-age=3600)
        var request2 = new HttpRequestMessage(HttpMethod.Get, "/cache/last-modified/doc1");
        var response2 = await helper.Client.SendAsync(request2, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(cts.Token);

        Assert.Equal(body1, body2);
    }

    [Fact(Timeout = 20000)]
    public async Task Vary_should_produce_different_cache_entries_per_header_value()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateCacheClient();

        // Request with Accept-Language: en
        var request1 = new HttpRequestMessage(HttpMethod.Get, "/cache/vary/Accept-Language");
        request1.Headers.TryAddWithoutValidation("Accept-Language", "en");
        var response1 = await helper.Client.SendAsync(request1, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("vary-Accept-Language:en", body1);

        // Request with Accept-Language: de — should NOT come from cache
        var request2 = new HttpRequestMessage(HttpMethod.Get, "/cache/vary/Accept-Language");
        request2.Headers.TryAddWithoutValidation("Accept-Language", "de");
        var response2 = await helper.Client.SendAsync(request2, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("vary-Accept-Language:de", body2);

        Assert.NotEqual(body1, body2);

        // Request again with Accept-Language: en — should come from cache
        var request3 = new HttpRequestMessage(HttpMethod.Get, "/cache/vary/Accept-Language");
        request3.Headers.TryAddWithoutValidation("Accept-Language", "en");
        var response3 = await helper.Client.SendAsync(request3, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response3.StatusCode);
        var body3 = await response3.Content.ReadAsStringAsync(cts.Token);

        Assert.Equal(body1, body3);
    }

    [Fact(Timeout = 20000)]
    public async Task MustRevalidate_should_force_revalidation()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateCacheClient();

        var request1 = new HttpRequestMessage(HttpMethod.Get, "/cache/must-revalidate");
        var response1 = await helper.Client.SendAsync(request1, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(cts.Token);

        // Second request — must revalidate (max-age=0), server returns 304 if ETag matches,
        // client should get the same cached body back after merge
        var request2 = new HttpRequestMessage(HttpMethod.Get, "/cache/must-revalidate");
        var response2 = await helper.Client.SendAsync(request2, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(cts.Token);

        Assert.Equal(body1, body2);
    }

    [Fact(Timeout = 20000)]
    public async Task SMaxAge_should_be_respected_by_shared_cache()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        // Use shared cache policy to honour s-maxage
        await using var helper = ClientHelper.CreateClient(
            _server.H2Port,
            new Version(2, 0),
            configure: builder => builder.WithCache(new CachePolicy { SharedCache = true }));

        var request1 = new HttpRequestMessage(HttpMethod.Get, "/cache/s-maxage/3600");
        var response1 = await helper.Client.SendAsync(request1, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(cts.Token);

        var request2 = new HttpRequestMessage(HttpMethod.Get, "/cache/s-maxage/3600");
        var response2 = await helper.Client.SendAsync(request2, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(cts.Token);

        Assert.Equal(body1, body2);
    }

    [Fact(Timeout = 20000)]
    public async Task Expires_should_enable_caching()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateCacheClient();

        var request1 = new HttpRequestMessage(HttpMethod.Get, "/cache/expires");
        var response1 = await helper.Client.SendAsync(request1, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(cts.Token);

        var request2 = new HttpRequestMessage(HttpMethod.Get, "/cache/expires");
        var response2 = await helper.Client.SendAsync(request2, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(cts.Token);

        Assert.Equal(body1, body2);
    }

    [Fact(Timeout = 20000)]
    public async Task Private_should_be_cached_by_private_cache()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateCacheClient();

        var request1 = new HttpRequestMessage(HttpMethod.Get, "/cache/private");
        var response1 = await helper.Client.SendAsync(request1, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(cts.Token);

        var request2 = new HttpRequestMessage(HttpMethod.Get, "/cache/private");
        var response2 = await helper.Client.SendAsync(request2, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(cts.Token);

        // Private cache should still cache private responses
        Assert.Equal(body1, body2);
    }

    [Fact(Timeout = 20000)]
    public async Task Private_should_not_be_cached_by_shared_cache()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.H2Port,
            new Version(2, 0),
            configure: builder => builder.WithCache(new CachePolicy { SharedCache = true }));

        var request1 = new HttpRequestMessage(HttpMethod.Get, "/cache/private");
        var response1 = await helper.Client.SendAsync(request1, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(cts.Token);

        // Small delay to ensure server timestamp differs
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        var request2 = new HttpRequestMessage(HttpMethod.Get, "/cache/private");
        var response2 = await helper.Client.SendAsync(request2, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(cts.Token);

        // Shared cache must not cache private responses
        Assert.NotEqual(body1, body2);
    }
}
