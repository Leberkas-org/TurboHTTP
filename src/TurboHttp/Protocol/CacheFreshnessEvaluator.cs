using System;
using System.Net.Http;

namespace TurboHttp.Protocol;

/// <summary>
/// RFC 9111 §4.2 — Evaluates whether a cached response is still fresh.
/// All logic is stateless (pure functions).
/// </summary>
public static class CacheFreshnessEvaluator
{
    /// <summary>
    /// RFC 9111 §4.2.1 — Computes the freshness lifetime of the cached entry.
    /// Priority: s-maxage (shared cache) > max-age > Expires > heuristic.
    /// Returns TimeSpan.Zero when no freshness information is available.
    /// </summary>
    public static TimeSpan GetFreshnessLifetime(CacheEntry entry, CachePolicy? policy = null)
    {
        policy ??= CachePolicy.Default;
        var cc = entry.CacheControl;

        // s-maxage applies only to shared caches (RFC 9111 §5.2.2.10)
        if (policy.SharedCache && cc?.SMaxAge.HasValue == true)
        {
            return cc.SMaxAge.Value;
        }

        // max-age (RFC 9111 §5.2.2.1)
        if (cc?.MaxAge.HasValue == true)
        {
            return cc.MaxAge.Value;
        }

        // Expires header (RFC 9111 §5.3)
        if (entry is { Expires: not null, Date: not null })
        {
            var lifetime = entry.Expires.Value - entry.Date.Value;
            return lifetime > TimeSpan.Zero ? lifetime : TimeSpan.Zero;
        }

        // Heuristic freshness (RFC 9111 §4.2.2) — 10 % of (Date – Last-Modified), capped at 1 day
        if (entry is not { LastModified: not null, Date: not null })
        {
            return TimeSpan.Zero;
        }

        var age = entry.Date.Value - entry.LastModified.Value;
        if (age <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var heuristic = TimeSpan.FromSeconds(age.TotalSeconds * 0.1);
        var cap = TimeSpan.FromDays(1);
        return heuristic < cap ? heuristic : cap;
    }

    /// <summary>
    /// RFC 9111 §4.2.3 — Computes the current age of the cached entry.
    /// current_age = corrected_age_value + resident_time
    /// where corrected_age_value = max(apparent_age, age_value)
    ///   and apparent_age = max(0, response_time – date_value)
    ///   and response_delay = response_time – request_time
    ///   and resident_time = now – response_time
    /// </summary>
    public static TimeSpan GetCurrentAge(CacheEntry entry, DateTimeOffset now)
    {
        // Apparent age (RFC 9111 §4.2.3 step 1)
        var apparentAge = TimeSpan.Zero;
        if (entry.Date.HasValue)
        {
            var diff = entry.ResponseTime - entry.Date.Value;
            if (diff > TimeSpan.Zero)
            {
                apparentAge = diff;
            }
        }

        // Age header value in seconds (RFC 9111 §4.2.3 step 2)
        var ageValue = entry.AgeSeconds.HasValue
            ? TimeSpan.FromSeconds(entry.AgeSeconds.Value)
            : TimeSpan.Zero;

        // Response delay = response_time − request_time
        var responseDelay = entry.ResponseTime - entry.RequestTime;
        if (responseDelay < TimeSpan.Zero)
        {
            responseDelay = TimeSpan.Zero;
        }

        // Corrected age value = max(apparent_age, age_value + response_delay)
        var correctedAge = apparentAge > ageValue + responseDelay
            ? apparentAge
            : ageValue + responseDelay;

        // Resident time = now − response_time
        var residentTime = now - entry.ResponseTime;
        if (residentTime < TimeSpan.Zero)
        {
            residentTime = TimeSpan.Zero;
        }

        return correctedAge + residentTime;
    }

    /// <summary>
    /// RFC 9111 §4.2 — Returns true if the cached entry is still fresh at <paramref name="now"/>.
    /// IsFresh = freshness_lifetime > current_age.
    /// </summary>
    public static bool IsFresh(CacheEntry entry, DateTimeOffset now, CachePolicy? policy = null)
    {
        var freshnessLifetime = GetFreshnessLifetime(entry, policy);
        if (freshnessLifetime == TimeSpan.Zero)
        {
            return false;
        }

        var currentAge = GetCurrentAge(entry, now);
        return freshnessLifetime > currentAge;
    }

    /// <summary>
    /// RFC 9111 §4 — Full evaluation of the cache entry against the incoming request.
    /// Applies request directives (no-cache, max-age, min-fresh, max-stale, only-if-cached)
    /// and response directives (must-revalidate) to determine the lookup outcome.
    /// </summary>
    public static CacheLookupResult Evaluate(
        CacheEntry? entry,
        HttpRequestMessage request,
        DateTimeOffset now,
        CachePolicy? policy = null)
    {
        if (entry is null)
        {
            return CacheLookupResult.Miss("No cached entry found.");
        }

        // Parse request Cache-Control
        var reqCc = request.Headers.TryGetValues("Cache-Control", out var ccValues)
            ? CacheControlParser.Parse(string.Join(", ", ccValues))
            : null;

        // RFC 9111 §5.2.1.4 — no-cache forces revalidation
        if (reqCc?.NoCache == true)
        {
            return CacheLookupResult.MustRevalidate(entry,
                "RFC 9111 §5.2.1.4: Request no-cache forces revalidation.");
        }

        var freshnessLifetime = GetFreshnessLifetime(entry, policy);
        var currentAge = GetCurrentAge(entry, now);
        var isFresh = freshnessLifetime > currentAge;

        // RFC 9111 §5.2.1.3 — min-fresh: entry must have at least min-fresh seconds of freshness remaining
        if (reqCc?.MinFresh.HasValue == true)
        {
            var freshnessRemaining = freshnessLifetime - currentAge;
            if (freshnessRemaining < reqCc.MinFresh.Value)
            {
                // Entry does not have enough freshness remaining — treat as stale
                isFresh = false;
            }
        }

        if (isFresh)
        {
            return CacheLookupResult.Fresh(entry,
                $"RFC 9111 §4.2: Entry is fresh (lifetime={freshnessLifetime.TotalSeconds:F0}s, age={currentAge.TotalSeconds:F0}s).");
        }

        // Entry is stale — check if must-revalidate applies
        var resCc = entry.CacheControl;
        if (resCc?.MustRevalidate == true || (policy?.SharedCache == true && resCc?.ProxyRevalidate == true))
        {
            return CacheLookupResult.MustRevalidate(entry,
                "RFC 9111 §5.2.2.8: must-revalidate — stale entry cannot be served without revalidation.");
        }

        // RFC 9111 §5.2.1.2 — max-stale: accept stale entry if within staleness tolerance
        if (reqCc?.MaxStale.HasValue == true)
        {
            var staleness = currentAge - freshnessLifetime;
            if (reqCc.MaxStale.Value == TimeSpan.MaxValue || staleness <= reqCc.MaxStale.Value)
            {
                return CacheLookupResult.Stale(entry,
                    $"RFC 9111 §5.2.1.2: Request max-stale accepts stale entry (staleness={staleness.TotalSeconds:F0}s).");
            }
        }

        return CacheLookupResult.MustRevalidate(entry,
            $"RFC 9111 §4.2: Entry is stale (lifetime={freshnessLifetime.TotalSeconds:F0}s, age={currentAge.TotalSeconds:F0}s).");
    }
}