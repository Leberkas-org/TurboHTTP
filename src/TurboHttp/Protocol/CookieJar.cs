using System;
using System.Collections.Generic;
using System.Net.Http;

namespace TurboHttp.Protocol;

/// <summary>
/// RFC 6265 §5.4 — Cookie ordering preference.
/// </summary>
public enum SameSitePolicy
{
    /// <summary>No SameSite attribute present.</summary>
    Unspecified,

    /// <summary>Cookie sent only for same-site requests.</summary>
    Strict,

    /// <summary>Cookie sent for same-site and top-level cross-site navigations.</summary>
    Lax,

    /// <summary>Cookie sent for all requests (requires Secure).</summary>
    None,
}

/// <summary>
/// RFC 6265 — Cookie storage entry.
/// </summary>
internal sealed record CookieEntry(
    string Name,
    string Value,
    string Domain,
    string Path,
    DateTimeOffset? ExpiresAt,
    bool Secure,
    bool HttpOnly,
    SameSitePolicy SameSite,
    bool IsHostOnly,
    DateTimeOffset CreatedAt);

/// <summary>
/// RFC 6265 — Cookie jar for storing and matching HTTP cookies.
///
/// Implements:
/// - Domain matching per RFC 6265 §5.1.3 (no naive EndsWith — uses proper label-boundary check)
/// - Path matching per RFC 6265 §5.1.4
/// - Host-only vs domain cookies
/// - Expires and Max-Age (Max-Age takes precedence per §5.2.2)
/// - Secure attribute: only send over HTTPS
/// - HttpOnly attribute: marks server-only cookies
/// - SameSite attribute (stored; enforcement is caller responsibility)
/// - Correct cookie replacement semantics (name+domain+path uniqueness)
/// </summary>
public sealed class CookieJar
{
    private readonly List<CookieEntry> _cookies = new();

    /// <summary>
    /// Processes all Set-Cookie headers in <paramref name="response"/>, updating the cookie jar.
    /// Existing cookies with the same name, domain, and path are replaced.
    /// Cookies with Max-Age=0 or past expiry are removed.
    /// </summary>
    public void ProcessResponse(Uri requestUri, HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        ArgumentNullException.ThrowIfNull(response);

        var now = DateTimeOffset.UtcNow;

        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookieValues))
        {
            return;
        }

        foreach (var header in setCookieValues)
        {
            var entry = CookieParser.Parse(header, requestUri, now);
            if (entry is null)
            {
                continue;
            }

            // RFC 6265 §5.3 step 11: Remove existing cookie with same name+domain+path
            _cookies.RemoveAll(c =>
                string.Equals(c.Name, entry.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Domain, entry.Domain, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Path, entry.Path, StringComparison.Ordinal));

            // If cookie is already expired (Max-Age=0 or past Expires), do not add
            if (!IsExpired(entry, now))
            {
                _cookies.Add(entry);
            }
        }
    }

    /// <summary>
    /// Adds applicable cookies from the jar to the request's Cookie header.
    /// Applies domain matching, path matching, Secure, and expiry rules.
    /// </summary>
    public void AddCookiesToRequest(Uri requestUri, ref HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTimeOffset.UtcNow;
        var requestHost = requestUri.Host.ToLowerInvariant();
        var requestPath = string.IsNullOrEmpty(requestUri.AbsolutePath) ? "/" : requestUri.AbsolutePath;
        var isHttps = requestUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);

        var applicable = new List<CookieEntry>();

        foreach (var cookie in _cookies)
        {
            if (IsExpired(cookie, now))
            {
                continue;
            }

            // RFC 6265 §5.4 step 1: Secure attribute — only send over HTTPS
            if (cookie.Secure && !isHttps)
            {
                continue;
            }

            // RFC 6265 §5.4 step 2: Domain matching
            if (!DomainMatches(cookie.Domain, cookie.IsHostOnly, requestHost))
            {
                continue;
            }

            // RFC 6265 §5.4 step 3: Path matching
            if (!PathMatches(cookie.Path, requestPath))
            {
                continue;
            }

            applicable.Add(cookie);
        }

        if (applicable.Count == 0)
        {
            return;
        }

        // RFC 6265 §5.4 step 2: Sort by path length (longer first), then by creation time (older first)
        applicable.Sort((a, b) =>
        {
            var pathLenCmp = b.Path.Length.CompareTo(a.Path.Length);
            if (pathLenCmp != 0)
            {
                return pathLenCmp;
            }

            return a.CreatedAt.CompareTo(b.CreatedAt);
        });

        var parts = new string[applicable.Count];
        for (var i = 0; i < applicable.Count; i++)
        {
            parts[i] = $"{applicable[i].Name}={applicable[i].Value}";
        }

        request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", parts));
    }

    /// <summary>Gets the number of cookies currently stored (including potentially expired ones not yet evicted).</summary>
    public int Count => _cookies.Count;

    /// <summary>Removes all cookies from the jar.</summary>
    public void Clear() => _cookies.Clear();

    // ── RFC 6265 §5.1.3 Domain Matching ──────────────────────────────────────

    /// <summary>
    /// Returns true if <paramref name="requestHost"/> domain-matches the cookie's domain.
    ///
    /// RFC 6265 §5.1.3:
    /// - Host-only cookies: exact match only.
    /// - Domain cookies: exact match OR subdomain match, provided the request host is
    ///   not an IP address and the boundary is a full label ("." prefix check).
    /// </summary>
    internal static bool DomainMatches(string cookieDomain, bool isHostOnly, string requestHost)
    {
        if (isHostOnly)
        {
            // Host-only: exact case-insensitive match
            return string.Equals(cookieDomain, requestHost, StringComparison.OrdinalIgnoreCase);
        }

        // Domain cookie: exact match
        if (string.Equals(cookieDomain, requestHost, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Domain cookie: subdomain match — requestHost must end with ".cookieDomain"
        // This ensures label boundary (prevents "notexample.com" matching ".example.com").
        // IP addresses cannot be subdomains.
        if (IsIpAddress(requestHost))
        {
            return false;
        }

        return requestHost.EndsWith("." + cookieDomain, StringComparison.OrdinalIgnoreCase);
    }

    // ── RFC 6265 §5.1.4 Path Matching ────────────────────────────────────────

    /// <summary>
    /// Returns true if <paramref name="requestPath"/> path-matches the cookie's path.
    ///
    /// RFC 6265 §5.1.4:
    /// - cookiePath == requestPath: true
    /// - requestPath starts with cookiePath AND (cookiePath ends with '/' OR next char is '/')
    /// </summary>
    internal static bool PathMatches(string cookiePath, string requestPath)
    {
        if (string.Equals(cookiePath, requestPath, StringComparison.Ordinal))
        {
            return true;
        }

        if (!requestPath.StartsWith(cookiePath, StringComparison.Ordinal))
        {
            return false;
        }

        // Ensure label boundary
        if (cookiePath.EndsWith('/'))
        {
            return true;
        }

        if (requestPath.Length > cookiePath.Length && requestPath[cookiePath.Length] == '/')
        {
            return true;
        }

        return false;
    }

    // ── Private Helpers ───────────────────────────────────────────────────────

    private static bool IsExpired(CookieEntry cookie, DateTimeOffset now)
    {
        return cookie.ExpiresAt.HasValue && cookie.ExpiresAt.Value <= now;
    }

    private static bool IsIpAddress(string host)
    {
        return System.Net.IPAddress.TryParse(host, out _);
    }
}