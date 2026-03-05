using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace TurboHttp.Protocol;

/// <summary>
/// Tracks the number of active HTTP connections per host and enforces per-host limits.
/// RFC 9112 §9.4 — Persistent Connection Pipelining recommends limiting concurrent connections.
/// </summary>
/// <remarks>
/// This class is thread-safe. All operations are protected by an internal lock.
/// Apply only to HTTP/1.x connections; HTTP/2 connections are multiplexed and
/// should not be tracked by this limiter (see <see cref="ConnectionPolicy.AllowHttp2Multiplexing"/>).
/// </remarks>
public sealed class PerHostConnectionLimiter
{
    private readonly int _maxConnectionsPerHost;
    private readonly ConcurrentDictionary<string, int> _active = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a new limiter with the specified per-host connection limit.
    /// </summary>
    /// <param name="maxConnectionsPerHost">
    ///     Maximum simultaneous connections allowed per host.
    ///     Use 0 to deny all connections.
    ///     Must be &gt;= 0.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="maxConnectionsPerHost"/> is negative.
    /// </exception>
    public PerHostConnectionLimiter(int maxConnectionsPerHost = 6)
    {
        if (maxConnectionsPerHost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConnectionsPerHost),
                "Maximum connections per host must be >= 0.");
        }

        _maxConnectionsPerHost = maxConnectionsPerHost;
    }

    /// <summary>The configured maximum number of connections per host.</summary>
    public int MaxConnectionsPerHost => _maxConnectionsPerHost;

    /// <summary>
    /// Returns the number of currently active connections for the given host.
    /// Returns 0 if the host is unknown.
    /// </summary>
    /// <param name="host">The host name (case-insensitive).</param>
    public int GetActiveConnections(string host)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);

        return _active.GetValueOrDefault(host, 0);
    }

    /// <summary>
    /// Attempts to acquire a connection slot for <paramref name="host"/>.
    /// </summary>
    /// <param name="host">The host name (case-insensitive).</param>
    /// <returns>
    ///     True if a slot was acquired (caller must call <see cref="Release"/> when done).
    ///     False if the per-host limit has been reached.
    /// </returns>
    public bool TryAcquire(string host)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);

        if (_maxConnectionsPerHost == 0)
        {
            return false;
        }

        var current = _active.GetValueOrDefault(host, 0);
        if (current >= _maxConnectionsPerHost)
        {
            return false;
        }

        _active[host] = current + 1;
        return true;
    }

    /// <summary>
    /// Releases a previously acquired connection slot for <paramref name="host"/>.
    /// Safe to call even if the host has no active connections (no-op in that case).
    /// </summary>
    /// <param name="host">The host name (case-insensitive).</param>
    public void Release(string host)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);

        if (!_active.TryGetValue(host, out var count) || count <= 0)
        {
            return;
        }

        if (count == 1)
        {
            _active.Remove(host, out _);
        }
        else
        {
            _active[host] = count - 1;
        }
    }
}