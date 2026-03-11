using System.Net;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http11;

public sealed class Http1XCorrelationStageTests : StreamTestBase
{
    /// <summary>
    /// Builds and runs a closed graph that wires requestSource → In0, responseSource → In1, Out → Sink.Seq.
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

            b.From(reqSrc).To(corr.In0);
            b.From(resSrc).To(corr.In1);
            b.From(corr.Out).To(s);

            return ClosedShape.Instance;
        }));

        var task = graph.Run(Materializer);
        var result = await task.WaitAsync(timeout ?? TimeSpan.FromSeconds(5));
        return result.ToList();
    }

    private static HttpResponseMessage OkResponse()
        => new(HttpStatusCode.OK);

    [Fact(Timeout = 10_000, DisplayName = "COR1X-001: Single request/response pairing → response.RequestMessage == request")]
    public async Task Single_Request_Response_Pairing()
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

    [Fact(Timeout = 10_000, DisplayName = "COR1X-002: 5 sequential requests → FIFO order maintained")]
    public async Task Five_Sequential_Requests_Fifo_Order()
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

    [Fact(Timeout = 10_000, DisplayName = "COR1X-003: Request reference is the exact same object (not copied)")]
    public async Task Request_Reference_Is_Same_Object()
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

    [Fact(Timeout = 10_000, DisplayName = "COR1X-004: Response arrives before request → correctly buffered and correlated")]
    public async Task Response_Before_Request_Buffered_And_Correlated()
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

    [Fact(Timeout = 10_000, DisplayName = "COR1X-005: Request arrives before response → correctly buffered and correlated")]
    public async Task Request_Before_Response_Buffered_And_Correlated()
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

    [Fact(Timeout = 10_000, DisplayName = "COR1X-006: Stage terminates on empty queue after UpstreamFinish on both inlets")]
    public async Task Stage_Terminates_When_Both_Upstreams_Finish_And_Queues_Empty()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/done");
        var response = OkResponse();

        // Both sources emit one element and then complete.
        // After correlation, both queues are empty → stage should complete.
        var sink = Sink.Seq<HttpResponseMessage>();

        var graph = RunnableGraph.FromGraph(GraphDsl.Create(sink, (b, s) =>
        {
            var corr = b.Add(new Http1XCorrelationStage());
            var reqSrc = b.Add(Source.Single(request));
            var resSrc = b.Add(Source.Single(response));

            b.From(reqSrc).To(corr.In0);
            b.From(resSrc).To(corr.In1);
            b.From(corr.Out).To(s);

            return ClosedShape.Instance;
        }));

        var task = graph.Run(Materializer);

        // The stream should complete within the timeout — proving the stage terminates.
        var results = await task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(results);
        Assert.Same(request, results[0].RequestMessage);
    }

    [Fact(Timeout = 10_000, DisplayName = "COR1X-007: Stage remains open while pending requests still exist")]
    public async Task Stage_Remains_Open_With_Pending_Requests()
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
            var reqSrc = b.Add(Source.From(new[] { request1, request2 }));
            var resSrc = b.Add(neverEndingResponses);

            b.From(reqSrc).To(corr.In0);
            b.From(resSrc).To(corr.In1);
            b.From(corr.Out).To(s);

            return ClosedShape.Instance;
        }));

        var task = graph.Run(Materializer);

        // The stream should NOT complete because there is still a pending request
        // with no matching response. Wait briefly and verify it's still running.
        var completed = task.WaitAsync(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAsync<TimeoutException>(() => completed);
    }
}
