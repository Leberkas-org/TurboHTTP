using System;
using System.Net.Http;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Client;
using TurboHttp.Internal;
using TurboHttp.Internal.Stages;
using TurboHttp.IO.Stages;
using TurboHttp.Streams.Stages;

namespace TurboHttp.Streams;

/// <summary>
/// Builds island 2 of the pipeline: protocol engine core + decompression.
/// <para><b>Stage ordering (invariants verified in StageOrderingTests):</b></para>
/// <list type="number">
///   <item><description>Partition → per-version BuildProtocolFlow → Merge — version-specific encode/decode.
///         ConnectionReuseStage runs inside each substream (INV-1).</description></item>
///   <item><description>DecompressionStage — RFC 9110 §8.4. Runs after decode so cached entries
///         store decompressed bodies (INV-6).</description></item>
/// </list>
/// </summary>
internal static class ProtocolCoreGraphBuilder
{
    public static IGraph<FlowShape<HttpRequestMessage, HttpResponseMessage>, NotUsed> Build(
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

            var decomp = builder.Add(new DecompressionStage());
            builder.From(hub.Out).To(decomp.Inlet);

            return new FlowShape<HttpRequestMessage, HttpResponseMessage>(partition.In, decomp.Outlet);
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
            connectionFlow = Flow.FromGraph(BuildConnectionFlow<TEngine>(
                Flow.FromGraph(new ConnectionStage(poolRouter)),
                clientOptions ?? new TurboClientOptions()));
        }

        return (Flow<HttpRequestMessage, HttpResponseMessage, NotUsed>)
            Flow.Create<HttpRequestMessage>()
                .GroupByHostKey(RequestEndpoint.FromRequest, maxSubstreams)
                .ViaSubFlow(connectionFlow)
                .MergeSubstreams();
    }

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

            // ExtractOptionsStage: first request → ConnectItem (Out1) + all requests (Out0)
            var extract = b.Add(new ExtractOptionsStage(clientOptions));

            // Concat: first the ConnectItem (In 0), then all BidiFlow transport output (In 1)
            var concat = b.Add(Concat.Create<IOutputItem>(2));

            // ConnectionReuseStage: evaluates keep-alive/close after each response
            var connReuse = b.Add(new ConnectionReuseStage());

            // MergePreferred: signal feedback (preferred) + normal data (in0) → transport
            var transportMerge = b.Add(new MergePreferred<IOutputItem>(1));

            // Request path: extract splits first request into ConnectItem + request stream
            b.From(extract.Out0).To(bidi.Inlet1);
            b.From(extract.Out1).To(concat.In(0));

            // Transport path: ConnectItem + BidiFlow encoded output → concat → merge → transport → BidiFlow decode
            b.From(bidi.Outlet1).To(concat.In(1));
            b.From(concat.Out).To(transportMerge.In(0));
            b.From(transportMerge.Out).To(transportFlow.Inlet);
            b.From(transportFlow.Outlet).To(bidi.Inlet2);

            // Response path: decoded response → ConnectionReuseStage → response output
            b.From(bidi.Outlet2).To(connReuse.In);

            // Signal feedback: ConnectionReuseItem → buffer → merge preferred → ConnectionStage
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
