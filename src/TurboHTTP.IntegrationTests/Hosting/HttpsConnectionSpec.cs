using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Hosting;

public sealed class HttpsConnectionSpec : IAsyncLifetime
{
    private WebApplication? _app;
    private ushort _port;
    private HttpClient? _client;

    public async ValueTask InitializeAsync()
    {
        _port = GetFreePort();
        var certificate = CreateSelfSignedCertificate();

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddTurboKestrel(options =>
        {
            options.ListenLocalhost(_port, listen =>
            {
                listen.UseHttps(certificate);
                listen.Protocols = HttpProtocols.Http1;
            });
        });

        _app = builder.Build();
        _app.MapTurboGet("/secure-hello", () => Results.Ok("Hello from HTTPS"));

        await _app.StartAsync();

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _client = new HttpClient(handler);
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_respond_over_https()
    {
        var response = await _client!.GetAsync(
            new Uri($"https://127.0.0.1:{_port}/secure-hello"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var value = JsonSerializer.Deserialize<string>(body);
        Assert.Equal("Hello from HTTPS", value);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_return_404_over_https_for_unknown_route()
    {
        var response = await _client!.GetAsync(
            new Uri($"https://127.0.0.1:{_port}/unknown"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static ushort GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return (ushort)port;
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(IPAddress.Loopback);
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
