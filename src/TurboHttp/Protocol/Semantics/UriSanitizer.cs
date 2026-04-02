namespace TurboHttp.Protocol.Semantics;

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
    /// Formats the authority component always including the port number,
    /// even for default ports (80 for HTTP, 443 for HTTPS).
    /// Required for CONNECT request-target per RFC 9110 §9.3.6:
    /// "A client MUST send the port number even if it was elided from the target URI."
    /// </summary>
    public static string FormatAuthorityWithPort(Uri uri)
    {
        var host = uri.Host;

        // IPv6 addresses must be enclosed in brackets
        if (uri.HostNameType == UriHostNameType.IPv6 && !host.StartsWith('['))
        {
            host = $"[{host}]";
        }

        var port = uri.IsDefaultPort ? GetDefaultPort(uri.Scheme) : uri.Port;
        return $"{host}:{port}";
    }

    private static int GetDefaultPort(string scheme) => scheme switch
    {
        "https" => 443,
        "http" => 80,
        _ => throw new ArgumentException($"Unknown scheme '{scheme}' — cannot determine default port.", nameof(scheme)),
    };

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
