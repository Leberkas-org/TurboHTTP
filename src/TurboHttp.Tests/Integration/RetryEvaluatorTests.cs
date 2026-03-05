using System.Net;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.Integration;

/// <summary>
/// Tests for <see cref="RetryEvaluator"/> and <see cref="RetryPolicy"/>.
/// RFC 9110 §9.2 — Idempotency-constrained automatic retry policy.
/// </summary>
public sealed class RetryEvaluatorTests
{
    // ── Idempotent method retry (network failure) ────────────────────────────

    [Fact(DisplayName = "RE-001: Should_Retry_When_GET_And_NetworkFailure")]
    public void Should_Retry_When_GET_And_NetworkFailure()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var decision = RetryEvaluator.Evaluate(request, networkFailure: true);

        Assert.True(decision.ShouldRetry);
    }

    [Fact(DisplayName = "RE-002: Should_Retry_When_HEAD_And_NetworkFailure")]
    public void Should_Retry_When_HEAD_And_NetworkFailure()
    {
        var request = new HttpRequestMessage(HttpMethod.Head, "https://example.com/");
        var decision = RetryEvaluator.Evaluate(request, networkFailure: true);

        Assert.True(decision.ShouldRetry);
    }

    [Fact(DisplayName = "RE-003: Should_Retry_When_PUT_And_NetworkFailure")]
    public void Should_Retry_When_PUT_And_NetworkFailure()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "https://example.com/resource");
        var decision = RetryEvaluator.Evaluate(request, networkFailure: true);

        Assert.True(decision.ShouldRetry);
    }

    [Fact(DisplayName = "RE-004: Should_Retry_When_DELETE_And_NetworkFailure")]
    public void Should_Retry_When_DELETE_And_NetworkFailure()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "https://example.com/resource");
        var decision = RetryEvaluator.Evaluate(request, networkFailure: true);

        Assert.True(decision.ShouldRetry);
    }

    [Fact(DisplayName = "RE-005: Should_Retry_When_OPTIONS_And_NetworkFailure")]
    public void Should_Retry_When_OPTIONS_And_NetworkFailure()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "https://example.com/");
        var decision = RetryEvaluator.Evaluate(request, networkFailure: true);

        Assert.True(decision.ShouldRetry);
    }

    [Fact(DisplayName = "RE-006: Should_Retry_When_TRACE_And_NetworkFailure")]
    public void Should_Retry_When_TRACE_And_NetworkFailure()
    {
        var request = new HttpRequestMessage(HttpMethod.Trace, "https://example.com/");
        var decision = RetryEvaluator.Evaluate(request, networkFailure: true);

        Assert.True(decision.ShouldRetry);
    }

    // ── Non-idempotent methods MUST NOT be retried ───────────────────────────

    [Fact(DisplayName = "RE-007: Should_NotRetry_When_POST_And_NetworkFailure")]
    public void Should_NotRetry_When_POST_And_NetworkFailure()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/submit");
        var decision = RetryEvaluator.Evaluate(request, networkFailure: true);

        Assert.False(decision.ShouldRetry);
        Assert.Contains("not idempotent", decision.Reason);
    }

    [Fact(DisplayName = "RE-008: Should_NotRetry_When_PATCH_And_NetworkFailure")]
    public void Should_NotRetry_When_PATCH_And_NetworkFailure()
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, "https://example.com/resource");
        var decision = RetryEvaluator.Evaluate(request, networkFailure: true);

        Assert.False(decision.ShouldRetry);
        Assert.Contains("not idempotent", decision.Reason);
    }

    [Fact(DisplayName = "RE-009: Should_NotRetry_When_POST_And_408Response")]
    public void Should_NotRetry_When_POST_And_408Response()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/submit");
        var response = new HttpResponseMessage((HttpStatusCode)408);
        var decision = RetryEvaluator.Evaluate(request, response);

        Assert.False(decision.ShouldRetry);
        Assert.Contains("not idempotent", decision.Reason);
    }

    [Fact(DisplayName = "RE-010: Should_NotRetry_When_POST_And_503Response")]
    public void Should_NotRetry_When_POST_And_503Response()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/submit");
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var decision = RetryEvaluator.Evaluate(request, response);

        Assert.False(decision.ShouldRetry);
        Assert.Contains("not idempotent", decision.Reason);
    }

    // ── Retriable status codes: 408, 503 ─────────────────────────────────────

    [Fact(DisplayName = "RE-011: Should_Retry_When_GET_And_408Response")]
    public void Should_Retry_When_GET_And_408Response()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var response = new HttpResponseMessage((HttpStatusCode)408);
        var decision = RetryEvaluator.Evaluate(request, response);

        Assert.True(decision.ShouldRetry);
        Assert.Contains("408", decision.Reason);
    }

    [Fact(DisplayName = "RE-012: Should_Retry_When_GET_And_503Response")]
    public void Should_Retry_When_GET_And_503Response()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var decision = RetryEvaluator.Evaluate(request, response);

        Assert.True(decision.ShouldRetry);
        Assert.Contains("503", decision.Reason);
    }

    [Fact(DisplayName = "RE-013: Should_Retry_When_DELETE_And_408Response")]
    public void Should_Retry_When_DELETE_And_408Response()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "https://example.com/resource");
        var response = new HttpResponseMessage((HttpStatusCode)408);
        var decision = RetryEvaluator.Evaluate(request, response);

        Assert.True(decision.ShouldRetry);
    }

    // ── Non-retriable status codes ────────────────────────────────────────────

    [Fact(DisplayName = "RE-014: Should_NotRetry_When_GET_And_500Response")]
    public void Should_NotRetry_When_GET_And_500Response()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var decision = RetryEvaluator.Evaluate(request, response);

        Assert.False(decision.ShouldRetry);
        Assert.Contains("500", decision.Reason);
    }

    [Fact(DisplayName = "RE-015: Should_NotRetry_When_GET_And_404Response")]
    public void Should_NotRetry_When_GET_And_404Response()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/missing");
        var response = new HttpResponseMessage(HttpStatusCode.NotFound);
        var decision = RetryEvaluator.Evaluate(request, response);

        Assert.False(decision.ShouldRetry);
    }

    [Fact(DisplayName = "RE-016: Should_NotRetry_When_GET_And_429Response")]
    public void Should_NotRetry_When_GET_And_429Response()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var response = new HttpResponseMessage((HttpStatusCode)429);
        var decision = RetryEvaluator.Evaluate(request, response);

        // 429 Too Many Requests: not a mandated retry trigger per RFC 9110 §9.2
        Assert.False(decision.ShouldRetry);
    }

    [Fact(DisplayName = "RE-017: Should_NotRetry_When_GET_And_200Response")]
    public void Should_NotRetry_When_GET_And_200Response()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var decision = RetryEvaluator.Evaluate(request, response);

        Assert.False(decision.ShouldRetry);
    }

    // ── Partially-consumed body blocks all retries ────────────────────────────

    [Fact(DisplayName = "RE-018: Should_NotRetry_When_BodyPartiallyConsumed_GET")]
    public void Should_NotRetry_When_BodyPartiallyConsumed_GET()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var decision = RetryEvaluator.Evaluate(
            request, networkFailure: true, bodyPartiallyConsumed: true);

        Assert.False(decision.ShouldRetry);
        Assert.Contains("partially consumed", decision.Reason);
    }

    [Fact(DisplayName = "RE-019: Should_NotRetry_When_BodyPartiallyConsumed_PUT")]
    public void Should_NotRetry_When_BodyPartiallyConsumed_PUT()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "https://example.com/resource");
        var response = new HttpResponseMessage((HttpStatusCode)408);
        var decision = RetryEvaluator.Evaluate(
            request, response, bodyPartiallyConsumed: true);

        Assert.False(decision.ShouldRetry);
        Assert.Contains("partially consumed", decision.Reason);
    }

    [Fact(DisplayName = "RE-020: Should_NotRetry_When_BodyPartiallyConsumed_DELETE")]
    public void Should_NotRetry_When_BodyPartiallyConsumed_DELETE()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "https://example.com/resource");
        var decision = RetryEvaluator.Evaluate(
            request, networkFailure: true, bodyPartiallyConsumed: true);

        Assert.False(decision.ShouldRetry);
    }

    // ── Retry limit enforcement ───────────────────────────────────────────────

    [Fact(DisplayName = "RE-021: Should_NotRetry_When_MaxRetries_Reached")]
    public void Should_NotRetry_When_MaxRetries_Reached()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var policy = new RetryPolicy { MaxRetries = 3 };
        var decision = RetryEvaluator.Evaluate(
            request, networkFailure: true, attemptCount: 3, policy: policy);

        Assert.False(decision.ShouldRetry);
        Assert.Contains("Retry limit", decision.Reason);
    }

    [Fact(DisplayName = "RE-022: Should_Retry_When_AttemptCount_BelowLimit")]
    public void Should_Retry_When_AttemptCount_BelowLimit()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var policy = new RetryPolicy { MaxRetries = 3 };
        var decision = RetryEvaluator.Evaluate(
            request, networkFailure: true, attemptCount: 2, policy: policy);

        Assert.True(decision.ShouldRetry);
    }

    [Fact(DisplayName = "RE-023: Should_NotRetry_When_MaxRetries_Zero")]
    public void Should_NotRetry_When_MaxRetries_Zero()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var policy = new RetryPolicy { MaxRetries = 0 };
        var decision = RetryEvaluator.Evaluate(
            request, networkFailure: true, attemptCount: 1, policy: policy);

        Assert.False(decision.ShouldRetry);
    }

    [Fact(DisplayName = "RE-024: Should_NotRetry_When_AttemptCount_ExceedsLimit")]
    public void Should_NotRetry_When_AttemptCount_ExceedsLimit()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var policy = new RetryPolicy { MaxRetries = 2 };
        var decision = RetryEvaluator.Evaluate(
            request, networkFailure: true, attemptCount: 5, policy: policy);

        Assert.False(decision.ShouldRetry);
        Assert.Contains("Retry limit", decision.Reason);
    }

    // ── Retry-After header parsing ────────────────────────────────────────────

    [Fact(DisplayName = "RE-025: Should_IncludeRetryAfterDelay_When_503_With_Seconds")]
    public void Should_IncludeRetryAfterDelay_When_503_With_Seconds()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        response.Headers.Add("Retry-After", "120");

        var decision = RetryEvaluator.Evaluate(request, response);

        Assert.True(decision.ShouldRetry);
        Assert.Equal(TimeSpan.FromSeconds(120), decision.RetryAfterDelay);
    }

    [Fact(DisplayName = "RE-026: Should_IncludeRetryAfterDelay_When_408_With_Seconds")]
    public void Should_IncludeRetryAfterDelay_When_408_With_Seconds()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "https://example.com/resource");
        var response = new HttpResponseMessage((HttpStatusCode)408);
        response.Headers.Add("Retry-After", "30");

        var decision = RetryEvaluator.Evaluate(request, response);

        Assert.True(decision.ShouldRetry);
        Assert.Equal(TimeSpan.FromSeconds(30), decision.RetryAfterDelay);
    }

    [Fact(DisplayName = "RE-027: Should_RetryAfterDelay_Be_Null_When_No_Header")]
    public void Should_RetryAfterDelay_Be_Null_When_No_Header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        // No Retry-After header

        var decision = RetryEvaluator.Evaluate(request, response);

        Assert.True(decision.ShouldRetry);
        Assert.Null(decision.RetryAfterDelay);
    }

    [Fact(DisplayName = "RE-028: Should_RetryAfterDelay_Be_Null_When_RespectRetryAfter_False")]
    public void Should_RetryAfterDelay_Be_Null_When_RespectRetryAfter_False()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        response.Headers.Add("Retry-After", "60");
        var policy = new RetryPolicy { RespectRetryAfter = false };

        var decision = RetryEvaluator.Evaluate(request, response, policy: policy);

        Assert.True(decision.ShouldRetry);
        Assert.Null(decision.RetryAfterDelay);
    }

    [Fact(DisplayName = "RE-029: Should_RetryAfterDelay_Be_Zero_When_Date_In_Past")]
    public void Should_RetryAfterDelay_Be_Zero_When_Date_In_Past()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        // Use a date clearly in the past; bypass strict header validation with TryAddWithoutValidation
        // so the evaluator's parser handles it (not HttpHeaders internal parser).
        var pastDate = DateTimeOffset.UtcNow.AddYears(-1).ToString("R");
        response.Headers.TryAddWithoutValidation("Retry-After", pastDate);

        var decision = RetryEvaluator.Evaluate(request, response);

        Assert.True(decision.ShouldRetry);
        Assert.Equal(TimeSpan.Zero, decision.RetryAfterDelay);
    }

    [Fact(DisplayName = "RE-030: Should_RetryAfterDelay_Be_Null_When_Header_Unparseable")]
    public void Should_RetryAfterDelay_Be_Null_When_Header_Unparseable()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        // Use TryAddWithoutValidation to bypass strict header format validation
        response.Headers.TryAddWithoutValidation("Retry-After", "not-a-valid-value");

        var decision = RetryEvaluator.Evaluate(request, response);

        Assert.True(decision.ShouldRetry);
        Assert.Null(decision.RetryAfterDelay);
    }

    // ── Default policy and fallback behavior ──────────────────────────────────

    [Fact(DisplayName = "RE-031: Should_UseDefaultPolicy_When_Policy_Null")]
    public void Should_UseDefaultPolicy_When_Policy_Null()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        // No policy provided — should use RetryPolicy.Default (MaxRetries=3)
        var decision = RetryEvaluator.Evaluate(request, networkFailure: true, policy: null);

        Assert.True(decision.ShouldRetry);
    }

    [Fact(DisplayName = "RE-032: Should_Retry_When_NoResponse_And_NoNetworkFailureFlag_GET")]
    public void Should_Retry_When_NoResponse_And_NoNetworkFailureFlag_GET()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        // null response, networkFailure=false — treated as implicit network failure
        var decision = RetryEvaluator.Evaluate(request, response: null, networkFailure: false);

        Assert.True(decision.ShouldRetry);
    }

    [Fact(DisplayName = "RE-033: Should_NotRetry_When_NoResponse_And_POST")]
    public void Should_NotRetry_When_NoResponse_And_POST()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/submit");
        var decision = RetryEvaluator.Evaluate(request, response: null, networkFailure: false);

        Assert.False(decision.ShouldRetry);
    }

    // ── Decision includes non-empty reason ───────────────────────────────────

    [Fact(DisplayName = "RE-034: Should_AlwaysHaveNonEmptyReason")]
    public void Should_AlwaysHaveNonEmptyReason()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var response = new HttpResponseMessage(HttpStatusCode.OK);

        var retryDecision = RetryEvaluator.Evaluate(request, networkFailure: true);
        var noRetryDecision = RetryEvaluator.Evaluate(request, response);

        Assert.NotEmpty(retryDecision.Reason);
        Assert.NotEmpty(noRetryDecision.Reason);
    }

    // ── RetryPolicy defaults ──────────────────────────────────────────────────

    [Fact(DisplayName = "RE-035: RetryPolicy_Default_MaxRetries_Is_Three")]
    public void RetryPolicy_Default_MaxRetries_Is_Three()
    {
        Assert.Equal(3, RetryPolicy.Default.MaxRetries);
    }

    [Fact(DisplayName = "RE-036: RetryPolicy_Default_RespectRetryAfter_Is_True")]
    public void RetryPolicy_Default_RespectRetryAfter_Is_True()
    {
        Assert.True(RetryPolicy.Default.RespectRetryAfter);
    }

    // ── RetryDecision factory methods ─────────────────────────────────────────

    [Fact(DisplayName = "RE-037: RetryDecision_Retry_Sets_ShouldRetry_True")]
    public void RetryDecision_Retry_Sets_ShouldRetry_True()
    {
        var decision = RetryDecision.Retry("test reason");

        Assert.True(decision.ShouldRetry);
        Assert.Equal("test reason", decision.Reason);
        Assert.Null(decision.RetryAfterDelay);
    }

    [Fact(DisplayName = "RE-038: RetryDecision_Retry_WithDelay_Sets_RetryAfterDelay")]
    public void RetryDecision_Retry_WithDelay_Sets_RetryAfterDelay()
    {
        var delay = TimeSpan.FromSeconds(60);
        var decision = RetryDecision.Retry("test reason", delay);

        Assert.True(decision.ShouldRetry);
        Assert.Equal(delay, decision.RetryAfterDelay);
    }

    [Fact(DisplayName = "RE-039: RetryDecision_NoRetry_Sets_ShouldRetry_False")]
    public void RetryDecision_NoRetry_Sets_ShouldRetry_False()
    {
        var decision = RetryDecision.NoRetry("test reason");

        Assert.False(decision.ShouldRetry);
        Assert.Equal("test reason", decision.Reason);
        Assert.Null(decision.RetryAfterDelay);
    }

    // ── Retry-After with future HTTP-date ─────────────────────────────────────

    [Fact(DisplayName = "RE-040: Should_RetryAfterDelay_Be_Positive_When_Date_In_Future")]
    public void Should_RetryAfterDelay_Be_Positive_When_Date_In_Future()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        // Use a date clearly in the future
        var futureDate = DateTimeOffset.UtcNow.AddMinutes(5);
        response.Headers.Add("Retry-After", futureDate.ToString("R"));

        var decision = RetryEvaluator.Evaluate(request, response);

        Assert.True(decision.ShouldRetry);
        Assert.NotNull(decision.RetryAfterDelay);
        Assert.True(decision.RetryAfterDelay!.Value > TimeSpan.Zero);
    }
}
