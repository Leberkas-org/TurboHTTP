# Feature Options and Builders

Feature options configure optional features and are applied via the builder API, not through `TurboClientOptions`. All `With*` methods accept an optional configuration delegate; calling them without arguments enables the feature with its defaults. See [Configuration guide](/client/configuration) for builder usage.

## RetryOptions

```csharp
public sealed class RetryOptions
{
    public int MaxRetries { get; set; } = 3;
    public bool RespectRetryAfter { get; set; } = true;
}
```

| Property | Default | Description |
|----------|---------|-------------|
| `MaxRetries` | `3` | Number of retry attempts for idempotent requests |
| `RespectRetryAfter` | `true` | Honor `Retry-After` header in responses |

```csharp
// Default retries (3 attempts)
builder.Services.AddTurboHttpClient("api", ...).WithRetry();

// Aggressive retry
builder.Services.AddTurboHttpClient("api", ...)
    .WithRetry(r => { r.MaxRetries = 5; r.RespectRetryAfter = false; });
```

See [Automatic Retries guide](/client/retries) for which methods and status codes are retried.

---

## CacheOptions

```csharp
public sealed class CacheOptions
{
    public int MaxEntries { get; set; } = 1000;
    public long MaxBodyBytes { get; set; } = 52_428_800;  // 50 MiB
    public bool SharedCache { get; set; }
}
```

| Property | Default | Description |
|----------|---------|-------------|
| `MaxEntries` | `1000` | Max number of responses in the cache |
| `MaxBodyBytes` | `52_428_800` (50 MiB) | Max total size of cached response bodies |
| `SharedCache` | `false` | Whether this is a shared cache (affecting `Cache-Control` directives) |

```csharp
// Enable caching with defaults
builder.Services.AddTurboHttpClient("api", ...).WithCache();

// Smaller cache for constrained environments
builder.Services.AddTurboHttpClient("api", ...)
    .WithCache(c => { c.MaxEntries = 100; c.MaxBodyBytes = 5 * 1024 * 1024; });

// Custom store shared across clients
var sharedStore = new CacheStore();
builder.Services.AddTurboHttpClient("api", ...).WithCache(sharedStore);
```

See [HTTP Caching guide](/client/caching) for freshness rules and conditional requests.

---

## RedirectOptions

```csharp
public sealed class RedirectOptions
{
    public int MaxRedirects { get; set; } = 10;
    public bool AllowHttpsToHttpDowngrade { get; set; }
}
```

| Property | Default | Description |
|----------|---------|-------------|
| `MaxRedirects` | `10` | Max number of redirects to follow |
| `AllowHttpsToHttpDowngrade` | `false` | Allow HTTPS → HTTP redirects (security risk) |

```csharp
// Follow redirects with defaults
builder.Services.AddTurboHttpClient("api", ...).WithRedirect();

// Custom limit
builder.Services.AddTurboHttpClient("api", ...).WithRedirect(r => { r.MaxRedirects = 3; });

// Disable redirect following (don't call .WithRedirect())
```

::: warning
`AllowHttpsToHttpDowngrade` should never be enabled in production. It exists for compatibility with legacy servers and testing only.
:::

See [Redirects guide](/client/redirects) for method rewriting and security details.

---

## CompressionOptions

```csharp
public sealed class CompressionOptions
{
    public string Encoding { get; set; } = "gzip";
    public long MinBodySizeBytes { get; set; } = 1024;
}
```

| Property | Default | Description |
|----------|---------|-------------|
| `Encoding` | `"gzip"` | Compression algorithm ("gzip", "br", "deflate") |
| `MinBodySizeBytes` | `1024` | Don't compress bodies smaller than this |

```csharp
// Request compression with Brotli for large bodies
builder.Services.AddTurboHttpClient("api", ...)
    .WithRequestCompression(c => { c.Encoding = "br"; c.MinBodySizeBytes = 4096; });

// Response decompression (automatic, no configuration needed)
builder.Services.AddTurboHttpClient("api", ...).WithDecompression(enabled: true);
```

See [Content Encoding guide](/client/content-encoding) for request compression and Expect: 100-continue.

---

## Expect100Options

```csharp
public sealed class Expect100Options
{
    public long MinBodySizeBytes { get; set; } = 1024;
}
```

