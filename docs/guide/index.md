# Getting Started

## Installation

Add TurboHttp to your .NET project:

```bash
dotnet add package TurboHttp
```

**Requirements:** .NET 10.0 or later.

## Basic Usage

### Simple Request

```csharp
using TurboHttp.Client;
using System.Net.Http;

await using var client = TurboHttpClientFactory.Create(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.DefaultRequestVersion = HttpVersion.Version20;
});

var request = new HttpRequestMessage(HttpMethod.Get, "/users");
var response = await client.SendAsync(request);

Console.WriteLine($"Status: {response.StatusCode}");
var body = await response.Content.ReadAsStringAsync();
Console.WriteLine(body);
```

### Channel-based API

For high-throughput scenarios, use the channel-based API directly:

```csharp
await using var client = TurboHttpClientFactory.Create(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
});

// Write requests
await client.RequestWriter.WriteAsync(new HttpRequestMessage(HttpMethod.Get, "/ping"));

// Read responses
var response = await client.ResponseReader.ReadAsync();
Console.WriteLine($"Status: {response.StatusCode}");
```

## Configuration

### HTTP Version

```csharp
var client = TurboHttpClientFactory.Create(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");

    // Force HTTP/2
    options.DefaultRequestVersion = HttpVersion.Version20;

    // Or force HTTP/1.1
    options.DefaultRequestVersion = HttpVersion.Version11;
});
```

### Default Headers

```csharp
var client = TurboHttpClientFactory.Create(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.DefaultRequestHeaders.Add("Authorization", "Bearer <token>");
    options.DefaultRequestHeaders.Add("Accept", "application/json");
});
```

### Per-Host Connection Limits

```csharp
var client = TurboHttpClientFactory.Create(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.MaxConnectionsPerHost = 6; // default: 8
});
```

### Timeout and Cancellation

TurboHttp respects `CancellationToken` on every async call:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

var request = new HttpRequestMessage(HttpMethod.Get, "/data");
var response = await client.SendAsync(request, cts.Token);
```

## What's Included

TurboHttp works out of the box — no middleware to wire up, no Polly policies to configure.

| Feature | Description |
|---------|-------------|
| **HTTP/1.0, HTTP/1.1 & HTTP/2** | Automatic version negotiation; HTTP/2 multiplexes multiple requests over one connection |
| **Automatic Retries** | Idempotent methods (GET, PUT, DELETE) are retried automatically; respects `Retry-After` headers |
| **Built-in Caching** | In-memory LRU cache with `ETag`/`Last-Modified` conditional requests and `Vary` support |
| **Redirect Following** | Follows 301/302/303/307/308 with correct method rewriting, loop detection, and auth header stripping |
| **Cookie Management** | `CookieJar` stores `Set-Cookie` responses and injects cookies on subsequent requests automatically |
| **Content Encoding** | Automatic gzip, deflate, and Brotli decompression |
| **Connection Pooling** | Per-host pools with idle eviction, automatic reconnect, and configurable concurrency limits |
| **Channel-based API** | `ChannelWriter`/`ChannelReader` interface for backpressure-aware, high-throughput request pipelines |

## Next Steps

**Feature guides** — learn how each feature works and how to configure it:

- [Configuration](./configuration) — all options, DI registration, named clients
- [Automatic Retries](./retries) — which methods are retried, `Retry-After` support, custom policies
- [HTTP Caching](./caching) — cache lifetime, conditional requests, `Vary`, disabling cache
- [Cookie Management](./cookies) — domain/path matching, `Secure`/`HttpOnly`/`SameSite`, custom jar
- [Redirects](./redirects) — status code behaviour, method rewriting, security rules
- [Content Encoding](./content-encoding) — gzip, deflate, Brotli, disabling decompression
- [Connection Pooling](./connection-pooling) — pool lifecycle, idle eviction, concurrency limits
- [HTTP/2 & Multiplexing](./http2) — when to use HTTP/2, header compression, flow control
- [Advanced Usage](./advanced) — channel API, custom retry/redirect/cookie/cache implementations
