# Configuration

TurboHTTP is configured through `TurboClientOptions` — a mutable class covering connection pool settings and TLS configuration. Features like caching, retries, and redirects are composed separately via the fluent builder API returned by `AddTurboHttpClient`.

## DI Registration

Register TurboHTTP in your ASP.NET Core or generic host application:

```csharp
builder.Services.AddTurboHttpClient(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.ConnectTimeout = TimeSpan.FromSeconds(5);
});
```

Inject `ITurboHttpClientFactory` wherever you need a client:

```csharp
public class MyService(ITurboHttpClientFactory factory)
{
    public async Task<string> FetchAsync(CancellationToken ct)
    {
        var client = factory.CreateClient();
        var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/data"), ct);
        return await response.Content.ReadAsStringAsync(ct);
    }
}
```

## Named Clients

Register multiple clients with different settings, then resolve by name:

```csharp
// Public API — caching and redirect following enabled
builder.Services.AddTurboHttpClient("public-api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithCache(CachePolicy.Default)
.WithRedirect();

// Internal service — aggressive retries
builder.Services.AddTurboHttpClient("internal", options =>
{
    options.BaseAddress = new Uri("http://internal-service:8080");
})
.WithRetry(new RetryPolicy { MaxRetries = 5 });
```

```csharp
var publicClient   = factory.CreateClient("public-api");
var internalClient = factory.CreateClient("internal");
```

## TurboClientOptions Reference

### Base Address

| Property | Type | Default |
|----------|------|---------|
| `BaseAddress` | `Uri?` | `null` |

Relative `HttpRequestMessage` URIs are resolved against this base address. When `null`, all request URIs must be absolute.

```csharp
options.BaseAddress = new Uri("https://api.example.com/v2/");
```

### Connection Pool

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ConnectTimeout` | `TimeSpan` | `00:00:15` | Timeout for establishing a new TCP connection |
| `PooledConnectionIdleTimeout` | `TimeSpan` | `00:01:30` | Time a connection may remain idle before eviction |
| `MaxConnectionsPerServer` | `int` | `6` | Maximum concurrent HTTP/1.x connections per host |
| `MaxPipelineDepth` | `int` | `16` | Maximum pipelined requests per HTTP/1.1 connection |
| `MaxEndpointSubstreams` | `uint` | `256` | Maximum concurrently active endpoint substreams |

`MaxConnectionsPerServer` mirrors the browser default. For workloads that require many parallel HTTP/1.1 requests, raise this value:

```csharp
options.ConnectTimeout = TimeSpan.FromSeconds(5);
options.PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2);
options.MaxConnectionsPerServer = 12;
```

### HTTP/2 Frame Size

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxFrameSize` | `int` | `131072` (128 KiB) | Maximum HTTP/2 frame payload size in bytes |

Increase for workloads with large response bodies to reduce frame fragmentation:

```csharp
options.MaxFrameSize = 512 * 1024; // 512 KiB
```

### TLS / HTTPS

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DangerousAcceptAnyServerCertificate` | `bool` | `false` | Accept all server certificates — dev/test only |
| `ServerCertificateValidationCallback` | `RemoteCertificateValidationCallback?` | Accept `SslPolicyErrors.None` only | Custom certificate validation |
| `ClientCertificates` | `X509CertificateCollection?` | `null` | Client certificates for mutual TLS |
| `EnabledSslProtocols` | `SslProtocols` | `SslProtocols.None` | TLS versions to enable (`None` = OS default) |

```csharp
// Accept a specific self-signed certificate by thumbprint (dev/staging)
options.ServerCertificateValidationCallback = (_, cert, _, errors) =>
    errors == SslPolicyErrors.None
    || cert?.GetCertHashString() == "ABCDEF1234567890";

