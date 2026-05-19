using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Features.Caching;
using TurboHTTP.Features.Cookies;

namespace TurboHTTP.Client;

public static class TurboHttpClientBuilderExtensions
{
    public static ITurboHttpClientBuilder WithCookies(this ITurboHttpClientBuilder builder)
    {
        builder.Services.Configure<TurboClientDescriptor>(builder.Name, d =>
        {
            d.EnableCookies = true;
            d.CustomCookieJar = new CookieJar();
        });
        return builder;
    }

    public static ITurboHttpClientBuilder WithCookies(this ITurboHttpClientBuilder builder, ICookieStore store)
    {
        builder.Services.Configure<TurboClientDescriptor>(builder.Name, d =>
        {
            d.EnableCookies = true;
            d.CustomCookieJar = new CookieJar(store);
        });
        return builder;
    }

    public static ITurboHttpClientBuilder WithCache(this ITurboHttpClientBuilder builder,
        Action<CacheOptions>? configure = null)
    {
        builder.Services.Configure<TurboClientDescriptor>(builder.Name, d =>
        {
            var options = new CacheOptions();
            configure?.Invoke(options);
            d.CachePolicy = options.To();
        });
        return builder;
    }

    public static ITurboHttpClientBuilder WithCache(this ITurboHttpClientBuilder builder, ICacheStore store,
        Action<CacheOptions>? configure = null)
    {
        builder.Services.Configure<TurboClientDescriptor>(builder.Name, d =>
        {
            var options = new CacheOptions();
            configure?.Invoke(options);
            d.CachePolicy = options.To();
            d.CustomCacheStore = store;
        });
        return builder;
    }

    public static ITurboHttpClientBuilder WithRetry(this ITurboHttpClientBuilder builder,
        Action<RetryOptions>? configure = null)
    {
        builder.Services.Configure<TurboClientDescriptor>(builder.Name, d =>
        {
            var options = new RetryOptions();
            configure?.Invoke(options);
            d.RetryPolicy = options.To();
        });
        return builder;
    }

    public static ITurboHttpClientBuilder WithRedirect(this ITurboHttpClientBuilder builder,
        Action<RedirectOptions>? configure = null)
    {
        builder.Services.Configure<TurboClientDescriptor>(builder.Name,
            d =>
            {
                var options = new RedirectOptions();
                configure?.Invoke(options);
                d.RedirectPolicy = options.To();
            });
        return builder;
    }

    public static ITurboHttpClientBuilder WithDecompression(this ITurboHttpClientBuilder builder, bool enabled = true)
    {
        builder.Services.Configure<TurboClientDescriptor>(builder.Name, d => { d.AutomaticDecompression = enabled; });
        return builder;
    }

    public static ITurboHttpClientBuilder WithRequestCompression(
        this ITurboHttpClientBuilder builder, Action<CompressionOptions>? configure = null)
    {
        builder.Services.Configure<TurboClientDescriptor>(builder.Name,
            d =>
            {
                var options = new CompressionOptions();
                configure?.Invoke(options);
                d.CompressionPolicy = options.To();
            });
        return builder;
    }

    public static ITurboHttpClientBuilder WithExpectContinue(
        this ITurboHttpClientBuilder builder, Action<Expect100Options>? configure = null)
    {
        builder.Services.Configure<TurboClientDescriptor>(builder.Name,
            d =>
            {
                var options = new Expect100Options();
                configure?.Invoke(options);
                d.Expect100Policy = options.To();
            });
        return builder;
    }

    /// <summary>
    /// Registers <typeparamref name="T"/> as a Transient service and appends it to the handler pipeline.
    /// Registration order is preserved (FIFO).
    /// </summary>
    public static ITurboHttpClientBuilder AddHandler<T>(this ITurboHttpClientBuilder builder)
        where T : TurboHandler
    {
        builder.Services.AddTransient<T>();
        builder.Services.Configure<TurboClientDescriptor>(builder.Name, d =>
        {
            d.HandlerTypes.Add(typeof(T));
            d.HandlerFactories.Add(sp => sp.GetRequiredService<T>());
        });
        return builder;
    }

    /// <summary>
    /// Wraps a request transform delegate in an anonymous <see cref="TurboHandler"/> and appends it
    /// to the handler pipeline. Registration order is preserved (FIFO).
    /// </summary>
    public static ITurboHttpClientBuilder UseRequest(
        this ITurboHttpClientBuilder builder,
        Func<HttpRequestMessage, HttpRequestMessage> transform)
    {
        builder.Services.Configure<TurboClientDescriptor>(builder.Name,
            d => { d.HandlerFactories.Add(_ => new DelegateRequestHandler(transform)); });
        return builder;
    }

    /// <summary>
    /// Wraps a response transform delegate in an anonymous <see cref="TurboHandler"/> and appends it
    /// to the handler pipeline. Registration order is preserved (FIFO).
    /// </summary>
    public static ITurboHttpClientBuilder UseResponse(
        this ITurboHttpClientBuilder builder,
        Func<HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> transform)
    {
        builder.Services.Configure<TurboClientDescriptor>(builder.Name,
            d => { d.HandlerFactories.Add(_ => new DelegateResponseHandler(transform)); });
        return builder;
    }

    private sealed class DelegateRequestHandler(Func<HttpRequestMessage, HttpRequestMessage> transform) : TurboHandler
    {
        public override HttpRequestMessage ProcessRequest(HttpRequestMessage request)
            => transform(request);
    }

    private sealed class DelegateResponseHandler(
        Func<HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> transform)
        : TurboHandler
    {
        public override HttpResponseMessage ProcessResponse(HttpRequestMessage original, HttpResponseMessage response)
            => transform(original, response);
    }
}