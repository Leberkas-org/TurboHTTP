using System.Net;
using TurboHTTP.Features.Caching;

namespace TurboHTTP.Tests.Features.Caching;

public sealed class CacheLruEvictionSpec
{
    private static readonly DateTimeOffset BaseTime = new(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static HttpRequestMessage GetRequest(string uri = "http://example.com/resource")
        => new(HttpMethod.Get, uri);

    private static HttpResponseMessage OkResponse(int maxAge = 60)
    {
        var r = new HttpResponseMessage(HttpStatusCode.OK);
        r.Headers.TryAddWithoutValidation("Cache-Control", $"max-age={maxAge}");
        r.Headers.Date = BaseTime;
        return r;
    }

    private static void Put(Cache cache, string uri, byte[]? body = null)
    {
        var request = GetRequest(uri);
        var response = OkResponse();
        var bytes = body ?? "test"u8.ToArray();
        var (owner, length) = Cache.RentBody(bytes);
        cache.Put(request, response, owner, length, BaseTime, BaseTime);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-3")]
    public void Cache_should_evict_lru_entry_when_max_entries_exceeded()
    {
        var policy = new CachePolicy { MaxEntries = 2 };
        var cache = new Cache(policy);

        Put(cache, "http://example.com/a");
        Put(cache, "http://example.com/b");

        Assert.Equal(2, cache.Count);

        Put(cache, "http://example.com/c");

        Assert.Equal(2, cache.Count);
        Assert.Null(cache.Get(GetRequest("http://example.com/a")));
        Assert.NotNull(cache.Get(GetRequest("http://example.com/b")));
        Assert.NotNull(cache.Get(GetRequest("http://example.com/c")));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-3")]
    public void Cache_should_promote_accessed_entry_in_lru()
    {
        var policy = new CachePolicy { MaxEntries = 2 };
        var cache = new Cache(policy);

        Put(cache, "http://example.com/a");
        Put(cache, "http://example.com/b");

        cache.Get(GetRequest("http://example.com/a"));

        Put(cache, "http://example.com/c");

        Assert.NotNull(cache.Get(GetRequest("http://example.com/a")));
        Assert.Null(cache.Get(GetRequest("http://example.com/b")));
        Assert.NotNull(cache.Get(GetRequest("http://example.com/c")));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-3")]
    public void Cache_should_reject_body_exceeding_max_body_bytes()
    {
        var policy = new CachePolicy { MaxBodyBytes = 10 };
        var cache = new Cache(policy);

        var largeBody = new byte[100];
        Put(cache, "http://example.com/large", largeBody);

        Assert.Equal(0, cache.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-4")]
    public void Cache_should_invalidate_all_variants_for_uri()
    {
        var cache = new Cache();

        Put(cache, "http://example.com/resource");

        Assert.Equal(1, cache.Count);

        cache.Invalidate(new Uri("http://example.com/resource"));

        Assert.Equal(0, cache.Count);
        Assert.Null(cache.Get(GetRequest()));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-4")]
    public void Cache_should_noop_when_invalidating_unknown_uri()
    {
        var cache = new Cache();
        Put(cache, "http://example.com/a");

        cache.Invalidate(new Uri("http://example.com/nonexistent"));

        Assert.Equal(1, cache.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-3")]
    public void Cache_should_clear_all_entries()
    {
        var cache = new Cache();

        Put(cache, "http://example.com/a");
        Put(cache, "http://example.com/b");

        Assert.Equal(2, cache.Count);

        cache.Clear();

        Assert.Equal(0, cache.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-5.2.1.5")]
    public void Cache_should_not_store_when_request_has_no_store()
    {
        var request = GetRequest();
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-store");

        Assert.False(Cache.ShouldStore(request, OkResponse()));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-5.2.2.5")]
    public void Cache_should_not_store_when_response_has_no_store()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Cache-Control", "no-store");

        Assert.False(Cache.ShouldStore(GetRequest(), response));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-3")]
    public void Cache_should_not_store_partial_content()
    {
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent);
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=60");

        Assert.False(Cache.ShouldStore(GetRequest(), response));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-3")]
    public void Cache_should_not_store_when_content_range_present()
    {
        var response = OkResponse();
        response.Content = new ByteArrayContent([1, 2, 3]);
        response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(0, 2, 100);

        Assert.False(Cache.ShouldStore(GetRequest(), response));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-3")]
    public void Cache_should_not_store_must_understand_with_non_understood_status()
    {
        var response = new HttpResponseMessage((HttpStatusCode)299);
        response.Headers.TryAddWithoutValidation("Cache-Control", "must-understand, max-age=60");

        Assert.False(Cache.ShouldStore(GetRequest(), response));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-3")]
    public void Cache_should_not_store_private_response_in_shared_cache()
    {
        var policy = new CachePolicy { SharedCache = true };
        var cache = new Cache(policy);

        var response = OkResponse();
        response.Headers.TryAddWithoutValidation("Cache-Control", "private, max-age=60");
        var request = GetRequest();
        var (owner, length) = Cache.RentBody("test"u8);
        cache.Put(request, response, owner, length, BaseTime, BaseTime);

        Assert.Equal(0, cache.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-3")]
    public void Cache_should_replace_existing_variant_on_put()
    {
        var cache = new Cache();

        Put(cache, "http://example.com/resource");
        Assert.Equal(1, cache.Count);

        Put(cache, "http://example.com/resource");
        Assert.Equal(1, cache.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-4.1")]
    public async Task RentBodyFromStreamAsync_should_read_stream_into_memory()
    {
        var data = "Hello, World!"u8.ToArray();
        using var stream = new MemoryStream(data);

        var (owner, length) = await Cache.RentBodyFromStreamAsync(stream);

        Assert.Equal(data.Length, length);
        Assert.Equal(data, owner.Memory[..length].ToArray());

        owner.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-4.1")]
    public async Task RentBodyFromStreamAsync_should_handle_empty_stream()
    {
        using var stream = new MemoryStream([]);

        var (owner, length) = await Cache.RentBodyFromStreamAsync(stream);

        Assert.Equal(0, length);
        owner.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9111-4.1")]
    public async Task RentBodyFromStreamAsync_should_grow_buffer_for_large_streams()
    {
        var data = new byte[10_000];
        Random.Shared.NextBytes(data);
        using var stream = new MemoryStream(data);

        var (owner, length) = await Cache.RentBodyFromStreamAsync(stream, sizeHint: 256);

        Assert.Equal(data.Length, length);
        Assert.Equal(data, owner.Memory[..length].ToArray());

        owner.Dispose();
    }
}