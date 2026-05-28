using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server.Middleware;

public sealed class MiddlewareSpec : ServerSpecBase
{
    protected override void ConfigureServer(WebApplicationBuilder builder, ushort port)
    {
        builder.Host.UseTurboHttp(options =>
        {
            options.Bind(new TcpListenerOptions { Host = "127.0.0.1", Port = port });
        });
    }

    protected override void ConfigureEndpoints(WebApplication app)
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

        app.MapGet("/hello", () => Results.Ok("hello"));
        app.MapGet("/api/data", () => Results.Ok(new { value = 42 }));
        app.MapGet("/other", () => Results.Ok("other"));
    }

    [Fact(Timeout = 15000)]
    public async Task Global_middleware_should_set_response_header()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/hello"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Powered-By"));
        Assert.Equal("TurboHTTP", response.Headers.GetValues("X-Powered-By").First());
    }

    [Fact(Timeout = 15000)]
    public async Task Mapped_middleware_should_apply_to_matching_path()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/api/data"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Api-Version"));
        Assert.Equal("2.0", response.Headers.GetValues("X-Api-Version").First());
    }

    [Fact(Timeout = 15000)]
    public async Task Mapped_middleware_should_not_apply_to_other_paths()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/other"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Api-Version"));
    }

    [Fact(Timeout = 15000)]
    public async Task Global_middleware_should_apply_to_all_paths()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/other"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Powered-By"));
        Assert.Equal("TurboHTTP", response.Headers.GetValues("X-Powered-By").First());
    }
}
