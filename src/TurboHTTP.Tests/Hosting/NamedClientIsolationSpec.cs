using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TurboHTTP.Protocol.Cookies;

namespace TurboHTTP.Tests.Hosting;

public sealed class NamedClientIsolationSpec
{
    private static TurboClientDescriptor GetDescriptor(IServiceCollection services, string name)
    {
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IOptionsMonitor<TurboClientDescriptor>>().Get(name);
    }

    [Fact(Timeout = 5000)]
    public void NamedClientIsolation_should_have_independent_descriptor_instances()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("a");
        services.AddTurboHttpClient("b");

        var sp = services.BuildServiceProvider();
        var monitor = sp.GetRequiredService<IOptionsMonitor<TurboClientDescriptor>>();

        var descriptorA = monitor.Get("a");
        var descriptorB = monitor.Get("b");

        Assert.NotSame(descriptorA, descriptorB);
    }

    [Fact(Timeout = 5000)]
    public void NamedClientIsolation_should_have_separate_jar_instances()
    {
        var jarA = new CookieJar();
        var jarB = new CookieJar();

        var services = new ServiceCollection();
        services.AddTurboHttpClient("a").WithCookies(jarA);
        services.AddTurboHttpClient("b").WithCookies(jarB);

        var descriptorA = GetDescriptor(services, "a");
        var descriptorB = GetDescriptor(services, "b");

        Assert.Same(jarA, descriptorA.CustomCookieJar);
        Assert.Same(jarB, descriptorB.CustomCookieJar);
        Assert.NotSame(descriptorA.CustomCookieJar, descriptorB.CustomCookieJar);
    }

    [Fact(Timeout = 5000)]
    public void NamedClientIsolation_should_not_affect_other_clients_when_configuring_cookies()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("a").WithCookies();
        services.AddTurboHttpClient("b");

        var descriptorA = GetDescriptor(services, "a");
        var descriptorB = GetDescriptor(services, "b");

        Assert.True(descriptorA.EnableCookies);
        Assert.False(descriptorB.EnableCookies);
        Assert.Null(descriptorB.CustomCookieJar);
    }

    [Fact(Timeout = 5000)]
    public void NamedClientIsolation_should_be_independent_when_mixed_configuration()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("a").WithCookies();
        services.AddTurboHttpClient("b");

        var descriptorA = GetDescriptor(services, "a");
        var descriptorB = GetDescriptor(services, "b");

        // "a" has cookies enabled
        Assert.True(descriptorA.EnableCookies);

        // "b" has no cookies and is otherwise unaffected
        Assert.False(descriptorB.EnableCookies);
        Assert.Null(descriptorB.CustomCookieJar);
        Assert.Empty(descriptorB.HandlerFactories);
    }
}