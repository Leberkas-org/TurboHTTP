using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Transport;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Hosting.Tls;

public sealed class ClientCertificateModeRequireSpec : IAsyncLifetime
{
    private WebApplication? _app;
    private ushort _port;
    private X509Certificate2? _serverCert;
    private X509Certificate2? _clientCert;

    public async ValueTask InitializeAsync()
    {
        _port = GetFreePort();
        _serverCert = CreateSelfSignedCertificate("localhost");
        _clientCert = CreateSelfSignedCertificate("client");

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddTurboKestrel(options =>
        {
            options.ListenLocalhost(_port, listen =>
            {
                listen.UseHttps(_serverCert, httpsOptions =>
                {
                    httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                    httpsOptions.ClientCertificateValidationCallback = (_, cert, _, _) => cert is not null;
                });
                listen.Protocols = HttpProtocols.Http1;
            });
        });

        _app = builder.Build();
        _app.MapTurboGet("/test", () => Results.Ok("OK"));
        await _app.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _serverCert?.Dispose();
        _clientCert?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    [Fact(Timeout = 15000)]
    public async Task RequireCertificate_should_fail_without_client_certificate()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        using var client = new HttpClient(handler);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await client.GetAsync(
                new Uri($"https://127.0.0.1:{_port}/test"),
                TestContext.Current.CancellationToken));

        Assert.NotNull(ex.InnerException);
    }

    [Fact(Timeout = 15000)]
    public async Task RequireCertificate_should_accept_request_with_valid_client_certificate()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            ClientCertificateOptions = ClientCertificateOption.Manual
        };
        handler.ClientCertificates.Add(_clientCert!);

        using var client = new HttpClient(handler);

        var response = await client.GetAsync(
            new Uri($"https://127.0.0.1:{_port}/test"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static ushort GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return (ushort)port;
    }

    private static X509Certificate2 CreateSelfSignedCertificate(string cn)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={cn}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(cn);
        request.CertificateExtensions.Add(sanBuilder.Build());

        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(1));

        return X509CertificateLoader.LoadPkcs12(
            cert.Export(X509ContentType.Pfx),
            null,
            X509KeyStorageFlags.Exportable);
    }
}
