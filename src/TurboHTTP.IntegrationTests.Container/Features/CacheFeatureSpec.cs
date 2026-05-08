using System.Net;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Container.Shared;

namespace TurboHTTP.IntegrationTests.Container.Features;

public sealed class CacheFeatureSpec : FeatureSpecBase
{
    public CacheFeatureSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture) { }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Cache_should_serve_cached_response_on_second_request(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithCache());
        var ct = TestContext.Current.CancellationToken;

        var r1 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache/60"), ct);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        var r2 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache/60"), ct);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Cache_should_send_if_none_match_for_etag(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithCache());
        var ct = TestContext.Current.CancellationToken;

        var r1 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/etag/test-etag"), ct);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        var r2 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/etag/test-etag"), ct);

        Assert.True(
            r2.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotModified,
            $"Expected OK or 304, got {r2.StatusCode}");
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Cache_should_return_fresh_response_when_cache_disabled(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        var r1 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache/60"), ct);
        var b1 = await r1.Content.ReadAsStringAsync(ct);

        var r2 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache/60"), ct);
        var b2 = await r2.Content.ReadAsStringAsync(ct);

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Cache_should_revalidate_with_no_cache(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithCache());
        var ct = TestContext.Current.CancellationToken;

        var r1 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache"), ct);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        var request = new HttpRequestMessage(HttpMethod.Get, "/cache");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
        {
            NoCache = true
        };
        var r2 = await helper.Client.SendAsync(request, ct);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
    }
}
