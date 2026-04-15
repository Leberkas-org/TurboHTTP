using System.Runtime.Versioning;
using TurboHTTP.Diagnostics;
using TurboHTTP.Internal;

namespace TurboHTTP.Transport.Connection;

/// <summary>
/// Wraps a <see cref="QuicConnectionHandle"/> with lifecycle management, metrics emission,
/// and stream-count tracking. Each lease represents one shared QUIC connection; multiple
/// concurrent HTTP/3 streams are multiplexed on the underlying connection.
/// <para>
/// Mirrors <see cref="ConnectionLease"/> structurally — a senior dev who knows one
/// immediately understands the other. Key difference: <see cref="CanAcceptStream"/> guards
/// per-connection stream capacity instead of per-host slot limits.
/// </para>
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macOS")]
[SupportedOSPlatform("windows")]
internal sealed class QuicConnectionLease : IDisposable
{
    private readonly long _createdTicks = Environment.TickCount64;
    private int _activeStreams;
    private bool _alive = true;
    private bool _reusable = true;

    public QuicConnectionLease(QuicConnectionHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        Handle = handle;
        LastActivity = DateTime.UtcNow;
    }

    /// <summary>The underlying QUIC connection handle.</summary>
    public QuicConnectionHandle Handle { get; }

    /// <summary>The connection target identity (scheme, host, port, version).</summary>
    public RequestEndpoint Key => Handle.Key;

    /// <summary>Whether this connection is still alive and usable.</summary>
    public bool IsAlive => _alive;

    /// <summary>Whether this connection can be reused for subsequent requests.</summary>
    public bool Reusable => _reusable;

    /// <summary>Timestamp of the last activity on this connection.</summary>
    public DateTime LastActivity { get; private set; }

    /// <summary>
    /// Number of stages currently holding this connection.
    /// Incremented by <see cref="MarkBusy"/>, decremented by <see cref="MarkIdle"/>.
    /// </summary>
    public int ActiveStreams => _activeStreams;

    /// <summary>
    /// Maximum number of stages that may hold this connection simultaneously.
    /// Defaults to 1 (exclusive use per stage — QUIC multiplexes internally).
    /// Can be raised to share one connection across multiple stages.
    /// </summary>
    public int MaxConcurrentStreams { get; set; } = 1;

    /// <summary>
    /// Whether this connection can accept another stage. Checks liveness, reusability,
    /// and the per-connection stream-capacity limit.
    /// </summary>
    public bool CanAcceptStream => _alive && _reusable && ActiveStreams < MaxConcurrentStreams;

    /// <summary>
    /// Returns <see langword="true"/> when the connection has exceeded the specified
    /// maximum lifetime (measured from creation). Used by connection pool eviction
    /// to enforce <see cref="TurboClientOptions.PooledConnectionLifetime"/>.
    /// </summary>
    public bool IsExpired(TimeSpan maxLifetime)
    {
        if (maxLifetime == Timeout.InfiniteTimeSpan)
        {
            return false;
        }

        return Environment.TickCount64 - _createdTicks > (long)maxLifetime.TotalMilliseconds;
    }

    /// <summary>Marks this connection as acquired by an additional stage.</summary>
    public void MarkBusy()
    {
        _activeStreams++;
        LastActivity = DateTime.UtcNow;
    }

    /// <summary>Marks one stage as done, reducing the active count.</summary>
    public void MarkIdle()
    {
        _activeStreams--;
        LastActivity = DateTime.UtcNow;
    }

    /// <summary>Marks this connection as non-reusable (e.g., after a transport error).</summary>
    public void MarkNoReuse()
    {
        _reusable = false;
    }

    /// <summary>
    /// Disposes this lease: closes the QUIC connection and emits duration metrics.
    /// </summary>
    public void Dispose()
    {
        if (!_alive)
        {
            return;
        }

        _alive = false;

        _ = Handle.DisposeAsync().AsTask();

        var durationMs = Environment.TickCount64 - _createdTicks;
        var host = Key.Host;
        var port = Key.Port;

        TurboHttpMetrics.ConnectionDuration.Record(
            durationMs / 1000.0,
            new("server.address", host),
            new("server.port", port));
        TurboTrace.Connection.Info(this, "QUIC connection closed: {0}:{1} ({2}ms)", host, port, durationMs);
    }
}
