using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using Servus.Akka.Diagnostics;

namespace Servus.Akka.IO.Tcp;

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

        var tlsActivity = ServusInstrumentation.StartTlsHandshake(targetHost);
        ServusTrace.Tls.Debug(this, "TLS handshake starting with '{0}'", targetHost);
        try
        {
            await _sslStream.AuthenticateAsClientAsync(authOptions, ct)
                .WaitAsync(options.ConnectTimeout, ct)
                .ConfigureAwait(false);

            if (tlsActivity is not null)
            {
                var protocolVersion = _sslStream.SslProtocol switch
                {
                    SslProtocols.Tls12 => "1.2",
                    SslProtocols.Tls13 => "1.3",
                    _ => _sslStream.SslProtocol.ToString()
                };
                ServusInstrumentation.SetTlsInfo(tlsActivity, "tls", protocolVersion);
                tlsActivity.Stop();
            }

            ServusTrace.Tls.Debug(this, "TLS handshake completed with '{0}'", targetHost);
        }
        catch (Exception ex)
        {
            if (tlsActivity is not null)
            {
                ServusInstrumentation.SetError(tlsActivity, ex);
                tlsActivity.Stop();
            }

            ServusTrace.Tls.Warning(this, "TLS handshake with '{0}' failed: {1}", targetHost, ex.Message);
            throw;
        }

        return _sslStream;
    }

    /// <summary>
    /// Sends an HTTP CONNECT request through the proxy to establish a tunnel to the target host.
    /// RFC 9110 §9.3.6: the CONNECT method requests that the proxy establish a tunnel.
    /// </summary>
    public static async Task EstablishConnectTunnelAsync(
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