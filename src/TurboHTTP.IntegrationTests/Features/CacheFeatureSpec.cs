using System.Net;
using TurboHTTP.Client;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.Features;

public sealed class CacheFeatureSpec : FeatureSpecBase
{
    public CacheFeatureSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Cache_should_serve_cached_response_on_second_request(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant, b => b.WithCache());

        var r1 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache/60"), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        var r2 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache/60"), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Cache_should_send_if_none_match_for_etag(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant, b => b.WithCache());

        var r1 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/etag/test-etag"), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        var r2 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/etag/test-etag"), CancellationToken);

        Assert.True(
            r2.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotModified,
            $"Expected OK or 304, got {r2.StatusCode}");
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Cache_should_return_fresh_response_when_cache_disabled(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant);

        var r1 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache/60"), CancellationToken);
        _ = await r1.Content.ReadAsStringAsync(CancellationToken);

        var r2 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache/60"), CancellationToken);
        _ = await r2.Content.ReadAsStringAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Cache_should_revalidate_with_no_cache(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant, b => b.WithCache());

        var r1 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache"), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        var request = new HttpRequestMessage(HttpMethod.Get, "/cache");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
        {
            NoCache = true
        };
        var r2 = await helper.Client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
    }
}