# TurboHttpClient Integration Test & RFC Compliance Migration Plan

> **Goal**: Wire all existing protocol handlers into the Akka.Streams pipeline,
> build clean end-to-end integration tests against real Kestrel servers,
> and achieve 100 % RFC compliance for Http10Engine, Http11Engine, and Http20Engine
> with Caching, Redirect, Cookies, Connection Reuse, Retry, and Content Encoding.

---

## Table of Contents

1. [Current State Assessment](#1-current-state-assessment)
2. [Target Architecture](#2-target-architecture)
3. [Phase 1 — New Akka.Streams Stages](#phase-1--new-akkastreams-stages-8-stages)
4. [Phase 2 — Engine Pipeline Wiring](#phase-2--engine-pipeline-wiring)
5. [Phase 3 — Kestrel Fixture Routes](#phase-3--kestrel-fixture-routes)
6. [Phase 4 — Http10Engine Integration Tests](#phase-4--http10engine-integration-tests)
7. [Phase 5 — Http11Engine Integration Tests](#phase-5--http11engine-integration-tests)
8. [Phase 6 — Http20Engine Integration Tests](#phase-6--http20engine-integration-tests)
9. [Phase 7 — Cross-Engine & TurboHttpClient Tests](#phase-7--cross-engine--turbohttpclient-tests)
10. [Phase 8 — Validation Gate](#phase-8--validation-gate)
11. [Dependency Graph](#dependency-graph)

---

## 1. Current State Assessment

### Protocol Handlers (Implemented, NOT wired into pipeline)

| Handler | RFC | Unit Tests | Location | Pipeline Status |
|---------|-----|-----------|----------|----------------|
| RedirectHandler | RFC 9110 §15.4 | 51 | Protocol/RedirectHandler.cs | **NOT WIRED** |
| CookieJar | RFC 6265 | 42 | Protocol/CookieJar.cs | **NOT WIRED** |
| RetryEvaluator | RFC 9110 §9.2 | 40 | Protocol/RetryEvaluator.cs | **NOT WIRED** |
| ConnectionReuseEvaluator | RFC 9112 §9 | 25 | Protocol/ConnectionReuseEvaluator.cs | **NOT WIRED** |
| PerHostConnectionLimiter | RFC 9112 §9.4 | 18 | Protocol/PerHostConnectionLimiter.cs | **NOT WIRED** |
| ContentEncodingDecoder | RFC 9110 §8.4 | 18+ | Protocol/ContentEncodingDecoder.cs | **Partial** (Http20StreamStage only) |
| CacheFreshnessEvaluator | RFC 9111 §4.2 | 0 | Protocol/CacheFreshnessEvaluator.cs | **NOT WIRED** |
| HttpCacheStore | RFC 9111 §3 | 0 | Protocol/HttpCacheStore.cs | **NOT WIRED** |
| CacheValidationRequestBuilder | RFC 9111 §4.3 | 0 | Protocol/CacheValidationRequestBuilder.cs | **NOT WIRED** |
| CacheControlParser | RFC 9111 §5.2 | 0 | Protocol/CacheControlParser.cs | **NOT WIRED** |

### Current Engine Pipeline (encode → TCP → decode → correlate ONLY)

```
RequestEnricherStage
    ↓
Partition<HttpRequestMessage> by Version
    ├→ Http10Engine (Encoder → TCP → Decoder → Correlation)
    ├→ Http11Engine (Encoder → TCP → Decoder → Correlation)
    ├→ Http20Engine (StreamIdAlloc → Request2Frame → Connection → Encoder → TCP → Decoder → Stream → Correlation)
    └→ Http30Engine (stub)
    ↓
Merge<HttpResponseMessage>
```

**Missing**: No redirect loop, no cookie injection/storage, no retry loop, no cache lookup/store, no decompression (except partial HTTP/2), no connection reuse evaluation.

### Integration Test Infrastructure

| Fixture | Routes | Actual Tests Using It |
|---------|--------|----------------------|
| KestrelFixture (HTTP/1.x) | 40+ routes | **0 tests** |
| KestrelH2Fixture (HTTP/2) | 18+ routes | **0 tests** |
| KestrelTlsFixture (HTTPS) | 40+ routes | **0 tests** |

**Critical Gap**: Zero end-to-end pipeline tests exist.

---

## 2. Target Architecture

### Full Pipeline (after wiring)

```
User Request (HttpRequestMessage)
    ↓
RequestEnricherStage (BaseAddress, Version, DefaultHeaders)
    ↓
CookieInjectionStage (CookieJar.AddCookiesToRequest)
    ↓
CacheLookupStage (HttpCacheStore.Get → CacheFreshnessEvaluator)
    ├→ FRESH → emit cached HttpResponseMessage (skip engine)
    └→ MISS/STALE → continue to engine (conditional request if stale)
    ↓
┌────────────────────────────────────────────┐
│  RetryStage (wraps engine, retries on 408/503)  │
│      ↓                                     │
│  Engine (partition by version)             │
│      ├→ Http10Engine                       │
│      ├→ Http11Engine                       │
│      └→ Http20Engine                       │
│      ↓                                     │
│  ConnectionReuseStage (evaluate reuse)     │
└────────────────────────────────────────────┘
    ↓
DecompressionStage (gzip/deflate/brotli via ContentEncodingDecoder)
    ↓
CookieStorageStage (CookieJar.ProcessResponse)
    ↓
CacheStorageStage (HttpCacheStore.Put, merge 304)
    ↓
RedirectStage (detect 3xx → loop back to CookieInjectionStage)
    ↓
HttpResponseMessage → User
```

### Design Principles

1. **Each stage = one responsibility** — stateless where possible, call existing protocol handlers
2. **Stages are composable** — each can be tested in isolation with mocked upstream/downstream
3. **Feature flags** — each stage is optional (disabled by default for backward compat)
4. **No breaking changes** — existing tests continue to pass without modification

---

## Phase 1 — New Akka.Streams Stages (8 stages)

All new stages go in `src/TurboHttp/Streams/Stages/`.

### 1.1 CookieInjectionStage

- [ ] **File**: `Streams/Stages/CookieInjectionStage.cs`
- [ ] `GraphStage<FlowShape<HttpRequestMessage, HttpRequestMessage>>`
- [ ] On push: call `CookieJar.AddCookiesToRequest(request.RequestUri, ref request)`; push downstream
- [ ] Constructor takes `CookieJar` instance
- [ ] When `CookieJar` is null, pass-through (no-op)

### 1.2 CookieStorageStage

- [ ] **File**: `Streams/Stages/CookieStorageStage.cs`
- [ ] `GraphStage<FlowShape<HttpResponseMessage, HttpResponseMessage>>`
- [ ] On push: call `CookieJar.ProcessResponse(response.RequestMessage.RequestUri, response)`; push downstream
- [ ] Side-effect only — response is NOT modified

### 1.3 DecompressionStage

- [ ] **File**: `Streams/Stages/DecompressionStage.cs`
- [ ] `GraphStage<FlowShape<HttpResponseMessage, HttpResponseMessage>>`
- [ ] On push: read `Content-Encoding` header → call `ContentEncodingDecoder.Decompress(body, encoding)`
- [ ] Replace `response.Content` with decompressed `ByteArrayContent`
- [ ] Remove `Content-Encoding` header, update `Content-Length`
- [ ] Pass-through if no `Content-Encoding` or already `identity`
- [ ] Handles: gzip, deflate, br (brotli), x-gzip

### 1.4 CacheLookupStage

- [ ] **File**: `Streams/Stages/CacheLookupStage.cs`
- [ ] `GraphStage<FanOutShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage>>`
- [ ] Outlet 0 (cache miss): request passes to engine
- [ ] Outlet 1 (cache hit): emit cached `HttpResponseMessage` directly
- [ ] On push: call `CacheFreshnessEvaluator.Evaluate(entry, request, now)`
  - **Fresh** → push to outlet 1 (response)
  - **MustRevalidate** → build conditional request via `CacheValidationRequestBuilder`, push to outlet 0
  - **Miss** → push to outlet 0 (original request)
- [ ] Constructor takes `HttpCacheStore`, `CachePolicy`

### 1.5 CacheStorageStage

- [ ] **File**: `Streams/Stages/CacheStorageStage.cs`
- [ ] `GraphStage<FlowShape<HttpResponseMessage, HttpResponseMessage>>`
- [ ] On push: if `304 Not Modified` → merge with cached entry via `CacheValidationRequestBuilder.MergeNotModifiedResponse`
- [ ] If `2xx` → call `HttpCacheStore.Put(request, response, body, requestTime, responseTime)`
- [ ] If unsafe method (POST/PUT/DELETE/PATCH) → call `HttpCacheStore.Invalidate(uri)`
- [ ] Push response downstream (possibly merged 200 from 304)

### 1.6 RedirectStage

- [ ] **File**: `Streams/Stages/RedirectStage.cs`
- [ ] `GraphStage<FlowShape<HttpResponseMessage, HttpResponseMessage>>`
- [ ] **Internal feedback loop** (most complex stage):
  - On push: if `RedirectHandler.IsRedirect(response)` → call `BuildRedirectRequest`
  - Re-emit redirect request upstream (via feedback port or internal re-request mechanism)
  - Track redirect count via `RedirectHandler.RedirectCount`
  - On max redirects or non-redirect → push final response downstream
- [ ] **Alternative**: Implement as `BidiShape` that wraps the engine, re-injecting redirect requests
- [ ] Constructor takes `RedirectHandler` (or `RedirectPolicy`)
- [ ] Integration with `CookieJar` for cross-redirect cookie handling

### 1.7 RetryStage

- [ ] **File**: `Streams/Stages/RetryStage.cs`
- [ ] `GraphStage<FlowShape<HttpResponseMessage, HttpResponseMessage>>`
- [ ] **Internal feedback loop**:
  - On push: call `RetryEvaluator.Evaluate(request, response, networkFailure, ...)`
  - If `ShouldRetry` → re-emit original request upstream (after optional `RetryAfter` delay)
  - If not → push response downstream
- [ ] **Alternative**: Wrap as a `RestartFlow` or `RecoverWith` pattern
- [ ] Constructor takes `RetryPolicy`
- [ ] Must preserve original `HttpRequestMessage` for re-emission

### 1.8 ConnectionReuseStage

- [ ] **File**: `Streams/Stages/ConnectionReuseStage.cs`
- [ ] `GraphStage<FlowShape<HttpResponseMessage, HttpResponseMessage>>`
- [ ] On push: call `ConnectionReuseEvaluator.Evaluate(response, version, bodyFullyConsumed)`
- [ ] If `CanReuse` → signal connection pool to keep connection alive
- [ ] If not → signal connection pool to close connection
- [ ] Side-effect: signals via `ITransportItem` or actor message to `ClientManager`

### Stage Unit Tests (StreamTests)

For each new stage, create a corresponding test file in `src/TurboHttp.StreamTests/`:

- [ ] `Stages/CookieInjectionStageTests.cs` (~8 tests)
- [ ] `Stages/CookieStorageStageTests.cs` (~6 tests)
- [ ] `Stages/DecompressionStageTests.cs` (~10 tests)
- [ ] `Stages/CacheLookupStageTests.cs` (~12 tests)
- [ ] `Stages/CacheStorageStageTests.cs` (~10 tests)
- [ ] `Stages/RedirectStageTests.cs` (~15 tests)
- [ ] `Stages/RetryStageTests.cs` (~10 tests)
- [ ] `Stages/ConnectionReuseStageTests.cs` (~8 tests)

**Subtotal**: ~79 new stage unit tests

---

## Phase 2 — Engine Pipeline Wiring

### 2.1 TurboClientOptions Extension

- [ ] **File**: `Client/TurboClientOptions.cs`
- [ ] Add feature flags (all default `false` for backward compat):
  ```
  bool EnableRedirectHandling { get; init; } = false;
  RedirectPolicy? RedirectPolicy { get; init; }
  bool EnableCookies { get; init; } = false;
  bool EnableRetry { get; init; } = false;
  RetryPolicy? RetryPolicy { get; init; }
  bool EnableCaching { get; init; } = false;
  CachePolicy? CachePolicy { get; init; }
  bool EnableDecompression { get; init; } = false;
  ConnectionPolicy? ConnectionPolicy { get; init; }
  ```

### 2.2 Engine.CreateFlow Enhancement

- [ ] **File**: `Streams/Engine.cs`
- [ ] Accept `TurboClientOptions` (or a new `PipelineOptions` record)
- [ ] Conditionally insert stages based on feature flags:
  ```
  RequestEnricherStage
      ↓ (if EnableCookies)
  CookieInjectionStage
      ↓ (if EnableCaching)
  CacheLookupStage ──(hit)──→ merge point
      ↓ (miss)
  [Engine core: partition → protocol engines → merge]
      ↓ (if EnableDecompression)
  DecompressionStage
      ↓ (if EnableCookies)
  CookieStorageStage
      ↓ (if EnableCaching)
  CacheStorageStage
      ↓ (if EnableRedirectHandling)
  RedirectStage (may loop back)
      ↓
  merge point ← (cache hit)
      ↓
  HttpResponseMessage
  ```
- [ ] Retry wraps the engine core (not a linear stage)

### 2.3 TurboClientStreamManager Activation

- [ ] **File**: `Client/TurboClientStreamManager.cs`
- [ ] Uncomment and complete the graph materialization code
- [ ] Wire: `ChannelSource → RequestEnricher → Engine.CreateFlow(options) → ChannelSink`
- [ ] Pass `CookieJar`, `HttpCacheStore` instances from `TurboClientOptions` or create defaults

### 2.4 Shared State Instances

- [ ] `CookieJar` — one per `TurboHttpClient` instance (thread-safe)
- [ ] `HttpCacheStore` — one per `TurboHttpClient` instance (thread-safe, LRU)
- [ ] `RedirectHandler` — one per request chain (stateful: redirect count) → create per-request
- [ ] `PerHostConnectionLimiter` — one per `TurboHttpClient` (thread-safe)

---

## Phase 3 — Kestrel Fixture Routes

### 3.1 Redirect Routes (KestrelFixture + KestrelH2Fixture)

- [ ] `GET /redirect/{code}/{target}` — respond with status `{code}`, `Location: {target}`
- [ ] `GET /redirect/chain/{n}` — chain of `n` redirects: /redirect/chain/3 → /redirect/chain/2 → /redirect/chain/1 → /hello
- [ ] `GET /redirect/loop` — infinite loop: /redirect/loop → /redirect/loop
- [ ] `GET /redirect/relative` — redirect with relative Location header
- [ ] `GET /redirect/cross-scheme` — HTTPS → HTTP downgrade redirect
- [ ] `POST /redirect/307` — 307 preserves method & body
- [ ] `POST /redirect/303` — 303 changes to GET

### 3.2 Cookie Routes (KestrelFixture + KestrelH2Fixture)

- [ ] `GET /cookie/set/{name}/{value}` — Set-Cookie: {name}={value}; Path=/
- [ ] `GET /cookie/set-secure/{name}/{value}` — Set-Cookie with Secure flag
- [ ] `GET /cookie/set-httponly/{name}/{value}` — Set-Cookie with HttpOnly flag
- [ ] `GET /cookie/set-samesite/{name}/{value}/{policy}` — Set-Cookie with SameSite
- [ ] `GET /cookie/set-expires/{name}/{value}/{seconds}` — Set-Cookie with Max-Age
- [ ] `GET /cookie/set-domain/{name}/{value}/{domain}` — Set-Cookie with Domain
- [ ] `GET /cookie/set-path/{name}/{value}/{path}` — Set-Cookie with Path
- [ ] `GET /cookie/echo` — return all received Cookie headers as JSON body
- [ ] `GET /cookie/set-multiple` — multiple Set-Cookie headers in one response
- [ ] `GET /cookie/delete/{name}` — Set-Cookie with Max-Age=0 (deletion)

### 3.3 Retry Routes (KestrelFixture + KestrelH2Fixture)

- [ ] `GET /retry/408` — respond with 408 Request Timeout
- [ ] `GET /retry/503` — respond with 503 Service Unavailable
- [ ] `GET /retry/503-retry-after/{seconds}` — 503 with Retry-After header
- [ ] `GET /retry/503-retry-after-date` — 503 with Retry-After as HTTP-date
- [ ] `GET /retry/succeed-after/{n}` — fail first N-1 times with 503, then 200
- [ ] `POST /retry/non-idempotent-503` — 503 on POST (should NOT retry)

### 3.4 Cache Routes (KestrelFixture + KestrelH2Fixture)

- [ ] `GET /cache/max-age/{seconds}` — Cache-Control: max-age={seconds}, body = timestamp
- [ ] `GET /cache/no-cache` — Cache-Control: no-cache
- [ ] `GET /cache/no-store` — Cache-Control: no-store (already exists in KestrelFixture)
- [ ] `GET /cache/etag/{id}` — ETag header, supports If-None-Match → 304
- [ ] `GET /cache/last-modified/{id}` — Last-Modified, supports If-Modified-Since → 304
- [ ] `GET /cache/vary/{header}` — Vary: {header}, body changes based on header value
- [ ] `GET /cache/must-revalidate` — Cache-Control: max-age=0, must-revalidate
- [ ] `GET /cache/s-maxage/{seconds}` — Cache-Control: s-maxage={seconds}
- [ ] `GET /cache/expires` — Expires header (absolute date)
- [ ] `GET /cache/private` — Cache-Control: private

### 3.5 Content Encoding Routes (KestrelFixture + KestrelH2Fixture)

- [ ] `GET /compress/gzip/{kb}` — gzip-compressed response
- [ ] `GET /compress/deflate/{kb}` — deflate-compressed response
- [ ] `GET /compress/br/{kb}` — brotli-compressed response
- [ ] `GET /compress/identity/{kb}` — no compression (control)
- [ ] `GET /compress/negotiate` — honor Accept-Encoding, respond with matching encoding

### 3.6 Connection Reuse Routes (KestrelFixture only, HTTP/1.x)

- [ ] `GET /conn/keep-alive` — explicit Connection: Keep-Alive header (HTTP/1.0)
- [ ] `GET /conn/close` — explicit Connection: close header
- [ ] `GET /conn/default` — no Connection header (HTTP/1.1 default keep-alive)
- [ ] `GET /conn/upgrade-101` — 101 Switching Protocols (connection not reusable)

---

## Phase 4 — Http10Engine Integration Tests

**File location**: `src/TurboHttp.IntegrationTests/Http10/`
**Fixture**: `KestrelFixture` with `request.Version = HttpVersion.Version10`

### 4.1 Basic RFC 1945 Compliance

- [ ] **File**: `01_Http10BasicTests.cs`
- [ ] `[Fact] RFC1945-BASIC-001: GET request returns 200 with body`
- [ ] `[Fact] RFC1945-BASIC-002: HEAD request returns headers without body`
- [ ] `[Fact] RFC1945-BASIC-003: POST request sends body and receives response`
- [ ] `[Fact] RFC1945-BASIC-004: PUT request sends body`
- [ ] `[Fact] RFC1945-BASIC-005: DELETE request returns success`
- [ ] `[Theory] RFC1945-BASIC-006: Status codes 200, 201, 204, 301, 302, 400, 404, 500`
- [ ] `[Fact] RFC1945-BASIC-007: Large response body (100KB)`
- [ ] `[Fact] RFC1945-BASIC-008: Request with custom headers preserved`
- [ ] `[Fact] RFC1945-BASIC-009: Multi-value response headers parsed correctly`
- [ ] `[Fact] RFC1945-BASIC-010: Empty response body with Content-Length: 0`

### 4.2 Connection Management (RFC 1945 — no keep-alive by default)

- [ ] **File**: `02_Http10ConnectionTests.cs`
- [ ] `[Fact] RFC1945-CONN-001: Connection closed after each response (no keep-alive)`
- [ ] `[Fact] RFC1945-CONN-002: Connection: Keep-Alive opt-in honored`
- [ ] `[Fact] RFC1945-CONN-003: Sequential requests each open new connection`
- [ ] `[Fact] RFC1945-CONN-004: Connection reuse with Keep-Alive header`
- [ ] `[Fact] RFC1945-CONN-005: Server Connection: close overrides Keep-Alive`

### 4.3 Redirect (RFC 9110 §15.4 via HTTP/1.0)

- [ ] **File**: `03_Http10RedirectTests.cs`
- [ ] `[Theory] RFC9110-REDIR-001: 301/302 GET follows redirect`
- [ ] `[Fact] RFC9110-REDIR-002: 302 POST changes to GET`
- [ ] `[Fact] RFC9110-REDIR-003: Redirect chain (3 hops)`
- [ ] `[Fact] RFC9110-REDIR-004: Redirect loop detected and throws`
- [ ] `[Fact] RFC9110-REDIR-005: Max redirect limit enforced`
- [ ] `[Fact] RFC9110-REDIR-006: Relative Location resolved correctly`
- [ ] `[Fact] RFC9110-REDIR-007: Authorization header stripped on cross-origin redirect`

### 4.4 Cookies (RFC 6265 via HTTP/1.0)

- [ ] **File**: `04_Http10CookieTests.cs`
- [ ] `[Fact] RFC6265-COOK-001: Set-Cookie stored and sent on subsequent request`
- [ ] `[Fact] RFC6265-COOK-002: Multiple cookies accumulated across responses`
- [ ] `[Fact] RFC6265-COOK-003: Cookie with Path attribute restricts scope`
- [ ] `[Fact] RFC6265-COOK-004: Cookie with Max-Age=0 deletes cookie`
- [ ] `[Fact] RFC6265-COOK-005: Expired cookie not sent`
- [ ] `[Fact] RFC6265-COOK-006: Cookies preserved across redirects`

### 4.5 Retry (RFC 9110 §9.2 via HTTP/1.0)

- [ ] **File**: `05_Http10RetryTests.cs`
- [ ] `[Fact] RFC9110-RETRY-001: GET retried on 503`
- [ ] `[Fact] RFC9110-RETRY-002: GET retried on 408`
- [ ] `[Fact] RFC9110-RETRY-003: POST not retried on 503 (non-idempotent)`
- [ ] `[Fact] RFC9110-RETRY-004: Retry respects Retry-After header`
- [ ] `[Fact] RFC9110-RETRY-005: Max retry count enforced`
- [ ] `[Fact] RFC9110-RETRY-006: Succeed after N failures`

### 4.6 Content Encoding (RFC 9110 §8.4 via HTTP/1.0)

- [ ] **File**: `06_Http10ContentEncodingTests.cs`
- [ ] `[Fact] RFC9110-CE-001: gzip response decompressed automatically`
- [ ] `[Fact] RFC9110-CE-002: deflate response decompressed automatically`
- [ ] `[Fact] RFC9110-CE-003: Uncompressed response passed through unchanged`
- [ ] `[Fact] RFC9110-CE-004: Content-Encoding header removed after decompression`
- [ ] `[Fact] RFC9110-CE-005: Content-Length updated after decompression`

**Phase 4 Total**: ~42 tests

---

## Phase 5 — Http11Engine Integration Tests

**File location**: `src/TurboHttp.IntegrationTests/Http11/`
**Fixture**: `KestrelFixture` with `request.Version = HttpVersion.Version11`

### 5.1 Basic RFC 9112 Compliance

- [ ] **File**: `01_Http11BasicTests.cs`
- [ ] `[Fact] RFC9112-BASIC-001: GET request with Host header`
- [ ] `[Fact] RFC9112-BASIC-002: HEAD request returns headers without body`
- [ ] `[Fact] RFC9112-BASIC-003: POST with Content-Length body`
- [ ] `[Fact] RFC9112-BASIC-004: PUT with Content-Length body`
- [ ] `[Fact] RFC9112-BASIC-005: DELETE request`
- [ ] `[Fact] RFC9112-BASIC-006: PATCH request`
- [ ] `[Fact] RFC9112-BASIC-007: OPTIONS request`
- [ ] `[Theory] RFC9112-BASIC-008: Status codes 200, 201, 204, 301, 302, 400, 404, 500`
- [ ] `[Fact] RFC9112-BASIC-009: Large response (1MB)`
- [ ] `[Fact] RFC9112-BASIC-010: Request and response headers round-trip`

### 5.2 Chunked Transfer Encoding (RFC 9112 §7)

- [ ] **File**: `02_Http11ChunkedTests.cs`
- [ ] `[Fact] RFC9112-CHUNK-001: Chunked response decoded correctly`
- [ ] `[Fact] RFC9112-CHUNK-002: Multiple chunks reassembled`
- [ ] `[Fact] RFC9112-CHUNK-003: Chunked request encoding (POST)`
- [ ] `[Fact] RFC9112-CHUNK-004: Zero-length final chunk terminates response`
- [ ] `[Fact] RFC9112-CHUNK-005: Trailer headers after final chunk`
- [ ] `[Fact] RFC9112-CHUNK-006: Large chunked response (100KB)`
- [ ] `[Fact] RFC9112-CHUNK-007: HEAD request for chunked resource returns no body`
- [ ] `[Fact] RFC9112-CHUNK-008: Chunked with MD5 checksum trailer`

### 5.3 Connection Management (RFC 9112 §9)

- [ ] **File**: `03_Http11ConnectionTests.cs`
- [ ] `[Fact] RFC9112-CONN-001: Keep-alive is default (no Connection header)`
- [ ] `[Fact] RFC9112-CONN-002: Multiple requests on same connection`
- [ ] `[Fact] RFC9112-CONN-003: Connection: close closes connection after response`
- [ ] `[Fact] RFC9112-CONN-004: Server Connection: close honored`
- [ ] `[Fact] RFC9112-CONN-005: Pipelined requests (sequential on same connection)`
- [ ] `[Fact] RFC9112-CONN-006: Per-host connection limit (max 6)`
- [ ] `[Fact] RFC9112-CONN-007: Connection reuse after successful response`
- [ ] `[Fact] RFC9112-CONN-008: Connection not reused after protocol error`

### 5.4 Redirect (RFC 9110 §15.4 via HTTP/1.1)

- [ ] **File**: `04_Http11RedirectTests.cs`
- [ ] `[Theory] RFC9110-REDIR-001: 301/302/307/308 GET follows redirect`
- [ ] `[Fact] RFC9110-REDIR-002: 303 changes POST to GET`
- [ ] `[Fact] RFC9110-REDIR-003: 307 preserves POST method and body`
- [ ] `[Fact] RFC9110-REDIR-004: 308 preserves POST method and body`
- [ ] `[Fact] RFC9110-REDIR-005: Redirect chain (5 hops)`
- [ ] `[Fact] RFC9110-REDIR-006: Redirect loop detected`
- [ ] `[Fact] RFC9110-REDIR-007: Cross-origin redirect strips Authorization`
- [ ] `[Fact] RFC9110-REDIR-008: HTTPS to HTTP downgrade blocked (default policy)`
- [ ] `[Fact] RFC9110-REDIR-009: Redirect with relative Location`
- [ ] `[Fact] RFC9110-REDIR-010: Redirect preserves cookies via CookieJar`

### 5.5 Cookies (RFC 6265 via HTTP/1.1)

- [ ] **File**: `05_Http11CookieTests.cs`
- [ ] `[Fact] RFC6265-COOK-001: Set-Cookie stored and sent on subsequent request`
- [ ] `[Fact] RFC6265-COOK-002: Multiple cookies accumulated`
- [ ] `[Fact] RFC6265-COOK-003: Path attribute restricts scope`
- [ ] `[Fact] RFC6265-COOK-004: Domain attribute enables sub-domain matching`
- [ ] `[Fact] RFC6265-COOK-005: Secure cookie only sent over HTTPS`
- [ ] `[Fact] RFC6265-COOK-006: HttpOnly flag set (no validation on client side)`
- [ ] `[Fact] RFC6265-COOK-007: Max-Age=0 deletes cookie`
- [ ] `[Fact] RFC6265-COOK-008: Expired cookie not sent`
- [ ] `[Fact] RFC6265-COOK-009: Multiple Set-Cookie headers in one response`
- [ ] `[Fact] RFC6265-COOK-010: Cookie sorting (longest path first)`
- [ ] `[Fact] RFC6265-COOK-011: Cookie preserved across redirects`
- [ ] `[Fact] RFC6265-COOK-012: SameSite=Strict not sent cross-site`

### 5.6 Retry (RFC 9110 §9.2 via HTTP/1.1)

- [ ] **File**: `06_Http11RetryTests.cs`
- [ ] `[Fact] RFC9110-RETRY-001: GET retried on 503`
- [ ] `[Fact] RFC9110-RETRY-002: GET retried on 408`
- [ ] `[Fact] RFC9110-RETRY-003: HEAD retried on 503 (idempotent)`
- [ ] `[Fact] RFC9110-RETRY-004: PUT retried on 503 (idempotent)`
- [ ] `[Fact] RFC9110-RETRY-005: DELETE retried on 503 (idempotent)`
- [ ] `[Fact] RFC9110-RETRY-006: POST NOT retried on 503`
- [ ] `[Fact] RFC9110-RETRY-007: Retry-After seconds respected`
- [ ] `[Fact] RFC9110-RETRY-008: Retry-After HTTP-date respected`
- [ ] `[Fact] RFC9110-RETRY-009: Max retry count (3) enforced`
- [ ] `[Fact] RFC9110-RETRY-010: Succeed after 2 failures`

### 5.7 Caching (RFC 9111 via HTTP/1.1)

- [ ] **File**: `07_Http11CacheTests.cs`
- [ ] `[Fact] RFC9111-CACHE-001: Cacheable GET response stored`
- [ ] `[Fact] RFC9111-CACHE-002: Cached response served on repeat request (within max-age)`
- [ ] `[Fact] RFC9111-CACHE-003: Stale cache triggers revalidation (conditional request)`
- [ ] `[Fact] RFC9111-CACHE-004: 304 Not Modified merges with cached entry`
- [ ] `[Fact] RFC9111-CACHE-005: ETag-based conditional request (If-None-Match)`
- [ ] `[Fact] RFC9111-CACHE-006: Last-Modified-based conditional request (If-Modified-Since)`
- [ ] `[Fact] RFC9111-CACHE-007: Cache-Control: no-store bypasses cache`
- [ ] `[Fact] RFC9111-CACHE-008: Cache-Control: no-cache forces revalidation`
- [ ] `[Fact] RFC9111-CACHE-009: Vary header creates separate cache entries`
- [ ] `[Fact] RFC9111-CACHE-010: POST invalidates cached resource`
- [ ] `[Fact] RFC9111-CACHE-011: Cache-Control: must-revalidate forces revalidation on stale`
- [ ] `[Fact] RFC9111-CACHE-012: HEAD response cached (RFC 9111 §3)`
- [ ] `[Fact] RFC9111-CACHE-013: min-fresh request directive honored`
- [ ] `[Fact] RFC9111-CACHE-014: max-stale request directive honored`
- [ ] `[Fact] RFC9111-CACHE-015: LRU eviction when store is full`

### 5.8 Content Encoding (RFC 9110 §8.4 via HTTP/1.1)

- [ ] **File**: `08_Http11ContentEncodingTests.cs`
- [ ] `[Fact] RFC9110-CE-001: gzip response decompressed`
- [ ] `[Fact] RFC9110-CE-002: deflate response decompressed`
- [ ] `[Fact] RFC9110-CE-003: brotli response decompressed`
- [ ] `[Fact] RFC9110-CE-004: Identity (no encoding) passed through`
- [ ] `[Fact] RFC9110-CE-005: Content-Encoding removed after decompression`
- [ ] `[Fact] RFC9110-CE-006: Accept-Encoding sent with request`
- [ ] `[Fact] RFC9110-CE-007: Large compressed response (500KB)`

**Phase 5 Total**: ~82 tests

---

## Phase 6 — Http20Engine Integration Tests

**File location**: `src/TurboHttp.IntegrationTests/Http20/`
**Fixture**: `KestrelH2Fixture` with `request.Version = HttpVersion.Version20`

### 6.1 Basic RFC 9113 Compliance

- [ ] **File**: `01_Http20BasicTests.cs`
- [ ] `[Fact] RFC9113-BASIC-001: GET request over HTTP/2`
- [ ] `[Fact] RFC9113-BASIC-002: HEAD request over HTTP/2`
- [ ] `[Fact] RFC9113-BASIC-003: POST with body over HTTP/2`
- [ ] `[Fact] RFC9113-BASIC-004: PUT with body over HTTP/2`
- [ ] `[Theory] RFC9113-BASIC-005: Status codes 200, 201, 204, 400, 404, 500`
- [ ] `[Fact] RFC9113-BASIC-006: Large response (1MB) over HTTP/2`
- [ ] `[Fact] RFC9113-BASIC-007: Request and response pseudo-headers correct`
- [ ] `[Fact] RFC9113-BASIC-008: Custom headers round-trip`
- [ ] `[Fact] RFC9113-BASIC-009: Binary body (non-UTF8) round-trip`

### 6.2 Multiplexing (RFC 9113 §5)

- [ ] **File**: `02_Http20MultiplexTests.cs`
- [ ] `[Fact] RFC9113-MUX-001: Concurrent requests on same connection`
- [ ] `[Fact] RFC9113-MUX-002: 10 parallel GET requests complete`
- [ ] `[Fact] RFC9113-MUX-003: Interleaved request/response ordering`
- [ ] `[Fact] RFC9113-MUX-004: Stream IDs are odd (client-initiated)`
- [ ] `[Fact] RFC9113-MUX-005: MAX_CONCURRENT_STREAMS respected`
- [ ] `[Fact] RFC9113-MUX-006: Slow stream doesn't block fast streams`

### 6.3 Flow Control (RFC 9113 §5.2)

- [ ] **File**: `03_Http20FlowControlTests.cs`
- [ ] `[Fact] RFC9113-FC-001: Large POST body respects flow control windows`
- [ ] `[Fact] RFC9113-FC-002: WINDOW_UPDATE increases send window`
- [ ] `[Fact] RFC9113-FC-003: Connection-level and stream-level windows independent`
- [ ] `[Fact] RFC9113-FC-004: Response body received despite small initial window`

### 6.4 HPACK (RFC 7541 via HTTP/2)

- [ ] **File**: `04_Http20HpackTests.cs`
- [ ] `[Fact] RFC7541-HPACK-001: Dynamic table reuse across requests`
- [ ] `[Fact] RFC7541-HPACK-002: Huffman-encoded headers decoded correctly`
- [ ] `[Fact] RFC7541-HPACK-003: Large header block split into CONTINUATION frames`
- [ ] `[Fact] RFC7541-HPACK-004: Many headers (100+) round-trip`
- [ ] `[Fact] RFC7541-HPACK-005: Sensitive headers (Authorization) never indexed`

### 6.5 Settings & Ping (RFC 9113 §6.5, §6.7)

- [ ] **File**: `05_Http20SettingsPingTests.cs`
- [ ] `[Fact] RFC9113-SETTINGS-001: Client and server exchange SETTINGS`
- [ ] `[Fact] RFC9113-SETTINGS-002: SETTINGS_MAX_CONCURRENT_STREAMS applied`
- [ ] `[Fact] RFC9113-SETTINGS-003: SETTINGS_INITIAL_WINDOW_SIZE change propagated`
- [ ] `[Fact] RFC9113-PING-001: PING roundtrip within timeout`

### 6.6 Redirect (RFC 9110 §15.4 via HTTP/2)

- [ ] **File**: `06_Http20RedirectTests.cs`
- [ ] `[Theory] RFC9110-REDIR-001: 301/302/307/308 follow redirect over HTTP/2`
- [ ] `[Fact] RFC9110-REDIR-002: 303 changes POST to GET`
- [ ] `[Fact] RFC9110-REDIR-003: 307 preserves POST method and body`
- [ ] `[Fact] RFC9110-REDIR-004: Redirect chain (5 hops)`
- [ ] `[Fact] RFC9110-REDIR-005: Redirect loop detected`
- [ ] `[Fact] RFC9110-REDIR-006: Redirects use same HTTP/2 connection (multiplexed)`

### 6.7 Cookies (RFC 6265 via HTTP/2)

- [ ] **File**: `07_Http20CookieTests.cs`
- [ ] `[Fact] RFC6265-COOK-001: Set-Cookie stored and sent on subsequent HTTP/2 request`
- [ ] `[Fact] RFC6265-COOK-002: Multiple cookies via separate Set-Cookie headers`
- [ ] `[Fact] RFC6265-COOK-003: Cookie header HPACK-compressed efficiently`
- [ ] `[Fact] RFC6265-COOK-004: Cookies preserved across HTTP/2 redirects`
- [ ] `[Fact] RFC6265-COOK-005: Cookie with Path attribute restricts scope`

### 6.8 Retry (RFC 9110 §9.2 via HTTP/2)

- [ ] **File**: `08_Http20RetryTests.cs`
- [ ] `[Fact] RFC9110-RETRY-001: GET retried on 503 over HTTP/2`
- [ ] `[Fact] RFC9110-RETRY-002: POST NOT retried on 503`
- [ ] `[Fact] RFC9110-RETRY-003: Retry uses new stream on same connection`
- [ ] `[Fact] RFC9110-RETRY-004: RST_STREAM with REFUSED_STREAM triggers retry`
- [ ] `[Fact] RFC9110-RETRY-005: GOAWAY with non-zero last-stream-ID retries pending streams`

### 6.9 Caching (RFC 9111 via HTTP/2)

- [ ] **File**: `09_Http20CacheTests.cs`
- [ ] `[Fact] RFC9111-CACHE-001: Cached GET response served from cache`
- [ ] `[Fact] RFC9111-CACHE-002: Stale entry triggers conditional request`
- [ ] `[Fact] RFC9111-CACHE-003: 304 Not Modified merges with cached entry`
- [ ] `[Fact] RFC9111-CACHE-004: no-store bypasses cache`
- [ ] `[Fact] RFC9111-CACHE-005: POST invalidates cached entry`

### 6.10 Content Encoding (RFC 9110 §8.4 via HTTP/2)

- [ ] **File**: `10_Http20ContentEncodingTests.cs`
- [ ] `[Fact] RFC9110-CE-001: gzip response decompressed over HTTP/2`
- [ ] `[Fact] RFC9110-CE-002: deflate response decompressed over HTTP/2`
- [ ] `[Fact] RFC9110-CE-003: brotli response decompressed over HTTP/2`
- [ ] `[Fact] RFC9110-CE-004: Large compressed response (500KB) over HTTP/2`

### 6.11 Error Handling (RFC 9113 §5.4, §6.4)

- [ ] **File**: `11_Http20ErrorHandlingTests.cs`
- [ ] `[Fact] RFC9113-ERR-001: RST_STREAM on single stream does not kill connection`
- [ ] `[Fact] RFC9113-ERR-002: GOAWAY gracefully shuts down connection`
- [ ] `[Fact] RFC9113-ERR-003: Server protocol error reported as exception`
- [ ] `[Fact] RFC9113-ERR-004: Request on closed connection automatically reconnects`

**Phase 6 Total**: ~68 tests

---

## Phase 7 — Cross-Engine & TurboHttpClient Tests

**File location**: `src/TurboHttp.IntegrationTests/Shared/`

### 7.1 TurboHttpClient SendAsync Tests

- [ ] **File**: `01_TurboHttpClientTests.cs`
- [ ] `[Fact] CLIENT-001: SendAsync returns HttpResponseMessage`
- [ ] `[Fact] CLIENT-002: BaseAddress applied to relative URI`
- [ ] `[Fact] CLIENT-003: DefaultRequestHeaders merged`
- [ ] `[Fact] CLIENT-004: DefaultRequestVersion used when not specified`
- [ ] `[Fact] CLIENT-005: CancellationToken cancels pending request`
- [ ] `[Fact] CLIENT-006: Timeout expires and throws TaskCanceledException`
- [ ] `[Fact] CLIENT-007: CancelPendingRequests cancels all in-flight`
- [ ] `[Fact] CLIENT-008: Concurrent SendAsync calls (10 parallel)`
- [ ] `[Fact] CLIENT-009: Dispose cleans up actor system`
- [ ] `[Fact] CLIENT-010: Channel-based API (Requests/Responses) works`

### 7.2 Version Negotiation

- [ ] **File**: `02_VersionNegotiationTests.cs`
- [ ] `[Fact] VERSION-001: HTTP/1.0 request routed to Http10Engine`
- [ ] `[Fact] VERSION-002: HTTP/1.1 request routed to Http11Engine`
- [ ] `[Fact] VERSION-003: HTTP/2 request routed to Http20Engine`
- [ ] `[Fact] VERSION-004: Mixed version requests demultiplexed correctly`
- [ ] `[Fact] VERSION-005: DefaultRequestVersion override works`

### 7.3 Cross-Feature Interaction

- [ ] **File**: `03_CrossFeatureTests.cs`
- [ ] `[Fact] CROSS-001: Redirect + Cookies (cookies followed through redirects)`
- [ ] `[Fact] CROSS-002: Redirect + Retry (retry after redirect target returns 503)`
- [ ] `[Fact] CROSS-003: Cache + Redirect (cached redirect response)`
- [ ] `[Fact] CROSS-004: Cache + Cookies (Set-Cookie from cached response)`
- [ ] `[Fact] CROSS-005: Decompression + Cache (compressed response cached, served decompressed)`
- [ ] `[Fact] CROSS-006: Retry + Decompression (decompressed retry response)`
- [ ] `[Fact] CROSS-007: All features enabled simultaneously (redirect → cookie → cache → decompress → retry)`
- [ ] `[Fact] CROSS-008: Feature flags disabled = passthrough (backward compat)`

### 7.4 TLS Integration

- [ ] **File**: `04_TlsTests.cs`
- [ ] `[Fact] TLS-001: HTTPS request with self-signed cert (custom validation)`
- [ ] `[Fact] TLS-002: Cookies with Secure flag only sent over HTTPS`
- [ ] `[Fact] TLS-003: HTTPS redirect preserves TLS`
- [ ] `[Fact] TLS-004: HTTP to HTTPS upgrade redirect followed`
- [ ] `[Fact] TLS-005: HTTPS to HTTP downgrade blocked`

### 7.5 Edge Cases & Error Handling

- [ ] **File**: `05_EdgeCaseTests.cs`
- [ ] `[Fact] EDGE-001: Server closes connection mid-response`
- [ ] `[Fact] EDGE-002: Extremely large header (32KB)`
- [ ] `[Fact] EDGE-003: Empty response body`
- [ ] `[Fact] EDGE-004: Response with unknown Content-Encoding passes through`
- [ ] `[Fact] EDGE-005: Request to non-existent host throws`
- [ ] `[Fact] EDGE-006: Request to non-listening port throws`
- [ ] `[Fact] EDGE-007: Connection timeout honored`
- [ ] `[Fact] EDGE-008: Concurrent requests to different hosts`

**Phase 7 Total**: ~38 tests

---

## Phase 8 — Validation Gate

### 8.1 Full Test Suite Verification

- [ ] All existing unit tests pass (310+ protocol, 100+ stream)
- [ ] All new stage unit tests pass (~79 tests)
- [ ] All Http10Engine integration tests pass (~42 tests)
- [ ] All Http11Engine integration tests pass (~82 tests)
- [ ] All Http20Engine integration tests pass (~68 tests)
- [ ] All cross-engine/client tests pass (~38 tests)
- [ ] Zero compiler warnings (excluding [Obsolete] deprecations)
- [ ] Build succeeds: `dotnet build --configuration Release src/TurboHttp.sln`

### 8.2 RFC Compliance Matrix

- [ ] **File**: `COMPLIANCE.md` — document per-RFC compliance
- [ ] RFC 1945 (HTTP/1.0): 100% coverage
- [ ] RFC 9112 (HTTP/1.1 framing): 100% coverage
- [ ] RFC 9113 (HTTP/2): 100% coverage
- [ ] RFC 9110 (HTTP semantics — redirects, retries): 100% coverage
- [ ] RFC 7541 (HPACK): 100% coverage
- [ ] RFC 6265 (Cookies): 100% coverage
- [ ] RFC 9111 (Caching): 100% coverage

### 8.3 Performance Baseline

- [ ] Run benchmarks: `dotnet run --configuration Release --project src/TurboHttp.Benchmarks/ -- --job dry`
- [ ] Verify no performance regression from new stages (< 5% overhead)
- [ ] Measure cache hit latency (should be < 1µs for in-memory cache)

---

## Dependency Graph

```
Phase 1 (Stages)
    ↓
Phase 2 (Engine wiring)
    ↓
Phase 3 (Kestrel routes) ← can be done in parallel with Phase 1
    ↓
Phase 4, 5, 6 (Engine integration tests) ← can be done in parallel
    ↓
Phase 7 (Cross-engine tests)
    ↓
Phase 8 (Validation)
```

### Parallelization Opportunities

| Work Stream | Depends On | Estimated Effort |
|------------|-----------|-----------------|
| Phase 1.1–1.2 (Cookie stages) | Nothing | 4h |
| Phase 1.3 (Decompression stage) | Nothing | 3h |
| Phase 1.4–1.5 (Cache stages) | Nothing | 6h |
| Phase 1.6 (Redirect stage) | Nothing | 6h |
| Phase 1.7 (Retry stage) | Nothing | 4h |
| Phase 1.8 (Connection reuse stage) | Nothing | 3h |
| Phase 2 (Wiring) | Phase 1 | 8h |
| Phase 3 (Fixture routes) | Nothing | 4h |
| Phase 4 (Http10 tests) | Phase 2 + 3 | 6h |
| Phase 5 (Http11 tests) | Phase 2 + 3 | 10h |
| Phase 6 (Http20 tests) | Phase 2 + 3 | 8h |
| Phase 7 (Cross-engine) | Phase 4 + 5 + 6 | 6h |
| Phase 8 (Validation) | Phase 7 | 4h |
| **Total** | | **~72h** |

---

## Summary

| Metric | Count |
|--------|-------|
| New Akka.Streams stages | 8 |
| New stage unit tests | ~79 |
| New Kestrel fixture routes | ~45 |
| Http10Engine integration tests | ~42 |
| Http11Engine integration tests | ~82 |
| Http20Engine integration tests | ~68 |
| Cross-engine + client tests | ~38 |
| **Total new tests** | **~309** |
| Estimated effort | ~72 hours |
| RFCs covered | 7 (1945, 9112, 9113, 9110, 7541, 6265, 9111) |
