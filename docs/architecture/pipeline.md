# Full Pipeline Flow

The pipeline view shows every stage in TurboHttp's request/response path — the forward flow of data, the backward flow of connection-reuse signals, and the short-circuit paths for cache hits, retries, and redirects.

<ClientOnly>
  <LikeC4Diagram viewId="pipelineFlow" :height="700" />
</ClientOnly>

---

## Request Chain (left to right / top to bottom)

Each `HttpRequestMessage` passes through the following stages before reaching the network:

| # | Stage | What it does |
|---|-------|--------------|
| 1 | `RequestEnricherStage` | Applies `BaseAddress`, `DefaultRequestVersion`, `DefaultRequestHeaders` to every request |
| 2 | `CookieInjectionStage` | Looks up matching cookies in `CookieJar` (domain + path + Secure/HttpOnly rules) and injects a `Cookie` header |
| 3 | `CacheLookupStage` | Checks `HttpCacheStore`; on a **cache hit**, returns the cached response immediately — stages 4 onward are bypassed entirely |
| 4 | `Engine` | Demultiplexes by `HttpRequestMessage.Version`; routes to `Http10Engine`, `Http11Engine`, or `Http20Engine` |
| 5 | *(version engine)* | Encodes the request to bytes and sends it through `ConnectionStage` to TCP |

---

## Response Chain (right to left / bottom to top)

After bytes return from TCP, the response passes through these stages:

| # | Stage | What it does |
|---|-------|--------------|
| 1 | *(version decoder)* | Parses raw bytes into `HttpResponseMessage`; correlates response with the originating request |
| 2 | `DecompressionStage` | Decompresses `gzip`, `deflate`, or `br` content encodings transparently |
| 3 | `CookieStorageStage` | Parses `Set-Cookie` response headers; stores cookies in `CookieJar` with full RFC 6265 attribute evaluation |
| 4 | `CacheStorageStage` | Stores cacheable responses in `HttpCacheStore`; respects `Cache-Control`, `Vary`, `Expires` directives |
| 5 | `RetryStage` | On transient errors or `503`/`429` responses, re-queues idempotent requests; parses `Retry-After` header for delay |
| 6 | `RedirectStage` | Follows `301`, `302`, `303`, `307`, `308` responses; rewrites method per RFC 9110 §15.4; detects redirect loops; blocks HTTPS→HTTP downgrade |

---

## Feedback Loops

Three feedback paths create non-linear behaviour in the pipeline:

### 1. Cache Hit Short-Circuit (amber)

`CacheLookupStage` checks the in-memory `HttpCacheStore` on every request. If the stored response is still fresh (per RFC 9111 §4.2 freshness calculation), it is returned immediately. The request never reaches the `Engine` or the network.

If the cache entry is stale but has an `ETag` or `Last-Modified` validator, `CacheLookupStage` emits a conditional request (`If-None-Match` / `If-Modified-Since`). On a `304 Not Modified` response, `CacheStorageStage` merges the new headers into the cached entry and returns it.

### 2. Keep-Alive Feedback (HTTP/1.1 only)

After the HTTP/1.1 decoder processes a response, `CorrelationHttp1XStage` emits a keep-alive or close signal (via `MergePreferred`) that feeds back into `ConnectionStage`. `ConnectionStage` uses this signal to decide whether to keep the TCP connection open for the next request or close it and request a new connection from `HostPoolActor`.

This loop is invisible to the caller — the `Engine` and higher layers see only a continuous stream of `HttpResponseMessage` objects.

### 3. Retry / Redirect Re-entry (red)

When `RetryStage` decides a request should be retried, or when `RedirectStage` needs to follow a redirect, the new `HttpRequestMessage` is fed back into the **front of the pipeline** (before `RequestEnricherStage`). This ensures that retry enrichment, cookie injection, and cache lookup all apply again on the new attempt.

A maximum retry count and redirect hop limit prevent infinite loops.

---

## Connection Pool Integration

`ConnectionStage` does not create TCP connections directly. It sends an `EnsureHost` message to `PoolRouterActor` and waits for a `ConnectionReady(ConnectionHandle)` reply. The I/O actor pool manages:

- Creating new connections when all existing ones are busy
- Enforcing per-host connection limits (`PerHostConnectionLimiter`)
- Reconnecting with exponential backoff after failures
- Evicting idle connections after a configurable timeout

Once `ConnectionHandle` is delivered to `ConnectionStage`, all further data movement bypasses the actor mailbox entirely — bytes travel through `System.Threading.Channels` at full throughput.
