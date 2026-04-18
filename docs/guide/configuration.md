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
.WithCache()
.WithRedirect();

// Internal service — aggressive retries
builder.Services.AddTurboHttpClient("internal", options =>
{
    options.BaseAddress = new Uri("http://internal-service:8080");
})
.WithRetry(retry => { retry.MaxRetries = 5; });
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
| `PooledConnectionLifetime` | `TimeSpan` | `infinite` | Maximum lifetime of a pooled connection |
| `MaxEndpointSubstreams` | `uint` | `256` | Maximum concurrently active endpoint substreams |

```csharp
options.ConnectTimeout = TimeSpan.FromSeconds(5);
options.PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2);
options.PooledConnectionLifetime = TimeSpan.FromMinutes(10);
```

### HTTP/1.x Options

Per-version connection and protocol settings are configured on nested sub-objects:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Http1.MaxConnectionsPerServer` | `int` | `6` | Maximum concurrent HTTP/1.x connections per host |
| `Http1.MaxPipelineDepth` | `int` | `16` | Maximum pipelined requests per HTTP/1.1 connection |
| `Http1.MaxBatchWeight` | `int` | `65536` (64 KiB) | Max batch weight for request encoding |
| `Http1.MaxResponseHeadersLength` | `int` | `64` (KB) | Max response header size |
| `Http1.MaxReconnectAttempts` | `int` | `3` | Max reconnect attempts on connection drop |

```csharp
options.Http1.MaxConnectionsPerServer = 12;  // raise for parallel HTTP/1.1
options.Http1.MaxPipelineDepth = 32;
```

### HTTP/2 Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Http2.MaxConnectionsPerServer` | `int` | `6` | Maximum concurrent HTTP/2 connections per host |
| `Http2.MaxConcurrentStreams` | `int` | `100` | Maximum concurrent streams per connection |
| `Http2.MaxFrameSize` | `int` | `16384` (16 KiB) | Maximum HTTP/2 frame payload size |
| `Http2.HeaderTableSize` | `int` | `4096` | HPACK dynamic table size |
| `Http2.MaxBatchWeight` | `int` | `262144` (256 KiB) | Max batch weight for frame encoding |
| `Http2.MaxReconnectAttempts` | `int` | `3` | Max reconnect attempts on connection drop |

Increase frame size for workloads with large response bodies to reduce framing overhead:

```csharp
options.Http2.MaxFrameSize = 4 * 1024 * 1024; // 4 MiB (default: 16 KiB)
```

### HTTP/3 Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Http3.MaxConnectionsPerServer` | `int` | `4` | Maximum concurrent QUIC connections per host |
| `Http3.QpackMaxTableCapacity` | `int` | `4096` | QPACK dynamic table size |
| `Http3.QpackBlockedStreams` | `int` | `100` | Max streams blocked waiting for QPACK |
| `Http3.MaxFieldSectionSize` | `int` | `65536` (64 KiB) | Max header block size |
| `Http3.IdleTimeout` | `TimeSpan` | `00:00:30` | QUIC idle timeout |
| `Http3.MaxReconnectAttempts` | `int` | `3` | Max reconnect attempts on connection drop |
| `Http3.AllowEarlyData` | `bool` | `false` | Allow QUIC 0-RTT early data |
| `Http3.AllowConnectionMigration` | `bool` | `true` | Allow QUIC connection migration |
| `Http3.AllowServerPush` | `bool` | `false` | Allow server push via PUSH_PROMISE |
| `Http3.MaxBatchWeight` | `int` | `262144` (256 KiB) | Max batch weight for frame encoding |
| `Http3.EnableAltSvcDiscovery` | `bool` | `false` | Auto-discover HTTP/3 via Alt-Svc headers |

See [HTTP/3 & QUIC guide](./http3) for QUIC-specific configuration and Alt-Svc discovery.

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
.WithRedirect()              // follow redirects (default settings)
.WithRetry()                 // automatic retries
.WithCookies()               // automatic cookie management
.WithCache()                 // HTTP caching
.WithDecompression()         // gzip/deflate/brotli response decompression
.WithRequestCompression()    // gzip request body compression
.WithExpectContinue();       // Expect: 100-continue for large POST bodies
```

### Redirect following

```csharp
.WithRedirect()                                                          // default: max 10 redirects
.WithRedirect(r => { r.MaxRedirects = 3; })                              // custom limit
```

**`RedirectOptions` properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxRedirects` | `int` | `10` | Maximum redirects before throwing an exception |
| `AllowHttpsToHttpDowngrade` | `bool` | `false` | Allow redirects from HTTPS to HTTP |

See [Redirects guide](./redirects) for method rewriting and security behaviours.

### Automatic retries

```csharp
.WithRetry()                                                              // max 3 retries
.WithRetry(r => { r.MaxRetries = 5; r.RespectRetryAfter = false; })
```

**`RetryOptions` properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxRetries` | `int` | `3` | Maximum retry attempts per request |
| `RespectRetryAfter` | `bool` | `true` | Honour the server's `Retry-After` header |

See [Automatic Retries guide](./retries) for which methods and status codes trigger retries.

### HTTP caching

```csharp
.WithCache()
.WithCache(c => { c.MaxEntries = 200; c.MaxBodyBytes = 5 * 1024 * 1024; })
```

To share a single store across multiple named clients, pass a `CacheStore` directly:

```csharp
var sharedStore = new CacheStore();

builder.Services.AddTurboHttpClient("client-a", options => { ... }).WithCache(sharedStore);
builder.Services.AddTurboHttpClient("client-b", options => { ... }).WithCache(sharedStore);
```

**`CacheOptions` properties:**

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
.WithRequestCompression()                                                              // gzip bodies >= 1 KiB
.WithRequestCompression(c => { c.Encoding = "br"; c.MinBodySizeBytes = 4096; })

.WithExpectContinue()                                                                  // Expect: 100-continue for bodies >= 1 KiB
.WithExpectContinue(e => { e.MinBodySizeBytes = 8192; })
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

    // Per-version tuning
    options.Http1.MaxConnectionsPerServer = 10;
    options.Http2.MaxConcurrentStreams = 200;
})
.WithRedirect()
.WithRetry()
.WithCookies()
.WithCache()
.WithDecompression();
```
