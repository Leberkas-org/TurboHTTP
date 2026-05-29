# Client

TurboHTTP is a high-performance HTTP client for .NET built on Akka.Streams. It supports HTTP/1.0, HTTP/1.1, HTTP/2, and HTTP/3 (QUIC) with automatic retries, caching, cookies, and connection pooling — all built in.

::: tip New to TurboHTTP?
Start with the [Client Quick Start](/getting-started/client) for a step-by-step setup guide.
:::

::: tip Coming from HttpClient?
See [Installation & Setup](./installation) for DI registration, named clients, and the fluent builder API. Check the [Migration Guide](/getting-started/migration) for a detailed comparison.
:::

::: info Looking for the server?
TurboHTTP also provides a high-performance drop-in ASP.NET Core IServer (a Kestrel replacement). See the [Server Guide](/server/).
:::

## High-Throughput Usage

In addition to `SendAsync`, TurboHTTP exposes a channel-based API for scenarios where you want to stream requests and responses without `await`-ing each one individually.

```csharp
var client = factory.CreateClient();

// Write requests to the input channel
ChannelWriter<HttpRequestMessage> requestWriter = client.Requests;

// Read responses from the output channel
ChannelReader<HttpResponseMessage> responseReader = client.Responses;
```

Use the channel API when:

- You have a producer loop generating requests faster than you can await responses
- You want to decouple request creation from response processing
- You are integrating TurboHTTP into a pipeline that already uses `System.Threading.Channels`

### Batch Pattern

Write requests from one task and read responses from another, running concurrently:

```csharp
var client = factory.CreateClient();
client.BaseAddress = new Uri("https://api.example.com");
client.DefaultRequestVersion = HttpVersion.Version20;

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

TurboHTTP works out of the box — no middleware to wire up, no Polly policies to configure.

| Feature                                 | Description                                                                                          |
| --------------------------------------- | ---------------------------------------------------------------------------------------------------- |
| **HTTP/1.0, HTTP/1.1, HTTP/2 & HTTP/3** | Automatic version negotiation; HTTP/2 multiplexes over TCP, HTTP/3 multiplexes over QUIC             |
| **Automatic Retries**                   | Idempotent methods (GET, PUT, DELETE) are retried automatically; respects `Retry-After` headers      |
| **Built-in Caching**                    | In-memory LRU cache with `ETag`/`Last-Modified` conditional requests and `Vary` support              |
| **Redirect Following**                  | Follows 301/302/303/307/308 with correct method rewriting, loop detection, and auth header stripping |
| **Cookie Management**                   | `CookieJar` stores `Set-Cookie` responses and injects cookies on subsequent requests automatically   |
| **Content Encoding**                    | Automatic gzip, deflate, and Brotli decompression                                                    |
| **Connection Pooling**                  | Per-host pools with idle eviction, automatic reconnect, and configurable concurrency limits          |
| **Channel-based API**                   | `ChannelWriter`/`ChannelReader` interface for backpressure-aware, high-throughput request pipelines  |

## Next Steps

**Setup & migration:**

- [Installation & Setup](./installation) — DI registration, named clients, typed clients, fluent builder
- [Migration from HttpClient](/getting-started/migration) — side-by-side comparison, step-by-step migration

**Feature guides:**

- [Configuration](./configuration) — all options, DI registration, named clients
- [Automatic Retries](./retries) — which methods are retried, `Retry-After` support, custom policies
- [HTTP Caching](./caching) — cache lifetime, conditional requests, `Vary`, disabling cache
- [Cookie Management](./cookies) — domain/path matching, `Secure`/`HttpOnly`/`SameSite`, custom jar
- [Redirects](./redirects) — status code behaviour, method rewriting, security rules
- [Content Encoding](./content-encoding) — gzip, deflate, Brotli, disabling decompression
- [Connection Pooling](./connection-pooling) — pool lifecycle, idle eviction, concurrency limits
- [HTTP/2 & Multiplexing](./http2) — when to use HTTP/2, header compression, flow control
- [HTTP/3 & QUIC](./http3) — QUIC transport, 0-RTT, connection migration, Alt-Svc discovery

- [Troubleshooting](./troubleshooting) — FAQ, common issues, debugging tips

**Deep dive:**

- [Architecture Overview](/architecture/) — client and server pipeline, protocol engines, end-to-end scenarios
