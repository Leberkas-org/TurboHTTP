using System;
using System.Threading;
using TurboHttp.Diagnostics;
using TurboHttp.Internal;
using TurboHttp.Pooling;

namespace TurboHttp.Transport;

/// <summary>
/// Wraps a <see cref="ConnectionHandle"/> and <see cref="ClientState"/> with lifecycle
/// management, metrics emission, and stream tracking. Each lease represents a single
/// owner responsible for cleanup when the connection is no longer needed.
/// </summary>
internal sealed class ConnectionLease : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly long _createdTicks = Environment.TickCount64;
    private int _activeStreams;
    private volatile bool _alive = true;
    private volatile bool _reusable = true;

    public ConnectionLease(ConnectionHandle handle, ClientState state)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(state);
        Handle = handle;
        State = state;
        LastActivity = DateTime.UtcNow;
        MaxConcurrentStreams = ComputeDefaultMaxConcurrentStreams(handle.Key.Version);
    }

    /// <summary>
    /// The underlying connection handle providing direct Channel I/O access.
    /// </summary>
    public ConnectionHandle Handle { get; }

    /// <summary>
    /// The transport-level state (TCP stream, channels, pipe).
    /// </summary>
    public ClientState State { get; }

    /// <summary>
    /// The connection target identity (scheme, host, port, version).
    /// </summary>
    public RequestEndpoint Key => Handle.Key;

    /// <summary>
    /// Whether this connection is still alive and usable.
    /// </summary>
    public bool IsAlive => _alive;

    /// <summary>
    /// Whether this connection can be reused for subsequent requests.
    /// </summary>
    public bool Reusable => _reusable;

    /// <summary>
    /// Timestamp of the last activity on this connection.
    /// </summary>
    public DateTime LastActivity { get; private set; }

    /// <summary>
    /// Number of currently active streams on this connection.
    /// </summary>
    public int ActiveStreams => Volatile.Read(ref _activeStreams);

    /// <summary>
    /// Maximum concurrent streams allowed on this connection.
    /// Version-dependent defaults: 1 for HTTP/1.0, 6 for HTTP/1.1, 100 for HTTP/2+.
    /// Can be updated dynamically via <see cref="UpdateMaxConcurrentStreams"/>.
    /// </summary>
    public int MaxConcurrentStreams { get; private set; }

    /// <summary>
    /// Whether this connection can accept another request.
    /// </summary>
    public bool HasAvailableSlot => _alive && _reusable && ActiveStreams < MaxConcurrentStreams;

    /// <summary>
    /// The <see cref="CancellationToken"/> that is cancelled when this lease is disposed.
    /// Use this to cancel ByteMover tasks tied to this connection.
    /// </summary>
    public CancellationToken Token => _cts.Token;

    /// <summary>
    /// Marks this connection as busy with an additional active stream.
    /// </summary>
    public void MarkBusy()
    {
        Interlocked.Increment(ref _activeStreams);
        LastActivity = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks one stream as complete, reducing the active stream count.
    /// </summary>
    public void MarkIdle()
    {
        Interlocked.Decrement(ref _activeStreams);
        LastActivity = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks this connection as non-reusable (e.g., after receiving Connection: close).
    /// </summary>
    public void MarkNoReuse()
    {
        _reusable = false;
    }

    /// <summary>
    /// Updates the maximum concurrent streams for this connection (e.g., from HTTP/2 SETTINGS).
    /// Also updates the underlying <see cref="ConnectionHandle"/>.
    /// </summary>
    public void UpdateMaxConcurrentStreams(int value)
    {
        MaxConcurrentStreams = value;
        Handle.UpdateMaxConcurrentStreams(value);
    }

    /// <summary>
    /// Disposes this lease: cancels the CTS, disposes ClientState, and emits
    /// connection duration metrics and diagnostics events.
    /// </summary>
    public void Dispose()
    {
        if (!_alive)
        {
            return;
        }

        _alive = false;

        // 1. Cancel CTS first — stops ByteMover tasks
        _cts.Cancel();
        _cts.Dispose();

        // 2. Dispose ClientState — closes channels + TCP stream
        State.Dispose();

        // 3. Emit metrics and diagnostics
        var durationMs = Environment.TickCount64 - _createdTicks;
        var host = Key.Host;
        var port = Key.Port;

        TurboHttpMetrics.ConnectionDuration.Record(
            durationMs / 1000.0,
            new("server.address", host),
            new("server.port", port));
        TurboHttpEventSource.Log.ConnectionClosed(host, port, durationMs);
        TurboHttpDiagnosticListener.OnConnectionClosed(host, port, TimeSpan.FromMilliseconds(durationMs));
    }

    private static int ComputeDefaultMaxConcurrentStreams(Version version)
    {
        if (version is { Major: 1, Minor: 0 })
        {
            return 1;
        }

        if (version.Major == 1)
        {
            return 6;
        }

        // HTTP/2+
        return 100;
    }
}
