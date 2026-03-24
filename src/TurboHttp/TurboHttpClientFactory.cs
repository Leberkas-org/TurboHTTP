using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Microsoft.Extensions.Options;
using TurboHttp.Protocol.RFC6265;
using TurboHttp.Protocol.RFC9111;
using TurboHttp.Streams;

namespace TurboHttp;

/// <summary>
/// Default implementation of <see cref="ITurboHttpClientFactory"/>.
/// Reads per-client configuration from <see cref="IOptionsMonitor{TOptions}"/> at
/// <see cref="CreateClient"/> time so that changes are picked up without restarting.
/// </summary>
internal sealed class TurboHttpClientFactory(
    IOptionsMonitor<TurboClientOptions> options,
    IOptionsMonitor<TurboClientDescriptor> descriptors,
    IServiceProvider provider,
    ActorSystem system)
    : ITurboHttpClientFactory
{
    public ITurboHttpClient CreateClient(string name)
    {
        var clientOptions = options.Get(name);
        var descriptor = descriptors.Get(name);

        var cookieJar = descriptor.EnableCookies
            ? descriptor.CustomCookieJar ?? new CookieJar()
            : null;

        var cacheStore = descriptor.CachePolicy is not null
            ? descriptor.CustomCacheStore ?? new CacheStore(descriptor.CachePolicy)
            : null;

        IReadOnlyList<TurboHandler> middlewares = descriptor.HandlerFactories.Count == 0
            ? []
            : descriptor.HandlerFactories.Select(f => f(provider)).ToList();

        var pipeline = new PipelineDescriptor(
            RedirectPolicy: descriptor.RedirectPolicy,
            RetryPolicy: descriptor.RetryPolicy,
            Expect100Policy: descriptor.Expect100Policy,
            RequestCompressionPolicy: descriptor.RequestCompressionPolicy,
            CookieJar: cookieJar,
            CacheStore: cacheStore,
            CachePolicy: descriptor.CachePolicy,
            Handlers: middlewares,
            AutomaticDecompression: descriptor.AutomaticDecompression);

        return new TurboHttpClient(clientOptions, system, pipeline);
    }
}
