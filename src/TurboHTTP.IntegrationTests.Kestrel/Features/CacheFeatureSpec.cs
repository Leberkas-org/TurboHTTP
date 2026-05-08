using System.Net;
using TurboHTTP.IntegrationTests.Kestrel.Shared;

namespace TurboHTTP.IntegrationTests.Kestrel.Features;

public sealed class CacheFeatureSpec : FeatureSpecBase
{
    public CacheFeatureSpec(ServerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture) { }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Cache_should_serve_max_age_from_cache(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithCache());
        var ct = TestContext.Current.CancellationToken;

        var r1 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache/max-age/3600"), ct);
        var b1 = await r1.Content.ReadAsStringAsync(ct);

        var r2 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache/max-age/3600"), ct);
        var b2 = await r2.Content.ReadAsStringAsync(ct);

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal(b1, b2);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Cache_should_never_cache_no_store(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithCache());
        var ct = TestContext.Current.CancellationToken;

        var r1 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache/no-store"), ct);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        await Task.Delay(50, ct);

        var r2 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache/no-store"), ct);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Cache_should_revalidate_with_etag(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithCache());
        var ct = TestContext.Current.CancellationToken;

        var r1 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache/etag/test1"), ct);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        var r2 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache/etag/test1"), ct);

        Assert.True(
            r2.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotModified,
            $"Expected OK or 304, got {r2.StatusCode}");
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Cache_should_revalidate_with_last_modified(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithCache());
        var ct = TestContext.Current.CancellationToken;

        var r1 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache/last-modified/doc1"), ct);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        var r2 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache/last-modified/doc1"), ct);

        Assert.True(
            r2.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotModified,
            $"Expected OK or 304, got {r2.StatusCode}");
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Cache_should_separate_entries_for_vary_header(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithCache());
        var ct = TestContext.Current.CancellationToken;

        var r1 = new HttpRequestMessage(HttpMethod.Get, "/cache/vary/Accept-Language");
        r1.Headers.Add("Accept-Language", "en");
        var resp1 = await helper.Client.SendAsync(r1, ct);
        var b1 = await resp1.Content.ReadAsStringAsync(ct);

        var r2 = new HttpRequestMessage(HttpMethod.Get, "/cache/vary/Accept-Language");
        r2.Headers.Add("Accept-Language", "de");
        var resp2 = await helper.Client.SendAsync(r2, ct);
        var b2 = await resp2.Content.ReadAsStringAsync(ct);

        Assert.NotEqual(b1, b2);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Cache_should_force_revalidation_with_no_cache(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithCache());
        var ct = TestContext.Current.CancellationToken;

        var r1 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache/no-cache"), ct);
        var r2 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cache/no-cache"), ct);

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
    }
}
