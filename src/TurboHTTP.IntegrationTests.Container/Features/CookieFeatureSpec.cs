using System.Net;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Container.Shared;

namespace TurboHTTP.IntegrationTests.Container.Features;

public sealed class CookieFeatureSpec : FeatureSpecBase
{
    public CookieFeatureSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture) { }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Cookie_should_roundtrip_set_and_echo(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithCookies().WithRedirect());
        var ct = TestContext.Current.CancellationToken;

        var setResponse = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookies/set?session=abc123"), ct);

        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        var body = await setResponse.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);
        Assert.Equal("abc123", json.RootElement.GetProperty("session").GetString());
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Cookie_should_persist_across_sequential_requests(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithCookies().WithRedirect());
        var ct = TestContext.Current.CancellationToken;

        await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookies/set?token=xyz"), ct);

        var echoResponse = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookies"), ct);

        var body = await echoResponse.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        Assert.True(json.RootElement.TryGetProperty("token", out var token),
            $"Cookie 'token' not sent on subsequent request. Body: {body}");
        Assert.Equal("xyz", token.GetString());
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Cookie_should_not_be_sent_when_cookies_disabled(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithRedirect());
        var ct = TestContext.Current.CancellationToken;

        await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookies/set?token=secret"), ct);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookies"), ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        Assert.Empty(json.RootElement.EnumerateObject());
    }
}
