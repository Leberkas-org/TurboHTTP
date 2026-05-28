namespace TurboHTTP.Features.Caching;

internal enum CacheLookupStatus
{
    Miss,
    Fresh,
    Stale,
    MustRevalidate
}

internal sealed record CacheLookupResult
{
    public CacheLookupStatus Status { get; private init; }
    public ICacheEntry? Entry { get; private init; }
    public string Reason { get; init; } = "";

    public static CacheLookupResult Miss(string reason)
        => new() { Status = CacheLookupStatus.Miss, Reason = reason };

    public static CacheLookupResult Fresh(ICacheEntry entry, string reason)
        => new() { Status = CacheLookupStatus.Fresh, Entry = entry, Reason = reason };

    public static CacheLookupResult Stale(ICacheEntry entry, string reason)
        => new() { Status = CacheLookupStatus.Stale, Entry = entry, Reason = reason };

    public static CacheLookupResult MustRevalidate(ICacheEntry entry, string reason)
        => new() { Status = CacheLookupStatus.MustRevalidate, Entry = entry, Reason = reason };
}