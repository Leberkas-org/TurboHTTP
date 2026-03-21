using System;
using System.IO;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace TurboHttp.Transport;

/// <summary>
/// QUIC implementation of <see cref="IClientProvider"/>. Establishes a QUIC connection
/// and opens a bidirectional stream for HTTP/3 communication.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macOS")]
[SupportedOSPlatform("windows")]
public sealed class QuicClientProvider(QuicOptions options) : IClientProvider
{
    private QuicConnection? _connection;

    public EndPoint? RemoteEndPoint => _connection?.RemoteEndPoint;

    public async Task<Stream> GetStreamAsync(CancellationToken ct = default)
    {
        // RFC 9114 §3.2: TLS handshake MUST include SNI extension.
        // A null or empty host means no SNI can be sent, which is a protocol violation.
        if (string.IsNullOrEmpty(options.Host))
        {
            throw new InvalidOperationException(
                "QUIC connections require a non-empty hostname for TLS SNI (RFC 9114 §3.2). "
                + "Cannot establish HTTP/3 connection without Server Name Indication.");
        }

        var clientConnectionOptions = new QuicClientConnectionOptions
        {
            RemoteEndPoint = new DnsEndPoint(options.Host, options.Port),
            DefaultStreamErrorCode = 0x0100, // H3_NO_ERROR
            DefaultCloseErrorCode = 0x0100,  // H3_NO_ERROR
            MaxInboundBidirectionalStreams = options.MaxBidirectionalStreams,
            MaxInboundUnidirectionalStreams = options.MaxUnidirectionalStreams,
            IdleTimeout = options.IdleTimeout,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                TargetHost = options.Host,
                ApplicationProtocols = options.ApplicationProtocols,
                RemoteCertificateValidationCallback = options.ServerCertificateValidationCallback,
            },
        };

        _connection = await QuicConnection.ConnectAsync(clientConnectionOptions, ct).ConfigureAwait(false);
        return await _connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, ct).ConfigureAwait(false);
    }

    public void Close()
    {
        if (_connection is null)
        {
            return;
        }

        try
        {
            _ = _connection.DisposeAsync();
        }
        catch (ObjectDisposedException)
        {
            // noop
        }
        finally
        {
            _connection = null;
        }
    }
}