using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Server;
using TurboHTTP.Routing.Binding;

namespace TurboHTTP.Tests.Server.Binding;

public sealed class AttributeBindingSpec
{
    [Fact(Timeout = 5000)]
    public async Task FromRoute_should_override_convention()
    {
        Delegate handler = ([FromRoute] string id) => TypedResults.Ok(string.Concat("route-", id));
        var bound = DelegateHandlerBinder.Bind("/items/{id}", handler);
        var ctx = CreateContext("/items/42");
        ctx.Request.RouteValues["id"] = "42";
        await bound(ctx, CreateServiceProvider());
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task FromQuery_should_bind_from_query_string()
    {
        Delegate handler = ([FromQuery] string q) => TypedResults.Ok(string.Concat("search-", q));
        var bound = DelegateHandlerBinder.Bind("/search", handler);
        var ctx = CreateContext("/search?q=hello");
        await bound(ctx, CreateServiceProvider());
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task FromQuery_should_override_route_convention()
    {
        Delegate handler = ([FromQuery] string id) => TypedResults.Ok(string.Concat("query-", id));
        var bound = DelegateHandlerBinder.Bind("/items/{id}", handler);
        var ctx = CreateContext("/items/ignored?id=from-query");
        ctx.Request.RouteValues["id"] = "ignored";
        await bound(ctx, CreateServiceProvider());
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task FromHeader_should_bind_from_request_header()
    {
        Delegate handler = ([FromHeader(Name = "X-Tenant")] string tenant)
            => TypedResults.Ok(string.Concat("tenant-", tenant));
        var bound = DelegateHandlerBinder.Bind("/test", handler);
        var ctx = CreateContext("/test");
        ctx.Request.Headers["X-Tenant"] = "acme";
        await bound(ctx, CreateServiceProvider());
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task FromHeader_should_use_parameter_name_when_Name_not_set()
    {
        Delegate handler = ([FromHeader] string accept) => TypedResults.Ok(string.Concat("accept-", accept));
        var bound = DelegateHandlerBinder.Bind("/test", handler);
        var ctx = CreateContext("/test");
        ctx.Request.Headers["accept"] = "application/json";
        await bound(ctx, CreateServiceProvider());
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task FromRoute_with_custom_name_should_bind_from_named_segment()
    {
        Delegate handler = ([FromRoute(Name = "userId")] string id) => TypedResults.Ok(string.Concat("user-", id));
        var bound = DelegateHandlerBinder.Bind("/users/{userId}", handler);
        var ctx = CreateContext("/users/99");
        ctx.Request.RouteValues["userId"] = "99";
        await bound(ctx, CreateServiceProvider());
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task FromServices_should_resolve_from_di()
    {
        Delegate handler = ([FromServices] IServiceProvider sp) => TypedResults.Ok("ok");
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var bound = DelegateHandlerBinder.Bind("/test", handler);
        var ctx = CreateContext("/test");
        await bound(ctx, services);
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task FromBody_should_deserialize_json()
    {
        Delegate handler = ([FromBody] CreateItemDto body) => TypedResults.Ok(body.Name);
        var bound = DelegateHandlerBinder.Bind("/items", handler);
        var ctx = CreateContextWithJsonBody("/items", """{"Name":"Widget","Quantity":5}""");
        await bound(ctx, CreateServiceProvider());
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Convention_should_still_work_without_attributes()
    {
        Delegate handler = (string id) => TypedResults.Ok(string.Concat("conv-", id));
        var bound = DelegateHandlerBinder.Bind("/items/{id}", handler);
        var ctx = CreateContext("/items/7");
        ctx.Request.RouteValues["id"] = "7";
        await bound(ctx, CreateServiceProvider());
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    private sealed record CreateItemDto(string Name, int Quantity);

    private sealed record ValidatedDto(
        [property: System.ComponentModel.DataAnnotations.Required] string Name,
        [property: System.ComponentModel.DataAnnotations.Range(1, 100)]
        int Quantity);

    private static IServiceProvider CreateServiceProvider()
    {
        return new ServiceCollection().AddLogging().BuildServiceProvider();
    }

    private static TurboHttpContext CreateContext(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost" + path);
        var connection = new TurboConnectionInfo("test", null, 0, null, 0);
        var services = CreateServiceProvider();
        return TestContextFactory.Create(request: request, connection: connection, services: services);
    }

    private static TurboHttpContext CreateContextWithJsonBody(string path, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost" + path)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        var connection = new TurboConnectionInfo("test", null, 0, null, 0);
        var services = CreateServiceProvider();
        return TestContextFactory.Create(request: request, connection: connection, services: services);
    }
}
