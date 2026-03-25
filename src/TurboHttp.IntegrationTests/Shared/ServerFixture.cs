using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
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

            // [2] HTTPS — HTTP/1 + HTTP/2 + HTTP/3 (QUIC)
            options.Listen(IPAddress.Loopback, 0, lo =>
            {
                lo.UseHttps(Certificate);
                lo.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
            });
        });

        var app = builder.Build();

        RegisterRoutes(app);

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

    private static void RegisterRoutes(WebApplication app)
    {
        // ── Basic ──────────────────────────────────────────────────────────────

        app.MapMethods("/hello", ["GET", "HEAD"], (HttpContext ctx) =>
        {
            if (ctx.Request.Method == "HEAD") return Results.NoContent();
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength = 11;
            return Results.Content("Hello World", "text/plain");
        });

        app.MapGet("/ping", () => Results.Content("pong", "text/plain"));

        app.MapGet("/large/{kb:int}", (int kb) =>
        {
            var body = new byte[kb * 1024];
            Array.Fill(body, (byte)'A');
            return Results.Bytes(body, "application/octet-stream");
        });

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

        app.MapGet("/content/{*ct}", async (HttpContext ctx, string ct) =>
        {
            ctx.Response.ContentType = ct;
            var body = "body"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        app.MapGet("/methods", (HttpContext ctx) => Results.Content(ctx.Request.Method, "text/plain"));

        app.MapMethods("/any",
            ["GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS", "HEAD"],
            (HttpContext ctx) => Results.Content(ctx.Request.Method, "text/plain"));

        // ── Body ──────────────────────────────────────────────────────────────

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

        app.MapMethods("/echo", ["PATCH"], async ctx =>
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

        app.MapGet("/headers/set", (HttpContext ctx) =>
        {
            foreach (var param in ctx.Request.Query)
            {
                ctx.Response.Headers[param.Key] = param.Value;
            }

            ctx.Response.ContentLength = 0;
            return Results.Empty;
        });

        app.MapGet("/headers/count", (HttpContext ctx) =>
        {
            var count = ctx.Request.Headers.Count;
            ctx.Response.Headers["X-Header-Count"] = count.ToString();
            ctx.Response.ContentLength = 0;
            return Results.Empty;
        });

        app.MapGet("/auth", (HttpContext ctx) =>
        {
            if (!ctx.Request.Headers.ContainsKey("Authorization"))
            {
                return Results.StatusCode(401);
            }

            return Results.Ok();
        });

        app.MapGet("/multiheader", (HttpContext ctx) =>
        {
            ctx.Response.Headers.Append("X-Value", "alpha");
            ctx.Response.Headers.Append("X-Value", "beta");
            ctx.Response.ContentLength = 0;
            return Results.Empty;
        });

        // ── Chunked (HTTP/1.x) ──────────────────────────────────────────────

        app.MapMethods("/chunked/{kb:int}", ["GET", "HEAD"], async (HttpContext ctx, int kb) =>
        {
            ctx.Response.ContentType = "application/octet-stream";
            await ctx.Response.StartAsync();
            if (ctx.Request.Method == "HEAD")
            {
                return;
            }

            const int chunkSize = 8192;
            var chunk = new byte[chunkSize];
            Array.Fill(chunk, (byte)'A');
            var remaining = kb * 1024;
            while (remaining > 0)
            {
                var toWrite = Math.Min(remaining, chunkSize);
                await ctx.Response.Body.WriteAsync(chunk.AsMemory(0, toWrite));
                await ctx.Response.Body.FlushAsync();
                remaining -= toWrite;
            }
        });

        app.MapGet("/chunked/exact/{count:int}/{chunkBytes:int}", async (HttpContext ctx, int count, int chunkBytes) =>
        {
            ctx.Response.ContentType = "application/octet-stream";
            await ctx.Response.StartAsync();
            var chunk = new byte[chunkBytes];
            Array.Fill(chunk, (byte)'B');
            for (var i = 0; i < count; i++)
            {
                await ctx.Response.Body.WriteAsync(chunk);
                await ctx.Response.Body.FlushAsync();
            }
        });

        app.MapPost("/echo/chunked", async ctx =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var body = ms.ToArray();
            ctx.Response.ContentType = ctx.Request.ContentType ?? "application/octet-stream";
            await ctx.Response.StartAsync();
            await ctx.Response.Body.WriteAsync(body);
        });

        app.MapGet("/chunked/trailer", async ctx =>
        {
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.StartAsync();
            var body = "chunked-with-trailer"u8.ToArray();
            await ctx.Response.Body.WriteAsync(body);
            await ctx.Response.Body.FlushAsync();
            if (ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseTrailersFeature>() is
                { } trailersFeature)
            {
                trailersFeature.Trailers["X-Checksum"] = "abc123";
            }
        });

        app.MapGet("/chunked/md5", async ctx =>
        {
            ctx.Response.ContentType = "text/plain";
            var body = "checksum-body"u8.ToArray();
            var md5 = Convert.ToBase64String(System.Security.Cryptography.MD5.HashData(body));
            ctx.Response.Headers.ContentMD5 = md5;
            await ctx.Response.StartAsync();
            await ctx.Response.Body.WriteAsync(body);
        });

        // ── Connection management ────────────────────────────────────────────

        app.MapGet("/close", async ctx =>
        {
            ctx.Response.Headers.Connection = "close";
            ctx.Response.ContentType = "text/plain";
            var body = "closing"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // ── Caching / ETag ───────────────────────────────────────────────────

        app.MapGet("/etag", async ctx =>
        {
            const string etag = "\"v1\"";
            if (ctx.Request.Headers.IfNoneMatch == etag)
            {
                ctx.Response.StatusCode = 304;
                ctx.Response.Headers.ETag = etag;
                return;
            }

            ctx.Response.Headers.ETag = etag;
            ctx.Response.ContentType = "text/plain";
            var body = "etag-resource"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        app.MapGet("/cache", async ctx =>
        {
            ctx.Response.Headers.CacheControl = "max-age=3600, public";
            ctx.Response.Headers.LastModified = DateTimeOffset.UtcNow.AddHours(-1).ToString("R");
            ctx.Response.Headers.Expires = DateTimeOffset.UtcNow.AddHours(1).ToString("R");
            ctx.Response.Headers.Pragma = "no-cache";
            ctx.Response.ContentType = "text/plain";
            var body = "cached-resource"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        var fixedLastModified = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        app.MapGet("/if-modified-since", async ctx =>
        {
            ctx.Response.Headers.LastModified = fixedLastModified.ToString("R");
            if (ctx.Request.Headers.TryGetValue("If-Modified-Since", out var ims) &&
                DateTimeOffset.TryParse(ims, out var imsDate) &&
                imsDate >= fixedLastModified)
            {
                ctx.Response.StatusCode = 304;
                return;
            }

            ctx.Response.ContentType = "text/plain";
            var body = "fresh-resource"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // ── Content Negotiation ─────────────────────────────────────────────

        app.MapGet("/negotiate", (HttpContext ctx) =>
        {
            var accept = ctx.Request.Headers.Accept.ToString();
            if (accept.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Content("{\"ok\":true}", "application/json");
            }

            if (accept.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Content("<html><body>ok</body></html>", "text/html");
            }

            return Results.Content("default", "text/plain");
        });

        app.MapGet("/negotiate/vary", (HttpContext ctx) =>
        {
            ctx.Response.Headers.Vary = "Accept";
            return Results.Content("data", "text/plain");
        });

        app.MapGet("/gzip-meta", async ctx =>
        {
            ctx.Response.Headers.ContentEncoding = "identity";
            ctx.Response.ContentType = "text/plain";
            var body = "encoded-body"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // ── Form ────────────────────────────────────────────────────────────

        app.MapPost("/form/multipart", async ctx =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var received = ms.ToArray();
            ctx.Response.ContentType = "text/plain";
            var response = System.Text.Encoding.UTF8.GetBytes($"received:{received.Length}");
            ctx.Response.ContentLength = response.Length;
            await ctx.Response.Body.WriteAsync(response);
        });

        app.MapPost("/form/urlencoded", async ctx =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var received = ms.ToArray();
            ctx.Response.ContentType = "text/plain";
            var response = System.Text.Encoding.UTF8.GetBytes($"received:{received.Length}");
            ctx.Response.ContentLength = response.Length;
            await ctx.Response.Body.WriteAsync(response);
        });

        // ── Range Requests ──────────────────────────────────────────────────

        app.MapGet("/range/{kb:int}", (int kb) =>
        {
            var body = new byte[kb * 1024];
            for (var i = 0; i < body.Length; i++)
            {
                body[i] = (byte)(i % 256);
            }

            return Results.Bytes(body, "application/octet-stream", enableRangeProcessing: true);
        });

        const string rangeEtag = "\"range-v1\"";
        app.MapGet("/range/etag", (HttpContext _) =>
        {
            var body = new byte[512];
            for (var i = 0; i < body.Length; i++)
            {
                body[i] = (byte)(i % 256);
            }

            var entityTag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue(rangeEtag);
            return Results.Bytes(body, "application/octet-stream",
                entityTag: entityTag,
                enableRangeProcessing: true);
        });

        // ── Slow / streaming ────────────────────────────────────────────────

        app.MapGet("/slow/{count:int}", async (HttpContext ctx, int count) =>
        {
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.StartAsync();
            var single = new[] { (byte)'x' };
            for (var i = 0; i < count; i++)
            {
                await ctx.Response.Body.WriteAsync(single);
                await ctx.Response.Body.FlushAsync();
                await Task.Delay(1);
            }
        });

        app.MapGet("/delay/{ms:int}", async (HttpContext ctx, int ms) =>
        {
            await Task.Delay(ms);
            ctx.Response.ContentType = "text/plain";
            var body = "delayed"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // ── Edge Cases ──────────────────────────────────────────────────────

        app.MapGet("/empty-cl", (HttpContext ctx) =>
        {
            ctx.Response.ContentLength = 0;
            return Results.Empty;
        });

        app.MapGet("/unknown-headers", (HttpContext ctx) =>
        {
            ctx.Response.Headers["X-Unknown-Foo"] = "bar";
            ctx.Response.Headers["X-Unknown-Bar"] = "baz";
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength = 2;
            return Results.Content("ok", "text/plain");
        });

        app.MapGet("/edge/close-mid-response", async (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength = 10000;
            await ctx.Response.StartAsync();
            await ctx.Response.Body.WriteAsync("partial"u8.ToArray());
            await ctx.Response.Body.FlushAsync();
            ctx.Abort();
        });

        app.MapGet("/edge/large-header/{kb:int}", async (HttpContext ctx, int kb) =>
        {
            var headerValue = new string('X', kb * 1024);
            ctx.Response.Headers["X-Large-Header"] = headerValue;
            ctx.Response.ContentType = "text/plain";
            var body = "ok"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        app.MapGet("/edge/unknown-encoding", async (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/plain";
            ctx.Response.Headers.ContentEncoding = "x-custom";
            var body = "raw-payload"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        app.MapGet("/edge/empty-body", async (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength = 0;
            await ctx.Response.StartAsync();
        });

        // ── HTTP/2 specific ─────────────────────────────────────────────────

        app.MapGet("/h2/settings", (HttpContext _) => Results.Content("h2-ok", "text/plain"));

        app.MapGet("/h2/many-headers", (HttpContext ctx) =>
        {
            for (var i = 0; i < 20; i++)
            {
                ctx.Response.Headers[$"X-Custom-{i:D3}"] = $"value-{i:D3}";
            }

            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength = 12;
            return Results.Content("many-headers", "text/plain");
        });

        app.MapPost("/h2/echo-binary", async ctx =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var body = ms.ToArray();
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        app.MapGet("/h2/cookie", (HttpContext ctx) =>
        {
            ctx.Response.Headers.Append("Set-Cookie", "session=abc123; Path=/; HttpOnly");
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength = 10;
            return Results.Content("cookie-set", "text/plain");
        });

        app.MapGet("/h2/large-headers/{kb:int}", (HttpContext ctx, int kb) =>
        {
            for (var i = 0; i < 10; i++)
            {
                ctx.Response.Headers[$"X-Large-{i:D2}"] = new string('v', 90);
            }

            var body = new byte[kb * 1024];
            Array.Fill(body, (byte)'A');
            return Results.Bytes(body, "application/octet-stream");
        });

        app.MapGet("/h2/priority/{kb:int}", (int kb) =>
        {
            var body = new byte[kb * 1024];
            Array.Fill(body, (byte)'P');
            return Results.Bytes(body, "application/octet-stream");
        });

        app.MapGet("/h2/echo-path", (HttpContext ctx) =>
        {
            var path = ctx.Request.Path + ctx.Request.QueryString;
            return Results.Content(path, "text/plain");
        });

        app.MapGet("/h2/settings/max-concurrent", (HttpContext ctx) =>
        {
            var streamId = ctx.Request.Headers["X-Stream-Id"].ToString();
            ctx.Response.Headers["X-Stream-Id"] = streamId;
            ctx.Response.ContentLength = 0;
            return Results.Empty;
        });

        // ── HTTP/3 specific ─────────────────────────────────────────────────

        app.MapGet("/h3/settings", (HttpContext ctx) =>
        {
            ctx.Response.Headers["X-Protocol"] = ctx.Request.Protocol;
            return Results.Content("h3-ok", "text/plain");
        });

        app.MapGet("/h3/protocol", (HttpContext ctx) =>
            Results.Content(ctx.Request.Protocol, "text/plain"));

        app.MapGet("/h3/many-headers", (HttpContext ctx) =>
        {
            for (var i = 0; i < 20; i++)
            {
                ctx.Response.Headers[$"X-Custom-{i:D3}"] = $"value-{i:D3}";
            }

            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength = 12;
            return Results.Content("many-headers", "text/plain");
        });

        app.MapPost("/h3/echo-binary", async ctx =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var body = ms.ToArray();
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        app.MapGet("/h3/delay/{ms:int}", async (HttpContext ctx, int ms) =>
        {
            await Task.Delay(ms);
            ctx.Response.ContentType = "text/plain";
            var body = "delayed"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

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

        // ── Shared route groups ─────────────────────────────────────────────

        Routes.RegisterRedirectRoutes(app);
        Routes.RegisterCookieRoutes(app);
        Routes.RegisterRetryRoutes(app);
        Routes.RegisterCacheRoutes(app);
        Routes.RegisterContentEncodingRoutes(app);
        Routes.RegisterErrorHandlingRoutes(app);
        Routes.RegisterConnectionReuseRoutes(app);
        Routes.RegisterExpectContinueRoutes(app);
    }
}
