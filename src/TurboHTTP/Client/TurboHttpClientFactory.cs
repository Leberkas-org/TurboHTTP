using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using Microsoft.Extensions.Options;
using TurboHTTP.Features.AltSvc;
using TurboHTTP.Features.Caching;
using TurboHTTP.Features.Cookies;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Lifecycle;

namespace TurboHTTP.Client;

internal sealed class TurboHttpClientFactory(
    IOptionsMonitor<TurboClientOptions> options,
    IOptionsMonitor<TurboClientDescriptor> descriptors,
    IServiceProvider provider,
    ActorSystem system)
    : ITurboHttpClientFactory, IDisposable
{
    private readonly IActorRef _manager =
        system.ActorOf(ClientStreamManager.Props(), $"stream-manager-{Guid.NewGuid():N}");

    private int _disposed;

    public ITurboHttpClient CreateClient(string name) => CreateClient(name, transportOverride: null);

    internal ITurboHttpClient CreateClient(string name, TransportRegistry? transportOverride)
    {
        ThrowIfDisposed();

        var clientOptions = options.Get(name);
        var descriptor = descriptors.Get(name);
        var pipeline = BuildPipeline(clientOptions, descriptor);

        var consumerId = Guid.NewGuid();
        var consumerRequests = Channel.CreateUnbounded<HttpRequestMessage>(
            new UnboundedChannelOptions { SingleReader = true });
        var consumerResponses = Channel.CreateUnbounded<HttpResponseMessage>(
            new UnboundedChannelOptions { SingleWriter = true });

        var registration = new NamedClientConsumerRegistration(_manager, name, consumerId);

        var client = new TurboHttpClient(
            consumerRequests.Writer,
            consumerResponses.Reader,
            CreateRequestOptions(clientOptions),
            registration);

        _manager.Tell(new ClientStreamManager.RegisterConsumer(
            name,
            consumerId,
            consumerRequests.Reader,
            consumerResponses.Writer,
            () => client.CachedOptions,
            clientOptions,
            pipeline,
            transportOverride));

        return client;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _manager.Tell(new ClientStreamManager.Shutdown());
    }

    private PipelineDescriptor BuildPipeline(TurboClientOptions clientOptions, TurboClientDescriptor descriptor)
    {
        var cookieJar = descriptor.EnableCookies
            ? descriptor.CustomCookieJar ?? new CookieJar()
            : null;

        var cacheStore = descriptor.CachePolicy is not null
            ? new Cache(descriptor.CustomCacheStore ?? new MemoryCacheStore(), descriptor.CachePolicy)
            : null;

        IReadOnlyList<TurboHandler> middlewares = descriptor.HandlerFactories.Count == 0
            ? []
            : descriptor.HandlerFactories.Select(f => f(provider)).ToList();

        var altSvcCache = clientOptions.Http3.EnableAltSvcDiscovery
            ? new AltSvcCache()
            : null;

        return new PipelineDescriptor(
            RedirectPolicy: descriptor.RedirectPolicy,
            RetryPolicy: descriptor.RetryPolicy,
            Expect100Policy: descriptor.Expect100Policy,
            CompressionPolicy: descriptor.CompressionPolicy,
            CookieJar: cookieJar,
            CacheStore: cacheStore,
            CachePolicy: descriptor.CachePolicy,
            Handlers: middlewares,
            AutomaticDecompression: descriptor.AutomaticDecompression,
            AltSvcCache: altSvcCache);
    }

    private static TurboRequestOptions CreateRequestOptions(TurboClientOptions clientOptions)
    {
        return new TurboRequestOptions(
            BaseAddress: clientOptions.BaseAddress,
            DefaultRequestHeaders: new HttpRequestMessage().Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(60),
            Credentials: clientOptions.Credentials,
            PreAuthenticate: clientOptions.PreAuthenticate);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(TurboHttpClientFactory));
        }
    }
}