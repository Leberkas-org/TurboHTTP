using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams.Stages.Decoding;
using TurboHttp.Streams.Stages.Encoding;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Regression tests for HTTP/2 stage completion propagation fixes (Feature 030).
/// Each test verifies that an upstream failure terminates the downstream outlet
/// within the timeout — a hang indicates the bug has been reintroduced.
/// </summary>
/// <remarks>
/// Stages under test: <see cref="Http20DecoderStage"/>, <see cref="Http20StreamIdAllocatorStage"/>,
/// <see cref="Http20PrependPrefaceStage"/>, <see cref="Http20Request2FrameStage"/>,
/// <see cref="Http20CorrelationStage"/>, <see cref="Http20ConnectionStage"/>.
/// </remarks>
public sealed class Http2StageCompletionRegressionTests : StreamTestBase
{
    private static readonly InvalidOperationException UpstreamError = new("upstream error");

    private static IInputItem MinimalInputItem()
    {
        var bytes = new byte[] { 0x00 };
        return new DataItem(new SimpleMemoryOwner(bytes), bytes.Length) { Key = RequestEndpoint.Default };
    }

    private static IOutputItem MinimalOutputItem()
    {
        var bytes = new byte[] { 0x00 };
        return new DataItem(new SimpleMemoryOwner(bytes), bytes.Length) { Key = RequestEndpoint.Default };
    }

    // ──────────────────────────────────────────────────────────────
    // SCREG-002: Http20DecoderStage
    // ──────────────────────────────────────────────────────────────

    [Fact(Timeout = 5000,
        DisplayName = "SCREG-002: Http20DecoderStage outlet terminates on upstream failure")]
    public async Task Http20DecoderStage_Outlet_Terminates_On_Upstream_Failure()
    {
        var source = Source.From(new[] { MinimalInputItem() })
            .Concat(Source.Failed<IInputItem>(UpstreamError));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await source
                .Via(Flow.FromGraph(new Http20DecoderStage()))
                .RunWith(Sink.Seq<Http2Frame>(), Materializer));
    }

    // ──────────────────────────────────────────────────────────────
    // SCREG-003: Http20StreamIdAllocatorStage
    // ──────────────────────────────────────────────────────────────

    [Fact(Timeout = 5000,
        DisplayName = "SCREG-003: Http20StreamIdAllocatorStage outlet terminates on upstream failure")]
    public async Task Http20StreamIdAllocatorStage_Outlet_Terminates_On_Upstream_Failure()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var source = Source.From(new[] { request })
            .Concat(Source.Failed<HttpRequestMessage>(UpstreamError));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await source
                .Via(Flow.FromGraph(new Http20StreamIdAllocatorStage()))
                .RunWith(Sink.Seq<(HttpRequestMessage, int)>(), Materializer));
    }

    // ──────────────────────────────────────────────────────────────
    // SCREG-004: Http20PrependPrefaceStage
    // ──────────────────────────────────────────────────────────────

    [Fact(Timeout = 5000,
        DisplayName = "SCREG-004: Http20PrependPrefaceStage outlet terminates on upstream failure")]
    public async Task Http20PrependPrefaceStage_Outlet_Terminates_On_Upstream_Failure()
    {
        var source = Source.From(new[] { MinimalOutputItem() })
            .Concat(Source.Failed<IOutputItem>(UpstreamError));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await source
                .Via(Flow.FromGraph(new Http20PrependPrefaceStage()))
                .RunWith(Sink.Seq<IOutputItem>(), Materializer));
    }

    // ──────────────────────────────────────────────────────────────
    // SCREG-005: Http20Request2FrameStage
    // ──────────────────────────────────────────────────────────────

    [Fact(Timeout = 5000,
        DisplayName = "SCREG-005: Http20Request2FrameStage outlet terminates on upstream failure")]
    public async Task Http20Request2FrameStage_Outlet_Terminates_On_Upstream_Failure()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var source = Source.From(new[] { (request, 1) })
            .Concat(Source.Failed<(HttpRequestMessage, int)>(UpstreamError));

        var encoder = new Http2RequestEncoder();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await source
                .Via(Flow.FromGraph(new Http20Request2FrameStage(encoder)))
                .RunWith(Sink.Seq<Http2Frame>(), Materializer));
    }

    // ──────────────────────────────────────────────────────────────
    // SCREG-006: Http20CorrelationStage
    // ──────────────────────────────────────────────────────────────

    [Fact(Timeout = 5000,
        DisplayName = "SCREG-006: Http20CorrelationStage outlet terminates on upstream failure")]
    public async Task Http20CorrelationStage_Outlet_Terminates_On_Upstream_Failure()
    {
        // Fail the request inlet — the stage should propagate to the outlet.
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var requestSource = Source.From(new[] { (request, 1) })
            .Concat(Source.Failed<(HttpRequestMessage, int)>(UpstreamError));

        var graph = GraphDsl.Create(
            Sink.Seq<HttpResponseMessage>(),
            (builder, sink) =>
            {
                var stage = builder.Add(new Http20CorrelationStage());
                var reqSrc = builder.Add(requestSource);
                var respSrc = builder.Add(Source.Empty<(HttpResponseMessage, int)>());

                builder.From(reqSrc).To(stage.In0);
                builder.From(respSrc).To(stage.In1);
                builder.From(stage.Out).To(sink);

                return ClosedShape.Instance;
            });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await RunnableGraph.FromGraph(graph).Run(Materializer));
    }

    // ──────────────────────────────────────────────────────────────
    // SCREG-007: Http20ConnectionStage
    // ──────────────────────────────────────────────────────────────

    [Fact(Timeout = 5000,
        DisplayName = "SCREG-007: Http20ConnectionStage outlet terminates on upstream failure")]
    public async Task Http20ConnectionStage_Outlet_Terminates_On_Upstream_Failure()
    {
        // Fail the InServer inlet — the stage should propagate to all outlets.
        var settingsFrame = new SettingsFrame([], isAck: false);
        var serverSource = Source.From(new Http2Frame[] { settingsFrame })
            .Concat(Source.Failed<Http2Frame>(UpstreamError));

        var graph = GraphDsl.Create(
            Sink.Seq<Http2Frame>(),
            (builder, streamSink) =>
            {
                var stage = builder.Add(new Http20ConnectionStage());
                var serverSrc = builder.Add(serverSource);
                var appSrc = builder.Add(Source.Empty<Http2Frame>());
                var serverOutSink = builder.Add(Sink.Ignore<Http2Frame>());
                var signalSink = builder.Add(Sink.Ignore<IControlItem>());

                builder.From(serverSrc).To(stage.InServer);
                builder.From(stage.OutStream).To(streamSink);
                builder.From(appSrc).To(stage.InApp);
                builder.From(stage.OutServer).To(serverOutSink);
                builder.From(stage.OutSignal).To(signalSink);

                return ClosedShape.Instance;
            });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await RunnableGraph.FromGraph(graph).Run(Materializer));
    }
}
