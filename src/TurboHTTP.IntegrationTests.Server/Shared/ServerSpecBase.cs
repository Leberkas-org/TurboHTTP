using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;

namespace TurboHTTP.IntegrationTests.Server.Shared;

public abstract class ServerSpecBase : IAsyncLifetime
{
    private WebApplication? _app;
    private HttpClient? _client;

    protected ushort Port { get; private set; }

    protected HttpClient Client => _client!;

    protected IServiceProvider Services => _app!.Services;

    protected static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    protected abstract void ConfigureServer(WebApplicationBuilder builder, ushort port);

    protected abstract void ConfigureEndpoints(WebApplication app);

    protected virtual HttpClient? CreateHttpClient() => new();

    public async ValueTask InitializeAsync()
    {
        Port = GetFreePort();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        ConfigureServer(builder, Port);
        _app = builder.Build();
        ConfigureEndpoints(_app);
        await _app.StartAsync();
        _client = CreateHttpClient();
    }

    public virtual async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    protected static HttpClient CreateTlsClient(X509Certificate2? clientCertificate = null)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        if (clientCertificate is not null)
        {
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;
            handler.ClientCertificates.Add(clientCertificate);
        }
        return new HttpClient(handler);
    }

    protected static X509Certificate2 CreateSelfSignedCertificate(string cn)
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
        if (cn is "localhost")
        {
            sanBuilder.AddIpAddress(IPAddress.Loopback);
        }
        request.CertificateExtensions.Add(sanBuilder.Build());

        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(1));

        return X509CertificateLoader.LoadPkcs12(
            cert.Export(X509ContentType.Pfx),
            null,
            X509KeyStorageFlags.Exportable);
    }

    private static ushort GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return (ushort)port;
    }
}
