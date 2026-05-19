using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Client;

public sealed class RetryOptions
{
    /// <summary>
    /// Maximum number of automatic retry attempts per request.
    /// Default is 3. Set to 0 to disable retries.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// If true, the <c>Retry-After</c> response header is parsed and the delay is
    /// included in the retry decision so callers can honour the server's back-off hint.
    /// RFC 9110 §10.2.3 — Retry-After.
    /// Default is true.
    /// </summary>
    public bool RespectRetryAfter { get; set; } = true;

    internal RetryPolicy To() => new()
    {
        MaxRetries = MaxRetries,
        RespectRetryAfter = RespectRetryAfter,
    };
}