using TurboHTTP.Routing;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server.Routing;

public sealed class TurboEntityBuilderSpec
{
    private sealed class TestActorKey;

    private sealed record TestMessage(string Id);

    [Fact(Timeout = 5000)]
    public void AddToRouteTable_should_register_get_route()
    {
        var builder = new TurboEntityBuilder("/orders/{id}");
        builder.OnGet((TurboHttpContext ctx) => new TestMessage(ctx.Request.RouteValues["id"]!.ToString()!));

        var table = new TurboRouteTable();
        builder.AddToRouteTable(table);
        var frozen = table.Freeze();

        var result = frozen.Match(HttpMethod.Get, "/orders/42");
        Assert.True(result.IsMatch);
        Assert.IsType<EntityDispatcher>(result.Dispatcher);
    }

    [Fact(Timeout = 5000)]
    public void AddToRouteTable_should_register_tell_route()
    {
        var builder = new TurboEntityBuilder("/orders/{id}");
        builder.OnPost(() => new TestMessage("new")).AcceptedResponse();

        var table = new TurboRouteTable();
        builder.AddToRouteTable(table);
        var frozen = table.Freeze();

        Assert.True(frozen.Match(HttpMethod.Post, "/orders/1").IsMatch);
    }

    [Fact(Timeout = 5000)]
    public void AddToRouteTable_should_register_multiple_methods()
    {
        var builder = new TurboEntityBuilder("/orders/{id}");
        builder.OnGet(() => new TestMessage("get"));
        builder.OnPut(() => new TestMessage("put"));
        builder.OnDelete(() => new TestMessage("del"));

        var table = new TurboRouteTable();
        builder.AddToRouteTable(table);
        var frozen = table.Freeze();

        Assert.True(frozen.Match(HttpMethod.Get, "/orders/1").IsMatch);
        Assert.True(frozen.Match(HttpMethod.Put, "/orders/1").IsMatch);
        Assert.True(frozen.Match(HttpMethod.Delete, "/orders/1").IsMatch);
        Assert.False(frozen.Match(HttpMethod.Post, "/orders/1").IsMatch);
    }

    [Fact(Timeout = 5000)]
    public void AddToRouteTable_should_extract_route_values()
    {
        var builder = new TurboEntityBuilder("/tenants/{tenantId}/orders/{orderId}");
        builder.OnGet(() => new TestMessage("get"));

        var table = new TurboRouteTable();
        builder.AddToRouteTable(table);
        var frozen = table.Freeze();

        var result = frozen.Match(HttpMethod.Get, "/tenants/t1/orders/o42");
        Assert.True(result.IsMatch);
        Assert.Equal("t1", result.RouteValues["tenantId"]);
        Assert.Equal("o42", result.RouteValues["orderId"]);
    }

    [Fact(Timeout = 5000)]
    public void AddToRouteTable_should_not_match_unregistered_method()
    {
        var builder = new TurboEntityBuilder("/orders/{id}");
        builder.OnGet(() => new TestMessage("get"));

        var table = new TurboRouteTable();
        builder.AddToRouteTable(table);
        var frozen = table.Freeze();

        Assert.False(frozen.Match(HttpMethod.Post, "/orders/1").IsMatch);
    }

    [Fact(Timeout = 5000)]
    public void AddToRouteTable_should_coexist_with_delegate_routes()
    {
        var table = new TurboRouteTable();

        table.Add(HttpMethod.Get, "/health", ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        var builder = new TurboEntityBuilder("/orders/{id}");
        builder.OnGet(() => new TestMessage("get"));
        builder.AddToRouteTable(table);

        var frozen = table.Freeze();

        var healthResult = frozen.Match(HttpMethod.Get, "/health");
        Assert.True(healthResult.IsMatch);
        Assert.IsType<DelegateDispatcher>(healthResult.Dispatcher);

        var orderResult = frozen.Match(HttpMethod.Get, "/orders/42");
        Assert.True(orderResult.IsMatch);
        Assert.IsType<EntityDispatcher>(orderResult.Dispatcher);
    }

    [Fact(Timeout = 5000)]
    public void Builder_should_accept_custom_resolver()
    {
        var builder = new TurboEntityBuilder("/orders/{id}");
        builder.OnGet(() => new TestMessage("get"));
        builder.UseActorRef<TestActorKey>();

        var table = new TurboRouteTable();
        builder.AddToRouteTable(table);
        var frozen = table.Freeze();

        Assert.True(frozen.Match(HttpMethod.Get, "/orders/1").IsMatch);
    }
}