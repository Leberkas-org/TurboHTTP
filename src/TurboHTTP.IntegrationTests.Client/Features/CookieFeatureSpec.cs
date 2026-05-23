using System.Net;
using System.Text.Json;
using TurboHTTP.Client;
using TurboHTTP.Tests.Shared;
using TurboHTTP.IntegrationTests.Client.Shared;

namespace TurboHTTP.IntegrationTests.Features;

public sealed class CookieFeatureSpec : FeatureSpecBase
{
    public CookieFeatureSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Cookie_should_roundtrip_set_and_echo(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant, b => b.WithCookies().WithRedirect());

        var setResponse = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookies/set?session=abc123"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        var body = await setResponse.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);
        var cookies = json.RootElement.GetProperty("cookies");
        Assert.Equal("abc123", cookies.GetProperty("session").GetString());
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Cookie_should_persist_across_sequential_requests(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant, b => b.WithCookies().WithRedirect());

        await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookies/set?token=xyz"), CancellationToken);

        var echoResponse = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookies"), CancellationToken);

        var body = await echoResponse.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        var cookies = json.RootElement.GetProperty("cookies");
        Assert.True(cookies.TryGetProperty("token", out var token),
            $"Cookie 'token' not sent on subsequent request. Body: {body}");
        Assert.Equal("xyz", token.GetString());
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Cookie_should_not_be_sent_when_cookies_disabled(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant, b => b.WithRedirect());

        await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookies/set?token=secret"), CancellationToken);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookies"), CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        var cookies = json.RootElement.GetProperty("cookies");
        Assert.Empty(cookies.EnumerateObject());
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Cookie_should_be_removed_after_delete(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant, b => b.WithCookies().WithRedirect());

        await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookies/set?token=xyz"), CancellationToken);

        var beforeBody = await (await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookies"), CancellationToken))
            .Content.ReadAsStringAsync(CancellationToken);
        var before = JsonDocument.Parse(beforeBody);
        Assert.True(before.RootElement.GetProperty("cookies").TryGetProperty("token", out _),
            $"Cookie 'token' should be present before delete. Body: {beforeBody}");

        await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookies/delete?token"), CancellationToken);

        var afterBody = await (await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookies"), CancellationToken))
            .Content.ReadAsStringAsync(CancellationToken);
        var after = JsonDocument.Parse(afterBody);
        var cookies = after.RootElement.GetProperty("cookies");
        Assert.False(cookies.TryGetProperty("token", out _),
            $"Cookie 'token' should be absent after delete. Body: {afterBody}");
    }
}