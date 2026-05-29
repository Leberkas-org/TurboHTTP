using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Server.Hosting.Tls;

[Collection("Infrastructure")]
public sealed class ClientCertificateModeAllowSpec(ActorSystemFixture systemFixture) : ServerSpecBase(systemFixture)
{
    private X509Certificate2? _serverCert;
    private X509Certificate2? _clientCert;

    protected override void ConfigureServer(WebApplicationBuilder builder, ushort port)
    {
        _serverCert = CreateSelfSignedCertificate("localhost");
        _clientCert = CreateSelfSignedCertificate("client");

        builder.Host.UseTurboHttp(options =>
        {
            options.ListenLocalhost(port, listen =>
            {
                listen.UseHttps(_serverCert, httpsOptions =>
                {
                    httpsOptions.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
                    httpsOptions.ClientCertificateValidationCallback = (_, _, _, _) => true;
                });
                listen.Protocols = HttpProtocols.Http1;
            });
        });
    }

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/test", () => Results.Ok("OK"));
    }

    protected override HttpClient? CreateHttpClient() => null;

    public override async ValueTask DisposeAsync()
    {
        _serverCert?.Dispose();
        _clientCert?.Dispose();
        await base.DisposeAsync();
    }

    [Fact(Timeout = 15000)]
    public async Task AllowCertificate_should_accept_request_without_client_certificate()
    {
        using var client = CreateTlsClient();

        var response = await client.GetAsync(
            new Uri($"https://127.0.0.1:{Port}/test"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task AllowCertificate_should_accept_request_with_client_certificate()
    {
        using var client = CreateTlsClient(_clientCert);

        var response = await client.GetAsync(
            new Uri($"https://127.0.0.1:{Port}/test"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
