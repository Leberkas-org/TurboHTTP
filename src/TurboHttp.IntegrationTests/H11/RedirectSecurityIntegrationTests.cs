using System.Net;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.H11;

[Collection("H11")]
public sealed class RedirectSecurityIntegrationTests
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public RedirectSecurityIntegrationTests(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    private ClientHelper CreateRedirectClient()
    {
        return ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            configure: builder => builder.WithRedirect(),
            system: _systemFixture.System);
    }

    // ── Loop Detection ──────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9110-15.4-RSI-003: Self-redirect loop returns final redirect response")]
    public async Task Self_Redirect_Loop_Returns_Final_Response()
    {
        // RedirectBidiStage catches loop/max-exceeded and forwards the last response.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/loop");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }

    // ── Chain Depth ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9110-15.4-RSI-004: Redirect chain of 4 hops succeeds")]
    public async Task Redirect_Chain_4_Hops_Succeeds()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/chain/4");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "RFC9110-15.4-RSI-005: Redirect chain of 11 hops rejected (exceeds max 10)")]
    public async Task Redirect_Chain_11_Hops_Rejected()
    {
        // Default MaxRedirects = 5. A chain of 6 exceeds this.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/chain/11");
        var response = await helper.Client.SendAsync(request, cts.Token);

        // The stage forwards the last redirect response when max depth exceeded
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }
}
