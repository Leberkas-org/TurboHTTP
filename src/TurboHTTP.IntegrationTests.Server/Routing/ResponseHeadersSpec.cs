using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Routing;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server.Routing;

public sealed class ResponseHeadersSpec : ServerSpecBase
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
        routeTable.Add("GET", "/custom-header", (TurboHttpContext ctx) =>
        {
            ctx.Response.Headers["X-Request-Id"] = "abc-123";
            ctx.Response.StatusCode = 200;
            return Results.Ok("ok").ExecuteAsync(ctx);
        });

        routeTable.Add("GET", "/multi-header", (TurboHttpContext ctx) =>
        {
            ctx.Response.Headers.Append("X-Tag", "alpha");
            ctx.Response.Headers.Append("X-Tag", "beta");
            ctx.Response.StatusCode = 200;
            return Results.Ok("ok").ExecuteAsync(ctx);
        });

        routeTable.Add("GET", "/cache-headers", (TurboHttpContext ctx) =>
        {
            ctx.Response.Headers["Cache-Control"] = "no-cache, no-store";
            ctx.Response.Headers["ETag"] = "\"v1\"";
            ctx.Response.StatusCode = 200;
            return Results.Ok("cached").ExecuteAsync(ctx);
        });
    }

    [Fact(Timeout = 15000)]
    public async Task Custom_response_header_should_arrive_at_client()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/custom-header"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Request-Id", out var values));
        Assert.Equal("abc-123", values.First());
    }

    [Fact(Timeout = 15000)]
    public async Task Multiple_values_for_same_header_should_arrive()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/multi-header"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Tag", out var values));
        var tagList = values.ToList();
        Assert.Contains("alpha", tagList);
        Assert.Contains("beta", tagList);
    }

    [Fact(Timeout = 15000)]
    public async Task Standard_cache_headers_should_arrive()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/cache-headers"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Cache-Control", out var cacheValues));
        var cacheValue = cacheValues.First();
        Assert.Contains("no-cache", cacheValue);
        Assert.Contains("no-store", cacheValue);
        Assert.True(response.Headers.TryGetValues("ETag", out var etagValues));
        Assert.Equal("\"v1\"", etagValues.First());
    }
}
