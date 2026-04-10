using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TurboHTTP.Protocol.Cookies;

namespace TurboHTTP.Tests.Hosting;

public sealed class NamedClientIsolationTests
{
    // Helpers

    private static TurboClientDescriptor GetDescriptor(IServiceCollection services, string name)
    {
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IOptionsMonitor<TurboClientDescriptor>>().Get(name);
    }

    // Distinct descriptor instances per named client

    [Fact(DisplayName = "Two named clients receive independent TurboClientDescriptor instances")]
    public void TwoNamedClients_HaveIndependentDescriptorInstances()
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

    // Cookie jar isolation — separate CookieJar per named client

    [Fact(DisplayName = "Each named client with WithCookies(jar) gets its own CookieJar instance")]
    public void TwoClientsWithCustomCookieJar_HaveSeparateJarInstances()
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

    [Fact(DisplayName = "Configuring cookies on client 'a' does not set EnableCookies on client 'b'")]
    public void WithCookies_OnClientA_DoesNotAffectClientB()
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

    // Mixed configuration — client with cookies, client without

    [Fact(DisplayName = "Client with WithCookies and client without are independently configured")]
    public void ClientWithCookies_AndClientWithout_AreIndependent()
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
