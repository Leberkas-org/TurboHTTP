using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TurboHttp.Tests.Hosting;

public sealed class TurboHttpClientBuilderMiddlewareTests
{
    // ---------------------------------------------------------------------------
    // Test doubles
    // ---------------------------------------------------------------------------

    private sealed class TestMiddleware : TurboHandler { }

    private sealed class AlphaMiddleware : TurboHandler { }

    private sealed class BetaMiddleware : TurboHandler { }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static TurboClientDescriptor GetDescriptor(IServiceCollection services, string name)
    {
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IOptionsMonitor<TurboClientDescriptor>>().Get(name);
    }

    // ---------------------------------------------------------------------------
    // AddMiddleware<T> — type registration
    // ---------------------------------------------------------------------------

    [Fact(DisplayName = "AddMiddleware<T>() adds typeof(T) to HandlerTypes")]
    public void AddMiddleware_AddsTypeToHandlerTypes()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").AddMiddleware<TestMiddleware>();

        var descriptor = GetDescriptor(services, "test");

        Assert.Contains(typeof(TestMiddleware), descriptor.HandlerTypes);
    }

    [Fact(DisplayName = "AddMiddleware<T>() also appends one factory to HandlerFactories")]
    public void AddMiddleware_AddsFactoryToHandlerFactories()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").AddMiddleware<TestMiddleware>();

        var descriptor = GetDescriptor(services, "test");

        Assert.Single(descriptor.HandlerFactories);
    }

    // ---------------------------------------------------------------------------
    // AddMiddleware<T> — DI registration lifetime
    // ---------------------------------------------------------------------------

    [Fact(DisplayName = "AddMiddleware<T>() registers T as Transient in the service collection")]
    public void AddMiddleware_RegistersTransientService()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").AddMiddleware<TestMiddleware>();

        Assert.Contains(services, sd =>
            sd.ServiceType == typeof(TestMiddleware) &&
            sd.Lifetime == ServiceLifetime.Transient);
    }

    // ---------------------------------------------------------------------------
    // UseRequest — anonymous middleware
    // ---------------------------------------------------------------------------

    [Fact(DisplayName = "UseRequest() adds one factory to HandlerFactories without touching HandlerTypes")]
    public void UseRequest_AddsOneFactoryWithNoTypeEntry()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").UseRequest(
            (req, ct) => ValueTask.FromResult(req));

        var descriptor = GetDescriptor(services, "test");

        Assert.Single(descriptor.HandlerFactories);
        Assert.Empty(descriptor.HandlerTypes);
    }

    // ---------------------------------------------------------------------------
    // FIFO ordering
    // ---------------------------------------------------------------------------

    [Fact(DisplayName = "Multiple AddMiddleware<T>() calls preserve FIFO order in HandlerTypes")]
    public void AddMiddleware_PreservesFifoOrderInTypes()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test")
            .AddMiddleware<AlphaMiddleware>()
            .AddMiddleware<BetaMiddleware>();

        var descriptor = GetDescriptor(services, "test");

        Assert.Equal(typeof(AlphaMiddleware), descriptor.HandlerTypes[0]);
        Assert.Equal(typeof(BetaMiddleware), descriptor.HandlerTypes[1]);
    }

    [Fact(DisplayName = "Multiple AddMiddleware<T>() calls preserve FIFO order in HandlerFactories")]
    public void AddMiddleware_PreservesFifoOrderInFactories()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test")
            .AddMiddleware<AlphaMiddleware>()
            .AddMiddleware<BetaMiddleware>();

        var sp = services.BuildServiceProvider();
        var descriptor = sp.GetRequiredService<IOptionsMonitor<TurboClientDescriptor>>().Get("test");

        Assert.Equal(2, descriptor.HandlerFactories.Count);
        Assert.IsType<AlphaMiddleware>(descriptor.HandlerFactories[0](sp));
        Assert.IsType<BetaMiddleware>(descriptor.HandlerFactories[1](sp));
    }

    // ---------------------------------------------------------------------------
    // Factory DI resolution
    // ---------------------------------------------------------------------------

    [Fact(DisplayName = "AddMiddleware<T>() factory resolves T from a real IServiceProvider")]
    public void AddMiddleware_FactoryResolvesFromServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").AddMiddleware<TestMiddleware>();

        var sp = services.BuildServiceProvider();
        var descriptor = sp.GetRequiredService<IOptionsMonitor<TurboClientDescriptor>>().Get("test");

        var resolved = descriptor.HandlerFactories[0](sp);

        Assert.IsType<TestMiddleware>(resolved);
    }
}
