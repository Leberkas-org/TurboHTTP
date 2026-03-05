using System;
using System.Globalization;
using System.Net.Http;

namespace TurboHttp.Protocol;

/// <summary>
/// RFC 9110 §9.2 — Evaluates whether an HTTP request should be automatically retried.
/// Only idempotent methods may be retried; non-idempotent methods (e.g. POST) are never retried.
/// Retries are only issued for network-level failures or specific error status codes (408, 503).
/// </summary>
public static class RetryEvaluator
{
    /// <summary>
    /// Determines whether the given HTTP request should be retried after a failure.
    /// </summary>
    /// <param name="request">The original HTTP request.</param>
    /// <param name="response">
    ///     The HTTP response received, or null if a network-level failure occurred
    ///     before any response was received.
    /// </param>
    /// <param name="networkFailure">
    ///     True if the failure was a network-level error (connection refused, reset, timeout, etc.)
    ///     with no HTTP response received. When true, <paramref name="response"/> should be null.
    ///     Default is false.
    /// </param>
    /// <param name="bodyPartiallyConsumed">
    ///     True if the request body was partially sent or consumed (e.g. a streamed body that
    ///     cannot be rewound to the start). A partially-consumed body cannot be safely re-sent.
    ///     RFC 9110 §9.2.2: clients MUST NOT automatically retry with a partially-consumed body.
    ///     Default is false.
    /// </param>
    /// <param name="attemptCount">
    ///     The number of attempts already made (1 = first attempt just failed).
    ///     When <c>attemptCount >= policy.MaxRetries</c> no further retries are issued.
    ///     Default is 1.
    /// </param>
    /// <param name="policy">
    ///     The retry policy to apply. Defaults to <see cref="RetryPolicy.Default"/> when null.
    /// </param>
    /// <returns>
    ///     A <see cref="RetryDecision"/> indicating whether the request should be retried,
    ///     the reason, and an optional <c>Retry-After</c> delay parsed from the response.
    /// </returns>
    public static RetryDecision Evaluate(HttpRequestMessage request, HttpResponseMessage? response = null,
        bool networkFailure = false,
        bool bodyPartiallyConsumed = false,
        int attemptCount = 1,
        RetryPolicy? policy = null)
    {
        policy ??= RetryPolicy.Default;

        // A partially-consumed request body cannot be rewound and re-sent.
        // RFC 9110 §9.2.2: MUST NOT automatically retry a request with a non-replayable body.
        if (bodyPartiallyConsumed)
        {
            return RetryDecision.NoRetry("RFC 9110 §9.2.2: Request body partially consumed; cannot rewind for retry.");
        }

        // Method must be idempotent.
        // RFC 9110 §9.2.2: automatic retry is only safe for idempotent methods.
        var method = request.Method;
        if (!IsIdempotent(method))
        {
            return RetryDecision.NoRetry(
                $"RFC 9110 §9.2.2: Method {method} is not idempotent; automatic retry is not safe.");
        }

        // Retry limit.
        if (attemptCount >= policy.MaxRetries)
        {
            return RetryDecision.NoRetry($"Retry limit reached ({policy.MaxRetries} attempt(s)).");
        }

        // Determine if this failure is retriable.
        if (networkFailure)
        {
            // Network-level failure with no response — safe to retry idempotent methods.
            return RetryDecision.Retry("RFC 9110 §9.2.2: Network failure on idempotent method; retrying.");
        }

        if (response is null)
        {
            // No response and no explicit network failure flag — treat as network failure.
            return RetryDecision.Retry(
                "RFC 9110 §9.2.2: No response received (treated as network failure); retrying idempotent method.");
        }

        var statusCode = (int)response.StatusCode;

        // 408 Request Timeout — server explicitly requests a retry.
        if (statusCode == 408)
        {
            var delay = policy.RespectRetryAfter ? ParseRetryAfter(response) : null;
            return RetryDecision.Retry("RFC 9110 §9.2.2: 408 Request Timeout on idempotent method; retrying.", delay);
        }

        // 503 Service Unavailable — server temporarily unable to handle the request.
        if (statusCode == 503)
        {
            var delay = policy.RespectRetryAfter ? ParseRetryAfter(response) : null;
            return RetryDecision.Retry("RFC 9110 §9.2.2: 503 Service Unavailable on idempotent method; retrying.",
                delay);
        }

        // All other status codes — not a retriable condition.
        return RetryDecision.NoRetry($"Status {statusCode} is not a retriable error code (not 408 or 503).");
    }

    // ── Private Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the HTTP method is idempotent per RFC 9110 §9.2.2.
    /// Idempotent methods: GET, HEAD, PUT, DELETE, OPTIONS, TRACE.
    /// POST, PATCH, and CONNECT are not idempotent.
    /// </summary>
    private static bool IsIdempotent(HttpMethod method)
    {
        // Use reference equality where possible (HttpMethod caches well-known methods).
        return method switch
        {
            _ when method == HttpMethod.Get => true,
            _ when method == HttpMethod.Head => true,
            _ when method == HttpMethod.Put => true,
            _ when method == HttpMethod.Delete => true,
            _ when method == HttpMethod.Options => true,
            _ when method == HttpMethod.Trace => true,
            // POST, PATCH, CONNECT — not idempotent.
            _ => false
        };
    }

    /// <summary>
    /// Parses the <c>Retry-After</c> header from the response.
    /// RFC 9110 §10.2.3: value is either a delay-seconds integer or an HTTP-date.
    /// Returns null if the header is absent or unparseable.
    /// </summary>
    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Retry-After", out var values))
        {
            return null;
        }

        foreach (var value in values)
        {
            var trimmed = value.Trim();

            // Delay-seconds: a non-negative integer.
            if (int.TryParse(trimmed, out var seconds) && seconds >= 0)
            {
                return TimeSpan.FromSeconds(seconds);
            }

            // HTTP-date: attempt to parse as a DateTimeOffset.
            if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, out var date))
            {
                var delay = date - DateTimeOffset.UtcNow;
                // Clamp to zero if date is in the past.
                return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
            }
        }

        return null;
    }
}