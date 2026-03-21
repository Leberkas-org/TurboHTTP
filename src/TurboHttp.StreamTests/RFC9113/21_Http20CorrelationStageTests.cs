using System.Net;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.StreamTests.RFC9113;

/// <summary>
/// Tests the HTTP/2 stream correlation stage per RFC 9113.
/// Verifies that responses are matched to their originating requests using stream IDs and RequestMessage is set.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="CorrelationHttp20Stage"/>.
/// RFC 9113 §5.1: HTTP/2 stream state machine and stream-ID-based request-response correlation.
/// </remarks>
public sealed class Http20CorrelationStageTests : StreamTestBase
{
    /// <summary>
    /// Runs the CorrelationHttp20Stage with the given sources and returns collected responses.
    /// In0 = (HttpRequestMessage, streamId), In1 = (HttpResponseMessage, streamId), Out = HttpResponseMessage.
    /// </summary>
    private async Task<List<HttpResponseMessage>> RunStageAsync(
        Source<(HttpRequestMessage, int), NotUsed> requestSource,
        Source<(HttpResponseMessage, int), NotUsed> responseSource,
        TimeSpan? timeout = null)
    {
        var sink = Sink.Seq<HttpResponseMessage>();

        var graph = RunnableGraph.FromGraph(GraphDsl.Create(sink, (b, s) =>
        {
            var corr = b.Add(new Http20CorrelationStage());
            var reqSrc = b.Add(requestSource);
            var resSrc = b.Add(responseSource);

            b.From(reqSrc).To(corr.In0);
            b.From(resSrc).To(corr.In1);
            b.From(corr.Out).To(s);

            return ClosedShape.Instance;
        }));

        var task = graph.Run(Materializer);
        var result = await task.WaitAsync(timeout ?? TimeSpan.FromSeconds(5));
        return result.ToList();
    }

    private static HttpRequestMessage MakeRequest(int index = 0)
        => new(HttpMethod.Get, $"http://example.com/{index}");

