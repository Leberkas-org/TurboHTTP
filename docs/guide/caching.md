# HTTP Caching

TurboHttp includes a built-in in-memory cache that automatically stores and reuses responses — eliminating redundant network round-trips without any configuration.

Caching is disabled by default. Enable it by setting `CachePolicy` in `TurboClientOptions`.

## What Gets Cached

TurboHttp caches **GET responses** that the server declares as cacheable. A response is stored when:

- The request method is `GET`
- The response status is `200 OK`, `203 Non-Authoritative`, `204 No Content`, `206 Partial Content`, or `301 Moved Permanently`
- The response does **not** include `Cache-Control: no-store` or `Cache-Control: private`
- At least one freshness indicator is present (`max-age`, `s-maxage`, `Expires`, or a heuristic lifetime can be calculated)

Responses to `POST`, `PUT`, `DELETE`, and all other methods are **never cached**.

## How Long a Response Is Cached

Freshness is evaluated in this priority order:

| Source | Example | Notes |
|--------|---------|-------|
| `s-maxage` directive | `Cache-Control: s-maxage=3600` | Shared-cache lifetime; takes priority over `max-age` |
| `max-age` directive | `Cache-Control: max-age=300` | Seconds from the response date |
| `Expires` header | `Expires: Fri, 21 Mar 2026 12:00:00 GMT` | Absolute expiry date; ignored when `max-age` is present |
| Heuristic freshness | _(no directive)_ | 10% of the age since `Last-Modified`; applied only when no explicit directive is given |

