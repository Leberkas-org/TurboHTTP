using System;
using System.Net;
using System.Net.Sockets;
using TurboHttp.Client;

namespace TurboHttp.Transport;

internal static class TcpOptionsFactory
{
    private static bool IsTls(this Uri value)
    {
        return string.Equals(value.Scheme, "https", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value.Scheme, "wss", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHttp3(Version? requestVersion)
    {
        return requestVersion is not null
               && requestVersion.Major == 3
               && requestVersion.Minor == 0;
    }

    internal static TcpOptions Build(Uri requestUri, TurboClientOptions clientOptions, Version? requestVersion = null)
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

        if (IsHttp3(requestVersion))
        {
            return new QuicOptions
            {
                Host = host,
                Port = port,
                AddressFamily = af,
                ServerCertificateValidationCallback = clientOptions.EffectiveServerCertificateValidationCallback,
                ConnectTimeout = clientOptions.ConnectTimeout,
                ReconnectInterval = clientOptions.ReconnectInterval,
                MaxReconnectAttempts = clientOptions.MaxReconnectAttempts,
                MaxFrameSize = clientOptions.MaxFrameSize,
            };
        }

        if (isTls)
        {
            return new TlsOptions
            {
                Host = host,
                Port = port,
                AddressFamily = af,
                TargetHost = host,
                ServerCertificateValidationCallback = clientOptions.EffectiveServerCertificateValidationCallback,
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