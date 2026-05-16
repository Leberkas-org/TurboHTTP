using Akka.Streams.Dsl;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Server;
using TurboHTTP.Server.Binding;

namespace TurboHTTP.Tests.Server.Binding;

public sealed class ParameterBinderSpec
{
    [Fact(Timeout = 5000)]
    public void ContextBinder_should_return_turbo_context()
    {
        var ctx = CreateContext("/test", TestContext.Current.CancellationToken);
        var binder = new ContextBinder();
        var result = binder.Bind(ctx, null!);
        Assert.Same(ctx, result);
    }

    [Fact(Timeout = 5000)]
    public void CancellationTokenBinder_should_return_request_aborted()
    {
        var cts = new CancellationTokenSource();
        var ctx = CreateContext("/test", cancellationToken: cts.Token);
        var binder = new CancellationTokenBinder();
        var result = binder.Bind(ctx, null!);
        Assert.Equal(cts.Token, result);
    }

    [Fact(Timeout = 5000)]
    public void RequestBinder_should_return_http_request()
    {
        var ctx = CreateContext("/test", TestContext.Current.CancellationToken);
        var binder = new RequestBinder();
        var result = binder.Bind(ctx, null!);
        Assert.Same(ctx.Request, result);
    }

    [Fact(Timeout = 5000)]
    public void RouteValueBinder_should_extract_string()
    {
        var ctx = CreateContext("/orders/42", TestContext.Current.CancellationToken);
        ctx.RouteValues["id"] = "42";
        var binder = new RouteValueBinder("id", typeof(string));
        var result = binder.Bind(ctx, null!);
        Assert.Equal("42", result);
    }

    [Fact(Timeout = 5000)]
    public void RouteValueBinder_should_parse_int()
    {
        var ctx = CreateContext("/orders/42", TestContext.Current.CancellationToken);
        ctx.RouteValues["id"] = "42";
        var binder = new RouteValueBinder("id", typeof(int));
        var result = binder.Bind(ctx, null!);
        Assert.Equal(42, result);
    }

    [Fact(Timeout = 5000)]
    public void RouteValueBinder_should_parse_guid()
    {
        var guid = Guid.NewGuid();
        var ctx = CreateContext("/items/" + guid, TestContext.Current.CancellationToken);
        ctx.RouteValues["id"] = guid.ToString();
        var binder = new RouteValueBinder("id", typeof(Guid));
        var result = binder.Bind(ctx, null!);
        Assert.Equal(guid, result);
    }

    [Fact(Timeout = 5000)]
    public void ServiceBinder_should_resolve_from_di()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITestService>(new TestService());
        var provider = services.BuildServiceProvider();
        var ctx = CreateContext("/test", TestContext.Current.CancellationToken);
        var binder = new ServiceBinder(typeof(ITestService));
        var result = binder.Bind(ctx, provider);
        Assert.IsType<TestService>(result);
    }

    [Fact(Timeout = 5000)]
    public void QueryStringBinder_should_extract_string()
    {
        var ctx = CreateContext("/search?q=hello", TestContext.Current.CancellationToken);
        var binder = new QueryStringBinder("q", typeof(string));
        var result = binder.Bind(ctx, null!);
        Assert.Equal("hello", result);
    }

    [Fact(Timeout = 5000)]
    public void QueryStringBinder_should_parse_int()
    {
        var ctx = CreateContext("/items?page=3", TestContext.Current.CancellationToken);
        var binder = new QueryStringBinder("page", typeof(int));
        var result = binder.Bind(ctx, null!);
        Assert.Equal(3, result);
    }

    private interface ITestService;

    private sealed class TestService : ITestService;

    private static TurboHttpContext CreateContext(string path, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost" + path);
        var connection = new TurboConnectionInfo("test", null, 0, null, 0);
        return new TurboHttpContext(request, connection, Source.Empty<ReadOnlyMemory<byte>>(), cancellationToken);
    }
}