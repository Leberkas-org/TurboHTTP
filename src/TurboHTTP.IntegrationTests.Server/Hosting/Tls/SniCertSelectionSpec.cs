using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server.Hosting.Tls;

public sealed class SniCertSelectionSpec : IAsyncLifetime
{
    private WebApplication? _app;
    private ushort _port;
    private HttpClient? _client;
    private X509Certificate2? _certA;
    private X509Certificate2? _certB;
    private int _selectorCallCount;

    public async ValueTask InitializeAsync()
    {
        _port = GetFreePort();
        _certA = CreateSelfSignedCertificate("host-a.local");
        _certB = CreateSelfSignedCertificate("host-b.local");
        _selectorCallCount = 0;

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddTurboKestrel(options =>
        {
            options.ListenLocalhost(_port, listen =>
            {
                listen.UseHttps(options =>
                {
                    options.ServerCertificateSelector = hostname =>
                    {
                        _selectorCallCount++;
                        return hostname == "host-a.local" ? _certA : _certB;
                    };
                });
                listen.Protocols = HttpProtocols.Http1;
            });
        });

        _app = builder.Build();
        _app.MapTurboGet("/sni-test", () => Results.Ok("SNI test response"));
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
        _certA?.Dispose();
        _certB?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    [Fact(Timeout = 15000)]
    public async Task Server_with_sni_selector_should_respond_to_requests()
    {
        var response = await _client!.GetAsync(
            new Uri($"https://127.0.0.1:{_port}/sni-test"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task ServerCertificateSelector_should_be_invoked_for_handshake()
    {
        var initialCount = _selectorCallCount;

        var response = await _client!.GetAsync(
            new Uri($"https://127.0.0.1:{_port}/sni-test"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(_selectorCallCount > initialCount, "Selector should have been called");
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
