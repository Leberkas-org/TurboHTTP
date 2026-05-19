using System.Net.Security;

namespace TurboHTTP.Server;

[Flags]
public enum HttpProtocols
{
    None = 0,
    Http1 = 1,
    Http2 = 2,
    Http1AndHttp2 = Http1 | Http2,
    Http3 = 4
}

public static class HttpProtocolsExtensions
{
    public static List<SslApplicationProtocol> ToAlpnProtocols(this HttpProtocols protocols)
    {
        var result = new List<SslApplicationProtocol>();

        if ((protocols & HttpProtocols.Http3) != 0)
        {
            result.Add(SslApplicationProtocol.Http3);
        }

        if ((protocols & HttpProtocols.Http2) != 0)
        {
            result.Add(SslApplicationProtocol.Http2);
        }

        if ((protocols & HttpProtocols.Http1) != 0)
        {
            result.Add(SslApplicationProtocol.Http11);
        }

        return result;
    }
}