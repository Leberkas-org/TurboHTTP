# Connection Pooling

TurboHTTP automatically manages a pool of connections for each host, so you never need to open, close, or track connections yourself.

## How It Works

Each unique host (scheme + hostname + port + HTTP version) gets its own connection pool. When a request arrives, TurboHTTP tries to reuse an existing open connection. If all connections are busy and the per-host limit has not been reached, a new connection is established. If the limit is already reached, the request waits until a connection becomes free.

```
Request → ConnectionPool → HostConnections (per-host manager)
                                ├─ Idle connection available? → Return lease
                                ├─ Below per-host limit?      → Establish new connection
                                └─ At per-host limit?         → Wait for release
```

The pool runs entirely in the background. Your code just calls `SendAsync` — connection acquisition, reuse, and lifecycle are transparent.

## Connection Reuse

How connections are reused depends on the HTTP version:

- **HTTP/1.1** — connections use keep-alive by default. After a response is received, the connection returns to the idle pool and is available for the next request.
- **HTTP/2** — a single TCP connection carries multiple concurrent requests as independent streams. When a connection reaches its stream limit, additional connections are opened.
- **HTTP/3** — similar to HTTP/2 but over QUIC instead of TCP. Supports connection migration and 0-RTT early data.
- **HTTP/1.0** — connections are closed after each response. No reuse. Each request opens a new TCP connection.

## Idle Connection Eviction

Idle HTTP/1.1 connections that have not been used for a configurable period are automatically closed and removed from the pool. This prevents the pool from holding open sockets to hosts that are no longer being actively requested.

The idle timeout is measured from the moment a connection returns to the pool with no pending requests. An active connection is never evicted mid-request.

## Automatic Reconnect

If a connection is dropped unexpectedly (network interruption, server-side timeout, or RST), TurboHTTP detects the failure and reconnects automatically. While reconnecting, queued requests wait for the connection to recover. Once reconnected, TurboHTTP replays the queue.

::: tip Backoff timing
Reconnect attempts use exponential backoff — each failed attempt waits progressively longer before the next try (1 s → 2 s → 4 s → 8 s → 16 s cap).
:::

## Per-Host Concurrency Limits

Each host has a configurable maximum number of simultaneous connections, configured per protocol version:

- **HTTP/1.x** — default limit is **6 connections per host** (`Http1.MaxConnectionsPerServer`)
- **HTTP/2** — default is **6 connections per host** (`Http2.MaxConnectionsPerServer`), each carrying up to **100 concurrent streams** (`Http2.MaxConcurrentStreams`)
- **HTTP/3** — default is **4 QUIC connections per host** (`Http3.MaxConnectionsPerServer`)

These defaults are chosen conservatively to avoid overwhelming servers.

## Configuration

Connection pool behaviour is controlled via properties on `TurboClientOptions`:

```csharp
builder.Services.AddTurboHttpClient("my-api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2);
    options.PooledConnectionLifetime = TimeSpan.FromMinutes(10);

    // Per-version connection limits
    options.Http1.MaxConnectionsPerServer = 10;
    options.Http2.MaxConnectionsPerServer = 4;
    options.Http3.MaxConnectionsPerServer = 2;
});
```

### Common Tuning Scenarios

**High-throughput HTTP/1.1 client** — increase connections per host:

```csharp
options.Http1.MaxConnectionsPerServer = 20;
```

**Low-traffic background service** — reduce connections to release sockets promptly:

```csharp
options.Http1.MaxConnectionsPerServer = 2;
options.PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30);
```

**HTTP/2 server** — each connection multiplexes many concurrent streams. TurboHTTP opens additional connections when a connection reaches its stream limit:

```csharp
var client = factory.CreateClient("http2-api");
client.DefaultRequestVersion = HttpVersion.Version20;
client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
```

**HTTP/3 server** — QUIC connections with configurable idle timeout:

```csharp
options.Http3.MaxConnectionsPerServer = 4;
options.Http3.IdleTimeout = TimeSpan.FromSeconds(60);
```

## Pool Lifecycle

The pool is created lazily when the first request to a host is made, and torn down when the `TurboHttpClient` is disposed. All idle connections are closed on disposal. In-flight requests complete before their connections are closed.

```csharp
var client = factory.CreateClient("my-api");

// Pool for api.example.com created on first request
var response = await client.SendAsync(request, ct);

// Disposing client drains all pools and closes all connections
client.Dispose();
```
