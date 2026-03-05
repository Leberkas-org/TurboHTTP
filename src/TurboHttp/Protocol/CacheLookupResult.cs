namespace TurboHttp.Protocol;

/// <summary>
/// RFC 9111 §4 — Outcome of a cache lookup.
/// </summary>
public enum CacheLookupStatus
{
    /// <summary>No matching entry was found in the store.</summary>
    Miss,

    /// <summary>A matching entry was found and it is still fresh.</summary>
    Fresh,

    /// <summary>A matching entry was found but it is stale; may be used subject to policy.</summary>
    Stale,

    /// <summary>A matching entry was found but must be revalidated with the origin before use.</summary>
    MustRevalidate
}

/// <summary>
/// RFC 9111 §4 — Result of looking up a request in the cache.
/// </summary>
public sealed record CacheLookupResult
{
    /// <summary>The lookup status.</summary>
    public CacheLookupStatus Status { get; init; }

    /// <summary>The cached entry when Status is Fresh, Stale, or MustRevalidate; null for Miss.</summary>
    public CacheEntry? Entry { get; init; }

    /// <summary>Human-readable explanation of the outcome.</summary>
    public string Reason { get; init; } = "";

    /// <summary>Creates a Miss result with the given reason.</summary>
    public static CacheLookupResult Miss(string reason)
        => new() { Status = CacheLookupStatus.Miss, Reason = reason };

    /// <summary>Creates a Fresh result with the given entry and reason.</summary>
    public static CacheLookupResult Fresh(CacheEntry entry, string reason)
        => new() { Status = CacheLookupStatus.Fresh, Entry = entry, Reason = reason };

    /// <summary>Creates a Stale result with the given entry and reason.</summary>
    public static CacheLookupResult Stale(CacheEntry entry, string reason)
        => new() { Status = CacheLookupStatus.Stale, Entry = entry, Reason = reason };

    /// <summary>Creates a MustRevalidate result with the given entry and reason.</summary>
    public static CacheLookupResult MustRevalidate(CacheEntry entry, string reason)
        => new() { Status = CacheLookupStatus.MustRevalidate, Entry = entry, Reason = reason };
}
