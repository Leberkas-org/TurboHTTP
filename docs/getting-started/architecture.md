# Architecture Overview

TurboHTTP is both an HTTP client and a standalone HTTP server, built on Akka.Streams. Both sides follow the same principle: composable pipeline stages connected by backpressure-aware streams.

<ClientOnly>
  <LikeC4Diagram viewId="index" :height="300" />
</ClientOnly>

---

## Client

### Request Pipeline

When you call `SendAsync()`, your request passes through a series of processing stages:

<ClientOnly>
  <LikeC4Diagram viewId="pipelineFlow" :height="500" />
</ClientOnly>

```
Your Request
    ↓
[Enricher] — applies default headers, base address
    ↓
[Tracing] — starts an activity span for observability
    ↓
[Handlers] — runs any custom middleware you registered
    ↓
[Redirects] — tracks redirect chain; follows on response side
    ↓
[Cookies] — injects matching cookies from the cookie jar
    ↓
[Retry] — attaches retry context; re-sends on transient failures
    ↓
[Expect-Continue] — holds large bodies until server confirms readiness
    ↓
[Cache] — returns immediately if response is cached and fresh
    ↓
[Content Encoding] — compresses request body / decompresses response
    ↓
[Protocol Encoder] — converts to HTTP/1.0, 1.1, 2, or 3 bytes
    ↓
[Network] — sends over TCP or QUIC
    ↓
[Protocol Decoder] — parses response bytes
    ↓
[Content Encoding] — decompresses gzip/deflate/brotli
    ↓
[Cache] — stores response if cacheable
    ↓
[Retry] — re-sends on transient errors, 408, or 503
    ↓
[Cookies] — stores Set-Cookie headers
    ↓
[Redirects] — follows 301-308 automatically
    ↓
[Tracing] — closes the activity span
    ↓
Your Response
```

Each stage does one thing well. Most of the time you don't think about them — they just work.

### Client Architecture

<ClientOnly>
  <LikeC4Diagram viewId="clientLayer" :height="300" />
</ClientOnly>

- **ITurboHttpClient** — the public API: `SendAsync()` for single requests, `Requests`/`Responses` channels for high-throughput streaming
- **TurboHttpClient** — concrete implementation that wraps the Akka.Streams pipeline
- **ClientStreamManager** — materialises the full pipeline graph from the configured stages
- **ClientStreamOwner** — owns the materialised stream lifecycle: starts, monitors, and restarts the pipeline on failure

### Key Characteristics

- **Automatic**: Cookies, caching, retries, redirects all work out of the box
- **Efficient**: HTTP/2 and HTTP/3 multiplexing, keep-alive connection reuse, lock-free data movement
- **Composable**: Each feature is an opt-in pipeline stage — add or remove with `.WithRetry()`, `.WithCache()`, etc.
- **Observable**: Built-in tracing stage spans every request/response cycle

---

## Server

### Request Pipeline

When a request arrives at TurboHTTP Server, it passes through a complementary pipeline:

<ClientOnly>
  <LikeC4Diagram viewId="serverPipeline" :height="500" />
</ClientOnly>

```
Incoming TCP/QUIC Connection
    ↓
[Transport] — accepts connection via ListenerActor
    ↓
[Protocol Decoder] — parses HTTP/1.0, 1.1, 2, or 3 bytes
    ↓
[HttpContext Builder] — creates TurboHttpContext from parsed request
    ↓
[Middleware Pipeline] — runs registered middleware (Use/Run/Map/MapWhen)
    ↓
[Router] — matches request to registered route
    ↓
[Dispatcher] — DelegateDispatcher (handler) or EntityDispatcher (actor)
    ↓
[Parameter Binding] — binds route values, query, body, headers to handler parameters
    ↓
[Handler / Actor] — executes your code
    ↓
[Response] — writes response back through the pipeline
```

Each connection is managed by a `ConnectionActor` that owns the full Akka.Streams graph for that connection — from transport bytes through to response serialisation.

### Server Architecture

TurboHTTP Server is a standalone HTTP server — it does not use Kestrel. It uses its own transport layer via Servus.Akka.Transport.

<ClientOnly>
  <LikeC4Diagram viewId="serverHierarchy" :height="400" />
</ClientOnly>

- **TurboServerHostedService** — `IHostedService` entry point: creates ActorSystem and spawns the supervisor
- **ServerSupervisorActor** — manages all listeners and tracks connection counts
- **ListenerActor** — binds TCP or QUIC transport, accepts incoming connections, spawns a ConnectionActor per client
- **ConnectionActor** — materialises the protocol engine + middleware + routing graph for a single client

### Transport Layer

- **TCP**: `TcpListenerFactory` handles HTTP/1.0, HTTP/1.1, and HTTP/2 connections
- **QUIC**: `QuicListenerFactory` handles HTTP/3 connections

Protocol engines (`Http10ServerEngine`, `Http11ServerEngine`, `Http20ServerEngine`, `Http30ServerEngine`) are selected via ALPN negotiation when TLS is enabled, or default to HTTP/1.1 for plaintext connections.

### Key Characteristics

- **Standalone**: Own TCP/QUIC transport — no Kestrel dependency
- **Actor-based**: Supervisor → Listener → Connection hierarchy with graceful shutdown and coordinated termination
- **Composable**: ASP.NET Core-style middleware pipeline with Use/Run/Map/MapWhen
- **Protocol-complete**: HTTP/1.0, 1.1, 2, and 3 with automatic ALPN negotiation

---

## Deep Dive

- [Request Pipeline](/architecture/pipeline) — full client pipeline flow with LikeC4 diagram
- [Protocol Engines](/architecture/engines) — engine internals and protocol selection
- [Handler Design](/architecture/handlers) — handler patterns and server request pipeline
- [E2E Scenarios](/architecture/scenarios) — end-to-end request scenarios
- [Extending the Pipeline](/architecture/extending) — custom stages and handlers
