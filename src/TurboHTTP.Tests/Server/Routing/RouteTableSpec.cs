using TurboHTTP.Server.Routing;

namespace TurboHTTP.Tests.Server.Routing;

public sealed class RouteTableSpec
{
    private static IRouteDispatcher Dummy() => new DelegateDispatcher(_ => Task.CompletedTask);

    [Fact(Timeout = 5000)]
    public void Match_should_find_exact_static_route()
    {
        var table = new RouteTableBuilder()
            .Add(HttpMethod.Get, "/api/health", Dummy())
            .Build();

        var result = table.Match(HttpMethod.Get, "/api/health");
        Assert.True(result.IsMatch);
    }

    [Fact(Timeout = 5000)]
    public void Match_should_return_no_match_for_unknown_path()
    {
        var table = new RouteTableBuilder()
            .Add(HttpMethod.Get, "/api/health", Dummy())
            .Build();

        var result = table.Match(HttpMethod.Get, "/api/unknown");
        Assert.False(result.IsMatch);
    }

    [Fact(Timeout = 5000)]
    public void Match_should_extract_route_parameters()
    {
        var table = new RouteTableBuilder()
            .Add(HttpMethod.Get, "/api/orders/{id}", Dummy())
            .Build();

        var result = table.Match(HttpMethod.Get, "/api/orders/42");
        Assert.True(result.IsMatch);
        Assert.Equal("42", result.RouteValues["id"]);
    }

    [Fact(Timeout = 5000)]
    public void Match_should_extract_multiple_parameters()
    {
        var table = new RouteTableBuilder()
            .Add(HttpMethod.Get, "/api/{controller}/{id}", Dummy())
            .Build();

        var result = table.Match(HttpMethod.Get, "/api/orders/42");
        Assert.True(result.IsMatch);
        Assert.Equal("orders", result.RouteValues["controller"]);
        Assert.Equal("42", result.RouteValues["id"]);
    }

    [Fact(Timeout = 5000)]
    public void Match_should_respect_http_method()
    {
        var table = new RouteTableBuilder()
            .Add(HttpMethod.Post, "/api/orders", Dummy())
            .Build();

        var result = table.Match(HttpMethod.Get, "/api/orders");
        Assert.False(result.IsMatch);
    }

    [Fact(Timeout = 5000)]
    public void Match_should_support_wildcard_method()
    {
        var table = new RouteTableBuilder()
            .Add(new HttpMethod("*"), "/api/health", Dummy())
            .Build();

        Assert.True(table.Match(HttpMethod.Get, "/api/health").IsMatch);
        Assert.True(table.Match(HttpMethod.Post, "/api/health").IsMatch);
    }

    [Fact(Timeout = 5000)]
    public void Match_should_prefer_static_over_parameterized()
    {
        var staticDispatcher = new DelegateDispatcher(_ =>
        {
            // _ would be TurboHttpContext, setting StatusCode there
            return Task.CompletedTask;
        });
        var paramDispatcher = new DelegateDispatcher(_ =>
        {
            return Task.CompletedTask;
        });

        var table = new RouteTableBuilder()
            .Add(HttpMethod.Get, "/api/orders/latest", staticDispatcher)
            .Add(HttpMethod.Get, "/api/orders/{id}", paramDispatcher)
            .Build();

        var result = table.Match(HttpMethod.Get, "/api/orders/latest");
        Assert.True(result.IsMatch);
        Assert.Same(staticDispatcher, result.Dispatcher);
    }
}
