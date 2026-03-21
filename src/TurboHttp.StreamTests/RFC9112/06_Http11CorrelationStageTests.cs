using System.Net;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.StreamTests.RFC9112;

/// <summary>
/// Tests the HTTP/1.1 request-response correlation stage per RFC 9112.
/// Verifies that responses are matched to requests in FIFO order and that RequestMessage is correctly set.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="CorrelationHttp1XStage"/>.
/// RFC 9112 §9.3: HTTP/1.1 pipeline ordering and request-response pairing.
/// </remarks>
public sealed class Http11CorrelationStageTests : StreamTestBase
{
    /// <summary>
    /// Builds and runs a closed graph that wires requestSource → RequestIn, responseSource → ResponseIn, Out → Sink.Seq.
    /// Returns the collected responses once the stream completes.
    /// </summary>
    private async Task<List<HttpResponseMessage>> RunStageAsync(
        Source<HttpRequestMessage, NotUsed> requestSource,
        Source<HttpResponseMessage, NotUsed> responseSource,
        TimeSpan? timeout = null)
    {
        var sink = Sink.Seq<HttpResponseMessage>();

        var graph = RunnableGraph.FromGraph(GraphDsl.Create(sink, (b, s) =>
        {
            var corr = b.Add(new Http1XCorrelationStage());
            var reqSrc = b.Add(requestSource);
            var resSrc = b.Add(responseSource);
            var signalSink = b.Add(Sink.Ignore<IControlItem>().MapMaterializedValue(_ => NotUsed.Instance));

            var resetSrc = b.Add(Source.Never<NotUsed>());
            b.From(reqSrc).To(corr.InRequest);
            b.From(resSrc).To(corr.InResponse);
            b.From(resetSrc).To(corr.InReset);
            b.From(corr.Out).To(s);
            b.From(corr.OutSignal).To(signalSink);

            return ClosedShape.Instance;
        }));

        var task = graph.Run(Materializer);
        var result = await task.WaitAsync(timeout ?? TimeSpan.FromSeconds(5));
        return result.ToList();
    }

    private static HttpResponseMessage OkResponse()
        => new(HttpStatusCode.OK);

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-9.3-11CR-001: Single request/response pairing → response.RequestMessage == request")]
    public async Task Should_PairRequestWithResponse_WhenSingleRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var response = OkResponse();

        var results = await RunStageAsync(
            Source.Single(request),
            Source.Single(response));

