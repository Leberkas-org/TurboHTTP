# Configuration

TurboHttp is configured through `TurboClientOptions` — a record that covers everything from connection timeouts to caching and redirect behaviour. All properties are `init`-only, so a configuration is immutable once created.

## DI Registration

Register TurboHttp with your ASP.NET Core or generic host application:

```csharp
builder.Services.AddTurboHttpClient(options =>
{
    options = options with
    {
        BaseAddress = new Uri("https://api.example.com"),
        ConnectTimeout = TimeSpan.FromSeconds(5),
        RetryPolicy = RetryPolicy.Default,
        CachePolicy = CachePolicy.Default,
        RedirectPolicy = RedirectPolicy.Default,
        ConnectionPolicy = ConnectionPolicy.Default,
    };
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

## Named Clients (Per-Call Overrides)

`CreateClient` accepts an optional delegate to override individual options for a single client instance. The factory's registered options are the base; the delegate mutates a shallow copy:

```csharp
// Standard client — uses registered defaults
var defaultClient = factory.CreateClient();

// Search client — custom base address, no cache
var searchClient = factory.CreateClient(o =>
{
    o = o with
    {
        BaseAddress = new Uri("https://search.example.com"),
        CachePolicy = null,            // disable cache for this client
        ConnectTimeout = TimeSpan.FromSeconds(3),
    };
});
```

## TurboClientOptions Reference

### Base Address

| Property | Type | Default |
|----------|------|---------|
| `BaseAddress` | `Uri?` | `null` |

Relative `HttpRequestMessage` URIs are resolved against this base address. When `null`, all request URIs must be absolute.

```csharp
options = options with { BaseAddress = new Uri("https://api.example.com/v2/") };
```

### Connection Timeouts

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ConnectTimeout` | `TimeSpan` | `00:00:10` | Timeout for establishing a new TCP connection |
| `ReconnectInterval` | `TimeSpan` | `00:00:05` | How long to wait between reconnection attempts when a connection drops unexpectedly — useful for controlling how aggressively TurboHttp retries failed connections |
| `IdleTimeout` | `TimeSpan` | `00:00:10` | Time a pooled connection may remain idle before eviction |
| `MaxReconnectAttempts` | `int` | `10` | How many times TurboHttp tries to re-establish a dropped connection before giving up — increase this for unreliable networks, decrease it to fail fast |

```csharp
options = options with
{
    ConnectTimeout = TimeSpan.FromSeconds(3),
    ReconnectInterval = TimeSpan.FromSeconds(2),
    IdleTimeout = TimeSpan.FromMinutes(1),
    MaxReconnectAttempts = 5,
};
```

### HTTP/2 Frame Size

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxFrameSize` | `int` | `131072` (128 KiB) | Maximum HTTP/2 frame payload size in bytes |

Increase this for workloads with large response bodies to reduce frame fragmentation:

```csharp
options = options with { MaxFrameSize = 512 * 1024 }; // 512 KiB
```

### TLS / HTTPS

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ServerCertificateValidationCallback` | `RemoteCertificateValidationCallback?` | Accept only `SslPolicyErrors.None` | Callback to validate the server's TLS certificate |
| `ClientCertificates` | `X509CertificateCollection?` | `null` | Client certificates presented during TLS handshake |
| `EnabledSslProtocols` | `SslProtocols` | `SslProtocols.None` | TLS protocol versions to enable (`None` = OS default) |

```csharp
// Accept a specific self-signed certificate by thumbprint (dev/staging)
options = options with
{
    ServerCertificateValidationCallback = (_, cert, _, errors) =>
        errors == SslPolicyErrors.None
        || cert?.GetCertHashString() == "ABCDEF1234567890",
};

// Mutual TLS (mTLS) with a client certificate
var cert = X509CertificateLoader.LoadPkcs12FromFile("client.pfx", password);
options = options with
{
    ClientCertificates = new X509CertificateCollection { cert },
};
```

> **Warning:** Setting `ServerCertificateValidationCallback` to always return `true` disables certificate validation. Never do this in production.

### Redirect Policy

| Property | Type | Default |
|----------|------|---------|
| `RedirectPolicy` | `RedirectPolicy?` | `null` (redirects disabled) |

Set to `RedirectPolicy.Default` or a custom instance to enable automatic redirect following:

