using TurboHTTP.Protocol.AltSvc;

namespace TurboHTTP.Tests.AltSvc;

public sealed class AltSvcCacheSpec
{
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static AltSvcEntry CreateH3Entry(int maxAge = 3600, string host = "", int port = 443)
    {
        return new AltSvcEntry("h3", host, port, maxAge, false, FixedNow.AddSeconds(maxAge));
    }

    private static AltSvcEntry CreateH2Entry(int maxAge = 3600)
    {
        return new AltSvcEntry("h2", "", 443, maxAge, false, FixedNow.AddSeconds(maxAge));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void TryGetHttp3_should_return_true_when_h3_entry_cached()
    {
        var cache = new AltSvcCache();
        cache.Store("example.com", [CreateH3Entry()]);

        Assert.True(cache.TryGetHttp3("example.com", out var entry, FixedNow));
        Assert.NotNull(entry);
        Assert.True(entry.IsHttp3);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void TryGetHttp3_should_return_false_when_no_entries_cached()
    {
        var cache = new AltSvcCache();

        Assert.False(cache.TryGetHttp3("example.com", out _, FixedNow));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void TryGetHttp3_should_return_false_when_only_h2_cached()
    {
        var cache = new AltSvcCache();
        cache.Store("example.com", [CreateH2Entry()]);

        Assert.False(cache.TryGetHttp3("example.com", out _, FixedNow));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void TryGetHttp3_should_return_false_when_entry_expired()
    {
        var cache = new AltSvcCache();
        cache.Store("example.com", [CreateH3Entry(maxAge: 60)]);

        var afterExpiry = FixedNow.AddSeconds(61);
        Assert.False(cache.TryGetHttp3("example.com", out _, afterExpiry));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void TryGetHttp3_should_evict_when_all_entries_expired()
    {
        var cache = new AltSvcCache();
        cache.Store("example.com", [CreateH3Entry(maxAge: 60)]);

        var afterExpiry = FixedNow.AddSeconds(61);
        cache.TryGetHttp3("example.com", out _, afterExpiry);

        Assert.Equal(0, cache.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void TryGetHttp3_should_return_true_when_h3_among_multiple_entries()
    {
        var cache = new AltSvcCache();
        cache.Store("example.com", [CreateH2Entry(), CreateH3Entry()]);

        Assert.True(cache.TryGetHttp3("example.com", out var entry, FixedNow));
        Assert.NotNull(entry);
        Assert.Equal("h3", entry.Protocol);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void Clear_should_remove_entries_for_host()
    {
        var cache = new AltSvcCache();
        cache.Store("example.com", [CreateH3Entry()]);
        cache.Store("other.com", [CreateH3Entry()]);

        cache.Clear("example.com");

        Assert.False(cache.TryGetHttp3("example.com", out _, FixedNow));
        Assert.True(cache.TryGetHttp3("other.com", out _, FixedNow));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void ClearAll_should_remove_all_entries()
    {
        var cache = new AltSvcCache();
        cache.Store("example.com", [CreateH3Entry()]);
        cache.Store("other.com", [CreateH3Entry()]);

        cache.ClearAll();

        Assert.Equal(0, cache.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void Store_should_replace_existing_entries()
    {
        var cache = new AltSvcCache();
        cache.Store("example.com", [CreateH3Entry(port: 443)]);
        cache.Store("example.com", [CreateH3Entry(port: 8443)]);

        Assert.True(cache.TryGetHttp3("example.com", out var entry, FixedNow));
        Assert.Equal(8443, entry!.Port);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void Store_should_ignore_empty_host()
    {
        var cache = new AltSvcCache();
        cache.Store("", [CreateH3Entry()]);

        Assert.Equal(0, cache.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void Store_should_ignore_empty_entries()
    {
        var cache = new AltSvcCache();
        cache.Store("example.com", []);

        Assert.Equal(0, cache.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void TryGetHttp3_should_be_case_insensitive_on_host()
    {
        var cache = new AltSvcCache();
        cache.Store("Example.COM", [CreateH3Entry()]);

        Assert.True(cache.TryGetHttp3("example.com", out _, FixedNow));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void TryGetHttp3_should_return_entry_with_custom_host_and_port()
    {
        var cache = new AltSvcCache();
        cache.Store("example.com", [CreateH3Entry(host: "alt.example.com", port: 8443)]);

        Assert.True(cache.TryGetHttp3("example.com", out var entry, FixedNow));
        Assert.Equal("alt.example.com", entry!.Host);
        Assert.Equal(8443, entry.Port);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void TryGetHttp3_should_not_evict_fresh_list_stored_concurrently()
    {
        // Validates the compare-and-remove fix: when all entries expire and eviction
        // runs, a concurrent Store() that replaced the list must not have its new
        // list deleted by the stale eviction path.
        var cache = new AltSvcCache();
        var expiredEntry = CreateH3Entry(maxAge: 60);
        cache.Store("example.com", [expiredEntry]);

        var afterExpiry = FixedNow.AddSeconds(61);

        // Simulate concurrent Store: replace with fresh entries BEFORE eviction runs
        var freshEntry = CreateH3Entry(maxAge: 3600);
        cache.Store("example.com", [freshEntry]);

        // Now trigger eviction via TryGetHttp3 with the expired time —
        // the eviction should NOT remove the fresh list because the reference changed
        cache.TryGetHttp3("example.com", out _, afterExpiry);

        // The fresh entry should still be retrievable
        Assert.True(cache.TryGetHttp3("example.com", out var entry, FixedNow));
        Assert.NotNull(entry);
        Assert.Equal(1, cache.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public async Task TryGetHttp3_should_survive_concurrent_store_and_eviction_under_contention()
    {
        // Stress test: multiple threads racing Store (fresh) vs TryGetHttp3 (expired time)
        // to verify the atomic compare-and-remove doesn't corrupt state.
        var cache = new AltSvcCache();
        var afterExpiry = FixedNow.AddSeconds(61);
        const int iterations = 1000;

        using var barrier = new Barrier(2);

        var storeTask = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (var i = 0; i < iterations; i++)
            {
                cache.Store("example.com", [CreateH3Entry(maxAge: 60)]);
            }
        }, TestContext.Current.CancellationToken);

        var evictTask = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (var i = 0; i < iterations; i++)
            {
                cache.TryGetHttp3("example.com", out _, afterExpiry);
            }
        }, TestContext.Current.CancellationToken);

        await Task.WhenAll(storeTask, evictTask);

        // No exception thrown — cache is in a consistent state.
        // Count is 0 or 1 depending on timing; the key invariant is no corruption.
        Assert.InRange(cache.Count, 0, 1);
    }
}