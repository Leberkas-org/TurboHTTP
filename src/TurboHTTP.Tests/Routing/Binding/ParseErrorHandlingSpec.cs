using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using TurboHTTP.Server;
using TurboHTTP.Routing.Binding;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Routing.Binding;

public sealed class ParseErrorHandlingSpec
{
    [Fact(Timeout = 5000)]
    public async Task RouteValueBinder_should_throw_on_invalid_int()
    {
        var ctx = CreateContext("/orders/invalid", TestContext.Current.CancellationToken);
        ctx.Request.RouteValues["id"] = "invalid";
        var binder = new RouteValueBinder("id", typeof(int));
        await Assert.ThrowsAsync<ParameterParseException>(() => binder.BindAsync(ctx, null!).AsTask());
    }

    [Fact(Timeout = 5000)]
    public async Task RouteValueBinder_should_throw_on_invalid_guid()
    {
        var ctx = CreateContext("/items/invalid", TestContext.Current.CancellationToken);
        ctx.Request.RouteValues["id"] = "not-a-guid";
        var binder = new RouteValueBinder("id", typeof(Guid));
        await Assert.ThrowsAsync<ParameterParseException>(() => binder.BindAsync(ctx, null!).AsTask());
    }

    [Fact(Timeout = 5000)]
    public async Task HeaderBinder_should_throw_on_invalid_int()
    {
        var ctx = CreateContext("/test", TestContext.Current.CancellationToken);
        ctx.Request.Headers["X-Page-Size"] = "invalid";
        var binder = new HeaderBinder("X-Page-Size", typeof(int));
        await Assert.ThrowsAsync<ParameterParseException>(() => binder.BindAsync(ctx, null!).AsTask());
    }

    [Fact(Timeout = 5000)]
    public async Task QueryStringBinder_should_throw_on_invalid_int()
    {
        var ctx = CreateContext("/items?page=invalid", TestContext.Current.CancellationToken);
        var binder = new QueryStringBinder("page", typeof(int));
        await Assert.ThrowsAsync<ParameterParseException>(() => binder.BindAsync(ctx, null!).AsTask());
    }

    [Fact(Timeout = 5000)]
    public async Task DelegateHandler_should_return_400_on_route_parse_error()
    {
        const string pattern = "/orders/{id}";
        var factory = DelegateHandlerBinder.Bind(pattern, Handler);

        var ctx = CreateContext("/orders/invalid", TestContext.Current.CancellationToken);
        ctx.Request.RouteValues["id"] = "invalid";

        await factory(ctx, null!);

        Assert.Equal(400, ctx.Response.StatusCode);
        return;
        Ok<string> Handler(int id) => TypedResults.Ok(string.Concat("Order ", id));
    }

    [Fact(Timeout = 5000)]
    public async Task DelegateHandler_should_return_400_on_query_parse_error()
    {
        var handler = (int page = 1) =>
            TypedResults.Ok(string.Concat("Page ", page));
        const string pattern = "/items";
        var factory = DelegateHandlerBinder.Bind(pattern, handler);

        var ctx = CreateContext("/items?page=invalid", TestContext.Current.CancellationToken);

        await factory(ctx, null!);

        Assert.Equal(400, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task DelegateHandler_should_return_400_on_header_parse_error()
    {
        var handler = ([FromHeader(Name = "X-Page-Size")] int pageSize) =>
        TypedResults.Ok(string.Concat("Size ", pageSize));
        var pattern = "/items";
        var factory = DelegateHandlerBinder.Bind(pattern, handler);

        var ctx = CreateContext("/items", TestContext.Current.CancellationToken);
        ctx.Request.Headers["X-Page-Size"] = "invalid";

        await factory(ctx, null!);

        Assert.Equal(400, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task EntityDelegate_should_throw_binding_validation_on_parse_error()
    {
        const string pattern = "/entities/{id}";
        var factory = DelegateHandlerBinder.BindEntityDelegate(pattern, Handler);

        var ctx = CreateContext("/entities/invalid", TestContext.Current.CancellationToken);
        ctx.Request.RouteValues["id"] = "invalid";

        var ex = await Assert.ThrowsAsync<BindingValidationException>(() => factory(ctx, null!).AsTask());
        Assert.Equal(400, ex.StatusCode);
        return;
        GetEntityMessage Handler(int id) => new(id.ToString());
    }

    private sealed record GetEntityMessage(string Id);

    private static TurboHttpContext CreateContext(string path, CancellationToken cancellationToken = default)
    {
        return ServerTestContext.Request()
            .Get(path)
            .RequestAborted(cancellationToken)
            .Build();
    }
}
