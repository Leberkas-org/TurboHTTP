using System;

namespace TurboHttp.Protocol;

/// <summary>
/// Result of evaluating whether an HTTP request should be retried.
/// RFC 9110 §9.2 — Idempotency-constrained retry decisions.
/// </summary>
public sealed record RetryDecision
{
    /// <summary>Whether the request should be retried.</summary>
    public bool ShouldRetry { get; init; }

    /// <summary>Human-readable reason for the decision (for diagnostics and logging).</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// Delay before the next retry attempt, parsed from the <c>Retry-After</c> response header.
    /// Null if no <c>Retry-After</c> header was present or could not be parsed.
    /// RFC 9110 §10.2.3 — Retry-After.
    /// Callers SHOULD wait this duration before re-sending when non-null.
    /// </summary>
    public TimeSpan? RetryAfterDelay { get; init; }

    /// <summary>Creates a retry decision (request should be retried).</summary>
    public static RetryDecision Retry(string reason, TimeSpan? retryAfterDelay = null)
        => new() { ShouldRetry = true, Reason = reason, RetryAfterDelay = retryAfterDelay };

    /// <summary>Creates a no-retry decision (request must not be retried automatically).</summary>
    public static RetryDecision NoRetry(string reason)
        => new() { ShouldRetry = false, Reason = reason };
}
