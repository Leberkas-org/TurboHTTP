using Akka.Actor;
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
        builder.Services.Configure<TurboClientDescriptor>(builder.Name, d => { d.CachePolicy = policy; });
        return builder;
    }

    public static ITurboHttpClientBuilder WithCache(this ITurboHttpClientBuilder builder, CacheStore store, CachePolicy? policy = null)
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

    /// <summary>
    /// Configures a custom <see cref="SupervisorStrategy"/> for the <c>ClientStreamOwner</c> actor
    /// that supervises the stream instance. When not set, the default <c>AllForOneStrategy</c> with
    /// 3 retries and exponential backoff (100ms, 500ms, 2s) is used.
    /// <para>
    /// This is an advanced option for users who need fine-grained control over stream instance
    /// supervision behavior — for example, to increase retry limits or customize the decider logic.
    /// </para>
    /// </summary>
    /// <param name="builder">The client builder.</param>
    /// <param name="strategy">The custom supervisor strategy to apply.</param>
    /// <returns>The builder for chaining.</returns>
    public static ITurboHttpClientBuilder WithSupervisorStrategy(this ITurboHttpClientBuilder builder,
        SupervisorStrategy strategy)
    {
        builder.Services.Configure<TurboClientDescriptor>(builder.Name, d => { d.CustomSupervisorStrategy = strategy; });
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