# HTTP/3 & QUIC

HTTP/3 runs over QUIC instead of TCP, eliminating head-of-line blocking at the transport layer and enabling features like connection migration and 0-RTT handshakes.

## When to Use HTTP/3

HTTP/3 is beneficial when:

- **Latency matters** — QUIC combines the TLS and transport handshakes, reducing connection setup from 2-3 round trips (TCP + TLS) to 1 (or 0 with 0-RTT).
- **Mobile or unstable networks** — connection migration keeps the session alive when the client's IP address changes (e.g., Wi-Fi to cellular).
- **High packet loss** — unlike TCP, QUIC streams are independent at the transport layer. A lost packet on one stream doesn't block other streams.

If your server doesn't support HTTP/3, or you're on a stable, low-latency network, HTTP/2 is equally effective.

## Enabling HTTP/3

Set `DefaultRequestVersion` on the client after obtaining it from the factory:

```csharp
builder.Services.AddTurboHttpClient("http3-api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
});

// ...

var client = factory.CreateClient("http3-api");
client.DefaultRequestVersion = HttpVersion.Version30;
```

To allow graceful fallback to HTTP/2 or HTTP/1.1 when the server doesn't support HTTP/3:

```csharp
client.DefaultRequestVersion = HttpVersion.Version30;
client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
```

## Configuration

HTTP/3 options are configured on the nested `Http3` sub-object of `TurboClientOptions`:

```csharp
builder.Services.AddTurboHttpClient("http3-api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");

    options.Http3.MaxConnectionsPerServer = 4;    // default: 4
    options.Http3.IdleTimeout = TimeSpan.FromSeconds(60);  // default: 30 s
    options.Http3.MaxReconnectAttempts = 5;        // default: 3
});
```

### All HTTP/3 options

| Property                   | Type       | Default            | Description                                   |
| -------------------------- | ---------- | ------------------ | --------------------------------------------- |
| `MaxConnectionsPerServer`  | `int`      | `4`                | Max concurrent QUIC connections per host      |
| `QpackMaxTableCapacity`    | `int`      | `4096`             | QPACK dynamic table size in bytes             |
| `QpackBlockedStreams`      | `int`      | `100`              | Max streams blocked waiting for QPACK encoder |
| `MaxFieldSectionSize`      | `int`      | `65536` (64 KiB)   | Max header block size                         |
| `IdleTimeout`              | `TimeSpan` | `30 s`             | QUIC idle timeout                             |
| `MaxReconnectAttempts`     | `int`      | `3`                | Max reconnect attempts on connection drop     |
| `AllowEarlyData`           | `bool`     | `false`            | Allow QUIC 0-RTT early data                   |
| `AllowConnectionMigration` | `bool`     | `true`             | Allow QUIC connection migration               |
| `AllowServerPush`          | `bool`     | `false`            | Allow server push via PUSH_PROMISE            |
| `MaxBatchWeight`           | `long`     | `262144` (256 KiB) | Max batch weight for frame encoding           |
| `EnableAltSvcDiscovery`    | `bool`     | `false`            | Auto-discover HTTP/3 via Alt-Svc headers      |

## 0-RTT Early Data

When enabled, TurboHTTP can send idempotent requests (GET, HEAD, OPTIONS, TRACE, DELETE) before the TLS handshake completes on repeat connections to known servers. This reduces latency by one round trip.

```csharp
options.Http3.AllowEarlyData = true;
```

::: warning
0-RTT data can be replayed by an attacker. TurboHTTP only sends idempotent requests as early data — POST and PATCH are never sent early. If the server rejects 0-RTT, the request is automatically re-sent after the full handshake.
:::

## Connection Migration

QUIC connections can survive IP address changes. When the client moves between networks (e.g., Wi-Fi to cellular), the connection continues transparently without re-establishing:

```csharp
options.Http3.AllowConnectionMigration = true;  // default: true
```

When disabled, TurboHTTP closes the connection on address change and reconnects via the normal reconnect mechanism.

## Alt-Svc Discovery

TurboHTTP can automatically discover HTTP/3 availability by reading `Alt-Svc` headers from HTTP/1.1 and HTTP/2 responses. When a server advertises `h3` support, subsequent requests to that host are upgraded to HTTP/3:

```csharp
options.Http3.EnableAltSvcDiscovery = true;  // default: false
```

This is opt-in because not all environments support QUIC (firewalls may block UDP). Enable it when you know your network path supports QUIC and want automatic protocol upgrade.

## Server Push

HTTP/3 supports server push, where the server proactively sends resources the client hasn't requested yet. This is disabled by default:

```csharp
options.Http3.AllowServerPush = true;  // default: false
```

When disabled, any PUSH_PROMISE frames from the server are rejected.

## QPACK Header Compression

HTTP/3 uses QPACK for header compression (the QUIC equivalent of HPACK in HTTP/2). TurboHTTP manages QPACK encoding and decoding automatically. Tune the dynamic table size if needed:

```csharp
options.Http3.QpackMaxTableCapacity = 8192;  // default: 4096
options.Http3.QpackBlockedStreams = 200;      // default: 100
```

Larger tables improve compression ratio for APIs with many repeated headers but use more memory per connection.
