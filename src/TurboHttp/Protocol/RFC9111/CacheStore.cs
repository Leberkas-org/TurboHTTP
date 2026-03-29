using System.Net;

namespace TurboHttp.Protocol.RFC9111;

/// <summary>
/// RFC 9111 §3 — Thread-safe in-memory LRU cache store for HTTP responses.
/// </summary>
public sealed class CacheStore
{
    private readonly CachePolicy _policy;
    private readonly Lock _lock = new();

    // Linked list tracks LRU order; _index provides O(1) node lookup by compound key
    private readonly LinkedList<(string key, CacheEntry entry)> _lruList = [];
    private readonly Dictionary<string, LinkedListNode<(string key, CacheEntry entry)>> _index = new();

    // Secondary index: primary key → list of nodes; enables O(1) candidate lookup in Get/Invalidate
    private readonly Dictionary<string, List<LinkedListNode<(string key, CacheEntry entry)>>> _primaryIndex = new();

    public CacheStore(CachePolicy? policy = null)
    {
        _policy = policy ?? CachePolicy.Default;
    }

    /// <summary>Number of entries currently in the store.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _lruList.Count;
            }
        }
    }

    /// <summary>
    /// RFC 9111 §4 — Looks up a matching entry for the request.
    /// Returns null on a cache miss. Respects the Vary header for variant selection.
    /// </summary>
    public CacheEntry? Get(HttpRequestMessage request)
    {
        var primaryKey = GetPrimaryKey(request);

        lock (_lock)
        {
            // O(1) lookup of all variants for this URI via secondary index
            if (!_primaryIndex.TryGetValue(primaryKey, out var candidates))
            {
                return null;
            }

            LinkedListNode<(string key, CacheEntry entry)>? match = null;

            foreach (var candidate in candidates)
            {
                if (VaryMatches(candidate.Value.entry, request))
                {
                    match = candidate;
                    break;
                }
            }

            if (match is null)
            {
                return null;
            }

            // Move to front (most recently used)
            _lruList.Remove(match);
            _lruList.AddFirst(match);

            return match.Value.entry;
        }
    }

    /// <summary>
    /// RFC 9111 §3 — Stores a cacheable response. Respects MaxEntries (LRU eviction)
    /// and MaxBodyBytes. Does nothing if the response should not be stored.
    /// </summary>
    public void Put(
        HttpRequestMessage request,
        HttpResponseMessage response,
        byte[] body,
        DateTimeOffset requestTime,
        DateTimeOffset responseTime)
    {
        if (!ShouldStore(request, response))
        {
            return;
        }

        // RFC 9111 §5.2.2.7 — shared cache must not store unqualified-private responses
        if (_policy.SharedCache)
        {
            if (response.Headers.TryGetValues("Cache-Control", out var ccVals))
            {
                var cc = CacheControlParser.Parse(string.Join(", ", ccVals));
                if (cc is { Private: true, PrivateFields: null })
                {
                    return; // Unqualified private — reject entirely
                }
            }

            StripPrivateFields(response);
        }

        // RFC 9111 §3.1 — connection-specific headers must not be stored in cache
        StripConnectionHeaders(response);

        if (body.Length > _policy.MaxBodyBytes)
        {
            return;
        }

        var entry = BuildEntry(response, body, requestTime, responseTime, request);
        var primaryKey = GetPrimaryKey(request);

        lock (_lock)
        {
            // Remove any existing entry for this primary key + vary combination
            RemoveMatching(primaryKey, entry.VaryRequestValues);

            // LRU eviction
            while (_lruList.Count >= _policy.MaxEntries)
            {
                var last = _lruList.Last!;
                _lruList.RemoveLast();
                _index.Remove(last.Value.key + "|" + GetVaryKey(last.Value.entry));
                RemoveFromPrimaryIndex(last.Value.key, last);
            }

            var node = _lruList.AddFirst((primaryKey, entry));
            _index[primaryKey + "|" + GetVaryKey(entry)] = node;

            if (!_primaryIndex.TryGetValue(primaryKey, out var list))
            {
                list = [];
                _primaryIndex[primaryKey] = list;
            }

            list.Add(node);
        }
    }

    /// <summary>
    /// RFC 9111 §4.4 — Invalidates all stored entries whose URI matches the given URI.
    /// Called after unsafe methods (POST, PUT, DELETE, PATCH) that may have modified the resource.
    /// </summary>
    public void Invalidate(Uri uri)
    {
        var key = NormalizeUri(uri);

        lock (_lock)
        {
            if (!_primaryIndex.TryGetValue(key, out var candidates))
            {
                return;
            }

            // Take a copy since we will mutate _primaryIndex inside the loop
            foreach (var node in candidates.ToList())
            {
                _lruList.Remove(node);
                _index.Remove(node.Value.key + "|" + GetVaryKey(node.Value.entry));
            }

            _primaryIndex.Remove(key);
        }
    }

    /// <summary>
    /// RFC 9111 §3 — Returns true if the response status code is cacheable by default.
    /// Cacheable status codes: 200, 203, 204, 206, 300, 301, 308, 404, 405, 410, 414, 501.
    /// </summary>
    public static bool IsCacheable(HttpResponseMessage response)
    {
        return (int)response.StatusCode switch
        {
            200 => true,
            203 => true,
            204 => true,
            206 => true,
            300 => true,
            301 => true,
            308 => true,
            404 => true,
            405 => true,
            410 => true,
            414 => true,
            501 => true,
            _ => false
        };
    }

    /// <summary>
    /// RFC 9111 §3 — Returns true if this request/response pair should be stored in the cache.
    /// Checks: safe method, cacheable status, no-store directives, and cache authorization.
    /// </summary>
    public static bool ShouldStore(HttpRequestMessage request, HttpResponseMessage response)
    {
        // Only safe methods produce cacheable responses (RFC 9111 §3)
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
        {
            return false;
        }

        if (!IsCacheable(response))
        {
            return false;
        }

        // RFC 9111 §3.1 — incomplete responses (206 Partial Content) must not be
        // stored unless the cache supports combining partial content, which we do not.
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            return false;
        }

        // A response carrying Content-Range indicates a partial payload even on a 200;
        // caching it would serve incomplete content on subsequent requests.
        if (response.Content?.Headers?.ContentRange is not null)
        {
            return false;
        }

        // no-store on request (RFC 9111 §5.2.1.5)
        if (request.Headers.TryGetValues("Cache-Control", out var reqCcValues))
        {
            var reqCc = CacheControlParser.Parse(string.Join(", ", reqCcValues));
            if (reqCc?.NoStore == true)
            {
                return false;
            }
        }

        // no-store on response (RFC 9111 §5.2.2.4)
        if (response.Headers.TryGetValues("Cache-Control", out var resCcValues))
        {
            var resCc = CacheControlParser.Parse(string.Join(", ", resCcValues));
            if (resCc?.NoStore == true)
            {
                return false;
            }

            // RFC 9111 §5.2.2.3 — must-understand: only store if the cache understands
            // the status code's caching requirements (i.e., it is a cacheable-by-default code)
            if (resCc?.MustUnderstand == true && !IsUnderstoodStatusCode(response))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// RFC 9111 §5.2.2.3 — Returns true if the cache understands the caching requirements
    /// for this response's status code. Used with the must-understand directive.
    /// Understood codes are the cacheable-by-default status codes from RFC 9111 §3.
    /// </summary>
    private static bool IsUnderstoodStatusCode(HttpResponseMessage response)
        => IsCacheable(response);

    /// <summary>Clears all entries from the store.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _lruList.Clear();
            _index.Clear();
            _primaryIndex.Clear();
        }
    }

    private static CacheEntry BuildEntry(
        HttpResponseMessage response,
        byte[] body,
        DateTimeOffset requestTime,
        DateTimeOffset responseTime,
        HttpRequestMessage request)
    {
        CacheControl? cc = null;
        if (response.Headers.TryGetValues("Cache-Control", out var ccValues))
        {
            cc = CacheControlParser.Parse(string.Join(", ", ccValues));
        }

        string? etag = null;
        if (response.Headers.ETag is not null)
        {
            etag = response.Headers.ETag.ToString();
        }

        DateTimeOffset? lastModified = null;
        if (response.Content.Headers.LastModified.HasValue)
        {
            lastModified = response.Content.Headers.LastModified;
        }

        DateTimeOffset? expires = null;
        if (response.Content.Headers.Expires.HasValue)
        {
            expires = response.Content.Headers.Expires;
        }

        DateTimeOffset? date = null;
        if (response.Headers.Date.HasValue)
        {
            date = response.Headers.Date;
        }

        int? ageSeconds = null;
        if (response.Headers.Age.HasValue)
        {
            ageSeconds = (int)response.Headers.Age.Value.TotalSeconds;
        }

        // Parse Vary header
        var varyNames = new List<string>();
        if (response.Headers.TryGetValues("Vary", out var varyValues))
        {
            foreach (var v in varyValues)
            {
                varyNames.AddRange(v.Split(',').Select(part => part.Trim()));
            }
        }

        // Capture the corresponding request header values
        var varyRequestValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in varyNames)
        {
            string? reqValue = null;
            if (request.Headers.TryGetValues(name, out var reqHeaderValues))
            {
                reqValue = string.Join(", ", reqHeaderValues);
            }

            varyRequestValues[name] = reqValue;
        }

        return new CacheEntry
        {
            Response = response,
            Body = body,
            RequestTime = requestTime,
            ResponseTime = responseTime,
            ETag = etag,
            LastModified = lastModified,
            Expires = expires,
            Date = date,
            AgeSeconds = ageSeconds,
            CacheControl = cc,
            VaryHeaderNames = varyNames,
            VaryRequestValues = varyRequestValues
        };
    }

    /// <summary>
    /// Returns true if the cached entry's Vary fields match the incoming request.
    /// RFC 9111 §4.1 — Vary: * never matches.
    /// </summary>
    private static bool VaryMatches(CacheEntry entry, HttpRequestMessage request)
    {
        foreach (var name in entry.VaryHeaderNames)
        {
            // Vary: * — never matches any request
            if (name == "*")
            {
                return false;
            }

            var cachedValue = entry.VaryRequestValues.GetValueOrDefault(name);
            string? currentValue = null;

            if (request.Headers.TryGetValues(name, out var vals))
            {
                currentValue = string.Join(", ", vals);
            }

            if (!string.Equals(cachedValue, currentValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void RemoveMatching(string primaryKey, IReadOnlyDictionary<string, string?> varyValues)
    {
        if (!_primaryIndex.TryGetValue(primaryKey, out var candidates))
        {
            return;
        }

        for (var i = candidates.Count - 1; i >= 0; i--)
        {
            var node = candidates[i];
            var entryVary = node.Value.entry.VaryRequestValues;
            var same = true;

            foreach (var kvp in varyValues)
            {
                var entryVal = entryVary.GetValueOrDefault(kvp.Key);
                if (!string.Equals(entryVal, kvp.Value, StringComparison.Ordinal))
                {
                    same = false;
                    break;
                }
            }

            if (same)
            {
                _lruList.Remove(node);
                _index.Remove(primaryKey + "|" + GetVaryKey(node.Value.entry));
                candidates.RemoveAt(i);
            }
        }

        if (candidates.Count == 0)
        {
            _primaryIndex.Remove(primaryKey);
        }
    }

    private void RemoveFromPrimaryIndex(string primaryKey, LinkedListNode<(string key, CacheEntry entry)> node)
    {
        if (!_primaryIndex.TryGetValue(primaryKey, out var list))
        {
            return;
        }

        list.Remove(node);

        if (list.Count == 0)
        {
            _primaryIndex.Remove(primaryKey);
        }
    }

    private static string GetPrimaryKey(HttpRequestMessage request)
        => NormalizeUri(request.RequestUri!);

    private static string NormalizeUri(Uri uri)
        => uri.GetLeftPart(UriPartial.Query).ToLowerInvariant();

    /// <summary>
    /// RFC 9111 §5.2.2.7 — Strips header fields listed in private="field1, field2"
    /// from the response before storing in a shared cache.
    /// </summary>
    private static void StripPrivateFields(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Cache-Control", out var ccValues))
        {
            return;
        }

        var cc = CacheControlParser.Parse(string.Join(", ", ccValues));
        if (cc?.PrivateFields is not { Count: > 0 } fields)
        {
            return;
        }

        foreach (var field in fields)
        {
            response.Headers.Remove(field);
            response.Content?.Headers.Remove(field);
        }
    }

    /// <summary>
    /// RFC 9111 §3.1 — Removes connection-specific header fields from the response
    /// before storing in cache. These are hop-by-hop headers that have no meaning
    /// beyond the immediate connection.
    /// </summary>
    private static void StripConnectionHeaders(HttpResponseMessage response)
    {
        // Also strip any headers listed in the Connection header itself
        if (response.Headers.TryGetValues("Connection", out var connectionValues))
        {
            foreach (var value in connectionValues)
            {
                foreach (var field in value.Split(','))
                {
                    var trimmed = field.Trim();
                    if (trimmed.Length > 0)
                    {
                        response.Headers.Remove(trimmed);
                        response.Content?.Headers.Remove(trimmed);
                    }
                }
            }
        }

        response.Headers.Remove("Connection");
        response.Headers.Remove("Keep-Alive");
        response.Headers.Remove("Proxy-Authenticate");
        response.Headers.Remove("Proxy-Authorization");
        response.Headers.Remove("TE");
        response.Headers.Remove("Trailer");
        response.Headers.Remove("Transfer-Encoding");
        response.Headers.Remove("Upgrade");
    }

    private static string GetVaryKey(CacheEntry entry)
    {
        if (entry.VaryRequestValues.Count == 0)
        {
            return "";
        }

        var parts = entry.VaryRequestValues
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => $"{kvp.Key}={kvp.Value}");

        return string.Join("&", parts);
    }
}