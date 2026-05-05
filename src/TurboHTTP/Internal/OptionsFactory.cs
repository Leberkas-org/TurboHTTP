using System.Net.Security;
using Servus.Akka.Transport;

namespace TurboHTTP.Internal;

internal static class OptionsFactory
{
    internal static TransportOptions Build(RequestEndpoint endpoint, TurboClientOptions clientOptions)
    {
        var isTls = endpoint.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
                    || endpoint.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase);
        var port = (ushort)(endpoint.Port != 0 ? endpoint.Port : isTls ? 443 : 80);
        List<SslApplicationProtocol>? alpn = endpoint.Version switch
        {
            { Major: 3, Minor: 0 } => [SslApplicationProtocol.Http3],
            { Major: 2, Minor: 0 } => [SslApplicationProtocol.Http2],
            { Major: 1, Minor: 1 } => [SslApplicationProtocol.Http11],
            _ => null
        };

        var poolKey = endpoint.Version switch
        {
            { Major: 2, Minor: 0 } => PoolKeys.Http2,
            { Major: 1, Minor: 1 } => PoolKeys.Http11,
            { Major: 1, Minor: 0 } => PoolKeys.Http10,
            _ => null
        };

        if (endpoint.Version is { Major: 3, Minor: 0 })
        {
            return new QuicTransportOptions
            {
                Host = endpoint.Host,
                Port = port,
                ServerCertificateValidationCallback = clientOptions.EffectiveServerCertificateValidationCallback,
                ConnectTimeout = clientOptions.ConnectTimeout,
                SocketSendBufferSize = clientOptions.SocketSendBufferSize,
                SocketReceiveBufferSize = clientOptions.SocketReceiveBufferSize,
                AllowConnectionMigration = clientOptions.Http3.AllowConnectionMigration,
                IdleTimeout = clientOptions.Http3.IdleTimeout,
                MaxConnectionsPerHost = clientOptions.Http3.MaxConnectionsPerServer,
                MaxBidirectionalStreams = clientOptions.Http3.MaxConcurrentStreams,
                ApplicationProtocols = alpn,
                AutoReconnect = true,
                ConnectionLifetime = clientOptions.PooledConnectionLifetime
            };
        }

        if (isTls)
        {
            return new TlsTransportOptions
            {
                Host = endpoint.Host,
                Port = port,
                PoolKey = poolKey,
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

        return new TcpTransportOptions
        {
            Host = endpoint.Host,
            Port = port,
            PoolKey = poolKey,
            ConnectTimeout = clientOptions.ConnectTimeout,
            SocketSendBufferSize = clientOptions.SocketSendBufferSize,
            SocketReceiveBufferSize = clientOptions.SocketReceiveBufferSize,
            UseProxy = clientOptions.UseProxy,
            Proxy = clientOptions.Proxy,
            DefaultProxyCredentials = clientOptions.DefaultProxyCredentials,
        };
    }
}
