using System.Net;
using TurboHTTP.Protocol.Caching;

namespace TurboHTTP.Tests.Caching;

/// <summary>
/// RFC 9111 §4.2 — Cache freshness calculation tests.
/// Covers s-maxage, max-age, Expires headers, heuristic freshness,
/// Age header correction, and current-age calculation.
/// </summary>
/// <remarks>
/// Class under test: <see cref="CacheFreshnessEvaluator"/>.
/// RFC 9111 §4.2: A cached response is fresh if its remaining lifetime exceeds its current age.
/// </remarks>
public sealed class CacheFreshnessSpec
{
    private static readonly DateTimeOffset _baseTime = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);


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


    [Trait("RFC", "RFC9111-4.2")]
    [Fact]
    public void CacheFreshness_should_return_freshness_lifetime_60s_when_max_age_60()
    {
        var entry = MakeEntry(maxAgeSeconds: 60);
        var lifetime = CacheFreshnessEvaluator.GetFreshnessLifetime(entry);
        Assert.Equal(TimeSpan.FromSeconds(60), lifetime);
    }

    [Trait("RFC", "RFC9111-4.2")]
    [Fact]
    public void CacheFreshness_should_override_max_age_with_s_max_age_when_shared_cache()
    {
        var entry = MakeEntry(maxAgeSeconds: 60, sMaxAgeSeconds: 120);
        var sharedPolicy = new CachePolicy { SharedCache = true };
        var lifetime = CacheFreshnessEvaluator.GetFreshnessLifetime(entry, sharedPolicy);
        Assert.Equal(TimeSpan.FromSeconds(120), lifetime);
    }

    [Trait("RFC", "RFC9111-4.2")]
    [Fact]
    public void CacheFreshness_should_ignore_s_max_age_when_private_cache()
    {
        var entry = MakeEntry(maxAgeSeconds: 60, sMaxAgeSeconds: 120);
        var privatePolicy = new CachePolicy { SharedCache = false };
        var lifetime = CacheFreshnessEvaluator.GetFreshnessLifetime(entry, privatePolicy);
        Assert.Equal(TimeSpan.FromSeconds(60), lifetime);
    }

    [Trait("RFC", "RFC9111-5.3")]
    [Fact]
    public void CacheFreshness_should_use_expires_header_when_no_max_age()
    {
        var entry = MakeEntry(expires: _baseTime.AddSeconds(300));
        var lifetime = CacheFreshnessEvaluator.GetFreshnessLifetime(entry);
        Assert.Equal(TimeSpan.FromSeconds(300), lifetime);
    }

    [Trait("RFC", "RFC9111-4.2.2")]
    [Fact]
    public void CacheFreshness_should_use_ten_percent_of_age_when_heuristic_freshness()
    {
        // Date = base, Last-Modified = 1000s before Date → 10% = 100s
        var entry = MakeEntry(lastModified: _baseTime.AddSeconds(-1000));
        var lifetime = CacheFreshnessEvaluator.GetFreshnessLifetime(entry);
        Assert.Equal(TimeSpan.FromSeconds(100), lifetime);
    }

    [Trait("RFC", "RFC9111-4.2.2")]
    [Fact]
    public void CacheFreshness_should_cap_freshness_at_one_day_when_heuristic_freshness_exceeds_one_day()
    {
        // 10% of 100 days = 10 days → capped at 1 day
        var entry = MakeEntry(lastModified: _baseTime.AddDays(-100));
        var lifetime = CacheFreshnessEvaluator.GetFreshnessLifetime(entry);
        Assert.Equal(TimeSpan.FromDays(1), lifetime);
    }

    [Trait("RFC", "RFC9111-4.2")]
    [Fact]
    public void CacheFreshness_should_return_lifetime_zero_when_no_freshness_info()
    {
        var entry = MakeEntry();
        var lifetime = CacheFreshnessEvaluator.GetFreshnessLifetime(entry);
        Assert.Equal(TimeSpan.Zero, lifetime);
    }


    [Trait("RFC", "RFC9111-4.2.3")]
    [Fact]
    public void CacheFreshness_should_use_age_header_when_computing_current_age()
    {
        // Entry was received at _baseTime, Age header = 30s, now = _baseTime + 10s
        var entry = MakeEntry(ageHeaderSeconds: 30);
        var now = _baseTime.AddSeconds(10);
        var age = CacheFreshnessEvaluator.GetCurrentAge(entry, now);
        // corrected_age = max(apparent=0, age=30 + response_delay=1) = 31; resident=10 → 41
        Assert.Equal(TimeSpan.FromSeconds(41), age);
    }

    [Trait("RFC", "RFC9111-4.2.3")]
    [Fact]
    public void CacheFreshness_should_use_response_delay_when_no_age_header()
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


    [Trait("RFC", "RFC9111-4.2")]
    [Fact]
    public void CacheFreshness_should_return_is_fresh_true_when_freshness_lifetime_exceeds_current_age()
    {
        var entry = MakeEntry(maxAgeSeconds: 60);
        var now = _baseTime.AddSeconds(10);
        Assert.True(CacheFreshnessEvaluator.IsFresh(entry, now));
    }

    [Trait("RFC", "RFC9111-4.2")]
    [Fact]
    public void CacheFreshness_should_return_is_fresh_false_when_current_age_exceeds_freshness_lifetime()
    {
        var entry = MakeEntry(maxAgeSeconds: 10);
        var now = _baseTime.AddSeconds(60);
        Assert.False(CacheFreshnessEvaluator.IsFresh(entry, now));
    }


    [Trait("RFC", "RFC9111-4")]
    [Fact]
    public void CacheFreshness_should_return_miss_when_entry_is_null()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var result = CacheFreshnessEvaluator.Evaluate(null, request, DateTimeOffset.UtcNow);
        Assert.Equal(CacheLookupStatus.Miss, result.Status);
    }

    [Trait("RFC", "RFC9111-4")]
    [Fact]
    public void CacheFreshness_should_return_fresh_when_entry_is_fresh()
    {
        var entry = MakeEntry(maxAgeSeconds: 60);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var now = _baseTime.AddSeconds(10);
        var result = CacheFreshnessEvaluator.Evaluate(entry, request, now);
        Assert.Equal(CacheLookupStatus.Fresh, result.Status);
    }


    [Trait("RFC", "RFC9111-5.1")]
    [Fact]
    public void CacheFreshness_should_add_age_header_when_serving_from_cache()
    {
        var entry = MakeEntry(maxAgeSeconds: 300);
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var now = _baseTime.AddSeconds(50);

        CacheFreshnessEvaluator.InjectAgeHeader(response, entry, now);

        Assert.True(response.Headers.Contains("Age"));
    }

    [Trait("RFC", "RFC9111-5.1")]
    [Fact]
    public void CacheFreshness_should_match_current_age_when_age_header_generated()
    {
        var entry = MakeEntry(maxAgeSeconds: 300);
        var now = _baseTime.AddSeconds(50);

        var expectedAge = CacheFreshnessEvaluator.GetCurrentAge(entry, now);
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        CacheFreshnessEvaluator.InjectAgeHeader(response, entry, now);

        var ageValue = response.Headers.GetValues("Age").Single();
        Assert.Equal(((long)expectedAge.TotalSeconds).ToString(), ageValue);
    }

    [Trait("RFC", "RFC9111-5.1")]
    [Fact]
    public void CacheFreshness_should_overwrite_age_when_already_present()
    {
        var entry = MakeEntry(maxAgeSeconds: 300);
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Age", "9999");

        var now = _baseTime.AddSeconds(50);
        CacheFreshnessEvaluator.InjectAgeHeader(response, entry, now);

        var values = response.Headers.GetValues("Age").ToList();
        Assert.Single(values);
        Assert.NotEqual("9999", values[0]);
        // Value should match the calculated current age
        var expectedAge = (long)CacheFreshnessEvaluator.GetCurrentAge(entry, now).TotalSeconds;
        Assert.Equal(expectedAge.ToString(), values[0]);
    }
}
