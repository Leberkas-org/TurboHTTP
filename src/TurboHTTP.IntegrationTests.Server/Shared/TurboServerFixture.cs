using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Servus.Akka.Transport;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server.Shared;

public sealed class TurboServerFixture : IAsyncLifetime
{
    private WebApplication? _app;

    public ushort Port { get; private set; }

    public HttpClient Client { get; private set; } = null!;

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
        Client = new HttpClient();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
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

        app.MapGet("/ping", () => Results.Content("pong", "text/plain"));
        app.MapGet("/hello", () => Results.Ok("Hello from TurboHTTP Server"));
        app.MapGet("/other", () => Results.Ok("other"));
        app.MapPost("/echo", async (HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            return Results.Ok(body);
        });
        app.MapGet("/connection-info", (HttpContext ctx) =>
        {
            var remoteIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return Results.Ok(remoteIp);
        });
        app.MapGet("/api/data", () => Results.Ok(new { value = 42 }));
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
