using System.Collections.Concurrent;

namespace TurboHTTP.Protocol.Http11;

/// <summary>
/// Result of evaluating whether an HTTP/1.x connection can be reused for subsequent requests.
/// Common instances with fixed reason strings are cached to avoid per-request allocations.
/// </summary>
public sealed class ConnectionReuseDecision
{
    // Cache for Close() decisions keyed by reason string (all hot-path reasons are string literals)
    private static readonly ConcurrentDictionary<string, ConnectionReuseDecision> CloseCache = new();

    // Cache for simple KeepAlive() decisions (no timeout/maxRequests)
    private static readonly ConcurrentDictionary<string, ConnectionReuseDecision> KeepAliveCache = new();

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

    private ConnectionReuseDecision() { }

    /// <summary>Creates a keep-alive decision (connection may be reused).
    /// Cached when <paramref name="keepAliveTimeout"/> and <paramref name="maxRequests"/> are both null.</summary>
    public static ConnectionReuseDecision KeepAlive(string reason, TimeSpan? keepAliveTimeout = null,
        int? maxRequests = null)
    {
        // When no variable parameters, return a cached instance
        if (keepAliveTimeout is null && maxRequests is null)
        {
            return KeepAliveCache.GetOrAdd(reason, static r => new ConnectionReuseDecision
            {
                CanReuse = true,
                Reason = r,
            });
        }

        return new ConnectionReuseDecision
        {
            CanReuse = true,
            Reason = reason,
            KeepAliveTimeout = keepAliveTimeout,
            MaxRequests = maxRequests,
        };
    }

    /// <summary>Creates a close decision (connection must not be reused). Cached by reason string.</summary>
    public static ConnectionReuseDecision Close(string reason)
        => CloseCache.GetOrAdd(reason, static r => new ConnectionReuseDecision { CanReuse = false, Reason = r });
}