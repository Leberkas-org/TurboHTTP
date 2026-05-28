using System.Net;
using TurboHTTP.Protocol;

namespace TurboHTTP.Features.Cookies;

internal sealed class CookieJar
{
    private readonly ICookieStore _store;

    private readonly List<CookieStoreEntry> _applicable = [];

    public CookieJar()
        : this(new MemoryCookieStore())
    {
    }

    public CookieJar(ICookieStore store)
    {
        _store = store;
    }

    public void ProcessResponse(Uri requestUri, HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        ArgumentNullException.ThrowIfNull(response);

        var now = DateTimeOffset.UtcNow;

        if (!response.Headers.TryGetValues(WellKnownHeaders.SetCookie, out var setCookieValues))
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

            _store.Remove(entry.Name, entry.Domain, entry.Path);

            if (!IsExpired(entry, now))
            {
                _store.Add(ToStoreEntry(entry));
            }
        }
    }

    public void AddCookiesToRequest(Uri requestUri, ref HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTimeOffset.UtcNow;
        var requestHost = requestUri.Host.ToLowerInvariant();
        var requestPath = string.IsNullOrEmpty(requestUri.AbsolutePath) ? "/" : requestUri.AbsolutePath;
        var isHttps = requestUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);

        _applicable.Clear();

        foreach (var cookie in _store.GetAll())
        {
            if (IsExpired(cookie, now))
            {
                continue;
            }

            if (cookie.Secure && !isHttps)
            {
                continue;
            }

            if (!DomainMatches(cookie.Domain, cookie.IsHostOnly, requestHost))
            {
                continue;
            }

            if (!PathMatches(cookie.Path, requestPath))
            {
                continue;
            }

            _applicable.Add(cookie);
        }

        if (_applicable.Count == 0)
        {
            return;
        }

        _applicable.Sort((a, b) =>
        {
            var pathLenCmp = b.Path.Length.CompareTo(a.Path.Length);
            if (pathLenCmp != 0)
            {
                return pathLenCmp;
            }

            return a.CreatedAt.CompareTo(b.CreatedAt);
        });

        var parts = new string[_applicable.Count];
        for (var i = 0; i < _applicable.Count; i++)
        {
            parts[i] = $"{_applicable[i].Name}={_applicable[i].Value}";
        }

        request.Headers.TryAddWithoutValidation(WellKnownHeaders.Cookie,
            string.Join(WellKnownHeaders.SemiColonSpace, parts));
    }

    public int Count => _store.Count;

    public void Clear() => _store.Clear();

    internal static bool DomainMatches(string cookieDomain, bool isHostOnly, string requestHost)
    {
        if (isHostOnly)
        {
            return string.Equals(cookieDomain, requestHost, StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(cookieDomain, requestHost, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsIpAddress(requestHost))
        {
            return false;
        }

        return requestHost.EndsWith("." + cookieDomain, StringComparison.OrdinalIgnoreCase);
    }

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

    private static bool IsExpired(CookieEntry cookie, DateTimeOffset now)
    {
        return cookie.ExpiresAt.HasValue && cookie.ExpiresAt.Value <= now;
    }

    private static bool IsExpired(CookieStoreEntry cookie, DateTimeOffset now)
    {
        return cookie.ExpiresAt.HasValue && cookie.ExpiresAt.Value <= now;
    }

    private static bool IsIpAddress(string host)
    {
        return IPAddress.TryParse(host, out _);
    }

    private static CookieStoreEntry ToStoreEntry(CookieEntry entry) => new(
        entry.Name,
        entry.Value,
        entry.Domain,
        entry.Path,
        entry.ExpiresAt,
        entry.Secure,
        entry.HttpOnly,
        entry.SameSite,
        entry.IsHostOnly,
        entry.CreatedAt);
}