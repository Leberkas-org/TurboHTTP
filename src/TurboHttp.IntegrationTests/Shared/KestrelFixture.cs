using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace TurboHttp.IntegrationTests.Shared;

/// <summary>
/// Shared Kestrel fixture for HTTP/1.0 and HTTP/1.1 integration tests.
/// Starts a real in-process Kestrel server on a random port and registers
/// all routes used by Phase 12 and Phase 13 tests.
/// </summary>
public sealed class KestrelFixture : IAsyncLifetime
{
    private WebApplication? _app;

    /// <summary>The TCP port Kestrel is listening on after <see cref="InitializeAsync"/>.</summary>
    public int Port { get; private set; }

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
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
        // HEAD /hello → 200, headers only (body suppressed by ASP.NET Core middleware)
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

        // GET /content/{*ct} → 200, Content-Type from catch-all path segment(s)
        // e.g. /content/text/html  → Content-Type: text/html
        //      /content/application/json → Content-Type: application/json
        app.MapGet("/content/{*ct}", async (HttpContext ctx, string ct) =>
        {
            ctx.Response.ContentType = ct;
            var body = "body"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // GET /methods → 200, body = request method
        app.MapGet("/methods", (HttpContext ctx) => Results.Content(ctx.Request.Method, "text/plain"));

        // GET|POST|PUT|DELETE|PATCH|OPTIONS|HEAD /any → 200, body = method name
        // Used by HTTP/1.1 verb tests
        app.MapMethods("/any",
            ["GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS", "HEAD"],
            (HttpContext ctx) => Results.Content(ctx.Request.Method, "text/plain"));

        // ── Body ──────────────────────────────────────────────────────────────

        // POST /echo → 200, echoes request body verbatim with same Content-Type
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

        // PUT /echo → 200, echoes request body (same handler as POST)
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

        // PATCH /echo → 200, echoes request body
        app.MapMethods("/echo", ["PATCH"], async (HttpContext ctx) =>
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

        // GET /headers/set?Foo=Bar → sets response headers from query parameters
        app.MapGet("/headers/set", (HttpContext ctx) =>
        {
            foreach (var param in ctx.Request.Query)
            {
                ctx.Response.Headers[param.Key] = param.Value;
            }

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

        // GET /multiheader → response has two X-Value: a, b headers (same name)
        app.MapGet("/multiheader", (HttpContext ctx) =>
        {
            ctx.Response.Headers.Append("X-Value", "alpha");
            ctx.Response.Headers.Append("X-Value", "beta");
            ctx.Response.ContentLength = 0;
            return Results.Empty;
        });

        // ── HTTP/1.1 Chunked ──────────────────────────────────────────────────

        // GET|HEAD /chunked/{kb} → chunked response, kb*1024 bytes of 'A'
        // StartAsync() commits the response headers before body, forcing chunked for HTTP/1.1
        app.MapMethods("/chunked/{kb:int}", ["GET", "HEAD"], async (HttpContext ctx, int kb) =>
        {
            ctx.Response.ContentType = "application/octet-stream";
            // Start headers without Content-Length → Kestrel uses Transfer-Encoding: chunked
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

        // GET /chunked/exact/{count}/{chunkBytes} → exactly `count` chunks of `chunkBytes` bytes
        // StartAsync() forces chunked encoding before writing body
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

        // POST /echo/chunked → echoes request body as chunked response (no Content-Length)
        app.MapPost("/echo/chunked", async (HttpContext ctx) =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var body = ms.ToArray();
            ctx.Response.ContentType = ctx.Request.ContentType ?? "application/octet-stream";
            // StartAsync() commits headers before body, forcing chunked encoding
            await ctx.Response.StartAsync();
            await ctx.Response.Body.WriteAsync(body);
        });

        // GET /chunked/trailer → chunked response; body includes "chunked-with-trailer"
        // Trailers are sent as trailing headers after the last chunk
        app.MapGet("/chunked/trailer", async (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.StartAsync();
            var body = "chunked-with-trailer"u8.ToArray();
            await ctx.Response.Body.WriteAsync(body);
            await ctx.Response.Body.FlushAsync();
            // Append trailer after body (requires HTTP/1.1 chunked + trailer support)
            if (ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseTrailersFeature>() is
                { } trailersFeature)
            {
                trailersFeature.Trailers["X-Checksum"] = "abc123";
            }
        });

        // GET /chunked/md5 → chunked response with Content-MD5 header in response headers
        app.MapGet("/chunked/md5", async (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/plain";
            var body = "checksum-body"u8.ToArray();
            var md5 = Convert.ToBase64String(MD5.HashData(body));
            ctx.Response.Headers["Content-MD5"] = md5;
            await ctx.Response.StartAsync();
            await ctx.Response.Body.WriteAsync(body);
        });

        // ── HTTP/1.1 Connection management ────────────────────────────────────

        // GET /close → returns Connection: close header
        app.MapGet("/close", async (HttpContext ctx) =>
        {
            ctx.Response.Headers["Connection"] = "close";
            ctx.Response.ContentType = "text/plain";
            var body = "closing"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // ── HTTP/1.1 Caching / ETag ───────────────────────────────────────────

        // GET /etag → resource with ETag support for conditional requests
        app.MapGet("/etag", async (HttpContext ctx) =>
        {
            const string etag = "\"v1\"";
            if (ctx.Request.Headers["If-None-Match"] == etag)
            {
                ctx.Response.StatusCode = 304;
                ctx.Response.Headers["ETag"] = etag;
                return;
            }

            ctx.Response.Headers["ETag"] = etag;
            ctx.Response.ContentType = "text/plain";
            var body = "etag-resource"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // GET /cache → response with Cache-Control, Last-Modified, Expires headers
        app.MapGet("/cache", async (HttpContext ctx) =>
        {
            ctx.Response.Headers["Cache-Control"] = "max-age=3600, public";
            ctx.Response.Headers["Last-Modified"] = DateTimeOffset.UtcNow.AddHours(-1).ToString("R");
            ctx.Response.Headers["Expires"] = DateTimeOffset.UtcNow.AddHours(1).ToString("R");
            ctx.Response.Headers["Pragma"] = "no-cache";
            ctx.Response.ContentType = "text/plain";
            var body = "cached-resource"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // GET /if-modified-since → supports If-Modified-Since conditional logic
        var fixedLastModified = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        app.MapGet("/if-modified-since", async (HttpContext ctx) =>
        {
            ctx.Response.Headers["Last-Modified"] = fixedLastModified.ToString("R");
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

        // ── Phase 14: Content Negotiation ─────────────────────────────────────

        // GET /negotiate → returns content matching the Accept header
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

        // GET /negotiate/vary → returns Vary: Accept header in response
        app.MapGet("/negotiate/vary", (HttpContext ctx) =>
        {
            ctx.Response.Headers.Vary = "Accept";
            return Results.Content("data", "text/plain");
        });

        // GET /gzip-meta → returns Content-Encoding: identity header (metadata only — body is plain)
        app.MapGet("/gzip-meta", async (HttpContext ctx) =>
        {
            ctx.Response.Headers["Content-Encoding"] = "identity";
            ctx.Response.ContentType = "text/plain";
            var body = "encoded-body"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // POST /form/multipart → accepts multipart/form-data, echoes body length
        app.MapPost("/form/multipart", async (HttpContext ctx) =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var received = ms.ToArray();
            ctx.Response.ContentType = "text/plain";
            var response = System.Text.Encoding.UTF8.GetBytes($"received:{received.Length}");
            ctx.Response.ContentLength = response.Length;
            await ctx.Response.Body.WriteAsync(response);
        });

        // POST /form/urlencoded → accepts application/x-www-form-urlencoded, echoes body
        app.MapPost("/form/urlencoded", async (HttpContext ctx) =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var received = ms.ToArray();
            ctx.Response.ContentType = "text/plain";
            var response = System.Text.Encoding.UTF8.GetBytes($"received:{received.Length}");
            ctx.Response.ContentLength = response.Length;
            await ctx.Response.Body.WriteAsync(response);
        });

        // ── Phase 14: Range Requests ──────────────────────────────────────────

        // GET /range/{kb} → range-capable resource, kb*1024 sequential bytes
        app.MapGet("/range/{kb:int}", (int kb) =>
        {
            var body = new byte[kb * 1024];
            for (var i = 0; i < body.Length; i++)
            {
                body[i] = (byte)(i % 256);
            }

            return Results.Bytes(body, "application/octet-stream", enableRangeProcessing: true);
        });

        // GET /range/etag → range-capable resource with ETag for If-Range testing
        const string rangeEtag = "\"range-v1\"";
        app.MapGet("/range/etag", (HttpContext ctx) =>
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

        // ── Phase 14: Additional Cache Routes ────────────────────────────────

        // GET /cache/no-store → returns Cache-Control: no-store
        app.MapGet("/cache/no-store", async (HttpContext ctx) =>
        {
            ctx.Response.Headers["Cache-Control"] = "no-store";
            ctx.Response.ContentType = "text/plain";
            var body = "no-store-resource"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // ── Phase 14: Slow Response ───────────────────────────────────────────

        // GET /slow/{count} → sends count ASCII 'x' bytes, 1 per write with a flush,
        // simulating a streaming server that delivers data incrementally.
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

        // ── Phase 14: Edge Case Routes ────────────────────────────────────────

        // GET /empty-cl → returns 200 with Content-Length: 0 and no body
        app.MapGet("/empty-cl", (HttpContext ctx) =>
        {
            ctx.Response.ContentLength = 0;
            return Results.Empty;
        });

        // GET /unknown-headers → response with non-standard X-Custom-* headers
        app.MapGet("/unknown-headers", (HttpContext ctx) =>
        {
            ctx.Response.Headers["X-Unknown-Foo"] = "bar";
            ctx.Response.Headers["X-Unknown-Bar"] = "baz";
            ctx.Response.ContentType = "text/plain";
            var body = "ok"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            return Results.Content("ok", "text/plain");
        });
    }
}
