using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.Http2;
using TurboHttp.Streams.Stages.Decoding;

namespace TurboHttp.StreamTests.Http2.Connection;

/// <summary>
/// Tests GOAWAY frame handling in the HTTP/2 connection stage per RFC 9113.
/// Verifies that a received GOAWAY causes the stage to stop accepting new streams and drain existing ones.
/// </summary>
[Trait("RFC", "RFC9113-6.8")]
public sealed class Http2ConnectionGoAwaySpec : StreamTestBase
{
    private async Task<(IReadOnlyList<HttpResponseMessage> Downstream, IReadOnlyList<Http2Frame> ServerBound)> RunAsync(
        params Http2Frame[] serverFrames)
    {
        var downstreamSink = Sink.Seq<HttpResponseMessage>();
        var serverBoundSink = Sink.Seq<Http2Frame>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, serverBoundSink,
                (m1, m2) => (m1, m2),
                (b, dsSink, sbSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage());
                    var serverSource = b.Add(Source.From(serverFrames));
                    var requestSource = b.Add(Source.Never<HttpRequestMessage>());
                    var signalSink = b.Add(Sink.Ignore<IControlItem>().MapMaterializedValue(_ => NotUsed.Instance));

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutResponse).To(dsSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutServer).To(sbSink);
                    b.From(stage.OutSignal).To(signalSink);

                    return ClosedShape.Instance;
                }));

        var (downstreamTask, serverBoundTask) = graph.Run(Materializer);

        var downstream = await downstreamTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var serverBound = await serverBoundTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        return (downstream, serverBound);
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
                        Source.Single<HttpRequestMessage>(request.Item1)
                            .InitialDelay(TimeSpan.FromMilliseconds(200))
                            .Concat(Source.Never<HttpRequestMessage>()));

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutResponse).To(dsSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutServer).To(sbSink);
                    b.From(stage.OutSignal).To(signalSink);

                    return ClosedShape.Instance;
                }));

        var (downstreamTask, serverBoundTask) = graph.Run(Materializer);

        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        Assert.False(downstreamTask.IsFaulted,
            "Downstream task must not fault after GOAWAY + dropped request");
        Assert.False(serverBoundTask.IsFaulted,
            "ServerBound task must not fault after GOAWAY + dropped request");
    }
}
