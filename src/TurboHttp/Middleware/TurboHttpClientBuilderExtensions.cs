using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TurboHttp.Protocol.RFC6265;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Protocol.RFC9111;

namespace TurboHttp;

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

    public static ITurboHttpClientBuilder WithDecompression(this ITurboHttpClientBuilder builder, bool enabled = true)
    {
        builder.Services.Configure<TurboClientDescriptor>(builder.Name, d =>
        {
            d.AutomaticDecompression = enabled;
        });
        return builder;
    }

    /// <summary>
    /// Registers <typeparamref name="T"/> as a Transient service and appends it to the middleware pipeline.
    /// Registration order is preserved (FIFO).
    /// </summary>
    public static ITurboHttpClientBuilder AddMiddleware<T>(this ITurboHttpClientBuilder builder)
        where T : TurboHandler
    {
        builder.Services.AddTransient<T>();
        builder.Services.Configure<TurboClientDescriptor>(builder.Name, d =>
        {
            d.MiddlewareTypes.Add(typeof(T));
            d.MiddlewareFactories.Add(sp => sp.GetRequiredService<T>());
        });
        return builder;
    }

    /// <summary>
    /// Wraps a request transform delegate in an anonymous <see cref="TurboMiddleware"/> and appends it
    /// to the middleware pipeline. Registration order is preserved (FIFO).
    /// </summary>
    public static ITurboHttpClientBuilder UseRequest(
        this ITurboHttpClientBuilder builder,
        Func<HttpRequestMessage, CancellationToken, ValueTask<HttpRequestMessage>> transform)
    {
        builder.Services.Configure<TurboClientDescriptor>(builder.Name, d =>
        {
            d.MiddlewareFactories.Add(_ => new DelegateRequestMiddleware(transform));
        });
        return builder;
    }

    /// <summary>
    /// Wraps a response transform delegate in an anonymous <see cref="TurboMiddleware"/> and appends it
    /// to the middleware pipeline. Registration order is preserved (FIFO).
    /// </summary>
    public static ITurboHttpClientBuilder UseResponse(
        this ITurboHttpClientBuilder builder,
        Func<HttpRequestMessage, HttpResponseMessage, CancellationToken, ValueTask<HttpResponseMessage>> transform)
    {
        builder.Services.Configure<TurboClientDescriptor>(builder.Name, d =>
        {
            d.MiddlewareFactories.Add(_ => new DelegateResponseMiddleware(transform));
        });
        return builder;
    }

    private sealed class DelegateRequestMiddleware : TurboHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, ValueTask<HttpRequestMessage>> _transform;

        public DelegateRequestMiddleware(Func<HttpRequestMessage, CancellationToken, ValueTask<HttpRequestMessage>> transform)
            => _transform = transform;

        public override HttpRequestMessage ProcessRequest(HttpRequestMessage request)
            => _transform(request, CancellationToken.None).Result;
    }

    private sealed class DelegateResponseMiddleware : TurboHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage, CancellationToken, ValueTask<HttpResponseMessage>> _transform;

        public DelegateResponseMiddleware(Func<HttpRequestMessage, HttpResponseMessage, CancellationToken, ValueTask<HttpResponseMessage>> transform)
            => _transform = transform;

        public override HttpResponseMessage ProcessResponse(HttpRequestMessage original, HttpResponseMessage response)
            => _transform(original, response, CancellationToken.None).Result;
    }
}
