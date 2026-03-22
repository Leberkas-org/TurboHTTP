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
/// QUIC implementation of <see cref="IClientProvider"/>. Establishes a single QUIC connection
/// on the first call and opens a new bidirectional stream for each subsequent call,
/// enabling HTTP/3 request multiplexing per RFC 9114.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macOS")]
[SupportedOSPlatform("windows")]
public sealed class QuicClientProvider(QuicOptions options) : IClientProvider
{
    private QuicConnection? _connection;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    public EndPoint? RemoteEndPoint => _connection?.RemoteEndPoint;

    public bool SupportsMultipleStreams => true;

    public async Task<Stream> GetStreamAsync(CancellationToken ct = default)
    {
        var connection = await EnsureConnectedAsync(ct).ConfigureAwait(false);

        try
        {
            return await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, ct).ConfigureAwait(false);
        }
        catch (QuicException ex)
        {
            // Connection is dead — clear it so the next call triggers reconnect.
            Interlocked.CompareExchange(ref _connection, null, connection);
            throw new InvalidOperationException(
                $"QUIC connection to '{options.Host}:{options.Port}' is no longer usable. "
                + "A new connection will be established on the next request.", ex);
        }
    }

    /// <summary>
    /// Ensures a QUIC connection is established. The first caller connects; concurrent callers
    /// wait on the semaphore and then reuse the established connection.
    /// </summary>
    private async Task<QuicConnection> EnsureConnectedAsync(CancellationToken ct)
    {
        // Fast path: connection already established.
        var existing = Volatile.Read(ref _connection);
        if (existing is not null)
        {
            return existing;
        }

        await _connectLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring the lock.
            existing = Volatile.Read(ref _connection);
            if (existing is not null)
            {
                return existing;
            }

            // RFC 9114 §3.2: TLS handshake MUST include SNI extension.
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

            var connection = await QuicConnection.ConnectAsync(clientConnectionOptions, ct).ConfigureAwait(false);

            // RFC 9114 §3.3: Validate server certificate covers the target hostname
            // for safe connection coalescing. Skip if user provides a custom callback
            // (they are handling validation themselves).
            if (options.ServerCertificateValidationCallback is null)
            {
                ValidateCertificateHostname(connection, options.Host);
            }

            Volatile.Write(ref _connection, connection);
            return connection;
        }
        finally
        {
            _connectLock.Release();
        }
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
        var connection = Interlocked.Exchange(ref _connection, null);
        if (connection is null)
        {
            return;
        }

        try
        {
            _ = connection.DisposeAsync();
        }
        catch (ObjectDisposedException)
        {
            // noop
        }
    }
}
