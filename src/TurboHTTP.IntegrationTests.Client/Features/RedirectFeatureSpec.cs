using System.Net;
using System.Text.Json;
using TurboHTTP.Client;
using TurboHTTP.IntegrationTests.Client.Shared;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Client.Features;

public sealed class RedirectFeatureSpec : FeatureSpecBase
{
    public RedirectFeatureSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Redirect_should_follow_single_302(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant, b => b.WithRedirect());

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/redirect/1"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Redirect_should_follow_chain_of_3_hops(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant, b => b.WithRedirect());

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/redirect/3"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Redirect_should_not_follow_beyond_max_limit(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant, b => b.WithRedirect(r => r.MaxRedirects = 2));

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/redirect/5"), CancellationToken);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Redirect_should_follow_absolute_redirect(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant, b => b.WithRedirect());

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/redirect-to?url=%2Fget"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.TryGetProperty("url", out _));
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Redirect_should_return_redirect_response_when_disabled(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/redirect/1"), CancellationToken);

        Assert.True(
            (int)response.StatusCode is >= 300 and < 400,
            $"Expected 3xx redirect status, got {response.StatusCode}");
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Redirect_should_follow_absolute_location(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant, b => b.WithRedirect());

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/absolute-redirect/2"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Redirect_should_follow_relative_location(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant, b => b.WithRedirect());

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/relative-redirect/2"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}