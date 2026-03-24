using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TurboHttp.Diagnostics;
using TurboHttp.Internal;

namespace TurboHttp.Transport;

/// <summary>
/// Thread-safe connection pool that manages per-host <see cref="HostConnections"/>.
/// Replaces the actor-based PoolRouter + HostPool + PerHostConnectionLimiter + ConnectionState
/// with a direct async API: <see cref="AcquireAsync"/> / <see cref="Release"/>.
/// </summary>
internal sealed class ConnectionPool : IAsyncDisposable
{
    private readonly ConcurrentDictionary<RequestEndpoint, HostConnections> _hosts = new();
    private readonly TimeSpan _idleTimeout;
    private volatile bool _disposed;

    public ConnectionPool(TimeSpan idleTimeout)
    {
        _idleTimeout = idleTimeout;
    }

    /// <summary>
    /// Acquires a connection lease for the given endpoint. Version-aware:
    /// HTTP/1.0 always creates new, HTTP/1.1 tries idle reuse, HTTP/2 multiplexes.
    /// </summary>
    public Task<ConnectionLease> AcquireAsync(
        TcpOptions options,
        RequestEndpoint endpoint,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var host = _hosts.GetOrAdd(endpoint, key => new HostConnections(key, _idleTimeout));
        return host.AcquireAsync(options, ct);
    }

    /// <summary>
    /// Releases a connection lease back to the pool or disposes it.
    /// </summary>
    public void Release(ConnectionLease lease, bool canReuse)
    {
        ArgumentNullException.ThrowIfNull(lease);

        if (_disposed)
        {
            _ = lease.DisposeAsync();
            return;
        }

        if (_hosts.TryGetValue(lease.Key, out var host))
        {
            host.Release(lease, canReuse);
        }
        else
        {
            _ = lease.DisposeAsync();
        }
    }

    /// <summary>
    /// Disposes all hosts and their connections.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        var tasks = new List<ValueTask>();
        foreach (var kvp in _hosts)
        {
            tasks.Add(kvp.Value.DisposeAsync());
        }

        foreach (var task in tasks)
        {
            await task.ConfigureAwait(false);
        }

