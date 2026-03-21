# Getting Started

TurboHttp is a high-performance HTTP client for .NET built on Akka.Streams. It supports HTTP/1.0, HTTP/1.1, and HTTP/2 with automatic retries, caching, cookies, and connection pooling — all built in.

::: tip New to TurboHttp?
See [Installation & Setup](./installation) for DI registration, named clients, and the fluent builder API. Coming from HttpClient? Check the [Migration Guide](./migration).
:::

## Quick Start

```bash
dotnet add package TurboHttp
```

```csharp
using TurboHttp.Client;

await using var client = TurboHttpClientFactory.Create(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
});

var response = await client.SendAsync(
    new HttpRequestMessage(HttpMethod.Get, "/users"),
    CancellationToken.None);

Console.WriteLine($"Status: {response.StatusCode}");
Console.WriteLine(await response.Content.ReadAsStringAsync());
```

## Channel-Based API

For high-throughput scenarios, use the channel API to decouple request production from response consumption:

```csharp
await using var client = TurboHttpClientFactory.Create(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.DefaultRequestVersion = HttpVersion.Version20;
});

// Producer: write requests without waiting for responses
await client.RequestWriter.WriteAsync(new HttpRequestMessage(HttpMethod.Get, "/item/1"));
await client.RequestWriter.WriteAsync(new HttpRequestMessage(HttpMethod.Get, "/item/2"));

// Consumer: read responses as they arrive
await foreach (var response in client.ResponseReader.ReadAllAsync())
{
    Console.WriteLine($"{response.StatusCode}");
}
```

With HTTP/2, all requests flow over a single TCP connection as concurrent streams.

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

**Setup & migration:**

- [Installation & Setup](./installation) — DI registration, named clients, typed clients, fluent builder
- [Migration from HttpClient](./migration) — side-by-side comparison, step-by-step migration

**Feature guides:**

- [Configuration](./configuration) — all options, DI registration, named clients
- [Automatic Retries](./retries) — which methods are retried, `Retry-After` support, custom policies
- [HTTP Caching](./caching) — cache lifetime, conditional requests, `Vary`, disabling cache
- [Cookie Management](./cookies) — domain/path matching, `Secure`/`HttpOnly`/`SameSite`, custom jar
- [Redirects](./redirects) — status code behaviour, method rewriting, security rules
- [Content Encoding](./content-encoding) — gzip, deflate, Brotli, disabling decompression
- [Connection Pooling](./connection-pooling) — pool lifecycle, idle eviction, concurrency limits
- [HTTP/2 & Multiplexing](./http2) — when to use HTTP/2, header compression, flow control
- [Advanced Usage](./advanced) — channel API, custom retry/redirect/cookie/cache implementations
- [Troubleshooting](./troubleshooting) — FAQ, common issues, debugging tips

**Deep dive:**

- [Architecture Overview](/architecture/) — four-layer design, data flow
- [Internals](/internals/) — protocol implementation, memory management, HPACK, testing
