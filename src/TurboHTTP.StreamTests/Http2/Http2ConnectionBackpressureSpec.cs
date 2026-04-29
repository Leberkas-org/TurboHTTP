using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Tests.Shared;
using static TurboHTTP.StreamTests.Http2.Http2ConnectionTestHelper;

namespace TurboHTTP.StreamTests.Http2;

public sealed class Http2ConnectionBackpressureSpec : StreamTestBase
{
    private (
        ISourceQueueWithComplete<HttpRequestMessage> RequestQueue,
        TestPublisher.ManualProbe<ITransportInbound> ServerProbe,
        TestSubscriber.ManualProbe<ITransportOutbound> NetworkProbe,
        TestSubscriber.ManualProbe<HttpResponseMessage> AppOutProbe)
        CreateProbes(int maxConcurrentStreams)
    {
        var serverProbe = this.CreateManualPublisherProbe<ITransportInbound>();
        var networkProbe = this.CreateManualSubscriberProbe<ITransportOutbound>();
        var appOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(
                Source.Queue<HttpRequestMessage>(16, OverflowStrategy.Backpressure),
                (b, reqSrc) =>
                {
                    var stage = b.Add(new Http20ConnectionStage(new TurboClientOptions
                    { Http2 = { MaxConcurrentStreams = maxConcurrentStreams } }));
                    var srvSrc = b.Add(Source.FromPublisher(serverProbe));

                    b.From(srvSrc).To(stage.InServer);
                    b.From(stage.OutResponse).To(Sink.FromSubscriber(appOutProbe));
                    b.From(reqSrc).To(stage.InApp);
                    b.From(stage.OutNetwork).To(Sink.FromSubscriber(networkProbe));

                    return ClosedShape.Instance;
                }));

        var requestQueue = graph.Run(Materializer);

        return (requestQueue, serverProbe, networkProbe, appOutProbe);
    }

    private static async Task OfferAsync(ISourceQueueWithComplete<HttpRequestMessage> queue, HttpRequestMessage request)
    {
        var result = await queue.OfferAsync(request)
            .WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<QueueOfferResult.Enqueued>(result);
    }

    private static void ExpectRequestOutput(TestSubscriber.ManualProbe<ITransportOutbound> networkProbe,
        int expectedItems = 1)
    {
        for (var i = 0; i < expectedItems; i++)
        {
            networkProbe.ExpectNext(TestContext.Current.CancellationToken);
        }
    }

    private static async Task FillStreamsAsync(ISourceQueueWithComplete<HttpRequestMessage> queue,
        TestSubscriber.ManualProbe<ITransportOutbound> networkProbe,
        int count)
    {
        for (var i = 0; i < count; i++)
        {
            await OfferAsync(queue, new HttpRequestMessage(HttpMethod.Get, "http://example.com/"));
            if (i == 0)
            {
                // First request also emits ConnectTransport before TransportData
                networkProbe.ExpectNext(TestContext.Current.CancellationToken);
            }

            // Each request produces TransportData (frame data)
            ExpectRequestOutput(networkProbe);
        }
    }

    private static void DrainPreface(TestSubscriber.ManualProbe<ITransportOutbound> networkProbe)
    {
        var preface = networkProbe.ExpectNext(TestContext.Current.CancellationToken);
        var td = Assert.IsType<TransportData>(preface);
        td.Buffer.Dispose();
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public async Task Http2ConnectionBackpressure_should_stop_pulling_when_at_max_concurrent_streams_limit()
    {
        var (requestQueue, _, networkProbe, appOutProbe) = CreateProbes(3);

        var appOutSub = await appOutProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var networkSub = await networkProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        appOutSub.Request(100);
        networkSub.Request(100);

        DrainPreface(networkProbe);

        await FillStreamsAsync(requestQueue, networkProbe, 3);

        await OfferAsync(requestQueue, new HttpRequestMessage(HttpMethod.Get, "http://example.com/"));

        networkProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public async Task Http2ConnectionBackpressure_should_decrement_and_resume_pull_when_end_stream_received()
    {
        var (requestQueue, serverProbe, networkProbe, appOutProbe) = CreateProbes(3);

        var appOutSub = await appOutProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var networkSub = await networkProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        appOutSub.Request(100);
        networkSub.Request(100);

        DrainPreface(networkProbe);

        var srvSub = await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        await FillStreamsAsync(requestQueue, networkProbe, 3);

        await OfferAsync(requestQueue, new HttpRequestMessage(HttpMethod.Get, "http://example.com/"));

        networkProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        // DATA without prior HEADERS produces no HttpResponseMessage on OutResponse;
        // CloseStream still decrements _activeStreams and TryPullRequest resumes the gate.
        srvSub.SendNext(FramesToInput(new DataFrame(streamId: 1, data: Array.Empty<byte>(), endStream: true)));

        // After stream close: new request produces TransportData
        ExpectRequestOutput(networkProbe);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public async Task Http2ConnectionBackpressure_should_decrement_and_resume_pull_when_rst_stream_received()
    {
        var (requestQueue, serverProbe, networkProbe, appOutProbe) = CreateProbes(3);

        var appOutSub = await appOutProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var networkSub = await networkProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        appOutSub.Request(100);
        networkSub.Request(100);

        DrainPreface(networkProbe);

        var srvSub = await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        await FillStreamsAsync(requestQueue, networkProbe, 3);

        await OfferAsync(requestQueue, new HttpRequestMessage(HttpMethod.Get, "http://example.com/"));
        networkProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        srvSub.SendNext(FramesToInput(new RstStreamFrame(streamId: 3, Http2ErrorCode.Cancel)));

        // After RST_STREAM: new request produces TransportData
        ExpectRequestOutput(networkProbe);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public async Task
        Http2ConnectionBackpressure_should_enforce_new_concurrent_streams_limit_when_settings_updated_mid_session()
    {
        var (requestQueue, serverProbe, networkProbe, appOutProbe) = CreateProbes(100);

        var appOutSub = await appOutProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var networkSub = await networkProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        appOutSub.Request(100);
        networkSub.Request(100);

        DrainPreface(networkProbe);

        var srvSub = await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        await FillStreamsAsync(requestQueue, networkProbe, 2);

        srvSub.SendNext(FramesToInput(new SettingsFrame(
            [(SettingsParameter.MaxConcurrentStreams, 2u)])));

        // SETTINGS ACK (TransportData) — emitted on OutNetwork
        var settingsAck = await networkProbe.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<TransportData>(settingsAck);

        // The stage had an outstanding pull from when limit was 100.
        // That in-flight pull will be satisfied by the next offered element regardless of the new limit.
        await OfferAsync(requestQueue, new HttpRequestMessage(HttpMethod.Get, "http://example.com/"));
        ExpectRequestOutput(networkProbe);

        // Now at activeStreams=3 with limit=2 — offer a 4th, should be gated
        await OfferAsync(requestQueue, new HttpRequestMessage(HttpMethod.Get, "http://example.com/"));
        networkProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        // Close streams 1 and 3 to drop to activeStreams=1 < limit=2 → pull resumes.
        srvSub.SendNext(FramesToInput(new DataFrame(streamId: 1, data: Array.Empty<byte>(), endStream: true)));
        srvSub.SendNext(FramesToInput(new RstStreamFrame(streamId: 3, Http2ErrorCode.Cancel)));

        // After two stream closures, the gated request is released
        ExpectRequestOutput(networkProbe);
    }
}

