using Microsoft.Extensions.DependencyInjection;
using TurboHttp.Protocol.RFC6265;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Protocol.RFC9111;

namespace TurboHttp.Middleware;

public static class TurboHttpClientBuilderExtensions
{
    public static ITurboHttpClientBuilder WithCookies(this ITurboHttpClientBuilder builder, CookieJar? jar = null)
    {
        builder.Services.Configure<TurboClientDescriptor>(builder.Name, d =>
        {
            d.EnableCookies = true;
            d.CustomCookieJar = jar;
        });
        return builder;
    }

    public static ITurboHttpClientBuilder WithCache(this ITurboHttpClientBuilder builder, CachePolicy policy)
    {
        builder.Services.Configure<TurboClientDescriptor>(builder.Name, d =>
        {
            d.CachePolicy = policy;
        });
        return builder;
    }

    public static ITurboHttpClientBuilder WithRetry(this ITurboHttpClientBuilder builder, RetryPolicy policy)
    {
        builder.Services.Configure<TurboClientDescriptor>(builder.Name, d =>
        {
            d.RetryPolicy = policy;
        });
        return builder;
    }

    public static ITurboHttpClientBuilder WithRedirect(this ITurboHttpClientBuilder builder, RedirectPolicy? policy = null)
    {
        builder.Services.Configure<TurboClientDescriptor>(builder.Name, d =>
        {
            d.RedirectPolicy = policy ?? new RedirectPolicy();
        });
        return builder;
    }
}
