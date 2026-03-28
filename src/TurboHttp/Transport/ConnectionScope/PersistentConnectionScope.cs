using System;
using System.Threading;
using System.Threading.Tasks;
using TurboHttp.Internal;

namespace TurboHttp.Transport;

/// <summary>
/// Connection scope for HTTP/1.1+: reuses connections when keep-alive is active,
/// closes on <c>Connection: close</c> or when the server marks the connection non-reusable.
/// </summary>
internal sealed class PersistentConnectionScope : IConnectionScope
{
    private readonly ConnectionPool _pool;
    private readonly TcpOptions _options;
    private readonly RequestEndpoint _endpoint;
    private ConnectionLease? _lease;
    private bool _lastCanReuse = true;
    private volatile bool _disposed;

    public PersistentConnectionScope(
        ConnectionPool pool,
        TcpOptions options,
        RequestEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(pool);
        _pool = pool;
        _options = options;
        _endpoint = endpoint;
    }

    /// <inheritdoc />
    public async Task<ConnectionLease> AcquireAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // If we have a reusable lease, return it directly
        if (_lease is { IsAlive: true, Reusable: true })
        {
            return _lease;
        }

        // Clean up stale lease if any
        if (_lease is not null)
        {
            _pool.Release(_lease, canReuse: false);
            _lease = null;
        }

        _lease = await _pool.AcquireAsync(_options, _endpoint, ct).ConfigureAwait(false);
        _lastCanReuse = true;
        return _lease;
    }

    /// <inheritdoc />
    public Task ReturnAsync(bool canReuse, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_lease is null)
        {
            return Task.CompletedTask;
        }

        _lastCanReuse = canReuse;

        if (!canReuse || !_lease.IsAlive || !_lease.Reusable)
        {
            // Connection: close or dead connection — release and forget
            var lease = _lease;
            _lease = null;
            _pool.Release(lease, canReuse: false);
        }
        else
        {
            // Keep-alive: return to pool but retain reference for reuse
            _pool.Release(_lease, canReuse: true);
            // Clear our reference — pool now owns it, next AcquireAsync will get it back
            _lease = null;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public bool CanReuse()
    {
        if (_disposed)
        {
            return false;
        }

        return _lastCanReuse && _lease is { IsAlive: true, Reusable: true };
    }

    /// <inheritdoc />
    public Task CleanupAsync(CancellationToken ct = default)
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        _disposed = true;

        if (_lease is not null)
        {
            var lease = _lease;
            _lease = null;
            _pool.Release(lease, canReuse: _lastCanReuse && lease.IsAlive && lease.Reusable);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return new ValueTask(CleanupAsync());
    }
}
