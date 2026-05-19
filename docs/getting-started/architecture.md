# Architecture Overview

TurboHTTP handles HTTP requests through a simple pipeline. You send a request, and the library automatically handles cookies, caching, retries, redirects, and connection reuse — all transparently.

<ClientOnly>
  <LikeC4Diagram viewId="index" :height="300" />
</ClientOnly>

## The Request Pipeline

When you call `SendAsync()`, your request passes through a series of processing stages:

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

## Key Characteristics

- **Automatic**: Cookies, caching, retries, redirects all work out of the box
- **Efficient**: HTTP/2 and HTTP/3 multiplexing, keep-alive connection reuse, lock-free data movement
- **Correct**: Follows HTTP specifications for freshness, method rewriting, retry idempotency
- **Observable**: See exactly what happens at each stage via built-in tracing

## The Server Pipeline

When a request arrives at TurboHTTP Server, it passes through a complementary pipeline:

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

## Server Architecture

TurboHTTP Server is a standalone HTTP server — it does not use Kestrel. Instead, it uses its own transport layer via Servus.Akka.Transport.

### Actor Hierarchy

```
ServerSupervisorActor
├── ListenerActor (one per endpoint)
│   ├── ConnectionActor (one per client)
│   └── ConnectionActor
└── ListenerActor
    └── ConnectionActor
```

- **ServerSupervisorActor** — manages all listeners and tracks connection counts
- **ListenerActor** — binds TCP or QUIC transport, accepts incoming connections
- **ConnectionActor** — manages a single client connection, materializes the protocol + pipeline graph

### Transport Layer

- **TCP**: `TcpListenerFactory` handles HTTP/1.0, HTTP/1.1, and HTTP/2 connections
- **QUIC**: `QuicListenerFactory` handles HTTP/3 connections

Protocol engines (`Http10ServerEngine`, `Http11ServerEngine`, `Http20ServerEngine`, `Http30ServerEngine`) are selected via ALPN negotiation when TLS is enabled.

## Deep Dive

- [Request Pipeline](/architecture/pipeline) — full pipeline flow with LikeC4 diagram
- [Protocol Engines](/architecture/engines) — engine internals and selection
- [Handler Design](/architecture/handlers) — handler patterns
- [E2E Scenarios](/architecture/scenarios) — end-to-end request scenarios
- [Extending the Pipeline](/architecture/extending) — custom stages and handlers
