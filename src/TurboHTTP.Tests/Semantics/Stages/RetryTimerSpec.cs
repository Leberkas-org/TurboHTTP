using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Semantics.Stages;

public sealed class RetryTimerSpec : StreamTestBase
{
    private (TestSubscriber.ManualProbe<HttpRequestMessage> requestOut,
        TestSubscriber.ManualProbe<HttpResponseMessage> responseOut,
        Action<HttpResponseMessage> pushResponse) RunManual(
            RetryBidiStage stage,
            int requestOutDemand,
            int responseOutDemand,
            params HttpRequestMessage[] requests)
    {
        var responsePublisher = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var requestOutProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var responseOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var bidi = b.Add(stage);
            var reqSrc = b.Add(Source.From(requests).Concat(Source.Never<HttpRequestMessage>()));
            var respSrc = b.Add(Source.FromPublisher(responsePublisher));

            b.From(reqSrc).To(bidi.Inlet1);
            b.From(bidi.Outlet1).To(Sink.FromSubscriber(requestOutProbe));
            b.From(respSrc).To(bidi.Inlet2);
            b.From(bidi.Outlet2).To(Sink.FromSubscriber(responseOutProbe));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var responseSub = responsePublisher.ExpectSubscription(TestContext.Current.CancellationToken);
        var reqOutSub = requestOutProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var respOutSub = responseOutProbe.ExpectSubscription(TestContext.Current.CancellationToken);

        reqOutSub.Request(requestOutDemand);
        respOutSub.Request(responseOutDemand);

        return (requestOutProbe, responseOutProbe, responseSub.SendNext);
    }

    private static HttpResponseMessage BuildResponse(
        HttpStatusCode statusCode,
        HttpRequestMessage? requestMessage = null,
        string? retryAfterSeconds = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            RequestMessage = requestMessage ?? new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource")
        };
        if (retryAfterSeconds is not null)
        {
            response.Headers.TryAddWithoutValidation("Retry-After", retryAfterSeconds);
        }