        Assert.Single(results);
        Assert.Same(request, results[0].RequestMessage);
        Assert.Same(response, results[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-9.3-11CR-002: 5 sequential requests → FIFO order maintained")]
    public async Task Should_MaintainFifoOrder_WhenFiveSequentialRequests()
    {
        var requests = Enumerable.Range(1, 5)
            .Select(i => new HttpRequestMessage(HttpMethod.Get, $"http://example.com/{i}"))
            .ToArray();

        var responses = Enumerable.Range(1, 5)
            .Select(_ => OkResponse())
            .ToArray();

        var results = await RunStageAsync(
            Source.From(requests),
            Source.From(responses));

        Assert.Equal(5, results.Count);
        for (var i = 0; i < 5; i++)
        {
            Assert.Same(requests[i], results[i].RequestMessage);
            Assert.Same(responses[i], results[i]);
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-9.3-11CR-003: Request reference is the exact same object (not copied)")]
    public async Task Should_PreserveRequestReference_WhenCorrelated()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/data")
        {
            Content = new StringContent("body")
        };
        var response = OkResponse();

        var results = await RunStageAsync(
            Source.Single(request),
            Source.Single(response));

        Assert.True(ReferenceEquals(request, results[0].RequestMessage),
            "response.RequestMessage must be the exact same object reference as the sent request.");
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-9.3-11CR-004: Response arrives before request → correctly buffered and correlated")]
    public async Task Should_BufferAndCorrelate_WhenResponseArrivesBeforeRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/delayed");
        var response = OkResponse();

        // Response source emits immediately; request source delayed by 300ms.
        // The response will be buffered in the _waiting queue until the request arrives.
        var results = await RunStageAsync(
            Source.Single(request).InitialDelay(TimeSpan.FromMilliseconds(300)),
            Source.Single(response));

        Assert.Single(results);
        Assert.Same(request, results[0].RequestMessage);
        Assert.Same(response, results[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-9.3-11CR-005: Request arrives before response → correctly buffered and correlated")]
    public async Task Should_BufferAndCorrelate_WhenRequestArrivesBeforeResponse()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/eager");
        var response = OkResponse();

        // Request source emits immediately; response source delayed by 300ms.
        // The request will be buffered in the _pending queue until the response arrives.
        var results = await RunStageAsync(
            Source.Single(request),
            Source.Single(response).InitialDelay(TimeSpan.FromMilliseconds(300)));

        Assert.Single(results);
        Assert.Same(request, results[0].RequestMessage);
        Assert.Same(response, results[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-9.3-11CR-006: Stage stays alive after correlation when upstreams remain open")]
    public async Task Should_StayAlive_WhenQueuesEmptyButUpstreamOpen()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/1");
        var response = OkResponse();

        // Sources emit one element each but never complete — stage must NOT self-complete
        // when queues drain to empty. Completion is only valid when both upstreams finish.
        var requestSource = Source.Single(request)
            .Concat(Source.Never<HttpRequestMessage>());
        var responseSource = Source.Single(response)
            .Concat(Source.Never<HttpResponseMessage>());

        var sink = Sink.Seq<HttpResponseMessage>();

        var graph = RunnableGraph.FromGraph(GraphDsl.Create(sink, (b, s) =>
        {
            var corr = b.Add(new Http1XCorrelationStage());
            var reqSrc = b.Add(requestSource);
            var resSrc = b.Add(responseSource);
            var signalSink = b.Add(Sink.Ignore<IControlItem>().MapMaterializedValue(_ => NotUsed.Instance));

            var resetSrc = b.Add(Source.Never<NotUsed>());
            b.From(reqSrc).To(corr.InRequest);
            b.From(resSrc).To(corr.InResponse);
            b.From(resetSrc).To(corr.InReset);
            b.From(corr.Out).To(s);
            b.From(corr.OutSignal).To(signalSink);

            return ClosedShape.Instance;
        }));

        var task = graph.Run(Materializer);

        // Stage must NOT complete even though queues are empty — upstreams are still open.
        await Assert.ThrowsAsync<TimeoutException>(() => task.WaitAsync(TimeSpan.FromMilliseconds(500)));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-9.3-11CR-007: Stage remains open while pending requests still exist")]
    public async Task Should_RemainOpen_WhenPendingRequestsExist()
    {
        // Send 2 requests but only 1 response.
        // The response source stays open (via Concat+Never) so the stage cannot
        // complete due to upstream finish — only the pending request queue keeps it alive.
        var request1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/1");
        var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/2");
        var response1 = OkResponse();

        var sink = Sink.Seq<HttpResponseMessage>();

        // Keep the response source open so only pending-request logic matters.
        var neverEndingResponses = Source.Single(response1)
            .Concat(Source.Never<HttpResponseMessage>());

        var graph = RunnableGraph.FromGraph(GraphDsl.Create(sink, (b, s) =>
        {
            var corr = b.Add(new Http1XCorrelationStage());
            var reqSrc = b.Add(Source.From([request1, request2]));
            var resSrc = b.Add(neverEndingResponses);
            var signalSink = b.Add(Sink.Ignore<IControlItem>().MapMaterializedValue(_ => NotUsed.Instance));

            var resetSrc = b.Add(Source.Never<NotUsed>());
            b.From(reqSrc).To(corr.InRequest);
            b.From(resSrc).To(corr.InResponse);
            b.From(resetSrc).To(corr.InReset);
            b.From(corr.Out).To(s);
            b.From(corr.OutSignal).To(signalSink);

            return ClosedShape.Instance;
        }));

        var task = graph.Run(Materializer);

        // The stream should NOT complete because there is still a pending request
        // with no matching response. Wait briefly and verify it's still running.
        var completed = task.WaitAsync(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAsync<TimeoutException>(() => completed);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-9.3-11CR-008: One request pushed → OutletSignal emits one StreamAcquireItem")]
    public async Task Should_EmitOneStreamAcquireItem_WhenSingleRequestPushed()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var response = OkResponse();

        var signalSink = Sink.Seq<IControlItem>();

        var graph = RunnableGraph.FromGraph(GraphDsl.Create(signalSink, (b, s) =>
        {
            var corr = b.Add(new Http1XCorrelationStage());
            var reqSrc = b.Add(Source.Single(request));
            var resSrc = b.Add(Source.Single(response));
            var responseSink = b.Add(Sink.Ignore<HttpResponseMessage>().MapMaterializedValue(_ => NotUsed.Instance));

            var resetSrc = b.Add(Source.Never<NotUsed>());
            b.From(reqSrc).To(corr.InRequest);
            b.From(resSrc).To(corr.InResponse);
            b.From(resetSrc).To(corr.InReset);
            b.From(corr.Out).To(responseSink);
            b.From(corr.OutSignal).To(s);

            return ClosedShape.Instance;
        }));

        var task = graph.Run(Materializer);
        var signals = await task.WaitAsync(TimeSpan.FromSeconds(5));

        var items = signals.ToList();
        Assert.Single(items);
        Assert.IsType<StreamAcquireItem>(items[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-9.3-11CR-009: Two requests pushed → two StreamAcquireItems emitted")]
    public async Task Should_EmitTwoStreamAcquireItems_WhenTwoRequestsPushed()
    {
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/1"),
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/2")
        };
        var responses = new[]
        {
            OkResponse(),
            OkResponse()
        };

        var signalSink = Sink.Seq<IControlItem>();

        var graph = RunnableGraph.FromGraph(GraphDsl.Create(signalSink, (b, s) =>
        {
            var corr = b.Add(new Http1XCorrelationStage());
            var reqSrc = b.Add(Source.From(requests));
            var resSrc = b.Add(Source.From(responses));
            var responseSink = b.Add(Sink.Ignore<HttpResponseMessage>().MapMaterializedValue(_ => NotUsed.Instance));

            var resetSrc = b.Add(Source.Never<NotUsed>());
            b.From(reqSrc).To(corr.InRequest);
            b.From(resSrc).To(corr.InResponse);
            b.From(resetSrc).To(corr.InReset);
            b.From(corr.Out).To(responseSink);
            b.From(corr.OutSignal).To(s);

            return ClosedShape.Instance;
        }));

        var task = graph.Run(Materializer);
        var signals = await task.WaitAsync(TimeSpan.FromSeconds(5));

        var items = signals.ToList();
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.IsType<StreamAcquireItem>(item));
    }
}
