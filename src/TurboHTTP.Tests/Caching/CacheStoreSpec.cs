using System.Net;
using TurboHTTP.Protocol.Caching;

namespace TurboHTTP.Tests.Caching;

public sealed class CacheStoreSpec
{
    private static readonly DateTimeOffset _baseTime = new(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);


    private static void Put(CacheStore store, HttpRequestMessage request, HttpResponseMessage response,
        byte[] body, DateTimeOffset requestTime, DateTimeOffset responseTime)
    {
        var (owner, length) = CacheStore.RentBody(body);
        store.Put(request, response, owner, length, requestTime, responseTime);
    }

    private static HttpRequestMessage GetRequest(string uri = "http://example.com/resource")
        => new(HttpMethod.Get, uri);

    private static HttpResponseMessage OkResponse(int maxAge = 60)
    {
        var r = new HttpResponseMessage(HttpStatusCode.OK);
        r.Headers.TryAddWithoutValidation("Cache-Control", $"max-age={maxAge}");
        r.Headers.Date = _baseTime;
        return r;
    }


    [Trait("RFC", "RFC9111-3.1")]
    [Fact]
    public void CacheStore_should_be_cacheable_when_200_ok_with_max_age()
    {
        var response = OkResponse();
        Assert.True(CacheStore.IsCacheable(response));
    }

    [Trait("RFC", "RFC9111-3.1")]
    [Theory]
    [InlineData(200)]
    [InlineData(203)]
    [InlineData(204)]
    [InlineData(206)]
    [InlineData(300)]
    [InlineData(301)]
    [InlineData(308)]
    [InlineData(404)]
    [InlineData(410)]
    public void CacheStore_should_be_cacheable_when_status_code_is_cacheable(int statusCode)
    {
        var response = new HttpResponseMessage((HttpStatusCode)statusCode);
        Assert.True(CacheStore.IsCacheable(response));
    }

    [Trait("RFC", "RFC9111-3.1")]
    [Fact]
    public void CacheStore_should_not_be_cacheable_when_500_internal_server_error()
    {
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        Assert.False(CacheStore.IsCacheable(response));
    }


    [Trait("RFC", "RFC9111-3")]
    [Fact]
    public void CacheStore_should_store_entry_when_get_200_with_max_age()
    {
        Assert.True(CacheStore.ShouldStore(GetRequest(), OkResponse()));
    }

    [Trait("RFC", "RFC9111-3")]
    [Fact]
    public void CacheStore_should_not_store_entry_when_post_200_unsafe_method()
    {
        var post = new HttpRequestMessage(HttpMethod.Post, "http://example.com/resource");
        Assert.False(CacheStore.ShouldStore(post, OkResponse()));
    }

    [Trait("RFC", "RFC9111-5.2.1.5")]
    [Fact]
    public void CacheStore_should_not_store_entry_when_request_has_no_store()
    {
        var request = GetRequest();
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-store");
        Assert.False(CacheStore.ShouldStore(request, OkResponse()));
    }

