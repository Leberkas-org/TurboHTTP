using System;
using TurboHttp.Internal;

namespace TurboHttp.Transport;

/// <summary>
/// Factory for creating the correct <see cref="IConnectionScope"/> implementation
/// based on the HTTP version. HTTP/1.0 gets <see cref="SingleRequestConnectionScope"/>;
/// HTTP/1.1+ gets <see cref="PersistentConnectionScope"/>.
/// </summary>
internal static class ConnectionScopeFactory
{
    /// <summary>
    /// Creates the appropriate connection scope for the given HTTP version.
    /// </summary>
    /// <param name="version">The HTTP version (1.0, 1.1, 2.0, etc.).</param>
    /// <param name="pool">The connection pool to acquire/release leases from.</param>
    /// <param name="options">TCP connection options for new connections.</param>
    /// <param name="endpoint">The target endpoint (host, port, scheme, version).</param>
    /// <returns>
    /// A <see cref="SingleRequestConnectionScope"/> for HTTP/1.0, or
    /// a <see cref="PersistentConnectionScope"/> for HTTP/1.1+.
    /// </returns>
    public static IConnectionScope Create(
        Version version,
        ConnectionPool pool,
        TcpOptions options,
        RequestEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(pool);

        if (version is { Major: 1, Minor: 0 })
        {
            return new SingleRequestConnectionScope(pool, options, endpoint);
        }

        return new PersistentConnectionScope(pool, options, endpoint);
    }
}
