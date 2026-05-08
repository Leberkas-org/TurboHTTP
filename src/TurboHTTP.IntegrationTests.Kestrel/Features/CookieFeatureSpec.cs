using System.Net;
using TurboHTTP.IntegrationTests.Kestrel.Shared;

namespace TurboHTTP.IntegrationTests.Kestrel.Features;

public sealed class CookieFeatureSpec : FeatureSpecBase
{
    public CookieFeatureSpec(ServerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture) { }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Cookie_should_roundtrip_set_and_echo(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithCookies());
        var ct = TestContext.Current.CancellationToken;

        await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookie/set/session/abc123"), ct);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookie/echo"), ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("session", body);
        Assert.Contains("abc123", body);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Cookie_should_send_httponly_on_subsequent_requests(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithCookies());
        var ct = TestContext.Current.CancellationToken;

        await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookie/set-httponly/token/secret"), ct);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookie/echo"), ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("token", body);
        Assert.Contains("secret", body);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Cookie_should_return_empty_when_no_cookies_set(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithCookies());
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookie/echo"), ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.Equal("{}", body.Trim());
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Cookie_should_store_multiple_set_cookie_headers(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithCookies());
        var ct = TestContext.Current.CancellationToken;

        await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookie/set/first/one"), ct);
        await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookie/set/second/two"), ct);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookie/echo"), ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("first", body);
        Assert.Contains("second", body);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Cookie_should_be_deleted_via_max_age_zero(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithCookies());
        var ct = TestContext.Current.CancellationToken;

        await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookie/set/temp/value"), ct);

        await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookie/delete/temp"), ct);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookie/echo"), ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain("temp", body);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Cookie_should_persist_across_redirect(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithCookies().WithRedirect());
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookie/set-and-redirect"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("redirect_cookie", body);
    }
}
