using System;

namespace TurboHttp.Protocol.RFC9110;

/// <summary>
/// URI sanitization utilities per RFC 9110 §4.2.4:
/// "A sender MUST NOT generate the userinfo subcomponent (and its '@' delimiter)
/// in a URI reference."
/// </summary>
internal static class UriSanitizer
{
    /// <summary>
    /// Formats the authority component of a URI without the userinfo subcomponent.
    /// Returns host (with brackets for IPv6) and non-default port.
    /// </summary>
    public static string FormatAuthority(Uri uri)
    {
        var host = uri.Host;

        // IPv6 addresses must be enclosed in brackets
        if (uri.HostNameType == UriHostNameType.IPv6 && !host.StartsWith('['))
        {
            host = $"[{host}]";
        }

        return uri.IsDefaultPort ? host : $"{host}:{uri.Port}";
    }

    /// <summary>
    /// Rebuilds an absolute URI string without the userinfo subcomponent.
    /// Preserves scheme, host, port, path, query, and fragment.
    /// </summary>
    public static string StripUserInfo(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
        };

        return builder.Uri.ToString();
    }

    /// <summary>
    /// Returns the absolute URI without userinfo, excluding fragment.
    /// Suitable for HTTP/1.x absolute-form request-target.
    /// </summary>
    public static string FormatAbsoluteWithoutUserInfo(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Fragment = string.Empty,
        };

        return builder.Uri.GetLeftPart(UriPartial.Query);
    }
}
