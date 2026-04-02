using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.Http3;
using TurboHttp.Streams.Stages.Decoding;
using TurboHttp.Streams.Stages.Encoding;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Regression tests for HTTP/3 stage completion propagation fixes (Feature 030).
/// Each test verifies that an upstream failure terminates the downstream outlet
/// within the timeout — a hang indicates the bug has been reintroduced.
/// </summary>
/// <remarks>
/// Stages under test: <see cref="Http30DecoderStage"/>, <see cref="Http30StreamStage"/>,
/// <see cref="Http30ControlStreamPrefaceStage"/>, <see cref="Http30QpackEncoderPrefaceStage"/>,
/// <see cref="Http30Request2FrameStage"/>, <see cref="Http30CorrelationStage"/>,
/// <see cref="Http30ConnectionStage"/>.
/// </remarks>
public sealed class Http3StageCompletionRegressionSpec : StreamTestBase
{
    private static readonly InvalidOperationException UpstreamError = new("upstream error");

    private static IInputItem MinimalInputItem()
    {
        var bytes = new byte[] { 0x00 };
        return NetworkBuffer.FromArray(bytes);
    }

    private static IOutputItem MinimalOutputItem()
    {
        var bytes = new byte[] { 0x00 };
        return NetworkBuffer.FromArray(bytes);
    }

    [Fact(Timeout = 5000)]
    public async Task Http30DecoderStage_outlet_should_terminate_when_upstream_fails()
    {
        var source = Source.From(new[] { MinimalInputItem() })
            .Concat(Source.Failed<IInputItem>(UpstreamError));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await source
                .Via(Flow.FromGraph(new Http30DecoderStage()))
                .RunWith(Sink.Seq<Http3Frame>(), Materializer));
    }

    [Fact(Timeout = 5000)]
    public async Task Http30StreamStage_outlet_should_terminate_when_upstream_fails()
    {
        // Provide a minimal HEADERS frame then fail.
        var headersFrame = new Http3HeadersFrame(new byte[] { 0x00, 0x00, 0xD1 });
        var source = Source.From(new Http3Frame[] { headersFrame })
            .Concat(Source.Failed<Http3Frame>(UpstreamError));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await source
                .Via(Flow.FromGraph(new Http30StreamStage()))
                .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer));
    }

    [Fact(Timeout = 5000)]
    public async Task Http30ControlStreamPrefaceStage_outlet_should_terminate_when_upstream_fails()
    {
        var source = Source.From(new[] { MinimalOutputItem() })
            .Concat(Source.Failed<IOutputItem>(UpstreamError));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await source
                .Via(Flow.FromGraph(new Http30ControlStreamPrefaceStage()))
                .RunWith(Sink.Seq<IOutputItem>(), Materializer));
    }

    [Fact(Timeout = 5000)]
    public async Task Http30QpackEncoderPrefaceStage_outlet_should_terminate_when_upstream_fails()
    {
        var source = Source.From(new[] { (ReadOnlyMemory<byte>)new byte[] { 0x01, 0x02 } })
            .Concat(Source.Failed<ReadOnlyMemory<byte>>(UpstreamError));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await source
                .Via(Flow.FromGraph(new Http30QpackEncoderPrefaceStage()))
                .RunWith(Sink.Seq<IOutputItem>(), Materializer));
    }

    [Fact(Timeout = 5000)]
    public async Task Http30Request2FrameStage_outlet_should_terminate_when_upstream_fails()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var source = Source.From(new[] { request })
            .Concat(Source.Failed<HttpRequestMessage>(UpstreamError));

        var encoder = new Http3RequestEncoder();

        var graph = GraphDsl.Create(
            Sink.Seq<Http3Frame>(),
            (builder, frameSink) =>
            {
                var stage = builder.Add(new Http30Request2FrameStage(encoder));
                var src = builder.Add(source);
                var encoderSink = builder.Add(Sink.Ignore<ReadOnlyMemory<byte>>());

                builder.From(src).To(stage.In);
                builder.From(stage.OutFrame).To(frameSink);
                builder.From(stage.OutEncoder).To(encoderSink);

                return ClosedShape.Instance;
            });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await RunnableGraph.FromGraph(graph).Run(Materializer));
    }

    [Fact(Timeout = 5000)]
    public async Task Http30CorrelationStage_outlet_should_terminate_when_upstream_fails()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var requestSource = Source.From(new[] { request })
            .Concat(Source.Failed<HttpRequestMessage>(UpstreamError));

        var graph = GraphDsl.Create(
            Sink.Seq<HttpResponseMessage>(),
            (builder, sink) =>
            {
                var stage = builder.Add(new Http30CorrelationStage());
                var reqSrc = builder.Add(requestSource);
                var respSrc = builder.Add(Source.Empty<HttpResponseMessage>());

                builder.From(reqSrc).To(stage.In0);
                builder.From(respSrc).To(stage.In1);
                builder.From(stage.Out).To(sink);

                return ClosedShape.Instance;
            });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await RunnableGraph.FromGraph(graph).Run(Materializer));
    }

    [Fact(Timeout = 5000)]
    public async Task Http30ConnectionStage_outlet_should_terminate_when_upstream_fails()
    {
        // Fail the InServer inlet — stage should propagate to all outlets.
        var settingsFrame = new Http3SettingsFrame([]);
        var serverSource = Source.From(new Http3Frame[] { settingsFrame })
            .Concat(Source.Failed<Http3Frame>(UpstreamError));

        var graph = GraphDsl.Create(
            Sink.Seq<Http3Frame>(),
            (builder, appSink) =>
            {
                var stage = builder.Add(new Http30ConnectionStage());
                var serverSrc = builder.Add(serverSource);
                var appSrc = builder.Add(Source.Empty<Http3Frame>());
                var serverOutSink = builder.Add(Sink.Ignore<Http3Frame>());

                builder.From(serverSrc).To(stage.InServer);
                builder.From(stage.OutApp).To(appSink);
                builder.From(appSrc).To(stage.InApp);
                builder.From(stage.OutServer).To(serverOutSink);

                return ClosedShape.Instance;
            });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await RunnableGraph.FromGraph(graph).Run(Materializer));
    }
}