    private static HttpResponseMessage OkResponse()
        => new(HttpStatusCode.OK);

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.1-20CR-001: Single (Request,streamId=1) + (Response,streamId=1) → correctly correlated")]
    public async Task Should_Correlate_Single_Request_And_Response()
    {
        var request = MakeRequest();
        var response = OkResponse();

        var results = await RunStageAsync(
            Source.Single((request, 1)),
            Source.Single((response, 1)));

        Assert.Single(results);
        Assert.Same(request, results[0].RequestMessage);
        Assert.Same(response, results[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.1-20CR-002: 3 requests (IDs 1,3,5) + 3 responses (IDs 5,1,3) → out-of-order correlation")]
    public async Task Should_Correlate_Out_Of_Order_Responses()
    {
        var req1 = MakeRequest(1);
        var req3 = MakeRequest(3);
        var req5 = MakeRequest(5);

        var res1 = OkResponse();
        var res3 = OkResponse();
        var res5 = OkResponse();

        var requests = new[] { (req1, 1), (req3, 3), (req5, 5) };
        // Responses arrive in reverse stream-ID order
        var responses = new[] { (res5, 5), (res1, 1), (res3, 3) };

        var results = await RunStageAsync(
            Source.From(requests),
            Source.From(responses));

        Assert.Equal(3, results.Count);

        // Each response must carry its matching request via RequestMessage
        Assert.Contains(results, r => ReferenceEquals(r.RequestMessage, req1) && ReferenceEquals(r, res1));
        Assert.Contains(results, r => ReferenceEquals(r.RequestMessage, req3) && ReferenceEquals(r, res3));
        Assert.Contains(results, r => ReferenceEquals(r.RequestMessage, req5) && ReferenceEquals(r, res5));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.1-20CR-003: Response stream ID with no matching request → stays in queue")]
    public async Task Should_Keep_Unmatched_Response_In_Queue()
    {
        var req1 = MakeRequest(1);
        var res99 = OkResponse(); // stream ID 99 — no request was sent on this stream

        // Request source: only (req1, streamId=1) then stays open (never ends).
        // Response source: only (res99, streamId=99) then stays open.
        // There is NO matching request for streamId=99, so the stage must NOT emit anything.
        // Both upstreams remain live, so the stage keeps running indefinitely.
        // The TimeoutException proves the stage is still open — the orphan is held in _waiting.
        var requestSource = Source.Single((req1, 1))
            .Concat(Source.Never<(HttpRequestMessage, int)>());
        var responseSource = Source.Single((res99, 99))
            .Concat(Source.Never<(HttpResponseMessage, int)>());

        var sink = Sink.Seq<HttpResponseMessage>();

        var graph = RunnableGraph.FromGraph(GraphDsl.Create(sink, (b, s) =>
        {
            var corr = b.Add(new Http20CorrelationStage());
            var reqSrc = b.Add(requestSource);
            var resSrc = b.Add(responseSource);

            b.From(reqSrc).To(corr.In0);
            b.From(resSrc).To(corr.In1);
            b.From(corr.Out).To(s);

            return ClosedShape.Instance;
        }));

        var task = graph.Run(Materializer);

        // Stage must NOT complete: the unmatched response keeps _waiting non-empty
        // and there is no matching request to emit or empty the queue.
        var completedEarly = task.WaitAsync(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAsync<TimeoutException>(() => completedEarly);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.1-20CR-004: Reference equality: response.RequestMessage is exactly the sent object")]
    public async Task Should_Set_RequestMessage_As_Exact_Same_Reference()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/data")
        {
            Content = new StringContent("payload")
        };
        var response = OkResponse();

        var results = await RunStageAsync(
            Source.Single((request, 1)),
            Source.Single((response, 1)));

        Assert.True(ReferenceEquals(request, results[0].RequestMessage),
            "response.RequestMessage must be the exact same object reference as the original request.");
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.1-20CR-005: 10 interleaved requests/responses → all correctly matched")]
    public async Task Should_Match_All_When_Ten_Interleaved_Requests()
    {
        const int count = 10;

        // Stream IDs: 1,3,5,...,19
        var requests = Enumerable.Range(0, count)
            .Select(i => (Msg: MakeRequest(i), StreamId: 1 + i * 2))
            .ToArray();

        // Shuffle responses relative to requests
        var responses = requests
            .Select(r => (Msg: OkResponse(), r.StreamId))
            .Reverse()  // reverse order to interleave
            .ToArray();

        var requestSource = Source.From(requests.Select(r => (r.Msg, r.StreamId)));
        var responseSource = Source.From(responses.Select(r => (r.Msg, r.StreamId)));

        var results = await RunStageAsync(requestSource, responseSource);

        Assert.Equal(count, results.Count);

        // Build lookup by stream-id → original request
        var requestById = requests.ToDictionary(r => r.StreamId, r => r.Msg);
        var responseById = responses.ToDictionary(r => r.StreamId, r => r.Msg);

        foreach (var result in results)
        {
            var matched = requestById.Values.FirstOrDefault(r => ReferenceEquals(r, result.RequestMessage));
            Assert.NotNull(matched);
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.1-20CR-006: Stage stays alive after correlation when upstreams remain open")]
    public async Task Should_StayAlive_When_Dicts_Empty_But_Upstream_Open()
    {
        var request = MakeRequest();
        var response = OkResponse();

        // Sources emit one element each but never complete — stage must NOT self-complete
        // when dictionaries drain to empty. Completion only valid when both upstreams finish.
        var requestSource = Source.Single((request, 1))
            .Concat(Source.Never<(HttpRequestMessage, int)>());
        var responseSource = Source.Single((response, 1))
            .Concat(Source.Never<(HttpResponseMessage, int)>());

        var sink = Sink.Seq<HttpResponseMessage>();

        var graph = RunnableGraph.FromGraph(GraphDsl.Create(sink, (b, s) =>
        {
            var corr = b.Add(new Http20CorrelationStage());
            var reqSrc = b.Add(requestSource);
            var resSrc = b.Add(responseSource);

            b.From(reqSrc).To(corr.In0);
            b.From(resSrc).To(corr.In1);
            b.From(corr.Out).To(s);

            return ClosedShape.Instance;
        }));

        var task = graph.Run(Materializer);

        // Stage must NOT complete even though dictionaries are empty — upstreams are still open.
        await Assert.ThrowsAsync<TimeoutException>(() => task.WaitAsync(TimeSpan.FromMilliseconds(500)));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.1-20CR-007: Request(1), Response(3), Request(3) → correlation immediately on match")]
    public async Task Should_Correlate_Immediately_On_Match_When_Interleaved_Push()
    {
        var req1 = MakeRequest(1);
        var req3 = MakeRequest(3);
        var res3 = OkResponse();
        var res1 = OkResponse();

        // Request(1) arrives, no matching response yet.
        // Response(3) arrives, no matching request for 3 yet.
        // Request(3) arrives → immediately matched with Response(3).
        // Then Response(1) arrives → matched with Request(1).
        var requestSource = Source.From([(req1, 1), (req3, 3)]);
        var responseSource = Source.From([(res3, 3), (res1, 1)]);

        var results = await RunStageAsync(requestSource, responseSource);

        Assert.Equal(2, results.Count);

        Assert.Contains(results, r => ReferenceEquals(r.RequestMessage, req1) && ReferenceEquals(r, res1));
        Assert.Contains(results, r => ReferenceEquals(r.RequestMessage, req3) && ReferenceEquals(r, res3));
    }
}
