using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TurboHTTP.Benchmarks.Internal;

/// <summary>
/// Minimal Kestrel test server for benchmarking both HttpClient and TurboHttp.
/// Binds three dynamic ports: HTTP/1.1, HTTP/2 cleartext (h2c), and HTTP/3 (QUIC+TLS).
/// Exposes two simple benchmark routes with keep-alive enabled.
/// </summary>
public sealed class BenchmarkServer : IAsyncDisposable
{
    private WebApplication? _app;
    private X509Certificate2? _cert;

    /// <summary>Port on which the HTTP/1.1 listener is listening. Set after initialization.</summary>
    public int Http11Port { get; private set; }

    /// <summary>Port on which the HTTP/2 cleartext (h2c) listener is listening. Set after initialization.</summary>
    public int Http20Port { get; private set; }

    /// <summary>Port on which the HTTP/3 (QUIC+TLS) listener is listening. Set after initialization.</summary>
    public int Http30Port { get; private set; }

    /// <summary>
    /// Starts the Kestrel server on 127.0.0.1:0 (dynamic port) for each protocol.
    /// HTTP/1.1 and HTTP/2 use separate ports because HTTP/2 cleartext (h2c) requires
    /// an exclusive <see cref="HttpProtocols.Http2"/> listener — Kestrel ignores h2c prior
    /// knowledge on combined Http1AndHttp2 endpoints without TLS.
    /// HTTP/3 requires TLS (QUIC mandates TLS 1.3), so a self-signed certificate is used.
    /// Call this once in GlobalSetup.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        _cert = GenerateSelfSignedCert();

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        var cert = _cert;
        builder.Services.Configure<KestrelServerOptions>(options =>
        {
            // HTTP/1.1-only listener
            options.Listen(IPAddress.Loopback, 0, lo =>
                lo.Protocols = HttpProtocols.Http1);

            // HTTP/2 cleartext (h2c) prior-knowledge listener
            options.Listen(IPAddress.Loopback, 0, lo =>
                lo.Protocols = HttpProtocols.Http2);

            // HTTP/3 (QUIC+TLS) listener
            options.Listen(IPAddress.Loopback, 0, lo =>
            {
                lo.Protocols = HttpProtocols.Http3;
                lo.UseHttps(cert);
            });

            // Raise HTTP/2 limits to support high-concurrency benchmarks (CL=256+).
            options.Limits.Http2.MaxStreamsPerConnection = 512;
            options.Limits.Http2.InitialConnectionWindowSize = 4 * 1024 * 1024;
            options.Limits.Http2.InitialStreamWindowSize = 1024 * 1024;

            // Raise general limits for HTTP/3 high-concurrency benchmarks.
            options.Limits.MaxConcurrentConnections = null;
            options.Limits.MaxConcurrentUpgradedConnections = null;
        });

        var app = builder.Build();

        RegisterRoutes(app);

        await app.StartAsync();

        // Kestrel returns addresses in listener-registration order:
        // index 0 = HTTP/1.1, index 1 = HTTP/2, index 2 = HTTP/3
        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses
            .ToArray();

        Http11Port = new Uri(addresses[0]).Port;
        Http20Port = new Uri(addresses[1]).Port;
        Http30Port = new Uri(addresses[2]).Port;

        _app = app;
    }

    /// <summary>
    /// Stops the server and cleans up resources.
    /// Call this in GlobalCleanup.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        _cert?.Dispose();
    }

    private static X509Certificate2 GenerateSelfSignedCert()
    {
        using var key = RSA.Create(2048);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        var request = new CertificateRequest(
            "CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(san.Build());
        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null);
    }

    private static void RegisterRoutes(WebApplication app)
    {
        // Simple benchmark endpoint: minimal response body, suitable for throughput testing
        app.MapGet("/benchmark/simple", () =>
            Results.Content("OK\n", "text/plain"));

        // Payload echo endpoint: accepts POST body and returns size received
        app.MapPost("/benchmark/payload", async ctx =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var received = ms.ToArray();
            var response = System.Text.Encoding.UTF8.GetBytes($"received:{received.Length}");
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength = response.Length;
            await ctx.Response.Body.WriteAsync(response);
        });
    }
}
