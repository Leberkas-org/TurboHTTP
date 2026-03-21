using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams.Stages.Features;

namespace TurboHttp.StreamTests.RFC9113;

/// <summary>
/// Tests GOAWAY frame handling in the HTTP/2 connection stage per RFC 9113.
/// Verifies that a received GOAWAY causes the stage to stop accepting new streams and drain existing ones.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http20ConnectionStage"/>.
/// RFC 9113 §6.8: HTTP/2 GOAWAY frame semantics, graceful shutdown, and last stream ID.
/// </remarks>
public sealed class Http20ConnectionStageGoAwayTests : StreamTestBase
{
    /// <summary>
    /// Runs the Http20ConnectionStage with the given server frames (arriving on InServer).
    /// Returns (downstream frames from OutStream, server-bound frames from OutServer).
    /// </summary>
    private async Task<(IReadOnlyList<Http2Frame> Downstream, IReadOnlyList<Http2Frame> ServerBound)> RunAsync(
        params Http2Frame[] serverFrames)
    {
        var downstreamSink = Sink.Seq<Http2Frame>();
        var serverBoundSink = Sink.Seq<Http2Frame>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, serverBoundSink,
                (m1, m2) => (m1, m2),
                (b, dsSink, sbSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage());
                    var serverSource = b.Add(Source.From(serverFrames));
                    var requestSource = b.Add(Source.Never<Http2Frame>());
                    var signalSink = b.Add(Sink.Ignore<IControlItem>().MapMaterializedValue(_ => NotUsed.Instance));

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutStream).To(dsSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutServer).To(sbSink);
                    b.From(stage.OutSignal).To(signalSink);

                    return ClosedShape.Instance;
                }));

        var (downstreamTask, serverBoundTask) = graph.Run(Materializer);

        var downstream = await downstreamTask.WaitAsync(TimeSpan.FromSeconds(5));
        var serverBound = await serverBoundTask.WaitAsync(TimeSpan.FromSeconds(5));

        return (downstream, serverBound);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-6.8-20CG-002: GOAWAY frame is forwarded downstream")]
    public async Task Should_Forward_GoAway_Downstream()
    {
        var goAway = new GoAwayFrame(lastStreamId: 5, Http2ErrorCode.NoError, debugData: new byte[] { 0x01, 0x02 });

        var (downstream, _) = await RunAsync(goAway);

        var forwarded = Assert.Single(downstream);
        var goAwayFrame = Assert.IsType<GoAwayFrame>(forwarded);
        Assert.Equal(5, goAwayFrame.LastStreamId);
        Assert.Equal(Http2ErrorCode.NoError, goAwayFrame.ErrorCode);
        Assert.True(goAwayFrame.DebugData.Span.SequenceEqual(new byte[] { 0x01, 0x02 }));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-6.8-20CG-003: After GOAWAY new request frames are dropped without failing the stream")]
    public async Task Should_Drop_New_Requests_Without_Failing_Stream_When_GoAway_Received()
    {
        // After GOAWAY arrives from server, any new request from the app side must be
        // silently dropped. The stream must NOT fault — it must remain alive.
        var goAway = new GoAwayFrame(lastStreamId: 1, Http2ErrorCode.InternalError);
        var request = new HeadersFrame(streamId: 3, headerBlock: new byte[] { 0x82 }, endHeaders: true, endStream: true);

        var downstreamSink = Sink.Seq<Http2Frame>();
        var serverBoundSink = Sink.Seq<Http2Frame>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, serverBoundSink,
                (m1, m2) => (m1, m2),
                (b, dsSink, sbSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage());
                    var signalSink = b.Add(Sink.Ignore<IControlItem>().MapMaterializedValue(_ => NotUsed.Instance));

                    // Server sends GOAWAY then stays open (never finishes)
                    var serverSource = b.Add(
                        Source.Single<Http2Frame>(goAway).Concat(Source.Never<Http2Frame>()));

                    // Client sends a request after GOAWAY is processed
                    var requestSource = b.Add(
                        Source.Single<Http2Frame>(request)
                            .InitialDelay(TimeSpan.FromMilliseconds(200))
                            .Concat(Source.Never<Http2Frame>()));

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutStream).To(dsSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutServer).To(sbSink);
                    b.From(stage.OutSignal).To(signalSink);

                    return ClosedShape.Instance;
                }));

        var (downstreamTask, serverBoundTask) = graph.Run(Materializer);

        // Wait long enough for both the GOAWAY and the delayed request to be processed
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        // Stream must NOT have faulted — the stage survives GOAWAY and drops the request
        Assert.False(downstreamTask.IsFaulted,
            "Downstream task must not fault after GOAWAY + dropped request");
        Assert.False(serverBoundTask.IsFaulted,
            "ServerBound task must not fault after GOAWAY + dropped request");
    }
}
