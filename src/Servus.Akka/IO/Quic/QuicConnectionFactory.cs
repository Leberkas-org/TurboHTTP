

// QUIC APIs are platform-guarded; usage is gated at runtime via QuicOptions.

using Servus.Akka.Diagnostics;
using Servus.Akka.IO.Tcp;

#pragma warning disable CA1416

namespace Servus.Akka.IO.Quic;

/// <summary>
/// Eagerly establishes a new QUIC connection and wraps it in a <see cref="QuicConnectionLease"/>.
/// Mirrors <see cref="TcpConnectionFactory"/> for the QUIC path.
/// </summary>
internal sealed class QuicConnectionFactory : IQuicConnectionFactory
{
    public static readonly QuicConnectionFactory Instance = new();

    /// <summary>
    /// Connects to <paramref name="endpoint"/> using <paramref name="options"/>,
    /// performs the TLS/QUIC handshake, and returns a ready-to-use
    /// <see cref="QuicConnectionLease"/>.
    /// </summary>
    public async Task<QuicConnectionLease> EstablishAsync(
        QuicOptions options, RequestEndpoint endpoint, CancellationToken ct = default)
    {
        var provider = new QuicClientProvider(options);
        await provider.ConnectAsync(ct).ConfigureAwait(false);

        var handle = new QuicConnectionHandle(provider, options, endpoint);
        var lease = new QuicConnectionLease(handle);

        ServusTrace.Connection.Debug(Instance, "QUIC connected to {0}:{1}", endpoint.Host, endpoint.Port);

        ServusMetrics.OpenConnections.Add(1,
            new("http.connection.state", "active"),
            new("server.address", endpoint.Host),
            new("server.port", endpoint.Port));

        return lease;
    }
}