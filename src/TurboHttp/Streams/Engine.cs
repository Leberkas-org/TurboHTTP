using System.Net;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages.Features;
using TurboHttp.Streams.Stages.Internal;
using TurboHttp.Streams.Stages.Routing;
using TurboHttp.Transport;

namespace TurboHttp.Streams;

internal sealed class Engine
{
    internal Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> CreateFlow(ConnectionPool pool,
        TurboClientOptions? options,
        Func<TurboRequestOptions>? requestOptionsFactory,
        PipelineDescriptor descriptor)
    {
        options ??= new TurboClientOptions();
        requestOptionsFactory ??= () => BuildRequestOptions(options);

        return BuildExtendedPipeline(pool, options, requestOptionsFactory, descriptor);
    }

    internal Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> CreateFlow(
        Func<Flow<IOutputItem, IInputItem, NotUsed>> http10Factory,
        Func<Flow<IOutputItem, IInputItem, NotUsed>> http11Factory,
        Func<Flow<IOutputItem, IInputItem, NotUsed>> http20Factory,
        Func<Flow<IOutputItem, IInputItem, NotUsed>> http30Factory,
        PipelineDescriptor descriptor)
    {
        var holder = new HttpRequestMessage();
        var defaultOptions = new TurboRequestOptions(
            BaseAddress: null,
            DefaultRequestHeaders: holder.Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrHigher,
            Timeout: TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize: 1024 * 1024);

        return BuildExtendedPipeline(null!, new TurboClientOptions(), () => defaultOptions,
            descriptor,
            http10Factory, http11Factory, http20Factory, http30Factory).Async();
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

    /// <summary>
    /// Composes the pipeline by stacking feature and handler BidiFlows via <c>Atop</c>
    /// around the protocol engine core, with <see cref="RequestEnricherStage"/> prepended
    /// outside the BidiFlow chain.
    /// <para><b>Stacking order (outermost → innermost):</b></para>
    /// <list type="number">
    ///   <item><description>TracingBidiStage — root "TurboHttp.Request" activity lifecycle</description></item>
    ///   <item><description>User Handlers — HandlerBidiStage per TurboHandler (FIFO: [0] outermost)</description></item>
    ///   <item><description>RedirectBidiStage — RFC 9110 §15.4, internal feedback loop</description></item>
    ///   <item><description>CookieBidiStage — RFC 6265 §5.3–§5.4</description></item>
    ///   <item><description>RetryBidiStage — RFC 9110 §9.2, internal feedback loop</description></item>
    ///   <item><description>ExpectContinueBidiStage — RFC 9110 §10.1.1, Expect: 100-continue</description></item>
    ///   <item><description>CacheBidiStage — RFC 9111, internal short-circuit</description></item>
    ///   <item><description>ContentEncodingBidiStage — RFC 9110 §8.4 (request compression + response decompression)</description></item>
    /// </list>
    /// <para>Request direction: Handler[0] → … → Handler[N] → Redirect → Cookie → Retry → Expect100 → Cache → ContentEncoding → Engine</para>
    /// <para>Response direction: Engine → ContentEncoding → Cache → Expect100 → Retry → Cookie → Redirect → Handler[N] → … → Handler[0]</para>
    /// <para>Only BidiFlows for non-null policies are included. When all policies are null,
    /// no handlers exist, and <see cref="PipelineDescriptor.AutomaticDecompression"/> is true,
    /// the graph is: Enricher → ContentEncodingBidi(Engine) → Output.</para>
    /// </summary>
    private static Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> BuildExtendedPipeline(
        ConnectionPool pool,
        TurboClientOptions options,
        Func<TurboRequestOptions> requestOptionsFactory,
        PipelineDescriptor descriptor,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http10Factory = null,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http11Factory = null,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http20Factory = null,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http30Factory = null)
    {
        // Protocol engine core (endpoint grouping + version routing + encode/decode).
        var engineFlow = BuildProtocolCore(pool, options, http10Factory, http11Factory, http20Factory, http30Factory);

        // Build feature BidiFlow chain via conditional Atop stacking.
        // Build from innermost to outermost so that each new layer wraps the previous.
        BidiFlow<HttpRequestMessage, HttpRequestMessage,
            HttpResponseMessage, HttpResponseMessage, NotUsed>? features = null;

        if (descriptor.AutomaticDecompression || descriptor.CompressionPolicy is not null)
        {
            var contentEncoding = BidiFlow.FromGraph(
                new ContentEncodingBidiStage(descriptor.AutomaticDecompression, descriptor.CompressionPolicy));
            features = features is not null ? contentEncoding.Atop(features) : contentEncoding;
        }

        if (descriptor.CacheStore is not null)
        {
            var cache = BidiFlow.FromGraph(new CacheBidiStage(descriptor.CacheStore, descriptor.CachePolicy));
            features = features is not null ? cache.Atop(features) : cache;
        }

        if (descriptor.Expect100Policy is not null)
        {
            var expect = BidiFlow.FromGraph(new ExpectContinueBidiStage(descriptor.Expect100Policy));
            features = features is not null ? expect.Atop(features) : expect;
        }

        if (descriptor.RetryPolicy is not null)
        {
            var retry = BidiFlow.FromGraph(new RetryBidiStage(descriptor.RetryPolicy));
            features = features is not null ? retry.Atop(features) : retry;
        }

        if (descriptor.CookieJar is not null)
        {
            var cookie = BidiFlow.FromGraph(new CookieBidiStage(descriptor.CookieJar));
            features = features is not null ? cookie.Atop(features) : cookie;
        }

        if (descriptor.RedirectPolicy is not null)
        {
            var redirect = BidiFlow.FromGraph(new RedirectBidiStage(descriptor.RedirectPolicy));
            features = features is not null ? redirect.Atop(features) : redirect;
        }

        // Stack user handlers outermost via Atop. Iterate in reverse so that
        // Handlers[0] ends up outermost (sees initial request first, final response last).
        for (var i = descriptor.Handlers.Count - 1; i >= 0; i--)
        {
            var mw = BidiFlow.FromGraph(new HandlerBidiStage(descriptor.Handlers[i], i));
            features = features is not null ? mw.Atop(features) : mw;
        }

        // Tracing is the absolute outermost layer — wraps everything including handlers.
        // Creates root "TurboHttp.Request" activity per request, completes it on response.
        var tracing = BidiFlow.FromGraph(new TracingBidiStage());
        features = features is not null ? tracing.Atop(features) : tracing;

        // Join features with engine, or use engine directly if no features.
        var pipelineFlow = features is not null
            ? features.Join(engineFlow)
            : engineFlow;

        // Prepend enricher (initial requests only — redirects/retries are handled
        // internally by their BidiStages and bypass the enricher).
        var requestPrep = Flow.Create<HttpRequestMessage>()
            .Via(Flow.FromGraph(new RequestEnricherStage(requestOptionsFactory)));

        return requestPrep.Via(pipelineFlow);
    }

    /// <summary>
    /// Builds the protocol engine core: a single <see cref="FlowHostKeyGroupByExtensions.GroupByRequestKey{T,TMat}"/>
    /// groups by <see cref="RequestEndpoint"/> (scheme, host, port, version), then each substream
    /// is routed through a version-specific connection flow via <see cref="BuildVersionRouter"/>.
    /// </summary>
    private static Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> BuildProtocolCore(
        ConnectionPool pool,
        TurboClientOptions clientOptions,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http10Factory = null,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http11Factory = null,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http20Factory = null,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http30Factory = null)
    {
        var highThroughputBuffer = Attributes.CreateInputBuffer(16, 64);

        var http10 = LazyConnectionFlow<Http10Engine>(pool, http10Factory, clientOptions, highThroughputBuffer);
        var http11 = LazyConnectionFlow<Http11Engine>(pool, http11Factory, clientOptions, highThroughputBuffer);
        var http20 = LazyConnectionFlow<Http20Engine>(pool, http20Factory, clientOptions, highThroughputBuffer);
        var http30 = LazyConnectionFlow<Http30Engine>(pool, http30Factory, clientOptions, highThroughputBuffer);

        var versionRouter = BuildVersionRouter(http10, http11, http20, http30);

        // GroupByRequestKey returns a SubFlow; MergeSubstreams closes it back into an IFlow.
        // Cast to Flow<> to apply the high-throughput input buffer attribute.
        var core = (Flow<HttpRequestMessage, HttpResponseMessage, NotUsed>)
            Flow.Create<HttpRequestMessage>()
                .GroupByRequestKey(RequestEndpoint.FromRequest, maxSubstreams: clientOptions.MaxEndpointSubstreams)
                .ViaSubFlow(versionRouter)
                .MergeSubstreams();
        
        return core.WithAttributes(highThroughputBuffer);
    }

    /// <summary>
    /// Wraps a per-version connection flow in <see cref="Flow.LazyInitAsync"/> so it is
    /// only materialised when the first request for that version arrives within a substream.
    /// </summary>
    private static Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> LazyConnectionFlow<TEngine>(
        ConnectionPool pool,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? transportFactory,
        TurboClientOptions clientOptions,
        Attributes attributes)
        where TEngine : IHttpProtocolEngine, new()
    {
        return Flow.LazyInitAsync(() =>
            {
                Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> connectionFlow;

                if (transportFactory is not null)
                {
                    // Test mode: factory provides the transport; join with engine BidiFlow.
                    connectionFlow = new TEngine().CreateFlow().Join(transportFactory());
                }
                else
                {
                    // Production mode: ConnectionStage acquires connections via ConnectionPool.
                    connectionFlow = Flow.FromGraph(BuildConnectionFlow<TEngine>(
                        Flow.FromGraph(new ConnectionStage(pool)),
                        clientOptions));
                }

                return Task.FromResult(connectionFlow);
            })
            .MapMaterializedValue(_ => NotUsed.Instance)
            .WithAttributes(attributes);
    }

    /// <summary>
    /// Constructs a graph that partitions requests by <see cref="HttpRequestMessage.Version"/>
    /// into four version-specific connection flows and merges the responses.
    /// </summary>
    private static IGraph<FlowShape<HttpRequestMessage, HttpResponseMessage>, NotUsed> BuildVersionRouter(
        Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> http10,
        Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> http11,
        Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> http20,
        Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> http30)
    {
        return GraphDsl.Create(builder =>
        {
            var partition = builder.Add(new Partition<HttpRequestMessage>(4, msg
                => msg.Version switch
                {
                    { Major: 3, Minor: 0 } => 3,
                    { Major: 2, Minor: 0 } => 2,
                    { Major: 1, Minor: 1 } => 1,
                    { Major: 1, Minor: 0 } => 0,
                    _ => throw new ArgumentOutOfRangeException(nameof(msg), msg.Version,
                        $"Unsupported HTTP version: {msg.Version}")
                }));
            var merge = builder.Add(new Merge<HttpResponseMessage>(4));

            var h10 = builder.Add(http10);
            var h11 = builder.Add(http11);
            var h20 = builder.Add(http20);
            var h30 = builder.Add(http30);

            builder.From(partition.Out(0)).Via(h10).To(merge);
            builder.From(partition.Out(1)).Via(h11).To(merge);
            builder.From(partition.Out(2)).Via(h20).To(merge);
            builder.From(partition.Out(3)).Via(h30).To(merge);

            return new FlowShape<HttpRequestMessage, HttpResponseMessage>(partition.In, merge.Out);
        });
    }

    /// <summary>
    /// Wires a protocol engine BidiFlow with a transport flow, an <see cref="ExtractOptionsStage"/>
    /// for connection bootstrapping, and a <see cref="ConnectionReuseStage"/> for keep-alive handling.
    /// </summary>
    private static IGraph<FlowShape<HttpRequestMessage, HttpResponseMessage>, NotUsed>
        BuildConnectionFlow<TEngine>(
            Flow<IOutputItem, IInputItem, NotUsed> transport,
            TurboClientOptions clientOptions)
        where TEngine : IHttpProtocolEngine, new()
    {
        return GraphDsl.Create(b =>
        {
            var bidi = b.Add(new TEngine().CreateFlow());
            var transportFlow = b.Add(transport);

            // ExtractOptionsStage: first request → ConnectItem (OutSignal) + all requests (OutRequest)
            // Feedback inlet (InReuse) receives ConnectionReuseItem to trigger reconnect for HTTP/1.0
            var extract = b.Add(new ExtractOptionsStage(clientOptions));

            // Concat: first the ConnectItem (In 0), then all BidiFlow transport output (In 1)
            var transportMerge0 = b.Add(new MergePreferred<IOutputItem>(1));

            // ConnectionReuseStage: evaluates keep-alive/close after each response
            var connReuse = b.Add(new ConnectionReuseStage());

            // MergePreferred: signal feedback (preferred) + normal data (in0) → transport
            var transportMerge = b.Add(new MergePreferred<IOutputItem>(1));

            // Request path: extract splits first request into ConnectItem + request stream
            b.From(extract.Out0).To(bidi.Inlet1);
            b.From(extract.Out1).To(transportMerge0.Preferred);

            // Transport path: ConnectItem + BidiFlow encoded output → concat → merge → transport → BidiFlow decode
            b.From(bidi.Outlet1).To(transportMerge0.In(0));
            b.From(transportMerge0.Out).To(transportMerge.In(0));
            b.From(transportMerge.Out).To(transportFlow.Inlet);
            b.From(transportFlow.Outlet).To(bidi.Inlet2);

            // Response path: decoded response → ConnectionReuseStage → response output
            b.From(bidi.Outlet2).To(connReuse.In);

            // Signal feedback: ConnectionReuseItem → broadcast → ExtractOptionsStage + ConnectionStage
            b.From(connReuse.Out1)
                .Via(Flow.Create<IControlItem>().Select(IOutputItem (x) => x).Buffer(16, OverflowStrategy.Backpressure))
                .To(transportMerge.Preferred);

            return new FlowShape<HttpRequestMessage, HttpResponseMessage>(extract.In, connReuse.Out0);
        });
    }
}