        return response;
    }

    [Fact]
    [Trait("RFC", "RFC9110-9.2")]
    public void RetryTimer_should_retry_immediately_when_retry_after_is_zero()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RetryBidiStage(new RetryPolicy { RespectRetryAfter = true });
        var (reqOut, respOut, pushResp) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        var response = BuildResponse(HttpStatusCode.ServiceUnavailable, request, retryAfterSeconds: "0");
        pushResp(response);

        var retryReq = reqOut.ExpectNext(TestContext.Current.CancellationToken);
        Assert.Same(request, retryReq);
        respOut.ExpectNoMsg(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("RFC", "RFC9110-9.2")]
    public void RetryTimer_should_delay_retry_when_retry_after_is_positive()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RetryBidiStage(new RetryPolicy { RespectRetryAfter = true });
        var (reqOut, respOut, pushResp) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        var response = BuildResponse(HttpStatusCode.ServiceUnavailable, request, retryAfterSeconds: "1");
        pushResp(response);

        // Should NOT appear immediately
        reqOut.ExpectNoMsg(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        // Should appear after the delay
        var retryReq = reqOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Same(request, retryReq);
        respOut.ExpectNoMsg(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("RFC", "RFC9110-9.2")]
    public void RetryTimer_should_pass_final_through_when_retry_timer_is_pending()
    {
        var requestA = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var requestB = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        var stage = new RetryBidiStage(new RetryPolicy { RespectRetryAfter = true });
        var (reqOut, respOut, pushResp) = RunManual(stage, 5, 5, requestA, requestB);

        // Both requests forwarded
        Assert.Same(requestA, reqOut.ExpectNext(TestContext.Current.CancellationToken));
        Assert.Same(requestB, reqOut.ExpectNext(TestContext.Current.CancellationToken));

        // Request A gets 503 with long Retry-After
        var responseA = BuildResponse(HttpStatusCode.ServiceUnavailable, requestA, retryAfterSeconds: "10");
        pushResp(responseA);

        // Request B gets 200 OK — should pass through immediately
        var responseB = BuildResponse(HttpStatusCode.OK, requestB);
        pushResp(responseB);

        Assert.Same(responseB, respOut.ExpectNext(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("RFC", "RFC9110-9.2")]
    public void RetryTimer_should_prioritize_retry_over_new_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RetryBidiStage(new RetryPolicy());

        var responsePublisher = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var requestPublisher = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var requestOutProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var responseOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var bidi = b.Add(stage);
            var reqSrc = b.Add(Source.FromPublisher(requestPublisher));
            var respSrc = b.Add(Source.FromPublisher(responsePublisher));

            b.From(reqSrc).To(bidi.Inlet1);
            b.From(bidi.Outlet1).To(Sink.FromSubscriber(requestOutProbe));
            b.From(respSrc).To(bidi.Inlet2);
            b.From(bidi.Outlet2).To(Sink.FromSubscriber(responseOutProbe));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var reqPubSub = requestPublisher.ExpectSubscription(TestContext.Current.CancellationToken);
        var respPubSub = responsePublisher.ExpectSubscription(TestContext.Current.CancellationToken);
        var reqOutSub = requestOutProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var respOutSub = responseOutProbe.ExpectSubscription(TestContext.Current.CancellationToken);

        // Issue demand on both outlets
        reqOutSub.Request(5);
        respOutSub.Request(5);

        // Push a request
        reqPubSub.SendNext(request);
        Assert.Same(request, requestOutProbe.ExpectNext(TestContext.Current.CancellationToken));

        // Push a 503 → triggers retry. The retry request should be emitted on Out1 next.
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request };
        respPubSub.SendNext(response);

        // The retry request should appear on Out1 (priority over pulling In1 for new requests)
        var retryReq = requestOutProbe.ExpectNext(TestContext.Current.CancellationToken);
        Assert.Same(request, retryReq);
    }

    [Fact]
    [Trait("RFC", "RFC9110-9.2")]
    public void RetryTimer_should_have_independent_retry_budgets()
    {
        var policy = new RetryPolicy { MaxRetries = 2 };
        var requestA = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var requestB = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        var stage = new RetryBidiStage(policy);
        var (reqOut, respOut, pushResp) = RunManual(stage, 10, 10, requestA, requestB);

        // Both forwarded
        Assert.Same(requestA, reqOut.ExpectNext(TestContext.Current.CancellationToken));
        Assert.Same(requestB, reqOut.ExpectNext(TestContext.Current.CancellationToken));

        // A gets 503 → retry (attempt 1)
        pushResp(BuildResponse(HttpStatusCode.ServiceUnavailable, requestA));
        Assert.Same(requestA, reqOut.ExpectNext(TestContext.Current.CancellationToken));

        // B gets 503 → retry (attempt 1, independent of A)
        pushResp(BuildResponse(HttpStatusCode.ServiceUnavailable, requestB));
        Assert.Same(requestB, reqOut.ExpectNext(TestContext.Current.CancellationToken));

        // A gets 503 again → final (attempt 2 >= MaxRetries)
        var responseA2 = BuildResponse(HttpStatusCode.ServiceUnavailable, requestA);
        pushResp(responseA2);
        Assert.Same(responseA2, respOut.ExpectNext(TestContext.Current.CancellationToken));

        // B gets 503 again → final (attempt 2 >= MaxRetries)
        var responseB2 = BuildResponse(HttpStatusCode.ServiceUnavailable, requestB);
        pushResp(responseB2);
        Assert.Same(responseB2, respOut.ExpectNext(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("RFC", "RFC9110-9.2")]
    public void RetryTimer_should_absorb_request_upstream_failure()
    {
        var stage = new RetryBidiStage(new RetryPolicy());

        var requestPublisher = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var responsePublisher = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var requestOutProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var responseOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var bidi = b.Add(stage);
            b.From(Source.FromPublisher(requestPublisher)).To(bidi.Inlet1);
            b.From(bidi.Outlet1).To(Sink.FromSubscriber(requestOutProbe));
            b.From(Source.FromPublisher(responsePublisher)).To(bidi.Inlet2);
            b.From(bidi.Outlet2).To(Sink.FromSubscriber(responseOutProbe));
            return ClosedShape.Instance;
        })).Run(Materializer);

        var reqPubSub = requestPublisher.ExpectSubscription(TestContext.Current.CancellationToken);
        responsePublisher.ExpectSubscription(TestContext.Current.CancellationToken);
        requestOutProbe.ExpectSubscription(TestContext.Current.CancellationToken).Request(5);
        responseOutProbe.ExpectSubscription(TestContext.Current.CancellationToken).Request(5);

        reqPubSub.SendError(new Exception("request boom"));

        // Stage absorbs the error (no OnError) but gracefully completes _outRequest
        requestOutProbe.ExpectComplete(TestContext.Current.CancellationToken);
        responseOutProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("RFC", "RFC9110-9.2")]
    public void RetryTimer_should_absorb_response_upstream_failure()
    {
        var stage = new RetryBidiStage(new RetryPolicy());

        var requestPublisher = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var responsePublisher = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var requestOutProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var responseOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var bidi = b.Add(stage);
            b.From(Source.FromPublisher(requestPublisher)).To(bidi.Inlet1);
            b.From(bidi.Outlet1).To(Sink.FromSubscriber(requestOutProbe));
            b.From(Source.FromPublisher(responsePublisher)).To(bidi.Inlet2);
            b.From(bidi.Outlet2).To(Sink.FromSubscriber(responseOutProbe));
            return ClosedShape.Instance;
        })).Run(Materializer);

        requestPublisher.ExpectSubscription(TestContext.Current.CancellationToken);
        var respPubSub = responsePublisher.ExpectSubscription(TestContext.Current.CancellationToken);
        requestOutProbe.ExpectSubscription(TestContext.Current.CancellationToken).Request(5);
        responseOutProbe.ExpectSubscription(TestContext.Current.CancellationToken).Request(5);

        respPubSub.SendError(new Exception("response boom"));

        // Stage absorbs the error (no OnError) but gracefully completes _outResponse
        requestOutProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
        responseOutProbe.ExpectComplete(TestContext.Current.CancellationToken);
    }

    // DL-009: Atomic re-injection (upstream finished + retry pending)

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9110-9.2")]
    public void RetryTimer_should_keep_out_request_open_when_upstream_finished_and_retry_pending()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RetryBidiStage(new RetryPolicy());

        var requestPublisher = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var responsePublisher = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var requestOutProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var responseOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var bidi = b.Add(stage);
            b.From(Source.FromPublisher(requestPublisher)).To(bidi.Inlet1);
            b.From(bidi.Outlet1).To(Sink.FromSubscriber(requestOutProbe));
            b.From(Source.FromPublisher(responsePublisher)).To(bidi.Inlet2);
            b.From(bidi.Outlet2).To(Sink.FromSubscriber(responseOutProbe));
            return ClosedShape.Instance;
        })).Run(Materializer);

        var reqPubSub = requestPublisher.ExpectSubscription(TestContext.Current.CancellationToken);
        var respPubSub = responsePublisher.ExpectSubscription(TestContext.Current.CancellationToken);
        var reqOutSub = requestOutProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var respOutSub = responseOutProbe.ExpectSubscription(TestContext.Current.CancellationToken);

        reqOutSub.Request(10);
        respOutSub.Request(10);

        // Push a request then complete upstream (no more requests)
        reqPubSub.SendNext(request);
        Assert.Same(request, requestOutProbe.ExpectNext(TestContext.Current.CancellationToken));
        reqPubSub.SendComplete();

        // Push a 503 response → triggers retry. Despite upstream being finished,
        // OutRequest must stay open because the retry is pending.
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request };
        respPubSub.SendNext(response);

        // The retry request must appear on OutRequest (stage did NOT close it prematurely)
        var retryReq = requestOutProbe.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Same(request, retryReq);

        // Now push a 200 for the retry → should pass through on Out2
        var finalResponse = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
        respPubSub.SendNext(finalResponse);
        Assert.Same(finalResponse, responseOutProbe.ExpectNext(TestContext.Current.CancellationToken));

        // Now OutRequest should complete (upstream done, no more retries, in-flight resolved)
        requestOutProbe.ExpectComplete(TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9110-9.2")]
    public void RetryTimer_should_complete_out_request_when_upstream_finished_and_no_retry_pending()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RetryBidiStage(new RetryPolicy());

        var requestPublisher = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var responsePublisher = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var requestOutProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var responseOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var bidi = b.Add(stage);
            b.From(Source.FromPublisher(requestPublisher)).To(bidi.Inlet1);
            b.From(bidi.Outlet1).To(Sink.FromSubscriber(requestOutProbe));
            b.From(Source.FromPublisher(responsePublisher)).To(bidi.Inlet2);
            b.From(bidi.Outlet2).To(Sink.FromSubscriber(responseOutProbe));
            return ClosedShape.Instance;
        })).Run(Materializer);

        var reqPubSub = requestPublisher.ExpectSubscription(TestContext.Current.CancellationToken);
        var respPubSub = responsePublisher.ExpectSubscription(TestContext.Current.CancellationToken);
        var reqOutSub = requestOutProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var respOutSub = responseOutProbe.ExpectSubscription(TestContext.Current.CancellationToken);

        reqOutSub.Request(10);
        respOutSub.Request(10);

        // Push a request then complete upstream
        reqPubSub.SendNext(request);
        Assert.Same(request, requestOutProbe.ExpectNext(TestContext.Current.CancellationToken));
        reqPubSub.SendComplete();

        // Push a 200 OK → no retry needed
        var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
        respPubSub.SendNext(response);

        // Final response passes through
        Assert.Same(response, responseOutProbe.ExpectNext(TestContext.Current.CancellationToken));

        // OutRequest should complete (upstream done, no retries, in-flight resolved)
        requestOutProbe.ExpectComplete(TestContext.Current.CancellationToken);
    }
}