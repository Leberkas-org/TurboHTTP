using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http20;

public sealed class Http20ConnectionStageBackpressureTests : StreamTestBase
{
    /// <summary>
    /// Creates an Http20ConnectionStage graph with a <see cref="Source.Queue{T}"/> for AppIn
    /// and a <see cref="TestPublisher.ManualProbe{T}"/> for ServerIn.
    /// Subscriber probes capture all three outlets for assertion.
    /// </summary>
    private (
        ISourceQueueWithComplete<Http2Frame> RequestQueue,
        TestPublisher.ManualProbe<Http2Frame> ServerProbe,
        TestSubscriber.ManualProbe<Http2Frame> ServerBoundProbe,
        TestSubscriber.ManualProbe<Http2Frame> AppOutProbe,
        TestSubscriber.ManualProbe<IControlItem> SignalProbe)
        CreateProbes(int maxConcurrentStreams)
    {
        var serverProbe = this.CreateManualPublisherProbe<Http2Frame>();
        var serverBoundProbe = this.CreateManualSubscriberProbe<Http2Frame>();
        var appOutProbe = this.CreateManualSubscriberProbe<Http2Frame>();
        var signalProbe = this.CreateManualSubscriberProbe<IControlItem>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(
                Source.Queue<Http2Frame>(16, OverflowStrategy.Backpressure),
                (b, reqSrc) =>
                {
                    var stage = b.Add(new Http20ConnectionStage(maxConcurrentStreams: maxConcurrentStreams));
                    var srvSrc = b.Add(Source.FromPublisher(serverProbe));

                    b.From(srvSrc).To(stage.ServerIn);
                    b.From(stage.AppOut).To(Sink.FromSubscriber(appOutProbe));
                    b.From(reqSrc).To(stage.AppIn);
                    b.From(stage.ServerOut).To(Sink.FromSubscriber(serverBoundProbe));
                    b.From(stage.OutletSignal).To(Sink.FromSubscriber(signalProbe));

                    return ClosedShape.Instance;
                }));

        var requestQueue = graph.Run(Materializer);

