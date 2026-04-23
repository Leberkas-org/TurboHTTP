using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Servus.Akka.Diagnostics;

namespace Servus.Akka.IO.Tcp;

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

        var connectHost = proxyUri?.Host ?? options.Host;
        var connectPort = proxyUri?.Port ?? options.Port;

        _socket = CreateSocket(options.SocketSendBufferSize, options.SocketReceiveBufferSize);

        var dnsActivity = ServusInstrumentation.StartDnsLookup(connectHost);
        IPAddress[] addresses;
        try
        {
            var dnsStart = Stopwatch.GetTimestamp();
            addresses = await Dns.GetHostAddressesAsync(connectHost, ct).ConfigureAwait(false);
            var dnsDuration = Stopwatch.GetElapsedTime(dnsStart).TotalSeconds;

            if (addresses.Length == 0)
            {
                throw new InvalidOperationException($"Could not resolve any IP addresses for host '{connectHost}'.");
            }

            if (dnsActivity is not null)
            {
                ServusInstrumentation.SetDnsAnswers(dnsActivity,
                    Array.ConvertAll(addresses, a => a.ToString()));
            }

            ServusMetrics.DnsLookupDuration.Record(dnsDuration,
                new KeyValuePair<string, object?>("dns.question.name", connectHost));
            dnsActivity?.Stop();
            ServusTrace.Dns.Debug(this, "DNS '{0}' resolved {1} address(es)", connectHost, addresses.Length);
        }
        catch (Exception ex)
        {
            if (dnsActivity is not null)
            {
                ServusInstrumentation.SetError(dnsActivity, ex);
                dnsActivity.Stop();
            }

            ServusTrace.Dns.Warning(this, "DNS '{0}' failed: {1}", connectHost, ex.Message);
            throw;
        }

        var networkType = addresses[0].AddressFamily == AddressFamily.InterNetworkV6
            ? "ipv6"
            : "ipv4";
        var socketActivity = ServusInstrumentation.StartSocketConnect(
            addresses[0].ToString(), connectPort, "tcp", networkType);
        try
        {
            await _socket.ConnectAsync(addresses, connectPort, ct).ConfigureAwait(false);
            socketActivity?.Stop();
            ServusTrace.Connection.Debug(this, "TCP connected to {0}:{1}", addresses[0], connectPort);
        }
        catch (Exception ex)
        {
            if (socketActivity is not null)
            {
                ServusInstrumentation.SetError(socketActivity, ex);
                socketActivity.Stop();
            }

            ServusTrace.Connection.Warning(this, "TCP connect to {0}:{1} failed: {2}", addresses[0], connectPort, ex.Message);
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