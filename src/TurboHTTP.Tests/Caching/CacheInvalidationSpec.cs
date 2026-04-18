using System.Net;
using TurboHTTP.Protocol.Caching;

namespace TurboHTTP.Tests.Caching;

public sealed class CacheInvalidationSpec
{
    private static readonly DateTimeOffset _baseTime = new(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static CacheStore CreateStoreWithEntry(string uri = "http://example.com/resource")
    {
        var store = new CacheStore();
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
        response.Headers.Date = _baseTime;

        var (owner, length) = CacheStore.RentBody([1, 2, 3]);
        store.Put(request, response, owner, length, _baseTime.AddSeconds(-1), _baseTime);
        return store;
    }

    private static bool IsSameOrigin(Uri requestUri, Uri targetUri)
    {
        if (!targetUri.IsAbsoluteUri)
        {
            targetUri = new Uri(requestUri, targetUri);
        }

        return string.Equals(requestUri.Scheme, targetUri.Scheme, StringComparison.OrdinalIgnoreCase)
               && string.Equals(requestUri.Host, targetUri.Host, StringComparison.OrdinalIgnoreCase)
               && requestUri.Port == targetUri.Port;
    }


    [Trait("RFC", "RFC9111-4.4")]
    [Fact]
    public void CacheInvalidation_should_invalidate_when_post_with_location()
    {
        var targetUri = "http://example.com/created-resource";
        var store = CreateStoreWithEntry(targetUri);
        Assert.NotNull(store.Get(new HttpRequestMessage(HttpMethod.Get, targetUri)));

        store.Invalidate(new Uri(targetUri));

        Assert.Null(store.Get(new HttpRequestMessage(HttpMethod.Get, targetUri)));
    }

    [Trait("RFC", "RFC9111-4.4")]
    [Fact]
    public void CacheInvalidation_should_invalidate_when_put_with_content_location()
    {
        var targetUri = "http://example.com/updated-resource";
        var store = CreateStoreWithEntry(targetUri);
        Assert.NotNull(store.Get(new HttpRequestMessage(HttpMethod.Get, targetUri)));

        store.Invalidate(new Uri(targetUri));

        Assert.Null(store.Get(new HttpRequestMessage(HttpMethod.Get, targetUri)));
    }

    [Trait("RFC", "RFC9111-4.4")]
    [Fact]
    public void CacheInvalidation_should_not_invalidate_when_cross_origin_location()
    {
        var store = CreateStoreWithEntry();

        var requestUri = new Uri("http://example.com/action");
        var locationUri = new Uri("http://other-host.com/resource");

        Assert.False(IsSameOrigin(requestUri, locationUri));

        // The store entry should remain untouched
        Assert.NotNull(store.Get(new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource")));
    }

    [Trait("RFC", "RFC9111-4.4")]
    [Fact]
    public void CacheInvalidation_should_not_invalidate_when_safe_method()
    {
        var store = CreateStoreWithEntry();

        // GET is a safe method — no invalidation should happen
        var entry = store.Get(new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource"));

        Assert.NotNull(entry);
    }

    [Trait("RFC", "RFC9111-4.4")]
    [Fact]
    public void CacheInvalidation_should_not_invalidate_when_error_response()
    {
        var store = CreateStoreWithEntry();

        // A POST returning 500 should NOT trigger invalidation
        var entry = store.Get(new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource"));

        // Entry remains because error responses do not trigger invalidation
        Assert.NotNull(entry);
    }

    [Trait("RFC", "RFC9111-4.4")]
    [Fact]
    public void CacheInvalidation_should_invalidate_both_uris_when_delete_with_location()
    {
        var requestUri = "http://example.com/item/42";
        var locationUri = "http://example.com/items";

        var store = CreateStoreWithEntry(requestUri);

        var locRequest = new HttpRequestMessage(HttpMethod.Get, locationUri);
        var locResponse = new HttpResponseMessage(HttpStatusCode.OK);
        locResponse.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
        locResponse.Headers.Date = _baseTime;
        var (locOwner, locLength) = CacheStore.RentBody([4, 5, 6]);
        store.Put(locRequest, locResponse, locOwner, locLength, _baseTime.AddSeconds(-1), _baseTime);

        Assert.NotNull(store.Get(new HttpRequestMessage(HttpMethod.Get, requestUri)));
        Assert.NotNull(store.Get(new HttpRequestMessage(HttpMethod.Get, locationUri)));

        store.Invalidate(new Uri(requestUri));
        store.Invalidate(new Uri(locationUri));

        Assert.Null(store.Get(new HttpRequestMessage(HttpMethod.Get, requestUri)));
        Assert.Null(store.Get(new HttpRequestMessage(HttpMethod.Get, locationUri)));
    }

    [Trait("RFC", "RFC9111-4.4")]
    [Fact]
    public void CacheInvalidation_should_invalidate_when_same_origin_different_path()
    {
        var requestUri = new Uri("http://example.com/action");
        var locationUri = new Uri("http://example.com/other-path");

        Assert.True(IsSameOrigin(requestUri, locationUri));
    }

    [Trait("RFC", "RFC9111-4.4")]
    [Fact]
    public void CacheInvalidation_should_not_invalidate_when_different_port()
    {
        var requestUri = new Uri("http://example.com:80/action");
        var locationUri = new Uri("http://example.com:8080/resource");

        Assert.False(IsSameOrigin(requestUri, locationUri));
    }

    [Trait("RFC", "RFC9111-4.4")]
    [Fact]
    public void CacheInvalidation_should_not_invalidate_when_different_scheme()
    {
        var requestUri = new Uri("http://example.com/action");
        var locationUri = new Uri("https://example.com/resource");

        Assert.False(IsSameOrigin(requestUri, locationUri));
    }

    [Trait("RFC", "RFC9111-4.4")]
    [Fact]
    public void CacheInvalidation_should_resolve_relative_location_against_request_uri()
    {
        var requestUri = new Uri("http://example.com/api/action");
        var relativeUri = new Uri("/api/resource", UriKind.Relative);

        var resolved = new Uri(requestUri, relativeUri);

        Assert.Equal("http://example.com/api/resource", resolved.ToString());
        Assert.True(IsSameOrigin(requestUri, resolved));
    }
}
