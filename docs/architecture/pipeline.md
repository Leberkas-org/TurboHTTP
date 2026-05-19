# Full Pipeline Flow

The pipeline view shows every stage in TurboHTTP's request/response path — the forward flow of data, the backward flow of connection-reuse signals, and the short-circuit paths for cache hits, retries, and redirects.

<ClientOnly>
  <LikeC4Diagram viewId="pipelineFlow" :height="700" />
</ClientOnly>

---

## Request Chain (left to right / top to bottom)

Each `HttpRequestMessage` passes through the following stages before reaching the network:

| #   | Stage                                         | What it does                                                                                                                                                                                                                                        |
| --- | --------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1   | Request Enrichment (`RequestEnricher`)        | Applies your `BaseAddress`, default HTTP version, and default headers to every request                                                                                                                                                              |
| 2   | Tracing (`TracingBidiStage`)                  | Starts an activity span for observability; records request method, URL, timing, and final status code                                                                                                                                               |
| 3   | User Handlers (`HandlerBidiStage`)            | Runs any custom middleware you registered — zero or more, applied outermost-first                                                                                                                                                                   |
| 4   | Redirect (`RedirectBidiStage`)                | Tracks the redirect chain; on a `301`–`308` response, re-enters the pipeline at the Cookie stage so new-URL cookies are injected                                                                                                                    |
| 5   | Cookie Injection (`CookieBidiStage`)          | Looks up matching cookies for the target domain and path and adds a `Cookie` header                                                                                                                                                                 |
| 6   | Retry (`RetryBidiStage`)                      | On the request side, attaches retry context; on a transient failure, re-enters below the Cookie stage (same URL, cookies already set)                                                                                                               |
| 7   | Expect-Continue (`ExpectContinueBidiStage`)   | For requests with large bodies, sends `Expect: 100-continue` and holds the body until the server confirms it will accept it                                                                                                                         |
| 8   | Cache Lookup (`CacheBidiStage`)               | Checks the in-memory cache; on a **cache hit**, returns the cached response immediately — stages 9–11 are skipped entirely                                                                                                                          |
| 9   | Content Encoding (`ContentEncodingBidiStage`) | Compresses the request body if a compression policy is configured; on the response side, transparently decompresses `gzip`, `deflate`, or Brotli                                                                                                    |
| 10  | Alt-Svc Discovery (`AltSvcBidiStage`)         | Checks for cached Alt-Svc entries and upgrades the request version if a faster protocol is available; captures Alt-Svc headers from responses                                                                                                       |
| 11  | Version Router (`Engine`)                     | Routes the request to the correct protocol handler based on the requested HTTP version                                                                                                                                                              |
| 12  | Protocol ConnectionStage _(per version)_      | Unified stage that serialises the request to bytes, parses the response, and correlates request/response — then `TcpConnectionStage`/`QuicConnectionStage` (from Servus.Akka) handles the network connection                                       |

---

## Response Chain (right to left / bottom to top)

After bytes return from the network, the response passes back through the stages in reverse order:

| #   | Stage                                         | What it does                                                                                                                       |
| --- | --------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| 1   | Protocol ConnectionStage _(per version)_      | Parses raw bytes into an `HttpResponseMessage` and matches it to the original request                                              |
| 2   | Alt-Svc Discovery (`AltSvcBidiStage`)         | Captures Alt-Svc response headers and caches them for future requests                                                              |
| 3   | Content Encoding (`ContentEncodingBidiStage`) | Transparently decompresses `gzip`, `deflate`, or Brotli response bodies                                                            |
| 4   | Cache Storage (`CacheBidiStage`)              | Saves cacheable responses so future matching requests can be served from memory                                                    |
| 5   | Expect-Continue (`ExpectContinueBidiStage`)   | Processes `100 Continue` responses and unblocks the request body when the server is ready                                          |
| 6   | Automatic Retry (`RetryBidiStage`)            | Re-sends safe (idempotent) requests on transient errors, `408`, or `503` responses; respects `Retry-After` delays                  |
| 7   | Cookie Storage (`CookieBidiStage`)            | Reads `Set-Cookie` headers and stores cookies for future requests                                                                  |
| 8   | Redirect Following (`RedirectBidiStage`)      | Follows `301`–`308` redirects automatically; rewrites the HTTP method where needed; detects loops and blocks HTTPS→HTTP downgrades |
| 9   | Tracing (`TracingBidiStage`)                  | Closes the activity span, recording the final status code and any errors                                                           |

---

## Feedback Loops

Three feedback paths create non-linear behaviour in the pipeline:

### 1. Cache Hit Short-Circuit (amber)

`CacheBidiStage` checks the in-memory `HttpCacheStore` on every request. If the stored response is still fresh, it is returned immediately. The request never reaches the `Engine` or the network.

