using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server.Routing;

public sealed class ResponseHeadersSpec : ServerSpecBase
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
