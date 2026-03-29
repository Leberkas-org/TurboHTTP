using System.Collections.Immutable;
using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Streams.Stages.Features;

namespace TurboHttp.StreamTests.RFC9110;

/// <summary>
/// Tests the retry bidirectional stage per RFC 9110.
/// Verifies that the request direction forwards requests and buffers for retry,
/// and the response direction evaluates retry decisions internally.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="RetryBidiStage"/>.
/// RFC 9110 §9.2: Idempotency, safe methods, and retry semantics.
/// </remarks>
public sealed class RetryBidiStageTests : StreamTestBase
{
    private static readonly HttpRequestOptionsKey<int> AttemptCountKey = new("TurboHttp.RetryAttemptCount");

    /// <summary>
    /// Runs requests through the request direction (In1→Out1) of the BidiStage.
    /// The response direction is wired to empty source / ignored sink.
    /// </summary>
    private Task<IImmutableList<HttpRequestMessage>> RunRequestAsync(
        RetryBidiStage stage,
        params HttpRequestMessage[] requests)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpRequestMessage>(),
            (builder, sink) =>
            {
                var bidi = builder.Add(stage);
                var source = builder.Add(Source.From(requests));
                var neverResponseSource = builder.Add(Source.Maybe<HttpResponseMessage>());
                var ignoredSink = builder.Add(Sink.Ignore<HttpResponseMessage>());

                var take = builder.Add(Flow.Create<HttpRequestMessage>().Take(requests.Length));

                builder.From(source).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).Via(take).To(sink);
                builder.From(neverResponseSource).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(ignoredSink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

    /// <summary>
    /// Runs responses through the response direction (In2→Out2) of the BidiStage.
    /// The request direction is wired to empty source / ignored sink.
    /// </summary>
    private Task<IImmutableList<HttpResponseMessage>> RunResponseAsync(
        RetryBidiStage stage,
        params HttpResponseMessage[] responses)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpResponseMessage>(),
            (builder, sink) =>
            {
                var bidi = builder.Add(stage);
                var source = builder.Add(Source.From(responses));
                var emptyRequestSource = builder.Add(Source.Empty<HttpRequestMessage>());
                var ignoredSink = builder.Add(Sink.Ignore<HttpRequestMessage>());

                builder.From(emptyRequestSource).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).To(ignoredSink);
                builder.From(source).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(sink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

    /// <summary>
    /// Materialises a <see cref="RetryBidiStage"/> with manual subscriber probes on both outlets
    /// and a manual publisher probe on the response inlet. The request inlet receives the given
    /// requests concatenated with Source.Never to prevent premature completion.
    /// </summary>
    private (TestSubscriber.ManualProbe<HttpRequestMessage> requestOut,
        TestSubscriber.ManualProbe<HttpResponseMessage> responseOut,
        Action<HttpResponseMessage> pushResponse,
        Action completeResponse) RunManual(
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

        return (requestOutProbe, responseOutProbe, responseSub.SendNext, responseSub.SendComplete);
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

    // ============================
    // Pass-through tests (null policy)
    // ============================

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-9.2-RBIDI-001: null RetryPolicy → request passes through unchanged")]
    public async Task RequestDirection_Should_PassThrough_When_PolicyIsNull()
    {
        var stage = new RetryBidiStage(null);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Same(request, result);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-9.2-RBIDI-002: null RetryPolicy → response passes through unchanged")]
    public async Task ResponseDirection_Should_PassThrough_When_PolicyIsNull()
    {
        var stage = new RetryBidiStage(null);
        var response = BuildResponse(HttpStatusCode.ServiceUnavailable);

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        Assert.Same(response, result);
    }

    // ============================
    // Request direction tests
    // ============================

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-9.2-RBIDI-003: request forwarded on Out1")]
    public async Task RequestDirection_Should_ForwardRequest()
    {
        var stage = new RetryBidiStage(new RetryPolicy());
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Same(request, result);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-9.2-RBIDI-004: multiple requests forwarded in order")]
    public async Task RequestDirection_Should_ForwardMultipleRequestsInOrder()
    {
        var stage = new RetryBidiStage(new RetryPolicy());
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");

        var results = await RunRequestAsync(stage, req1, req2);

        Assert.Equal(2, results.Count);
        Assert.Same(req1, results[0]);
        Assert.Same(req2, results[1]);
    }

    // ============================
    // Response direction: non-retryable
    // ============================

    [Fact(DisplayName = "RFC9110-9.2-RBIDI-005: 200 OK → forwarded on Out2")]
    public void Should_ForwardFinalResponse_When_200OK()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RetryBidiStage(new RetryPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        // Consume the forwarded request
        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        // Push a 200 OK response
        var response = BuildResponse(HttpStatusCode.OK, request);
        pushResp(response);

        Assert.Same(response, respOut.ExpectNext(TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "RFC9110-9.2-RBIDI-006: 404 → forwarded on Out2")]
    public void Should_ForwardFinalResponse_When_404()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RetryBidiStage(new RetryPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        var response = BuildResponse(HttpStatusCode.NotFound, request);
        pushResp(response);

        Assert.Same(response, respOut.ExpectNext(TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "RFC9110-9.2-RBIDI-007: 408 on POST → forwarded on Out2 (not idempotent)")]
    public void Should_ForwardFinalResponse_When_PostReturns408()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/");
        var stage = new RetryBidiStage(new RetryPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        var response = BuildResponse(HttpStatusCode.RequestTimeout, request);
        pushResp(response);

        Assert.Same(response, respOut.ExpectNext(TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "RFC9110-9.2-RBIDI-008: null RequestMessage → forwarded on Out2")]
    public void Should_ForwardFinalResponse_When_RequestMessageIsNull()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RetryBidiStage(new RetryPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        var response = new HttpResponseMessage(HttpStatusCode.RequestTimeout);
        pushResp(response);

        Assert.Same(response, respOut.ExpectNext(TestContext.Current.CancellationToken));
    }

    // ============================
    // Response direction: retryable (immediate)
    // ============================

    [Fact(DisplayName = "RFC9110-9.2-RBIDI-009: 408 on GET → retry request emitted on Out1")]
    public void Should_EmitRetryOnOut1_When_GetReturns408()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RetryBidiStage(new RetryPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        // Original request forwarded
        Assert.Same(request, reqOut.ExpectNext(TestContext.Current.CancellationToken));

        // 408 response triggers retry
        var response = BuildResponse(HttpStatusCode.RequestTimeout, request);
        pushResp(response);

        // Retry request appears on Out1
        var retryReq = reqOut.ExpectNext(TestContext.Current.CancellationToken);
        Assert.Same(request, retryReq);

        // No response on Out2 (response was disposed)
        respOut.ExpectNoMsg(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "RFC9110-9.2-RBIDI-010: 503 on GET → retry request emitted on Out1")]
    public void Should_EmitRetryOnOut1_When_GetReturns503()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RetryBidiStage(new RetryPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        Assert.Same(request, reqOut.ExpectNext(TestContext.Current.CancellationToken));

        var response = BuildResponse(HttpStatusCode.ServiceUnavailable, request);
        pushResp(response);

        var retryReq = reqOut.ExpectNext(TestContext.Current.CancellationToken);
        Assert.Same(request, retryReq);
        respOut.ExpectNoMsg(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "RFC9110-9.2-RBIDI-011: attempt count incremented on retry")]
    public void Should_IncrementAttemptCount_When_Retrying()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RetryBidiStage(new RetryPolicy());
        var (reqOut, _, pushResp, _) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        var response = BuildResponse(HttpStatusCode.RequestTimeout, request);
        pushResp(response);

        var retryReq = reqOut.ExpectNext(TestContext.Current.CancellationToken);
        Assert.True(retryReq.Options.TryGetValue(AttemptCountKey, out var count));
        Assert.Equal(2, count);
    }

    [Fact(DisplayName = "RFC9110-9.2-RBIDI-012: retry limit reached → response forwarded on Out2")]
    public void Should_ForwardFinalResponse_When_RetryLimitReached()
    {
        var policy = new RetryPolicy { MaxRetries = 1 };
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RetryBidiStage(policy);
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        // MaxRetries=1, attemptCount starts at 1 → 1 >= 1 → no retry
        var response = BuildResponse(HttpStatusCode.RequestTimeout, request);
        pushResp(response);

        Assert.Same(response, respOut.ExpectNext(TestContext.Current.CancellationToken));
        reqOut.ExpectNoMsg(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
    }

    // ============================
    // Idempotent method coverage
    // ============================

    [Theory(DisplayName = "RFC9110-9.2-RBIDI-013: idempotent methods retry on 408")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public void Should_RetryOn408_When_MethodIsIdempotent(string methodName)
    {
        var method = new HttpMethod(methodName);
        var request = new HttpRequestMessage(method, "http://example.com/");
        var stage = new RetryBidiStage(new RetryPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        var response = BuildResponse(HttpStatusCode.RequestTimeout, request);
        pushResp(response);

        var retryReq = reqOut.ExpectNext(TestContext.Current.CancellationToken);
        Assert.Equal(method, retryReq.Method);
        respOut.ExpectNoMsg(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "RFC9110-9.2-RBIDI-014: 503 on PATCH → forwarded (not idempotent)")]
    public void Should_ForwardOnOut2_When_PatchReturns503()
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, "http://example.com/");
        var stage = new RetryBidiStage(new RetryPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        var response = BuildResponse(HttpStatusCode.ServiceUnavailable, request);
        pushResp(response);

        Assert.Same(response, respOut.ExpectNext(TestContext.Current.CancellationToken));
    }

    // ============================
    // Retry-After timer tests
    // ============================

    [Fact(DisplayName = "RFC9110-9.2-RBIDI-015: Retry-After: 0 → immediate retry on Out1")]
    public void Should_RetryImmediately_When_RetryAfterIsZero()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RetryBidiStage(new RetryPolicy { RespectRetryAfter = true });
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        var response = BuildResponse(HttpStatusCode.ServiceUnavailable, request, retryAfterSeconds: "0");
        pushResp(response);

        var retryReq = reqOut.ExpectNext(TestContext.Current.CancellationToken);
        Assert.Same(request, retryReq);
        respOut.ExpectNoMsg(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "RFC9110-9.2-RBIDI-016: Retry-After: 1 → delayed retry on Out1")]
    public void Should_DelayRetry_When_RetryAfterIsPositive()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RetryBidiStage(new RetryPolicy { RespectRetryAfter = true });
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

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

    // ============================
    // Mixed scenario tests
    // ============================

    [Fact(DisplayName = "RFC9110-9.2-RBIDI-017: final response passes through while retry timer is pending")]
    public void Should_PassFinalThrough_When_RetryTimerIsPending()
    {
        var requestA = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var requestB = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        var stage = new RetryBidiStage(new RetryPolicy { RespectRetryAfter = true });
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, requestA, requestB);

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

    [Fact(DisplayName = "RFC9110-9.2-RBIDI-018: retry priority over new request from In1")]
    public void Should_PrioritizeRetry_OverNewRequest()
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

    [Fact(DisplayName = "RFC9110-9.2-RBIDI-019: independent retry budgets for different requests")]
    public void Should_HaveIndependentRetryBudgets()
    {
        var policy = new RetryPolicy { MaxRetries = 2 };
        var requestA = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var requestB = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        var stage = new RetryBidiStage(policy);
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 10, 10, requestA, requestB);

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

    // ============================
    // Upstream failure handling
    // ============================

    [Fact(DisplayName = "RFC9110-9.2-RBIDI-020: request upstream failure absorbed")]
    public void Should_AbsorbRequestUpstreamFailure()
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

    [Fact(DisplayName = "RFC9110-9.2-RBIDI-021: response upstream failure absorbed")]
    public void Should_AbsorbResponseUpstreamFailure()
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

    // ============================
    // DL-009: Atomic re-injection (upstream finished + retry pending)
    // ============================

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-9.2-RBIDI-022: OutRequest stays open when upstream finished and retry is pending")]
    public void Should_KeepOutRequestOpen_When_UpstreamFinishedAndRetryPending()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-9.2-RBIDI-023: OutRequest completes when upstream finished and no retry pending")]
    public void Should_CompleteOutRequest_When_UpstreamFinishedAndNoRetryPending()
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
