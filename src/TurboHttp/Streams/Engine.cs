using System;
using System.Buffers;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Client;
using TurboHttp.Protocol;
using TurboHttp.Streams.Stages;

namespace TurboHttp.Streams;

public class Engine
{
    public Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> CreateFlow(IActorRef clientManager)
        => CreateFlow(clientManager, options: null);

    /// <summary>
    /// Creates the full HTTP pipeline flow, conditionally inserting protocol-handler stages
    /// based on <paramref name="options"/> feature flags.
    /// When all flags are false the pipeline is identical to the no-options overload.
    /// </summary>
    public Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> CreateFlow(
        IActorRef clientManager,
        TurboClientOptions? options)
    {
        options ??= new TurboClientOptions();
        var requestOptions = BuildRequestOptions(options);

        if (!NeedsExtendedPipeline(options))
        {
            return Flow.FromGraph(GraphDsl.Create(builder =>
            {
                var enricher = builder.Add(new RequestEnricherStage(() => requestOptions));

                var partition = builder.Add(Router());
                var hub = builder.Add(new Merge<HttpResponseMessage>(4));

                var http10 = builder.Add(BuildProtocolFlow<Http10Engine>(4, clientManager));
                var http11 = builder.Add(BuildProtocolFlow<Http11Engine>(4, clientManager));
                var http20 = builder.Add(BuildProtocolFlow<Http20Engine>(1, clientManager));
                var http30 = builder.Add(BuildProtocolFlow<Http30Engine>(1, clientManager));

                builder.From(enricher.Outlet).To(partition);
                builder.From(partition.Out(0)).Via(http10).To(hub);
                builder.From(partition.Out(1)).Via(http11).To(hub);
                builder.From(partition.Out(2)).Via(http20).To(hub);
                builder.From(partition.Out(3)).Via(http30).To(hub);

                return new FlowShape<HttpRequestMessage, HttpResponseMessage>(enricher.Inlet, hub.Out);
            }));
        }

        return BuildExtendedPipeline(clientManager, options, requestOptions);
    }

    internal Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> CreateFlow(
        Func<Flow<ITransportItem, (IMemoryOwner<byte>, int), NotUsed>> http10Factory,
        Func<Flow<ITransportItem, (IMemoryOwner<byte>, int), NotUsed>> http11Factory,
        Func<Flow<ITransportItem, (IMemoryOwner<byte>, int), NotUsed>> http20Factory,
        Func<Flow<ITransportItem, (IMemoryOwner<byte>, int), NotUsed>> http30Factory,
        TurboClientOptions? options = null)
    {
        options ??= new TurboClientOptions();

        var holder = new HttpRequestMessage();
        var defaultOptions = new TurboRequestOptions(
            BaseAddress: null,
            DefaultRequestHeaders: holder.Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrHigher,
            Timeout: TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize: 1024 * 1024);

        if (!NeedsExtendedPipeline(options))
        {
            return Flow.FromGraph(GraphDsl.Create(builder =>
            {
                // For testing, provide a minimal options object instead of null
                var enricher = builder.Add(new RequestEnricherStage(() => defaultOptions));

                // Custom 3-port partition for testing (HTTP/3.0 not yet implemented)
                var partition = builder.Add(new Partition<HttpRequestMessage>(3, msg
                    => msg.Version switch
                    {
                        { Major: 2, Minor: 0 } => 2,
                        { Major: 1, Minor: 1 } => 1,
                        { Major: 1, Minor: 0 } => 0,
                        _ => throw new SwitchExpressionException(msg.Version)
                    }));

                var hub = builder.Add(new Merge<HttpResponseMessage>(3));

                var http10 = builder.Add(BuildProtocolFlow<Http10Engine>(1, ActorRefs.Nobody, http10Factory));
                var http11 = builder.Add(BuildProtocolFlow<Http11Engine>(1, ActorRefs.Nobody, http11Factory));
                var http20 = builder.Add(BuildProtocolFlow<Http20Engine>(1, ActorRefs.Nobody, http20Factory));

                builder.From(enricher.Outlet).To(partition);
                builder.From(partition.Out(0)).Via(http10).To(hub);
                builder.From(partition.Out(1)).Via(http11).To(hub);
                builder.From(partition.Out(2)).Via(http20).To(hub);

                return new FlowShape<HttpRequestMessage, HttpResponseMessage>(enricher.Inlet, hub.Out);
            }));
        }

        return BuildExtendedPipeline(ActorRefs.Nobody, options, defaultOptions,
            http10Factory, http11Factory, http20Factory);
    }

