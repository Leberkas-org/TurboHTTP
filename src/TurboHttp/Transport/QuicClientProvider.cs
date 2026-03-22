using System;
using System.IO;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using TurboHttp.Protocol.RFC9114;

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

        // RFC 9114 §3.3: Validate server certificate covers the target hostname
        // for safe connection coalescing. Skip if user provides a custom callback
        // (they are handling validation themselves).
        if (options.ServerCertificateValidationCallback is null)
        {
            ValidateCertificateHostname(_connection, options.Host);
        }

        return await _connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, ct).ConfigureAwait(false);
    }

    private void ValidateCertificateHostname(QuicConnection connection, string hostname)
    {
        var remoteCert = connection.RemoteCertificate;
        if (remoteCert is null)
        {
            CloseConnection(connection);
            throw new Http3ConnectionException(
                Http3ErrorCode.GeneralProtocolError,
                $"QUIC connection to '{hostname}' did not provide a server certificate (RFC 9114 §3.3).");
        }

        // QuicConnection.RemoteCertificate returns X509Certificate; convert to X509Certificate2 if needed.
        var cert2 = remoteCert as X509Certificate2 ?? new X509Certificate2(remoteCert);

        if (!Http3CertificateValidator.CoversHostname(cert2, hostname))
        {
            CloseConnection(connection);
            throw new Http3ConnectionException(
                Http3ErrorCode.GeneralProtocolError,
                $"Server certificate does not cover hostname '{hostname}'. "
                + "Connection coalescing is unsafe (RFC 9114 §3.3). "
                + $"Certificate subject: {cert2.Subject}");
        }
    }

    private static void CloseConnection(QuicConnection connection)
    {
        try
        {
            _ = connection.DisposeAsync();
        }
        catch (ObjectDisposedException)
        {
            // noop
        }
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