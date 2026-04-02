using System.Net;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Diagnostics;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages.Features;
using TurboHttp.Streams.Stages.Internal;
using TurboHttp.Streams.Stages.Routing;
using TurboHttp.Transport.Tcp;
using TurboHttp.Transport.Quic;

namespace TurboHttp.Streams;

internal sealed class Engine
{
    internal Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> CreateFlow(IActorRef connectionManager,
        TurboClientOptions? options,
        Func<TurboRequestOptions>? requestOptionsFactory,
        PipelineDescriptor descriptor)
    {
        options ??= new TurboClientOptions();
        requestOptionsFactory ??= () => BuildRequestOptions(options);

        return BuildExtendedPipeline(TcpTransport, options, requestOptionsFactory, descriptor);

        // Create protocol-specific transport stage factories
        Flow<IOutputItem, IInputItem, NotUsed> TcpTransport()
            => Flow.FromGraph(new TcpConnectionStage(connectionManager, options));
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

        return BuildExtendedPipeline(NeverUsed, new TurboClientOptions(), () => defaultOptions,
            descriptor,
            http10Factory, http11Factory, http20Factory, http30Factory);

        // Test-only: transport factories are always provided, so the transport stage factory is never called.
        Flow<IOutputItem, IInputItem, NotUsed> NeverUsed() => throw new InvalidOperationException("Unreachable");
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
    /// Composes the pipeline by wiring all active feature BidiStages and the protocol engine
    /// core into a single <see cref="GraphDsl"/> <see cref="Flow{TIn,TOut,TMat}"/>.
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
    /// <para>Stages are collected into a list (innermost first) and wired together with the engine
    /// in a single <see cref="GraphDsl"/> call — no intermediate <c>BidiFlow</c> wrapper or
    /// <c>Join</c> call, avoiding the object that iterative <c>Atop</c> stacking or a separate
    /// <c>BidiFlow.Join(engineFlow)</c> would produce.</para>
    /// </summary>
    private static Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> BuildExtendedPipeline(
        Func<Flow<IOutputItem, IInputItem, NotUsed>> transportStageFactory,
        TurboClientOptions options,
        Func<TurboRequestOptions> requestOptionsFactory,
        PipelineDescriptor descriptor,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http10Factory = null,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http11Factory = null,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http20Factory = null,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http30Factory = null)
    {
        // Protocol engine core (endpoint grouping + version routing + encode/decode).
        var engineFlow = BuildProtocolCore(transportStageFactory, options, http10Factory, http11Factory, http20Factory,
            http30Factory);

        // Collect active feature stages innermost-first.
        // Index 0 connects directly to the engine; the last index is the outermost layer.
        // Capacity: ContentEncoding + Cache + Expect100 + Retry + Cookie + Redirect + Handlers + Tracing
        var maxLayers = 6 + descriptor.Handlers.Count + 1;
        var layers = new List<IGraph<BidiShape<HttpRequestMessage, HttpRequestMessage,
            HttpResponseMessage, HttpResponseMessage>, NotUsed>>(maxLayers);

        if (descriptor.AutomaticDecompression || descriptor.CompressionPolicy is not null)
        {
            layers.Add(new ContentEncodingBidiStage(descriptor.AutomaticDecompression, descriptor.CompressionPolicy));
        }

        if (descriptor.CacheStore is not null)
        {
            layers.Add(new CacheBidiStage(descriptor.CacheStore, descriptor.CachePolicy));
        }

        if (descriptor.Expect100Policy is not null)
        {
            layers.Add(new ExpectContinueBidiStage(descriptor.Expect100Policy));
        }

        if (descriptor.RetryPolicy is not null)
        {
            layers.Add(new RetryBidiStage(descriptor.RetryPolicy));
        }

        if (descriptor.CookieJar is not null)
        {
            layers.Add(new CookieBidiStage(descriptor.CookieJar));
        }

        if (descriptor.RedirectPolicy is not null)
        {
            layers.Add(new RedirectBidiStage(descriptor.RedirectPolicy));
        }

        // Handlers added outermost-first: reverse iteration so Handlers[0] is outermost.
        for (var i = descriptor.Handlers.Count - 1; i >= 0; i--)
        {
            layers.Add(new HandlerBidiStage(descriptor.Handlers[i], i));
        }

        // Tracing is the absolute outermost layer — only when a listener is active.
        if (TurboHttpInstrumentation.IsTracingActive)
        {
            layers.Add(new TracingBidiStage());
        }

        // Inline enrichment as a Select() — avoids a separate GraphStage instance.
        // Enrichment applies BaseAddress, default version, default headers, Referer
        // sanitization, and If-Range validation per RFC 9110.
        var enricher = new RequestEnricher(requestOptionsFactory);
        var enriched = Flow.Create<HttpRequestMessage>().Select(enricher.Enrich);

        if (layers.Count == 0)
        {
            return enriched.Via(engineFlow);
        }

        // Build a single Flow via GraphDsl — feature BidiLayers and engine wired in one graph,
        // eliminating the intermediate BidiFlow wrapper that BidiFlow.Join(engineFlow) would create.
        var compositeFlow = Flow.FromGraph(
            GraphDsl.Create(engineFlow, (b, engine) =>
            {
                // Add each layer to the graph builder — plain loop avoids LINQ enumerator + extra List.
                var stages = new BidiShape<HttpRequestMessage, HttpRequestMessage,
                    HttpResponseMessage, HttpResponseMessage>[layers.Count];
                for (var i = 0; i < layers.Count; i++)
                {
                    stages[i] = b.Add(layers[i]);
                }

                // Wire request direction (outer→inner) and response direction (inner→outer).
                // stages[i+1] is outer relative to stages[i].
                for (var i = 0; i < stages.Length - 1; i++)
                {
                    b.From(stages[i + 1].Outlet1).To(stages[i].Inlet1);
                    b.From(stages[i].Outlet2).To(stages[i + 1].Inlet2);
                }

                // Connect innermost feature layer directly to the engine flow.
                b.From(stages[0].Outlet1).To(engine.Inlet);
                b.From(engine.Outlet).To(stages[0].Inlet2);

                return new FlowShape<HttpRequestMessage, HttpResponseMessage>(
                    stages[^1].Inlet1,      // outermost request inlet
                    stages[^1].Outlet2);    // outermost response outlet
            }));

        return enriched.Via(compositeFlow);
    }

    /// <summary>
    /// Builds the protocol engine core: <see cref="GroupByExtensions.GroupByRequestEndpoint{T,TMat}"/>
    /// groups by <see cref="RequestEndpoint"/> (scheme, host, port, version), then each substream
    /// uses a <see cref="VersionDispatchStage"/> that lazily materializes the correct version-specific
    /// connection flow based on the first element — no Partition/Merge overhead.
    /// </summary>
    private static Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> BuildProtocolCore(
        Func<Flow<IOutputItem, IInputItem, NotUsed>> transportStageFactory,
        TurboClientOptions clientOptions,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http10Factory = null,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http11Factory = null,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http20Factory = null,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http30Factory = null)
    {
        // Higher buffer sizes reduce backpressure signaling frequency, which lowers
        // per-element overhead in high-concurrency scenarios. The initialSize handles
        // typical burst sizes (HTTP/2 multiplexed streams); maxSize accommodates
        // sustained throughput peaks without excessive memory.
        var highThroughputBuffer = Attributes.CreateInputBuffer(64, 256);

        var versionDispatch = Flow.FromGraph(new VersionDispatchStage(CreateFlowForVersion))
            .WithAttributes(highThroughputBuffer);

        var maxConnsH1 = clientOptions.MaxH1ConnectionsPerServer;
        var maxConnsH2 = clientOptions.MaxH2ConnectionsPerServer;

        var maxConcurrentH2Streams = clientOptions.MaxH2ConcurrentStreams;

        var core = (Flow<HttpRequestMessage, HttpResponseMessage, NotUsed>)
            Flow.Create<HttpRequestMessage>()
                .GroupByRequestEndpoint(RequestEndpoint.FromRequest, maxSubstreams: clientOptions.MaxEndpointSubstreams,
                    maxSubstreamsPerKey: MaxSubstreamsPerKey,
                    maxConcurrencyPerSlot: MaxConcurrencyPerSlot)
                .ViaSubFlow(versionDispatch)
                .MergeSubstreams();

        return core.WithAttributes(highThroughputBuffer);

        Flow<IOutputItem, IInputItem, NotUsed> QuicTransport()
            => Flow.FromGraph(new QuicConnectionStage());

        int MaxConcurrencyPerSlot(RequestEndpoint endpoint)
            => endpoint.Version.Major >= 2 ? maxConcurrentH2Streams : 1;

        int MaxSubstreamsPerKey(RequestEndpoint endpoint)
            => endpoint.Version.Major >= 2 ? maxConnsH2 : maxConnsH1;

        // Version-specific flow factory — called once per substream on first element.
        // Since GroupByRequestEndpoint already groups by version, each substream
        // contains a single version — no Partition/Merge needed.
        Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> CreateFlowForVersion(Version version)
        {
            var (engineFactory, transport, testTransport) = version switch
            {
                { Major: 1, Minor: 0 } => (
                    (Func<IHttpProtocolEngine>)(() => new Http10Engine(clientOptions.MaxPipelineDepth)),
                    (Func<Flow<IOutputItem, IInputItem, NotUsed>>)TcpTransportWithOptions, http10Factory),
                { Major: 1, Minor: 1 } => (() => new Http11Engine(clientOptions.MaxPipelineDepth),
                    (Func<Flow<IOutputItem, IInputItem, NotUsed>>)TcpTransportWithOptions, http11Factory),
                { Major: 2, Minor: 0 } => ((Func<IHttpProtocolEngine>)(() => new Http20Engine()),
                    (Func<Flow<IOutputItem, IInputItem, NotUsed>>)TcpTransportWithOptions, http20Factory),
                { Major: 3, Minor: 0 } => ((Func<IHttpProtocolEngine>)(() => new Http30Engine()),
                    (Func<Flow<IOutputItem, IInputItem, NotUsed>>)QuicTransportStage, http30Factory),
                _ => throw new ArgumentOutOfRangeException(nameof(version), version,
                    $"Unsupported HTTP version: {version}")
            };

            var engine = engineFactory();

            if (testTransport is not null)
            {
                return engine.CreateFlow().Join(testTransport());
            }

            return BuildConnectionFlow(transport(), engine);

            // Transport factories that create the correct stage with clientOptions for auto-connect.
            Flow<IOutputItem, IInputItem, NotUsed> TcpTransportWithOptions()
                => transportStageFactory();

            Flow<IOutputItem, IInputItem, NotUsed> QuicTransportStage()
                => QuicTransport();
        }
    }

    /// <summary>
    /// Wires a protocol engine BidiFlow directly with a transport flow.
    /// Connection reuse evaluation is handled by <see cref="Http1XCorrelationStage"/>
    /// (for HTTP/1.x) and <see cref="Http20ConnectionStage"/> (for HTTP/2) inside the
    /// engine BidiFlow — no external ConnectionReuseStage or feedback MergePreferred needed.
    /// <para>
    /// Connection bootstrapping is handled by <see cref="TcpConnectionStage.AutoConnect"/>.
    /// </para>
    /// </summary>
    private static Flow<HttpRequestMessage, HttpResponseMessage, NotUsed>
        BuildConnectionFlow(
            Flow<IOutputItem, IInputItem, NotUsed> transport,
            IHttpProtocolEngine engine)
    {
        // Async boundary on the joined flow: the full engine+transport sub-graph
        // runs in its own sub-actor (separate from GroupBy/VersionDispatch).
        return engine.CreateFlow().Join(transport.Async());
    }
}