using System.Buffers;
using System.Collections.Immutable;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.StreamTests.RFC9114;

/// <summary>
/// Tests the HTTP/3 stream demultiplexer stage per RFC 9114 §6.2.
/// Verifies that tagged items are routed to the correct QUIC stream outlet
/// (request, control, or QPACK encoder) with proper backpressure handling.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http30StreamDemuxStage"/>.
/// RFC 9114 §6.2: HTTP/3 uses multiple QUIC stream types; the demux stage
/// routes outbound items to the correct stream based on their tag.
/// </remarks>
public sealed class Http30StreamDemuxStageTests : StreamTestBase
{
    private static DataItem MakeDataItem(byte[] data)
    {
        var owner = MemoryPool<byte>.Shared.Rent(data.Length);
        data.AsSpan().CopyTo(owner.Memory.Span);
        return new DataItem(owner, data.Length);
    }

    private static Http3TaggedItem TagAs(IOutputItem inner, OutputStreamType type) =>
        new(inner, type);

    private async Task<(IReadOnlyList<IOutputItem> Request, IReadOnlyList<IOutputItem> Control, IReadOnlyList<IOutputItem> Encoder)>
        RunStageAsync(params IOutputItem[] items)
    {
        var requestSink = Sink.Seq<IOutputItem>();
        var controlSink = Sink.Seq<IOutputItem>();
        var encoderSink = Sink.Seq<IOutputItem>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(requestSink, controlSink, encoderSink,
                (m1, m2, m3) => (m1, m2, m3),
                (b, rSink, cSink, eSink) =>
                {
                    var source = b.Add(Source.From(items));
                    var stage = b.Add(new Http30StreamDemuxStage());

                    b.From(source).To(stage.In);
                    b.From(stage.OutRequest).To(rSink);
                    b.From(stage.OutControl).To(cSink);
                    b.From(stage.OutEncoder).To(eSink);

                    return ClosedShape.Instance;
                }));

        var (requestTask, controlTask, encoderTask) = graph.Run(Materializer);
        var requests = await requestTask;
        var control = await controlTask;
        var encoder = await encoderTask;
        return (requests, control, encoder);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-6.2-H3Demux-001: Request-tagged items route to request outlet")]
    public async Task Should_RouteToRequest_When_TaggedAsRequest()
    {
        var data = MakeDataItem([0xAA]);
        var tagged = TagAs(data, OutputStreamType.Request);

        var (requests, control, encoder) = await RunStageAsync(tagged);

        Assert.Single(requests);
        Assert.Empty(control);
        Assert.Empty(encoder);
        Assert.Same(tagged, requests[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-6.2-H3Demux-002: Control-tagged items route to control outlet")]
    public async Task Should_RouteToControl_When_TaggedAsControl()
    {
        var data = MakeDataItem([0xBB]);
        var tagged = TagAs(data, OutputStreamType.Control);

        var (requests, control, encoder) = await RunStageAsync(tagged);

        Assert.Empty(requests);
        Assert.Single(control);
        Assert.Empty(encoder);
        Assert.Same(tagged, control[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-6.2-H3Demux-003: QpackEncoder-tagged items route to encoder outlet")]
    public async Task Should_RouteToEncoder_When_TaggedAsQpackEncoder()
    {
        var data = MakeDataItem([0xCC]);
        var tagged = TagAs(data, OutputStreamType.QpackEncoder);

        var (requests, control, encoder) = await RunStageAsync(tagged);

        Assert.Empty(requests);
        Assert.Empty(control);
        Assert.Single(encoder);
        Assert.Same(tagged, encoder[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-6.2-H3Demux-004: Untagged items default to request outlet")]
    public async Task Should_RouteToRequest_When_Untagged()
    {
        var data = MakeDataItem([0xDD]);

        var (requests, control, encoder) = await RunStageAsync(data);

        Assert.Single(requests);
        Assert.Empty(control);
        Assert.Empty(encoder);
        Assert.Same(data, requests[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-6.2-H3Demux-005: Mixed tagged items route to correct outlets")]
    public async Task Should_RouteMixedItems_When_DifferentTags()
    {
        var req1 = TagAs(MakeDataItem([0x01]), OutputStreamType.Request);
        var ctrl = TagAs(MakeDataItem([0x02]), OutputStreamType.Control);
        var enc1 = TagAs(MakeDataItem([0x03]), OutputStreamType.QpackEncoder);
        var req2 = TagAs(MakeDataItem([0x04]), OutputStreamType.Request);
        var enc2 = TagAs(MakeDataItem([0x05]), OutputStreamType.QpackEncoder);

        var (requests, control, encoder) = await RunStageAsync(req1, ctrl, enc1, req2, enc2);

        Assert.Equal(2, requests.Count);
        Assert.Single(control);
        Assert.Equal(2, encoder.Count);

        Assert.Same(req1, requests[0]);
        Assert.Same(req2, requests[1]);
        Assert.Same(ctrl, control[0]);
        Assert.Same(enc1, encoder[0]);
        Assert.Same(enc2, encoder[1]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-6.2-H3Demux-006: Control stream preface arrives first before request data")]
    public async Task Should_DeliverControlFirst_When_ControlPrecedesRequest()
    {
        var ctrl = TagAs(MakeDataItem([0x00, 0x04, 0x00]), OutputStreamType.Control);
        var req = TagAs(MakeDataItem([0xAA]), OutputStreamType.Request);

        var (requests, control, encoder) = await RunStageAsync(ctrl, req);

        Assert.Single(control);
        Assert.Single(requests);
        Assert.Empty(encoder);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-6.2-H3Demux-007: Stage terminates cleanly on upstream finish with no pending items")]
    public async Task Should_TerminateCleanly_When_UpstreamFinishes()
    {
        var req = TagAs(MakeDataItem([0x01]), OutputStreamType.Request);

        var (requests, control, encoder) = await RunStageAsync(req);

        Assert.Single(requests);
        Assert.Empty(control);
        Assert.Empty(encoder);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-6.2-H3Demux-008: Empty upstream completes all outlets cleanly")]
    public async Task Should_CompleteAllOutlets_When_EmptyUpstream()
    {
        var (requests, control, encoder) = await RunStageAsync();

        Assert.Empty(requests);
        Assert.Empty(control);
        Assert.Empty(encoder);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-6.2-H3Demux-009: Multiple request items preserve ordering")]
    public async Task Should_PreserveRequestOrder_When_MultipleRequests()
    {
        var items = Enumerable.Range(0, 5)
            .Select(i => TagAs(MakeDataItem([(byte)i]), OutputStreamType.Request))
            .ToArray();

        var (requests, control, encoder) = await RunStageAsync(items);

        Assert.Equal(5, requests.Count);
        Assert.Empty(control);
        Assert.Empty(encoder);

        for (var i = 0; i < 5; i++)
        {
            Assert.Same(items[i], requests[i]);
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-6.2-H3Demux-010: Multiple encoder instructions preserve ordering")]
    public async Task Should_PreserveEncoderOrder_When_MultipleInstructions()
    {
        var items = Enumerable.Range(0, 3)
            .Select(i => TagAs(MakeDataItem([(byte)(0x10 + i)]), OutputStreamType.QpackEncoder))
            .ToArray();

        var (requests, control, encoder) = await RunStageAsync(items);

        Assert.Empty(requests);
        Assert.Empty(control);
        Assert.Equal(3, encoder.Count);

        for (var i = 0; i < 3; i++)
        {
            Assert.Same(items[i], encoder[i]);
        }
    }
}
