using System.Net;
using TurboHTTP.IntegrationTests.Kestrel.Shared;

namespace TurboHTTP.IntegrationTests.Kestrel.Features;

public sealed class FeatureInteractionSpec : FeatureSpecBase
{
    public FeatureInteractionSpec(ServerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture) { }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Interaction_should_preserve_cookies_across_redirect_hops(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithCookies().WithRedirect());
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/interaction/cookie-hop/1"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Interaction_should_serve_compressed_response_from_cache(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithDecompression().WithCache());
        var ct = TestContext.Current.CancellationToken;

        var r1 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/interaction/cache-gzip"), ct);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        var r2 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/interaction/cache-gzip"), ct);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Interaction_should_bypass_retry_on_cache_hit(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithCache().WithRetry());
        var ct = TestContext.Current.CancellationToken;

        var r1 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache/max-age/3600"), ct);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        var r2 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache/max-age/3600"), ct);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
    }
}
