using System.Net;
using Akka.Streams.Dsl;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Server;
using TurboHTTP.Server.Binding;

namespace TurboHTTP.Tests.Server.Binding;

public sealed class DelegateHandlerBinderSpec
{
    [Fact(Timeout = 5000)]
    public async Task Bind_should_handle_no_params_string_return()
    {
        Delegate handler = () => "hello";
        var bound = DelegateHandlerBinder.Bind("/test", handler);
        var ctx = CreateContext("/test");
        var result = await bound(ctx, new ServiceCollection().BuildServiceProvider());
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("hello", await result.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    public async Task Bind_should_inject_route_value()
    {
        Delegate handler = (string id) => $"order-{id}";
        var bound = DelegateHandlerBinder.Bind("/orders/{id}", handler);
        var ctx = CreateContext("/orders/42");
        ctx.RouteValues["id"] = "42";
        var result = await bound(ctx, new ServiceCollection().BuildServiceProvider());
        Assert.Equal("order-42", await result.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    public async Task Bind_should_inject_route_value_as_int()
    {
        Delegate handler = (int id) => $"order-{id}";
        var bound = DelegateHandlerBinder.Bind("/orders/{id}", handler);
        var ctx = CreateContext("/orders/42");
        ctx.RouteValues["id"] = "42";
        var result = await bound(ctx, new ServiceCollection().BuildServiceProvider());
        Assert.Equal("order-42", await result.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    public async Task Bind_should_inject_di_service()
    {
        Delegate handler = (ITestService svc) => svc.GetValue();
        var services = new ServiceCollection();
        services.AddSingleton<ITestService>(new TestService("injected"));
        var provider = services.BuildServiceProvider();
        var bound = DelegateHandlerBinder.Bind("/test", handler);
        var ctx = CreateContext("/test");
        var result = await bound(ctx, provider);
        Assert.Equal("injected", await result.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    public async Task Bind_should_inject_turbo_context()
    {
        Delegate handler = (TurboHttpContext ctx) => ctx.Request.Method.Method;
        var bound = DelegateHandlerBinder.Bind("/test", handler);
        var ctx = CreateContext("/test");
        var result = await bound(ctx, new ServiceCollection().BuildServiceProvider());
        Assert.Equal("GET", await result.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    public async Task Bind_should_handle_async_handler()
    {
        Delegate handler = async (string id) =>
        {
            await Task.Delay(1);
            return $"async-{id}";
        };
        var bound = DelegateHandlerBinder.Bind("/items/{id}", handler);
        var ctx = CreateContext("/items/7");
        ctx.RouteValues["id"] = "7";
        var result = await bound(ctx, new ServiceCollection().BuildServiceProvider());
        Assert.Equal("async-7", await result.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    public async Task Bind_should_handle_void_return()
    {
        var called = false;
        Delegate handler = () => { called = true; };
        var bound = DelegateHandlerBinder.Bind("/test", handler);
        var ctx = CreateContext("/test");
        var result = await bound(ctx, new ServiceCollection().BuildServiceProvider());
        Assert.True(called);
        Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Bind_should_passthrough_HttpResponseMessage()
    {
        Delegate handler = () => new HttpResponseMessage(HttpStatusCode.Accepted);
        var bound = DelegateHandlerBinder.Bind("/test", handler);
        var ctx = CreateContext("/test");
        var result = await bound(ctx, new ServiceCollection().BuildServiceProvider());
        Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);
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

    private static TurboHttpContext CreateContext(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost" + path);
        var connection = new TurboConnectionInfo("test", null, 0, null, 0);
        return new TurboHttpContext(request, connection, Source.Empty<ReadOnlyMemory<byte>>(), CancellationToken.None);
    }
}