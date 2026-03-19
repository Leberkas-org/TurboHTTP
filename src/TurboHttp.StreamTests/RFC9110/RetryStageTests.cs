using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.RFC9110;

public sealed class RetryStageTests : StreamTestBase
{
    // ── helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Materialises a <see cref="RetryStage"/> with manual subscriber probes,
    /// gives each outlet <paramref name="demandEach"/> demand, and returns the probes.
    /// Source is concatenated with Source.Never to prevent premature completion.
    /// </summary>
    private (TestSubscriber.ManualProbe<HttpResponseMessage> final,
             TestSubscriber.ManualProbe<HttpRequestMessage> retry) Run(
        RetryStage stage,
        int demandEach,
        params HttpResponseMessage[] responses)
    {
        var probeFinal = this.CreateManualSubscriberProbe<HttpResponseMessage>();
        var probeRetry = this.CreateManualSubscriberProbe<HttpRequestMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s   = b.Add(stage);
            var src = b.Add(Source.From(responses).Concat(Source.Never<HttpResponseMessage>()));

            b.From(src).To(s.In);
            b.From(s.Out0).To(Sink.FromSubscriber(probeFinal));
            b.From(s.Out1).To(Sink.FromSubscriber(probeRetry));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var subFinal = probeFinal.ExpectSubscription();
        var subRetry = probeRetry.ExpectSubscription();

        subFinal.Request(demandEach);
        subRetry.Request(demandEach);

        return (probeFinal, probeRetry);
    }

    /// <summary>Builds a response with a given status code and optional Retry-After header.</summary>
    private static HttpResponseMessage BuildResponse(
        HttpStatusCode statusCode,
        HttpMethod? method = null,
        string requestUri = "http://example.com/resource",
        string? retryAfterSeconds = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            RequestMessage = new HttpRequestMessage(method ?? HttpMethod.Get, requestUri)
        };
        if (retryAfterSeconds is not null)
        {
            response.Headers.TryAddWithoutValidation("Retry-After", retryAfterSeconds);
        }

