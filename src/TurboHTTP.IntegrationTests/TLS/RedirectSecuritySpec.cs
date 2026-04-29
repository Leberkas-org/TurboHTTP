using System.Net;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.TLS;

[Collection("TLS")]
public sealed class RedirectSecuritySpec
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public RedirectSecuritySpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    private ClientHelper CreateHttpsRedirectClient()
    {
        return ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(1, 1),
            scheme: "https",
            configure: builder => builder.WithRedirect(),
            system: _systemFixture.System);
    }


    [Fact(Timeout = 20000)]
    public async Task Https_to_http_301_downgrade_should_be_blocked()
    {
        // RedirectBidiStage catches ProtocolDowngrade and forwards the final redirect response.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateHttpsRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/cross-scheme/301");
        var response = await helper.Client.SendAsync(request, cts.Token);

        // The stage forwards the 301 response when downgrade is blocked
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
    }

    [Fact(Timeout = 20000)]
    public async Task Https_to_http_302_downgrade_should_be_blocked()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateHttpsRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/cross-scheme");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }
}