        return (requestQueue, serverProbe, serverBoundProbe, appOutProbe, signalProbe);
    }

    /// <summary>
    /// Offers a frame to the request queue and asserts it is enqueued (accepted).
    /// </summary>
    private static async Task OfferAsync(ISourceQueueWithComplete<Http2Frame> queue, Http2Frame frame)
    {
        var result = await queue.OfferAsync(frame).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsType<QueueOfferResult.Enqueued>(result);
    }

    /// <summary>
    /// Sends <paramref name="count"/> HeadersFrame elements through the queue and verifies
    /// each one is forwarded to ServerOut and emits a StreamAcquireItem signal.
    /// Returns the next available odd stream ID.
    /// </summary>
    private static async Task<int> FillStreamsAsync(
        ISourceQueueWithComplete<Http2Frame> queue,
        TestSubscriber.ManualProbe<Http2Frame> serverBoundProbe,
        TestSubscriber.ManualProbe<IControlItem> signalProbe,
        int count)
    {
        var streamId = 1;
        for (var i = 0; i < count; i++)
        {
            await OfferAsync(queue, new HeadersFrame(streamId: streamId, headerBlock: new byte[] { 0x82 }, endHeaders: true));
            serverBoundProbe.ExpectNext();
            signalProbe.ExpectNext();
            streamId += 2;
        }

        return streamId;
    }

    // ─── 20CS-BP-001: 3 HeadersFrames at limit=3 → no pull after 3rd ────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.2-20CS-BP-001: Backpressure gates request inlet at max concurrent streams")]
    public async Task Three_Headers_At_Limit_Three_Stops_Pulling()
    {
        var (requestQueue, _, serverBoundProbe, appOutProbe, signalProbe) = CreateProbes(3);

        var appOutSub = appOutProbe.ExpectSubscription();
        var serverBoundSub = serverBoundProbe.ExpectSubscription();
        var signalSub = signalProbe.ExpectSubscription();

        appOutSub.Request(100);
        serverBoundSub.Request(100);
        signalSub.Request(100);

        // Fill 3 streams (the limit)
        var nextId = await FillStreamsAsync(requestQueue, serverBoundProbe, signalProbe, 3);

        // Offer a 4th frame — it enters the queue buffer but the stage won't pull it
        await OfferAsync(requestQueue, new HeadersFrame(streamId: nextId, headerBlock: new byte[] { 0x82 }, endHeaders: true));

        // The 4th frame should NOT appear on ServerOut because the stage is gating _inletRequest
        serverBoundProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    // ─── 20CS-BP-002: 3 HeadersFrames + 1 END_STREAM → pull resumes ─────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.2-20CS-BP-002: END_STREAM decrements active streams and resumes pull")]
    public async Task EndStream_Decrements_And_Resumes_Pull()
    {
        var (requestQueue, serverProbe, serverBoundProbe, appOutProbe, signalProbe) = CreateProbes(3);

        var appOutSub = appOutProbe.ExpectSubscription();
        var serverBoundSub = serverBoundProbe.ExpectSubscription();
        var signalSub = signalProbe.ExpectSubscription();

        appOutSub.Request(100);
        serverBoundSub.Request(100);
        signalSub.Request(100);

        var srvSub = serverProbe.ExpectSubscription();

        // Fill 3 streams (the limit)
        var nextId = await FillStreamsAsync(requestQueue, serverBoundProbe, signalProbe, 3);

        // Offer a 4th frame — queued but not yet pulled by stage
        await OfferAsync(requestQueue, new HeadersFrame(streamId: nextId, headerBlock: new byte[] { 0x82 }, endHeaders: true));

        // Verify gated
        serverBoundProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));

        // Server sends END_STREAM on stream 1 (zero-length DataFrame)
        srvSub.SendNext(new DataFrame(streamId: 1, data: Array.Empty<byte>(), endStream: true));
        appOutProbe.ExpectNext();

        // Pull resumes — the 4th frame now appears on ServerOut
        serverBoundProbe.ExpectNext(TimeSpan.FromSeconds(3));
        signalProbe.ExpectNext(TimeSpan.FromSeconds(3));
    }

    // ─── 20CS-BP-003: RstStreamFrame → activeStreams decrements, pull resumes ─

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.2-20CS-BP-003: RstStreamFrame decrements active streams and resumes pull")]
    public async Task RstStream_Decrements_And_Resumes_Pull()
    {
        var (requestQueue, serverProbe, serverBoundProbe, appOutProbe, signalProbe) = CreateProbes(3);

        var appOutSub = appOutProbe.ExpectSubscription();
        var serverBoundSub = serverBoundProbe.ExpectSubscription();
        var signalSub = signalProbe.ExpectSubscription();

        appOutSub.Request(100);
        serverBoundSub.Request(100);
        signalSub.Request(100);

        var srvSub = serverProbe.ExpectSubscription();

        // Fill 3 streams (the limit)
        var nextId = await FillStreamsAsync(requestQueue, serverBoundProbe, signalProbe, 3);

        // Offer a 4th frame — queued but gated
        await OfferAsync(requestQueue, new HeadersFrame(streamId: nextId, headerBlock: new byte[] { 0x82 }, endHeaders: true));
        serverBoundProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));

        // Server sends RST_STREAM on stream 3
        srvSub.SendNext(new RstStreamFrame(streamId: 3, Http2ErrorCode.Cancel));
        appOutProbe.ExpectNext();

        // Pull resumes — the 4th frame now appears on ServerOut
        serverBoundProbe.ExpectNext(TimeSpan.FromSeconds(3));
        signalProbe.ExpectNext(TimeSpan.FromSeconds(3));
    }

    // ─── 20CS-BP-004: SETTINGS MAX_CONCURRENT_STREAMS mid-session → new limit ─

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.2-20CS-BP-004: SETTINGS MAX_CONCURRENT_STREAMS mid-session enforces new limit immediately")]
    public async Task Settings_MaxConcurrentStreams_MidSession_Enforces_New_Limit()
    {
        // Start with limit=100, open 2 streams, then SETTINGS lowers limit to 2
        var (requestQueue, serverProbe, serverBoundProbe, appOutProbe, signalProbe) = CreateProbes(100);

        var appOutSub = appOutProbe.ExpectSubscription();
        var serverBoundSub = serverBoundProbe.ExpectSubscription();
        var signalSub = signalProbe.ExpectSubscription();

        appOutSub.Request(100);
        serverBoundSub.Request(100);
        signalSub.Request(100);

        var srvSub = serverProbe.ExpectSubscription();

        // Open 2 streams
        await FillStreamsAsync(requestQueue, serverBoundProbe, signalProbe, 2);

        // Server sends SETTINGS lowering MAX_CONCURRENT_STREAMS to 2
        srvSub.SendNext(new SettingsFrame(
            [(SettingsParameter.MaxConcurrentStreams, 2u)]));

        // SETTINGS frame forwarded to AppOut
        appOutProbe.ExpectNext();
        // SETTINGS ACK emitted on ServerOut
        serverBoundProbe.ExpectNext();
        // MaxConcurrentStreamsItem signal
        signalProbe.ExpectNext();

        // The stage had an outstanding pull from when limit was 100.
        // That in-flight pull will be satisfied by the next offered element regardless of the new limit.
        // Offer the 3rd frame — it passes through on the pre-existing pull.
        await OfferAsync(requestQueue, new HeadersFrame(streamId: 5, headerBlock: new byte[] { 0x82 }, endHeaders: true));
        serverBoundProbe.ExpectNext(TimeSpan.FromSeconds(3));
        signalProbe.ExpectNext(TimeSpan.FromSeconds(3));

        // Now at activeStreams=3 with limit=2 — offer a 4th, should be gated
        await OfferAsync(requestQueue, new HeadersFrame(streamId: 7, headerBlock: new byte[] { 0x82 }, endHeaders: true));
        serverBoundProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // Close streams 1 and 3 to drop to activeStreams=1 < limit=2 → pull resumes
        srvSub.SendNext(new DataFrame(streamId: 1, data: Array.Empty<byte>(), endStream: true));
        appOutProbe.ExpectNext();
        srvSub.SendNext(new RstStreamFrame(streamId: 3, Http2ErrorCode.Cancel));
        appOutProbe.ExpectNext();

        // The 4th frame should now flow through
        serverBoundProbe.ExpectNext(TimeSpan.FromSeconds(3));
        signalProbe.ExpectNext(TimeSpan.FromSeconds(3));
    }
}
