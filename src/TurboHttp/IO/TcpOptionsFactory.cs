using System;
using System.Net.Sockets;
using TurboHttp.Client;

namespace TurboHttp.IO;

internal static class TcpOptionsFactory
{
    internal static TcpOptions Build(Uri requestUri, TurboClientOptions clientOptions)
    {
        var host = requestUri.Host;
        var scheme = requestUri.Scheme;
        var isTls = string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(scheme, "wss", StringComparison.OrdinalIgnoreCase);
        var port = requestUri.Port != -1 ? requestUri.Port : isTls ? 443 : 80;
        var af = AddressFamilyOf(requestUri.HostNameType);

        if (isTls)
        {
            return new TlsOptions
            {
                Host = host,
                Port = port,
                AddressFamily = af,
                TargetHost = host,
                ServerCertificateValidationCallback = clientOptions.ServerCertificateValidationCallback,
                ClientCertificates = clientOptions.ClientCertificates,
                EnabledSslProtocols = clientOptions.EnabledSslProtocols,
                ConnectTimeout = clientOptions.ConnectTimeout,
                ReconnectInterval = clientOptions.ReconnectInterval,
                MaxReconnectAttempts = clientOptions.MaxReconnectAttempts,
                MaxFrameSize = clientOptions.MaxFrameSize,
            };
        }

        return new TcpOptions
        {
            Host = host,
            Port = port,
            AddressFamily = af,
            ConnectTimeout = clientOptions.ConnectTimeout,
            ReconnectInterval = clientOptions.ReconnectInterval,
            MaxReconnectAttempts = clientOptions.MaxReconnectAttempts,
            MaxFrameSize = clientOptions.MaxFrameSize,
        };
    }

    private static AddressFamily AddressFamilyOf(UriHostNameType type)
        => type switch
        {
            UriHostNameType.IPv4 => AddressFamily.InterNetwork,
            UriHostNameType.IPv6 => AddressFamily.InterNetworkV6,
            _ => AddressFamily.Unspecified,
        };
}