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
/// Pure transport QUIC implementation of <see cref="IClientProvider"/>. Establishes a single QUIC
/// connection on the first call and opens a new bidirectional stream for each subsequent call.
/// Contains no HTTP/3 protocol logic — all protocol concerns (control stream, QPACK, SETTINGS)
/// are handled by stages in <c>Http30Engine</c>.
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

    public async Task<Stream> GetUnidirectionalStreamAsync(CancellationToken ct = default)
    {
        var connection = await EnsureConnectedAsync(ct).ConfigureAwait(false);

        try
        {
            return await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, ct).ConfigureAwait(false);
        }
        catch (QuicException ex)
        {
            Interlocked.CompareExchange(ref _connection, null, connection);
            throw new InvalidOperationException(
                $"QUIC connection to '{options.Host}:{options.Port}' is no longer usable. "
                + "A new connection will be established on the next request.", ex);
        }
    }

    public async Task<Stream> AcceptInboundStreamAsync(CancellationToken ct = default)
    {
        var connection = await EnsureConnectedAsync(ct).ConfigureAwait(false);

        try
        {
            return await connection.AcceptInboundStreamAsync(ct).ConfigureAwait(false);
        }
        catch (QuicException ex)
        {
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

            // TLS 1.3 handshake requires SNI extension for QUIC connections.
            if (string.IsNullOrEmpty(options.Host))
            {
                throw new InvalidOperationException(
                    "QUIC connections require a non-empty hostname for TLS SNI. "
                    + "Cannot establish a QUIC connection without Server Name Indication.");
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
                }
            };

            var connection = await QuicConnection.ConnectAsync(clientConnectionOptions, ct).ConfigureAwait(false);

            Volatile.Write(ref _connection, connection);
            return connection;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private static async ValueTask CloseConnectionAsync(QuicConnection connection)
    {
        try
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // noop
        }
    }

    public async ValueTask DisposeAsync()
    {
        var connection = Interlocked.Exchange(ref _connection, null);
        if (connection is null)
        {
            return;
        }

        await CloseConnectionAsync(connection).ConfigureAwait(false);
        _connectLock.Dispose();
    }
}