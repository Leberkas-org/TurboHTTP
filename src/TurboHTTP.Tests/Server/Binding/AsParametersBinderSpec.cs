using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Server;
using TurboHTTP.Server.Binding;

namespace TurboHTTP.Tests.Server.Binding;

public sealed class AsParametersBinderSpec
{
    [Fact(Timeout = 5000)]
    public async Task AsParameters_should_bind_flat_dto_from_route_and_query()
    {
        Delegate handler = ([AsParameters] ItemQuery q) => TypedResults.Ok(string.Concat(q.Id, "-", q.Page));
        var bound = DelegateHandlerBinder.Bind("/items/{id}", handler);
        var ctx = CreateContext("/items/42?page=3");
        ctx.Request.RouteValues["id"] = "42";
        await bound(ctx, CreateServiceProvider());
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task AsParameters_should_bind_with_FromHeader_on_property()
    {
        Delegate handler = ([AsParameters] TenantQuery q)
            => TypedResults.Ok(string.Concat(q.Id, "-", q.Tenant));
        var bound = DelegateHandlerBinder.Bind("/items/{id}", handler);
        var ctx = CreateContext("/items/42");
        ctx.Request.RouteValues["id"] = "42";
        ctx.Request.Headers["X-Tenant"] = "acme";
        await bound(ctx, CreateServiceProvider());
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task AsParameters_should_bind_nested_complex_type()
    {
        Delegate handler = ([AsParameters] OuterQuery q)
            => TypedResults.Ok(string.Concat(q.Id, "-", q.Paging.Page, "-", q.Paging.Size));
        var bound = DelegateHandlerBinder.Bind("/items/{id}", handler);
        var ctx = CreateContext("/items/42?page=2&size=10");
        ctx.Request.RouteValues["id"] = "42";
        await bound(ctx, CreateServiceProvider());
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public void AsParameters_should_detect_circular_reference()
    {
        Delegate handler = ([AsParameters] CircularA q) => TypedResults.Ok();
        Assert.Throws<InvalidOperationException>(() => DelegateHandlerBinder.Bind("/test", handler));
    }

    [Fact(Timeout = 5000)]
    public async Task AsParameters_should_validate_annotated_properties()
    {
        Delegate handler = ([AsParameters] ValidatedQuery q) => TypedResults.Ok(q.Name);
        var bound = DelegateHandlerBinder.Bind("/items/{id}", handler);
        var ctx = CreateContext("/items/42");
        ctx.Request.RouteValues["id"] = "42";
        await bound(ctx, CreateServiceProvider());
        Assert.Equal(400, ctx.Response.StatusCode);
    }

    private sealed record ItemQuery(
        [property: FromRoute] string Id,
        [property: FromQuery] int Page);

    private sealed record TenantQuery(
        [property: FromRoute] string Id,
        [property: FromHeader(Name = "X-Tenant")] string Tenant);

    private sealed record OuterQuery(
        [property: FromRoute] string Id,
        [AsParameters] PagingQuery Paging);

    private sealed record PagingQuery(
        [property: FromQuery] int Page,
        [property: FromQuery] int Size);

    private sealed record ValidatedQuery(
        [property: FromRoute] string Id,
        [property: Required] string Name);

    public sealed record CircularA([AsParameters] CircularB B);

    public sealed record CircularB([AsParameters] CircularA A);

    private static IServiceProvider CreateServiceProvider()
        => new ServiceCollection().AddLogging().BuildServiceProvider();

    private static TurboHttpContext CreateContext(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost" + path);
        var connection = new TurboConnectionInfo("test", null, 0, null, 0);
        var services = CreateServiceProvider();
        return TestContextFactory.Create(request: request, connection: connection, services: services);
    }
}