using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using TurboHTTP.Diagnostics;

namespace TurboHTTP.Transport.Connection;

/// <summary>
/// Abstracts a raw TCP or TLS connection so that <see cref="ClientState"/> is independent
/// of the underlying transport.
/// </summary>
public interface IClientProvider : IAsyncDisposable
{
    /// <summary>Gets the remote endpoint the socket is connected to, or <see langword="null"/> if not yet connected.</summary>
    EndPoint? RemoteEndPoint { get; }

    /// <summary>Gets the local endpoint the socket is bound to, or <see langword="null"/> if not yet connected.</summary>
    EndPoint? LocalEndPoint => null;

    /// <summary>Opens a connection to the configured host asynchronously and returns the network stream.</summary>
    Task<Stream> GetStreamAsync(CancellationToken ct = default);

    /// <summary>
    /// Indicates whether this provider supports opening multiple streams on a single connection.
    /// Returns <see langword="true"/> for QUIC (HTTP/3), <see langword="false"/> for TCP/TLS.
    /// </summary>
    bool SupportsMultipleStreams => false;

    /// <summary>
    /// Opens a unidirectional outbound stream on the underlying connection.
    /// Only supported by QUIC transports; TCP/TLS providers throw <see cref="NotSupportedException"/>.
    /// </summary>
    Task<Stream> GetUnidirectionalStreamAsync(CancellationToken ct = default)
        => throw new NotSupportedException("Unidirectional streams are only supported by QUIC transports.");

    /// <summary>
    /// Accepts a server-initiated inbound unidirectional stream.
    /// The caller is responsible for reading the stream-type byte from the returned stream.
    /// Only supported by QUIC transports; TCP/TLS providers throw <see cref="NotSupportedException"/>.
    /// </summary>
    Task<Stream> AcceptInboundStreamAsync(CancellationToken ct = default)
        => throw new NotSupportedException("Inbound streams are only supported by QUIC transports.");
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
        // Resolve proxy if configured
        var proxyUri = ResolveProxy(options);

        var connectHost = proxyUri is not null ? proxyUri.Host : options.Host;
        var connectPort = proxyUri is not null ? proxyUri.Port : options.Port;

        _socket = CreateSocket(options.SocketSendBufferSize, options.SocketReceiveBufferSize);

        var dnsActivity = TurboHttpInstrumentation.StartDnsLookup(connectHost);
        TurboHttpEventSource.Instance.DnsLookupStart(connectHost);
        IPAddress[] addresses;
        try
        {
            var dnsStart = Stopwatch.GetTimestamp();
            addresses = await Dns.GetHostAddressesAsync(connectHost, ct).ConfigureAwait(false);
            var dnsDurationMs = Stopwatch.GetElapsedTime(dnsStart).TotalMilliseconds;

            if (addresses.Length == 0)
            {
                throw new InvalidOperationException($"Could not resolve any IP addresses for host '{connectHost}'.");
            }

            if (dnsActivity is not null)
            {
                TurboHttpInstrumentation.SetDnsAnswers(dnsActivity,
                    Array.ConvertAll(addresses, a => a.ToString()));
            }

            TurboHttpEventSource.Instance.DnsLookupStop(connectHost, dnsDurationMs);
            TurboHttpMetrics.DnsLookupDuration.Record(dnsDurationMs / 1000.0,
                new KeyValuePair<string, object?>("dns.question.name", connectHost));
            dnsActivity?.Stop();
        }
        catch (Exception ex)
        {
            if (dnsActivity is not null)
            {
                TurboHttpInstrumentation.SetError(dnsActivity, ex);
                dnsActivity.Stop();
            }

            TurboHttpEventSource.Instance.DnsLookupStop(connectHost, 0);
            throw;
        }

        var networkType = addresses[0].AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? "ipv6"
            : "ipv4";
        var socketActivity = TurboHttpInstrumentation.StartSocketConnect(
            addresses[0].ToString(), connectPort, "tcp", networkType);
        try
        {
            await _socket.ConnectAsync(addresses, connectPort, ct).ConfigureAwait(false);
            socketActivity?.Stop();
        }
        catch (Exception ex)
        {
            if (socketActivity is not null)
            {
                TurboHttpInstrumentation.SetError(socketActivity, ex);
                socketActivity.Stop();
            }

            throw;
        }

