using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server.Hosting.Tls;

public sealed class SniCertSelectionSpec : ServerSpecBase
{
    private X509Certificate2? _certA;
    private X509Certificate2? _certB;
    private int _selectorCallCount;

    protected override void ConfigureServer(IServiceCollection services, ushort port)
    {
        _certA = CreateSelfSignedCertificate("host-a.local");
        _certB = CreateSelfSignedCertificate("host-b.local");
        _selectorCallCount = 0;

        services.AddTurboKestrel(options =>
        {
            options.ListenLocalhost(port, listen =>
            {
                listen.UseHttps(httpsOptions =>
                {
                    httpsOptions.ServerCertificateSelector = hostname =>
                    {
                        _selectorCallCount++;
                        return hostname == "host-a.local" ? _certA : _certB;
                    };
                });
                listen.Protocols = HttpProtocols.Http1;
            });
        });
    }

    protected override void ConfigureRoutes(TurboRouteTable routeTable)
    {
        routeTable.Add("GET", "/sni-test", () => Results.Ok("SNI test response"));
    }

    protected override HttpClient CreateHttpClient() => CreateTlsClient();

    public override async ValueTask DisposeAsync()
    {
        _certA?.Dispose();
        _certB?.Dispose();
        await base.DisposeAsync();
    }

    [Fact(Timeout = 15000)]
    public async Task Server_with_sni_selector_should_respond_to_requests()
    {
        var response = await Client.GetAsync(
            new Uri($"https://127.0.0.1:{Port}/sni-test"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task ServerCertificateSelector_should_be_invoked_for_handshake()
    {
        var initialCount = _selectorCallCount;

        var response = await Client.GetAsync(
            new Uri($"https://127.0.0.1:{Port}/sni-test"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(_selectorCallCount > initialCount, "Selector should have been called");
    }
}