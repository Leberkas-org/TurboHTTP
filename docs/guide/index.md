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

var actorSystem = ActorSystem.Create("turbo");
await using var client = new TurboHttpClient(new TurboClientOptions
{
    BaseAddress = new Uri("https://api.example.com"),
}, actorSystem);

var response = await client.SendAsync(
    new HttpRequestMessage(HttpMethod.Get, "/users"),
    CancellationToken.None);

Console.WriteLine($"Status: {response.StatusCode}");
Console.WriteLine(await response.Content.ReadAsStringAsync());
```

## High-Throughput Usage

In addition to `SendAsync`, TurboHttp exposes a channel-based API for scenarios where you want to stream requests and responses without `await`-ing each one individually.

```csharp
var client = new TurboHttpClient(new TurboClientOptions
{
    BaseAddress = new Uri("https://api.example.com"),
}, actorSystem);

// Write requests to the input channel
ChannelWriter<HttpRequestMessage> requestWriter = client.Requests;

// Read responses from the output channel
ChannelReader<HttpResponseMessage> responseReader = client.Responses;
```

Use the channel API when:
- You have a producer loop generating requests faster than you can await responses
- You want to decouple request creation from response processing
- You are integrating TurboHttp into a pipeline that already uses `System.Threading.Channels`

### Batch Pattern

Write requests from one task and read responses from another, running concurrently:

```csharp
var client = new TurboHttpClient(new TurboClientOptions
{
    BaseAddress = new Uri("https://api.example.com"),
    DefaultRequestVersion = HttpVersion.Version20,
}, actorSystem);

var ids = Enumerable.Range(1, 1000).ToList();

// Producer: write all requests without waiting for responses
var producer = Task.Run(async () =>
{
    foreach (var id in ids)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/items/{id}");
        await client.Requests.WriteAsync(request);
    }
    client.Requests.Complete();
});

// Consumer: process responses as they arrive
var consumer = Task.Run(async () =>
{
    await foreach (var response in client.Responses.ReadAllAsync())
    {
        var body = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"{(int)response.StatusCode}: {body.Length} bytes");
    }
});

await Task.WhenAll(producer, consumer);
```

With HTTP/2, all 1000 requests flow over a single TCP connection as concurrent streams. With HTTP/1.1, they are serialised per connection but the producer/consumer split still keeps throughput high.

### Backpressure

The channel has a bounded capacity. If the connection cannot keep up with your producer, `WriteAsync` will pause automatically until there is room. You never drop requests — the channel applies backpressure instead.

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

- [Troubleshooting](./troubleshooting) — FAQ, common issues, debugging tips

**Deep dive:**

- [Architecture Overview](/architecture/) — four-layer design, data flow, protocol engines, end-to-end scenarios
