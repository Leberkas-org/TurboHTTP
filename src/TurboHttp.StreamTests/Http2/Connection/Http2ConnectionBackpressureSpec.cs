using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHttp.Internal;
using TurboHttp.Protocol.Http2;
using TurboHttp.Streams.Stages.Decoding;

namespace TurboHttp.StreamTests.Http2.Connection;

/// <summary>
/// Tests backpressure behaviour in the HTTP/2 connection stage per RFC 9113.
/// Verifies that the stage correctly applies flow control and does not emit frames faster than the downstream can consume.
/// </summary>
[Trait("RFC", "RFC9113-5.2")]
public sealed class Http2ConnectionBackpressureSpec : StreamTestBase
{
    private (
        ISourceQueueWithComplete<HttpRequestMessage> RequestQueue,
        TestPublisher.ManualProbe<Http2Frame> ServerProbe,
        TestSubscriber.ManualProbe<Http2Frame> ServerBoundProbe,
        TestSubscriber.ManualProbe<HttpResponseMessage> AppOutProbe,
        TestSubscriber.ManualProbe<IControlItem> SignalProbe)
        CreateProbes(int maxConcurrentStreams)
    {
        var serverProbe = this.CreateManualPublisherProbe<Http2Frame>();
        var serverBoundProbe = this.CreateManualSubscriberProbe<Http2Frame>();
        var appOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();
        var signalProbe = this.CreateManualSubscriberProbe<IControlItem>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(
                Source.Queue<HttpRequestMessage>(16, OverflowStrategy.Backpressure),
                (b, reqSrc) =>
                {
                    var stage = b.Add(new Http20ConnectionStage(maxConcurrentStreams: maxConcurrentStreams));
                    var srvSrc = b.Add(Source.FromPublisher(serverProbe));

                    b.From(srvSrc).To(stage.InServer);
                    b.From(stage.OutResponse).To(Sink.FromSubscriber(appOutProbe));
                    b.From(reqSrc).To(stage.InApp);
                    b.From(stage.OutServer).To(Sink.FromSubscriber(serverBoundProbe));
                    b.From(stage.OutSignal).To(Sink.FromSubscriber(signalProbe));

                    return ClosedShape.Instance;
                }));

        var requestQueue = graph.Run(Materializer);

