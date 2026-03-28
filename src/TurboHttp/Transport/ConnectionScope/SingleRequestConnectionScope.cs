using System;
using System.Threading;
using System.Threading.Tasks;
using TurboHttp.Internal;

namespace TurboHttp.Transport;

/// <summary>
/// Connection scope for HTTP/1.0: always acquires a new connection per request
/// and always closes it after the response is received.
/// </summary>
internal sealed class SingleRequestConnectionScope : IConnectionScope
{
    private readonly ConnectionPool _pool;
    private readonly TcpOptions _options;
    private readonly RequestEndpoint _endpoint;
    private ConnectionLease? _lease;
    private volatile bool _disposed;

    public SingleRequestConnectionScope(
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

        if (_lease is not null)
        {
            throw new InvalidOperationException(
                "SingleRequestConnectionScope already has an active lease. " +
                "Call ReturnAsync before acquiring again.");
        }

        _lease = await _pool.AcquireAsync(_options, _endpoint, ct).ConfigureAwait(false);
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

        // HTTP/1.0: always close, never reuse — ignore canReuse parameter
        var lease = _lease;
        _lease = null;
        _pool.Release(lease, canReuse: false);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public bool CanReuse() => false;

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
            _pool.Release(lease, canReuse: false);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return new ValueTask(CleanupAsync());
    }
}