Once a cached response becomes stale, TurboHttp issues a **conditional request** to revalidate it rather than fetching the full response again (see [Conditional Requests](#conditional-requests) below).

## Cache-Control Directives

| Directive | Direction | Behavior |
|-----------|-----------|----------|
| `max-age=N` | Response | Cache for N seconds from the response date |
| `s-maxage=N` | Response | Shared-cache lifetime; overrides `max-age` |
| `no-store` | Response | Never cache this response |
| `no-cache` | Response / Request | Cache the response, but **always revalidate** before serving it |
| `must-revalidate` | Response | Once stale, do not serve the cached copy without revalidation |
| `private` | Response | Do not cache — response is personalised to one user |
| `public` | Response | Explicitly marks the response as cacheable, even on shared caches |
| `no-cache` | Request | Bypass cache; fetch fresh from the server |
| `no-store` | Request | Bypass cache and do not store the response |
| `only-if-cached` | Request | Return cached copy or `504 Gateway Timeout` — never go to the network |

## Conditional Requests

When a cached response becomes stale, TurboHttp does not immediately throw it away. Instead, it asks the server whether the content has changed. This is called **revalidation**.

```
Client                                   Server
  |                                         |
  |── GET /data ──────────────────────────>|  (first request — cold cache)
  |<─ 200 OK + ETag: "abc123" ────────────|  (response stored in cache)
  |                                         |
  |   ... cache becomes stale ...           |
  |                                         |
  |── GET /data                            |  (second request — stale cache)
  |   If-None-Match: "abc123" ────────────>|  (conditional request sent)
  |<─ 304 Not Modified ────────────────────|  (server confirms unchanged)
  |                                         |  (TurboHttp refreshes TTL, serves cached body)
  |                                         |
  |── GET /data ──────────────────────────>|  (later, content has changed)
  |   If-None-Match: "abc123" ────────────>|
  |<─ 200 OK + new body + ETag: "xyz789" ─|  (new response replaces cache entry)
```

Two standard mechanisms are used:

| Validator | Request header | Response header | Description |
|-----------|----------------|-----------------|-------------|
| ETag | `If-None-Match: "abc123"` | `ETag: "abc123"` | Opaque token identifying the specific version of the content |
| Last-Modified date | `If-Modified-Since: Mon, 20 Mar 2026 10:00:00 GMT` | `Last-Modified: Mon, 20 Mar 2026 10:00:00 GMT` | Timestamp of the last content modification |

When the server responds with `304 Not Modified`, TurboHttp:

1. Keeps the cached response body (no data transferred)
2. Merges any updated headers from the `304` response (e.g. a new `Cache-Control` or `ETag`)
3. Resets the freshness clock — the entry is now fresh again

## Vary Header Support

When a response includes a `Vary` header, TurboHttp stores **separate cache entries** for each distinct combination of the listed request headers. This ensures that content-negotiated responses are cached correctly.

```
Vary: Accept-Encoding
```

Means: cache one entry for `Accept-Encoding: gzip` and a separate entry for `Accept-Encoding: identity`. A client requesting uncompressed content will never receive the compressed cached entry.

```
Vary: Accept, Accept-Language
```

Means: the cache key includes both `Accept` and `Accept-Language` header values. Each distinct combination gets its own entry.

> If the server returns `Vary: *`, the response is treated as uncacheable — it cannot be reused for any request.

## Configuration

Caching is configured via `CachePolicy` on `TurboClientOptions`:

```csharp
// Enable caching with defaults
var options = new TurboClientOptions
{
    BaseAddress = new Uri("https://api.example.com"),
    CachePolicy = CachePolicy.Default
};
```

Customise the cache size or behaviour:

```csharp
var options = new TurboClientOptions
{
    CachePolicy = new CachePolicy
    {
        MaxEntries = 500,            // maximum number of cached responses (default: 1000)
        MaxBodySize = 1024 * 512,    // maximum body size to cache, in bytes (default: 1 MB)
    }
};
```

Disable caching entirely:

```csharp
var options = new TurboClientOptions
{
    CachePolicy = null  // null disables the cache
};
```

With DI:

```csharp
builder.Services.AddTurboHttpClientFactory(options =>
{
    options = options with
    {
        BaseAddress = new Uri("https://api.example.com"),
        CachePolicy = CachePolicy.Default,
    };
});
```

## Bypassing the Cache Per-Request

Force a fresh fetch for a single request without changing the global policy:

```csharp
var request = new HttpRequestMessage(HttpMethod.Get, "/data");
request.Headers.CacheControl = new CacheControlHeaderValue
{
    NoCache = true  // revalidate regardless of freshness
};

var response = await client.SendAsync(request, ct);
```

To bypass the cache entirely and avoid storing the response:

```csharp
request.Headers.CacheControl = new CacheControlHeaderValue
{
    NoStore = true
};
```

## Custom Cache Store

The default cache is an in-memory LRU store scoped to the client instance. To use a different backing store — for example, a distributed cache or a persistent store — implement `IHttpCacheStore`:

```csharp
public sealed class RedisHttpCacheStore : IHttpCacheStore
{
    private readonly IDatabase _db;

    public RedisHttpCacheStore(IDatabase db) => _db = db;

    public ValueTask<CacheLookupResult> TryGetAsync(
        HttpRequestMessage request,
        CancellationToken ct)
    {
        // look up by cache key derived from method + URL + Vary headers
        var key = BuildKey(request);
        var entry = /* fetch from Redis */ ...;
        return entry is null
            ? new ValueTask<CacheLookupResult>(CacheLookupResult.Miss)
            : new ValueTask<CacheLookupResult>(CacheLookupResult.Hit(entry));
    }

    public ValueTask StoreAsync(
        HttpRequestMessage request,
        CacheEntry entry,
        CancellationToken ct)
    {
        var key = BuildKey(request);
        // store in Redis with TTL from entry.FreshnessLifetime
        return ValueTask.CompletedTask;
    }

    private static string BuildKey(HttpRequestMessage request) =>
        $"{request.Method}:{request.RequestUri}";
}
```

Register the custom store:

```csharp
builder.Services.AddTurboHttpClientFactory(options =>
{
    options = options with
    {
        CachePolicy = CachePolicy.Default,
        CacheStore = new RedisHttpCacheStore(redisDb),
    };
});
```
