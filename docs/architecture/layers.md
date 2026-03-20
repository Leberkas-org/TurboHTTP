# Architectural Layers

TurboHttp is composed of four layers. The container view shows all four layers and how they relate to each other and the outside world.

## Container View

<LikeC4Diagram viewId="turbohttp" :height="560" />

The four containers are:

| Container | Description |
|-----------|-------------|
| **Client** | Public API surface — `ITurboHttpClient`, `SendAsync`, and the channel-based request/response interface |
| **Streams** | Akka.Streams `GraphStage` pipeline — version demultiplexer, per-version protocol engines, request enrichment, and all middleware stages (cookies, caching, compression, redirect, retry) |
| **Protocol** | Pure-logic RFC implementations — encoders, decoders, HPACK, `CookieJar`, `HttpCacheStore`, `RedirectHandler`, `RetryEvaluator` |
| **I/O** | Hybrid lifecycle + data-path layer — actor hierarchy manages connection pooling; `System.Threading.Channels` carry bytes with zero actor hops |

---

## Client Layer

<LikeC4Diagram viewId="clientLayer" :height="400" />

`ITurboHttpClient` exposes two interaction modes:

- **`SendAsync(HttpRequestMessage, CancellationToken)`** — standard `HttpClient`-style request/response, suitable for most callers
- **Channel API** — direct `ChannelWriter<HttpRequestMessage>` / `ChannelReader<HttpResponseMessage>` for high-throughput producer/consumer pipelines

The client layer creates and materialises the Akka.Streams graph. All configuration (base address, default HTTP version, default headers, per-host connection limits) is applied here before requests enter the stream.

---

## Streams Layer

<LikeC4Diagram viewId="streamsLayer" :height="600" />

The Streams layer is the heart of TurboHttp. It is a single composable Akka.Streams graph that processes every request through the following stages (in order):

**Request chain:**

1. `RequestEnricherStage` — applies `BaseAddress`, `DefaultRequestVersion`, `DefaultRequestHeaders`
2. `CookieInjectionStage` — injects matching cookies from `CookieJar` into the `Cookie` header
3. `CacheLookupStage` — checks `HttpCacheStore`; returns a cached response immediately on hit, bypassing the network
4. `Engine` — demultiplexes by HTTP version and routes to `Http10Engine`, `Http11Engine`, or `Http20Engine`

**Response chain (after the network):**

5. `DecompressionStage` — decompresses gzip/deflate/brotli response bodies
6. `CookieStorageStage` — parses `Set-Cookie` headers and stores cookies in `CookieJar`
7. `CacheStorageStage` — stores cacheable responses in `HttpCacheStore`
8. `RetryStage` — retries idempotent requests on transient failures (RFC 9110 §9.2)
9. `RedirectStage` — follows redirects (RFC 9110 §15.4) with method rewriting and loop detection

**Key cross-cutting stages:**

| Stage | Purpose |
|-------|---------|
| `ConnectionStage` | TCP connection wrapper; communicates with the I/O actor pool via `ConnectionHandle` |
| `CorrelationHttp1XStage` | FIFO request-response matching for HTTP/1.x pipelined connections |
| `CorrelationHttp20Stage` | Stream-ID-based matching for HTTP/2 multiplexed streams |

---

## I/O Layer

<LikeC4Diagram viewId="ioLayer" :height="520" />

The I/O layer uses a **hybrid pattern** — actors manage connection lifecycle while data travels through lock-free channels.

### Actor Hierarchy (lifecycle only)

```
PoolRouterActor
  └── HostPoolActor  (one per host:port)
        └── ConnectionActor  (one per TCP connection)
              └── ClientRunner → ClientByteMover
```

- **`PoolRouterActor`** — receives `EnsureHost` messages; routes to the correct `HostPoolActor`, creating one if needed
- **`HostPoolActor`** — maintains the pool of connections for a single host; enforces `PerHostConnectionLimiter`; handles reconnect scheduling and idle eviction
- **`ConnectionActor`** — owns a single TCP socket; creates `Channel<(IMemoryOwner<byte>, int)>` pairs; spawns `ClientRunner` on connect; sends `ConnectionReady(ConnectionHandle)` back to `HostPoolActor`
- **`ClientRunner`** — per-connection actor that starts `ClientByteMover` tasks and signals lifecycle events (connected, disconnected, error)
- **`ClientByteMover`** — three static async tasks per connection: TCP→Pipe, Pipe→InboundChannel, OutboundChannel→TCP

### Data Path (zero actor hops)

```
ConnectionStage ←→ OutboundWriter / InboundReader ←→ ClientByteMover ←→ TCP socket
```

`ConnectionHandle` is a plain record containing `ChannelWriter<byte>` (outbound) and `ChannelReader<byte>` (inbound). `ConnectionStage` writes and reads these channels directly — no actor mailbox is ever in the hot path.

### Why Hybrid?

Actors are excellent for managing shared, mutable state (pool membership, reconnect backoff, idle timers). They are poor at high-frequency data movement. `System.Threading.Channels` gives the data path lock-free, zero-copy throughput without the overhead of actor message passing.