        return response;
    }

    // ── non-retriable responses pass through on Out0 ───────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-9.2.2-RTRY-001: 200 OK on GET → forwarded on Out0 (final)")]
    public async Task RETRY_001_200OK_ForwardedOnOut0()
    {
        var response = BuildResponse(HttpStatusCode.OK);
        var (final, retry) = Run(new RetryStage(), 1, response);

        Assert.Same(response, final.ExpectNext());
        retry.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-9.2.2-RTRY-002: 404 Not Found → forwarded on Out0 (final)")]
    public async Task RETRY_002_404_ForwardedOnOut0()
    {
        var response = BuildResponse(HttpStatusCode.NotFound);
        var (final, retry) = Run(new RetryStage(), 1, response);

        Assert.Same(response, final.ExpectNext());
        retry.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-9.2.2-RTRY-003: 500 Internal Server Error → forwarded on Out0 (not retryable)")]
    public async Task RETRY_003_500_ForwardedOnOut0()
    {
        var response = BuildResponse(HttpStatusCode.InternalServerError);
        var (final, retry) = Run(new RetryStage(), 1, response);

        Assert.Same(response, final.ExpectNext());
        retry.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    // ── 408 triggers retry for idempotent methods ─────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-9.2.2-RTRY-004: 408 on GET → retry request emitted on Out1")]
    public async Task RETRY_004_408_GET_EmitsRetryOnOut1()
    {
        var response = BuildResponse(HttpStatusCode.RequestTimeout, HttpMethod.Get);
        var (final, retry) = Run(new RetryStage(), 1, response);

        var retryRequest = retry.ExpectNext();
        Assert.Equal(HttpMethod.Get, retryRequest.Method);
        final.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-9.2.2-RTRY-005: 503 on GET → retry request emitted on Out1")]
    public async Task RETRY_005_503_GET_EmitsRetryOnOut1()
    {
        var response = BuildResponse(HttpStatusCode.ServiceUnavailable, HttpMethod.Get);
        var (final, retry) = Run(new RetryStage(), 1, response);

        var retryRequest = retry.ExpectNext();
        Assert.Equal(HttpMethod.Get, retryRequest.Method);
        final.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    // ── non-idempotent methods are never retried ──────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-9.2.2-RTRY-006: 408 on POST → forwarded on Out0 (not idempotent)")]
    public async Task RETRY_006_408_POST_ForwardedOnOut0()
    {
        var response = BuildResponse(HttpStatusCode.RequestTimeout, HttpMethod.Post);
        var (final, retry) = Run(new RetryStage(), 1, response);

        Assert.Same(response, final.ExpectNext());
        retry.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-9.2.2-RTRY-007: 503 on PATCH → forwarded on Out0 (not idempotent)")]
    public async Task RETRY_007_503_PATCH_ForwardedOnOut0()
    {
        var response = BuildResponse(HttpStatusCode.ServiceUnavailable, HttpMethod.Patch);
        var (final, retry) = Run(new RetryStage(), 1, response);

        Assert.Same(response, final.ExpectNext());
        retry.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    // ── retry limit enforcement ────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-9.2.2-RTRY-008: retry limit of 1 → second 408 forwarded as final on Out0")]
    public async Task RETRY_008_RetryLimitExhausted_ForwardedOnOut0()
    {
        var policy = new RetryPolicy { MaxRetries = 1 };
        // With MaxRetries = 1 and attemptCount starting at 1, the first 408 already fails the
        // limit check (attemptCount >= MaxRetries), so the response goes to Out0.
        var response = BuildResponse(HttpStatusCode.RequestTimeout, HttpMethod.Get);
        var (final, retry) = Run(new RetryStage(policy), 1, response);

        Assert.Same(response, final.ExpectNext());
        retry.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    // ── null RequestMessage ────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-9.2.2-RTRY-009: response with null RequestMessage → passes through on Out0")]
    public async Task RETRY_009_NullRequestMessage_ForwardedOnOut0()
    {
        // No RequestMessage — evaluator cannot determine idempotency.
        var response = new HttpResponseMessage(HttpStatusCode.RequestTimeout);

        var (final, retry) = Run(new RetryStage(), 1, response);

        Assert.Same(response, final.ExpectNext());
        retry.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    // ── retry preserves original request ─────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-9.2.2-RTRY-010: retry request on Out1 is the original RequestMessage")]
    public async Task RETRY_010_RetryRequest_IsOriginalRequestMessage()
    {
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");
        var response = new HttpResponseMessage(HttpStatusCode.RequestTimeout)
        {
            RequestMessage = original
        };

        var (final, retry) = Run(new RetryStage(), 1, response);

        var retryRequest = retry.ExpectNext();
        Assert.Same(original, retryRequest);
        final.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    // ── Retry-After delay respected ────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-9.2.2-RTRY-011: Retry-After: 0 on 503 GET → immediate retry on Out1")]
    public async Task RETRY_011_RetryAfter_Zero_ImmediateRetry()
    {
        var response = BuildResponse(HttpStatusCode.ServiceUnavailable, HttpMethod.Get,
            retryAfterSeconds: "0");

        var (final, retry) = Run(new RetryStage(), 1, response);

        var retryRequest = retry.ExpectNext();
        Assert.Equal(HttpMethod.Get, retryRequest.Method);
        final.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    // ── default policy ─────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-9.2.2-RTRY-012: null policy constructor → uses RetryPolicy.Default")]
    public async Task RETRY_012_NullPolicy_UsesDefault()
    {
        var stage = new RetryStage(null);
        var response = BuildResponse(HttpStatusCode.RequestTimeout, HttpMethod.Delete);

        var (final, retry) = Run(stage, 1, response);

        // Default policy allows retries for idempotent methods.
        var retryRequest = retry.ExpectNext();
        Assert.Equal(HttpMethod.Delete, retryRequest.Method);
        final.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    // ── idempotent method coverage ─────────────────────────────────────────────

    [Theory(Timeout = 10_000, DisplayName = "RFC9110-9.2.2-RTRY-013: idempotent methods retry on 408")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task RETRY_013_IdempotentMethods_RetryOn408(string methodName)
    {
        var method = new HttpMethod(methodName);
        var response = BuildResponse(HttpStatusCode.RequestTimeout, method);

        var (final, retry) = Run(new RetryStage(), 1, response);

        var retryRequest = retry.ExpectNext();
        Assert.Equal(method, retryRequest.Method);
        final.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    // ── per-request retry isolation ────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-9.2.2-RTRY-014: Request B starts at attempt 1 after Request A retried 2x")]
    public async Task RETRY_014_SequentialRequests_IndependentRetryBudgets()
    {
        // MaxRetries = 3 means attempts 1 and 2 are retried, attempt 3 is final.
        // Request A: two 503 responses → retried twice (attempts 1, 2).
        // Request B: one 503 response → should be retried (attempt 1), NOT treated as attempt 3.
        var policy = new RetryPolicy { MaxRetries = 3 };

        var requestA = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var responseA1 = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = requestA };
        var responseA2 = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = requestA };

        var requestB = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        var responseB1 = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = requestB };

        // Feed all three responses sequentially through the stage.
        var (final, retry) = Run(new RetryStage(policy), 5, responseA1, responseA2, responseB1);

        // A1 → retry (attempt 1 < MaxRetries)
        var retryA1 = retry.ExpectNext();
        Assert.Same(requestA, retryA1);

        // A2 → retry (attempt 2 < MaxRetries)
        var retryA2 = retry.ExpectNext();
        Assert.Same(requestA, retryA2);

        // B1 → retry (attempt 1 < MaxRetries — independent of A's count)
        var retryB1 = retry.ExpectNext();
        Assert.Same(requestB, retryB1);

        final.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-9.2.2-RTRY-015: Interleaved requests each get independent retry budgets")]
    public async Task RETRY_015_InterleavedRequests_IndependentRetryBudgets()
    {
        var policy = new RetryPolicy { MaxRetries = 2 };

        var requestA = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var requestB = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");

        var responseA1 = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = requestA };
        var responseB1 = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = requestB };
        var responseA2 = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = requestA };
        var responseB2 = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = requestB };

        // Interleaved: A, B, A, B — with MaxRetries=2, first attempt of each should retry,
        // second attempt (attemptCount=2 >= MaxRetries=2) should be final.
        var (final, retry) = Run(new RetryStage(policy), 5, responseA1, responseB1, responseA2, responseB2);

        // A1 → retry (attempt 1)
        var retryA1 = retry.ExpectNext();
        Assert.Same(requestA, retryA1);

        // B1 → retry (attempt 1, independent of A)
        var retryB1 = retry.ExpectNext();
        Assert.Same(requestB, retryB1);

        // A2 → final (attempt 2 >= MaxRetries)
        var finalA = final.ExpectNext();
        Assert.Same(responseA2, finalA);

        // B2 → final (attempt 2 >= MaxRetries, independent of A)
        var finalB = final.ExpectNext();
        Assert.Same(responseB2, finalB);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-9.2.2-RTRY-016: Retry-After on Request A does not affect Request B's attempt count")]
    public async Task RETRY_016_RetryAfterTimer_DoesNotAffectOtherRequests()
    {
        // Request A has Retry-After: 0 (immediate). Request B also gets 503.
        // Both should be retried independently — B at attempt 1, not affected by A's state.
        var policy = new RetryPolicy { MaxRetries = 3, RespectRetryAfter = true };

        var requestA = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var responseA = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = requestA };
        responseA.Headers.TryAddWithoutValidation("Retry-After", "0");

        var requestB = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        var responseB = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = requestB };

        var (final, retry) = Run(new RetryStage(policy), 5, responseA, responseB);

        // A → retried (attempt 1, with Retry-After: 0 → immediate)
        var retryA = retry.ExpectNext();
        Assert.Same(requestA, retryA);

        // B → retried (attempt 1, independent of A)
        var retryB = retry.ExpectNext();
        Assert.Same(requestB, retryB);

        final.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    // ── concurrent retry tests ──────────────────────────────────────────────

    /// <summary>
    /// Materialises a <see cref="RetryStage"/> with a manual publisher probe on the inlet
    /// so that responses can be pushed individually, enabling interleaved push/assert sequences.
    /// Returns the publisher subscription (for SendNext/SendComplete) and subscriber probes.
    /// </summary>
    /// <summary>
    /// Holds actions for pushing to the source and subscriber probes for assertion.
    /// </summary>
    private sealed class ManualHarness
    {
        private readonly Action<HttpResponseMessage> _push;
        private readonly Action _complete;

        public TestSubscriber.ManualProbe<HttpResponseMessage> FinalProbe { get; }
        public TestSubscriber.ManualProbe<HttpRequestMessage> RetryProbe { get; }

        public ManualHarness(
            Action<HttpResponseMessage> push,
            Action complete,
            TestSubscriber.ManualProbe<HttpResponseMessage> finalProbe,
            TestSubscriber.ManualProbe<HttpRequestMessage> retryProbe)
        {
            _push = push;
            _complete = complete;
            FinalProbe = finalProbe;
            RetryProbe = retryProbe;
        }

        public void Push(HttpResponseMessage item) => _push(item);
        public void Complete() => _complete();
    }

    private ManualHarness RunManual(
        RetryStage stage,
        int finalDemand,
        int retryDemand)
    {
        var probeSource = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var probeFinal = this.CreateManualSubscriberProbe<HttpResponseMessage>();
        var probeRetry = this.CreateManualSubscriberProbe<HttpRequestMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            var src = b.Add(Source.FromPublisher(probeSource));

            b.From(src).To(s.In);
            b.From(s.Out0).To(Sink.FromSubscriber(probeFinal));
            b.From(s.Out1).To(Sink.FromSubscriber(probeRetry));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var sourceSub = probeSource.ExpectSubscription();
        var subFinal = probeFinal.ExpectSubscription();
        var subRetry = probeRetry.ExpectSubscription();

        subFinal.Request(finalDemand);
        subRetry.Request(retryDemand);

        return new ManualHarness(
            sourceSub.SendNext,
            sourceSub.SendComplete,
            probeFinal,
            probeRetry);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-9.2.2-RTRY-017: Final response passes through while Retry-After timer is running")]
    public async Task RETRY_017_ConcurrentPassThrough_WhileTimerRunning()
    {
        // Request A gets 503 with Retry-After: 10 (long delay).
        // Request B gets 200 OK — should pass through on Out0 immediately.
        var stage = new RetryStage();
        var h = RunManual(stage, finalDemand: 5, retryDemand: 5);

        // Push a 503 with a long Retry-After for Request A.
        var requestA = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var responseA = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = requestA };
        responseA.Headers.TryAddWithoutValidation("Retry-After", "10");

        h.Push(responseA);

        // Request A should NOT appear on either outlet yet (timer pending, no final push).
        h.FinalProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

        // Push a 200 OK for Request B — should flow through on Out0 immediately.
        var requestB = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        var responseB = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = requestB };

        h.Push(responseB);

        var finalResponse = h.FinalProbe.ExpectNext();
        Assert.Same(responseB, finalResponse);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-9.2.2-RTRY-018: Multiple immediate retries drain sequentially on demand")]
    public async Task RETRY_018_ReadyQueueDrain_MultipleImmediateRetries()
    {
        var stage = new RetryStage();
        var h = RunManual(stage, finalDemand: 5, retryDemand: 5);

        // Push three 503 responses without Retry-After — all go to ready queue.
        var requestA = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var responseA = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = requestA };
        h.Push(responseA);

        var retryA = h.RetryProbe.ExpectNext();
        Assert.Same(requestA, retryA);

        var requestB = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        var responseB = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = requestB };
        h.Push(responseB);

        var retryB = h.RetryProbe.ExpectNext();
        Assert.Same(requestB, retryB);

        var requestC = new HttpRequestMessage(HttpMethod.Get, "http://example.com/c");
        var responseC = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = requestC };
        h.Push(responseC);

        var retryC = h.RetryProbe.ExpectNext();
        Assert.Same(requestC, retryC);

        // No final responses should have been emitted.
        h.FinalProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-9.2.2-RTRY-019: Stage completes after upstream finish and all pending retries drained")]
    public async Task RETRY_019_DrainOnUpstreamFinish()
    {
        var stage = new RetryStage();
        var h = RunManual(stage, finalDemand: 5, retryDemand: 5);

        // Push a 503 immediate retry, then complete upstream.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/drain");
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request };
        h.Push(response);

        // Retry should be emitted.
        var retryRequest = h.RetryProbe.ExpectNext();
        Assert.Same(request, retryRequest);

        // Complete upstream — stage should complete since no more pending retries.
        h.Complete();

        // Both outlets should see completion.
        h.FinalProbe.ExpectComplete();
        h.RetryProbe.ExpectComplete();
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-9.2.2-RTRY-020: Mixed final and retry responses interleave correctly")]
    public async Task RETRY_020_MixedFinalAndRetry_Interleaved()
    {
        var stage = new RetryStage();
        var h = RunManual(stage, finalDemand: 5, retryDemand: 5);

        // 503 GET → retry
        var requestA = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var responseA = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = requestA };
        h.Push(responseA);
        Assert.Same(requestA, h.RetryProbe.ExpectNext());

        // 200 OK → final
        var requestB = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        var responseB = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = requestB };
        h.Push(responseB);
        Assert.Same(responseB, h.FinalProbe.ExpectNext());

        // 408 DELETE → retry
        var requestC = new HttpRequestMessage(HttpMethod.Delete, "http://example.com/c");
        var responseC = new HttpResponseMessage(HttpStatusCode.RequestTimeout) { RequestMessage = requestC };
        h.Push(responseC);
        Assert.Same(requestC, h.RetryProbe.ExpectNext());

        // 404 → final
        var requestD = new HttpRequestMessage(HttpMethod.Get, "http://example.com/d");
        var responseD = new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = requestD };
        h.Push(responseD);
        Assert.Same(responseD, h.FinalProbe.ExpectNext());
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-9.2.2-RTRY-021: Parallel Retry-After timers fire independently")]
    public async Task RETRY_021_ParallelTimers_FireIndependently()
    {
        // Two requests with short Retry-After delays — both should fire their timers independently.
        var stage = new RetryStage();
        var h = RunManual(stage, finalDemand: 5, retryDemand: 5);

        // Request A: 503 with Retry-After: 1 second
        var requestA = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var responseA = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = requestA };
        responseA.Headers.TryAddWithoutValidation("Retry-After", "1");
        h.Push(responseA);

        // Request B: 503 with Retry-After: 1 second
        var requestB = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        var responseB = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = requestB };
        responseB.Headers.TryAddWithoutValidation("Retry-After", "1");
        h.Push(responseB);

        // Neither should appear immediately.
        h.RetryProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));

        // Both should appear after ~1 second (within 3 second window).
        var retryFirst = h.RetryProbe.ExpectNext(TimeSpan.FromSeconds(3));
        var retrySecond = h.RetryProbe.ExpectNext(TimeSpan.FromSeconds(3));

        // Both original requests should have been retried (order may vary).
        var retried = new[] { retryFirst, retrySecond };
        Assert.Contains(requestA, retried);
        Assert.Contains(requestB, retried);

        h.FinalProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }
}
