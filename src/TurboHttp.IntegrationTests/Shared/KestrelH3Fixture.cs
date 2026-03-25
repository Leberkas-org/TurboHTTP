using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using Servus.Core.Network;

namespace TurboHttp.IntegrationTests.Shared;

/// <summary>
/// Shared Kestrel fixture for HTTP/3 integration tests.
/// Starts a real in-process Kestrel server on a random port using
/// HttpProtocols.Http1AndHttp2AndHttp3 with a self-signed TLS certificate.
/// QUIC transport requires Windows 11+ or Linux with libmsquic.
/// </summary>
public sealed class KestrelH3Fixture : IAsyncLifetime
{
    private WebApplication? _app;

    /// <summary>The TCP/UDP port Kestrel is listening on after <see cref="InitializeAsync"/>.</summary>
    public int Port { get; private set; }

    /// <summary>The self-signed certificate used for TLS. Exposed for client trust configuration.</summary>
    public X509Certificate2? Certificate { get; private set; }

    public async ValueTask InitializeAsync()
    {
        var port = PortFinder.FindFreeLocalPort();
        Certificate = CreateSelfSignedCertificate();

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestHeaderCount = 2000;
            options.Limits.MaxRequestHeadersTotalSize = 512 * 1024;
            options.ListenLocalhost(port, listenOptions =>
            {
                listenOptions.UseHttps(Certificate);
                listenOptions.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
            });
        });

        var app = builder.Build();

        RegisterRoutes(app);

        await app.StartAsync();

        Port = port;
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

    /// <summary>
    /// Creates a self-signed certificate with SAN (Subject Alternative Name)
    /// for localhost. QUIC/TLS 1.3 requires SAN in certificates.
    /// </summary>
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

    private static void RegisterRoutes(WebApplication app)
    {
        // ── Basic ──────────────────────────────────────────────────────────────

        // GET /hello → 200 "Hello World"
        app.MapMethods("/hello", ["GET", "HEAD"], (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength = 11;
            return Results.Content("Hello World", "text/plain");
        });

        // GET /ping → 200 "pong"
        app.MapGet("/ping", () => Results.Content("pong", "text/plain"));

        // GET /large/{kb} → 200, kb*1024 bytes of 'A'
        app.MapGet("/large/{kb:int}", (int kb) =>
        {
            var body = new byte[kb * 1024];
            Array.Fill(body, (byte)'A');
            return Results.Bytes(body, "application/octet-stream");
        });

        // GET /status/{code} → returns the requested status code
        app.MapGet("/status/{code:int}", async (HttpContext ctx, int code) =>
        {
            ctx.Response.StatusCode = code;
            if (code != 204 && code != 304)
            {
                var body = "ok"u8.ToArray();
                ctx.Response.ContentLength = body.Length;
                await ctx.Response.Body.WriteAsync(body);
            }
        });

        // GET /methods → body = request method
        app.MapGet("/methods", (HttpContext ctx) => Results.Content(ctx.Request.Method, "text/plain"));

        // ── Body ──────────────────────────────────────────────────────────────

        // POST /echo → echoes request body verbatim
        app.MapPost("/echo", async ctx =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var body = ms.ToArray();
            var contentType = ctx.Request.ContentType ?? "application/octet-stream";
            ctx.Response.ContentType = contentType;
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // PUT /echo → echoes request body verbatim
        app.MapPut("/echo", async ctx =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var body = ms.ToArray();
            var contentType = ctx.Request.ContentType ?? "application/octet-stream";
            ctx.Response.ContentType = contentType;
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // ── Headers ───────────────────────────────────────────────────────────

        // GET /headers/echo → echoes X-* request headers back as response headers
        app.MapGet("/headers/echo", (HttpContext ctx) =>
        {
            foreach (var header in ctx.Request.Headers)
            {
                if (header.Key.StartsWith("X-", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.Headers[header.Key] = header.Value;
                }
            }

            ctx.Response.ContentLength = 0;
            return Results.Empty;
        });

        // GET /headers/count → responds with X-Header-Count
        app.MapGet("/headers/count", (HttpContext ctx) =>
        {
            var count = ctx.Request.Headers.Count;
            ctx.Response.Headers["X-Header-Count"] = count.ToString();
            ctx.Response.ContentLength = 0;
            return Results.Empty;
        });

        // GET /multiheader → response has two X-Value headers
        app.MapGet("/multiheader", (HttpContext ctx) =>
        {
            ctx.Response.Headers.Append("X-Value", "alpha");
            ctx.Response.Headers.Append("X-Value", "beta");
            ctx.Response.ContentLength = 0;
            return Results.Empty;
        });

        // ── HTTP/3 specific ─────────────────────────────────────────────────

        // GET /h3/settings → returns h3-ok to verify HTTP/3 connectivity
        app.MapGet("/h3/settings", (HttpContext ctx) =>
        {
            ctx.Response.Headers["X-Protocol"] = ctx.Request.Protocol;
            return Results.Content("h3-ok", "text/plain");
        });

        // GET /h3/protocol → returns the request protocol version in the body
        app.MapGet("/h3/protocol", (HttpContext ctx) =>
            Results.Content(ctx.Request.Protocol, "text/plain"));

        // GET /h3/many-headers → response with 20 custom headers
        app.MapGet("/h3/many-headers", (HttpContext ctx) =>
        {
            for (var i = 0; i < 20; i++)
            {
                ctx.Response.Headers[$"X-Custom-{i:D3}"] = $"value-{i:D3}";
            }

            ctx.Response.ContentType = "text/plain";
            var body = "many-headers"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            return Results.Content("many-headers", "text/plain");
        });

        // POST /h3/echo-binary → echoes binary request body
        app.MapPost("/h3/echo-binary", async ctx =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var body = ms.ToArray();
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // GET /h3/delay/{ms} → delays response by ms milliseconds, then returns 200
        app.MapGet("/h3/delay/{ms:int}", async (HttpContext ctx, int ms) =>
        {
            await Task.Delay(ms);
            ctx.Response.ContentType = "text/plain";
            var body = "delayed"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // GET /h3/stream/{count} → streams count bytes one at a time
        app.MapGet("/h3/stream/{count:int}", async (HttpContext ctx, int count) =>
        {
            ctx.Response.ContentType = "application/octet-stream";
            await ctx.Response.StartAsync();
            var single = new[] { (byte)'x' };
            for (var i = 0; i < count; i++)
            {
                await ctx.Response.Body.WriteAsync(single);
                await ctx.Response.Body.FlushAsync();
            }
        });

        // ── Shared Routes ─────────────────────────────────────────────────

        Routes.RegisterRedirectRoutes(app);
        Routes.RegisterCookieRoutes(app);
        Routes.RegisterRetryRoutes(app);
        Routes.RegisterCacheRoutes(app);
        Routes.RegisterContentEncodingRoutes(app);
        Routes.RegisterErrorHandlingRoutes(app);
    }
}
