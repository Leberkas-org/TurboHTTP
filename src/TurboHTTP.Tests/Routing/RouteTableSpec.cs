using TurboHTTP.Routing;

namespace TurboHTTP.Tests.Routing;

public sealed class RouteTableSpec
{
    private static IRouteDispatcher Dummy() => new DelegateDispatcher(_ => Task.CompletedTask);

    [Fact(Timeout = 5000)]
    public void Match_should_find_exact_static_route()
    {
        var table = new RouteTableBuilder()
            .Add("GET", "/api/health", Dummy())
            .Build();

        var result = table.Match("GET", "/api/health");
        Assert.True(result.IsMatch);
    }

    [Fact(Timeout = 5000)]
    public void Match_should_return_no_match_for_unknown_path()
    {
        var table = new RouteTableBuilder()
            .Add("GET", "/api/health", Dummy())
            .Build();

        var result = table.Match("GET", "/api/unknown");
        Assert.False(result.IsMatch);
    }

    [Fact(Timeout = 5000)]
    public void Match_should_extract_route_parameters()
    {
        var table = new RouteTableBuilder()
            .Add("GET", "/api/orders/{id}", Dummy())
            .Build();

        var result = table.Match("GET", "/api/orders/42");
        Assert.True(result.IsMatch);
        Assert.Equal("42", result.RouteValues["id"]);
    }

    [Fact(Timeout = 5000)]
    public void Match_should_extract_multiple_parameters()
    {
        var table = new RouteTableBuilder()
            .Add("GET", "/api/{controller}/{id}", Dummy())
            .Build();

        var result = table.Match("GET", "/api/orders/42");
        Assert.True(result.IsMatch);
        Assert.Equal("orders", result.RouteValues["controller"]);
        Assert.Equal("42", result.RouteValues["id"]);
    }

    [Fact(Timeout = 5000)]
    public void Match_should_respect_http_method()
    {
        var table = new RouteTableBuilder()
            .Add("POST", "/api/orders", Dummy())
            .Build();

        var result = table.Match("GET", "/api/orders");
        Assert.False(result.IsMatch);
    }

    [Fact(Timeout = 5000)]
    public void Match_should_support_wildcard_method()
    {
        var table = new RouteTableBuilder()
            .Add("*", "/api/health", Dummy())
            .Build();

        Assert.True(table.Match("GET", "/api/health").IsMatch);
        Assert.True(table.Match("POST", "/api/health").IsMatch);
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
            .Add("GET", "/api/orders/latest", staticDispatcher)
            .Add("GET", "/api/orders/{id}", paramDispatcher)
            .Build();

        var result = table.Match("GET", "/api/orders/latest");
        Assert.True(result.IsMatch);
        Assert.Same(staticDispatcher, result.Dispatcher);
    }
}