        return (requestQueue, serverProbe, serverBoundProbe, appOutProbe, signalProbe);
    }

    private static async Task OfferAsync(ISourceQueueWithComplete<HttpRequestMessage> queue, HttpRequestMessage request)
    {
        var result = await queue.OfferAsync(request).WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<QueueOfferResult.Enqueued>(result);
    }

    private static async Task<int> FillStreamsAsync(
        ISourceQueueWithComplete<HttpRequestMessage> queue,
        TestSubscriber.ManualProbe<Http2Frame> serverBoundProbe,
        TestSubscriber.ManualProbe<IControlItem> signalProbe,
        int count)
    {
        var streamId = 1;
        for (var i = 0; i < count; i++)
        {
            await OfferAsync(queue, new HttpRequestMessage(HttpMethod.Get, "http://example.com/"));
            serverBoundProbe.ExpectNext(TestContext.Current.CancellationToken);
            signalProbe.ExpectNext(TestContext.Current.CancellationToken);
            streamId += 2;
        }

        return streamId;
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public async Task Http2ConnectionBackpressure_should_stop_pulling_when_at_max_concurrent_streams_limit()
    {
        var (requestQueue, _, serverBoundProbe, appOutProbe, signalProbe) = CreateProbes(3);

        var appOutSub = appOutProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var serverBoundSub = serverBoundProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var signalSub = signalProbe.ExpectSubscription(TestContext.Current.CancellationToken);

        appOutSub.Request(100);
        serverBoundSub.Request(100);
        signalSub.Request(100);

        await FillStreamsAsync(requestQueue, serverBoundProbe, signalProbe, 3);

        await OfferAsync(requestQueue, new HttpRequestMessage(HttpMethod.Get, "http://example.com/"));

        serverBoundProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public async Task Http2ConnectionBackpressure_should_decrement_and_resume_pull_when_end_stream_received()
    {
        var (requestQueue, serverProbe, serverBoundProbe, appOutProbe, signalProbe) = CreateProbes(3);

        var appOutSub = appOutProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var serverBoundSub = serverBoundProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var signalSub = signalProbe.ExpectSubscription(TestContext.Current.CancellationToken);

        appOutSub.Request(100);
        serverBoundSub.Request(100);
        signalSub.Request(100);

        var srvSub = serverProbe.ExpectSubscription(TestContext.Current.CancellationToken);

        await FillStreamsAsync(requestQueue, serverBoundProbe, signalProbe, 3);

        await OfferAsync(requestQueue, new HttpRequestMessage(HttpMethod.Get, "http://example.com/"));

        serverBoundProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        // DATA without prior HEADERS produces no HttpResponseMessage on OutResponse;
        // CloseStream still decrements _activeStreams and TryPullRequest resumes the gate.
        srvSub.SendNext(new DataFrame(streamId: 1, data: Array.Empty<byte>(), endStream: true));

        serverBoundProbe.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        signalProbe.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public async Task Http2ConnectionBackpressure_should_decrement_and_resume_pull_when_rst_stream_received()
    {
        var (requestQueue, serverProbe, serverBoundProbe, appOutProbe, signalProbe) = CreateProbes(3);

        var appOutSub = appOutProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var serverBoundSub = serverBoundProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var signalSub = signalProbe.ExpectSubscription(TestContext.Current.CancellationToken);

        appOutSub.Request(100);
        serverBoundSub.Request(100);
        signalSub.Request(100);

        var srvSub = serverProbe.ExpectSubscription(TestContext.Current.CancellationToken);

        await FillStreamsAsync(requestQueue, serverBoundProbe, signalProbe, 3);

        await OfferAsync(requestQueue, new HttpRequestMessage(HttpMethod.Get, "http://example.com/"));
        serverBoundProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        srvSub.SendNext(new RstStreamFrame(streamId: 3, Http2ErrorCode.Cancel));

        serverBoundProbe.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        signalProbe.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public async Task Http2ConnectionBackpressure_should_enforce_new_concurrent_streams_limit_when_settings_updated_mid_session()
    {
        var (requestQueue, serverProbe, serverBoundProbe, appOutProbe, signalProbe) = CreateProbes(100);

        var appOutSub = appOutProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var serverBoundSub = serverBoundProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var signalSub = signalProbe.ExpectSubscription(TestContext.Current.CancellationToken);

        appOutSub.Request(100);
        serverBoundSub.Request(100);
        signalSub.Request(100);

        var srvSub = serverProbe.ExpectSubscription(TestContext.Current.CancellationToken);

        await FillStreamsAsync(requestQueue, serverBoundProbe, signalProbe, 2);

        srvSub.SendNext(new SettingsFrame(
            [(SettingsParameter.MaxConcurrentStreams, 2u)]));

        // SETTINGS ACK emitted on OutServer
        serverBoundProbe.ExpectNext(TestContext.Current.CancellationToken);
        // MaxConcurrentStreamsItem signal
        signalProbe.ExpectNext(TestContext.Current.CancellationToken);

        // The stage had an outstanding pull from when limit was 100.
        // That in-flight pull will be satisfied by the next offered element regardless of the new limit.
        await OfferAsync(requestQueue, new HttpRequestMessage(HttpMethod.Get, "http://example.com/"));
        serverBoundProbe.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        signalProbe.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        // Now at activeStreams=3 with limit=2 — offer a 4th, should be gated
        await OfferAsync(requestQueue, new HttpRequestMessage(HttpMethod.Get, "http://example.com/"));
        serverBoundProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        // Close streams 1 and 3 to drop to activeStreams=1 < limit=2 → pull resumes.
        srvSub.SendNext(new DataFrame(streamId: 1, data: Array.Empty<byte>(), endStream: true));
        srvSub.SendNext(new RstStreamFrame(streamId: 3, Http2ErrorCode.Cancel));

        serverBoundProbe.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        signalProbe.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
    }
}
