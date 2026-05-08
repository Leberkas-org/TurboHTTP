using System.Net;
using TurboHTTP.IntegrationTests.Kestrel.Shared;

namespace TurboHTTP.IntegrationTests.Kestrel.Features;

public sealed class RedirectFeatureSpec : FeatureSpecBase
{
    public RedirectFeatureSpec(ServerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture) { }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Redirect_should_follow_301(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithRedirect());
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/redirect/301/hello"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.Equal("Hello World", body);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Redirect_should_follow_302(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithRedirect());
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/redirect/302/hello"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Redirect_should_follow_307(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithRedirect());
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/redirect/307/hello"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Redirect_should_follow_chain(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithRedirect());
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/redirect/chain/3"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Redirect_should_not_follow_beyond_limit(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithRedirect(r => r.MaxRedirects = 2));
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/redirect/chain/5"), ct);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Redirect_should_return_redirect_when_disabled(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/redirect/302/hello"), ct);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }
}
