using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TurboHTTP.Client;

namespace TurboHTTP.Tests.Client.Hosting;

public sealed class TurboHttpClientBuilderHandlerSpec
{
    private sealed class TestHandler : TurboHandler;

    private sealed class AlphaHandler : TurboHandler;

    private sealed class BetaHandler : TurboHandler;

    private static TurboClientDescriptor GetDescriptor(IServiceCollection services, string name)
    {
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IOptionsMonitor<TurboClientDescriptor>>().Get(name);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpClientBuilderHandler_should_add_type_to_handler_types()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").AddHandler<TestHandler>();

        var descriptor = GetDescriptor(services, "test");

        Assert.Contains(typeof(TestHandler), descriptor.HandlerTypes);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpClientBuilderHandler_should_add_factory_to_handler_factories()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").AddHandler<TestHandler>();

        var descriptor = GetDescriptor(services, "test");

        Assert.Single(descriptor.HandlerFactories);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpClientBuilderHandler_should_register_transient_service()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").AddHandler<TestHandler>();

        Assert.Contains(services, sd =>
            sd.ServiceType == typeof(TestHandler) &&
            sd.Lifetime == ServiceLifetime.Transient);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpClientBuilderHandler_should_add_one_factory_with_no_type_entry()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").UseRequest(req => req);

        var descriptor = GetDescriptor(services, "test");

        Assert.Single(descriptor.HandlerFactories);
        Assert.Empty(descriptor.HandlerTypes);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpClientBuilderHandler_should_preserve_fifo_order_in_types()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test")
            .AddHandler<AlphaHandler>()
            .AddHandler<BetaHandler>();

        var descriptor = GetDescriptor(services, "test");

        Assert.Equal(typeof(AlphaHandler), descriptor.HandlerTypes[0]);
        Assert.Equal(typeof(BetaHandler), descriptor.HandlerTypes[1]);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpClientBuilderHandler_should_preserve_fifo_order_in_factories()
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

    [Fact(Timeout = 5000)]
    public void TurboHttpClientBuilderHandler_should_resolve_from_service_provider()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").AddHandler<TestHandler>();

        var sp = services.BuildServiceProvider();
        var descriptor = sp.GetRequiredService<IOptionsMonitor<TurboClientDescriptor>>().Get("test");

        var resolved = descriptor.HandlerFactories[0](sp);

        Assert.IsType<TestHandler>(resolved);
    }
}