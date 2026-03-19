using System;
using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Client;
using TurboHttp.Internal;
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
            // ---- PRE-PROCESSING ISLAND (fused island 1: lightweight request stages) ----

            var enricher = builder.Add(new RequestEnricherStage(requestOptionsFactory));
            var requestTip = enricher.Outlet;

            // Redirect merge (feedback from redirect stage in post-processing island)
            var redirectMerge = builder.Add(new MergePreferred<HttpRequestMessage>(1));
            builder.From(requestTip).To(redirectMerge.In(0));
            requestTip = redirectMerge.Out;

            // Cookie injection
            var cookieInject = builder.Add(new CookieInjectionStage(cookieJar));
            builder.From(requestTip).To(cookieInject.Inlet);
            requestTip = cookieInject.Outlet;

            // Retry merge (feedback from retry stage in post-processing island)
            var retryMerge = builder.Add(new MergePreferred<HttpRequestMessage>(1));
            builder.From(requestTip).To(retryMerge.In(0));
            requestTip = retryMerge.Out;

            // Cache lookup
            var cacheLookup = builder.Add(new CacheLookupStage(cacheStore, options.CachePolicy));
            builder.From(requestTip).To(cacheLookup.In);

            var engineRequest = cacheLookup.Out0; // cache miss
            var cacheHit = cacheLookup.Out1; // cache hit

            // ---- PROTOCOL ENGINE ISLAND (fused island 2: CPU-intensive encode/decode + decompression) ----
            // Async boundary separates this from the lightweight pre/post-processing stages,
            // allowing protocol work to run in parallel on a separate thread.

            var engineAndDecomp = builder.Add(
                Flow.FromGraph(
                        BuildEngineCoreGraph(poolRouter, options, http10Factory, http11Factory, http20Factory))
                    .Via(new DecompressionStage())
                    .WithAttributes(Attributes.CreateAsyncBoundary()));

            builder.From(engineRequest).To(engineAndDecomp.Inlet);

            // ---- POST-PROCESSING ISLAND (fused island 3: response evaluation stages) ----
            // Async boundary separates this from the protocol engine island.

            var postProcess = builder.Add(
                BuildPostProcessGraph(cookieJar, cacheStore, options)
                    .Async());

            builder.From(engineAndDecomp.Outlet).To(postProcess.ResponseIn);
            builder.From(cacheHit).To(postProcess.CacheHitIn);

            // Feedback loops: cross from post-processing island back to pre-processing island.
            // Buffer(4) breaks the cycle and allows multiple in-flight redirects/retries
            // without back-pressuring the main pipeline. MergePreferred ensures feedback
            // items are always processed before new requests from the source.
            builder.From(postProcess.RetryFeedbackOut)
                .Via(Flow.Create<HttpRequestMessage>().Buffer(4, OverflowStrategy.Backpressure))
                .To(retryMerge.Preferred);

            builder.From(postProcess.RedirectFeedbackOut)
                .Via(Flow.Create<HttpRequestMessage>().Buffer(4, OverflowStrategy.Backpressure))
                .To(redirectMerge.Preferred);

            return new FlowShape<HttpRequestMessage, HttpResponseMessage>(
                enricher.Inlet,
                postProcess.ResponseOut
            );
        }));
    }

    /// <summary>
    /// Builds the post-processing sub-graph as a single fused island.
    /// Contains: CookieStorage → CacheStorage → Retry → CacheMerge → Redirect.
    /// Exposed ports: 2 inlets (response, cache hits), 3 outlets (response, retry feedback, redirect feedback).
    /// </summary>
    private static IGraph<PostProcessShape, NotUsed> BuildPostProcessGraph(
        CookieJar cookieJar,
        HttpCacheStore cacheStore,
        TurboClientOptions options)
    {
        return GraphDsl.Create(builder =>
        {
            var cookieStorage = builder.Add(new CookieStorageStage(cookieJar));
            var cacheStorage = builder.Add(new CacheStorageStage(cacheStore));
            var retry = builder.Add(new RetryStage(options.RetryPolicy));
            var cacheMerge = builder.Add(new Merge<HttpResponseMessage>(2));
            var redirect = builder.Add(new RedirectStage(options.RedirectPolicy));

            // CookieStorage → CacheStorage → Retry
            builder.From(cookieStorage.Outlet).To(cacheStorage.Inlet);
            builder.From(cacheStorage.Outlet).To(retry.In);

            // Retry.Out0 (pass-through) → CacheMerge.In(0), CacheMerge → Redirect
            builder.From(retry.Out0).To(cacheMerge.In(0));
            builder.From(cacheMerge.Out).To(redirect.In);

            return new PostProcessShape(
                cookieStorage.Inlet,    // response input from engine+decompression
                cacheMerge.In(1),       // cache hit input from cache lookup
                redirect.Out0,          // final response output
                retry.Out1,             // retry feedback → pre-processing
                redirect.Out1);         // redirect feedback → pre-processing
        });
    }

    /// <summary>
    /// Shape for the post-processing sub-graph: 2 inlets (response, cache hits),
    /// 3 outlets (final response, retry feedback, redirect feedback).
    /// </summary>
    private sealed class PostProcessShape : Shape
    {
        public Inlet<HttpResponseMessage> ResponseIn { get; }
        public Inlet<HttpResponseMessage> CacheHitIn { get; }
        public Outlet<HttpResponseMessage> ResponseOut { get; }
        public Outlet<HttpRequestMessage> RetryFeedbackOut { get; }
        public Outlet<HttpRequestMessage> RedirectFeedbackOut { get; }

        public PostProcessShape(
            Inlet<HttpResponseMessage> responseIn,
            Inlet<HttpResponseMessage> cacheHitIn,
            Outlet<HttpResponseMessage> responseOut,
            Outlet<HttpRequestMessage> retryFeedbackOut,
            Outlet<HttpRequestMessage> redirectFeedbackOut)
        {
            ResponseIn = responseIn;
            CacheHitIn = cacheHitIn;
            ResponseOut = responseOut;
            RetryFeedbackOut = retryFeedbackOut;
            RedirectFeedbackOut = redirectFeedbackOut;
        }

        public override ImmutableArray<Inlet> Inlets =>
            [ResponseIn, CacheHitIn];

        public override ImmutableArray<Outlet> Outlets =>
            [ResponseOut, RetryFeedbackOut, RedirectFeedbackOut];

        public override Shape DeepCopy() => new PostProcessShape(
            (Inlet<HttpResponseMessage>)ResponseIn.CarbonCopy(),
            (Inlet<HttpResponseMessage>)CacheHitIn.CarbonCopy(),
            (Outlet<HttpResponseMessage>)ResponseOut.CarbonCopy(),
            (Outlet<HttpRequestMessage>)RetryFeedbackOut.CarbonCopy(),
            (Outlet<HttpRequestMessage>)RedirectFeedbackOut.CarbonCopy());

        public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
            => new PostProcessShape(
                (Inlet<HttpResponseMessage>)inlets[0],
                (Inlet<HttpResponseMessage>)inlets[1],
                (Outlet<HttpResponseMessage>)outlets[0],
                (Outlet<HttpRequestMessage>)outlets[1],
                (Outlet<HttpRequestMessage>)outlets[2]);
    }

    private static IGraph<FlowShape<HttpRequestMessage, HttpResponseMessage>, NotUsed> BuildEngineCoreGraph(
        IActorRef poolRouter,
        TurboClientOptions clientOptions,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http10Factory = null,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http11Factory = null,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http20Factory = null,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http30Factory = null)
    {
        return GraphDsl.Create(builder =>
        {
            var partition = builder.Add(Router());
            var hub = builder.Add(new Merge<HttpResponseMessage>(4));

            // Encoder/decoder stage groups get larger input buffers for throughput.
            // Lightweight stages (cookie, cache, enricher) inherit the smaller global default (4/16).
            var highThroughputBuffer = Attributes.CreateInputBuffer(16, 64);

            var http10 =
                builder.Add(BuildProtocolFlow<Http10Engine>(256, poolRouter, http10Factory, clientOptions)
                    .WithAttributes(highThroughputBuffer));
            var http11 =
                builder.Add(BuildProtocolFlow<Http11Engine>(256, poolRouter, http11Factory, clientOptions)
                    .WithAttributes(highThroughputBuffer));
            var http20 =
                builder.Add(BuildProtocolFlow<Http20Engine>(64, poolRouter, http20Factory, clientOptions)
                    .WithAttributes(highThroughputBuffer));
            var http30 =
                builder.Add(BuildProtocolFlow<Http30Engine>(32, poolRouter, http30Factory, clientOptions)
                    .WithAttributes(highThroughputBuffer));

            builder.From(partition.Out(0)).Via(http10).To(hub);
            builder.From(partition.Out(1)).Via(http11).To(hub);
            builder.From(partition.Out(2)).Via(http20).To(hub);
            builder.From(partition.Out(3)).Via(http30).To(hub);

            return new FlowShape<HttpRequestMessage, HttpResponseMessage>(partition.In, hub.Out);
        });
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
                .GroupByHostKey(RequestEndpoint.FromRequest, maxSubstreams)
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

            // ExtractOptionsStage: first request → ConnectItem (Out0) + all requests (Out1)
            // Replaces Broadcast + Buffer + Take(1).Select pattern.
            // The stage internally buffers the first request and delivers it via Out1 on demand,
            // eliminating the deadlock that required the explicit Buffer(1) decoupling.
            var extract = b.Add(new ExtractOptionsStage(clientOptions));

            // Concat: first the ConnectItem (In 0), then all BidiFlow transport output (In 1)
            var concat = b.Add(Concat.Create<IOutputItem>(2));

            // ConnectionReuseStage: evaluates keep-alive/close after each response
            var connReuse = b.Add(new ConnectionReuseStage());

            // MergePreferred: signal feedback (preferred) + normal data (in0) → transport
            // Same cycle-breaking pattern used by retry/redirect stages in BuildExtendedPipeline.
            var transportMerge = b.Add(new MergePreferred<IOutputItem>(1));

            // Request path: extract splits first request into ConnectItem + request stream
            b.From(extract.Out0).To(bidi.Inlet1);
            b.From(extract.Out1).To(concat.In(0));

            // Transport path: BidiFlow encoded output + ConnectItem → merge → transport → BidiFlow decode
            b.From(bidi.Outlet1).To(concat.In(1));
            b.From(concat.Out).To(transportMerge.In(0));
            b.From(transportMerge.Out).To(transportFlow.Inlet);
            b.From(transportFlow.Outlet).To(bidi.Inlet2);

            // Response path: decoded response → ConnectionReuseStage → response output
            b.From(bidi.Outlet2).To(connReuse.In);

            // Signal feedback: ConnectionReuseItem → buffer → merge preferred → ConnectionStage
            // ConnectionStage handles CanReuse=false by telling HostPoolActor via ConnectionActor.
            b.From(connReuse.Out1)
                .Via(Flow.Create<IControlItem>().Select(IOutputItem (x) => x)
                    .Buffer(1, OverflowStrategy.Backpressure))
                .To(transportMerge.Preferred);

            return new FlowShape<HttpRequestMessage, HttpResponseMessage>(
                extract.In, connReuse.Out0);
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