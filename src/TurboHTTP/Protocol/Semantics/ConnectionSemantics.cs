using System.Net;

namespace TurboHTTP.Protocol.Semantics;

internal static class ConnectionSemantics
{
    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        WellKnownHeaders.Connection,
        WellKnownHeaders.KeepAliveHeader,
        WellKnownHeaders.TransferEncoding,
        WellKnownHeaders.Te,
        WellKnownHeaders.Upgrade,
        WellKnownHeaders.ProxyAuthenticate,
        WellKnownHeaders.ProxyAuthorization,
        WellKnownHeaders.Trailer
    };

    public static bool IsHopByHop(string headerName) => HopByHopHeaders.Contains(headerName);

    public static bool IsPersistent(HeaderCollection headers, Version version)
    {
        var hasKeepAlive = false;
        var hasClose = false;

        foreach (var v in headers.GetValues(WellKnownHeaders.Connection))
        {
            foreach (var part in v.AsSpan().Split(','))
            {
                var t = HeaderValidation.TrimOws(v[part.Start..part.End]);
                if (string.IsNullOrEmpty(t))
                {
                    continue;
                }

                if (string.Equals(t, WellKnownHeaders.KeepAliveValue, StringComparison.OrdinalIgnoreCase))
                {
                    hasKeepAlive = true;
                }
                else if (string.Equals(t, WellKnownHeaders.CloseValue, StringComparison.OrdinalIgnoreCase))
                {
                    hasClose = true;
                }
            }
        }

        if (version.Equals(HttpVersion.Version10))
        {
            return hasKeepAlive;
        }

        if (version.Equals(HttpVersion.Version11))
        {
            return !hasClose;
        }

        return true;
    }
}