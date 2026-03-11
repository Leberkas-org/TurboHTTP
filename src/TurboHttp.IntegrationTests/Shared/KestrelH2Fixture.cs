using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace TurboHttp.IntegrationTests.Shared;

/// <summary>
/// Shared Kestrel fixture for HTTP/2 cleartext (h2c) integration tests.
/// Starts a real in-process Kestrel server on a random port using
/// HttpProtocols.Http2 only (no TLS, no HTTP/1.1).
/// </summary>
public sealed class KestrelH2Fixture : IAsyncLifetime
{
    private WebApplication? _app;

    /// <summary>The TCP port Kestrel is listening on after <see cref="InitializeAsync"/>.</summary>
    public int Port { get; private set; }

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestHeaderCount = 2000;
            // MaxRequestHeadersTotalSize must be <= MaxRequestBufferSize (default 1 MB).
            // 512 KB handles 500 custom headers × ~100 bytes each = ~50 KB comfortably.
            options.Limits.MaxRequestHeadersTotalSize = 512 * 1024;
            options.ListenAnyIP(0, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });
        builder.Logging.ClearProviders();

        var app = builder.Build();

        RegisterRoutes(app);

        await app.StartAsync();

        var server = app.Services.GetRequiredService<IServer>();
        var addrFeature = server.Features.Get<IServerAddressesFeature>()!;
        Port = new Uri(addrFeature.Addresses.First()).Port;

        _app = app;
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
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
        app.MapPost("/echo", async (HttpContext ctx) =>
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
        app.MapPut("/echo", async (HttpContext ctx) =>
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

        // GET /headers/count → responds with X-Header-Count indicating how many request headers arrived
        app.MapGet("/headers/count", (HttpContext ctx) =>
        {
            var count = ctx.Request.Headers.Count;
            ctx.Response.Headers["X-Header-Count"] = count.ToString();
            ctx.Response.ContentLength = 0;
            return Results.Empty;
        });

        // GET /auth → 401 without Authorization, 200 with any Authorization value
        app.MapGet("/auth", (HttpContext ctx) =>
        {
            if (!ctx.Request.Headers.ContainsKey("Authorization"))
            {
                return Results.StatusCode(401);
            }

            return Results.Ok();
        });

        // GET /multiheader → response has two X-Value headers
        app.MapGet("/multiheader", (HttpContext ctx) =>
        {
            ctx.Response.Headers.Append("X-Value", "alpha");
            ctx.Response.Headers.Append("X-Value", "beta");
            ctx.Response.ContentLength = 0;
            return Results.Empty;
        });

        // ── Slow / streaming ──────────────────────────────────────────────────

        // GET /slow/{count} → sends count bytes 1-at-a-time
        app.MapGet("/slow/{count:int}", async (HttpContext ctx, int count) =>
        {
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.StartAsync();
            var single = new byte[] { (byte)'x' };
            for (var i = 0; i < count; i++)
            {
                await ctx.Response.Body.WriteAsync(single);
                await ctx.Response.Body.FlushAsync();
                await Task.Delay(1);
            }
        });

        // ── HTTP/2 specific ───────────────────────────────────────────────────

        // GET /h2/settings → echoes some server settings info
        app.MapGet("/h2/settings", (HttpContext ctx) =>
        {
            return Results.Content("h2-ok", "text/plain");
        });

        // GET /h2/many-headers → response with 20 custom headers
        app.MapGet("/h2/many-headers", (HttpContext ctx) =>
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

        // POST /h2/echo-binary → echoes binary request body
        app.MapPost("/h2/echo-binary", async (HttpContext ctx) =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var body = ms.ToArray();
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // GET /h2/cookie → response with a Set-Cookie header
        app.MapGet("/h2/cookie", (HttpContext ctx) =>
        {
            ctx.Response.Headers.Append("Set-Cookie", "session=abc123; Path=/; HttpOnly");
            ctx.Response.ContentType = "text/plain";
            var body = "cookie-set"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            return Results.Content("cookie-set", "text/plain");
        });

        // GET /h2/large-headers/{kb} → returns kb*1024 bytes with 10 custom response headers
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

        // GET /h2/priority/{kb} → returns kb*1024 bytes (used for priority stream tests)
        app.MapGet("/h2/priority/{kb:int}", (int kb) =>
        {
            var body = new byte[kb * 1024];
            Array.Fill(body, (byte)'P');
            return Results.Bytes(body, "application/octet-stream");
        });

        // GET /h2/echo-path → echoes the request :path pseudo-header value in body
        app.MapGet("/h2/echo-path", (HttpContext ctx) =>
        {
            var path = ctx.Request.Path + ctx.Request.QueryString;
            return Results.Content(path, "text/plain");
        });

        // GET /h2/settings/max-concurrent → echoes X-Stream-Id header value
        app.MapGet("/h2/settings/max-concurrent", (HttpContext ctx) =>
        {
            var streamId = ctx.Request.Headers["X-Stream-Id"].ToString();
            ctx.Response.Headers["X-Stream-Id"] = streamId;
            ctx.Response.ContentLength = 0;
            return Results.Empty;
        });

        // ── Redirect Routes ─────────────────────────────────────────────────
        KestrelFixture.RegisterRedirectRoutes(app);
    }
}
