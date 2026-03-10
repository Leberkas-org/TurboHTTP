using System;

namespace TurboHttp.Protocol;

/// <summary>
/// Result of evaluating whether an HTTP/1.x connection can be reused for subsequent requests.
/// </summary>
public sealed record ConnectionReuseDecision
{
    /// <summary>Whether the connection can be reused for the next request.</summary>
    public bool CanReuse { get; private init; }

    /// <summary>Human-readable reason for the decision (for diagnostics and logging).</summary>
    public string Reason { get; private init; } = string.Empty;

    /// <summary>
    /// Server-advertised keep-alive timeout parsed from the Keep-Alive response header.
    /// Null if no timeout was specified.
    /// RFC 9112 §9.3: client SHOULD NOT keep connection open longer than this interval.
    /// </summary>
    public TimeSpan? KeepAliveTimeout { get; private init; }

    /// <summary>
    /// Server-advertised maximum number of requests on this connection,
    /// parsed from the Keep-Alive header's <c>max</c> parameter.
    /// Null if no max was specified.
    /// </summary>
    public int? MaxRequests { get; private init; }

    /// <summary>Creates a keep-alive decision (connection may be reused).</summary>
    public static ConnectionReuseDecision KeepAlive(string reason, TimeSpan? keepAliveTimeout = null,
        int? maxRequests = null)
        => new()
        {
            CanReuse = true,
            Reason = reason,
            KeepAliveTimeout = keepAliveTimeout,
            MaxRequests = maxRequests,
        };

    /// <summary>Creates a close decision (connection must not be reused).</summary>
    public static ConnectionReuseDecision Close(string reason) => new() { CanReuse = false, Reason = reason };
}