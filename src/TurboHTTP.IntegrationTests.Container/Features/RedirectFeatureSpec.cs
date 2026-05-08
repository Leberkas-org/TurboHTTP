using System.Net;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Container.Shared;

namespace TurboHTTP.IntegrationTests.Container.Features;

public sealed class RedirectFeatureSpec : FeatureSpecBase
{
    public RedirectFeatureSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture) { }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Redirect_should_follow_single_302(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithRedirect());
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/redirect/1"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Redirect_should_follow_chain_of_3_hops(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithRedirect());
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/redirect/3"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Redirect_should_not_follow_beyond_max_limit(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithRedirect(r => r.MaxRedirects = 2));
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/redirect/5"), ct);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Redirect_should_follow_absolute_redirect(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithRedirect());
        var ct = TestContext.Current.CancellationToken;

        var targetUrl = $"{helper.Client.BaseAddress}get";
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/redirect-to?url={Uri.EscapeDataString(targetUrl)}"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.TryGetProperty("url", out _));
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Redirect_should_return_redirect_response_when_disabled(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/redirect/1"), ct);

        Assert.True(
            (int)response.StatusCode is >= 300 and < 400,
            $"Expected 3xx redirect status, got {response.StatusCode}");
    }
}
