using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHttp.Protocol;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Streams;

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

    [Fact(Timeout = 10_000, DisplayName = "RETRY-001: 200 OK on GET → forwarded on Out0 (final)")]
    public async Task RETRY_001_200OK_ForwardedOnOut0()
    {
        var response = BuildResponse(HttpStatusCode.OK);
        var (final, retry) = Run(new RetryStage(), 1, response);

        Assert.Same(response, final.ExpectNext());
        retry.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "RETRY-002: 404 Not Found → forwarded on Out0 (final)")]
    public async Task RETRY_002_404_ForwardedOnOut0()
    {
        var response = BuildResponse(HttpStatusCode.NotFound);
        var (final, retry) = Run(new RetryStage(), 1, response);

        Assert.Same(response, final.ExpectNext());
        retry.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "RETRY-003: 500 Internal Server Error → forwarded on Out0 (not retryable)")]
    public async Task RETRY_003_500_ForwardedOnOut0()
    {
        var response = BuildResponse(HttpStatusCode.InternalServerError);
        var (final, retry) = Run(new RetryStage(), 1, response);

        Assert.Same(response, final.ExpectNext());
        retry.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    // ── 408 triggers retry for idempotent methods ─────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RETRY-004: 408 on GET → retry request emitted on Out1")]
    public async Task RETRY_004_408_GET_EmitsRetryOnOut1()
    {
        var response = BuildResponse(HttpStatusCode.RequestTimeout, HttpMethod.Get);
        var (final, retry) = Run(new RetryStage(), 1, response);

        var retryRequest = retry.ExpectNext();
        Assert.Equal(HttpMethod.Get, retryRequest.Method);
        final.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "RETRY-005: 503 on GET → retry request emitted on Out1")]
    public async Task RETRY_005_503_GET_EmitsRetryOnOut1()
    {
        var response = BuildResponse(HttpStatusCode.ServiceUnavailable, HttpMethod.Get);
        var (final, retry) = Run(new RetryStage(), 1, response);

        var retryRequest = retry.ExpectNext();
        Assert.Equal(HttpMethod.Get, retryRequest.Method);
        final.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    // ── non-idempotent methods are never retried ──────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RETRY-006: 408 on POST → forwarded on Out0 (not idempotent)")]
    public async Task RETRY_006_408_POST_ForwardedOnOut0()
    {
        var response = BuildResponse(HttpStatusCode.RequestTimeout, HttpMethod.Post);
        var (final, retry) = Run(new RetryStage(), 1, response);

        Assert.Same(response, final.ExpectNext());
        retry.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "RETRY-007: 503 on PATCH → forwarded on Out0 (not idempotent)")]
    public async Task RETRY_007_503_PATCH_ForwardedOnOut0()
    {
        var response = BuildResponse(HttpStatusCode.ServiceUnavailable, HttpMethod.Patch);
        var (final, retry) = Run(new RetryStage(), 1, response);

        Assert.Same(response, final.ExpectNext());
        retry.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    // ── retry limit enforcement ────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RETRY-008: retry limit of 1 → second 408 forwarded as final on Out0")]
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

    [Fact(Timeout = 10_000, DisplayName = "RETRY-009: response with null RequestMessage → passes through on Out0")]
    public async Task RETRY_009_NullRequestMessage_ForwardedOnOut0()
    {
        // No RequestMessage — evaluator cannot determine idempotency.
        var response = new HttpResponseMessage(HttpStatusCode.RequestTimeout);

        var (final, retry) = Run(new RetryStage(), 1, response);

        Assert.Same(response, final.ExpectNext());
        retry.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    // ── retry preserves original request ─────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RETRY-010: retry request on Out1 is the original RequestMessage")]
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

    [Fact(Timeout = 10_000, DisplayName = "RETRY-011: Retry-After: 0 on 503 GET → immediate retry on Out1")]
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

    [Fact(Timeout = 10_000, DisplayName = "RETRY-012: null policy constructor → uses RetryPolicy.Default")]
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

    [Theory(Timeout = 10_000, DisplayName = "RETRY-013: idempotent methods retry on 408")]
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
}
