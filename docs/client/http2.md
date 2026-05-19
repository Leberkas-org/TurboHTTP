# HTTP/2 & Multiplexing

HTTP/2 is the recommended protocol for high-throughput workloads. TurboHTTP enables it with a single property change — everything else is handled automatically.

## When to Use HTTP/2

HTTP/2 shines when your application sends many requests to the same server at the same time. Common scenarios:

- **Microservices** — a service calling the same downstream API from multiple request handlers simultaneously
- **Batch processing** — firing dozens of API calls in parallel (`Task.WhenAll`)
- **Real-time dashboards** — polling multiple endpoints concurrently
- **File upload/download pipelines** — streaming multiple resources in parallel

If you send one request at a time and wait for each response before sending the next, HTTP/1.1 is perfectly adequate. HTTP/2 pays off when requests pile up concurrently.

## How Multiplexing Works

With HTTP/1.1, each request occupies an entire TCP connection from start to finish. To run 10 requests in parallel you need 10 separate connections.

With HTTP/2, a single TCP connection carries multiple requests at the same time as independent _streams_. Each stream has its own ID and flows alongside the others without blocking:

```
HTTP/1.1 (4 connections needed for 4 parallel requests):
  conn-1: ─── req A ────────────────────────────
  conn-2: ─── req B ─────────────────
  conn-3: ──────── req C ───────────────────────
  conn-4: ──────── req D ─────────

HTTP/2 (1 connection, 4 streams):
  stream 1: ─── req A ────────────────────────────
  stream 3: ─── req B ─────────────────
  stream 5: ──────── req C ───────────────────────
  stream 7: ──────── req D ─────────
```

The server can also respond out of order — if response B is ready before response A, TurboHTTP routes it to the correct caller automatically.

## Header Compression

HTTP/2 compresses request and response headers automatically. Repeated headers (like `Authorization`, `Content-Type`, or `User-Agent`) are not retransmitted in full — only the difference from the previous request is sent. On APIs where headers dwarf the body, this alone can cut wire size significantly.

Header compression is fully transparent. You set headers the same way as with HTTP/1.1; TurboHTTP handles compression and decompression for you.

## Flow Control

HTTP/2 has built-in flow control to prevent a fast sender from overwhelming a slow receiver. TurboHTTP manages flow control windows automatically — you never need to interact with it directly.

## Enabling HTTP/2

Set `DefaultRequestVersion` on the client after obtaining it from the factory:

```csharp
builder.Services.AddTurboHttpClient("http2-api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
});

// ...

var client = factory.CreateClient("http2-api");
client.DefaultRequestVersion = HttpVersion.Version20;
```

All requests will now use HTTP/2. If the server does not support HTTP/2, TurboHTTP will establish the connection and the server will reject or downgrade — no automatic fallback to HTTP/1.1 is performed when you explicitly request HTTP/2.

To allow graceful fallback to HTTP/1.1 when the server doesn't support HTTP/2:

```csharp
client.DefaultRequestVersion = HttpVersion.Version20;
client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
```

## Multiplexing Configuration

By default, TurboHTTP opens up to 6 HTTP/2 connections per host, each carrying up to 100 concurrent streams. Tune these values on the nested `Http2` options:

```csharp
builder.Services.AddTurboHttpClient("http2-api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.Http2.MaxConnectionsPerServer = 4;    // default: 6
    options.Http2.MaxConcurrentStreams = 200;      // default: 100
});
```

When a connection reaches its stream limit, TurboHTTP automatically opens additional connections up to `MaxConnectionsPerServer`.

## HTTP/2 over TLS vs. Cleartext

HTTP/2 is normally negotiated during the TLS handshake (the server advertises `h2` support and TurboHTTP activates it). This is the standard path for `https://` URLs — no extra configuration required.

HTTP/2 over cleartext (`http://` URLs, sometimes called h2c) is also supported. Set `DefaultRequestVersion = HttpVersion.Version20` and use an `http://` base address. Note that many public APIs and proxies do not support h2c, so this is mainly useful for internal service-to-service communication within a trusted network.

## Frame Size

Each HTTP/2 request and response is broken into frames before being sent over the wire. The default maximum frame size is 16 KiB. Increase it for workloads that transfer large bodies to reduce framing overhead:

```csharp
builder.Services.AddTurboHttpClient("http2-api", options =>
{
    options.Http2.MaxFrameSize = 4 * 1024 * 1024; // 4 MiB (default: 16 KiB, max: 16 MiB)
});
```

Larger frames mean fewer round-trips for big payloads but more memory per in-flight stream. The default is a good balance for most workloads.

## Sending Parallel Requests

Use standard .NET concurrency patterns — TurboHTTP multiplexes automatically:

```csharp
var client = factory.CreateClient("http2-api");
client.DefaultRequestVersion = HttpVersion.Version20;

// All three requests flow over the same TCP connection simultaneously
var results = await Task.WhenAll(
    client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/users"), ct),
    client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/products"), ct),
    client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/orders"), ct)
);
```

No pooling, no batching, no special API needed — just `Task.WhenAll`.
