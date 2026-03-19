using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http20;

public sealed class Http20ConnectionStageStreamAcquireTests : StreamTestBase
{
    /// <summary>
    /// Runs the Http20ConnectionStage with request frames on AppIn and a
    /// harmless SETTINGS ACK on ServerIn to allow the stage to complete.
    /// Returns (server-bound frames from ServerOut, signal items from OutletSignal).
    /// </summary>
    private async Task<(IReadOnlyList<Http2Frame> ServerBound, IReadOnlyList<IControlItem> Signals)> RunWithRequestsAsync(
        params Http2Frame[] requestFrames)
    {
        var serverBoundSink = Sink.Seq<Http2Frame>();
        var signalSeqSink = Sink.Seq<IControlItem>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(serverBoundSink, signalSeqSink,
                (m1, m2) => (m1, m2),
                (b, sbSink, sigSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage());

                    // A SETTINGS ACK on ServerIn is harmless (no ACK reply) and lets
                    // the inlet complete, which tears down the stage via the default
                    // onUpstreamFinish on _inletRaw.
                    var serverSource = b.Add(
                        Source.Single<Http2Frame>(new SettingsFrame([], isAck: true))
                            .InitialDelay(TimeSpan.FromMilliseconds(200)));

                    var requestSource = b.Add(Source.From(requestFrames));
                    var downstreamSink = b.Add(Sink.Ignore<Http2Frame>().MapMaterializedValue(_ => NotUsed.Instance));

                    b.From(serverSource).To(stage.ServerIn);
                    b.From(stage.AppOut).To(downstreamSink);
                    b.From(requestSource).To(stage.AppIn);
                    b.From(stage.ServerOut).To(sbSink);
                    b.From(stage.OutletSignal).To(sigSink);

                    return ClosedShape.Instance;
                }));

        var (serverBoundTask, signalTask) = graph.Run(Materializer);

        var serverBound = await serverBoundTask.WaitAsync(TimeSpan.FromSeconds(5));
        var signals = await signalTask.WaitAsync(TimeSpan.FromSeconds(5));

        return (serverBound, signals);
    }

    // ─── 20CS-SA-001: HeadersFrame on InletRequest → OutletSignal emits StreamAcquireItem ─

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.1-20CS-SA-001: HeadersFrame on InletRequest emits StreamAcquireItem on OutletSignal")]
    public async Task HeadersFrame_Emits_StreamAcquireItem()
    {
        var headers = new HeadersFrame(streamId: 1, headerBlock: new byte[] { 0x82 }, endHeaders: true, endStream: true);

        var (_, signals) = await RunWithRequestsAsync(headers);

        var signal = Assert.Single(signals);
        Assert.IsType<StreamAcquireItem>(signal);
    }

    // ─── 20CS-SA-002: DataFrame on InletRequest → no emission on OutletSignal ─────

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.1-20CS-SA-002: DataFrame on InletRequest does not emit on OutletSignal")]
    public async Task DataFrame_Does_Not_Emit_Signal()
    {
        var data = new DataFrame(streamId: 1, data: new byte[] { 0x01 }, endStream: true);

        var (_, signals) = await RunWithRequestsAsync(data);

        Assert.Empty(signals);
    }
}
