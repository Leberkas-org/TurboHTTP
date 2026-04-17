using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Protocol.Caching;
using TurboHTTP.Protocol.Cookies;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP;

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
        builder.Services.Configure<TurboClientDescriptor>(builder.Name, d => { d.CachePolicy = policy; });
        return builder;
    }

    public static ITurboHttpClientBuilder WithCache(this ITurboHttpClientBuilder builder, CacheStore store,
        CachePolicy? policy = null)
    {
        builder.Services.Configure<TurboClientDescriptor>(builder.Name, d =>
        {
            d.CachePolicy = policy ?? CachePolicy.Default;
            d.CustomCacheStore = store;
        });
        return builder;
    }

    public static ITurboHttpClientBuilder WithRetry(this ITurboHttpClientBuilder builder, RetryPolicy policy)
    {
        builder.Services.Configure<TurboClientDescriptor>(builder.Name, d => { d.RetryPolicy = policy; });
        return builder;
    }

    public static ITurboHttpClientBuilder WithRedirect(this ITurboHttpClientBuilder builder,
        RedirectPolicy? policy = null)
    {
        builder.Services.Configure<TurboClientDescriptor>(builder.Name,
            d => { d.RedirectPolicy = policy ?? new RedirectPolicy(); });
        return builder;
    }

    public static ITurboHttpClientBuilder WithDecompression(this ITurboHttpClientBuilder builder, bool enabled = true)
    {
        builder.Services.Configure<TurboClientDescriptor>(builder.Name, d => { d.AutomaticDecompression = enabled; });
        return builder;
    }

    public static ITurboHttpClientBuilder WithRequestCompression(
        this ITurboHttpClientBuilder builder, CompressionPolicy? policy = null)
    {
        builder.Services.Configure<TurboClientDescriptor>(builder.Name,
            d => { d.CompressionPolicy = policy ?? CompressionPolicy.Default; });
        return builder;
    }

    public static ITurboHttpClientBuilder WithExpectContinue(
        this ITurboHttpClientBuilder builder, Expect100Policy? policy = null)
    {
        builder.Services.Configure<TurboClientDescriptor>(builder.Name,
            d => { d.Expect100Policy = policy ?? Expect100Policy.Default; });
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