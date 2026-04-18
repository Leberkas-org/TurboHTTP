using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Tests.Shared;
using static TurboHTTP.StreamTests.Http2.Http2ConnectionTestHelper;

namespace TurboHTTP.StreamTests.Http2;

public sealed class Http2ConnectionGoAwaySpec : StreamTestBase
{
    private async Task<(IReadOnlyList<HttpResponseMessage> Downstream, IReadOnlyList<Http2Frame> ServerBound)> RunAsync(
        params Http2Frame[] serverFrames)
    {
        var downstreamSink = Sink.Seq<HttpResponseMessage>();
        var networkSink = Sink.Seq<IOutputItem>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, networkSink,
                (m1, m2) => (m1, m2),
                (b, dsSink, nwSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage(new Http2Options().ToEngineOptions()));
                    var serverSource = b.Add(Source.From(FramesToInputs(serverFrames)));
                    var requestSource = b.Add(Source.Never<HttpRequestMessage>());

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutResponse).To(dsSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutNetwork).To(nwSink);

                    return ClosedShape.Instance;
                }));

        var mat = graph.Run(Materializer);
        var (downstreamTask, networkTask) = (mat.m1, mat.m2);

        var downstream = await downstreamTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var networkItems = await networkTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        return (downstream, DecodeFrames(networkItems, skipPreface: true));
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2ConnectionGoAway_should_not_forward_goaway_to_out_response()
    {
        var goAway = new GoAwayFrame(lastStreamId: 5, Http2ErrorCode.NoError, debugData: new byte[] { 0x01, 0x02 });

        var (downstream, serverBound) = await RunAsync(goAway);

        Assert.Empty(downstream);
        Assert.Empty(serverBound);
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2ConnectionGoAway_should_drop_new_requests_without_failing_stream_when_goaway_received()
    {
        var goAway = new GoAwayFrame(lastStreamId: 1, Http2ErrorCode.InternalError);
        var request = (new HttpRequestMessage(HttpMethod.Get, "http://example.com/"), 3);

        var downstreamSink = Sink.Seq<HttpResponseMessage>();
        var networkSink = Sink.Seq<IOutputItem>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, networkSink,
                (m1, m2) => (m1, m2),
                (b, dsSink, nwSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage(new Http2Options().ToEngineOptions()));

                    // Server sends GOAWAY then stays open (never finishes)
                    var serverSource = b.Add(
                        Source.From(FramesToInputs([goAway])).Concat(Source.Never<IInputItem>()));

                    // Client sends a request after GOAWAY is processed
                    var requestSource = b.Add(
                        Source.Single<HttpRequestMessage>(request.Item1)
                            .InitialDelay(TimeSpan.FromMilliseconds(200))
                            .Concat(Source.Never<HttpRequestMessage>()));

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutResponse).To(dsSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutNetwork).To(nwSink);

                    return ClosedShape.Instance;
                }));

        var mat = graph.Run(Materializer);
        var (downstreamTask, networkTask) = (mat.m1, mat.m2);

        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        Assert.False(downstreamTask.IsFaulted,
            "Downstream task must not fault after GOAWAY + dropped request");
        Assert.False(networkTask.IsFaulted,
            "Network task must not fault after GOAWAY + dropped request");
    }
}