using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace TurboHttp.Protocol;

/// <summary>
/// RFC 9111 §3 — Thread-safe in-memory LRU cache store for HTTP responses.
/// </summary>
public sealed class HttpCacheStore
{
    private readonly CachePolicy _policy;
    private readonly object _lock = new();

    // Linked list tracks LRU order; dictionary provides O(1) access
    private readonly LinkedList<(string key, CacheEntry entry)> _lruList = new();
    private readonly Dictionary<string, LinkedListNode<(string key, CacheEntry entry)>> _index = new();

    public HttpCacheStore(CachePolicy? policy = null)
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
            // Walk all nodes with the same primary key; pick the one whose Vary matches
            var node = _lruList.First;
            LinkedListNode<(string key, CacheEntry entry)>? match = null;

            while (node != null)
            {
                if (node.Value.key == primaryKey)
                {
                    if (VaryMatches(node.Value.entry, request))
                    {
                        match = node;
                        break;
                    }
                }

                node = node.Next;
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
            }

            var node = _lruList.AddFirst((primaryKey, entry));
            _index[primaryKey + "|" + GetVaryKey(entry)] = node;
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
            var toRemove = _lruList
                .Where(n => n.key == key)
                .ToList();

            foreach (var item in toRemove)
            {
                var node = _lruList.Find(item);
                if (node != null)
                {
                    _lruList.Remove(node);
                    _index.Remove(item.key + "|" + GetVaryKey(item.entry));
                }
            }
        }
    }

    /// <summary>
    /// RFC 9111 §3.1 — Returns true if the response status code is cacheable by default.
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

        // no-store on request (RFC 9111 §5.2.1.5)
        if (request.Headers.TryGetValues("Cache-Control", out var reqCcValues))
        {
            var reqCc = CacheControlParser.Parse(string.Join(", ", reqCcValues));
            if (reqCc?.NoStore == true)
            {
                return false;
            }
        }

        // no-store on response (RFC 9111 §5.2.2.5)
        if (response.Headers.TryGetValues("Cache-Control", out var resCcValues))
        {
            var resCc = CacheControlParser.Parse(string.Join(", ", resCcValues));
            if (resCc?.NoStore == true)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Clears all entries from the store.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _lruList.Clear();
            _index.Clear();
        }
    }

    // ── Private Helpers ──────────────────────────────────────────────────────

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
                foreach (var part in v.Split(','))
                {
                    varyNames.Add(part.Trim());
                }
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

            string? cachedValue = entry.VaryRequestValues.TryGetValue(name, out var cv) ? cv : null;
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
        var node = _lruList.First;
        while (node != null)
        {
            var next = node.Next;
            if (node.Value.key == primaryKey)
            {
                // Check if vary values match (same variant)
                var entryVary = node.Value.entry.VaryRequestValues;
                var same = true;

                foreach (var kvp in varyValues)
                {
                    var entryVal = entryVary.TryGetValue(kvp.Key, out var ev) ? ev : null;
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
                }
            }

            node = next;
        }
    }

    private static string GetPrimaryKey(HttpRequestMessage request)
        => NormalizeUri(request.RequestUri!);

    private static string NormalizeUri(Uri uri)
        => uri.GetLeftPart(UriPartial.Query).ToLowerInvariant();

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
