using System.Net;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.H11;

[Collection("H11")]
public sealed class RedirectSecuritySpec
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public RedirectSecuritySpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    [Fact(Timeout = 20000)]
    public async Task RedirectSecurity_should_handle_self_redirect_loop_gracefully()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            configure: b => b.WithRedirect(),
            system: _systemFixture.System);

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/loop");
        var response = await helper.Client.SendAsync(request, cts.Token);

        // Redirect stage detects loop and returns the final 302
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }

    [Fact(Timeout = 20000)]
    public async Task RedirectSecurity_should_follow_redirect_chain_of_4_hops()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            configure: b => b.WithRedirect(),
            system: _systemFixture.System);

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/chain/4");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 20000)]
    public async Task RedirectSecurity_should_reject_redirect_chain_exceeding_max_hops()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            configure: b => b.WithRedirect(),
            system: _systemFixture.System);

        // Assuming default max is 10, this chain has 11 hops
        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/chain/11");
        var response = await helper.Client.SendAsync(request, cts.Token);

        // When max redirects exceeded, stage returns the final redirect response
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }
}
