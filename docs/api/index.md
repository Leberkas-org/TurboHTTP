# API Reference

TurboHTTP exposes a small, focused public API. The primary entry point is `ITurboHttpClient`, obtained via `ITurboHttpClientFactory`.

## ITurboHttpClientFactory

```csharp
public interface ITurboHttpClientFactory
{
    ITurboHttpClient CreateClient(string name);
}
```

Registered via dependency injection. Resolve a named client by passing the name used at registration:

```csharp
// Default (unnamed) client — registered with AddTurboHttpClient()
var client = factory.CreateClient();          // extension method: CreateClient(string.Empty)

// Named client — registered with AddTurboHttpClient("search", ...)
var searchClient = factory.CreateClient("search");
```

See [Configuration guide](/guide/configuration) for DI setup and named client registration.

---

## ITurboHttpClient

```csharp
public interface ITurboHttpClient : IDisposable
{
    Uri? BaseAddress { get; set; }
    HttpRequestHeaders DefaultRequestHeaders { get; }
    Version DefaultRequestVersion { get; set; }
    HttpVersionPolicy DefaultVersionPolicy { get; set; }
    TimeSpan Timeout { get; set; }
    long MaxResponseContentBufferSize { get; set; }

    ChannelWriter<HttpRequestMessage> Requests { get; }
    ChannelReader<HttpResponseMessage> Responses { get; }

    void CancelPendingRequests();
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}
```

### BaseAddress

Base address used to resolve relative URIs. When set, relative URIs in `SendAsync` and `Requests` are combined with this base:

```csharp
client.BaseAddress = new Uri("https://api.example.com/v2/");

// Resolves to https://api.example.com/v2/users/42
var response = await client.SendAsync(
    new HttpRequestMessage(HttpMethod.Get, "users/42"), ct);
```

### DefaultRequestHeaders

Headers added to every outgoing request. Useful for authentication tokens, `User-Agent`, or `Accept`:

```csharp
client.DefaultRequestHeaders.Add("User-Agent", "MyApp/1.0");
client.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", token);
```

### DefaultRequestVersion and DefaultVersionPolicy

Controls which HTTP version is used. `DefaultRequestVersion` sets the preferred version; `DefaultVersionPolicy` controls the negotiation behaviour:

```csharp
// Force HTTP/2 (fails if server doesn't support it)
client.DefaultRequestVersion = HttpVersion.Version20;
client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;

// Prefer HTTP/2, fall back to HTTP/1.1
client.DefaultRequestVersion = HttpVersion.Version20;
client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
```

Per-request version overrides are also supported via `HttpRequestMessage.Version` and `HttpRequestMessage.VersionPolicy`.

See [HTTP/2 & Multiplexing guide](/guide/http2) for multiplexing details.

### Timeout

Per-request timeout applied by `SendAsync`. Defaults to 60 seconds. Does not affect the channel-based API:

```csharp
client.Timeout = TimeSpan.FromSeconds(30);

// Times out after 30 s:
var response = await client.SendAsync(request, CancellationToken.None);
```

### MaxResponseContentBufferSize

Maximum number of bytes that the response body is buffered to. Requests that exceed this limit throw `HttpRequestException`:

```csharp
// Limit response bodies to 10 MiB
client.MaxResponseContentBufferSize = 10 * 1024 * 1024;
```

### Requests and Responses channels

High-throughput channel API for submitting many requests without waiting for individual responses:

```csharp
// Producer: write multiple requests without awaiting responses
await client.Requests.WriteAsync(new HttpRequestMessage(HttpMethod.Get, "/item/1"), ct);
await client.Requests.WriteAsync(new HttpRequestMessage(HttpMethod.Get, "/item/2"), ct);

// Consumer: read responses as they arrive
await foreach (var response in client.Responses.ReadAllAsync(ct))
{
    Console.WriteLine($"{response.RequestMessage?.RequestUri} → {response.StatusCode}");
}
```

