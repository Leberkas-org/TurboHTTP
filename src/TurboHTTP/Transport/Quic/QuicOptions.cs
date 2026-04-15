using System.Net.Security;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Transport.Quic;

/// <summary>
/// QUIC connection options, extending <see cref="TcpOptions"/> with QUIC-specific settings.
/// </summary>
public record QuicOptions : TlsOptions
{
    /// <summary>The idle timeout after which the QUIC connection is closed.</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum number of bidirectional streams the peer may open concurrently.</summary>
    public int MaxBidirectionalStreams { get; init; } = 100;

    /// <summary>Maximum number of unidirectional streams the peer may open concurrently.</summary>
    public int MaxUnidirectionalStreams { get; init; } = 3;

    /// <summary>
    /// Whether to allow QUIC 0-RTT early data for idempotent requests.
    /// When enabled, repeat connections to known servers may send requests before the
    /// TLS handshake completes, reducing latency. If the server rejects 0-RTT, the
    /// request is automatically re-sent after full handshake. Default is false.
    /// </summary>
    public bool AllowEarlyData { get; init; }

    /// <summary>
    /// Whether to allow QUIC connection migration when the client's local address changes.
    /// When enabled, the connection continues transparently. When disabled, the transport
    /// closes the connection and triggers a reconnect. Default is true. RFC 9000 §9.
    /// </summary>
    public bool AllowConnectionMigration { get; init; } = true;
}