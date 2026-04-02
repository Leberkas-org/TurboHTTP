# HTTP Caching

TurboHttp includes a built-in in-memory cache that automatically stores and reuses responses — eliminating redundant network round-trips without any configuration.

Caching is disabled by default. Enable it by calling `.WithCache()` on the builder.

## What Gets Cached

TurboHttp caches **GET responses** that the server declares as cacheable. A response is stored when:

- The request method is `GET`
- The response status indicates success or a permanent redirect:
  - **Successful responses:** `200 OK`, `204 No Content`, `206 Partial Content`
  - **Modified by intermediary:** `203 Non-Authoritative Information`
  - **Permanent redirect:** `301 Moved Permanently`
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
| Heuristic freshness | _(no directive)_ | When the server provides no explicit cache lifetime, TurboHttp estimates one: if a resource was last changed 100 days ago, it is assumed fresh for 10 days (10% of the time since the last modification). This only applies when no `max-age`, `s-maxage`, or `Expires` header is present. |

Once a cached response becomes stale, TurboHttp issues a **conditional request** to revalidate it rather than fetching the full response again (see [Conditional Requests](#conditional-requests) below).

## Cache-Control Directives

| Directive | Direction | Behavior |
|-----------|-----------|----------|
| `max-age=N` | Response | Cache for N seconds from the response date |
| `s-maxage=N` | Response | Shared-cache lifetime; overrides `max-age` |
| `no-store` | Response | Never cache this response |
| `no-cache` | Response | Cache the response, but **always revalidate** with the server before serving it |
| `must-revalidate` | Response | Once stale, do not serve the cached copy without revalidation |
| `private` | Response | Do not cache — response is personalised to one user |
| `public` | Response | Explicitly marks the response as cacheable, even on shared caches |
| `no-cache` | Request | Bypass cache; fetch a fresh response from the server |
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

Caching is configured via `.WithCache()` on the builder:

```csharp
// Enable caching with defaults
builder.Services.AddTurboHttpClient(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithCache(CachePolicy.Default);
```

Customise the cache size or behaviour:

```csharp
builder.Services.AddTurboHttpClient("api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithCache(new CachePolicy
{
    MaxEntries = 500,             // maximum number of cached responses (default: 1000)
    MaxBodyBytes = 512 * 1024,    // maximum body size to cache, in bytes (default: 50 MiB)
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

## Sharing a Cache Store

By default each named client gets its own `CacheStore`. To share a single store across multiple named clients — for example, to deduplicate in-flight requests across services — create a `CacheStore` once and pass it directly:

```csharp
var sharedStore = new CacheStore(CachePolicy.Default);

builder.Services.AddTurboHttpClient("client-a", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithCache(sharedStore);

builder.Services.AddTurboHttpClient("client-b", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithCache(sharedStore);
```

`CacheStore` is thread-safe and designed for concurrent access from multiple clients.

See the [Configuration guide](./configuration) for more details on cache setup.