        _hosts.Clear();
    }

    /// <summary>
    /// Per-host connection manager. Handles idle reuse, multiplexing, limits, and eviction.
    /// </summary>
    internal sealed class HostConnections : IAsyncDisposable
    {
        private readonly RequestEndpoint _endpoint;
        private readonly TimeSpan _idleTimeout;
        private readonly List<ConnectionLease> _leases = [];
        private readonly ConcurrentQueue<ConnectionLease> _idle = new();
        private readonly SemaphoreSlim _limiter;
        private readonly Timer _evictionTimer;
        private readonly object _lock = new();
        private volatile bool _disposed;

        public HostConnections(RequestEndpoint endpoint, TimeSpan idleTimeout)
        {
            _endpoint = endpoint;
            _idleTimeout = idleTimeout;

            // HTTP/1.x: 6 connections per host (RFC 9112 §9.4)
            // HTTP/2+: effectively unlimited (multiplexed)
            var maxConnections = endpoint.Version.Major >= 2 ? int.MaxValue : 6;
            _limiter = new SemaphoreSlim(maxConnections, maxConnections);

            _evictionTimer = new Timer(
                _ => EvictIdle(),
                null,
                idleTimeout,
                idleTimeout);
        }

        /// <summary>
        /// Acquires a connection: reuses idle/multiplexed if possible, else creates new.
        /// </summary>
        public async Task<ConnectionLease> AcquireAsync(TcpOptions options, CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var version = _endpoint.Version;

            // HTTP/1.0: always create new (no reuse)
            if (version is { Major: 1, Minor: 0 })
            {
                return await EstablishAndTrack(options, ct).ConfigureAwait(false);
            }

            // HTTP/2+: try to find an existing lease with available stream slots (MRU)
            if (version.Major >= 2)
            {
                var mru = SelectMru();
                if (mru is not null)
                {
                    mru.MarkBusy();
                    TurboHttpMetrics.ConnectionIdle.Add(-1,
                        new("server.address", _endpoint.Host),
                        new("server.port", _endpoint.Port));
                    return mru;
                }

                return await EstablishAndTrack(options, ct).ConfigureAwait(false);
            }

            // HTTP/1.1: try idle queue first
            while (_idle.TryDequeue(out var idle))
            {
                if (idle.IsAlive && idle.Reusable)
                {
                    idle.MarkBusy();
                    TurboHttpMetrics.ConnectionIdle.Add(-1,
                        new("server.address", _endpoint.Host),
                        new("server.port", _endpoint.Port));
                    return idle;
                }

                // Stale — dispose and try next
                RemoveLease(idle);
                await idle.DisposeAsync().ConfigureAwait(false);
                _limiter.Release();
            }

            // No idle available — wait for a slot then create new
            await _limiter.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await EstablishAndTrack(options, ct).ConfigureAwait(false);
            }
            catch
            {
                _limiter.Release();
                throw;
            }
        }

        /// <summary>
        /// Releases a lease: returns to idle pool or disposes.
        /// </summary>
        public void Release(ConnectionLease lease, bool canReuse)
        {
            var version = _endpoint.Version;

            // HTTP/1.0: always dispose
            if (version is { Major: 1, Minor: 0 })
            {
                RemoveLease(lease);
                _ = lease.DisposeAsync();
                TurboHttpMetrics.ConnectionActive.Add(-1,
                    new("server.address", _endpoint.Host),
                    new("server.port", _endpoint.Port));
                return;
            }

            // HTTP/2+: decrement stream count; only dispose when last stream and non-reusable
            if (version.Major >= 2)
            {
                lease.MarkIdle();

                if (!canReuse)
                {
                    lease.MarkNoReuse();
                }

                if (lease.ActiveStreams <= 0 && !lease.Reusable)
                {
                    RemoveLease(lease);
                    _ = lease.DisposeAsync();
                    TurboHttpMetrics.ConnectionActive.Add(-1,
                        new("server.address", _endpoint.Host),
                        new("server.port", _endpoint.Port));
                }

                return;
            }

            // HTTP/1.1
            lease.MarkIdle();

            if (canReuse && lease.IsAlive && lease.Reusable)
            {
                _idle.Enqueue(lease);
                TurboHttpMetrics.ConnectionIdle.Add(1,
                    new("server.address", _endpoint.Host),
                    new("server.port", _endpoint.Port));
            }
            else
            {
                RemoveLease(lease);
                _ = lease.DisposeAsync();
                _limiter.Release();
                TurboHttpMetrics.ConnectionActive.Add(-1,
                    new("server.address", _endpoint.Host),
                    new("server.port", _endpoint.Port));
            }
        }

        /// <summary>
        /// Returns the MRU (Most Recently Used) lease with an available stream slot,
        /// or <c>null</c> if none is available.
        /// </summary>
        internal ConnectionLease? SelectMru()
        {
            lock (_lock)
            {
                ConnectionLease? best = null;
                foreach (var lease in _leases)
                {
                    if (lease.HasAvailableSlot &&
                        (best is null || lease.LastActivity > best.LastActivity))
                    {
                        best = lease;
                    }
                }

                return best;
            }
        }

        /// <summary>
        /// Evicts idle connections older than <see cref="_idleTimeout"/>,
        /// keeping at least one connection per host.
        /// </summary>
        internal void EvictIdle()
        {
            if (_disposed)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var toEvict = new List<ConnectionLease>();

            // Drain idle queue, keep fresh ones, collect expired
            var freshItems = new List<ConnectionLease>();
            while (_idle.TryDequeue(out var idle))
            {
                if (!idle.IsAlive)
                {
                    toEvict.Add(idle);
                }
                else if (now - idle.LastActivity > _idleTimeout)
                {
                    toEvict.Add(idle);
                }
                else
                {
                    freshItems.Add(idle);
                }
            }

            // Keep at least 1 per host: if we'd evict all, keep the most recent
            if (freshItems.Count == 0 && toEvict.Count > 0)
            {
                // Find the most recent among evictees and keep it
                var keeper = toEvict[0];
                for (var i = 1; i < toEvict.Count; i++)
                {
                    if (toEvict[i].IsAlive && toEvict[i].LastActivity > keeper.LastActivity)
                    {
                        keeper = toEvict[i];
                    }
                }

                if (keeper.IsAlive)
                {
                    toEvict.Remove(keeper);
                    freshItems.Add(keeper);
                }
            }

            // Re-enqueue fresh items
            foreach (var item in freshItems)
            {
                _idle.Enqueue(item);
            }

            // Dispose evicted
            foreach (var lease in toEvict)
            {
                RemoveLease(lease);
                _ = lease.DisposeAsync();
                _limiter.Release();
                TurboHttpMetrics.ConnectionIdle.Add(-1,
                    new("server.address", _endpoint.Host),
                    new("server.port", _endpoint.Port));
                TurboHttpMetrics.ConnectionActive.Add(-1,
                    new("server.address", _endpoint.Host),
                    new("server.port", _endpoint.Port));
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            await _evictionTimer.DisposeAsync().ConfigureAwait(false);

            List<ConnectionLease> snapshot;
            lock (_lock)
            {
                snapshot = [.. _leases];
                _leases.Clear();
            }

            foreach (var lease in snapshot)
            {
                await lease.DisposeAsync().ConfigureAwait(false);
            }

            // Drain idle queue
            while (_idle.TryDequeue(out var idle))
            {
                await idle.DisposeAsync().ConfigureAwait(false);
            }

            _limiter.Dispose();
        }

        private async Task<ConnectionLease> EstablishAndTrack(TcpOptions options, CancellationToken ct)
        {
            var lease = await DirectConnectionFactory.EstablishAsync(options, _endpoint, ct)
                .ConfigureAwait(false);
            lease.MarkBusy();

            lock (_lock)
            {
                _leases.Add(lease);
            }

            TurboHttpMetrics.ConnectionActive.Add(1,
                new("server.address", _endpoint.Host),
                new("server.port", _endpoint.Port));

            return lease;
        }

        private void RemoveLease(ConnectionLease lease)
        {
            lock (_lock)
            {
                _leases.Remove(lease);
            }
        }
    }
}
