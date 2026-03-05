namespace TurboHttp.Protocol;

/// <summary>
/// Configuration for HTTP retry behavior.
/// RFC 9110 §9.2 — Idempotency-constrained automatic retry policy.
/// </summary>
public sealed record RetryPolicy
{
    /// <summary>Default retry policy: up to 3 retries, Retry-After respected.</summary>
    public static readonly RetryPolicy Default = new();

    /// <summary>
    /// Maximum number of automatic retry attempts per request.
    /// Default is 3. Set to 0 to disable retries.
    /// </summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// If true, the <c>Retry-After</c> response header is parsed and the delay is
    /// included in the retry decision so callers can honour the server's back-off hint.
    /// RFC 9110 §10.2.3 — Retry-After.
    /// Default is true.
    /// </summary>
    public bool RespectRetryAfter { get; init; } = true;
}
