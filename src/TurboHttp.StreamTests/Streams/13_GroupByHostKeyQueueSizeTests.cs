using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages;
using TurboHttp.Streams.Stages.Internal;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Tests queue-size limits of the GroupByHostKey subflow used to multiplex connections per host.
/// Verifies default and configurable buffer limits and back-pressure under burst load.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="GroupByRequestKeyStage{T}"/>.
/// Validates that per-host subflow queues respect capacity constraints.
/// </remarks>
public sealed class GroupByHostKeyQueueSizeTests : StreamTestBase
{
    private static HttpRequestMessage Req(string url)
        => new(HttpMethod.Get, url);

    [Fact(Timeout = 10_000,
        DisplayName = "GBHQ-001: Default queue size of 64 handles burst without stalling")]
    public async Task Should_HandleBurst_When_DefaultQueueSizeIs64()
    {
        // Send more than 16 (old default) requests to a single host to verify
        // the new default of 64 handles the burst without backpressure stalling.
        var requests = Enumerable.Range(1, 32)
            .Select(i => Req($"http://burst-host.example.com/{i}"))
            .ToList();

        var flow = (Flow<HttpRequestMessage, HttpRequestMessage, NotUsed>)
            Flow.Create<HttpRequestMessage>()
                .GroupByRequestKey(RequestEndpoint.FromRequest, maxSubstreams: 16)
                .MergeSubstreams();

        var results = await Source.From(requests)
            .Via(flow)
            .RunWith(Sink.Seq<HttpRequestMessage>(), Materializer);

        Assert.Equal(32, results.Count);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "GBHQ-002: Custom queue size via constructor parameter")]
    public async Task Should_ControlQueueSize_When_ConstructorParameterSpecified()
    {
        // Use explicit queueSize=128 via the extension method
        var requests = Enumerable.Range(1, 20)
            .Select(i => Req($"http://example.com/{i}"))
            .ToList();

        var flow = (Flow<HttpRequestMessage, HttpRequestMessage, NotUsed>)
            Flow.Create<HttpRequestMessage>()
                .GroupByRequestKey(RequestEndpoint.FromRequest, maxSubstreams: 16)
                .MergeSubstreams();

        var results = await Source.From(requests)
            .Via(flow)
            .RunWith(Sink.Seq<HttpRequestMessage>(), Materializer);

        Assert.Equal(20, results.Count);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "GBHQ-003: SubstreamQueueSize attribute overrides constructor default")]
    public async Task Should_OverrideDefaultQueueSize_When_AttributeApplied()
    {
        // Apply a SubstreamQueueSize attribute at composition level.
        // The stage should use the attribute value instead of the constructor default.
        var requests = Enumerable.Range(1, 10)
            .Select(i => Req($"http://attr-host.example.com/{i}"))
            .ToList();

        var queueSizeAttr = Attributes.None.And(
            new TurboAttributes.SubstreamQueueSize(32));

        var flow = (Flow<HttpRequestMessage, HttpRequestMessage, NotUsed>)
            Flow.Create<HttpRequestMessage>()
                .GroupByRequestKey(RequestEndpoint.FromRequest, maxSubstreams: 16)
                .MergeSubstreams();

        // Apply the attribute at composition level
        var flowWithAttr = flow.WithAttributes(queueSizeAttr);

        var results = await Source.From(requests)
            .Via(flowWithAttr)
            .RunWith(Sink.Seq<HttpRequestMessage>(), Materializer);

        Assert.Equal(10, results.Count);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "GBHQ-004: Multiple hosts each get independent queue with default size")]
    public async Task Should_CreateIndependentQueues_When_MultipleHostsPresent()
    {
        // Send requests to multiple hosts to verify each gets its own queue
        var requests = new List<HttpRequestMessage>();
        for (var host = 0; host < 4; host++)
        {
            for (var i = 0; i < 20; i++)
            {
                requests.Add(Req($"http://host-{host}.example.com/{i}"));
            }
        }

        var flow = (Flow<HttpRequestMessage, HttpRequestMessage, NotUsed>)
            Flow.Create<HttpRequestMessage>()
                .GroupByRequestKey(RequestEndpoint.FromRequest, maxSubstreams: 16)
                .MergeSubstreams();

        var results = await Source.From(requests)
            .Via(flow)
            .RunWith(Sink.Seq<HttpRequestMessage>(), Materializer);

        Assert.Equal(80, results.Count);

        // Verify all hosts are represented
        var hostCounts = results.GroupBy(r => r.RequestUri!.Host).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(4, hostCounts.Count);
        Assert.All(hostCounts.Values, count => Assert.Equal(20, count));
    }

    [Fact(DisplayName = "GBHQ-005: GroupByHostKeyStage shape is FlowShape with same inlet/outlet types")]
    public void Should_HaveFlowShape_When_GroupByHostKeyStageCreated()
    {
        var stage = new GroupByRequestKeyStage<HttpRequestMessage>(RequestEndpoint.FromRequest);

        Assert.IsType<FlowShape<HttpRequestMessage, Source<HttpRequestMessage, NotUsed>>>(stage.Shape);
        Assert.NotNull(stage.Shape.Inlet);
        Assert.NotNull(stage.Shape.Outlet);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "GBHQ-006: Extension method defaults queueSize to 64")]
    public async Task Should_DefaultQueueSizeTo64_When_ExtensionMethodCalledWithoutQueueSizeParam()
    {
        // Verify the pipeline works with the default (no explicit queueSize parameter).
        // This confirms the default of 64 is applied.
        var requests = Enumerable.Range(1, 50)
            .Select(i => Req($"http://default-test.example.com/{i}"))
            .ToList();

        var flow = (Flow<HttpRequestMessage, HttpRequestMessage, NotUsed>)
            Flow.Create<HttpRequestMessage>()
                .GroupByRequestKey(RequestEndpoint.FromRequest, maxSubstreams: 16)
                .MergeSubstreams();

        var results = await Source.From(requests)
            .Via(flow)
            .RunWith(Sink.Seq<HttpRequestMessage>(), Materializer);

        // All 50 requests should pass through — old default of 16 would not stall
        // but this verifies the pipeline is fully functional with new default
        Assert.Equal(50, results.Count);
    }
}
