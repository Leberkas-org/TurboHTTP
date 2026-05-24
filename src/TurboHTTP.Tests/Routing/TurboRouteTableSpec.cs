using Microsoft.AspNetCore.Http;
using TurboHTTP.Routing;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Routing;

public sealed class TurboRouteTableSpec
{
    [Fact(Timeout = 5000)]
    public void Add_should_register_route()
    {
        var table = new TurboRouteTable();
        table.Add("GET", "/health", _ => Results.Ok().ExecuteAsync(null!));
        var frozen = table.Freeze();
        var result = frozen.Match("GET", "/health");
        Assert.True(result.IsMatch);
    }

    [Fact(Timeout = 5000)]
    public void Freeze_should_return_same_instance_on_second_call()
    {
        var table = new TurboRouteTable();
        table.Add("GET", "/test", _ => Results.Ok().ExecuteAsync(null!));
        var first = table.Freeze();
        var second = table.Freeze();
        Assert.Same(first, second);
    }

    [Fact(Timeout = 5000)]
    public void Group_should_prepend_prefix()
    {
        var table = new TurboRouteTable();
        var group = table.CreateGroup("/api/v1");
        group.MapGet("/users", () => Results.Ok());
        var frozen = table.Freeze();
        var result = frozen.Match("GET", "/api/v1/users");
        Assert.True(result.IsMatch);
    }

    [Fact(Timeout = 5000)]
    public void Nested_groups_should_concat_prefixes()
    {
        var table = new TurboRouteTable();
        var api = table.CreateGroup("/api");
        var v1 = api.MapGroup("/v1");
        v1.MapGet("/items", () => Results.Ok());
        var frozen = table.Freeze();
        var result = frozen.Match("GET", "/api/v1/items");
        Assert.True(result.IsMatch);
    }

    [Fact(Timeout = 5000)]
    public void RouteBuilder_should_return_from_map_methods()
    {
        var table = new TurboRouteTable();
        var builder = table.Add("GET", "/test", _ => Results.Ok().ExecuteAsync(null!));
        Assert.NotNull(builder);
        Assert.IsType<TurboRouteHandlerBuilder>(builder);
    }
}