If the cache entry is stale but has an `ETag` or `Last-Modified` validator, `CacheBidiStage` emits a conditional request (`If-None-Match` / `If-Modified-Since`). On a `304 Not Modified` response, `CacheBidiStage` merges the new headers into the cached entry and returns it.

### 2. Keep-Alive Feedback (HTTP/1.1 only)

After each HTTP/1.1 response, `Http11ConnectionStage` evaluates the `Connection` header internally and decides whether to reuse the TCP connection for the next request or close it and request a new one from the connection manager actor.

This loop is invisible to the caller — the `Engine` and higher layers see only a continuous stream of `HttpResponseMessage` objects.

### 3. Redirect Re-entry

When `RedirectBidiStage` needs to follow a redirect, the new `HttpRequestMessage` re-enters the pipeline at the **Cookie stage** — not at the very beginning. This ensures cookie injection applies for the new URL while skipping the initial enrichment step (default headers are already applied). A redirect hop limit prevents infinite loops.

### 4. Retry Re-entry

When `RetryBidiStage` decides a request should be retried, it re-enters the pipeline at the **Expect-Continue stage** — below Cookie injection, since the URL hasn't changed and cookies are already set. A maximum retry count prevents infinite loops.

### 5. QPACK Table Sync (HTTP/3 only)

HTTP/3 uses QPACK for header compression. The server sends decoder table updates on a dedicated QUIC stream; `Http30ConnectionStage` processes these updates internally to keep the encoder and decoder dynamic tables in sync.

---

## Connection Management

The pipeline uses actor-based connection managers (from Servus.Akka) to reuse TCP (or QUIC for HTTP/3) connections efficiently:

- **HTTP/1.0**: Each request gets a new connection; it's closed after the response
- **HTTP/1.1**: Connections are kept alive and reused for multiple requests to the same host
- **HTTP/2 & HTTP/3**: A single connection is shared by multiple concurrent requests as independent streams

This all happens automatically. You don't manage connections — TurboHTTP does.

See [Connection Pooling Guide](../client/connection-pooling) for tuning options.

---

## Server Pipeline

The server pipeline mirrors the client architecture, transforming incoming bytes into responses:

```
Incoming TCP/QUIC Bytes
    ↓
[Transport] — accepts connection; ListenerActor spawns ConnectionActor
    ↓
[ProtocolRouter] — detects HTTP version from initial bytes
    ↓
[Server Protocol Engine] — Http10/11/20/30ServerEngine decodes request, encodes response
    ↓
[HttpContextBidiStage] — wraps parsed request as TurboHttpContext (request/response object)
    ↓
[MiddlewarePipelineStage] — runs registered middleware (Use/Run/Map/MapWhen)
    ↓
[RoutingStage] — matches request path to registered route pattern
    ↓
[DispatcherStage] — delegates to DelegateDispatcher (handler function) or EntityDispatcher (actor)
    ↓
[Handler / Entity Actor] — executes your code; returns response
    ↓
[Server Protocol Engine] — encodes response to bytes
    ↓
Outgoing TCP/QUIC Bytes
```

Each connection is bound to a single `ConnectionActor` that owns the entire Akka.Streams graph — from transport bytes through protocol parsing, middleware execution, routing, and response serialisation.

### Server Pipeline Stages

| Stage                        | Role                                                                                                                                                                                                                |
| ---------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ProtocolRouter`             | Inspects initial bytes to detect HTTP/1.0, 1.1, 2, or 3; routes to the appropriate server engine state machine                                                                                                     |
| `Http*ServerEngine`          | Protocol-specific state machine: parses request bytes, manages connection/stream-level flow control, encodes response frames                                                                                        |
| `HttpContextBidiStage`       | Wraps the parsed protocol request as a `TurboHttpContext` object with `.Request` and `.Response` properties                                                                                                         |
| `MiddlewarePipelineStage`    | Runs all registered middleware in order (outermost-first for request, innermost-first for response). Middleware can short-circuit by not calling `next(ctx)`                                                          |
| `RoutingStage`               | Matches the request path against registered route patterns; extracts route parameters (`{id}`, etc.) into `ctx.RouteValues`                                                                                        |
| `DispatcherStage`            | Selects and invokes the handler: `DelegateDispatcher` for function-based routes (`MapTurboGet`), `EntityDispatcher` for actor-based routes (`MapTurboEntity`)                                                         |
| `ParameterBindingStage`      | (within dispatcher) Binds route parameters, query string, body, and headers to handler parameters using reflection and model binding                                                                                 |

After the handler returns a response, the response object flows back through the pipeline in reverse — middleware response hooks can transform or log the response, and the protocol engine serialises it back to wire bytes.
