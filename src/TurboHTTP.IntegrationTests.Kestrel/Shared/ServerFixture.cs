using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace TurboHTTP.IntegrationTests.Kestrel.Shared;

public sealed class ServerFixture : IAsyncLifetime
{
    private WebApplication? _app;

    public int H1Port { get; private set; }

    public int HttpPort { get; private set; }

    public int H2Port { get; private set; }

    public int HttpsPort { get; private set; }

    public bool IsEnabled { get; private set; }

    public async ValueTask InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("TURBOHTTP_KESTREL_TESTS") is not "true")
        {
            return;
        }

        IsEnabled = true;

        var cert = CreateSelfSignedCertificate();
        var httpsPort = GetFreePort();

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.Hosting", LogLevel.Warning);

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestHeaderCount = 2000;
            options.Limits.MaxRequestHeadersTotalSize = 512 * 1024;

            options.Listen(IPAddress.Loopback, 0, lo => lo.Protocols = HttpProtocols.Http1);
            options.Listen(IPAddress.Loopback, 0, lo => lo.Protocols = HttpProtocols.Http1);
            options.Listen(IPAddress.Loopback, 0, lo => lo.Protocols = HttpProtocols.Http2);

            options.Limits.Http2.MaxStreamsPerConnection = 512;
            options.Limits.Http2.InitialConnectionWindowSize = 4 * 1024 * 1024;
            options.Limits.Http2.InitialStreamWindowSize = 1024 * 1024;

            options.Listen(IPAddress.Loopback, httpsPort, lo =>
            {
                lo.UseHttps(cert);
                lo.Protocols = HttpProtocols.Http1AndHttp2;
            });
        });

        var app = builder.Build();

        Routes.RegisterRoutes(app);
        Routes.RegisterH2Routes(app);
        Routes.RegisterRedirectRoutes(app);
        Routes.RegisterCookieRoutes(app);
        Routes.RegisterRetryRoutes(app);
        Routes.RegisterCacheRoutes(app);
        Routes.RegisterContentEncodingRoutes(app);
        Routes.RegisterErrorHandlingRoutes(app);
        Routes.RegisterConnectionReuseRoutes(app);
        Routes.RegisterExpectContinueRoutes(app);
        Routes.RegisterResilienceRoutes(app);
        Routes.RegisterRequestCompressionRoutes(app);
        Routes.RegisterInteractionRoutes(app);
        Routes.RegisterOptionsTestRoutes(app);

        await app.StartAsync();

        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.ToArray();

        var httpAddresses = addresses.Where(a => a.StartsWith("http://")).ToArray();
        var httpsAddress = addresses.First(a => a.StartsWith("https://"));

        H1Port = new Uri(httpAddresses[0]).Port;
        HttpPort = new Uri(httpAddresses[1]).Port;
        H2Port = new Uri(httpAddresses[2]).Port;
        HttpsPort = new Uri(httpsAddress).Port;

        _app = app;
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);

        var req = new CertificateRequest(
            "CN=localhost",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        req.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));

        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                false));

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(IPAddress.Loopback);
        sanBuilder.AddIpAddress(IPAddress.IPv6Loopback);
        req.CertificateExtensions.Add(sanBuilder.Build());

        var cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null);
    }

    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
