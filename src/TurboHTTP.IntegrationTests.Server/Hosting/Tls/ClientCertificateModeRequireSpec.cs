using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Routing;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server.Hosting.Tls;

public sealed class ClientCertificateModeRequireSpec : ServerSpecBase
{
    private X509Certificate2? _serverCert;
    private X509Certificate2? _clientCert;

    protected override void ConfigureServer(IServiceCollection services, ushort port)
    {
        _serverCert = CreateSelfSignedCertificate("localhost");
        _clientCert = CreateSelfSignedCertificate("client");

        services.AddTurboKestrel(options =>
        {
            options.ListenLocalhost(port, listen =>
            {
                listen.UseHttps(_serverCert, httpsOptions =>
                {
                    httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                    httpsOptions.ClientCertificateValidationCallback = (_, cert, _, _) => cert is not null;
                });
                listen.Protocols = HttpProtocols.Http1;
            });
        });
    }

    protected override void ConfigureRoutes(TurboRouteTable routeTable)
    {
        routeTable.Add("GET", "/test", () => Results.Ok("OK"));
    }

    protected override HttpClient? CreateHttpClient() => null;

    public override async ValueTask DisposeAsync()
    {
        _serverCert?.Dispose();
        _clientCert?.Dispose();
        await base.DisposeAsync();
    }

    [Fact(Timeout = 15000)]
    public async Task RequireCertificate_should_fail_without_client_certificate()
    {
        using var client = CreateTlsClient();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await client.GetAsync(
                new Uri($"https://127.0.0.1:{Port}/test"),
                CancellationToken));

        Assert.NotNull(ex.InnerException);
    }

    [Fact(Timeout = 15000)]
    public async Task RequireCertificate_should_accept_request_with_valid_client_certificate()
    {
        using var client = CreateTlsClient(_clientCert);

        var response = await client.GetAsync(
            new Uri($"https://127.0.0.1:{Port}/test"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}