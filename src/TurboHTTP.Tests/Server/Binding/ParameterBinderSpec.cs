using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Server;
using TurboHTTP.Routing.Binding;

namespace TurboHTTP.Tests.Server.Binding;

public sealed class ParameterBinderSpec
{
    [Fact(Timeout = 5000)]
    public async Task ContextBinder_should_return_turbo_context()
    {
        var ctx = CreateContext("/test", TestContext.Current.CancellationToken);
        var binder = new ContextBinder();
        var result = await binder.BindAsync(ctx, null!);
        Assert.Same(ctx, result);
    }

    [Fact(Timeout = 5000)]
    public async Task CancellationTokenBinder_should_return_request_aborted()
    {
        var cts = new CancellationTokenSource();
        var ctx = CreateContext("/test", cancellationToken: cts.Token);
        var binder = new CancellationTokenBinder();
        var result = await binder.BindAsync(ctx, null!);
        Assert.Equal(cts.Token, result);
    }

    [Fact(Timeout = 5000)]
    public async Task RouteValueBinder_should_extract_string()
    {
        var ctx = CreateContext("/orders/42", TestContext.Current.CancellationToken);
        ctx.Request.RouteValues["id"] = "42";
        var binder = new RouteValueBinder("id", typeof(string));
        var result = await binder.BindAsync(ctx, null!);
        Assert.Equal("42", result);
    }

    [Fact(Timeout = 5000)]
    public async Task RouteValueBinder_should_parse_int()
    {
        var ctx = CreateContext("/orders/42", TestContext.Current.CancellationToken);
        ctx.Request.RouteValues["id"] = "42";
        var binder = new RouteValueBinder("id", typeof(int));
        var result = await binder.BindAsync(ctx, null!);
        Assert.Equal(42, result);
    }

    [Fact(Timeout = 5000)]
    public async Task RouteValueBinder_should_parse_guid()
    {
        var guid = Guid.NewGuid();
        var ctx = CreateContext("/items/" + guid, TestContext.Current.CancellationToken);
        ctx.Request.RouteValues["id"] = guid.ToString();
        var binder = new RouteValueBinder("id", typeof(Guid));
        var result = await binder.BindAsync(ctx, null!);
        Assert.Equal(guid, result);
    }

    [Fact(Timeout = 5000)]
    public async Task ServiceBinder_should_resolve_from_di()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITestService>(new TestService());
        var provider = services.BuildServiceProvider();
        var ctx = CreateContext("/test", TestContext.Current.CancellationToken);
        var binder = new ServiceBinder(typeof(ITestService));
        var result = await binder.BindAsync(ctx, provider);
        Assert.IsType<TestService>(result);
    }

    [Fact(Timeout = 5000)]
    public async Task QueryStringBinder_should_extract_string()
    {
        var ctx = CreateContext("/search?q=hello", TestContext.Current.CancellationToken);
        var binder = new QueryStringBinder("q", typeof(string));
        var result = await binder.BindAsync(ctx, null!);
        Assert.Equal("hello", result);
    }

    [Fact(Timeout = 5000)]
    public async Task QueryStringBinder_should_parse_int()
    {
        var ctx = CreateContext("/items?page=3", TestContext.Current.CancellationToken);
        var binder = new QueryStringBinder("page", typeof(int));
        var result = await binder.BindAsync(ctx, null!);
        Assert.Equal(3, result);
    }

    [Fact(Timeout = 5000)]
    public async Task HeaderBinder_should_extract_string()
    {
        var ctx = CreateContext("/test", TestContext.Current.CancellationToken);
        ctx.Request.Headers["X-Tenant"] = "acme";
        var binder = new HeaderBinder("X-Tenant", typeof(string));
        var result = await binder.BindAsync(ctx, null!);
        Assert.Equal("acme", result);
    }

    [Fact(Timeout = 5000)]
    public async Task HeaderBinder_should_parse_int()
    {
        var ctx = CreateContext("/test", TestContext.Current.CancellationToken);
        ctx.Request.Headers["X-Page-Size"] = "50";
        var binder = new HeaderBinder("X-Page-Size", typeof(int));
        var result = await binder.BindAsync(ctx, null!);
        Assert.Equal(50, result);
    }

    [Fact(Timeout = 5000)]
    public async Task HeaderBinder_should_return_null_when_header_missing()
    {
        var ctx = CreateContext("/test", TestContext.Current.CancellationToken);
        var binder = new HeaderBinder("X-Missing", typeof(string));
        var result = await binder.BindAsync(ctx, null!);
        Assert.Null(result);
    }

    [Fact(Timeout = 5000)]
    public async Task HeaderBinder_should_return_default_for_value_type_when_missing()
    {
        var ctx = CreateContext("/test", TestContext.Current.CancellationToken);
        var binder = new HeaderBinder("X-Missing", typeof(int));
        var result = await binder.BindAsync(ctx, null!);
        Assert.Equal(0, result);
    }

    private interface ITestService;

    private sealed class TestService : ITestService;

    private static TurboHttpContext CreateContext(string path, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost" + path);
        var connection = new TurboConnectionInfo("test", null, 0, null, 0);
        return TestContextFactory.Create(request: request, connection: connection,
            cancellationToken: cancellationToken);
    }
}