// Mutual TLS (mTLS) with a client certificate
var cert = X509CertificateLoader.LoadPkcs12FromFile("client.pfx", password);
options.ClientCertificates = new X509CertificateCollection { cert };
```

::: warning
Setting `DangerousAcceptAnyServerCertificate = true` disables all certificate validation. Never use this in production.
:::

## Feature Configuration via Builder

Features are **not** set on `TurboClientOptions`. Instead, they are composed using the builder returned by `AddTurboHttpClient`:

```csharp
builder.Services.AddTurboHttpClient("full-featured", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithRedirect()              // follow redirects (default policy)
.WithRetry(RetryPolicy.Default)   // automatic retries
.WithCookies()               // automatic cookie management
.WithCache(CachePolicy.Default)   // HTTP caching
.WithDecompression()         // gzip/deflate/brotli response decompression
.WithRequestCompression()    // gzip request body compression
.WithExpectContinue();       // Expect: 100-continue for large POST bodies
```

### Redirect following

```csharp
.WithRedirect()                                          // default: max 10 redirects
.WithRedirect(new RedirectPolicy { MaxRedirects = 3 })   // custom limit
```

**`RedirectPolicy` properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxRedirects` | `int` | `10` | Maximum redirects before throwing an exception |
| `AllowHttpsToHttpDowngrade` | `bool` | `false` | Allow redirects from HTTPS to HTTP |

See [Redirects guide](./redirects) for method rewriting and security behaviours.

### Automatic retries

```csharp
.WithRetry(RetryPolicy.Default)                                         // max 3 retries
.WithRetry(new RetryPolicy { MaxRetries = 5, RespectRetryAfter = false })
```

**`RetryPolicy` properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxRetries` | `int` | `3` | Maximum retry attempts per request |
| `RespectRetryAfter` | `bool` | `true` | Honour the server's `Retry-After` header |

See [Automatic Retries guide](./retries) for which methods and status codes trigger retries.

### HTTP caching

```csharp
.WithCache(CachePolicy.Default)
.WithCache(new CachePolicy { MaxEntries = 200, MaxBodyBytes = 5 * 1024 * 1024 })
```

To share a single store across multiple named clients, pass a `CacheStore` directly:

```csharp
var sharedStore = new CacheStore(CachePolicy.Default);

builder.Services.AddTurboHttpClient("client-a", options => { ... }).WithCache(sharedStore);
builder.Services.AddTurboHttpClient("client-b", options => { ... }).WithCache(sharedStore);
```

**`CachePolicy` properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxEntries` | `int` | `1000` | Maximum entries in the LRU store |
| `MaxBodyBytes` | `long` | `52428800` (50 MiB) | Maximum body size per cached response |
| `SharedCache` | `bool` | `false` | When `true`, acts as a shared (proxy) cache |

See [HTTP Caching guide](./caching) for freshness evaluation and conditional request behaviour.

### Cookie management

```csharp
.WithCookies()             // private cookie jar per named client
.WithCookies(sharedJar)    // shared CookieJar across multiple clients
```

See [Cookies guide](./cookies) for session and domain handling.

### Response decompression

```csharp
.WithDecompression()       // advertises Accept-Encoding: gzip, br, deflate and decompresses automatically
.WithDecompression(false)  // disable
```

### Request compression and Expect: 100-continue

```csharp
.WithRequestCompression()                                   // gzip bodies >= 1 KiB
.WithRequestCompression(new CompressionPolicy { Encoding = "br", MinBodySizeBytes = 4096 })

.WithExpectContinue()                                       // Expect: 100-continue for bodies >= 1 KiB
.WithExpectContinue(new Expect100Policy { MinBodySizeBytes = 8192 })
```

See [Content Encoding guide](./content-encoding) for details.

## Complete Example

```csharp
builder.Services.AddTurboHttpClient(options =>
{
    // Transport
    options.BaseAddress = new Uri("https://api.example.com");
    options.ConnectTimeout = TimeSpan.FromSeconds(5);
    options.PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2);
    options.MaxConnectionsPerServer = 10;
})
.WithRedirect()
.WithRetry(RetryPolicy.Default)
.WithCookies()
.WithCache(CachePolicy.Default)
.WithDecompression();
```