Requests are matched to responses in submission order (HTTP/1.x) or by stream ID (HTTP/2). See [Getting Started guide](/guide/#high-throughput-usage) for batch patterns and backpressure.

### CancelPendingRequests

Cancels all in-flight `SendAsync` calls and clears the pending request map. Does not affect the channel-based API:

```csharp
// Cancel everything in-flight (e.g., on application shutdown)
client.CancelPendingRequests();
```

### SendAsync

Sends a single request and returns the response. Internally writes to `Requests` and awaits the matching response. The call respects `Timeout` and the provided `CancellationToken`:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
var request = new HttpRequestMessage(HttpMethod.Post, "/orders")
{
    Content = JsonContent.Create(new { ProductId = 42, Quantity = 1 })
};
var response = await client.SendAsync(request, cts.Token);
response.EnsureSuccessStatusCode();
var order = await response.Content.ReadFromJsonAsync<Order>(cts.Token);
```

---

## TurboClientOptions

```csharp
public sealed class TurboClientOptions
{
    // Base address
    public Uri? BaseAddress { get; set; }

    // Version-specific options (nested)
    public Http1Options Http1 { get; set; }    // HTTP/1.x settings
    public Http2Options Http2 { get; set; }    // HTTP/2 settings
    public Http3Options Http3 { get; set; }    // HTTP/3 settings

    // Connection pool
    public TimeSpan ConnectTimeout { get; set; }              // Default: 15 s
    public TimeSpan PooledConnectionIdleTimeout { get; set; } // Default: 90 s
    public TimeSpan PooledConnectionLifetime { get; set; }    // Default: infinite
    public uint MaxEndpointSubstreams { get; set; }           // Default: 256

    // TLS
    public bool DangerousAcceptAnyServerCertificate { get; set; }
    public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; set; }
    public X509CertificateCollection? ClientCertificates { get; set; }
    public SslProtocols EnabledSslProtocols { get; set; }

    // Proxy
    public bool UseProxy { get; set; }                        // Default: true
    public IWebProxy? Proxy { get; set; }
    public ICredentials? DefaultProxyCredentials { get; set; }

    // Authentication
    public ICredentials? Credentials { get; set; }
    public bool PreAuthenticate { get; set; }                 // Default: false
}
```

### Connection options

| Property | Default | Description |
|---|---|---|
| `BaseAddress` | `null` | Base URI for relative requests |
| `ConnectTimeout` | `15 s` | TCP/QUIC connection timeout |
| `PooledConnectionIdleTimeout` | `90 s` | How long idle connections are kept in the pool |
| `PooledConnectionLifetime` | `infinite` | Maximum lifetime of a pooled connection |
| `MaxEndpointSubstreams` | `256` | Max concurrently active endpoint substreams |

Per-version connection limits are configured on the nested options objects:

| Property | Default | Description |
|---|---|---|
| `Http1.MaxConnectionsPerServer` | `6` | Max concurrent HTTP/1.x connections per host |
| `Http1.MaxPipelineDepth` | `16` | Max pipelined requests per HTTP/1.1 connection |
| `Http2.MaxConnectionsPerServer` | `6` | Max concurrent HTTP/2 connections per host |
| `Http2.MaxConcurrentStreams` | `100` | Max concurrent streams per HTTP/2 connection |
| `Http3.MaxConnectionsPerServer` | `4` | Max concurrent QUIC connections per host |

See [Connection Pooling guide](/guide/connection-pooling) for pool lifecycle details.

### HTTP/1.x options

| Property | Default | Description |
|---|---|---|
| `Http1.MaxConnectionsPerServer` | `6` | Max concurrent TCP connections per host |
| `Http1.MaxPipelineDepth` | `16` | Max pipelined requests per connection |
| `Http1.MaxBatchWeight` | `65536` (64 KiB) | Max batch weight for request encoding |
| `Http1.MaxResponseHeadersLength` | `64` (KB) | Max response header size |
| `Http1.MaxReconnectAttempts` | `3` | Max reconnect attempts on connection drop |
| `Http1.MaxResponseDrainSize` | `1048576` (1 MB) | Max bytes to drain from incomplete response |
| `Http1.ResponseDrainTimeout` | `2 s` | Timeout for draining incomplete response body |

### HTTP/2 options

| Property | Default | Description |
|---|---|---|
| `Http2.MaxConnectionsPerServer` | `6` | Max concurrent TCP connections per host |
| `Http2.MaxConcurrentStreams` | `100` | Max concurrent streams per connection |
| `Http2.InitialConnectionWindowSize` | `67108864` (64 MB) | Connection-level flow control window |
| `Http2.InitialStreamWindowSize` | `65535` | Per-stream flow control window |
| `Http2.MaxFrameSize` | `16384` (16 KiB) | Max frame payload size |
| `Http2.HeaderTableSize` | `4096` | HPACK dynamic table size |
| `Http2.MaxBatchWeight` | `262144` (256 KiB) | Max batch weight for frame encoding |
| `Http2.MaxReconnectAttempts` | `3` | Max reconnect attempts on connection drop |
| `Http2.KeepAlivePingDelay` | `infinite` | Delay before sending keep-alive PING |
| `Http2.KeepAlivePingTimeout` | `20 s` | Timeout for PING acknowledgment |
| `Http2.KeepAlivePingPolicy` | `Always` | When to send keep-alive PINGs |

### HTTP/3 options

| Property | Default | Description |
|---|---|---|
| `Http3.MaxConnectionsPerServer` | `4` | Max concurrent QUIC connections per host |
| `Http3.QpackMaxTableCapacity` | `4096` | QPACK dynamic table size |
| `Http3.QpackBlockedStreams` | `100` | Max streams blocked waiting for QPACK |
| `Http3.MaxFieldSectionSize` | `65536` (64 KiB) | Max header block size |
| `Http3.IdleTimeout` | `30 s` | QUIC idle timeout |
| `Http3.MaxReconnectAttempts` | `3` | Max reconnect attempts on connection drop |
| `Http3.AllowEarlyData` | `false` | Allow QUIC 0-RTT early data |
| `Http3.AllowConnectionMigration` | `true` | Allow QUIC connection migration |
| `Http3.AllowServerPush` | `false` | Allow server push via PUSH_PROMISE |
| `Http3.MaxBatchWeight` | `262144` (256 KiB) | Max batch weight for frame encoding |
| `Http3.EnableAltSvcDiscovery` | `false` | Auto-discover HTTP/3 via Alt-Svc headers |

### TLS options

| Property | Default | Description |
|---|---|---|
| `DangerousAcceptAnyServerCertificate` | `false` | Skip all certificate validation — dev/test only |
| `ServerCertificateValidationCallback` | Accept valid certs | Custom TLS certificate validation |
| `ClientCertificates` | `null` | Client certificates for mutual TLS |
| `EnabledSslProtocols` | `SslProtocols.None` (OS default) | TLS protocol versions to permit |

```csharp
// Mutual TLS with a client certificate
options.ClientCertificates = new X509CertificateCollection
{
    X509CertificateLoader.LoadPkcs12FromFile("client.pfx", password)
};
```

### HTTP/2 frame size

```csharp
// Increase frame size for large binary payloads (default: 16 KiB, max: 16 MiB)
options.Http2.MaxFrameSize = 4 * 1024 * 1024; // 4 MiB
```

See [HTTP/2 & Multiplexing guide](/guide/http2) for multiplexing configuration and [HTTP/3 & QUIC guide](/guide/http3) for QUIC-specific settings.

---

## Feature Options

Feature options configure optional features and are applied via the builder API, not through `TurboClientOptions`. All `With*` methods accept an optional configuration delegate; calling them without arguments enables the feature with its defaults. See [Configuration guide](/guide/configuration) for builder usage.

### RedirectOptions

```csharp
public sealed class RedirectOptions
{
    public int MaxRedirects { get; set; }              // Default: 10
    public bool AllowHttpsToHttpDowngrade { get; set; } // Default: false
}
```

```csharp
// Follow redirects with defaults
builder.Services.AddTurboHttpClient("api", ...).WithRedirect();

