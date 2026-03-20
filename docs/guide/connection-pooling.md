# Connection Pooling

TurboHttp manages a pool of TCP connections for every host you talk to. Connections are created on demand, reused across requests, and automatically cleaned up when idle.

## How It Works

Each unique host (scheme + hostname + port + HTTP version) gets its own connection pool. When a request arrives, TurboHttp tries to reuse an existing open connection. If all connections are busy and the per-host limit has not been reached, a new connection is established. If the limit is already reached, the request waits until a connection becomes free.

```
Request → HostPool
               ├─ Idle connection available? → Reuse it
               ├─ Below per-host limit?      → Open new connection
               └─ At per-host limit?         → Wait in queue
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

If a connection is dropped unexpectedly (network interruption, server-side timeout, or RST), TurboHttp detects the failure and reconnects automatically. Reconnect attempts use exponential backoff — each failed attempt waits progressively longer before the next try:

| Attempt | Wait |
|---------|------|
| 1st | 1 s |
| 2nd | 2 s |
| 3rd | 4 s |
| 4th | 8 s |
| 5th+ | 16 s (capped) |

While reconnecting, queued requests wait for the connection to recover. Once reconnected, TurboHttp replays the queue.

## Per-Host Concurrency Limits

Each host has a configurable maximum number of simultaneous connections. The default limit is chosen conservatively to avoid overwhelming servers and saturating the local network stack.

For HTTP/2 hosts, the limit is effectively 1 (one multiplexed connection per host) unless you explicitly configure more. Multiple streams flow over that connection.

## Configuration

Connection pool behaviour is controlled via `ConnectionPoolOptions` on `TurboClientOptions`:

```csharp
var options = new TurboClientOptions
{
    BaseAddress = new Uri("https://api.example.com"),
    ConnectionPool = new ConnectionPoolOptions
    {
        MaxConnectionsPerHost = 10,         // max simultaneous connections (default: 4)
        IdleConnectionTimeout = TimeSpan.FromSeconds(90), // close idle connections after 90 s (default: 60 s)
        MaxReconnectAttempts = 5,           // give up reconnecting after 5 failed tries (default: unlimited)
    }
};
```

With DI:

```csharp
services.AddTurboHttpClientFactory();

var client = factory.CreateClient(opts => opts with
{
    ConnectionPool = new ConnectionPoolOptions
    {
        MaxConnectionsPerHost = 20,
        IdleConnectionTimeout = TimeSpan.FromMinutes(2),
    }
});
```

### Common Tuning Scenarios

**High-throughput API client** — increase connections per host and extend the idle timeout so connections stay warm:

```csharp
ConnectionPool = new ConnectionPoolOptions
{
    MaxConnectionsPerHost = 20,
    IdleConnectionTimeout = TimeSpan.FromMinutes(5),
}
```

**Low-traffic background service** — reduce connections and shorten idle timeout to release sockets promptly:

```csharp
ConnectionPool = new ConnectionPoolOptions
{
    MaxConnectionsPerHost = 2,
    IdleConnectionTimeout = TimeSpan.FromSeconds(30),
}
```

**HTTP/2 server** — a single multiplexed connection handles many concurrent streams. Keep the per-host limit at 1 to avoid redundant TCP handshakes:

```csharp
var options = new TurboClientOptions
{
    DefaultRequestVersion = HttpVersion.Version20,
    ConnectionPool = new ConnectionPoolOptions
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
