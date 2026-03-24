# Connection Pooling

TurboHttp automatically manages a pool of connections for each host, so you never need to open, close, or track connections yourself.

## How It Works

Each unique host (scheme + hostname + port + HTTP version) gets its own connection pool. When a request arrives, TurboHttp tries to reuse an existing open connection. If all connections are busy and the per-host limit has not been reached, a new connection is established. If the limit is already reached, the request waits until a connection becomes free.

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
- **HTTP/2** — a single TCP connection carries multiple concurrent requests as independent streams. All in-flight requests to the same host share one connection.
- **HTTP/1.0** — connections are closed after each response. No reuse. Each request opens a new TCP connection.

## Idle Connection Eviction

Idle HTTP/1.1 connections that have not been used for a configurable period are automatically closed and removed from the pool. This prevents the pool from holding open sockets to hosts that are no longer being actively requested.

The idle timeout is measured from the moment a connection returns to the pool with no pending requests. An active connection is never evicted mid-request.

## Automatic Reconnect

If a connection is dropped unexpectedly (network interruption, server-side timeout, or RST), TurboHttp detects the failure and reconnects automatically. While reconnecting, queued requests wait for the connection to recover. Once reconnected, TurboHttp replays the queue.

::: tip Backoff timing
Reconnect attempts use exponential backoff — each failed attempt waits progressively longer before the next try (1 s → 2 s → 4 s → 8 s → 16 s cap). You can control timing with `ReconnectInterval` and `MaxReconnectAttempts` in [Configuration](./configuration).
:::

## Per-Host Concurrency Limits

Each host has a configurable maximum number of simultaneous connections:

- **HTTP/1.1** — default limit is **6 connections per host**
- **HTTP/2** — default is **1 multiplexed connection per host** (multiple concurrent requests share that single connection as independent streams)

These defaults are chosen conservatively to avoid overwhelming servers. Adjust them via `ConnectionPolicy` (see [Configuration](#configuration) below).

## Configuration

Connection pool behaviour is controlled via `ConnectionPolicy` on `TurboClientOptions`:

```csharp
var options = new TurboClientOptions
{
    BaseAddress = new Uri("https://api.example.com"),
    ConnectionPolicy = new ConnectionPolicy
    {
        MaxConnectionsPerHost = 10,         // max simultaneous connections (default: 6)
    }
};
```

With DI:

```csharp
services.AddTurboHttpClient("my-api", opts =>
{
    opts = opts with
    {
        BaseAddress = new Uri("https://api.example.com"),
    };
});

var client = factory.CreateClient(opts => opts with
{
    ConnectionPolicy = new ConnectionPolicy
    {
        MaxConnectionsPerHost = 20,
    }
});
```

### Common Tuning Scenarios

**High-throughput API client** — increase connections per host:

```csharp
ConnectionPolicy = new ConnectionPolicy
{
    MaxConnectionsPerHost = 20,
}
```

**Low-traffic background service** — reduce connections to release sockets promptly:

```csharp
ConnectionPolicy = new ConnectionPolicy
{
    MaxConnectionsPerHost = 2,
}
```

**HTTP/2 server** — a single multiplexed connection handles many concurrent streams. Keep the per-host limit at 1 to avoid redundant TCP handshakes:

```csharp
var options = new TurboClientOptions
{
    DefaultRequestVersion = HttpVersion.Version20,
    ConnectionPolicy = new ConnectionPolicy
    {
        MaxConnectionsPerHost = 1,
    }
};
```

## Pool Lifecycle

The pool is created lazily when the first request to a host is made, and torn down when the `TurboHttpClient` is disposed. All idle connections are closed on disposal. In-flight requests complete before their connections are closed.

```csharp
await using var client = new TurboHttpClient(options);

// Pool for api.example.com created on first request
var response = await client.SendAsync(request);

// Disposing client drains all pools and closes all connections
```
