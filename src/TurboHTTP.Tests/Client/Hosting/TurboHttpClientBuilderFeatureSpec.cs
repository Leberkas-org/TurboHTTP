using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TurboHTTP.Client;
using TurboHTTP.Features.Cookies;

namespace TurboHTTP.Tests.Client.Hosting;

public sealed class TurboHttpClientBuilderFeatureSpec
{
    private static TurboClientDescriptor GetDescriptor(IServiceCollection services, string name)
    {
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IOptionsMonitor<TurboClientDescriptor>>().Get(name);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpClientBuilderFeature_should_set_enable_cookies_true_when_no_jar()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").WithCookies();

        var descriptor = GetDescriptor(services, "test");

        Assert.True(descriptor.EnableCookies);
        Assert.NotNull(descriptor.CustomCookieJar);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpClientBuilderFeature_should_set_custom_cookie_store()
    {
        var store = new MemoryCookieStore();
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").WithCookies(store);

        var descriptor = GetDescriptor(services, "test");

        Assert.True(descriptor.EnableCookies);
        Assert.NotNull(descriptor.CustomCookieJar);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpClientBuilderFeature_should_assign_cache_policy()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").WithCache(x => x.MaxEntries = 500);

        var descriptor = GetDescriptor(services, "test");

        Assert.Equal(500, descriptor.CachePolicy?.MaxEntries);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpClientBuilderFeature_should_assign_retry_policy()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").WithRetry(x => x.MaxRetries = 5);

        var descriptor = GetDescriptor(services, "test");

        Assert.Equal(5, descriptor.RetryPolicy?.MaxRetries);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpClientBuilderFeature_should_set_default_redirect_policy()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").WithRedirect();

        var descriptor = GetDescriptor(services, "test");

        Assert.NotNull(descriptor.RedirectPolicy);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpClientBuilderFeature_should_assign_redirect_policy()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").WithRedirect(x => x.MaxRedirects = 5);

        var descriptor = GetDescriptor(services, "test");

        Assert.Equal(5, descriptor.RedirectPolicy?.MaxRedirects);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpClientBuilderFeature_should_have_automatic_decompression_true()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test");

        var descriptor = GetDescriptor(services, "test");

        Assert.True(descriptor.AutomaticDecompression);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpClientBuilderFeature_should_set_automatic_decompression_true()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").WithDecompression();

        var descriptor = GetDescriptor(services, "test");

        Assert.True(descriptor.AutomaticDecompression);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpClientBuilderFeature_should_set_automatic_decompression_false()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").WithDecompression(false);

        var descriptor = GetDescriptor(services, "test");

        Assert.False(descriptor.AutomaticDecompression);
    }
}