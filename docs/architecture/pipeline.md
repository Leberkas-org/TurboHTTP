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
| 1 | Request Enrichment (`RequestEnricherStage`) | Applies your `BaseAddress`, default HTTP version, and default headers to every request |
| 2 | Cookie Injection (`CookieBidiStage`) | Looks up matching cookies for the target domain and path and adds a `Cookie` header |
| 3 | Cache Lookup (`CacheBidiStage`) | Checks the in-memory cache; on a **cache hit**, returns the cached response immediately — stages 4–5 are skipped entirely |
| 4 | Version Router (`Engine`) | Routes the request to the correct protocol handler based on the requested HTTP version |
| 5 | Protocol Encoder *(per version)* | Serialises the request to bytes and sends it over the network connection |

---

## Response Chain (right to left / bottom to top)

After bytes return from TCP, the response passes through these stages:

| # | Stage | What it does |
|---|-------|--------------|
| 1 | Protocol Decoder *(per version)* | Parses raw bytes into an `HttpResponseMessage` and matches it to the original request |
| 2 | Decompression (`DecompressionBidiStage`) | Transparently decompresses `gzip`, `deflate`, or Brotli response bodies |
| 3 | Cookie Storage (`CookieBidiStage`) | Reads `Set-Cookie` headers and stores cookies for future requests |
| 4 | Cache Storage (`CacheBidiStage`) | Saves cacheable responses so future matching requests can be served from memory |
| 5 | Automatic Retry (`RetryBidiStage`) | Re-sends safe (idempotent) requests on transient errors or `503`/`429` responses; respects `Retry-After` delays |
| 6 | Redirect Following (`RedirectBidiStage`) | Follows `301`–`308` redirects automatically; rewrites the HTTP method where needed; detects loops and blocks HTTPS→HTTP downgrades |

---

## Feedback Loops

Three feedback paths create non-linear behaviour in the pipeline:

### 1. Cache Hit Short-Circuit (amber)

`CacheBidiStage` checks the in-memory `HttpCacheStore` on every request. If the stored response is still fresh, it is returned immediately. The request never reaches the `Engine` or the network.

If the cache entry is stale but has an `ETag` or `Last-Modified` validator, `CacheBidiStage` emits a conditional request (`If-None-Match` / `If-Modified-Since`). On a `304 Not Modified` response, `CacheBidiStage` merges the new headers into the cached entry and returns it.

### 2. Keep-Alive Feedback (HTTP/1.1 only)

After each HTTP/1.1 response, a signal is sent back to the connection layer indicating whether the connection should stay open or be closed. The connection stage uses this signal to decide whether to reuse the TCP connection for the next request or close it and request a new one from the pool.

This loop is invisible to the caller — the `Engine` and higher layers see only a continuous stream of `HttpResponseMessage` objects.

### 3. Retry / Redirect Re-entry (red)

When `RetryBidiStage` decides a request should be retried, or when `RedirectBidiStage` needs to follow a redirect, the new `HttpRequestMessage` is fed back into the **front of the pipeline** (before `RequestEnricherStage`). This ensures that retry enrichment, cookie injection, and cache lookup all apply again on the new attempt.

A maximum retry count and redirect hop limit prevent infinite loops.

---

## Connection Pool Integration

`ConnectionStage` does not create TCP connections directly. It sends an `EnsureHost` message to `PoolRouter` and waits for a `ConnectionReady(ConnectionHandle)` reply. The I/O actor pool manages:

- Creating new connections when all existing ones are busy
- Enforcing per-host connection limits (`PerHostConnectionLimiter`)
- Reconnecting with exponential backoff after failures
- Evicting idle connections after a configurable timeout

Once `ConnectionHandle` is delivered to `ConnectionStage`, all further data movement bypasses the actor mailbox entirely — bytes travel through `System.Threading.Channels` at full throughput.
