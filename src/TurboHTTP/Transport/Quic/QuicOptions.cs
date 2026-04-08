using System.Net.Security;

using TurboHTTP.Transport.Connection;
namespace TurboHTTP.Transport.Quic;

/// <summary>
/// QUIC connection options, extending <see cref="TcpOptions"/> with QUIC-specific settings.
/// </summary>
public record QuicOptions : TcpOptions
{
    /// <summary>The idle timeout after which the QUIC connection is closed.</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum number of bidirectional streams the peer may open concurrently.</summary>
    public int MaxBidirectionalStreams { get; init; } = 100;

    /// <summary>Maximum number of unidirectional streams the peer may open concurrently.</summary>
    public int MaxUnidirectionalStreams { get; init; } = 3;

    /// <summary>ALPN protocols advertised during the QUIC handshake. Defaults to h3.</summary>
    public List<SslApplicationProtocol> ApplicationProtocols { get; init; } = [new("h3")];

    /// <summary>Optional callback to validate the server certificate during the QUIC/TLS handshake.</summary>
    public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; init; }
}
