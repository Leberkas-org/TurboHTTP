using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TurboHttp.Protocol.Cookies;
using TurboHttp.Protocol.Semantics;
using TurboHttp.Protocol.Caching;

namespace TurboHttp.Tests.Hosting;

public sealed class TurboHttpClientBuilderFeatureTests
{
    private static TurboClientDescriptor GetDescriptor(IServiceCollection services, string name)
    {
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IOptionsMonitor<TurboClientDescriptor>>().Get(name);
    }

    [Fact(DisplayName = "WithCookies() sets EnableCookies to true with no custom jar")]
    public void WithCookies_NoJar_SetsEnableCookiesTrue()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").WithCookies();

        var descriptor = GetDescriptor(services, "test");

        Assert.True(descriptor.EnableCookies);
        Assert.Null(descriptor.CustomCookieJar);
    }

    [Fact(DisplayName = "WithCookies(jar) sets EnableCookies to true and assigns the custom jar")]
    public void WithCookies_WithJar_SetsCustomCookieJar()
    {
        var jar = new CookieJar();
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").WithCookies(jar);

        var descriptor = GetDescriptor(services, "test");

        Assert.True(descriptor.EnableCookies);
        Assert.Same(jar, descriptor.CustomCookieJar);
    }

    [Fact(DisplayName = "WithCache(policy) assigns the cache policy to the descriptor")]
    public void WithCache_AssignsCachePolicy()
    {
        var policy = new CachePolicy { MaxEntries = 500 };
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").WithCache(policy);

        var descriptor = GetDescriptor(services, "test");

        Assert.Equal(policy, descriptor.CachePolicy);
    }

    [Fact(DisplayName = "WithRetry(policy) assigns the retry policy to the descriptor")]
    public void WithRetry_AssignsRetryPolicy()
    {
        var policy = new RetryPolicy { MaxRetries = 5 };
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").WithRetry(policy);

        var descriptor = GetDescriptor(services, "test");

        Assert.Equal(policy, descriptor.RetryPolicy);
    }

    [Fact(DisplayName = "WithRedirect() sets a non-null default redirect policy")]
    public void WithRedirect_NoPolicy_SetsDefaultPolicy()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").WithRedirect();

        var descriptor = GetDescriptor(services, "test");

        Assert.NotNull(descriptor.RedirectPolicy);
    }

    [Fact(DisplayName = "WithRedirect(policy) assigns the provided redirect policy to the descriptor")]
    public void WithRedirect_WithPolicy_AssignsRedirectPolicy()
    {
        var policy = new RedirectPolicy { MaxRedirects = 5 };
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").WithRedirect(policy);

        var descriptor = GetDescriptor(services, "test");

        Assert.Equal(policy, descriptor.RedirectPolicy);
    }

    [Fact(DisplayName = "Default descriptor has AutomaticDecompression true")]
    public void Default_AutomaticDecompression_IsTrue()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test");

        var descriptor = GetDescriptor(services, "test");

        Assert.True(descriptor.AutomaticDecompression);
    }

    [Fact(DisplayName = "WithDecompression() sets AutomaticDecompression to true")]
    public void WithDecompression_NoArg_SetsTrue()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").WithDecompression();

        var descriptor = GetDescriptor(services, "test");

        Assert.True(descriptor.AutomaticDecompression);
    }

    [Fact(DisplayName = "WithDecompression(false) sets AutomaticDecompression to false")]
    public void WithDecompression_False_SetsFalse()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").WithDecompression(false);

        var descriptor = GetDescriptor(services, "test");

        Assert.False(descriptor.AutomaticDecompression);
    }
}
