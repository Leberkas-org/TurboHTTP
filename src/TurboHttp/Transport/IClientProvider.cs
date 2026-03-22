using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace TurboHttp.Transport;

/// <summary>
/// Abstracts a raw TCP or TLS connection so that <see cref="ClientState"/> is independent
/// of the underlying transport.
/// </summary>
public interface IClientProvider : IAsyncDisposable
{
    /// <summary>Gets the remote endpoint the socket is connected to, or <see langword="null"/> if not yet connected.</summary>
    EndPoint? RemoteEndPoint { get; }

    /// <summary>Opens a connection to the configured host asynchronously and returns the network stream.</summary>
    Task<Stream> GetStreamAsync(CancellationToken ct = default);

    /// <summary>Closes the connection and releases all transport resources.</summary>
    void Close();

    /// <summary>
    /// Indicates whether this provider supports opening multiple streams on a single connection.
    /// Returns <see langword="true"/> for QUIC (HTTP/3), <see langword="false"/> for TCP/TLS.
    /// </summary>
    bool SupportsMultipleStreams => false;
}

/// <summary>
/// Plain TCP implementation of <see cref="IClientProvider"/>.
/// </summary>
public class TcpClientProvider(TcpOptions options) : IClientProvider
{
    private Socket? _socket;

    public EndPoint? RemoteEndPoint => _socket?.RemoteEndPoint;

    public async Task<Stream> GetStreamAsync(CancellationToken ct = default)
    {
        var host = options.Host;
        var port = options.Port;

        _socket = CreateSocket();
        var addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
        if (addresses.Length == 0)
        {
            throw new InvalidOperationException($"Could not resolve any IP addresses for host '{host}'.");
        }

        await _socket.ConnectAsync(addresses, port, ct).ConfigureAwait(false);
        return new NetworkStream(_socket, ownsSocket: false);
    }

    public void Close()
    {
        if (_socket is null)
        {
            return;
        }

        try
        {
            _socket.Close();
            _socket.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // noop
        }
        finally
        {
            _socket = null;
        }
    }

    public ValueTask DisposeAsync()
    {
        Close();
        return ValueTask.CompletedTask;
    }

    private Socket CreateSocket()
    {
        var addressFamily = options.AddressFamily;
        var result = new Socket(addressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
            LingerState = new LingerOption(true, 0)
        };

        if (addressFamily is AddressFamily.Unspecified)
        {
            result = new Socket(SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
                LingerState = new LingerOption(true, 0)
            };
        }

        result.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        if (addressFamily is AddressFamily.InterNetworkV6)
        {
            result.DualMode = true;
        }

        return result;
    }
}

/// <summary>
/// TLS-wrapped implementation of <see cref="IClientProvider"/>. Establishes a plain TCP connection
/// first and then performs TLS handshake using <see cref="SslStream"/>.
/// </summary>
public class TlsClientProvider(TlsOptions options) : IClientProvider
{
    private readonly TcpClientProvider _tcpClientProvider = new(options);
    private SslStream? _sslStream;

    public EndPoint? RemoteEndPoint => _tcpClientProvider.RemoteEndPoint;

    public async Task<Stream> GetStreamAsync(CancellationToken ct = default)
    {
        var networkStream = await _tcpClientProvider.GetStreamAsync(ct).ConfigureAwait(false);
        _sslStream = new SslStream(
            networkStream,
            leaveInnerStreamOpen: false,
            options.ServerCertificateValidationCallback
        );

        var targetHost = options.TargetHost ?? options.Host;
        var authOptions = new SslClientAuthenticationOptions
        {
            TargetHost = targetHost,
            EnabledSslProtocols = options.EnabledSslProtocols,
            ClientCertificates = options.ClientCertificates,
        };

        await _sslStream.AuthenticateAsClientAsync(authOptions, ct).ConfigureAwait(false);
        return _sslStream;
    }

    public void Close()
    {
        if (_sslStream is not null)
        {
            try
            {
                _sslStream.Close();
                _sslStream.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // noop
            }
            finally
            {
                _sslStream = null;
            }
        }

        _tcpClientProvider.Close();
    }

    public async ValueTask DisposeAsync()
    {
        if (_sslStream is not null)
        {
            try
            {
                await _sslStream.DisposeAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // noop
            }
            finally
            {
                _sslStream = null;
            }
        }

        await _tcpClientProvider.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// TLS connection options, extending <see cref="TcpOptions"/> with certificate and protocol settings.
/// </summary>
public record TlsOptions : TcpOptions
{
    public string? TargetHost { get; init; }
    public X509CertificateCollection? ClientCertificates { get; init; }
    public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; init; }
    public SslProtocols EnabledSslProtocols { get; init; } = SslProtocols.None;
}

/// <summary>
/// Configuration options for a plain TCP connection.
/// </summary>
public record TcpOptions
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public int MaxFrameSize { get; init; } = 128 * 1024;
    public AddressFamily AddressFamily { get; init; } = AddressFamily.Unspecified;
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan ReconnectInterval { get; init; } = TimeSpan.FromSeconds(5);
    public int MaxReconnectAttempts { get; init; } = 10;
}