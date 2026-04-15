using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace TurboHTTP.Protocol.AltSvc;

/// <summary>
/// Thread-safe per-host cache of Alt-Svc directives with TTL-based expiration.
/// Used to discover HTTP/3 availability and upgrade connections automatically.
/// </summary>
internal sealed class AltSvcCache
{
    private readonly ConcurrentDictionary<string, List<AltSvcEntry>> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Stores parsed Alt-Svc entries for a given origin host.
    /// Replaces any existing entries for that host.
    /// </summary>
    /// <param name="host">The origin host (e.g., "example.com").</param>
    /// <param name="entries">The parsed Alt-Svc entries to cache.</param>
    public void Store(string host, List<AltSvcEntry> entries)
    {
        if (string.IsNullOrEmpty(host) || entries.Count == 0)
        {
            return;
        }

        _cache[host] = entries;
    }

    /// <summary>
    /// Clears all cached entries for a given origin host.
    /// Called when an Alt-Svc: clear header is received.
    /// </summary>
    /// <param name="host">The origin host to clear.</param>
    public void Clear(string host)
    {
        _cache.TryRemove(host, out _);
    }

    /// <summary>
    /// Clears all cached entries for all hosts.
    /// </summary>
    public void ClearAll()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Attempts to find a valid (non-expired) HTTP/3 Alt-Svc entry for the given host.
    /// Returns true if an HTTP/3 alternative is available and not expired.
    /// </summary>
    /// <param name="host">The origin host to look up.</param>
    /// <param name="entry">The matching Alt-Svc entry if found.</param>
    /// <param name="now">Current time for expiration check. If null, uses DateTimeOffset.UtcNow.</param>
    /// <returns>True if a valid HTTP/3 entry was found.</returns>
    public bool TryGetHttp3(string host, [NotNullWhen(true)] out AltSvcEntry? entry, DateTimeOffset? now = null)
    {
        entry = null;

        if (!_cache.TryGetValue(host, out var entries))
        {
            return false;
        }

        var currentTime = now ?? DateTimeOffset.UtcNow;

        foreach (var e in entries)
        {
            if (e.IsHttp3 && e.IsValid(currentTime))
            {
                entry = e;
                return true;
            }
        }

        // All entries expired — evict atomically via TryRemove with value comparison.
        // Only removes if the value reference is still the same list we checked,
        // preventing deletion of a fresh list concurrently added by Store().
        var allExpired = true;
        foreach (var e in entries)
        {
            if (e.IsValid(currentTime))
            {
                allExpired = false;
                break;
            }
        }

        if (allExpired)
        {
            ((ICollection<KeyValuePair<string, List<AltSvcEntry>>>)_cache)
                .Remove(new KeyValuePair<string, List<AltSvcEntry>>(host, entries));
        }

        return false;
    }

    /// <summary>
    /// Returns the number of hosts with cached entries. Useful for testing.
    /// </summary>
    internal int Count => _cache.Count;
}
