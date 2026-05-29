# How It Works

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
[ApplicationBridgeStage] — bridges decoded HTTP to IFeatureCollection
    ↓
[ASP.NET Core Pipeline] — middleware, routing, parameter binding, endpoint execution
    ↓
[Response] — writes response back through the pipeline
```

Each connection is managed by a `ConnectionActor` that owns the full Akka.Streams graph for that connection — from transport bytes through to response serialisation.

::: tip Routing and Dispatching
Routing, parameter binding, and request dispatching are handled by standard ASP.NET Core — middleware, endpoint routing, and model binding. If you need actor-based request handling, the optional [Servus.Akka.AspNetCore](https://github.com/Aaronontheweb/Servus.Akka.AspNetCore) package provides `EntityDispatcher` and `AkkaResults` helpers for integrating Akka actors as endpoints.
:::

## Learn More

- [**Pipeline Details**](./pipeline) — All stages and how they interact
- [**Scenarios**](./scenarios) — End-to-end walkthroughs for HTTP/1.0, 1.1, 2, and 3
- [**Connection Pooling**](../client/connection-pooling) — How connections are reused
- [**Server Guide**](/server/) — middleware, routing, entity gateway
- [**Server Hosting & Lifecycle**](/server/hosting) — actor hierarchy and graceful shutdown
