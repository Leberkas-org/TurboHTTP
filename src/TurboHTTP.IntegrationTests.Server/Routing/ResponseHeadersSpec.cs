using System.Net;
using TurboHTTP.IntegrationTests.Server.Shared;

namespace TurboHTTP.IntegrationTests.Server.Routing;

public sealed class ResponseHeadersSpec(TurboServerFixture server) : IDisposable
{
    private readonly HttpClient _client = server.CreateClient();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _client.Dispose();

    [Fact(Timeout = 15000)]
    public async Task Custom_response_header_should_arrive_at_client()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/custom-header"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Request-Id", out var values));
        Assert.Equal("abc-123", values.First());
    }

    [Fact(Timeout = 15000)]
    public async Task Multiple_values_for_same_header_should_arrive()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/multi-header"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Tag", out var values));
        var tagList = values.ToList();
        Assert.Contains("alpha", tagList);
        Assert.Contains("beta", tagList);
    }

    [Fact(Timeout = 15000)]
    public async Task Standard_cache_headers_should_arrive()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/cache-headers"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Cache-Control", out var cacheValues));
        var cacheValue = cacheValues.First();
        Assert.Contains("no-cache", cacheValue);
        Assert.Contains("no-store", cacheValue);
        Assert.True(response.Headers.TryGetValues("ETag", out var etagValues));
        Assert.Equal("\"v1\"", etagValues.First());
    }
}
