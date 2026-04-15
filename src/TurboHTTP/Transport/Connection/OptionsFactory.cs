using System.Net.Security;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Quic;

namespace TurboHTTP.Transport.Connection;

internal static class OptionsFactory
{
    private static bool IsHttp3(Version? requestVersion)
    {
        return requestVersion is { Major: 3, Minor: 0 };
    }

    internal static TcpOptions Build(RequestEndpoint endpoint, TurboClientOptions clientOptions)
    {
        var isTls = endpoint.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
                    || endpoint.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase);
        var port = endpoint.Port != 0 ? endpoint.Port : isTls ? 443 : 80;
        List<SslApplicationProtocol>? alpn = endpoint.Version switch
        {
            { Major: 3, Minor: 0 } => [SslApplicationProtocol.Http3],
            { Major: 2, Minor: 0 } => [SslApplicationProtocol.Http2],
            { Major: 1, Minor: 1 } => [SslApplicationProtocol.Http11],
            _ => null
        };

        if (IsHttp3(endpoint.Version))
        {
            return new QuicOptions
            {
                Host = endpoint.Host,
                Port = port,
                ServerCertificateValidationCallback = clientOptions.EffectiveServerCertificateValidationCallback,
                ConnectTimeout = clientOptions.ConnectTimeout,
                SocketSendBufferSize = clientOptions.SocketSendBufferSize,
                SocketReceiveBufferSize = clientOptions.SocketReceiveBufferSize,
                AllowConnectionMigration = clientOptions.Http3.AllowConnectionMigration,
                AllowEarlyData = clientOptions.Http3.AllowEarlyData,
                ApplicationProtocols = alpn
            };
        }

        if (isTls)
        {
            return new TlsOptions
            {
                Host = endpoint.Host,
                Port = port,
                TargetHost = endpoint.Host,
                ServerCertificateValidationCallback = clientOptions.EffectiveServerCertificateValidationCallback,
                ClientCertificates = clientOptions.ClientCertificates,
                EnabledSslProtocols = clientOptions.EnabledSslProtocols,
                ConnectTimeout = clientOptions.ConnectTimeout,
                SocketSendBufferSize = clientOptions.SocketSendBufferSize,
                SocketReceiveBufferSize = clientOptions.SocketReceiveBufferSize,
                UseProxy = clientOptions.UseProxy,
                Proxy = clientOptions.Proxy,
                DefaultProxyCredentials = clientOptions.DefaultProxyCredentials,
                ApplicationProtocols = alpn,
            };
        }

        return new TcpOptions
        {
            Host = endpoint.Host,
            Port = port,
            ConnectTimeout = clientOptions.ConnectTimeout,
            SocketSendBufferSize = clientOptions.SocketSendBufferSize,
            SocketReceiveBufferSize = clientOptions.SocketReceiveBufferSize,
            UseProxy = clientOptions.UseProxy,
            Proxy = clientOptions.Proxy,
            DefaultProxyCredentials = clientOptions.DefaultProxyCredentials,
        };
    }
}