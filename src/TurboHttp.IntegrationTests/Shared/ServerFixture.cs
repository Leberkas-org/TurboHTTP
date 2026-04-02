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

namespace TurboHttp.IntegrationTests.Shared;

/// <summary>
/// Single shared Kestrel server for all integration tests.
/// Exposes three endpoints — HTTP/1.x plaintext, HTTP/2 h2c, and HTTPS (HTTP/1+2+3).
/// Registered as an assembly fixture so exactly one instance exists per test run.
/// All endpoints use OS-assigned port 0 to eliminate port conflicts.
/// </summary>
public sealed class ServerFixture : IAsyncLifetime
{
    private WebApplication? _app;

    /// <summary>HTTP/1.x plaintext port (for H10 and H11 tests).</summary>
    public int HttpPort { get; private set; }

    /// <summary>HTTP/2 cleartext (h2c) port.</summary>
    public int H2Port { get; private set; }

    /// <summary>HTTPS port (HTTP/1+2+3, TLS + QUIC).</summary>
    public int HttpsPort { get; private set; }

    /// <summary>Self-signed certificate used for the HTTPS endpoint.</summary>
    public X509Certificate2? Certificate { get; private set; }

    public async ValueTask InitializeAsync()
    {
        Certificate = CreateSelfSignedCertificate();

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestHeaderCount = 2000;
            options.Limits.MaxRequestHeadersTotalSize = 512 * 1024;

            // [0] HTTP/1.x plaintext
            options.Listen(IPAddress.Loopback, 0, lo => lo.Protocols = HttpProtocols.Http1);

            // [1] HTTP/2 h2c (prior knowledge, no TLS)
            options.Listen(IPAddress.Loopback, 0, lo => lo.Protocols = HttpProtocols.Http2);

            // Raise HTTP/2 limits to support high-concurrency integration tests.
            options.Limits.Http2.MaxStreamsPerConnection = 512;
            options.Limits.Http2.InitialConnectionWindowSize = 4 * 1024 * 1024;
            options.Limits.Http2.InitialStreamWindowSize = 1024 * 1024;

            // [2] HTTPS — HTTP/1 + HTTP/2 + HTTP/3 (QUIC)
            options.Listen(IPAddress.Loopback, 0, lo =>
            {
                lo.UseHttps(Certificate);
                lo.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
            });
        });

        var app = builder.Build();

        Routes.RegisterRoutes(app);
        Routes.RegisterH2Routes(app);
        Routes.RegisterH3Routes(app);
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

        await app.StartAsync();

        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.ToArray();

        // Addresses are emitted in registration order.
        // The HTTPS endpoint is the only one with scheme "https".
        // The two HTTP endpoints preserve ListenLocalhost() order.
        var httpAddresses = addresses.Where(a => a.StartsWith("http://")).ToArray();
        var httpsAddress = addresses.First(a => a.StartsWith("https://"));

        HttpPort = new Uri(httpAddresses[0]).Port;
        H2Port = new Uri(httpAddresses[1]).Port;
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

        req.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

        // SAN is required for QUIC/TLS 1.3
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
}