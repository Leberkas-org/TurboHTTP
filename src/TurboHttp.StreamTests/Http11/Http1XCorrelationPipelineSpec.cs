using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.StreamTests.Http11;

/// <summary>
/// Tests the HTTP/1.x pipeline queue behaviour in Http1XCorrelationStage per RFC 9112 §9.3.
/// Verifies eager request pulling up to depth=8, FIFO response matching, Connection: close
/// depth downgrade, and PipelineRetryItem emission on abrupt connection close.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http1XCorrelationStage"/>.
/// RFC 9112 §9.3: Pipelining — client MAY send multiple requests without waiting for each response;
/// server MUST send responses in the same order as the corresponding requests.
/// </remarks>
public sealed class Http1XCorrelationPipelineSpec : StreamTestBase
{
    private static HttpResponseMessage OkResponse() => new(HttpStatusCode.OK);

    private static HttpResponseMessage CloseResponse()
    {
        var r = new HttpResponseMessage(HttpStatusCode.OK);
        r.Headers.Connection.Add("close");
        return r;
    }

    /// <summary>
    /// Builds probes for both inlets and both outlets, runs the graph, and returns all four handles.
    /// </summary>
    private (
        TestPublisher.ManualProbe<HttpRequestMessage> RequestProbe,
        TestPublisher.ManualProbe<HttpResponseMessage> ResponseProbe,
        TestSubscriber.ManualProbe<HttpResponseMessage> ResponseOut,
        TestSubscriber.ManualProbe<IOutputItem> SignalOut)
        CreateProbes()
    {
        var requestProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var responseProbe = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var responseOut = this.CreateManualSubscriberProbe<HttpResponseMessage>();
        var signalOut = this.CreateManualSubscriberProbe<IOutputItem>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(b =>
            {
                var stage = b.Add(new Http1XCorrelationStage());
                var reqSrc = b.Add(Source.FromPublisher(requestProbe));
                var resSrc = b.Add(Source.FromPublisher(responseProbe));

                b.From(reqSrc).To(stage.InRequest);
                b.From(resSrc).To(stage.InResponse);
                b.From(stage.OutResponse).To(Sink.FromSubscriber(responseOut));
                b.From(stage.OutControl).To(Sink.FromSubscriber(signalOut));

                return ClosedShape.Instance;
            }));

        graph.Run(Materializer);

