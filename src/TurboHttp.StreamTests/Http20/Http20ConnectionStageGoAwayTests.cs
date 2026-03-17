using System.Collections.Immutable;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http20;

public sealed class Http20ConnectionStageGoAwayTests : StreamTestBase
{
    /// <summary>
    /// Runs the Http20ConnectionStage with the given server frames (arriving on ServerIn).
    /// Returns (downstream frames from AppOut, server-bound frames from ServerOut).
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

                    b.From(serverSource).To(stage.ServerIn);
                    b.From(stage.AppOut).To(dsSink);
                    b.From(requestSource).To(stage.AppIn);
                    b.From(stage.ServerOut).To(sbSink);
                    b.From(stage.OutletSignal).To(signalSink);

                    return ClosedShape.Instance;
                }));

        var (downstreamTask, serverBoundTask) = graph.Run(Materializer);

        var downstream = await downstreamTask.WaitAsync(TimeSpan.FromSeconds(5));
        var serverBound = await serverBoundTask.WaitAsync(TimeSpan.FromSeconds(5));

        return (downstream, serverBound);
    }

    /// <summary>
    /// Runs the Http20ConnectionStage where a GOAWAY arrives from the server,
    /// then a client request frame is pushed. Expects the stage to fail.
    /// </summary>
    private RunnableGraph<(Task<IImmutableList<Http2Frame>>, Task<IImmutableList<Http2Frame>>)>
        BuildGraphWithRequestAfterGoAway(Http2Frame goAwayFrame, Http2Frame requestFrame)
    {
        var downstreamSink = Sink.Seq<Http2Frame>();
        var serverBoundSink = Sink.Seq<Http2Frame>();

        return RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, serverBoundSink,
                (m1, m2) => (m1, m2),
                (b, dsSink, sbSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage());
                    var signalSink = b.Add(Sink.Ignore<IControlItem>().MapMaterializedValue(_ => NotUsed.Instance));

                    // Server sends GOAWAY then closes
                    var serverSource = b.Add(
                        Source.Single(goAwayFrame).Concat(Source.Never<Http2Frame>()));

                    // Client sends a request after a delay (to ensure GOAWAY is processed first)
                    var requestSource = b.Add(
                        Source.Single(requestFrame)
                            .InitialDelay(TimeSpan.FromMilliseconds(200))
                            .Concat(Source.Never<Http2Frame>()));

                    b.From(serverSource).To(stage.ServerIn);
                    b.From(stage.AppOut).To(dsSink);
                    b.From(requestSource).To(stage.AppIn);
                    b.From(stage.ServerOut).To(sbSink);
                    b.From(stage.OutletSignal).To(signalSink);

                    return ClosedShape.Instance;
                }));
    }

    // ─── 20CG-001: GOAWAY received → _goAwayReceived flag set ─────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.8-20CG-001: GOAWAY received sets goAwayReceived flag (verified by rejecting subsequent request)")]
    public async Task GoAway_Received_Sets_Flag()
    {
        // We verify the flag is set indirectly: after GOAWAY, pushing a request
        // must cause the stage to fail with Http2Exception.
        var goAway = new GoAwayFrame(lastStreamId: 1, Http2ErrorCode.NoError);
        var request = new HeadersFrame(streamId: 3, headerBlock: new byte[] { 0x82 }, endHeaders: true, endStream: true);

        var graph = BuildGraphWithRequestAfterGoAway(goAway, request);
        var (downstreamTask, serverBoundTask) = graph.Run(Materializer);

        // The stage should fail — either task will throw
        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await Task.WhenAll(
                downstreamTask.WaitAsync(TimeSpan.FromSeconds(5)),
                serverBoundTask.WaitAsync(TimeSpan.FromSeconds(5)));
        });

        Assert.Contains("GOAWAY", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ─── 20CG-002: GOAWAY frame forwarded downstream ──────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.8-20CG-002: GOAWAY frame is forwarded downstream")]
    public async Task GoAway_Forwarded_Downstream()
    {
        var goAway = new GoAwayFrame(lastStreamId: 5, Http2ErrorCode.NoError, debugData: [0x01, 0x02]);

        var (downstream, _) = await RunAsync(goAway);

        var forwarded = Assert.Single(downstream);
        var goAwayFrame = Assert.IsType<GoAwayFrame>(forwarded);
        Assert.Equal(5, goAwayFrame.LastStreamId);
        Assert.Equal(Http2ErrorCode.NoError, goAwayFrame.ErrorCode);
        Assert.Equal(new byte[] { 0x01, 0x02 }, goAwayFrame.DebugData);
    }

    // ─── 20CG-003: After GOAWAY → new requests rejected ──────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.8-20CG-003: After GOAWAY new requests are rejected with Http2Exception")]
    public async Task After_GoAway_New_Requests_Rejected()
    {
        var goAway = new GoAwayFrame(lastStreamId: 1, Http2ErrorCode.InternalError);
        var request = new HeadersFrame(streamId: 3, headerBlock: new byte[] { 0x82 }, endHeaders: true, endStream: true);

        var graph = BuildGraphWithRequestAfterGoAway(goAway, request);
        var (downstreamTask, serverBoundTask) = graph.Run(Materializer);

        // Both sinks should eventually fail because the stage fails
        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await Task.WhenAll(
                downstreamTask.WaitAsync(TimeSpan.FromSeconds(5)),
                serverBoundTask.WaitAsync(TimeSpan.FromSeconds(5)));
        });

        // Verify it's specifically about GOAWAY rejection
        Assert.Contains("GOAWAY", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
