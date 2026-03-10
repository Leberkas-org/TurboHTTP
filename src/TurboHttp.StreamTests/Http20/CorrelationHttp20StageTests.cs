using System.Net;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http20;

public sealed class CorrelationHttp20StageTests : StreamTestBase
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
            var corr = b.Add(new CorrelationHttp20Stage());
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

    [Fact(Timeout = 10_000, DisplayName = "COR20-001: Single (Request,streamId=1) + (Response,streamId=1) → correctly correlated")]
    public async Task COR20_001_Single_Request_Response_Correlated()
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

    [Fact(Timeout = 10_000, DisplayName = "COR20-002: 3 requests (IDs 1,3,5) + 3 responses (IDs 5,1,3) → out-of-order correlation")]
    public async Task COR20_002_Out_Of_Order_Responses_Correlated()
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

    [Fact(Timeout = 10_000, DisplayName = "COR20-003: Response stream ID with no matching request → stays in queue")]
    public async Task COR20_003_Unmatched_Response_Stays_In_Queue()
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
            var corr = b.Add(new CorrelationHttp20Stage());
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

    [Fact(Timeout = 10_000, DisplayName = "COR20-004: Reference equality: response.RequestMessage is exactly the sent object")]
    public async Task COR20_004_Reference_Equality()
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

    [Fact(Timeout = 10_000, DisplayName = "COR20-005: 10 interleaved requests/responses → all correctly matched")]
    public async Task COR20_005_Ten_Interleaved_All_Matched()
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

    [Fact(Timeout = 10_000, DisplayName = "COR20-006: Stage terminates on empty dictionaries after UpstreamFinish")]
    public async Task COR20_006_Stage_Terminates_On_Empty_Dicts_After_Finish()
    {
        var request = MakeRequest();
        var response = OkResponse();

        var sink = Sink.Seq<HttpResponseMessage>();

        var graph = RunnableGraph.FromGraph(GraphDsl.Create(sink, (b, s) =>
        {
            var corr = b.Add(new CorrelationHttp20Stage());
            var reqSrc = b.Add(Source.Single((request, 1)));
            var resSrc = b.Add(Source.Single((response, 1)));

            b.From(reqSrc).To(corr.In0);
            b.From(resSrc).To(corr.In1);
            b.From(corr.Out).To(s);

            return ClosedShape.Instance;
        }));

        var task = graph.Run(Materializer);

        // The stream should complete without timeout, proving TryComplete fires correctly
        var results = await task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(results);
        Assert.Same(request, results[0].RequestMessage);
    }

    [Fact(Timeout = 10_000, DisplayName = "COR20-007: Request(1), Response(3), Request(3) → correlation immediately on match")]
    public async Task COR20_007_Interleaved_Push_Correlated_On_Match()
    {
        var req1 = MakeRequest(1);
        var req3 = MakeRequest(3);
        var res3 = OkResponse();
        var res1 = OkResponse();

        // Request(1) arrives, no matching response yet.
        // Response(3) arrives, no matching request for 3 yet.
        // Request(3) arrives → immediately matched with Response(3).
        // Then Response(1) arrives → matched with Request(1).
        var requestSource = Source.From(new[] { (req1, 1), (req3, 3) });
        var responseSource = Source.From(new[] { (res3, 3), (res1, 1) });

        var results = await RunStageAsync(requestSource, responseSource);

        Assert.Equal(2, results.Count);

        Assert.Contains(results, r => ReferenceEquals(r.RequestMessage, req1) && ReferenceEquals(r, res1));
        Assert.Contains(results, r => ReferenceEquals(r.RequestMessage, req3) && ReferenceEquals(r, res3));
    }
}
