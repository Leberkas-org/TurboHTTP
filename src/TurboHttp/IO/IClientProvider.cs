using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace TurboHttp.IO;

public interface IClientProvider
{
    EndPoint? RemoteEndPoint { get; }
    Stream GetStream();
    void Close();
}

public class TcpClientProvider(TcpOptions options) : IClientProvider
{
    private Socket? _socket;

    public EndPoint? RemoteEndPoint => _socket?.RemoteEndPoint;

    public Stream GetStream()
    {
        var host = options.Host;
        var port = options.Port;

        _socket = CreateSocket();
        var addresses = Dns.GetHostAddresses(host);
        if (addresses.Length == 0)
        {
            throw new InvalidOperationException($"Could not resolve any IP addresses for host '{host}'.");
        }

        _socket.Connect(addresses, port);
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

public class TlsClientProvider(TlsOptions options) : IClientProvider
{
    private readonly TcpClientProvider _tcpClientProvider = new(options);
    private SslStream? _sslStream;

    public EndPoint? RemoteEndPoint => _tcpClientProvider.RemoteEndPoint;

    public Stream GetStream()
    {
        var networkStream = _tcpClientProvider.GetStream();
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

        _sslStream.AuthenticateAsClient(authOptions);
        return _sslStream!;
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
}

public record TlsOptions : TcpOptions
{
    public string? TargetHost { get; init; }
    public X509CertificateCollection? ClientCertificates { get; init; }
    public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; init; }
    public SslProtocols EnabledSslProtocols { get; init; } = SslProtocols.None;
}

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