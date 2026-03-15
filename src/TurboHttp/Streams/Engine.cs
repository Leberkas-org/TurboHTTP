using System;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Client;
using TurboHttp.IO;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC6265;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Protocol.RFC9111;
using TurboHttp.Streams.Stages;

namespace TurboHttp.Streams;

public class Engine
{
    public Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> CreateFlow(IActorRef poolRouter,
        TurboClientOptions? options)
        => CreateFlow(poolRouter, options, requestOptionsFactory: null);

    public Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> CreateFlow(IActorRef poolRouter,
        TurboClientOptions? options,
        Func<TurboRequestOptions>? requestOptionsFactory)
    {
        options ??= new TurboClientOptions();
        var requestOptions = BuildRequestOptions(options);
        requestOptionsFactory ??= () => requestOptions;

        return BuildExtendedPipeline(poolRouter, options, requestOptionsFactory);
    }

    internal Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> CreateFlow(
        Func<Flow<IOutputItem, IInputItem, NotUsed>> http10Factory,
        Func<Flow<IOutputItem, IInputItem, NotUsed>> http11Factory,
        Func<Flow<IOutputItem, IInputItem, NotUsed>> http20Factory,
        Func<Flow<IOutputItem, IInputItem, NotUsed>> http30Factory,
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

        return BuildExtendedPipeline(ActorRefs.Nobody, options, () => defaultOptions,
            http10Factory, http11Factory, http20Factory);
    }

    private static TurboRequestOptions BuildRequestOptions(TurboClientOptions options)
    {
        var holder = new HttpRequestMessage();
        return new TurboRequestOptions(
            BaseAddress: options.BaseAddress,
            DefaultRequestVersion: holder.Version,
            DefaultRequestHeaders: holder.Headers,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrHigher,
            Timeout: TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize: 1024 * 1024);
    }

    private static Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> BuildExtendedPipeline(
        IActorRef poolRouter,
        TurboClientOptions options,
        Func<TurboRequestOptions> requestOptionsFactory,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http10Factory = null,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http11Factory = null,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http20Factory = null)
    {
        var cookieJar = new CookieJar();
        var cacheStore = new HttpCacheStore(options.CachePolicy);

        return Flow.FromGraph(GraphDsl.Create(builder =>
        {
            // ---- REQUEST CHAIN ----

            var enricher = builder.Add(new RequestEnricherStage(requestOptionsFactory));
            var requestTip = enricher.Outlet;

            // Redirect merge (feedback from redirect stage)
            var redirectMerge = builder.Add(new MergePreferred<HttpRequestMessage>(1));
            builder.From(requestTip).To(redirectMerge.In(0));
            requestTip = redirectMerge.Out;

            // Cookie injection
            var cookieInject = builder.Add(new CookieInjectionStage(cookieJar));
            builder.From(requestTip).To(cookieInject.Inlet);
            requestTip = cookieInject.Outlet;

            // Retry merge (feedback from retry stage)
            var retryMerge = builder.Add(new MergePreferred<HttpRequestMessage>(1));
            builder.From(requestTip).To(retryMerge.In(0));
            requestTip = retryMerge.Out;

            // Cache lookup
            var cacheLookup = builder.Add(new CacheLookupStage(cacheStore!, options.CachePolicy));
            builder.From(requestTip).To(cacheLookup.In);

            var engineRequest = cacheLookup.Out0; // cache miss
            var cacheHit = cacheLookup.Out1; // cache hit

            // ---- ENGINE CORE ----

            var engineCore = builder.Add(
                BuildEngineCoreGraph(poolRouter, options, http10Factory, http11Factory, http20Factory));

            builder.From(engineRequest).To(engineCore.Inlet);
            var responseTip = engineCore.Outlet;

            // ---- RESPONSE CHAIN ----

            // Decompression
            var decomp = builder.Add(new DecompressionStage());
            builder.From(responseTip).To(decomp.Inlet);
            responseTip = decomp.Outlet;

            // Cookie storage
            var cookieStorage = builder.Add(new CookieStorageStage(cookieJar));
            builder.From(responseTip).To(cookieStorage.Inlet);
            responseTip = cookieStorage.Outlet;

            // Cache storage
            var cacheStorage = builder.Add(new CacheStorageStage(cacheStore!));
            builder.From(responseTip).To(cacheStorage.Inlet);
            responseTip = cacheStorage.Outlet;

            // Retry stage
            var retry = builder.Add(new RetryStage(options.RetryPolicy));
            builder.From(responseTip).To(retry.In);

            builder.From(retry.Out1)
                .Via(Flow.Create<HttpRequestMessage>().Buffer(1, OverflowStrategy.Backpressure))
                .To(retryMerge.Preferred);

            responseTip = retry.Out0;

            // Cache merge
            var cacheMerge = builder.Add(new Merge<HttpResponseMessage>(2));
            builder.From(responseTip).To(cacheMerge.In(0));
            builder.From(cacheHit).To(cacheMerge.In(1));
            responseTip = cacheMerge.Out;

            // Redirect stage
            var redirect = builder.Add(new RedirectStage(new RedirectHandler(options.RedirectPolicy)));
            builder.From(responseTip).To(redirect.In);

            builder.From(redirect.Out1)
                .Via(Flow.Create<HttpRequestMessage>().Buffer(1, OverflowStrategy.Backpressure))
                .To(redirectMerge.Preferred);

            responseTip = redirect.Out0;

            return new FlowShape<HttpRequestMessage, HttpResponseMessage>(
                enricher.Inlet,
                responseTip
            );
        }));
    }