        return new NetworkStream(_socket, ownsSocket: false);
    }

    /// <summary>
    /// Resolves the proxy URI for the target destination, or <see langword="null"/> if no proxy should be used.
    /// Applies <see cref="TcpOptions.DefaultProxyCredentials"/> to the proxy when credentials are not already set.
    /// </summary>
    private static Uri? ResolveProxy(TcpOptions options)
    {
        if (!options.UseProxy || options.Proxy is null)
        {
            return null;
        }

        var targetUri = new Uri($"http://{options.Host}:{options.Port}/");

        if (options.Proxy.IsBypassed(targetUri))
        {
            return null;
        }

        if (options.DefaultProxyCredentials is not null && options.Proxy.Credentials is null)
        {
            options.Proxy.Credentials = options.DefaultProxyCredentials;
        }

        return options.Proxy.GetProxy(targetUri);
    }

    public ValueTask DisposeAsync()
    {
        if (_socket is null)
        {
            return ValueTask.CompletedTask;
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

        return ValueTask.CompletedTask;
    }

    private static Socket CreateSocket(int? sendBufferSize, int? receiveBufferSize)
    {
        var result = new Socket(SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
            LingerState = new LingerOption(true, 0),
        };

        result.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

        if (sendBufferSize.HasValue)
        {
            result.SendBufferSize = sendBufferSize.Value;
        }

        if (receiveBufferSize.HasValue)
        {
            result.ReceiveBufferSize = receiveBufferSize.Value;
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

        // When connecting through a proxy, establish a CONNECT tunnel before TLS handshake.
        if (options is { UseProxy: true, Proxy: not null })
        {
            var proxyUri = options.Proxy.GetProxy(new Uri($"https://{options.Host}:{options.Port}/"));
            if (proxyUri is not null)
            {
                await EstablishConnectTunnelAsync(networkStream, options.Host, options.Port,
                    options.Proxy, options.DefaultProxyCredentials, ct).ConfigureAwait(false);
            }
        }

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
            ApplicationProtocols = options.ApplicationProtocols,
        };

        var tlsActivity = TurboHttpInstrumentation.StartTlsHandshake(targetHost);
        TurboHttpEventSource.Instance.TlsHandshakeStart(targetHost);
        var tlsStart = Stopwatch.GetTimestamp();
        try
        {
            await _sslStream.AuthenticateAsClientAsync(authOptions, ct)
                .WaitAsync(options.ConnectTimeout, ct)
                .ConfigureAwait(false);

            var tlsDurationMs = Stopwatch.GetElapsedTime(tlsStart).TotalMilliseconds;

            if (tlsActivity is not null)
            {
                var protocolName = _sslStream.SslProtocol switch
                {
                    SslProtocols.Tls12 => "tls",
                    SslProtocols.Tls13 => "tls",
                    _ => "tls"
                };
                var protocolVersion = _sslStream.SslProtocol switch
                {
                    SslProtocols.Tls12 => "1.2",
                    SslProtocols.Tls13 => "1.3",
                    _ => _sslStream.SslProtocol.ToString()
                };
                TurboHttpInstrumentation.SetTlsInfo(tlsActivity, protocolName, protocolVersion);
                tlsActivity.Stop();
            }

            TurboHttpEventSource.Instance.TlsHandshakeStop(targetHost, tlsDurationMs);
        }
        catch (Exception ex)
        {
            if (tlsActivity is not null)
            {
                TurboHttpInstrumentation.SetError(tlsActivity, ex);
                tlsActivity.Stop();
            }

            var tlsDurationMs = Stopwatch.GetElapsedTime(tlsStart).TotalMilliseconds;
            TurboHttpEventSource.Instance.TlsHandshakeStop(targetHost, tlsDurationMs);
            throw;
        }

        return _sslStream;
    }

    /// <summary>
    /// Sends an HTTP CONNECT request through the proxy to establish a tunnel to the target host.
    /// RFC 9110 §9.3.6: the CONNECT method requests that the proxy establish a tunnel.
    /// </summary>
    private static async Task EstablishConnectTunnelAsync(
        Stream proxyStream,
        string targetHost,
        int targetPort,
        IWebProxy proxy,
        ICredentials? defaultProxyCredentials,
        CancellationToken ct)
    {
        var connectRequest = $"CONNECT {targetHost}:{targetPort} HTTP/1.1\r\nHost: {targetHost}:{targetPort}\r\n";

        // Resolve proxy credentials: use explicit proxy credentials, fall back to default
        var proxyUri = proxy.GetProxy(new Uri($"https://{targetHost}:{targetPort}/"));
        var credentials = proxy.Credentials ?? defaultProxyCredentials;
        if (credentials is not null && proxyUri is not null)
        {
            var credential = credentials.GetCredential(proxyUri, "Basic");
            if (credential is not null)
            {
                var encoded = Convert.ToBase64String(
                    System.Text.Encoding.UTF8.GetBytes($"{credential.UserName}:{credential.Password}"));
                connectRequest += $"Proxy-Authorization: Basic {encoded}\r\n";
            }
        }

        connectRequest += "\r\n";

        var requestBytes = System.Text.Encoding.ASCII.GetBytes(connectRequest);
        await proxyStream.WriteAsync(requestBytes, ct).ConfigureAwait(false);
        await proxyStream.FlushAsync(ct).ConfigureAwait(false);

        // Read the proxy response status line
        var responseBuffer = new byte[4096];
        var totalRead = 0;
        while (totalRead < responseBuffer.Length)
        {
            var bytesRead = await proxyStream.ReadAsync(
                responseBuffer.AsMemory(totalRead, responseBuffer.Length - totalRead), ct).ConfigureAwait(false);

            if (bytesRead == 0)
            {
                throw new HttpRequestException("Proxy closed connection during CONNECT tunnel establishment.");
            }

            totalRead += bytesRead;

            // Check if we've received the full response headers (ends with \r\n\r\n)
            var response = System.Text.Encoding.ASCII.GetString(responseBuffer, 0, totalRead);
            if (response.Contains("\r\n\r\n"))
            {
                // Verify 200 status
                if (!response.StartsWith("HTTP/1.1 200", StringComparison.OrdinalIgnoreCase)
                    && !response.StartsWith("HTTP/1.0 200", StringComparison.OrdinalIgnoreCase))
                {
                    var statusLine = response[..response.IndexOf('\r')];
                    throw new HttpRequestException(
                        $"Proxy CONNECT tunnel failed: {statusLine}");
                }

                return;
            }
        }

        throw new HttpRequestException("Proxy CONNECT response exceeded buffer size.");
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
    public List<SslApplicationProtocol>? ApplicationProtocols { get; init; }
}

/// <summary>
/// Configuration options for a plain TCP connection.
/// </summary>
public record TcpOptions
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public int? SocketSendBufferSize { get; init; }
    public int? SocketReceiveBufferSize { get; init; }
    public bool UseProxy { get; init; }
    public IWebProxy? Proxy { get; init; }
    public ICredentials? DefaultProxyCredentials { get; init; }
}