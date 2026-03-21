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

    [Fact(DisplayName = "AddMiddleware<T>() adds typeof(T) to MiddlewareTypes")]
    public void AddMiddleware_AddsTypeToMiddlewareTypes()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").AddMiddleware<TestMiddleware>();

        var descriptor = GetDescriptor(services, "test");

        Assert.Contains(typeof(TestMiddleware), descriptor.MiddlewareTypes);
    }

    [Fact(DisplayName = "AddMiddleware<T>() also appends one factory to MiddlewareFactories")]
    public void AddMiddleware_AddsFactoryToMiddlewareFactories()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").AddMiddleware<TestMiddleware>();

        var descriptor = GetDescriptor(services, "test");

        Assert.Single(descriptor.MiddlewareFactories);
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

    [Fact(DisplayName = "UseRequest() adds one factory to MiddlewareFactories without touching MiddlewareTypes")]
    public void UseRequest_AddsOneFactoryWithNoTypeEntry()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").UseRequest(
            (req, ct) => ValueTask.FromResult(req));

        var descriptor = GetDescriptor(services, "test");

        Assert.Single(descriptor.MiddlewareFactories);
        Assert.Empty(descriptor.MiddlewareTypes);
    }

    // ---------------------------------------------------------------------------
    // FIFO ordering
    // ---------------------------------------------------------------------------

    [Fact(DisplayName = "Multiple AddMiddleware<T>() calls preserve FIFO order in MiddlewareTypes")]
    public void AddMiddleware_PreservesFifoOrderInTypes()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test")
            .AddMiddleware<AlphaMiddleware>()
            .AddMiddleware<BetaMiddleware>();

        var descriptor = GetDescriptor(services, "test");

        Assert.Equal(typeof(AlphaMiddleware), descriptor.MiddlewareTypes[0]);
        Assert.Equal(typeof(BetaMiddleware), descriptor.MiddlewareTypes[1]);
    }

    [Fact(DisplayName = "Multiple AddMiddleware<T>() calls preserve FIFO order in MiddlewareFactories")]
    public void AddMiddleware_PreservesFifoOrderInFactories()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test")
            .AddMiddleware<AlphaMiddleware>()
            .AddMiddleware<BetaMiddleware>();

        var sp = services.BuildServiceProvider();
        var descriptor = sp.GetRequiredService<IOptionsMonitor<TurboClientDescriptor>>().Get("test");

        Assert.Equal(2, descriptor.MiddlewareFactories.Count);
        Assert.IsType<AlphaMiddleware>(descriptor.MiddlewareFactories[0](sp));
        Assert.IsType<BetaMiddleware>(descriptor.MiddlewareFactories[1](sp));
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

        var resolved = descriptor.MiddlewareFactories[0](sp);

        Assert.IsType<TestMiddleware>(resolved);
    }
}
