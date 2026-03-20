# API Reference

TurboHttp exposes a small, focused public API. The primary entry point is `ITurboHttpClient`, obtained via `ITurboHttpClientFactory`.

## ITurboHttpClientFactory

```csharp
public interface ITurboHttpClientFactory
{
    ITurboHttpClient CreateClient(Action<TurboClientOptions>? configure = null);
}
```

Create a client with default options or supply a configuration delegate:

```csharp
var client = factory.CreateClient(opts =>
{
    opts = opts with
    {
        BaseAddress = new Uri("https://api.example.com"),
        ConnectTimeout = TimeSpan.FromSeconds(5),
    };
});
```

## ITurboHttpClient

```csharp
public interface ITurboHttpClient
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

### SendAsync — Simple Request/Response

The familiar `Task`-based API for one-shot requests:

```csharp
var request = new HttpRequestMessage(HttpMethod.Get, "/users/42");
var response = await client.SendAsync(request, cancellationToken);
Console.WriteLine(response.StatusCode);
```

### Channel-Based Streaming

For high-throughput scenarios, write requests directly to the `Requests` channel and read responses from `Responses`:

```csharp
// Producer
await client.Requests.WriteAsync(new HttpRequestMessage(HttpMethod.Get, "/item/1"), ct);
await client.Requests.WriteAsync(new HttpRequestMessage(HttpMethod.Get, "/item/2"), ct);
client.Requests.Complete();

// Consumer
await foreach (var response in client.Responses.ReadAllAsync(ct))
{
    Console.WriteLine(response.StatusCode);
}
```

Requests are matched to responses in order (HTTP/1.x FIFO pipelining) or by stream ID (HTTP/2 multiplexing).

## TurboClientOptions

```csharp
public record TurboClientOptions
{
    public Uri? BaseAddress { get; init; }
    public TimeSpan ConnectTimeout { get; init; }       // Default: 10s
    public TimeSpan ReconnectInterval { get; init; }    // Default: 5s
    public TimeSpan IdleTimeout { get; init; }          // Default: 10s
    public int MaxReconnectAttempts { get; init; }      // Default: 10
    public int MaxFrameSize { get; init; }              // Default: 128 KiB

    // TLS
    public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; init; }
    public X509CertificateCollection? ClientCertificates { get; init; }
    public SslProtocols EnabledSslProtocols { get; init; }

    // Policies
    public RedirectPolicy? RedirectPolicy { get; init; }
    public RetryPolicy? RetryPolicy { get; init; }
    public CachePolicy? CachePolicy { get; init; }
    public ConnectionPolicy? ConnectionPolicy { get; init; }
}
```

### Policy Types

| Type | Purpose | RFC |
|------|---------|-----|
| `RedirectPolicy` | Controls automatic redirect following (max hops, HTTPS→HTTP protection) | RFC 9110 §15.4 |
| `RetryPolicy` | Controls idempotency-based retry and `Retry-After` handling | RFC 9110 §9.2 |
| `CachePolicy` | Enables in-memory LRU response caching with Vary support | RFC 9111 |
| `ConnectionPolicy` | Per-host connection limits and keep-alive settings | RFC 9112 §9 |

## Protocol Version Selection

Set `DefaultRequestVersion` and `DefaultVersionPolicy` to control which HTTP version is used:

```csharp
// Force HTTP/2
client.DefaultRequestVersion = HttpVersion.Version20;
client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;

// Negotiate (HTTP/1.1 or HTTP/2 via ALPN)
client.DefaultRequestVersion = HttpVersion.Version20;
client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
```

Per-request version overrides are also supported via `HttpRequestMessage.Version`.

## Key Protocol Types

These types are used by the pipeline and policies but are not directly instantiated by library consumers:

| Type | Namespace | Purpose |
|------|-----------|---------|
| `HpackEncoder` / `HpackDecoder` | `TurboHttp.Protocol.RFC7541` | HPACK header compression for HTTP/2 |
| `RedirectHandler` | `TurboHttp.Protocol.RFC9110` | Redirect following logic |
| `RetryEvaluator` | `TurboHttp.Protocol.RFC9110` | Retry eligibility evaluation |
| `CookieJar` | `TurboHttp.Protocol.RFC6265` | Cookie storage and injection |
| `HttpCacheStore` | `TurboHttp.Protocol.RFC9111` | In-memory response cache |
| `ConnectionHandle` | `TurboHttp.IO` | Bundles channel writers/readers for a TCP connection |