    private static bool NeedsExtendedPipeline(TurboClientOptions options) =>
        options.EnableCookies ||
        options.EnableCaching ||
        options.EnableRetry ||
        options.EnableRedirectHandling ||
        options.EnableDecompression;

    private static TurboRequestOptions BuildRequestOptions(TurboClientOptions options)
    {
        var holder = new HttpRequestMessage();
        return new TurboRequestOptions(
            BaseAddress: options.BaseAddress,
            DefaultRequestHeaders: holder.Headers,
            DefaultRequestVersion: options.DefaultRequestVersion,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrHigher,
            Timeout: TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize: 1024 * 1024);
    }

    /// <summary>
    /// Builds the extended pipeline graph with conditionally-inserted protocol handler stages.
    ///
    /// Full topology (all flags enabled):
    /// <code>
    ///   Input
    ///     ↓
    ///   [RequestEnricher]
    ///     ↓
    ///   [MergePreferred] ← redirect feedback (buffered)
    ///     ↓
    ///   [CookieInjection]       (EnableCookies)
    ///     ↓
    ///   [MergePreferred] ← retry feedback (buffered)
    ///     ↓
    ///   [CacheLookup]           (EnableCaching)
    ///     ↓ miss      ↓ hit ──────────────────────────┐
    ///   [Engine Core]                                  │
    ///     ↓                                            │
    ///   [Decompression]         (EnableDecompression)  │
    ///     ↓                                            │
    ///   [CookieStorage]         (EnableCookies)        │
    ///     ↓                                            │
    ///   [CacheStorage]          (EnableCaching)        │
    ///     ↓                                            │
    ///   [RetryStage]            (EnableRetry)          │
    ///     ↓ final   ↓ retry → retryMerge.Preferred     │
    ///   [Merge(2)] ←────────────────────────────────── ┘  (cache hit)
    ///     ↓
    ///   [RedirectStage]         (EnableRedirectHandling)
    ///     ↓ final   ↓ redirect → redirectMerge.Preferred
    ///   Output
    /// </code>
    /// </summary>
    private static Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> BuildExtendedPipeline(
        IActorRef clientManager,
        TurboClientOptions options,
        TurboRequestOptions requestOptions,
        Func<Flow<ITransportItem, (IMemoryOwner<byte>, int), NotUsed>>? http10Factory = null,
        Func<Flow<ITransportItem, (IMemoryOwner<byte>, int), NotUsed>>? http11Factory = null,
        Func<Flow<ITransportItem, (IMemoryOwner<byte>, int), NotUsed>>? http20Factory = null)
    {
        var cookieJar = options.EnableCookies ? new CookieJar() : null;
        var cacheStore = options.EnableCaching ? new HttpCacheStore(options.CachePolicy) : null;

        return Flow.FromGraph(GraphDsl.Create(builder =>
        {
            // ---- REQUEST CHAIN ----

            var enricher = builder.Add(new RequestEnricherStage(() => requestOptions));
            Outlet<HttpRequestMessage> requestTip = enricher.Outlet;

            // Redirect merge: preferred port receives redirect feedback.
            // MergePreferred is used so that the preferred (feedback) path never back-pressures
            // the cycle, preventing deadlock.
            MergePreferred<HttpRequestMessage>.MergePreferredShape? redirectMerge = null;
            if (options.EnableRedirectHandling)
            {
                var m = builder.Add(new MergePreferred<HttpRequestMessage>(1));
                builder.From(requestTip).To(m.In(0));
                redirectMerge = m;
                requestTip = m.Out;
            }

            // Cookie injection
            if (options.EnableCookies)
            {
                var cookieInject = builder.Add(new CookieInjectionStage(cookieJar));
                builder.From(requestTip).To(cookieInject.Inlet);
                requestTip = cookieInject.Outlet;
            }

            // Retry merge: preferred port receives retry feedback.
            MergePreferred<HttpRequestMessage>.MergePreferredShape? retryMerge = null;
            if (options.EnableRetry)
            {
                var m = builder.Add(new MergePreferred<HttpRequestMessage>(1));
                builder.From(requestTip).To(m.In(0));
                retryMerge = m;
                requestTip = m.Out;
            }

            // Cache lookup fan-out: Out0 = cache miss (forward to engine), Out1 = cache hit (bypass engine)
            Outlet<HttpResponseMessage>? cacheHitTip = null;
            if (options.EnableCaching)
            {
                var cacheLookup = builder.Add(new CacheLookupStage(cacheStore!, options.CachePolicy));
                builder.From(requestTip).To(cacheLookup.In);
                requestTip = cacheLookup.Out0;  // miss → engine
                cacheHitTip = cacheLookup.Out1; // hit → cache merge (bypasses engine)
            }

            // ---- ENGINE CORE ----

            var engineCore = builder.Add(
                BuildEngineCoreGraph(clientManager, http10Factory, http11Factory, http20Factory));
            builder.From(requestTip).To(engineCore.Inlet);
            Outlet<HttpResponseMessage> responseTip = engineCore.Outlet;

            // ---- RESPONSE CHAIN ----

            // Decompression
            if (options.EnableDecompression)
            {
                var decomp = builder.Add(new DecompressionStage());
                builder.From(responseTip).To(decomp.Inlet);
                responseTip = decomp.Outlet;
            }

            // Cookie storage (side-effect: stores Set-Cookie headers)
            if (options.EnableCookies)
            {
                var cookieStorage = builder.Add(new CookieStorageStage(cookieJar));
                builder.From(responseTip).To(cookieStorage.Inlet);
                responseTip = cookieStorage.Outlet;
            }

            // Cache storage (stores 2xx responses, merges 304s)
            if (options.EnableCaching)
            {
                var cacheStorage = builder.Add(new CacheStorageStage(cacheStore!));
                builder.From(responseTip).To(cacheStorage.Inlet);
                responseTip = cacheStorage.Outlet;
            }

            // Retry stage — wraps the engine core (not a linear stage):
            // Out0 = final response, Out1 = retry request fed back to retryMerge.
            // A buffer decouples the feedback cycle to prevent deadlock.
            if (options.EnableRetry)
            {
                var retry = builder.Add(new RetryStage(options.RetryPolicy));
                builder.From(responseTip).To(retry.In);
                builder.From(retry.Out1)
                    .Via(Flow.Create<HttpRequestMessage>().Buffer(1, OverflowStrategy.Backpressure))
                    .To(retryMerge!.Preferred);
                responseTip = retry.Out0;
            }

            // Cache merge: rejoins the cache-hit path with engine responses.
            // Cache hits bypass the engine, decompression, cookie/cache storage, and retry.
            if (options.EnableCaching)
            {
                var cacheMerge = builder.Add(new Merge<HttpResponseMessage>(2));
                builder.From(responseTip).To(cacheMerge.In(0));
                builder.From(cacheHitTip!).To(cacheMerge.In(1));
                responseTip = cacheMerge.Out;
            }

            // Redirect stage — wraps the full pipeline (not a linear stage):
            // Out0 = final response, Out1 = redirect request fed back to redirectMerge.
            // A buffer decouples the feedback cycle to prevent deadlock.
            if (options.EnableRedirectHandling)
            {
                var redirect = builder.Add(new RedirectStage(
                    new RedirectHandler(options.RedirectPolicy), cookieJar));
                builder.From(responseTip).To(redirect.In);
                builder.From(redirect.Out1)
                    .Via(Flow.Create<HttpRequestMessage>().Buffer(1, OverflowStrategy.Backpressure))
                    .To(redirectMerge!.Preferred);
                responseTip = redirect.Out0;
            }

            return new FlowShape<HttpRequestMessage, HttpResponseMessage>(enricher.Inlet, responseTip);
        }));
    }

