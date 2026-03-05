using System;
using System.Globalization;

namespace TurboHttp.Protocol;

/// <summary>
/// RFC 6265 §5.2 — Parses a Set-Cookie header value into a <see cref="CookieEntry"/>.
/// </summary>
internal static class CookieParser
{
    /// <summary>
    /// Parses a single Set-Cookie header string.
    /// Returns null if the header is malformed (no name, empty name, etc.).
    /// Domain cookies whose Domain attribute does not match the request host are rejected (null).
    /// </summary>
    public static CookieEntry? Parse(string header, Uri requestUri, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(header))
        {
            return null;
        }

        // RFC 6265 §5.2 step 1–2: Split on first ';' to get cookie-pair
        var firstSemi = header.IndexOf(';');
        var cookiePair = firstSemi >= 0 ? header[..firstSemi] : header;
        var attributesRaw = firstSemi >= 0 ? header[(firstSemi + 1)..] : string.Empty;

        var eqIdx = cookiePair.IndexOf('=');
        if (eqIdx < 0)
        {
            return null;
        }

        var name = cookiePair[..eqIdx].Trim();
        var value = cookiePair[(eqIdx + 1)..].Trim();

        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        // Parse attributes
        string? domainAttr = null;
        string? pathAttr = null;
        DateTimeOffset? expiresAt = null;
        int? maxAgeSecs = null;
        var secure = false;
        var httpOnly = false;
        var sameSite = SameSitePolicy.Unspecified;

        if (!string.IsNullOrEmpty(attributesRaw))
        {
            foreach (var part in attributesRaw.Split(';'))
            {
                var attr = part.Trim();
                if (string.IsNullOrEmpty(attr))
                {
                    continue;
                }

                var eqPos = attr.IndexOf('=');
                if (eqPos < 0)
                {
                    // Boolean attribute
                    if (attr.Equals("Secure", StringComparison.OrdinalIgnoreCase))
                    {
                        secure = true;
                    }
                    else if (attr.Equals("HttpOnly", StringComparison.OrdinalIgnoreCase))
                    {
                        httpOnly = true;
                    }
                }
                else
                {
                    var attrName = attr[..eqPos].Trim();
                    var attrValue = attr[(eqPos + 1)..].Trim();

                    if (attrName.Equals("Domain", StringComparison.OrdinalIgnoreCase))
                    {
                        // RFC 6265 §5.2.3: Strip leading dot, lowercase
                        domainAttr = attrValue.TrimStart('.').ToLowerInvariant();
                        if (string.IsNullOrEmpty(domainAttr))
                        {
                            domainAttr = null;
                        }
                    }
                    else if (attrName.Equals("Path", StringComparison.OrdinalIgnoreCase))
                    {
                        // RFC 6265 §5.2.4: Path attribute value
                        pathAttr = string.IsNullOrEmpty(attrValue) ? null : attrValue;
                    }
                    else if (attrName.Equals("Expires", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryParseExpires(attrValue, out var expires))
                        {
                            expiresAt = expires;
                        }
                    }
                    else if (attrName.Equals("Max-Age", StringComparison.OrdinalIgnoreCase))
                    {
                        // RFC 6265 §5.2.2: Max-Age is an integer (seconds)
                        if (int.TryParse(attrValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxAge))
                        {
                            maxAgeSecs = maxAge;
                        }
                    }
                    else if (attrName.Equals("SameSite", StringComparison.OrdinalIgnoreCase))
                    {
                        sameSite = attrValue.ToLowerInvariant() switch
                        {
                            "strict" => SameSitePolicy.Strict,
                            "lax" => SameSitePolicy.Lax,
                            "none" => SameSitePolicy.None,
                            _ => SameSitePolicy.Unspecified,
                        };
                    }
                }
            }
        }

        // RFC 6265 §5.3 step 3: Max-Age takes precedence over Expires
        if (maxAgeSecs.HasValue)
        {
            expiresAt = maxAgeSecs.Value <= 0
                ? now.AddSeconds(-1) // Max-Age=0 → expired immediately (delete)
                : now.AddSeconds(maxAgeSecs.Value);
        }

        // RFC 6265 §5.3 step 5–8: Determine domain and host-only flag
        var requestHost = requestUri.Host.ToLowerInvariant();
        bool isHostOnly;
        string domain;

        if (string.IsNullOrEmpty(domainAttr))
        {
            // No Domain attribute → host-only cookie
            isHostOnly = true;
            domain = requestHost;
        }
        else
        {
            // Domain attribute present → domain cookie
            // RFC 6265 §5.3 step 6: Reject if domain does not domain-match the request host
            if (!requestHost.Equals(domainAttr, StringComparison.OrdinalIgnoreCase) &&
                !requestHost.EndsWith("." + domainAttr, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            isHostOnly = false;
            domain = domainAttr;
        }

        // RFC 6265 §5.2.4: Determine path
        var path = !string.IsNullOrEmpty(pathAttr) && pathAttr.StartsWith('/')
            ? pathAttr
            : GetDefaultPath(requestUri);

        return new CookieEntry(
            Name: name,
            Value: value,
            Domain: domain,
            Path: path,
            ExpiresAt: expiresAt,
            Secure: secure,
            HttpOnly: httpOnly,
            SameSite: sameSite,
            IsHostOnly: isHostOnly,
            CreatedAt: now);
    }

    // ── RFC 6265 §5.1.4: Default path computation ─────────────────────────────

    private static string GetDefaultPath(Uri requestUri)
    {
        var uriPath = requestUri.AbsolutePath;
        if (string.IsNullOrEmpty(uriPath) || uriPath[0] != '/')
        {
            return "/";
        }

        var lastSlash = uriPath.LastIndexOf('/');
        if (lastSlash == 0)
        {
            return "/";
        }

        return uriPath[..lastSlash];
    }

    // ── RFC 6265 §5.1.1: Lenient date parsing ─────────────────────────────────

    private static readonly string[] ExpiresFormats =
    [
        "ddd, dd MMM yyyy HH:mm:ss 'GMT'",
        "ddd, dd-MMM-yyyy HH:mm:ss 'GMT'",
        "ddd, dd MMM yyyy HH:mm:ss zzz",
        "ddd, dd-MMM-yy HH:mm:ss 'GMT'",
        "ddd, dd MMM yy HH:mm:ss 'GMT'",
        "ddd, dd MMM yyyy HH:mm:ss",
        "dddd, dd-MMM-yy HH:mm:ss 'GMT'",
    ];

    private static bool TryParseExpires(string value, out DateTimeOffset result)
    {
        if (DateTimeOffset.TryParseExact(
                value,
                ExpiresFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces,
                out result))
        {
            return true;
        }

        // Fallback: generic parse
        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out result))
        {
            return true;
        }

        result = default;
        return false;
    }
}