    private static IGraph<FlowShape<HttpRequestMessage, HttpResponseMessage>, NotUsed> BuildEngineCoreGraph(
        IActorRef poolRouter,
        TurboClientOptions clientOptions,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http10Factory,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http11Factory,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http20Factory)
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

                var http10 = builder.Add(BuildProtocolFlow<Http10Engine>(16, ActorRefs.Nobody, http10Factory));
                var http11 = builder.Add(BuildProtocolFlow<Http11Engine>(16, ActorRefs.Nobody, http11Factory!));
                var http20 = builder.Add(BuildProtocolFlow<Http20Engine>(16, ActorRefs.Nobody, http20Factory!));

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

                var http10 =
                    builder.Add(BuildProtocolFlow<Http10Engine>(256, poolRouter, clientOptions: clientOptions));
                var http11 =
                    builder.Add(BuildProtocolFlow<Http11Engine>(256, poolRouter, clientOptions: clientOptions));
                var http20 =
                    builder.Add(BuildProtocolFlow<Http20Engine>(64, poolRouter, clientOptions: clientOptions));
                var http30 =
                    builder.Add(BuildProtocolFlow<Http30Engine>(32, poolRouter, clientOptions: clientOptions));

                builder.From(partition.Out(0)).Via(http10).To(hub);
                builder.From(partition.Out(1)).Via(http11).To(hub);
                builder.From(partition.Out(2)).Via(http20).To(hub);
                builder.From(partition.Out(3)).Via(http30).To(hub);

                return new FlowShape<HttpRequestMessage, HttpResponseMessage>(partition.In, hub.Out);
            });
        }
    }

    private static IGraph<FlowShape<HttpRequestMessage, HttpResponseMessage>, NotUsed> BuildProtocolFlow<TEngine>(
        int maxSubstreams,
        IActorRef poolRouter,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? transportFactory = null,
        TurboClientOptions? clientOptions = null)
        where TEngine : IHttpProtocolEngine, new()
    {
        // One connection flow blueprint per protocol version; GroupByHostKey
        // materializes a fresh copy for each unique (host, port, scheme) substream.
        Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> connectionFlow;

        if (transportFactory is not null)
        {
            // Test mode: factory provides the transport; join with engine BidiFlow.
            connectionFlow = new TEngine().CreateFlow().Join(transportFactory());
        }
        else
        {
            // Production mode: ConnectionStage contacts PoolRouterActor for TCP refs.
            connectionFlow = Flow.FromGraph(BuildConnectionFlowPublic<TEngine>(
                Flow.FromGraph(new ConnectionStage(poolRouter)),
                clientOptions ?? new TurboClientOptions()));
        }

        return (Flow<HttpRequestMessage, HttpResponseMessage, NotUsed>)
            Flow.Create<HttpRequestMessage>()
                .GroupBy(HostKey.FromRequest, maxSubstreams)
                .ViaSubFlow(connectionFlow)
                .MergeSubstreams();
    }

    internal static IGraph<FlowShape<HttpRequestMessage, HttpResponseMessage>, NotUsed>
        BuildConnectionFlowPublic<TEngine>(
            Flow<IOutputItem, IInputItem, NotUsed> transport,
            TurboClientOptions clientOptions)
        where TEngine : IHttpProtocolEngine, new()
    {
        return GraphDsl.Create(b =>
        {
            var bidi = b.Add(new TEngine().CreateFlow());
            var transportFlow = b.Add(transport);

            var requestBCast = b.Add(new Broadcast<HttpRequestMessage>(2));

            // Extract URI from the first request and create a ConnectItem.
            // Take(1) completes after one element, causing Concat to switch to In(1).
            var connectOnce = b.Add(
                Flow.Create<HttpRequestMessage>()
                    .Take(1)
                    .Select(IOutputItem (req) =>
                        new ConnectItem(TcpOptionsFactory.Build(req.RequestUri!, clientOptions), req.Version)));

            // Concat: first the ConnectItem (In 0), then all BidiFlow transport output (In 1)
            var concat = b.Add(Concat.Create<IOutputItem>(2));

            // Buffer(1) decouples the Broadcast from the BidiFlow to prevent deadlock:
            // Broadcast waits for all outputs to pull, but Concat only pulls In(1) after In(0)
            // completes. The buffer absorbs the first request while Concat processes the ConnectItem.
            var buffer = b.Add(
                Flow.Create<HttpRequestMessage>().Buffer(1, OverflowStrategy.Backpressure));

            b.From(requestBCast.Out(0)).Via(buffer).To(bidi.Inlet1);
            b.From(requestBCast.Out(1)).Via(connectOnce).To(concat.In(0));
            b.From(bidi.Outlet1).To(concat.In(1));
            b.From(concat.Out).To(transportFlow.Inlet);
            b.From(transportFlow.Outlet).To(bidi.Inlet2);

            return new FlowShape<HttpRequestMessage, HttpResponseMessage>(
                requestBCast.In, bidi.Outlet2);
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
                { Major: 1, Minor: 0 } => 0,
                _ => throw new ArgumentOutOfRangeException(nameof(msg), msg.Version,
                    $"Unsupported HTTP version: {msg.Version}")
            });
    }
}