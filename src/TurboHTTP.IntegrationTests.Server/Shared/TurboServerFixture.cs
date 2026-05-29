using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Servus.Akka.Transport;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server.Shared;

public sealed class TurboServerFixture : IAsyncLifetime
{
    private WebApplication? _app;

    public ushort Port { get; private set; }

    public HttpClient CreateClient() => new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.Zero
    });

    public async ValueTask InitializeAsync()
    {
        Port = GetFreePort();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Host.UseTurboHttp(options =>
        {
            options.Bind(new TcpListenerOptions { Host = "127.0.0.1", Port = Port });
        });

        _app = builder.Build();
        RegisterEndpoints(_app);
        await _app.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private static void RegisterEndpoints(WebApplication app)
    {
        app.Use(async (ctx, next) =>
        {
            ctx.Response.Headers.XPoweredBy = "TurboHTTP";
            await next(ctx);
        });

        app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/api"), api =>
        {
            api.Use(async (ctx, next) =>
            {
                ctx.Response.Headers["X-Api-Version"] = "2.0";
                await next(ctx);
            });
            api.UseRouting();
            api.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/api/data", () => Results.Ok(new { value = 42 }));
            });
        });

        // Basic
        app.MapGet("/ping", () => Results.Content("pong", "text/plain"));
        app.MapGet("/hello", () => Results.Ok("Hello from TurboHTTP Server"));
        app.MapGet("/other", () => Results.Ok("other"));
        app.MapGet("/ok", () => Results.Ok("fine"));
        app.MapGet("/echo", () => Results.Ok("ok"));
        app.MapGet("/text", () => Results.Ok("hello world"));
        app.MapGet("/api/data", () => Results.Ok(new { value = 42 }));

        // Echo / body
        app.MapPost("/echo", async (HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            return Results.Ok(body);
        });
        app.MapPost("/echo-body", async (HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            return Results.Ok(new { body });
        });
        app.MapPost("/echo-json", async (HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var raw = await reader.ReadToEndAsync();
            var parsed = JsonDocument.Parse(raw);
            return Results.Ok(parsed.RootElement);
        });
        app.MapPost("/form", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var name = form["name"].ToString();
            var age = form["age"].ToString();
            return Results.Ok(new { name, age });
        });

        // Connection info
        app.MapGet("/connection-info", (HttpContext ctx) =>
        {
            var remoteIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return Results.Ok(remoteIp);
        });
        app.MapGet("/connection", (HttpContext ctx) => Results.Ok(new
        {
            remoteIp = ctx.Connection.RemoteIpAddress?.ToString(),
            remotePort = ctx.Connection.RemotePort,
            localIp = ctx.Connection.LocalIpAddress?.ToString(),
            localPort = ctx.Connection.LocalPort
        }));
        app.MapGet("/protocol", (HttpContext ctx) => Results.Ok(new { protocol = ctx.Request.Protocol }));

        // Error handling
        app.MapGet("/throw-sync", () =>
        {
            throw new InvalidOperationException("sync boom");
#pragma warning disable CS0162
            return Results.Ok();
#pragma warning restore CS0162
        });
        app.MapGet("/throw-async", async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("async boom");
#pragma warning disable CS0162
            return Results.Ok();
#pragma warning restore CS0162
        });

        // Parameter binding
        app.MapGet("/users/{id:int}", (int id) => Results.Ok(new { id }));
        app.MapGet("/search", (string q) => Results.Ok(new { query = q }));
        app.MapGet("/paged", (string q, int page) => Results.Ok(new { query = q, page }));
        app.MapGet("/with-header", ([FromHeader(Name = "X-Tenant")] string tenant) => Results.Ok(new { tenant }));
        app.MapGet("/optional", (string? name) => Results.Ok(new { name = name ?? "default" }));
        app.MapGet("/items/{category}/{id}", (string category, int id) => Results.Ok(new { category, id }));

        // Response headers
        app.MapGet("/custom-header", (HttpContext ctx) =>
        {
            ctx.Response.Headers["X-Request-Id"] = "abc-123";
            return Results.Ok("ok");
        });
        app.MapGet("/multi-header", (HttpContext ctx) =>
        {
            ctx.Response.Headers.Append("X-Tag", "alpha");
            ctx.Response.Headers.Append("X-Tag", "beta");
            return Results.Ok("ok");
        });
        app.MapGet("/cache-headers", (HttpContext ctx) =>
        {
            ctx.Response.Headers.CacheControl = "no-cache, no-store";
            ctx.Response.Headers.ETag = "\"v1\"";
            return Results.Ok("cached");
        });

        // Multi-method routing
        app.MapGet("/multi", () => Results.Ok(new { method = "GET" }));
        app.MapPost("/multi", () => Results.Ok(new { method = "POST" }));
        app.MapPut("/multi", () => Results.Ok(new { method = "PUT" }));
        app.MapPost("/upload", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var file = form.Files.GetFile("document");
            if (file is null)
            {
                return Results.BadRequest("No file");
            }
            using var reader = new StreamReader(file.OpenReadStream());
            var content = await reader.ReadToEndAsync();
            return Results.Ok(new { fileName = file.FileName, size = file.Length, content });
        });

        // Streaming
        app.MapGet("/stream-bytes", () =>
        {
            var chunks = new[] { new byte[] { 1, 2, 3 }, new byte[] { 4, 5, 6 }, new byte[] { 7, 8, 9 } };
            return Results.Stream(async stream =>
            {
                foreach (var chunk in chunks)
                {
                    await stream.WriteAsync(chunk);
                }
            }, "application/octet-stream");
        });
        app.MapGet("/stream-text", () =>
        {
            var lines = new[] { "line1\n", "line2\n", "line3\n" };
            return Results.Stream(async stream =>
            {
                foreach (var line in lines)
                {
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(line));
                }
            }, "text/plain");
        });
        app.MapGet("/stream-large", () =>
        {
            return Results.Stream(async stream =>
            {
                var chunk = new byte[1024];
                Array.Fill(chunk, (byte)0xAB);
                for (var i = 0; i < 100; i++)
                {
                    await stream.WriteAsync(chunk);
                }
            }, "application/octet-stream");
        });
        app.MapGet("/stream-no-cl", () =>
        {
            var chunks = new[] { "chunk1", "chunk2", "chunk3" };
            return Results.Stream(async stream =>
            {
                foreach (var chunk in chunks)
                {
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(chunk));
                }
            }, "text/plain");
        });
        app.MapGet("/with-cl", (HttpContext ctx) =>
        {
            var body = Encoding.UTF8.GetBytes("exact-length-body");
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength = body.Length;
            return ctx.Response.Body.WriteAsync(body).AsTask();
        });
        app.MapGet("/no-content", () => Results.NoContent());
        app.MapGet("/not-modified", () => Results.StatusCode(304));

        // SSE
        app.MapGet("/events", async (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/event-stream";
            var events = new[] { "event1", "event2" };
            foreach (var evt in events)
            {
                var data = Encoding.UTF8.GetBytes($"data: {evt}\n\n");
                await ctx.Response.Body.WriteAsync(data);
            }
        });
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
