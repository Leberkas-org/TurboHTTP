using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Tests <see cref="ExtractOptionsStage"/> which splits a composed HttpRequest into transport options and HttpRequestMessage.
/// Verifies that demand sequencing avoids 'Cannot pull port twice' races at graph startup.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="ExtractOptionsStage"/>.
/// Validates fan-out shape behavior and correct downstream demand sequencing.
/// </remarks>
public sealed class ExtractOptionsStageTests : StreamTestBase
{
    private static HttpRequestMessage MakeRequest(string url = "http://example.com/")
        => new(HttpMethod.Get, url);

    /// <summary>
    /// Runs requests through ExtractOptionsStage with manually-sequenced demand to avoid the
    /// "Cannot pull port twice" race that occurs when two eager Sink.Seq probes demand
    /// simultaneously at graph startup.
    ///
    /// Demand sequence:
    ///   1. Request 1 from Out0 → stage pulls In → source pushes req[0]
    ///      → stage sets _pending and pushes ConnectItem to Out0, then completes Out0.
    ///   2. Request N from Out1 → _pending != null → stage pushes req[0] first,
    ///      then pulls In for each subsequent element.
    /// </summary>
    private async Task<(IReadOnlyList<IOutputItem> signals, IReadOnlyList<HttpRequestMessage> messages)>
        RunStageAsync(IEnumerable<HttpRequestMessage> requests)
    {
        var requestList = requests.ToList();

        var probe0 = this.CreateManualSubscriberProbe<IOutputItem>();
        var probe1 = this.CreateManualSubscriberProbe<HttpRequestMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var stage = b.Add(new ExtractOptionsStage());
            // Concat Never: prevents the source from completing before Out1 can deliver _pending.
            // A single-element completing source fires CompleteStage() synchronously in the same
            // interpreter turn as the first push, so Out1 never sees its stashed request.
            var src = b.Add(Source.From(requestList).Concat(Source.Never<HttpRequestMessage>()));

            b.From(src).To(stage.In);
            b.From(stage.Out1).To(Sink.FromSubscriber(probe0));
            b.From(stage.Out0).To(Sink.FromSubscriber(probe1));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var sub0 = await probe0.ExpectSubscriptionAsync(CancellationToken.None);
        var sub1 = await probe1.ExpectSubscriptionAsync(CancellationToken.None);

        var signals = new List<IOutputItem>();
        var messages = new List<HttpRequestMessage>();

        if (requestList.Count > 0)
        {
            // Step 1: give Out0 exactly 1 demand.
            // Out0.onPull → Pull(In) → source pushes req[0]
            // → stage: !_initialSent → _pending = req[0], _initialSent = true, Push(Out0, ConnectItem), Complete(Out0)
            sub0.Request(1);
            signals.Add(await probe0.ExpectNextAsync(CancellationToken.None));

            // Step 2: give Out1 all N demands.
            // Out1.onPull → _pending != null → Push(Out1, req[0])
            // → for each remaining demand: Pull(In) → source pushes → Push(Out1, msg)
            sub1.Request(requestList.Count);
            for (var i = 0; i < requestList.Count; i++)
            {
                messages.Add(await probe1.ExpectNextAsync(CancellationToken.None));
            }
        }

        return (signals, messages);
    }

    [Fact(Timeout = 10_000, DisplayName = "EXT-001: First request → Out0 emits ConnectItem, Out1 emits HttpRequestMessage")]
    public async Task Should_EmitConnectItemAndRequestMessage_When_FirstRequestArrives()
    {
        var req = MakeRequest();

        var (signals, messages) = await RunStageAsync([req]);

        Assert.Single(signals);
        Assert.IsType<ConnectItem>(signals[0]);
        Assert.Single(messages);
        Assert.Same(req, messages[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "EXT-002: Second request → only Out1 emits (no repeated signal)")]
    public async Task Should_NotEmitConnectItemAgain_When_SecondRequestArrives()
    {
        var req1 = MakeRequest("http://example.com/1");
        var req2 = MakeRequest("http://example.com/2");

        var (signals, messages) = await RunStageAsync([req1, req2]);

        Assert.Single(signals);
        Assert.Equal(2, messages.Count);
    }

    [Fact(Timeout = 10_000, DisplayName = "EXT-003: 5 requests → exactly 1× Out0, 5× Out1")]
    public async Task Should_EmitOneSignalAndFiveMessages_When_FiveRequestsArriveInSequence()
    {
        var requests = Enumerable.Range(1, 5)
            .Select(i => MakeRequest($"http://example.com/{i}"))
            .ToArray();

        var (signals, messages) = await RunStageAsync(requests);

        Assert.Single(signals);
        Assert.Equal(5, messages.Count);
    }

    [Fact(Timeout = 10_000, DisplayName = "EXT-004: ConnectItem extracted only on very first request (_initialSent flag)")]
    public async Task Should_ExtractConnectItemOnlyOnce_When_MultipleRequestsArrive()
    {
        var requests = Enumerable.Range(1, 5)
            .Select(i => MakeRequest($"http://example.com/{i}"))
            .ToArray();

        var (signals, _) = await RunStageAsync(requests);

        Assert.Single(signals);
        var connectItem = Assert.IsType<ConnectItem>(signals[0]);
        Assert.NotNull(connectItem.Options);
    }

    [Fact(Timeout = 10_000, DisplayName = "EXT-005: UpstreamFinish → stage terminates cleanly")]
    public async Task Should_TerminateCleanly_When_UpstreamFinishes()
    {
        // Uses a completing source (no Concat Never) so that after all 3 requests are consumed
        // the source completes → onUpstreamFinish → CompleteStage() → both outlets complete.
        var requests = Enumerable.Range(1, 3)
            .Select(i => MakeRequest($"http://example.com/{i}"))
            .ToArray();

        var probe0 = this.CreateManualSubscriberProbe<IOutputItem>();
        var probe1 = this.CreateManualSubscriberProbe<HttpRequestMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var stage = b.Add(new ExtractOptionsStage());
            var src = b.Add(Source.From(requests)); // completing source — intentional

            b.From(src).To(stage.In);
            b.From(stage.Out1).To(Sink.FromSubscriber(probe0));
            b.From(stage.Out0).To(Sink.FromSubscriber(probe1));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var sub0 = await probe0.ExpectSubscriptionAsync(CancellationToken.None);
        var sub1 = await probe1.ExpectSubscriptionAsync(CancellationToken.None);

        sub0.Request(1);
        await probe0.ExpectNextAsync(CancellationToken.None); // ConnectItem

        sub1.Request(3);
        for (var i = 0; i < 3; i++)
        {
            await probe1.ExpectNextAsync(CancellationToken.None);
        }

        // Out0 is completed immediately after the first push.
        await probe0.ExpectCompleteAsync(CancellationToken.None);
        // Source exhausted + _pending delivered → CompleteStage() → Out1 completes.
        await probe1.ExpectCompleteAsync(CancellationToken.None);
    }

    [Fact(Timeout = 10_000, DisplayName = "EXT-006: Pending request after ConnectItem correctly delivered via Out1")]
    public async Task Should_DeliverPendingRequest_When_ConnectItemEmittedFirst()
    {
        var req = MakeRequest("http://example.com/pending");

        var (signals, messages) = await RunStageAsync([req]);

        Assert.Single(signals);
        Assert.IsType<ConnectItem>(signals[0]);
        Assert.Single(messages);
        Assert.Same(req, messages[0]);
    }
}
