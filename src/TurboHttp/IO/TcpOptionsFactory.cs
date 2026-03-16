using System;
using System.Net.Sockets;
using TurboHttp.Client;
using TurboHttp.IO.Stages;

namespace TurboHttp.IO;

internal static class TcpOptionsFactory
{
    internal static bool IsTls(this Uri value)
    {
        return string.Equals(value.Scheme, "https", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value.Scheme, "wss", StringComparison.OrdinalIgnoreCase);
    }

    internal static TcpOptions Build(Uri requestUri, TurboClientOptions clientOptions)
    {
        var host = requestUri.Host;
        var isTls = requestUri.IsTls();
        int port;
        if (requestUri.Port is not -1)
        {
            port = requestUri.Port;
        }
        else
        {
            port = isTls ? 443 : 80;
        }

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