using System.Net;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages.Routing;

namespace TurboHTTP.StreamTests.Http11;

/// <summary>
/// Tests the HTTP/1.1 request-response correlation stage per RFC 9112.
/// Verifies that responses are matched to requests in FIFO order and that RequestMessage is correctly set.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http11CorrelationStage"/>.
/// RFC 9112 §9: HTTP/1.x strict one-request-in-flight ordering and request-response pairing.
/// </remarks>
public sealed class Http11CorrelationStageSpec : StreamTestBase
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
            var corr = b.Add(new Http11CorrelationStage());
            var reqSrc = b.Add(requestSource);
            var resSrc = b.Add(responseSource);
            var signalSink = b.Add(Sink.Ignore<IOutputItem>().MapMaterializedValue(_ => NotUsed.Instance));

            b.From(reqSrc).To(corr.InRequest);
            b.From(resSrc).To(corr.InResponse);
            b.From(corr.OutResponse).To(s);
            b.From(corr.OutControl).To(signalSink);

            return ClosedShape.Instance;
        }));

        var task = graph.Run(Materializer);
        var result = await task.WaitAsync(timeout ?? TimeSpan.FromSeconds(5));
        return result.ToList();
    }

    private static HttpResponseMessage OkResponse()
        => new(HttpStatusCode.OK);

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11CorrelationStage_should_pair_request_with_response_when_single_request()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11CorrelationStage_should_maintain_fifo_order_when_five_sequential_requests()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11CorrelationStage_should_preserve_request_reference_when_correlated()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11CorrelationStage_should_correlate_when_response_arrives_after_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/delayed");
        var response = OkResponse();

        // Request is sent first; response is delayed by 300ms.
        // Stage pulls InRequest first, then pulls InResponse after request arrives.
        var results = await RunStageAsync(
            Source.Single(request),
            Source.Single(response).InitialDelay(TimeSpan.FromMilliseconds(300)));

        Assert.Single(results);
        Assert.Same(request, results[0].RequestMessage);
        Assert.Same(response, results[0]);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11CorrelationStage_should_buffer_and_correlate_when_request_arrives_before_response()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/eager");
        var response = OkResponse();

        // Request source emits immediately; response source delayed by 300ms.
        // Stage waits for the response after pulling and storing the request.
        var results = await RunStageAsync(
            Source.Single(request),
            Source.Single(response).InitialDelay(TimeSpan.FromMilliseconds(300)));

        Assert.Single(results);
        Assert.Same(request, results[0].RequestMessage);
        Assert.Same(response, results[0]);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11CorrelationStage_should_stay_alive_when_queues_empty_but_upstream_open()
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
            var corr = b.Add(new Http11CorrelationStage());
            var reqSrc = b.Add(requestSource);
            var resSrc = b.Add(responseSource);
            var signalSink = b.Add(Sink.Ignore<IOutputItem>().MapMaterializedValue(_ => NotUsed.Instance));

            b.From(reqSrc).To(corr.InRequest);
            b.From(resSrc).To(corr.InResponse);
            b.From(corr.OutResponse).To(s);
            b.From(corr.OutControl).To(signalSink);

            return ClosedShape.Instance;
        }));

        var task = graph.Run(Materializer);

        // Stage must NOT complete even though queues are empty — upstreams are still open.
        await Assert.ThrowsAsync<TimeoutException>(() => task.WaitAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11CorrelationStage_should_remain_open_when_in_flight_request_awaiting_response()
    {
        // Send 2 requests but keep the response source open after delivering only 1 response.
        // The second request is pulled after the first response; it waits for its response
        // indefinitely (Never) — the stage must remain open.
        var request1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/1");
        var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/2");
        var response1 = OkResponse();

        var sink = Sink.Seq<HttpResponseMessage>();

        // Keep the response source open so only the in-flight back-pressure matters.
        var neverEndingResponses = Source.Single(response1)
            .Concat(Source.Never<HttpResponseMessage>());

        var graph = RunnableGraph.FromGraph(GraphDsl.Create(sink, (b, s) =>
        {
            var corr = b.Add(new Http11CorrelationStage());
            var reqSrc = b.Add(Source.From([request1, request2]));
            var resSrc = b.Add(neverEndingResponses);
            var signalSink = b.Add(Sink.Ignore<IOutputItem>().MapMaterializedValue(_ => NotUsed.Instance));

            b.From(reqSrc).To(corr.InRequest);
            b.From(resSrc).To(corr.InResponse);
            b.From(corr.OutResponse).To(s);
            b.From(corr.OutControl).To(signalSink);

            return ClosedShape.Instance;
        }));

        var task = graph.Run(Materializer);

        // The stream should NOT complete because request2 is in flight with no matching response.
        var completed = task.WaitAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<TimeoutException>(() => completed);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11CorrelationStage_should_emit_one_stream_acquire_item_when_single_request_pushed()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var response = OkResponse();

        var signalSink = Sink.Seq<IOutputItem>();

        var graph = RunnableGraph.FromGraph(GraphDsl.Create(signalSink, (b, s) =>
        {
            var corr = b.Add(new Http11CorrelationStage());
            var reqSrc = b.Add(Source.Single(request));
            var resSrc = b.Add(Source.Single(response));
            var responseSink = b.Add(Sink.Ignore<HttpResponseMessage>().MapMaterializedValue(_ => NotUsed.Instance));

            b.From(reqSrc).To(corr.InRequest);
            b.From(resSrc).To(corr.InResponse);
            b.From(corr.OutResponse).To(responseSink);
            b.From(corr.OutControl).To(s);

            return ClosedShape.Instance;
        }));

        var task = graph.Run(Materializer);
        var signals = await task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var items = signals.ToList();
        // 1 StreamAcquireItem (request) + 1 ConnectionReuseItem (response) = 2 signals
        Assert.Equal(2, items.Count);
        Assert.IsType<StreamAcquireItem>(items[0]);
        Assert.IsType<ConnectionReuseItem>(items[1]);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11CorrelationStage_should_emit_two_stream_acquire_items_and_two_connection_reuse_items_when_two_requests_pushed()
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

        var signalSink = Sink.Seq<IOutputItem>();

        var graph = RunnableGraph.FromGraph(GraphDsl.Create(signalSink, (b, s) =>
        {
            var corr = b.Add(new Http11CorrelationStage());
            var reqSrc = b.Add(Source.From(requests));
            var resSrc = b.Add(Source.From(responses));
            var responseSink = b.Add(Sink.Ignore<HttpResponseMessage>().MapMaterializedValue(_ => NotUsed.Instance));

            b.From(reqSrc).To(corr.InRequest);
            b.From(resSrc).To(corr.InResponse);
            b.From(corr.OutResponse).To(responseSink);
            b.From(corr.OutControl).To(s);

            return ClosedShape.Instance;
        }));

        var task = graph.Run(Materializer);
        var signals = await task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var items = signals.ToList();
        // 2 StreamAcquireItems (requests) + 2 ConnectionReuseItems (responses) = 4 signals
        Assert.Equal(4, items.Count);
        Assert.Equal(2, items.Count(i => i is StreamAcquireItem));
        Assert.Equal(2, items.Count(i => i is ConnectionReuseItem));
    }
}
