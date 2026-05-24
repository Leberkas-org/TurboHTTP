using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Routing;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server.Hosting;

public sealed class HttpsConnectionSpec : ServerSpecBase
{
    protected override void ConfigureServer(IServiceCollection services, ushort port)
    {
        var certificate = CreateSelfSignedCertificate("localhost");
        services.AddTurboKestrel(options =>
        {
            options.ListenLocalhost(port, listen =>
            {
                listen.UseHttps(certificate);
                listen.Protocols = HttpProtocols.Http1;
            });
        });
    }

    protected override void ConfigureRoutes(TurboRouteTable routeTable)
    {
        routeTable.Add("GET", "/secure-hello", () => Results.Ok("Hello from HTTPS"));
    }

    protected override HttpClient CreateHttpClient() => CreateTlsClient();

    [Fact(Timeout = 15000)]
    public async Task Server_should_respond_over_https()
    {
        var response = await Client.GetAsync(
            new Uri($"https://127.0.0.1:{Port}/secure-hello"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var value = JsonSerializer.Deserialize<string>(body);
        Assert.Equal("Hello from HTTPS", value);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_return_404_over_https_for_unknown_route()
    {
        var response = await Client.GetAsync(
            new Uri($"https://127.0.0.1:{Port}/unknown"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
