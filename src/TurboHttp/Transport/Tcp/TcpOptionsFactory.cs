using TurboHttp.Internal;
using TurboHttp.Transport.Connection;
using TurboHttp.Transport.Quic;

namespace TurboHttp.Transport.Tcp;

internal static class TcpOptionsFactory
{
    private static bool IsTls(this Uri value)
    {
        return string.Equals(value.Scheme, "https", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value.Scheme, "wss", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHttp3(Version? requestVersion)
    {
        return requestVersion is { Major: 3, Minor: 0 };
    }

    /// <summary>
    /// Builds <see cref="TcpOptions"/> from a <see cref="RequestEndpoint"/>.
    /// Used by <see cref="TcpConnectionStage"/> for auto-connect (no separate ExtractOptionsStage).
    /// </summary>
    internal static TcpOptions Build(RequestEndpoint endpoint, TurboClientOptions clientOptions)
    {
        var isTls = endpoint.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
                    || endpoint.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase);
        var port = endpoint.Port != 0 ? endpoint.Port : (isTls ? 443 : 80);

        if (IsHttp3(endpoint.Version))
        {
            return new QuicOptions
            {
                Host = endpoint.Host,
                Port = port,
                ServerCertificateValidationCallback = clientOptions.EffectiveServerCertificateValidationCallback,
                ConnectTimeout = clientOptions.ConnectTimeout,
                MaxFrameSize = clientOptions.MaxFrameSize,
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
                MaxFrameSize = clientOptions.MaxFrameSize,
            };
        }

        return new TcpOptions
        {
            Host = endpoint.Host,
            Port = port,
            ConnectTimeout = clientOptions.ConnectTimeout,
            MaxFrameSize = clientOptions.MaxFrameSize,
        };
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

        if (IsHttp3(requestVersion))
        {
            return new QuicOptions
            {
                Host = host,
                Port = port,
                ServerCertificateValidationCallback = clientOptions.EffectiveServerCertificateValidationCallback,
                ConnectTimeout = clientOptions.ConnectTimeout,
                MaxFrameSize = clientOptions.MaxFrameSize,
            };
        }

        if (isTls)
        {
            return new TlsOptions
            {
                Host = host,
                Port = port,
                TargetHost = host,
                ServerCertificateValidationCallback = clientOptions.EffectiveServerCertificateValidationCallback,
                ClientCertificates = clientOptions.ClientCertificates,
                EnabledSslProtocols = clientOptions.EnabledSslProtocols,
                ConnectTimeout = clientOptions.ConnectTimeout,
                MaxFrameSize = clientOptions.MaxFrameSize,
            };
        }

        return new TcpOptions
        {
            Host = host,
            Port = port,
            ConnectTimeout = clientOptions.ConnectTimeout,
            MaxFrameSize = clientOptions.MaxFrameSize,
        };
    }
}