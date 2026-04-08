using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages.Internal;
using TurboHTTP.Streams.Stages.Routing;

namespace TurboHTTP.Streams;

/// <summary>
/// Builds the protocol engine core: <see cref="GroupByExtensions.GroupByRequestEndpoint{T,TMat}"/>
/// groups by <see cref="RequestEndpoint"/> (scheme, host, port, version), then each substream
/// uses a <see cref="EndpointDispatchStage"/> that lazily materializes the correct version-specific
/// connection flow based on the first element — no Partition/Merge overhead.
/// </summary>
internal static class ProtocolCoreBuilder
{
    internal static Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> Build(
        TurboClientOptions clientOptions,
        TransportRegistry transports)
    {
        // Higher buffer sizes reduce backpressure signaling frequency, which lowers
        // per-element overhead in high-concurrency scenarios. The initialSize handles
        // typical burst sizes (HTTP/2 multiplexed streams); maxSize accommodates
        // sustained throughput peaks without excessive memory.
        var highThroughputBuffer = Attributes.CreateInputBuffer(64, 256);

        var maxConnsH1 = clientOptions.Http1.MaxConnectionsPerServer;
        var maxConnsH2 = clientOptions.Http2.MaxConnectionsPerServer;
        var maxConcurrentH2Streams = clientOptions.Http2.MaxConcurrentStreams;

        var endpointDispatch = Flow.FromGraph(new EndpointDispatchStage(CreateFlowForEndpoint))
            .WithAttributes(highThroughputBuffer);

        var core = (Flow<HttpRequestMessage, HttpResponseMessage, NotUsed>)
            Flow.Create<HttpRequestMessage>()
                .GroupByRequestEndpoint(RequestEndpoint.FromRequest, maxSubstreams: clientOptions.MaxEndpointSubstreams,
                    maxSubstreamsPerKey: MaxSubstreamsPerKey,
                    maxConcurrencyPerSlot: MaxConcurrencyPerSlot)
                .ViaSubFlow(endpointDispatch)
                .MergeSubstreams();

        return core.WithAttributes(highThroughputBuffer);

        int MaxConcurrencyPerSlot(RequestEndpoint endpoint)
            => endpoint.Version.Major >= 2 ? maxConcurrentH2Streams
             : endpoint.Version is { Major: 1, Minor: 0 } ? 1  // HTTP/1.0 no pipelining
             : clientOptions.Http1.MaxPipelineDepth;              // HTTP/1.1 pre-fill slots

        int MaxSubstreamsPerKey(RequestEndpoint endpoint)
            => endpoint.Version.Major >= 2 ? maxConnsH2 : maxConnsH1;

        // Endpoint-specific flow factory — called once per substream on first element.
        // Since GroupByRequestEndpoint already groups by endpoint, each substream
        // contains a single endpoint — no Partition/Merge needed.
        Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> CreateFlowForEndpoint(RequestEndpoint endpoint)
        {
            var version = endpoint.Version;
            var engine = (version switch
            {
                { Major: 1, Minor: 0 } => (IHttpProtocolEngine)new Http10Engine(clientOptions.Http1.MaxPipelineDepth),
                { Major: 1, Minor: 1 } => new Http11Engine(clientOptions.Http1.MaxPipelineDepth),
                { Major: 2, Minor: 0 } => new Http20Engine(clientOptions.Http2.InitialWindowSize, maxConcurrentH2Streams, clientOptions.Http2MaxBatchWeight),
                { Major: 3, Minor: 0 } => new Http30Engine(),
                _ => throw new ArgumentOutOfRangeException(nameof(version), version,
                    $"Unsupported HTTP version: {version}")
            });

            // Async boundary on the joined flow: the full engine+transport sub-graph
            // runs in its own sub-actor (separate from GroupBy/EndpointDispatch).
            return engine.CreateFlow().Join(transports.Get(version).Async());
        }
    }
}
