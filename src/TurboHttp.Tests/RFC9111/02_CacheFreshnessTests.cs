using System.Net;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9111;

public sealed class CacheFreshnessTests
{
    private static readonly DateTimeOffset _baseTime = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // ── Helper ───────────────────────────────────────────────────────────────

    private static CacheEntry MakeEntry(
        int? maxAgeSeconds = null,
        int? sMaxAgeSeconds = null,
        DateTimeOffset? expires = null,
        DateTimeOffset? lastModified = null,
        int? ageHeaderSeconds = null,
        DateTimeOffset? date = null,
        DateTimeOffset? requestTime = null,
        DateTimeOffset? responseTime = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);

        CacheControl? cc = null;
        if (maxAgeSeconds.HasValue || sMaxAgeSeconds.HasValue)
        {
            cc = new CacheControl
            {
                MaxAge = maxAgeSeconds.HasValue ? TimeSpan.FromSeconds(maxAgeSeconds.Value) : null,
                SMaxAge = sMaxAgeSeconds.HasValue ? TimeSpan.FromSeconds(sMaxAgeSeconds.Value) : null
            };
        }

        var actualDate = date ?? _baseTime;
        return new CacheEntry
        {
            Response = response,
            Body = [],
            RequestTime = requestTime ?? actualDate.AddSeconds(-1),
            ResponseTime = responseTime ?? actualDate,
            Date = actualDate,
            Expires = expires,
            LastModified = lastModified,
            AgeSeconds = ageHeaderSeconds,
            CacheControl = cc
        };
    }

    // ── GetFreshnessLifetime ─────────────────────────────────────────────────

    [Fact(DisplayName = "RFC-9111-§4.2: max-age=60 → freshness lifetime = 60s")]
    public void MaxAge_FreshnessLifetime_60s()
    {
        var entry = MakeEntry(maxAgeSeconds: 60);
        var lifetime = CacheFreshnessEvaluator.GetFreshnessLifetime(entry);
        Assert.Equal(TimeSpan.FromSeconds(60), lifetime);
    }

    [Fact(DisplayName = "RFC-9111-§4.2: s-maxage=120 overrides max-age=60 for shared cache")]
    public void SMaxAge_OverridesMaxAge_SharedCache()
    {
        var entry = MakeEntry(maxAgeSeconds: 60, sMaxAgeSeconds: 120);
        var sharedPolicy = new CachePolicy { SharedCache = true };
        var lifetime = CacheFreshnessEvaluator.GetFreshnessLifetime(entry, sharedPolicy);
        Assert.Equal(TimeSpan.FromSeconds(120), lifetime);
    }

    [Fact(DisplayName = "RFC-9111-§4.2: s-maxage ignored for private cache")]
    public void SMaxAge_IgnoredForPrivateCache()
    {
        var entry = MakeEntry(maxAgeSeconds: 60, sMaxAgeSeconds: 120);
        var privatePolicy = new CachePolicy { SharedCache = false };
        var lifetime = CacheFreshnessEvaluator.GetFreshnessLifetime(entry, privatePolicy);
        Assert.Equal(TimeSpan.FromSeconds(60), lifetime);
    }

    [Fact(DisplayName = "RFC-9111-§5.3: Expires header used when no max-age")]
    public void ExpiresHeader_UsedWhenNoMaxAge()
    {
        var entry = MakeEntry(expires: _baseTime.AddSeconds(300));
        var lifetime = CacheFreshnessEvaluator.GetFreshnessLifetime(entry);
        Assert.Equal(TimeSpan.FromSeconds(300), lifetime);
    }

    [Fact(DisplayName = "RFC-9111-§4.2.2: heuristic freshness = 10% of age from Last-Modified")]
    public void HeuristicFreshness_TenPercentOfAge()
    {
        // Date = base, Last-Modified = 1000s before Date → 10% = 100s
        var entry = MakeEntry(lastModified: _baseTime.AddSeconds(-1000));
        var lifetime = CacheFreshnessEvaluator.GetFreshnessLifetime(entry);
        Assert.Equal(TimeSpan.FromSeconds(100), lifetime);
    }

    [Fact(DisplayName = "RFC-9111-§4.2.2: heuristic freshness capped at 1 day")]
    public void HeuristicFreshness_CappedAtOneDay()
    {
        // 10% of 100 days = 10 days → capped at 1 day
        var entry = MakeEntry(lastModified: _baseTime.AddDays(-100));
        var lifetime = CacheFreshnessEvaluator.GetFreshnessLifetime(entry);
        Assert.Equal(TimeSpan.FromDays(1), lifetime);
    }

    [Fact(DisplayName = "RFC-9111-§4.2: no freshness info → lifetime = zero")]
    public void NoFreshnessInfo_LifetimeZero()
    {
        var entry = MakeEntry();
        var lifetime = CacheFreshnessEvaluator.GetFreshnessLifetime(entry);
        Assert.Equal(TimeSpan.Zero, lifetime);
    }

    // ── GetCurrentAge ────────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC-9111-§4.2.3: current age uses Age header value")]
    public void CurrentAge_UsesAgeHeader()
    {
        // Entry was received at _baseTime, Age header = 30s, now = _baseTime + 10s
        var entry = MakeEntry(ageHeaderSeconds: 30);
        var now = _baseTime.AddSeconds(10);
        var age = CacheFreshnessEvaluator.GetCurrentAge(entry, now);
        // corrected_age = max(apparent=0, age=30 + response_delay=1) = 31; resident=10 → 41
        Assert.Equal(TimeSpan.FromSeconds(41), age);
    }

    [Fact(DisplayName = "RFC-9111-§4.2.3: current age without Age header uses response delay")]
    public void CurrentAge_WithoutAgeHeader_UsesResponseDelay()
    {
        // No Age header; date = request+1s; now = request+11s
        var entry = MakeEntry();
        var now = _baseTime.AddSeconds(10);
        // apparent_age = max(0, responseTime - date) = 0
        // corrected_age = max(0, 0 + responseDelay=1) = 1
        // resident_time = 10s
        // total = 11s
        var age = CacheFreshnessEvaluator.GetCurrentAge(entry, now);
        Assert.Equal(TimeSpan.FromSeconds(11), age);
    }

    // ── IsFresh ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC-9111-§4.2: fresh entry: freshness_lifetime > current_age → IsFresh=true")]
    public void IsFresh_True_WhenFreshnessExceedsAge()
    {
        var entry = MakeEntry(maxAgeSeconds: 60);
        var now = _baseTime.AddSeconds(10);
        Assert.True(CacheFreshnessEvaluator.IsFresh(entry, now));
    }

    [Fact(DisplayName = "RFC-9111-§4.2: stale entry: freshness_lifetime ≤ current_age → IsFresh=false")]
    public void IsFresh_False_WhenAgeExceedsFreshness()
    {
        var entry = MakeEntry(maxAgeSeconds: 10);
        var now = _baseTime.AddSeconds(60);
        Assert.False(CacheFreshnessEvaluator.IsFresh(entry, now));
    }

    // ── Evaluate ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC-9111-§4: Evaluate with null entry → Miss")]
    public void Evaluate_NullEntry_Miss()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var result = CacheFreshnessEvaluator.Evaluate(null, request, DateTimeOffset.UtcNow);
        Assert.Equal(CacheLookupStatus.Miss, result.Status);
    }

    [Fact(DisplayName = "RFC-9111-§4: Evaluate with fresh entry → Fresh")]
    public void Evaluate_FreshEntry_Fresh()
    {
        var entry = MakeEntry(maxAgeSeconds: 60);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var now = _baseTime.AddSeconds(10);
        var result = CacheFreshnessEvaluator.Evaluate(entry, request, now);
        Assert.Equal(CacheLookupStatus.Fresh, result.Status);
    }
}