    /// <summary>
    /// Builds the engine core: HTTP-version router (Partition) → per-version protocol flows → Merge.
    /// When factory functions are supplied (test mode) a 3-port Partition is used.
    /// In production mode a 4-port Partition (HTTP/1.0, 1.1, 2.0, 3.0) is used.
    /// </summary>
    private static IGraph<FlowShape<HttpRequestMessage, HttpResponseMessage>, NotUsed> BuildEngineCoreGraph(
        IActorRef clientManager,
        Func<Flow<ITransportItem, (IMemoryOwner<byte>, int), NotUsed>>? http10Factory,
        Func<Flow<ITransportItem, (IMemoryOwner<byte>, int), NotUsed>>? http11Factory,
        Func<Flow<ITransportItem, (IMemoryOwner<byte>, int), NotUsed>>? http20Factory)
    {
        if (http10Factory is not null)
        {
            // Test mode: 3-port partition (no HTTP/3)
            return GraphDsl.Create(builder =>
            {
                var partition = builder.Add(new Partition<HttpRequestMessage>(3, msg
                    => msg.Version switch
                    {
                        { Major: 2, Minor: 0 } => 2,
                        { Major: 1, Minor: 1 } => 1,
                        { Major: 1, Minor: 0 } => 0,
                        _ => throw new SwitchExpressionException(msg.Version)
                    }));
                var hub = builder.Add(new Merge<HttpResponseMessage>(3));

                var http10 = builder.Add(BuildProtocolFlow<Http10Engine>(1, ActorRefs.Nobody, http10Factory));
                var http11 = builder.Add(BuildProtocolFlow<Http11Engine>(1, ActorRefs.Nobody, http11Factory!));
                var http20 = builder.Add(BuildProtocolFlow<Http20Engine>(1, ActorRefs.Nobody, http20Factory!));

                builder.From(partition.Out(0)).Via(http10).To(hub);
                builder.From(partition.Out(1)).Via(http11).To(hub);
                builder.From(partition.Out(2)).Via(http20).To(hub);

                return new FlowShape<HttpRequestMessage, HttpResponseMessage>(partition.In, hub.Out);
            });
        }
        else
        {
            // Production mode: 4-port partition
            return GraphDsl.Create(builder =>
            {
                var partition = builder.Add(Router());
                var hub = builder.Add(new Merge<HttpResponseMessage>(4));

                var http10 = builder.Add(BuildProtocolFlow<Http10Engine>(4, clientManager));
                var http11 = builder.Add(BuildProtocolFlow<Http11Engine>(4, clientManager));
                var http20 = builder.Add(BuildProtocolFlow<Http20Engine>(1, clientManager));
                var http30 = builder.Add(BuildProtocolFlow<Http30Engine>(1, clientManager));

                builder.From(partition.Out(0)).Via(http10).To(hub);
                builder.From(partition.Out(1)).Via(http11).To(hub);
                builder.From(partition.Out(2)).Via(http20).To(hub);
                builder.From(partition.Out(3)).Via(http30).To(hub);

                return new FlowShape<HttpRequestMessage, HttpResponseMessage>(partition.In, hub.Out);
            });
        }
    }

