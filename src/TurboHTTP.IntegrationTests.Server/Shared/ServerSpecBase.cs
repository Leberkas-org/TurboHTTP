using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server.Shared;

public abstract class ServerSpecBase : IAsyncLifetime
{
    private IHost? _host;
    private HttpClient? _client;

    protected ushort Port { get; private set; }

    protected HttpClient Client => _client!;

    protected IServiceProvider Services => _host!.Services;

    protected static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    protected abstract void ConfigureServer(IServiceCollection services, ushort port);

    protected abstract void ConfigureRoutes(TurboRouteTable routeTable);

    protected virtual HttpClient? CreateHttpClient() => new();

    public async ValueTask InitializeAsync()
    {
        Port = GetFreePort();
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        ConfigureServer(builder.Services, Port);
        _host = builder.Build();
        ConfigureRoutes(_host.Services.GetRequiredService<TurboRouteTable>());
        await _host.StartAsync();
        _client = CreateHttpClient();
    }

    public virtual async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
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
