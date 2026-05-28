using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Akka.Actor;
using Akka.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Server;
using QuicListenerOptionsServus = Servus.Akka.Transport.QuicListenerOptions;

namespace TurboHTTP.IntegrationTests.End2End.Shared;

public abstract class End2EndSpecBase : IAsyncLifetime
{
    private WebApplication? _app;
    private ITurboHttpClient? _client;
    private Microsoft.Extensions.DependencyInjection.ServiceProvider? _clientProvider;
    private X509Certificate2? _cert;

    protected abstract Version ProtocolVersion { get; }

    protected abstract void ConfigureEndpoints(WebApplication app);

    protected virtual bool UseTls => ProtocolVersion.Major >= 2;

    protected virtual void ConfigureServer(TurboServerOptions options, ushort port, X509Certificate2? cert)
    {
        if (ProtocolVersion.Major == 3)
        {
            if (!QuicConnection.IsSupported)
            {
                return;
            }

            var quicOptions = new QuicListenerOptionsServus
            {
                Host = "127.0.0.1",
                Port = port,
                ServerCertificate = cert!,
                ApplicationProtocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http3 }
            };

            options.Bind(quicOptions);
        }
        else if (ProtocolVersion.Major == 2)
        {
            options.ListenLocalhost(port, listen =>
            {
                listen.UseHttps(cert!);
                listen.Protocols = HttpProtocols.Http2;
            });
        }
        else
        {
            options.Bind(new TcpListenerOptions
            {
                Host = "127.0.0.1",
                Port = port
            });
        }
    }

    protected virtual void ConfigureClientOptions(TurboClientOptions options)
    {
    }

    protected ITurboHttpClient Client => _client!;

    protected string BaseUri { get; private set; } = string.Empty;

    protected CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var port = GetFreePort();
        var needsTls = UseTls;

        if (needsTls)
        {
            _cert = CreateSelfSignedCertificate("127.0.0.1");
        }

        var scheme = needsTls ? "https" : "http";
        BaseUri = $"{scheme}://127.0.0.1:{port}";

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        builder.Host.UseTurboHttp(options =>
        {
            ConfigureServer(options, port, _cert);
        });

        _app = builder.Build();
        ConfigureEndpoints(_app);
        await _app.StartAsync();

        var services = new ServiceCollection();

        var diSetup = DependencyResolverSetup.Create(services.BuildServiceProvider());
        var bootstrap = BootstrapSetup.Create();
        var system = ActorSystem.Create($"e2e-client-{Guid.NewGuid():N}", bootstrap.And(diSetup));
        services.AddSingleton(system);

        var clientOptions = new TurboClientOptions
        {
            BaseAddress = new Uri(BaseUri),
            DangerousAcceptAnyServerCertificate = needsTls
        };

        ConfigureClientOptions(clientOptions);

        services.AddTurboHttpClient();
        services.Replace(ServiceDescriptor.Singleton<IOptionsFactory<TurboClientOptions>>(
            new FixedOptionsFactory(clientOptions)));

        _clientProvider = services.BuildServiceProvider();

        var factory = _clientProvider.GetRequiredService<ITurboHttpClientFactory>();
        _client = factory.CreateClient(string.Empty);
        _client.DefaultRequestVersion = ProtocolVersion;
        _client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        _client.Timeout = TimeSpan.FromSeconds(10);
    }

    public virtual async ValueTask DisposeAsync()
    {
        _client?.Dispose();

        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        if (_clientProvider is not null)
        {
            var system = _clientProvider.GetService<ActorSystem>();
            if (system is not null)
            {
                await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
                await system.WhenTerminated.WaitAsync(TimeSpan.FromSeconds(5));
            }

            await _clientProvider.DisposeAsync();
        }

        _cert?.Dispose();
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
        sanBuilder.AddIpAddress(IPAddress.Parse("127.0.0.1"));
        request.CertificateExtensions.Add(sanBuilder.Build());

        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));

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

    private sealed class FixedOptionsFactory(TurboClientOptions options) : IOptionsFactory<TurboClientOptions>
    {
        public TurboClientOptions Create(string name) => options;
    }
}
