using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Routing;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server.Binding;

public sealed class DelegateRoutingIntegrationSpec
{
    [Fact(Timeout = 5000)]
    public void MapTurboGet_with_delegate_should_register_route()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddTurboKestrel();
        var app = builder.Build();

        app.MapTurboGet("/health", () => TypedResults.Ok("ok"));

        var table = app.Services.GetRequiredService<TurboRouteTable>();
        var result = table.Freeze().Match(HttpMethod.Get, "/health");
        Assert.True(result.IsMatch);
    }

    [Fact(Timeout = 5000)]
    public async Task MapTurboGet_with_delegate_should_invoke_handler()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddTurboKestrel();
        var app = builder.Build();

        app.MapTurboGet("/health", () => TypedResults.Ok("healthy"));

        var table = app.Services.GetRequiredService<TurboRouteTable>();
        var result = table.Freeze().Match(HttpMethod.Get, "/health");
        Assert.True(result.IsMatch);

        var ctx = CreateContext("/health");
        ctx.RequestServices = app.Services;
        await result.Dispatcher!.DispatchAsync(ctx, CancellationToken.None);

        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public void MapTurboGroup_with_delegate_should_register_prefixed_route()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddTurboKestrel();
        var app = builder.Build();

        var api = app.MapTurboGroup("/api");
        api.MapGet("/users", () => TypedResults.Ok("users"));

        var table = app.Services.GetRequiredService<TurboRouteTable>();
        Assert.True(table.Freeze().Match(HttpMethod.Get, "/api/users").IsMatch);
    }

    private static TurboHttpContext CreateContext(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost" + path);
        var connection = new TurboConnectionInfo("test", null, 0, null, 0);
        return TestContextFactory.Create(request: request, connection: connection);
    }
}
