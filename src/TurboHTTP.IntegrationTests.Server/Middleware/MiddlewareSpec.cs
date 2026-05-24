using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Routing;
using TurboHTTP.Server;
using TurboHTTP.Server.Middleware;

namespace TurboHTTP.IntegrationTests.Server.Middleware;

public sealed class MiddlewareSpec : ServerSpecBase
{
    protected override void ConfigureServer(IServiceCollection services, ushort port)
    {
        services.AddTurboKestrel(options =>
        {
            options.Bind(new TcpListenerOptions { Host = "127.0.0.1", Port = port });
        });
    }

    protected override void ConfigureRoutes(TurboRouteTable routeTable)
    {
        var pipeline = Services.GetRequiredService<TurboPipelineBuilder>();

        pipeline.Use(async (ctx, next) =>
        {
            ctx.Response.Headers["X-Powered-By"] = "TurboHTTP";
            await next(ctx);
        });

        pipeline.Map("/api", api =>
        {
            api.Use(async (ctx, next) =>
            {
                ctx.Response.Headers["X-Api-Version"] = "2.0";
                await next(ctx);
            });
        });

        routeTable.Add("GET", "/hello", () => Results.Ok("hello"));
        routeTable.Add("GET", "/api/data", () => Results.Ok(new { value = 42 }));
        routeTable.Add("GET", "/other", () => Results.Ok("other"));
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
