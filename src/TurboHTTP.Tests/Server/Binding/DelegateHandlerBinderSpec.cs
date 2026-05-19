using System.Net;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Server;
using TurboHTTP.Server.Binding;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Tests.Server.Binding;

public sealed class DelegateHandlerBinderSpec
{
    [Fact(Timeout = 5000)]
    public async Task Bind_should_handle_no_params_IResult_return()
    {
        Delegate handler = () => TypedResults.Ok("hello");
        var bound = DelegateHandlerBinder.Bind("/test", handler);
        var ctx = CreateContext("/test");
        await bound(ctx, CreateServiceProvider());
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Bind_should_inject_route_value()
    {
        Delegate handler = (string id) => TypedResults.Ok(string.Concat("order-", id));
        var bound = DelegateHandlerBinder.Bind("/orders/{id}", handler);
        var ctx = CreateContext("/orders/42");
        ctx.Request.RouteValues["id"] = "42";
        await bound(ctx, CreateServiceProvider());
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Bind_should_inject_route_value_as_int()
    {
        Delegate handler = (int id) => TypedResults.Ok(string.Concat("order-", id));
        var bound = DelegateHandlerBinder.Bind("/orders/{id}", handler);
        var ctx = CreateContext("/orders/42");
        ctx.Request.RouteValues["id"] = "42";
        await bound(ctx, CreateServiceProvider());
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Bind_should_inject_di_service()
    {
        Delegate handler = (ITestService svc) => TypedResults.Ok(svc.GetValue());
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITestService>(new TestService("injected"));
        var provider = services.BuildServiceProvider();
        var bound = DelegateHandlerBinder.Bind("/test", handler);
        var ctx = CreateContext("/test");
        await bound(ctx, provider);
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Bind_should_inject_turbo_context()
    {
        Delegate handler = (TurboHttpContext ctx) => TypedResults.Ok(ctx.Request.Method);
        var bound = DelegateHandlerBinder.Bind("/test", handler);
        var ctx = CreateContext("/test");
        await bound(ctx, CreateServiceProvider());
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Bind_should_handle_async_handler()
    {
        Delegate handler = async (string id) =>
        {
            await Task.Delay(1);
            return TypedResults.Ok(string.Concat("async-", id));
        };
        var bound = DelegateHandlerBinder.Bind("/items/{id}", handler);
        var ctx = CreateContext("/items/7");
        ctx.Request.RouteValues["id"] = "7";
        await bound(ctx, CreateServiceProvider());
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Bind_should_inject_http_context_base_type()
    {
        Delegate handler = (HttpContext ctx) => TypedResults.Ok(ctx.Request.Method);
        var bound = DelegateHandlerBinder.Bind("/test", handler);
        var ctx = CreateContext("/test");
        await bound(ctx, CreateServiceProvider());
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public void Bind_should_reject_non_IResult_return()
    {
        Delegate handler = () => "not IResult";
        Assert.Throws<InvalidOperationException>(() => DelegateHandlerBinder.Bind("/test", handler));
    }

    [Fact(Timeout = 5000)]
    public void Bind_should_reject_HttpResponseMessage_return()
    {
        Delegate handler = () => new HttpResponseMessage(HttpStatusCode.Accepted);
        Assert.Throws<InvalidOperationException>(() => DelegateHandlerBinder.Bind("/test", handler));
    }

    [Fact(Timeout = 5000)]
    public void Bind_should_reject_void_return()
    {
        Delegate handler = () => { };
        Assert.Throws<InvalidOperationException>(() => DelegateHandlerBinder.Bind("/test", handler));
    }

    private interface ITestService
    {
        string GetValue();
    }

    private sealed class TestService : ITestService
    {
        private readonly string _value;

        public TestService(string value)
        {
            _value = value;
        }

        public string GetValue() => _value;
    }

    private static IServiceProvider CreateServiceProvider()
    {
        return new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
    }

    private static TurboHttpContext CreateContext(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost" + path);
        var connection = new TurboConnectionInfo("test", null, 0, null, 0);

        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature(request, Source.Empty<ReadOnlyMemory<byte>>()));
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature());
        features.Set<IHttpConnectionFeature>(new TurboHttpConnectionFeature(connection));
        features.Set<IHttpResponseBodyFeature>(new TurboHttpResponseBodyFeature());

        var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        return new TurboHttpContext(features, connection, services, CancellationToken.None);
    }
}