    [Trait("RFC", "RFC9111-5.2.2.5")]
    [Fact]
    public void CacheStore_should_not_store_entry_when_response_has_no_store()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Cache-Control", "no-store");
        Assert.False(CacheStore.ShouldStore(GetRequest(), response));
    }


    [Trait("RFC", "RFC9111-4")]
    [Fact]
    public void CacheStore_should_return_null_when_store_is_empty()
    {
        var store = new CacheStore();
        var result = store.Get(GetRequest());
        Assert.Null(result);
    }

    [Trait("RFC", "RFC9111-3")]
    [Fact]
    public void CacheStore_should_return_cached_entry_when_put_then_get_same_uri()
    {
        var store = new CacheStore();
        var request = GetRequest();
        var response = OkResponse();
        var body = new byte[] { 1, 2, 3 };

        Put(store, request, response, body, _baseTime.AddSeconds(-1), _baseTime);

        var entry = store.Get(new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource"));
        Assert.NotNull(entry);
        Assert.True(entry.Body.Span.SequenceEqual(body));
    }

    [Trait("RFC", "RFC9111-4.4")]
    [Fact]
    public void CacheStore_should_remove_entry_when_invalidated()
    {
        var store = new CacheStore();
        var request = GetRequest();
        Put(store, request, OkResponse(), [], _baseTime.AddSeconds(-1), _baseTime);

        store.Invalidate(new Uri("http://example.com/resource"));

        Assert.Null(store.Get(GetRequest()));
    }


    [Trait("RFC", "RFC9111-4.1")]
    [Fact]
    public void CacheStore_should_return_miss_when_vary_header_and_different_accept()
    {
        var store = new CacheStore();

        var request1 = GetRequest();
        request1.Headers.TryAddWithoutValidation("Accept", "application/json");

        var response = OkResponse();
        response.Headers.TryAddWithoutValidation("Vary", "Accept");

        Put(store, request1, response, [], _baseTime.AddSeconds(-1), _baseTime);

        var request2 = GetRequest();
        request2.Headers.TryAddWithoutValidation("Accept", "text/html");

        Assert.Null(store.Get(request2));
    }

    [Trait("RFC", "RFC9111-4.1")]
    [Fact]
    public void CacheStore_should_return_hit_when_vary_header_and_matching_accept()
    {
        var store = new CacheStore();

        var request1 = GetRequest();
        request1.Headers.TryAddWithoutValidation("Accept", "application/json");

        var response = OkResponse();
        response.Headers.TryAddWithoutValidation("Vary", "Accept");

        Put(store, request1, response, [42], _baseTime.AddSeconds(-1), _baseTime);

        var request2 = GetRequest();
        request2.Headers.TryAddWithoutValidation("Accept", "application/json");

        var entry = store.Get(request2);
        Assert.NotNull(entry);
    }

    [Trait("RFC", "RFC9111-4.1")]
    [Fact]
    public void CacheStore_should_never_match_when_vary_is_star()
    {
        var store = new CacheStore();

        var response = OkResponse();
        response.Headers.TryAddWithoutValidation("Vary", "*");

        Put(store, GetRequest(), response, [], _baseTime.AddSeconds(-1), _baseTime);

        Assert.Null(store.Get(GetRequest()));
    }


    [Trait("RFC", "RFC9111-5.2.2.3")]
    [Fact]
    public void CacheStore_should_store_when_must_understand_and_200()
    {
        var request = GetRequest();
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=60, must-understand");
        response.Headers.Date = _baseTime;

        Assert.True(CacheStore.ShouldStore(request, response));
    }

    [Trait("RFC", "RFC9111-5.2.2.3")]
    [Fact]
    public void CacheStore_should_not_store_when_must_understand_and_unknown_status()
    {
        var request = GetRequest();
        var response = new HttpResponseMessage((HttpStatusCode)299);
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=60, must-understand");
        response.Headers.Date = _baseTime;

        Assert.False(CacheStore.ShouldStore(request, response));
    }

    [Trait("RFC", "RFC9111-5.2.2.3")]
    [Fact]
    public void CacheStore_should_store_when_no_must_understand()
    {
        var request = GetRequest();
        // 200 is cacheable-by-default; without must-understand, any cacheable status is fine
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=60");
        response.Headers.Date = _baseTime;

        Assert.True(CacheStore.ShouldStore(request, response));
    }

    [Trait("RFC", "RFC9111-3.1")]
    [Fact]
    public void CacheStore_should_not_store_when_206_partial_content()
    {
        var request = GetRequest();
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent);
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=60");
        response.Headers.Date = _baseTime;

        Assert.False(CacheStore.ShouldStore(request, response));
    }

    [Trait("RFC", "RFC9111-3.1")]
    [Fact]
    public void CacheStore_should_not_store_when_response_has_content_range()
    {
        var request = GetRequest();
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new ByteArrayContent([1, 2, 3]);
        response.Content.Headers.ContentRange =
            new System.Net.Http.Headers.ContentRangeHeaderValue(0, 2, 100);
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=60");
        response.Headers.Date = _baseTime;

        Assert.False(CacheStore.ShouldStore(request, response));
    }

    [Trait("RFC", "RFC9111-3.1")]
    [Fact]
    public void CacheStore_should_store_when_200_without_content_range()
    {
        var request = GetRequest();
        var response = OkResponse();

        Assert.True(CacheStore.ShouldStore(request, response));
    }

    [Trait("RFC", "RFC9111-3.1")]
    [Fact]
    public void CacheStore_should_not_merge_trailers_when_cached_with_trailers()
    {
        var store = new CacheStore();
        var request = GetRequest();

        // Simulate a chunked response with trailing headers
        var response = OkResponse();
        response.TrailingHeaders.TryAddWithoutValidation("Checksum", "abc123");
        response.TrailingHeaders.TryAddWithoutValidation("Signature", "xyz789");

        Put(store, request, response, [1, 2, 3], _baseTime.AddSeconds(-1), _baseTime);

        var entry = store.Get(GetRequest());
        Assert.NotNull(entry);

        // RFC 9111 §3.1: trailers MUST NOT be combined with header fields
        Assert.False(entry.Response.Headers.Contains("Checksum"),
            "Trailer field 'Checksum' must not appear in response headers");
        Assert.False(entry.Response.Headers.Contains("Signature"),
            "Trailer field 'Signature' must not appear in response headers");

        // Verify trailers are still available on TrailingHeaders
        Assert.True(entry.Response.TrailingHeaders.Contains("Checksum"));
        Assert.True(entry.Response.TrailingHeaders.Contains("Signature"));
        Assert.Equal("abc123", entry.Response.TrailingHeaders.GetValues("Checksum").Single());
        Assert.Equal("xyz789", entry.Response.TrailingHeaders.GetValues("Signature").Single());
    }

    [Trait("RFC", "RFC9111-3.1")]
    [Fact]
    public void CacheStore_should_not_store_connection_header_when_connection_header()
    {
        var store = new CacheStore();
        var request = GetRequest();
        var response = OkResponse();
        response.Headers.TryAddWithoutValidation("Connection", "keep-alive");
        response.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=5");

        Put(store, request, response, [1, 2, 3], _baseTime.AddSeconds(-1), _baseTime);

        var entry = store.Get(GetRequest());
        Assert.NotNull(entry);
        Assert.False(entry.Response.Headers.Contains("Connection"),
            "Connection header must not be stored in cache");
        Assert.False(entry.Response.Headers.Contains("Keep-Alive"),
            "Keep-Alive header must not be stored in cache");
    }

    [Trait("RFC", "RFC9111-3.1")]
    [Theory]
    [InlineData("Keep-Alive")]
    [InlineData("Proxy-Authenticate")]
    [InlineData("Proxy-Authorization")]
    [InlineData("TE")]
    [InlineData("Trailer")]
    [InlineData("Transfer-Encoding")]
    [InlineData("Upgrade")]
    public void CacheStore_should_not_store_connection_specific_header(string headerName)
    {
        var store = new CacheStore();
        var request = GetRequest();
        var response = OkResponse();
        response.Headers.TryAddWithoutValidation(headerName, "some-value");

        Put(store, request, response, [1, 2, 3], _baseTime.AddSeconds(-1), _baseTime);

        var entry = store.Get(GetRequest());
        Assert.NotNull(entry);
        Assert.False(entry.Response.Headers.Contains(headerName),
            $"{headerName} header must not be stored in cache");
    }

    [Trait("RFC", "RFC9111-3.1")]
    [Fact]
    public void CacheStore_should_store_custom_headers()
    {
        var store = new CacheStore();
        var request = GetRequest();
        var response = OkResponse();
        response.Headers.TryAddWithoutValidation("X-Custom-Header", "my-value");
        response.Headers.TryAddWithoutValidation("X-Request-Id", "abc-123");
        // Also add a connection-specific header to ensure it's stripped while custom headers survive
        response.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=5");

        Put(store, request, response, [1, 2, 3], _baseTime.AddSeconds(-1), _baseTime);

        var entry = store.Get(GetRequest());
        Assert.NotNull(entry);
        Assert.True(entry.Response.Headers.Contains("X-Custom-Header"),
            "Custom header X-Custom-Header must be preserved in cache");
        Assert.Equal("my-value", entry.Response.Headers.GetValues("X-Custom-Header").Single());
        Assert.True(entry.Response.Headers.Contains("X-Request-Id"),
            "Custom header X-Request-Id must be preserved in cache");
        Assert.Equal("abc-123", entry.Response.Headers.GetValues("X-Request-Id").Single());
        Assert.False(entry.Response.Headers.Contains("Keep-Alive"),
            "Keep-Alive header must not be stored in cache");
    }

    [Trait("RFC", "RFC9111-3")]
    [Fact]
    public void CacheStore_should_evict_entries_when_max_entries_exceeded()
    {
        var policy = new CachePolicy { MaxEntries = 2 };
        var store = new CacheStore(policy);

        for (var i = 0; i < 3; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"http://example.com/r{i}");
            Put(store, req, OkResponse(), [], _baseTime.AddSeconds(-1), _baseTime);
        }

        // Store should have at most 2 entries
        Assert.Equal(2, store.Count);
    }
}