        return (requestProbe, responseProbe, responseOut, signalOut);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http1XCorrelationPipeline_should_pull_five_requests_eagerly_and_match_responses_in_fifo_order()
    {
        var (requestProbe, responseProbe, responseOut, signalOut) = CreateProbes();

        var responseOutSub = responseOut.ExpectSubscription(TestContext.Current.CancellationToken);
        var signalOutSub = signalOut.ExpectSubscription(TestContext.Current.CancellationToken);
        var requestPubSub = requestProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var responsePubSub = responseProbe.ExpectSubscription(TestContext.Current.CancellationToken);

        // Request demand on both outlets upfront
        responseOutSub.Request(5);
        signalOutSub.Request(100);

        // Create 5 requests and 5 responses
        var requests = Enumerable.Range(1, 5)
            .Select(i => new HttpRequestMessage(HttpMethod.Get, $"http://example.com/{i}"))
            .ToArray();
        var responses = Enumerable.Range(0, 5).Select(_ => OkResponse()).ToArray();

        // Push all 5 requests — stage has depth=8 so all should be pulled eagerly
        foreach (var req in requests)
        {
            requestPubSub.SendNext(req);
        }

        // Verify 5 StreamAcquireItems are emitted (one per request pulled)
        for (var i = 0; i < 5; i++)
        {
            var sig = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.IsType<StreamAcquireItem>(sig);
        }

        // Push responses in order — FIFO matching must hold
        for (var i = 0; i < 5; i++)
        {
            responsePubSub.SendNext(responses[i]);
            var resp = responseOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.Same(requests[i], resp.RequestMessage);
            Assert.Same(responses[i], resp);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http1XCorrelationPipeline_should_reduce_depth_to_one_when_connection_close_arrives_with_multiple_in_flight()
    {
        var (requestProbe, responseProbe, responseOut, signalOut) = CreateProbes();

        var responseOutSub = responseOut.ExpectSubscription(TestContext.Current.CancellationToken);
        var signalOutSub = signalOut.ExpectSubscription(TestContext.Current.CancellationToken);
        var requestPubSub = requestProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var responsePubSub = responseProbe.ExpectSubscription(TestContext.Current.CancellationToken);

        responseOutSub.Request(10);
        signalOutSub.Request(100);

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/1");
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/2");
        var req3 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/3");

        // Push 3 requests — all fit within depth=8, stage pulls them eagerly
        requestPubSub.SendNext(req1);
        requestPubSub.SendNext(req2);
        requestPubSub.SendNext(req3);

        // Verify all 3 are acquired (pulled) before any response
        for (var i = 0; i < 3; i++)
        {
            var sig = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.IsType<StreamAcquireItem>(sig);
        }

        // First response arrives with Connection: close while 3 are in-flight — depth drops to 1
        var closeResp = CloseResponse();
        responsePubSub.SendNext(closeResp);
        var reuse1 = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<ConnectionReuseItem>(reuse1);
        var resp1 = responseOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Same(req1, resp1.RequestMessage);
        Assert.True(resp1.Headers.ConnectionClose);

        // Send responses 2 and 3 to drain the queue
        responsePubSub.SendNext(OkResponse());
        var reuse2 = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<ConnectionReuseItem>(reuse2);
        var resp2 = responseOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Same(req2, resp2.RequestMessage);

        responsePubSub.SendNext(OkResponse());
        var reuse3 = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<ConnectionReuseItem>(reuse3);
        var resp3 = responseOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Same(req3, resp3.RequestMessage);

        // After depth=1 is in effect, the stage must NOT eagerly pull a 4th request
        // even when 0 are in-flight (depth=1 means only 1 can be in flight at a time).
        // Push a 4th request and a new response — stage should pull exactly 1 more.
        var req4 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/4");
        requestPubSub.SendNext(req4);
        var sig4 = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<StreamAcquireItem>(sig4);

        // There must be no second request pulled before the first response arrives
        // (depth=1 gate: no second pull until previous cycle completes).
        var req5 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/5");
        requestPubSub.SendNext(req5);

        // Give the stage a moment to incorrectly pull req5 if the depth guard is broken
        signalOut.ExpectNoMsg(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        // Now deliver the response to req4 — only then should req5 be pulled
        responsePubSub.SendNext(OkResponse());
        var reuse4 = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<ConnectionReuseItem>(reuse4);
        var resp4 = responseOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Same(req4, resp4.RequestMessage);

        // Now req5 may be pulled (depth=1: queue was 0 after resp4 delivered)
        var sig5 = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<StreamAcquireItem>(sig5);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http1XCorrelationPipeline_should_keep_depth_at_eight_when_connection_close_arrives_with_single_in_flight()
    {
        var (requestProbe, responseProbe, responseOut, signalOut) = CreateProbes();

        var responseOutSub = responseOut.ExpectSubscription(TestContext.Current.CancellationToken);
        var signalOutSub = signalOut.ExpectSubscription(TestContext.Current.CancellationToken);
        var requestPubSub = requestProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var responsePubSub = responseProbe.ExpectSubscription(TestContext.Current.CancellationToken);

        responseOutSub.Request(10);
        signalOutSub.Request(100);

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/1");

        // Only 1 request in-flight
        requestPubSub.SendNext(req1);
        var sig1 = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<StreamAcquireItem>(sig1);

        // Response arrives with Connection: close but queue had only 1 item — depth stays 8
        var closeResp = CloseResponse();
        responsePubSub.SendNext(closeResp);
        // ConnectionReuseItem emitted for the response
        var reuse1 = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<ConnectionReuseItem>(reuse1);
        var resp1 = responseOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Same(req1, resp1.RequestMessage);
        Assert.True(resp1.Headers.ConnectionClose);

        // Verify depth is still 8: push 3 more requests and confirm all 3 are pulled eagerly
        // without waiting for intermediate responses (depth=8 allows it).
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/2");
        var req3 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/3");
        var req4 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/4");

        requestPubSub.SendNext(req2);
        requestPubSub.SendNext(req3);
        requestPubSub.SendNext(req4);

        // All 3 must arrive at the signal outlet without any responses in between
        var sig2 = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<StreamAcquireItem>(sig2);
        var sig3 = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<StreamAcquireItem>(sig3);
        var sig4 = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<StreamAcquireItem>(sig4);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http1XCorrelationPipeline_should_emit_three_pipeline_retry_items_when_connection_closes_with_three_in_flight()
    {
        var (requestProbe, responseProbe, responseOut, signalOut) = CreateProbes();

        var responseOutSub = responseOut.ExpectSubscription(TestContext.Current.CancellationToken);
        var signalOutSub = signalOut.ExpectSubscription(TestContext.Current.CancellationToken);
        var requestPubSub = requestProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var responsePubSub = responseProbe.ExpectSubscription(TestContext.Current.CancellationToken);

        responseOutSub.Request(10);
        // Need enough demand to receive 3 StreamAcquireItems + 3 PipelineRetryItems
        signalOutSub.Request(100);

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/1");
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/2");
        var req3 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/3");

        // Push 3 requests — eagerly pulled
        requestPubSub.SendNext(req1);
        requestPubSub.SendNext(req2);
        requestPubSub.SendNext(req3);

        // Consume the 3 StreamAcquireItems
        for (var i = 0; i < 3; i++)
        {
            var sig = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.IsType<StreamAcquireItem>(sig);
        }

        // Close the response upstream before any response arrives
        responsePubSub.SendComplete();

        // Stage must emit one PipelineRetryItem per orphaned in-flight request
        var retry1 = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        var retry2 = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        var retry3 = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        Assert.IsType<PipelineRetryItem>(retry1);
        Assert.IsType<PipelineRetryItem>(retry2);
        Assert.IsType<PipelineRetryItem>(retry3);

        // Each PipelineRetryItem carries the original request object (reference equality)
        var retried = new[]
        {
            ((PipelineRetryItem)retry1).Request,
            ((PipelineRetryItem)retry2).Request,
            ((PipelineRetryItem)retry3).Request
        };
        Assert.Contains(req1, retried);
        Assert.Contains(req2, retried);
        Assert.Contains(req3, retried);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http1XCorrelationPipeline_should_complete_after_all_responses_when_request_upstream_finishes_early()
    {
        var sink = Sink.Seq<HttpResponseMessage>();

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/1");
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/2");
        var req3 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/3");

        var resp1 = OkResponse();
        var resp2 = OkResponse();
        var resp3 = OkResponse();

        // Request upstream finishes after emitting exactly 3 requests
        var requestSource = Source.From(new[] { req1, req2, req3 });

        // Response upstream: delivers 3 responses then completes
        var responseSource = Source.From(new[] { resp1, resp2, resp3 });

        var graph = RunnableGraph.FromGraph(GraphDsl.Create(sink, (b, s) =>
        {
            var corr = b.Add(new Http1XCorrelationStage());
            var reqSrc = b.Add(requestSource);
            var resSrc = b.Add(responseSource);
            var signalSink = b.Add(Sink.Ignore<IOutputItem>().MapMaterializedValue(_ => Akka.NotUsed.Instance));

            b.From(reqSrc).To(corr.InRequest);
            b.From(resSrc).To(corr.InResponse);
            b.From(corr.OutResponse).To(s);
            b.From(corr.OutControl).To(signalSink);

            return ClosedShape.Instance;
        }));

        var task = graph.Run(Materializer);
        var results = await task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var list = results.ToList();

        // Stage must complete and deliver all 3 responses in FIFO order
        Assert.Equal(3, list.Count);
        Assert.Same(req1, list[0].RequestMessage);
        Assert.Same(req2, list[1].RequestMessage);
        Assert.Same(req3, list[2].RequestMessage);
    }
}