| Property | Default | Description |
|----------|---------|-------------|
| `MinBodySizeBytes` | `1024` | Only use Expect: 100-continue for bodies >= this size |

```csharp
// Enable 100-continue for bodies > 8 KiB
builder.Services.AddTurboHttpClient("api", ...)
    .WithExpectContinue(e => { e.MinBodySizeBytes = 8 * 1024; });
```

See [Content Encoding guide](/client/content-encoding) for Expect: 100-continue details.

---

## Cookies

Cookies are configured via the builder, not through an options class:

```csharp
// Enable cookies with the built-in CookieJar
builder.Services.AddTurboHttpClient("api", ...).WithCookies();

// Custom cookie storage
var customStore = new MyCookieStore();
builder.Services.AddTurboHttpClient("api", ...).WithCookies(customStore);
```

Cookies are automatically extracted from `Set-Cookie` headers, stored by domain, and injected into subsequent requests. See [Cookies guide](/client/cookies) for domain matching and expiration rules.

---

## Builder Extension Methods

All feature options are configured via the `ITurboHttpClientBuilder` interface:

```csharp
public static class TurboHttpClientBuilderExtensions
{
    ITurboHttpClientBuilder WithCookies(this ITurboHttpClientBuilder builder);
    ITurboHttpClientBuilder WithCookies(this ITurboHttpClientBuilder builder, ICookieStore store);
    
    ITurboHttpClientBuilder WithCache(this ITurboHttpClientBuilder builder, Action<CacheOptions>? configure = null);
    ITurboHttpClientBuilder WithCache(this ITurboHttpClientBuilder builder, ICacheStore store, Action<CacheOptions>? configure = null);
    
    ITurboHttpClientBuilder WithRetry(this ITurboHttpClientBuilder builder, Action<RetryOptions>? configure = null);
    
    ITurboHttpClientBuilder WithRedirect(this ITurboHttpClientBuilder builder, Action<RedirectOptions>? configure = null);
    
    ITurboHttpClientBuilder WithDecompression(this ITurboHttpClientBuilder builder, bool enabled = true);
    
    ITurboHttpClientBuilder WithRequestCompression(this ITurboHttpClientBuilder builder, Action<CompressionOptions>? configure = null);
    
    ITurboHttpClientBuilder WithExpectContinue(this ITurboHttpClientBuilder builder, Action<Expect100Options>? configure = null);
    
    ITurboHttpClientBuilder AddHandler<T>(this ITurboHttpClientBuilder builder) where T : TurboHandler;
    
    ITurboHttpClientBuilder UseRequest(this ITurboHttpClientBuilder builder, Func<HttpRequestMessage, HttpRequestMessage> transform);
    
    ITurboHttpClientBuilder UseResponse(this ITurboHttpClientBuilder builder, Func<HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> transform);
}
```

---

## Custom Handlers

The `TurboHandler` base class provides a hook for custom request/response middleware:

```csharp
public abstract class TurboHandler
{
    public virtual HttpRequestMessage ProcessRequest(HttpRequestMessage request) => request;
    public virtual HttpResponseMessage ProcessResponse(HttpRequestMessage original, HttpResponseMessage response) => response;
}
```

Custom handlers are registered via the builder:

```csharp
// Define a custom handler
public class AuthHeaderHandler : TurboHandler
{
    public override HttpRequestMessage ProcessRequest(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetToken());
        return request;
    }
}

// Register it
builder.Services.AddTurboHttpClient("api", ...)
    .AddHandler<AuthHeaderHandler>();
```

For request/response transformations, use `UseRequest` and `UseResponse` for inline lambdas:

```csharp
builder.Services.AddTurboHttpClient("api", ...)
    .UseRequest(req => { req.Headers.Add("X-Custom", "value"); return req; });
```

See [Configuration guide](/client/configuration) for integration patterns and handler composition.

---

## Extension Points

These types are part of the public API and can be customized:

| Type | Purpose | Guide |
|------|---------|-------|
| `CookieJar` | Cookie storage and injection — provided via `.WithCookies()` | [Cookies](/client/cookies) |
| `CacheStore` | In-memory LRU cache backend — provided via `.WithCache(store)` | [Caching](/client/caching) |
| `TurboHandler` | Custom request/response middleware — registered via `.AddHandler<T>()` | [Configuration](/client/configuration) |

See [Configuration guide](/client/configuration) for integration patterns.
