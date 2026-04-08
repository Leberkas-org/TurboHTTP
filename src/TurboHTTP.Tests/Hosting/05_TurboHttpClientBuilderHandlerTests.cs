using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TurboHTTP.Tests.Hosting;

public sealed class TurboHttpClientBuilderHandlerTests
{
    // Test doubles

    private sealed class TestHandler : TurboHandler { }

    private sealed class AlphaHandler : TurboHandler { }

    private sealed class BetaHandler : TurboHandler { }

    // Helpers

    private static TurboClientDescriptor GetDescriptor(IServiceCollection services, string name)
    {
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IOptionsMonitor<TurboClientDescriptor>>().Get(name);
    }

    // AddHandler<T> — type registration

    [Fact(DisplayName = "AddHandler<T>() adds typeof(T) to HandlerTypes")]
    public void AddHandler_AddsTypeToHandlerTypes()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").AddHandler<TestHandler>();

        var descriptor = GetDescriptor(services, "test");

        Assert.Contains(typeof(TestHandler), descriptor.HandlerTypes);
    }

    [Fact(DisplayName = "AddHandler<T>() also appends one factory to HandlerFactories")]
    public void AddHandler_AddsFactoryToHandlerFactories()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").AddHandler<TestHandler>();

        var descriptor = GetDescriptor(services, "test");

        Assert.Single(descriptor.HandlerFactories);
    }

    // AddHandler<T> — DI registration lifetime

    [Fact(DisplayName = "AddHandler<T>() registers T as Transient in the service collection")]
    public void AddHandler_RegistersTransientService()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").AddHandler<TestHandler>();

        Assert.Contains(services, sd =>
            sd.ServiceType == typeof(TestHandler) &&
            sd.Lifetime == ServiceLifetime.Transient);
    }

    // UseRequest — anonymous handler

    [Fact(DisplayName = "UseRequest() adds one factory to HandlerFactories without touching HandlerTypes")]
    public void UseRequest_AddsOneFactoryWithNoTypeEntry()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").UseRequest(req => req);

        var descriptor = GetDescriptor(services, "test");

        Assert.Single(descriptor.HandlerFactories);
        Assert.Empty(descriptor.HandlerTypes);
    }

    // FIFO ordering

    [Fact(DisplayName = "Multiple AddHandler<T>() calls preserve FIFO order in HandlerTypes")]
    public void AddHandler_PreservesFifoOrderInTypes()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test")
            .AddHandler<AlphaHandler>()
            .AddHandler<BetaHandler>();

        var descriptor = GetDescriptor(services, "test");

        Assert.Equal(typeof(AlphaHandler), descriptor.HandlerTypes[0]);
        Assert.Equal(typeof(BetaHandler), descriptor.HandlerTypes[1]);
    }

    [Fact(DisplayName = "Multiple AddHandler<T>() calls preserve FIFO order in HandlerFactories")]
    public void AddHandler_PreservesFifoOrderInFactories()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test")
            .AddHandler<AlphaHandler>()
            .AddHandler<BetaHandler>();

        var sp = services.BuildServiceProvider();
        var descriptor = sp.GetRequiredService<IOptionsMonitor<TurboClientDescriptor>>().Get("test");

        Assert.Equal(2, descriptor.HandlerFactories.Count);
        Assert.IsType<AlphaHandler>(descriptor.HandlerFactories[0](sp));
        Assert.IsType<BetaHandler>(descriptor.HandlerFactories[1](sp));
    }

    // Factory DI resolution

    [Fact(DisplayName = "AddHandler<T>() factory resolves T from a real IServiceProvider")]
    public void AddHandler_FactoryResolvesFromServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").AddHandler<TestHandler>();

        var sp = services.BuildServiceProvider();
        var descriptor = sp.GetRequiredService<IOptionsMonitor<TurboClientDescriptor>>().Get("test");

        var resolved = descriptor.HandlerFactories[0](sp);

        Assert.IsType<TestHandler>(resolved);
    }
}
