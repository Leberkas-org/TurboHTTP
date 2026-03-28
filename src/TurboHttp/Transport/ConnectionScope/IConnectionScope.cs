using System;
using System.Threading;
using System.Threading.Tasks;

namespace TurboHttp.Transport;

/// <summary>
/// Encapsulates protocol-specific connection lifecycle (acquire, use, return)
/// so that the pipeline doesn't need protocol-aware branches for HTTP/1.0 vs HTTP/1.1+.
/// </summary>
internal interface IConnectionScope : IAsyncDisposable
{
    /// <summary>
    /// Acquires a connection lease from the pool. For HTTP/1.0 this always creates
    /// a new connection; for HTTP/1.1+ it may reuse an existing idle connection.
    /// </summary>
    Task<ConnectionLease> AcquireAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the connection lease to the pool. For HTTP/1.0 this always closes
    /// the connection; for HTTP/1.1+ it may return it for reuse based on <paramref name="canReuse"/>.
    /// </summary>
    Task ReturnAsync(bool canReuse, CancellationToken ct = default);

    /// <summary>
    /// Returns <c>true</c> if the current lease can be reused for subsequent requests
    /// without re-acquiring. Always <c>false</c> for HTTP/1.0.
    /// </summary>
    bool CanReuse();

    /// <summary>
    /// Releases all resources held by this scope. Safe to call multiple times.
    /// </summary>
    Task CleanupAsync(CancellationToken ct = default);
}
