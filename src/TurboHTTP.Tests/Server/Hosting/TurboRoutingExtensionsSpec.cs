using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Server.Hosting;
using TurboHTTP.Server.Routing;

namespace TurboHTTP.Tests.Server.Hosting;

public sealed class TurboRoutingExtensionsSpec
{
    [Fact(Timeout = 5000)]
    public void MapTurboGet_should_register_route()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddTurboKestrel();
        var app = builder.Build();

        var handlerBuilder = app.MapTurboGet("/health", () => TypedResults.Ok());
        Assert.IsType<TurboRouteHandlerBuilder>(handlerBuilder);

        var table = app.Services.GetRequiredService<TurboRouteTable>();
        var frozen = table.Freeze();
        Assert.True(frozen.Match(HttpMethod.Get, "/health").IsMatch);
    }

    [Fact(Timeout = 5000)]
    public void MapTurboPost_should_register_route()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddTurboKestrel();
        var app = builder.Build();

        app.MapTurboPost("/items", () => TypedResults.Created("/items/1", new { Id = 1 }));

        var table = app.Services.GetRequiredService<TurboRouteTable>();
        var frozen = table.Freeze();
        Assert.True(frozen.Match(HttpMethod.Post, "/items").IsMatch);
    }

    [Fact(Timeout = 5000)]
    public void MapTurboGroup_should_return_group_builder()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddTurboKestrel();
        var app = builder.Build();

        var group = app.MapTurboGroup("/api");
        Assert.IsType<TurboRouteGroupBuilder>(group);
    }

    [Fact(Timeout = 5000)]
    public void MapTurboGroup_routes_should_resolve()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddTurboKestrel();
        var app = builder.Build();

        app.MapTurboGroup("/api")
           .MapGet("/users", () => TypedResults.Ok());

        var table = app.Services.GetRequiredService<TurboRouteTable>();
        var frozen = table.Freeze();
        Assert.True(frozen.Match(HttpMethod.Get, "/api/users").IsMatch);
    }

    [Fact(Timeout = 5000)]
    public void MapTurboGet_should_support_fluent_metadata()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddTurboKestrel();
        var app = builder.Build();

        var handlerBuilder = app.MapTurboGet("/test", () => TypedResults.Ok())
            .WithName("GetTest")
            .WithTags("test");

        Assert.Equal("GetTest", handlerBuilder.Metadata.Name);
        Assert.Contains("test", handlerBuilder.Metadata.Tags);
    }
}