// Custom limit
builder.Services.AddTurboHttpClient("api", ...).WithRedirect(r => { r.MaxRedirects = 3; });

// Disable redirect following (default — no .WithRedirect() call)
```

See [Redirects guide](/guide/redirects) for method rewriting and security details.

### RetryOptions

```csharp
public sealed class RetryOptions
{
    public int MaxRetries { get; set; }         // Default: 3
    public bool RespectRetryAfter { get; set; } // Default: true
}
```

```csharp
// Default retries (3 attempts)
builder.Services.AddTurboHttpClient("api", ...).WithRetry();

// Aggressive retry
builder.Services.AddTurboHttpClient("api", ...)
    .WithRetry(r => { r.MaxRetries = 5; r.RespectRetryAfter = false; });
```

See [Automatic Retries guide](/guide/retries) for which methods and status codes are retried.

### CacheOptions

```csharp
public sealed class CacheOptions
{
    public int MaxEntries { get; set; }     // Default: 1000
    public long MaxBodyBytes { get; set; }  // Default: 52_428_800 (50 MiB)
    public bool SharedCache { get; set; }   // Default: false (private cache)
}
```

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

See [HTTP Caching guide](/guide/caching) for freshness rules and conditional requests.

### CompressionOptions

```csharp
public sealed class CompressionOptions
{
    public string Encoding { get; set; }         // Default: "gzip"
    public long MinBodySizeBytes { get; set; }   // Default: 1024
}
```

```csharp
builder.Services.AddTurboHttpClient("api", ...)
    .WithRequestCompression(c => { c.Encoding = "br"; c.MinBodySizeBytes = 4096; });
```

### Expect100Options

```csharp
public sealed class Expect100Options
{
    public long MinBodySizeBytes { get; set; } // Default: 1024
}
```

```csharp
builder.Services.AddTurboHttpClient("api", ...)
    .WithExpectContinue(e => { e.MinBodySizeBytes = 8192; });
```

See [Content Encoding guide](/guide/content-encoding) for request compression and Expect: 100-continue.

---

## Extension Points

These types are part of the public API and can be customized via the builder extensions:

| Type | Purpose | Guide |
|---|---|---|
| `CookieJar` | Cookie storage and injection — provided via `.WithCookies()` | [Cookies](/guide/cookies) |
| `CacheStore` | In-memory LRU cache backend — provided via `.WithCache(store)` | [Caching](/guide/caching) |
| `RedirectHandler` | Built-in HTTP redirect handling — controlled via `.WithRedirect()` | [Redirects](/guide/redirects) |
| `RetryEvaluator` | Built-in idempotent method retry — controlled via `.WithRetry()` | [Retries](/guide/retries) |
| `TurboHandler` | Custom request/response middleware — registered via `.AddHandler<T>()` | [Extending the Pipeline](/architecture/extending) |

See the [Configuration guide](/guide/configuration) and [Extending the Pipeline](/architecture/extending) for integration patterns.
