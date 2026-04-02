# API Reference

TurboHttp exposes a small, focused public API. The primary entry point is `ITurboHttpClient`, obtained via `ITurboHttpClientFactory`.

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

Per-request timeout applied by `SendAsync`. Defaults to 100 seconds (matching `HttpClient`). Does not affect the channel-based API:

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

    // Connection pool
    public TimeSpan ConnectTimeout { get; set; }            // Default: 15 s
    public TimeSpan PooledConnectionIdleTimeout { get; set; } // Default: 90 s
    public int MaxConnectionsPerServer { get; set; }        // Default: 6
    public int MaxPipelineDepth { get; set; }               // Default: 16
    public uint MaxEndpointSubstreams { get; set; }         // Default: 256

    // HTTP/2
    public int MaxFrameSize { get; set; }                   // Default: 128 KiB

    // TLS
    public bool DangerousAcceptAnyServerCertificate { get; set; }
    public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; set; }
    public X509CertificateCollection? ClientCertificates { get; set; }
    public SslProtocols EnabledSslProtocols { get; set; }
}
```

### Connection options

| Property | Default | Description |
|---|---|---|
| `BaseAddress` | `null` | Base URI for relative requests |
| `ConnectTimeout` | `15 s` | TCP connection timeout |
| `PooledConnectionIdleTimeout` | `90 s` | How long idle connections are kept in the pool |
| `MaxConnectionsPerServer` | `6` | Max concurrent HTTP/1.x connections per host |
| `MaxPipelineDepth` | `16` | Max pipelined requests per HTTP/1.1 connection |
| `MaxEndpointSubstreams` | `256` | Max concurrently active endpoint substreams |

See [Connection Pooling guide](/guide/connection-pooling) for pool lifecycle details.

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
// Increase frame size for large binary payloads (max: 16 MiB)
options.MaxFrameSize = 4 * 1024 * 1024; // 4 MiB
```

See [HTTP/2 & Multiplexing guide](/guide/http2) for multiplexing configuration.

---

## Policy Types

Policies configure optional features and are applied via the builder API, not through `TurboClientOptions`. See [Configuration guide](/guide/configuration) for builder usage.

### RedirectPolicy

```csharp
public sealed record RedirectPolicy
{
    public static readonly RedirectPolicy Default; // MaxRedirects = 10
    public int MaxRedirects { get; init; }         // Default: 10
    public bool AllowHttpsToHttpDowngrade { get; init; } // Default: false
}
```

```csharp
// Custom redirect limit
builder.Services.AddTurboHttpClient("api", ...).WithRedirect(new RedirectPolicy { MaxRedirects = 3 });

// Disable redirect following (default — no .WithRedirect() call)
```

See [Redirects guide](/guide/redirects) for method rewriting and security details.

### RetryPolicy

```csharp
public sealed record RetryPolicy
{
    public static readonly RetryPolicy Default; // MaxRetries = 3, RespectRetryAfter = true
    public int MaxRetries { get; init; }         // Default: 3
    public bool RespectRetryAfter { get; init; } // Default: true
}
```

```csharp
// Aggressive retry
builder.Services.AddTurboHttpClient("api", ...)
    .WithRetry(new RetryPolicy { MaxRetries = 5, RespectRetryAfter = false });
```

See [Automatic Retries guide](/guide/retries) for which methods and status codes are retried.

### CachePolicy

```csharp
public sealed record CachePolicy
{
    public static CachePolicy Default { get; }  // MaxEntries = 1000, MaxBodyBytes = 50 MiB
    public int MaxEntries { get; init; }         // Default: 1000
    public long MaxBodyBytes { get; init; }      // Default: 52_428_800 (50 MiB)
    public bool SharedCache { get; init; }       // Default: false (private cache)
}
```

```csharp
// Enable caching with defaults
builder.Services.AddTurboHttpClient("api", ...).WithCache(CachePolicy.Default);

// Smaller cache for constrained environments
builder.Services.AddTurboHttpClient("api", ...)
    .WithCache(new CachePolicy { MaxEntries = 100, MaxBodyBytes = 5 * 1024 * 1024 });
```

See [HTTP Caching guide](/guide/caching) for freshness rules and conditional requests.

### CompressionPolicy

```csharp
public sealed record CompressionPolicy
{
    public static readonly CompressionPolicy Default; // Encoding = "gzip", MinBodySizeBytes = 1024
    public string Encoding { get; init; }             // Default: "gzip"
    public long MinBodySizeBytes { get; init; }       // Default: 1024
}
```

```csharp
builder.Services.AddTurboHttpClient("api", ...)
    .WithRequestCompression(new CompressionPolicy { Encoding = "br", MinBodySizeBytes = 4096 });
```

### Expect100Policy

```csharp
public sealed record Expect100Policy
{
    public static readonly Expect100Policy Default; // MinBodySizeBytes = 1024
    public long MinBodySizeBytes { get; init; }     // Default: 1024
}
```

```csharp
builder.Services.AddTurboHttpClient("api", ...)
    .WithExpectContinue(new Expect100Policy { MinBodySizeBytes = 8192 });
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