    private static IGraph<FlowShape<HttpRequestMessage, HttpResponseMessage>, NotUsed> BuildProtocolFlow<TEngine>(
        int connectionCount,
        IActorRef clientManager,
        Func<Flow<ITransportItem, (IMemoryOwner<byte>, int), NotUsed>>? transportFactory = null)
        where TEngine : IHttpProtocolEngine, new()
    {
        return GraphDsl.Create(builder =>
        {
            var balance = builder.Add(new Balance<HttpRequestMessage>(connectionCount));
            var merge = builder.Add(new Merge<HttpResponseMessage>(connectionCount));

            for (var i = 0; i < connectionCount; i++)
            {
                var tcp = transportFactory?.Invoke() ?? Flow.FromGraph(new ConnectionStage(clientManager));
                var conn = builder.Add(new TEngine().CreateFlow().Join(tcp));
                builder.From(balance.Out(i)).Via(conn).To(merge.In(i));
            }

            return new FlowShape<HttpRequestMessage, HttpResponseMessage>(balance.In, merge.Out);
        });
    }

    private static Partition<HttpRequestMessage> Router()
    {
        return new Partition<HttpRequestMessage>(4, msg
            => msg.Version switch
            {
                { Major: 3, Minor: 0 } => 3,
                { Major: 2, Minor: 0 } => 2,
                { Major: 1, Minor: 1 } => 1,
                { Major: 1, Minor: 0 } => 0
            });
    }
}