```csharp
// Enable redirects with defaults (max 10, no HTTPS→HTTP downgrade)
options = options with { RedirectPolicy = RedirectPolicy.Default };

// Custom: allow more redirects
options = options with
{
    RedirectPolicy = new RedirectPolicy { MaxRedirects = 20 },
};
```

**`RedirectPolicy` properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxRedirects` | `int` | `10` | Maximum redirects before throwing an exception |
| `AllowHttpsToHttpDowngrade` | `bool` | `false` | Allow redirects from HTTPS to HTTP (disabled for security) |

See [Redirects guide](./redirects) for details on method rewriting and security behaviours.

### Retry Policy

| Property | Type | Default |
|----------|------|---------|
| `RetryPolicy` | `RetryPolicy?` | `null` (retries disabled) |

Set to `RetryPolicy.Default` or a custom instance to enable automatic retries for idempotent requests:

```csharp
// Enable retries with defaults (max 3, respects Retry-After)
options = options with { RetryPolicy = RetryPolicy.Default };

// Custom: more aggressive retrying, ignore Retry-After
options = options with
{
    RetryPolicy = new RetryPolicy
    {
        MaxRetries = 5,
        RespectRetryAfter = false,
    },
};
```

**`RetryPolicy` properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxRetries` | `int` | `3` | Maximum retry attempts per request |
| `RespectRetryAfter` | `bool` | `true` | Honour the server's `Retry-After` response header |

See [Automatic Retries guide](./retries) for which methods and status codes trigger retries.

### Cache Policy

| Property | Type | Default |
|----------|------|---------|
| `CachePolicy` | `CachePolicy?` | `null` (caching disabled) |

Set to `CachePolicy.Default` or a custom instance to enable the in-memory response cache:

```csharp
// Enable caching with defaults (1 000 entries, 50 MiB body limit, private cache)
options = options with { CachePolicy = CachePolicy.Default };

// Custom: smaller cache for constrained environments
options = options with
{
    CachePolicy = new CachePolicy
    {
        MaxEntries = 200,
        MaxBodyBytes = 5 * 1024 * 1024, // 5 MiB
    },
};
```

**`CachePolicy` properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxEntries` | `int` | `1000` | Maximum number of entries in the LRU store |
| `MaxBodyBytes` | `long` | `52428800` (50 MiB) | Maximum body size per cached response |
| `SharedCache` | `bool` | `false` | When `true`, behaves as a shared (proxy) cache — `s-maxage` honoured, `private` responses not stored |

See [HTTP Caching guide](./caching) for freshness evaluation and conditional request behaviour.

### Connection Policy

| Property | Type | Default |
|----------|------|---------|
| `ConnectionPolicy` | `ConnectionPolicy?` | `null` (default limits apply) |

Controls per-host connection pool limits and HTTP/2 multiplexing:

```csharp
// Use defaults (6 connections per host, HTTP/2 multiplexing enabled)
options = options with { ConnectionPolicy = ConnectionPolicy.Default };

// Custom: higher limit for an API that requires many parallel requests
options = options with
{
    ConnectionPolicy = new ConnectionPolicy
    {
        MaxConnectionsPerHost = 20,
        AllowHttp2Multiplexing = true,
    },
};
```

**`ConnectionPolicy` properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxConnectionsPerHost` | `int` | `6` | Maximum simultaneous HTTP/1.x connections per host |
| `AllowHttp2Multiplexing` | `bool` | `true` | HTTP/2 connections are multiplexed — the per-host limit does not apply |

See [Connection Pooling guide](./connection-pooling) for pool lifecycle and eviction details.

## Complete Example

```csharp
builder.Services.AddTurboHttpClient(options =>
{
    options = options with
    {
        // Base address for relative URIs
        BaseAddress = new Uri("https://api.example.com"),

        // Connection management
        ConnectTimeout = TimeSpan.FromSeconds(5),
        ReconnectInterval = TimeSpan.FromSeconds(2),
        IdleTimeout = TimeSpan.FromMinutes(2),
        MaxReconnectAttempts = 5,

        // Automatic redirect following
        RedirectPolicy = RedirectPolicy.Default,

        // Automatic retries for idempotent requests
        RetryPolicy = RetryPolicy.Default,

        // In-memory response caching
        CachePolicy = CachePolicy.Default,

        // Per-host connection limits
        ConnectionPolicy = ConnectionPolicy.Default,
    };
});
```
