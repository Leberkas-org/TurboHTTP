using System.Net;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.StreamTests.RFC9114;

/// <summary>
/// Tests the HTTP/3 FIFO correlation stage per RFC 9114.
/// Verifies that responses are matched to their originating requests in FIFO order
/// and that RequestMessage is set correctly.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http30CorrelationStage"/>.
/// RFC 9114 §4.1: HTTP/3 request-response exchange — FIFO correlation (QUIC handles multiplexing natively).
/// </remarks>
public sealed class Http30CorrelationStageTests : StreamTestBase
{
    /// <summary>
    /// Runs the Http30CorrelationStage with the given sources and returns collected responses.
    /// In0 = HttpRequestMessage, In1 = HttpResponseMessage, Out = HttpResponseMessage.
    /// </summary>
    private async Task<List<HttpResponseMessage>> RunStageAsync(
        Source<HttpRequestMessage, NotUsed> requestSource,
        Source<HttpResponseMessage, NotUsed> responseSource,
        TimeSpan? timeout = null)
    {
        var sink = Sink.Seq<HttpResponseMessage>();

        var graph = RunnableGraph.FromGraph(GraphDsl.Create(sink, (b, s) =>
        {
            var corr = b.Add(new Http30CorrelationStage());
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

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30CR-001: Single request + response → correctly correlated in FIFO order")]
    public async Task Should_Correlate_Single_Request_And_Response()
    {
        var request = MakeRequest();
        var response = OkResponse();

        var results = await RunStageAsync(
            Source.Single(request),
            Source.Single(response));

        Assert.Single(results);
        Assert.Same(request, results[0].RequestMessage);
        Assert.Same(response, results[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30CR-002: 3 requests + 3 responses → FIFO-matched in order")]
    public async Task Should_Correlate_Multiple_Requests_In_Fifo_Order()
    {
        var req0 = MakeRequest(0);
        var req1 = MakeRequest(1);
        var req2 = MakeRequest(2);

        var res0 = OkResponse();
        var res1 = OkResponse();
        var res2 = OkResponse();

        var results = await RunStageAsync(
            Source.From([req0, req1, req2]),
            Source.From([res0, res1, res2]));

        Assert.Equal(3, results.Count);

        // FIFO: first request matches first response, etc.
        Assert.Same(req0, results[0].RequestMessage);
        Assert.Same(res0, results[0]);
        Assert.Same(req1, results[1].RequestMessage);
        Assert.Same(res1, results[1]);
        Assert.Same(req2, results[2].RequestMessage);
        Assert.Same(res2, results[2]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30CR-003: Response arrives before request → stays in queue until request arrives")]
    public async Task Should_Keep_Unmatched_Response_In_Queue()
    {
        var req0 = MakeRequest(0);

        var requestSource = Source.Single(req0)
            .Concat(Source.Never<HttpRequestMessage>());
        var responseSource = Source.From([OkResponse(), OkResponse()])
            .Concat(Source.Never<HttpResponseMessage>());

        var sink = Sink.Seq<HttpResponseMessage>();

        var graph = RunnableGraph.FromGraph(GraphDsl.Create(sink, (b, s) =>
        {
            var corr = b.Add(new Http30CorrelationStage());
            var reqSrc = b.Add(requestSource);
            var resSrc = b.Add(responseSource);

            b.From(reqSrc).To(corr.In0);
            b.From(resSrc).To(corr.In1);
            b.From(corr.Out).To(s);

            return ClosedShape.Instance;
        }));

        var task = graph.Run(Materializer);

        // Stage must NOT complete: the second response has no matching request
        var completedEarly = task.WaitAsync(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAsync<TimeoutException>(() => completedEarly);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30CR-004: Reference equality: response.RequestMessage is exactly the sent object")]
    public async Task Should_Set_RequestMessage_As_Exact_Same_Reference()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/data")
        {
            Content = new StringContent("payload")
        };
        var response = OkResponse();

        var results = await RunStageAsync(
            Source.Single(request),
            Source.Single(response));

        Assert.True(ReferenceEquals(request, results[0].RequestMessage),
            "response.RequestMessage must be the exact same object reference as the original request.");
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30CR-005: 10 sequential requests/responses → all correctly FIFO-matched")]
    public async Task Should_Match_All_When_Ten_Sequential_Requests()
    {
        const int count = 10;

        var requests = Enumerable.Range(0, count)
            .Select(i => MakeRequest(i))
            .ToArray();

        var responses = Enumerable.Range(0, count)
            .Select(_ => OkResponse())
            .ToArray();

        var results = await RunStageAsync(
            Source.From(requests),
            Source.From(responses));

        Assert.Equal(count, results.Count);

        for (var i = 0; i < count; i++)
        {
            Assert.Same(requests[i], results[i].RequestMessage);
            Assert.Same(responses[i], results[i]);
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30CR-006: Stage stays alive after correlation when upstreams remain open")]
    public async Task Should_StayAlive_When_Queues_Empty_But_Upstream_Open()
    {
        var request = MakeRequest();
        var response = OkResponse();

        var requestSource = Source.Single(request)
            .Concat(Source.Never<HttpRequestMessage>());
        var responseSource = Source.Single(response)
            .Concat(Source.Never<HttpResponseMessage>());

        var sink = Sink.Seq<HttpResponseMessage>();

        var graph = RunnableGraph.FromGraph(GraphDsl.Create(sink, (b, s) =>
        {
            var corr = b.Add(new Http30CorrelationStage());
            var reqSrc = b.Add(requestSource);
            var resSrc = b.Add(responseSource);

            b.From(reqSrc).To(corr.In0);
            b.From(resSrc).To(corr.In1);
            b.From(corr.Out).To(s);

            return ClosedShape.Instance;
        }));

        var task = graph.Run(Materializer);

        // Stage must NOT complete even though queues are empty — upstreams are still open.
        await Assert.ThrowsAsync<TimeoutException>(() => task.WaitAsync(TimeSpan.FromMilliseconds(500)));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30CR-007: Both upstreams finish → stage completes")]
    public async Task Should_Complete_When_Both_Upstreams_Finish()
    {
        var request = MakeRequest();
        var response = OkResponse();

        var results = await RunStageAsync(
            Source.Single(request),
            Source.Single(response));

        Assert.Single(results);
        Assert.Same(request, results[0].RequestMessage);
    }
}
