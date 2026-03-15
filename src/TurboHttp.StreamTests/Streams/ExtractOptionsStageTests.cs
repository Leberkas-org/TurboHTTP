using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHttp.IO.Stages;
using TurboHttp.Streams.Stages;
using HttpRequestOptions = TurboHttp.Streams.Stages.HttpRequestOptions;

namespace TurboHttp.StreamTests.Streams;

public sealed class ExtractOptionsStageTests : StreamTestBase
{
    private static RequestItem MakeRequest(string url = "http://example.com/")
    {
        var msg = new HttpRequestMessage(HttpMethod.Get, url);
        return new RequestItem(new HttpRequestOptions(), msg);
    }

    /// <summary>
    /// Runs requests through ExtractOptionsStage with manually-sequenced demand to avoid the
    /// "Cannot pull port twice" race that occurs when two eager Sink.Seq probes demand
    /// simultaneously at graph startup.
    ///
    /// Demand sequence:
    ///   1. Request 1 from Out0 → stage pulls In → source pushes req[0]
    ///      → stage sets _pending and pushes InitialInput to Out0.
    ///   2. Request N from Out1 → _pending != null → stage pushes req[0].Message first,
    ///      then pulls In for each subsequent element.
    /// </summary>
    private async Task<(IReadOnlyList<IOutputItem> options, IReadOnlyList<HttpRequestMessage> messages)>
        RunStageAsync(IEnumerable<RequestItem> requests)
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
            var src = b.Add(Source.From(requestList).Concat(Source.Never<RequestItem>()));

            b.From(src).To(stage.In);
            b.From(stage.Out0).To(Sink.FromSubscriber(probe0));
            b.From(stage.Out1).To(Sink.FromSubscriber(probe1));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var sub0 = await probe0.ExpectSubscriptionAsync(CancellationToken.None);
        var sub1 = await probe1.ExpectSubscriptionAsync(CancellationToken.None);

        var options = new List<IOutputItem>();
        var messages = new List<HttpRequestMessage>();

        if (requestList.Count > 0)
        {
            // Step 1: give Out0 exactly 1 demand.
            // Out0.onPull → Pull(In) → source pushes req[0]
            // → stage: !_initialSent → _pending = req[0], _initialSent = true, Push(Out0, InitialInput)
            sub0.Request(1);
            options.Add(await probe0.ExpectNextAsync(CancellationToken.None));

            // Step 2: give Out1 all N demands.
            // Out1.onPull → _pending != null → Push(Out1, req[0].Message), _pending = null
            // → for each remaining demand: Pull(In) → source pushes → Push(Out1, msg)
            sub1.Request(requestList.Count);
            for (var i = 0; i < requestList.Count; i++)
            {
                messages.Add(await probe1.ExpectNextAsync(CancellationToken.None));
            }
        }

        return (options, messages);
    }

    [Fact(Timeout = 10_000, DisplayName = "EXT-001: First request → Out0 emits InitialInput(TcpOptions), Out1 emits RequestMessage")]
    public async Task First_Request_Emits_InitialInput_And_RequestMessage()
    {
        var req = MakeRequest();

        var (options, messages) = await RunStageAsync([req]);

        Assert.Single(options);
        Assert.IsType<ConnectItem>(options[0]);
        Assert.Single(messages);
        Assert.Same(req.RequestMessage, messages[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "EXT-002: Second request → only Out1 emits (no repeated options event)")]
    public async Task Second_Request_Does_Not_Emit_Options()
    {
        var req1 = MakeRequest("http://example.com/1");
        var req2 = MakeRequest("http://example.com/2");

        var (options, messages) = await RunStageAsync([req1, req2]);

        Assert.Single(options);
        Assert.Equal(2, messages.Count);
    }

    [Fact(Timeout = 10_000, DisplayName = "EXT-003: 5 requests → exactly 1× Out0, 5× Out1")]
    public async Task Five_Requests_One_Options_Five_Messages()
    {
        var requests = Enumerable.Range(1, 5)
            .Select(i => MakeRequest($"http://example.com/{i}"))
            .ToArray();

        var (options, messages) = await RunStageAsync(requests);

        Assert.Single(options);
        Assert.Equal(5, messages.Count);
    }

    [Fact(Timeout = 10_000, DisplayName = "EXT-004: Options extracted only on very first request (_initialSent flag)")]
    public async Task Options_Extracted_Only_On_First_Request()
    {
        var requests = Enumerable.Range(1, 5)
            .Select(i => MakeRequest($"http://example.com/{i}"))
            .ToArray();

        var (options, _) = await RunStageAsync(requests);

        Assert.Single(options);
        var initial = Assert.IsType<ConnectItem>(options[0]);
        Assert.NotNull(initial.Options);
    }

    [Fact(Timeout = 10_000, DisplayName = "EXT-005: UpstreamFinish → stage terminates cleanly")]
    public async Task UpstreamFinish_Stage_Terminates_Cleanly()
    {
        // Uses a completing source (no Concat Never) so that after all 3 requests are consumed
        // the source completes → onUpstreamFinish → CompleteStage() → both outlets complete.
        //
        // Demand sequence:
        //   sub0.Request(1) → Pull(In) → req[0] pushed → _pending=req[0], Push(Out0, InitialInput)
        //   sub1.Request(3):
        //     1st pull: _pending != null → Push(Out1, req[0].Message), _pending = null
        //     2nd pull: Pull(In) → req[1] pushed → Push(Out1, req[1].Message)
        //     3rd pull: Pull(In) → req[2] pushed → Push(Out1, req[2].Message); source exhausted → CompleteStage()
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
            b.From(stage.Out0).To(Sink.FromSubscriber(probe0));
            b.From(stage.Out1).To(Sink.FromSubscriber(probe1));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var sub0 = await probe0.ExpectSubscriptionAsync(CancellationToken.None);
        var sub1 = await probe1.ExpectSubscriptionAsync(CancellationToken.None);

        sub0.Request(1);
        await probe0.ExpectNextAsync(CancellationToken.None); // InitialInput

        sub1.Request(3);
        for (var i = 0; i < 3; i++)
        {
            await probe1.ExpectNextAsync(CancellationToken.None);
        }

        // Source exhausted → CompleteStage() → both outlets complete.
        await probe0.ExpectCompleteAsync(CancellationToken.None);
        await probe1.ExpectCompleteAsync(CancellationToken.None);
    }

    [Fact(Timeout = 10_000, DisplayName = "EXT-006: Pending request after InitialInput correctly delivered via Out1")]
    public async Task Pending_Request_Correctly_Delivered_After_InitialInput()
    {
        // The first request is stored as _pending when InitialInput is pushed to Out0.
        // When Out1 then pulls, it should receive the pending request's RequestMessage.
        var req = MakeRequest("http://example.com/pending");

        var (options, messages) = await RunStageAsync([req]);

        Assert.Single(options);
        Assert.IsType<ConnectItem>(options[0]);
        Assert.Single(messages);
        Assert.Same(req.RequestMessage, messages[0]);
    }
}
