using System.Net;
using TurboHTTP.Protocol.Caching;

namespace TurboHTTP.Tests.Caching;

public sealed class CacheFreshnessSpec
{
    private static readonly DateTimeOffset BaseTime = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

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

        var actualDate = date ?? BaseTime;
        var (owner, length) = CacheStore.RentBody([]);
        return new CacheEntry
        {
            Response = response,
            BodyOwner = owner,
            BodyLength = length,
            RequestTime = requestTime ?? actualDate.AddSeconds(-1),
            ResponseTime = responseTime ?? actualDate,
            Date = actualDate,
            Expires = expires,
            LastModified = lastModified,
            AgeSeconds = ageHeaderSeconds,
            CacheControl = cc
        };
    }


    [Fact]
    [Trait("RFC", "RFC9111-4.2")]
    public void CacheFreshness_should_return_freshness_lifetime_60s_when_max_age_60()
    {
        var entry = MakeEntry(maxAgeSeconds: 60);
        var lifetime = CacheFreshnessEvaluator.GetFreshnessLifetime(entry);
        Assert.Equal(TimeSpan.FromSeconds(60), lifetime);
    }

    [Fact]
    [Trait("RFC", "RFC9111-4.2")]
    public void CacheFreshness_should_override_max_age_with_s_max_age_when_shared_cache()
    {
        var entry = MakeEntry(maxAgeSeconds: 60, sMaxAgeSeconds: 120);
        var sharedPolicy = new CachePolicy { SharedCache = true };
        var lifetime = CacheFreshnessEvaluator.GetFreshnessLifetime(entry, sharedPolicy);
        Assert.Equal(TimeSpan.FromSeconds(120), lifetime);
    }

    [Fact]
    [Trait("RFC", "RFC9111-4.2")]
    public void CacheFreshness_should_ignore_s_max_age_when_private_cache()
    {
        var entry = MakeEntry(maxAgeSeconds: 60, sMaxAgeSeconds: 120);
        var privatePolicy = new CachePolicy { SharedCache = false };
        var lifetime = CacheFreshnessEvaluator.GetFreshnessLifetime(entry, privatePolicy);
        Assert.Equal(TimeSpan.FromSeconds(60), lifetime);
    }

    [Fact]
    [Trait("RFC", "RFC9111-5.3")]
    public void CacheFreshness_should_use_expires_header_when_no_max_age()
    {
        var entry = MakeEntry(expires: BaseTime.AddSeconds(300));
        var lifetime = CacheFreshnessEvaluator.GetFreshnessLifetime(entry);
        Assert.Equal(TimeSpan.FromSeconds(300), lifetime);
    }

    [Fact]
    [Trait("RFC", "RFC9111-4.2.2")]
    public void CacheFreshness_should_use_ten_percent_of_age_when_heuristic_freshness()
    {
        // Date = base, Last-Modified = 1000s before Date → 10% = 100s
        var entry = MakeEntry(lastModified: BaseTime.AddSeconds(-1000));
        var lifetime = CacheFreshnessEvaluator.GetFreshnessLifetime(entry);
        Assert.Equal(TimeSpan.FromSeconds(100), lifetime);
    }

    [Fact]
    [Trait("RFC", "RFC9111-4.2.2")]
    public void CacheFreshness_should_cap_freshness_at_one_day_when_heuristic_freshness_exceeds_one_day()
    {
        // 10% of 100 days = 10 days → capped at 1 day
        var entry = MakeEntry(lastModified: BaseTime.AddDays(-100));
        var lifetime = CacheFreshnessEvaluator.GetFreshnessLifetime(entry);
        Assert.Equal(TimeSpan.FromDays(1), lifetime);
    }

    [Fact]
    [Trait("RFC", "RFC9111-4.2")]
    public void CacheFreshness_should_return_lifetime_zero_when_no_freshness_info()
    {
        var entry = MakeEntry();
        var lifetime = CacheFreshnessEvaluator.GetFreshnessLifetime(entry);
        Assert.Equal(TimeSpan.Zero, lifetime);
    }

    [Fact]
    [Trait("RFC", "RFC9111-4.2.3")]
    public void CacheFreshness_should_use_age_header_when_computing_current_age()
    {
        // Entry was received at _baseTime, Age header = 30s, now = _baseTime + 10s
        var entry = MakeEntry(ageHeaderSeconds: 30);
        var now = BaseTime.AddSeconds(10);
        var age = CacheFreshnessEvaluator.GetCurrentAge(entry, now);
        // corrected_age = max(apparent=0, age=30 + response_delay=1) = 31; resident=10 → 41
        Assert.Equal(TimeSpan.FromSeconds(41), age);
    }

    [Fact]
    [Trait("RFC", "RFC9111-4.2.3")]
    public void CacheFreshness_should_use_response_delay_when_no_age_header()
    {
        // No Age header; date = request+1s; now = request+11s
        var entry = MakeEntry();
        var now = BaseTime.AddSeconds(10);
        // apparent_age = max(0, responseTime - date) = 0
        // corrected_age = max(0, 0 + responseDelay=1) = 1
        // resident_time = 10s
        // total = 11s
        var age = CacheFreshnessEvaluator.GetCurrentAge(entry, now);
        Assert.Equal(TimeSpan.FromSeconds(11), age);
    }


    [Fact]
    [Trait("RFC", "RFC9111-4.2")]
    public void CacheFreshness_should_return_is_fresh_true_when_freshness_lifetime_exceeds_current_age()
    {
        var entry = MakeEntry(maxAgeSeconds: 60);
        var now = BaseTime.AddSeconds(10);
        Assert.True(CacheFreshnessEvaluator.IsFresh(entry, now));
    }

    [Fact]
    [Trait("RFC", "RFC9111-4.2")]
    public void CacheFreshness_should_return_is_fresh_false_when_current_age_exceeds_freshness_lifetime()
    {
        var entry = MakeEntry(maxAgeSeconds: 10);
        var now = BaseTime.AddSeconds(60);
        Assert.False(CacheFreshnessEvaluator.IsFresh(entry, now));
    }

    [Fact]
    [Trait("RFC", "RFC9111-4")]
    public void CacheFreshness_should_return_miss_when_entry_is_null()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var result = CacheFreshnessEvaluator.Evaluate(null, request, DateTimeOffset.UtcNow);
        Assert.Equal(CacheLookupStatus.Miss, result.Status);
    }

    [Fact]
    [Trait("RFC", "RFC9111-4")]
    public void CacheFreshness_should_return_fresh_when_entry_is_fresh()
    {
        var entry = MakeEntry(maxAgeSeconds: 60);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var now = BaseTime.AddSeconds(10);
        var result = CacheFreshnessEvaluator.Evaluate(entry, request, now);
        Assert.Equal(CacheLookupStatus.Fresh, result.Status);
    }

    [Fact]
    [Trait("RFC", "RFC9111-5.1")]
    public void CacheFreshness_should_add_age_header_when_serving_from_cache()
    {
        var entry = MakeEntry(maxAgeSeconds: 300);
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var now = BaseTime.AddSeconds(50);

        CacheFreshnessEvaluator.InjectAgeHeader(response, entry, now);

        Assert.True(response.Headers.Contains("Age"));
    }

    [Fact]
    [Trait("RFC", "RFC9111-5.1")]
    public void CacheFreshness_should_match_current_age_when_age_header_generated()
    {
        var entry = MakeEntry(maxAgeSeconds: 300);
        var now = BaseTime.AddSeconds(50);

        var expectedAge = CacheFreshnessEvaluator.GetCurrentAge(entry, now);
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        CacheFreshnessEvaluator.InjectAgeHeader(response, entry, now);

        var ageValue = response.Headers.GetValues("Age").Single();
        Assert.Equal(((long)expectedAge.TotalSeconds).ToString(), ageValue);
    }

    [Fact]
    [Trait("RFC", "RFC9111-5.1")]
    public void CacheFreshness_should_overwrite_age_when_already_present()
    {
        var entry = MakeEntry(maxAgeSeconds: 300);
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Age", "9999");

        var now = BaseTime.AddSeconds(50);
        CacheFreshnessEvaluator.InjectAgeHeader(response, entry, now);

        var values = response.Headers.GetValues("Age").ToList();
        Assert.Single(values);
        Assert.NotEqual("9999", values[0]);
        // Value should match the calculated current age
        var expectedAge = (long)CacheFreshnessEvaluator.GetCurrentAge(entry, now).TotalSeconds;
        Assert.Equal(expectedAge.ToString(), values[0]);
    }

    [Fact]
    [Trait("RFC", "RFC9111-5.2.1.4")]
    public void Evaluate_should_return_must_revalidate_when_request_no_cache()
    {
        var entry = MakeEntry(maxAgeSeconds: 60);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
        var now = BaseTime.AddSeconds(10);

        var result = CacheFreshnessEvaluator.Evaluate(entry, request, now);

        Assert.Equal(CacheLookupStatus.MustRevalidate, result.Status);
    }

    [Fact]
    [Trait("RFC", "RFC9111-5.2.2.3")]
    public void Evaluate_should_return_must_revalidate_when_response_unqualified_no_cache()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var cc = new CacheControl { MaxAge = TimeSpan.FromSeconds(60), NoCache = true, NoCacheFields = null };
        var (owner, length) = CacheStore.RentBody([]);
        var entry = new CacheEntry
        {
            Response = response,
            BodyOwner = owner,
            BodyLength = length,
            RequestTime = BaseTime.AddSeconds(-1),
            ResponseTime = BaseTime,
            Date = BaseTime,
            CacheControl = cc
        };

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var now = BaseTime.AddSeconds(10);
        var result = CacheFreshnessEvaluator.Evaluate(entry, request, now);

        Assert.Equal(CacheLookupStatus.MustRevalidate, result.Status);
    }

    [Fact]
    [Trait("RFC", "RFC9111-5.2.1.3")]
    public void Evaluate_should_return_stale_when_request_min_fresh_not_satisfied()
    {
        var entry = MakeEntry(maxAgeSeconds: 60);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("Cache-Control", "min-fresh=50");
        var now = BaseTime.AddSeconds(20); // Only 40s freshness remaining

        var result = CacheFreshnessEvaluator.Evaluate(entry, request, now);

        Assert.Equal(CacheLookupStatus.MustRevalidate, result.Status);
    }

    [Fact]
    [Trait("RFC", "RFC9111-5.2.2.2")]
    public void Evaluate_should_return_must_revalidate_when_stale_and_must_revalidate_set()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var cc = new CacheControl { MaxAge = TimeSpan.FromSeconds(10), MustRevalidate = true };
        var (owner, length) = CacheStore.RentBody([]);
        var entry = new CacheEntry
        {
            Response = response,
            BodyOwner = owner,
            BodyLength = length,
            RequestTime = BaseTime.AddSeconds(-1),
            ResponseTime = BaseTime,
            Date = BaseTime,
            CacheControl = cc
        };

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var now = BaseTime.AddSeconds(60); // Entry is stale
        var result = CacheFreshnessEvaluator.Evaluate(entry, request, now);

        Assert.Equal(CacheLookupStatus.MustRevalidate, result.Status);
    }

    [Fact]
    [Trait("RFC", "RFC9111-5.2.2.2")]
    public void Evaluate_should_return_must_revalidate_when_stale_proxy_and_proxy_revalidate_in_shared_cache()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var cc = new CacheControl { MaxAge = TimeSpan.FromSeconds(10), ProxyRevalidate = true };
        var (owner, length) = CacheStore.RentBody([]);
        var entry = new CacheEntry
        {
            Response = response,
            BodyOwner = owner,
            BodyLength = length,
            RequestTime = BaseTime.AddSeconds(-1),
            ResponseTime = BaseTime,
            Date = BaseTime,
            CacheControl = cc
        };

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var now = BaseTime.AddSeconds(60); // Entry is stale
        var sharedPolicy = new CachePolicy { SharedCache = true };
        var result = CacheFreshnessEvaluator.Evaluate(entry, request, now, sharedPolicy);

        Assert.Equal(CacheLookupStatus.MustRevalidate, result.Status);
    }

    [Fact]
    [Trait("RFC", "RFC9111-5.2.1.2")]
    public void Evaluate_should_return_stale_when_request_max_stale_with_sufficient_tolerance()
    {
        var entry = MakeEntry(maxAgeSeconds: 10);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("Cache-Control", "max-stale=60");
        var now = BaseTime.AddSeconds(30); // 20s stale, within 60s tolerance

        var result = CacheFreshnessEvaluator.Evaluate(entry, request, now);

        Assert.Equal(CacheLookupStatus.Stale, result.Status);
    }

    [Fact]
    [Trait("RFC", "RFC9111-5.2.1.2")]
    public void Evaluate_should_return_must_revalidate_when_stale_exceeds_max_stale_tolerance()
    {
        var entry = MakeEntry(maxAgeSeconds: 10);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("Cache-Control", "max-stale=10");
        var now = BaseTime.AddSeconds(30); // 20s stale, exceeds 10s tolerance

        var result = CacheFreshnessEvaluator.Evaluate(entry, request, now);

        Assert.Equal(CacheLookupStatus.MustRevalidate, result.Status);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-5.2.1.2")]
    public void Evaluate_should_accept_stale_when_max_stale_no_value()
    {
        var entry = MakeEntry(maxAgeSeconds: 10);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("Cache-Control", "max-stale");
        var now = BaseTime.AddSeconds(100); // Very stale, but max-stale with no value accepts any staleness

        var result = CacheFreshnessEvaluator.Evaluate(entry, request, now);

        Assert.Equal(CacheLookupStatus.Stale, result.Status);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-4.2.3")]
    public void GetCurrentAge_should_use_apparent_age_when_date_in_future()
    {
        // When response_time > date, apparent age = response_time - date
        var entry = MakeEntry(requestTime: BaseTime, responseTime: BaseTime.AddSeconds(10), date: BaseTime);
        var now = BaseTime.AddSeconds(20);
        var age = CacheFreshnessEvaluator.GetCurrentAge(entry, now);
        // apparent_age = response_time - date = 10, age_value = 0, response_delay = response_time - request_time = 10
        // corrected_age = max(10, 0 + 10) = 10; resident = 20 - 10 = 10; total = 10 + 10 = 20
        Assert.Equal(TimeSpan.FromSeconds(20), age);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-4.2.3")]
    public void GetCurrentAge_should_handle_negative_response_delay()
    {
        // When request_time > response_time (clock skew), treat response_delay as 0
        var entry = MakeEntry(requestTime: BaseTime.AddSeconds(5), responseTime: BaseTime);
        var now = BaseTime.AddSeconds(10);
        var age = CacheFreshnessEvaluator.GetCurrentAge(entry, now);
        // response_delay becomes 0 due to clamping
        // corrected_age = max(0, 0 + 0) = 0; resident = 10
        Assert.Equal(TimeSpan.FromSeconds(10), age);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-4.2.3")]
    public void GetCurrentAge_should_handle_negative_resident_time()
    {
        // When now < response_time (clock skew), treat resident_time as 0
        var entry = MakeEntry();
        var now = BaseTime.AddSeconds(-10);
        var age = CacheFreshnessEvaluator.GetCurrentAge(entry, now);
        // response_delay = response_time - request_time = 1s, resident_time becomes 0 due to clamping
        // corrected_age = max(0, 0 + 1) = 1, total = 1 + 0 = 1
        Assert.Equal(TimeSpan.FromSeconds(1), age);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-5.3")]
    public void GetFreshnessLifetime_should_return_zero_when_expires_in_past()
    {
        var entry = MakeEntry(expires: BaseTime.AddSeconds(-300));
        var lifetime = CacheFreshnessEvaluator.GetFreshnessLifetime(entry);
        Assert.Equal(TimeSpan.Zero, lifetime);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-4.2.2")]
    public void GetFreshnessLifetime_should_return_zero_when_last_modified_in_future()
    {
        var entry = MakeEntry(lastModified: BaseTime.AddSeconds(1000));
        var lifetime = CacheFreshnessEvaluator.GetFreshnessLifetime(entry);
        Assert.Equal(TimeSpan.Zero, lifetime);
    }

    [Fact(Timeout = 5000)]
    public void Evaluate_should_return_stale_default_message_when_no_freshness()
    {
        var entry = MakeEntry();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var now = BaseTime;

        var result = CacheFreshnessEvaluator.Evaluate(entry, request, now);

        Assert.Equal(CacheLookupStatus.MustRevalidate, result.Status);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-5.3")]
    public void GetFreshnessLifetime_should_return_zero_when_expires_without_date()
    {
        // Expires header requires Date header to compute lifetime (RFC 9111 §5.3)
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var (owner, length) = CacheStore.RentBody([]);
        var entry = new CacheEntry
        {
            Response = response,
            BodyOwner = owner,
            BodyLength = length,
            RequestTime = BaseTime.AddSeconds(-1),
            ResponseTime = BaseTime,
            Date = null, // No Date header
            Expires = BaseTime.AddSeconds(300) // Has Expires but no Date
        };

        var lifetime = CacheFreshnessEvaluator.GetFreshnessLifetime(entry);

        Assert.Equal(TimeSpan.Zero, lifetime);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-4.2.3")]
    public void GetCurrentAge_should_use_zero_apparent_age_when_response_time_equals_date()
    {
        // When response_time == date, apparent_age = 0
        var entry = MakeEntry(requestTime: BaseTime, responseTime: BaseTime, date: BaseTime);
        var now = BaseTime.AddSeconds(5);
        var age = CacheFreshnessEvaluator.GetCurrentAge(entry, now);
        // apparent_age = 0, response_delay = 0, corrected_age = 0, resident = 5 → total = 5
        Assert.Equal(TimeSpan.FromSeconds(5), age);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-4.2.3")]
    public void GetCurrentAge_should_prefer_apparent_age_when_exceeds_age_plus_delay()
    {
        // When apparent_age > (ageValue + response_delay), use apparent_age
        // Response received 100s after Date, Age header = 50s, response_delay = 10s
        // apparent_age = 100, ageValue = 50, response_delay = 10
        // corrected_age = max(100, 50 + 10) = 100
        var entry = MakeEntry(
            ageHeaderSeconds: 50,
            requestTime: BaseTime,
            responseTime: BaseTime.AddSeconds(10),
            date: BaseTime.AddSeconds(-90) // Date is 90s before response
        );
        var now = BaseTime.AddSeconds(20);
        var age = CacheFreshnessEvaluator.GetCurrentAge(entry, now);
        // apparent_age = (responseTime - date) = 10 - (-90) = 100
        // ageValue = 50, response_delay = 10
        // corrected_age = max(100, 50 + 10) = 100
        // resident_time = 20 - 10 = 10
        // total = 100 + 10 = 110
        Assert.Equal(TimeSpan.FromSeconds(110), age);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-5.2.2.2")]
    public void Evaluate_should_return_must_revalidate_when_stale_proxy_and_proxy_revalidate_in_private_cache()
    {
        // proxy-revalidate should NOT apply in private cache (SharedCache=false)
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var cc = new CacheControl { MaxAge = TimeSpan.FromSeconds(10), ProxyRevalidate = true };
        var (owner, length) = CacheStore.RentBody([]);
        var entry = new CacheEntry
        {
            Response = response,
            BodyOwner = owner,
            BodyLength = length,
            RequestTime = BaseTime.AddSeconds(-1),
            ResponseTime = BaseTime,
            Date = BaseTime,
            CacheControl = cc
        };

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var now = BaseTime.AddSeconds(60); // Entry is stale
        var privatePolicy = new CachePolicy { SharedCache = false };
        var result = CacheFreshnessEvaluator.Evaluate(entry, request, now, privatePolicy);

        // In private cache, proxy-revalidate is ignored → entry is stale without validators → MustRevalidate
        Assert.Equal(CacheLookupStatus.MustRevalidate, result.Status);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-5.2.1.2")]
    public void Evaluate_should_return_stale_when_max_stale_no_value_accepting_any_staleness()
    {
        // max-stale with no value means accept any staleness (already tested but verifying edge case)
        var entry = MakeEntry(maxAgeSeconds: 10);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("Cache-Control", "max-stale");
        var now = BaseTime.AddSeconds(1000); // Extremely stale

        var result = CacheFreshnessEvaluator.Evaluate(entry, request, now);

        Assert.Equal(CacheLookupStatus.Stale, result.Status);
    }
}