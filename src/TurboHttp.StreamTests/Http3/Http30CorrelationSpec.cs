using System.Net;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.StreamTests.Http3;

/// <summary>
/// Tests the HTTP/3 FIFO correlation stage per RFC 9114.
/// Verifies that responses are matched to their originating requests in FIFO order
/// and that RequestMessage is set correctly.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http30CorrelationStage"/>.
/// RFC 9114 §4.1: HTTP/3 request-response exchange — FIFO correlation (QUIC handles multiplexing natively).
/// </remarks>
public sealed class Http30CorrelationSpec : StreamTestBase
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Correlation_should_correlate_single_request_and_response()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Correlation_should_correlate_multiple_requests_in_fifo_order()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Correlation_should_keep_unmatched_response_in_queue()
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
        var completedEarly = task.WaitAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<TimeoutException>(() => completedEarly);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Correlation_should_set_request_message_as_exact_same_reference()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Correlation_should_match_all_when_ten_sequential_requests()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Correlation_should_stay_alive_when_queues_empty_but_upstream_open()
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
        await Assert.ThrowsAsync<TimeoutException>(() => task.WaitAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Correlation_should_complete_when_both_upstreams_finish()
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
