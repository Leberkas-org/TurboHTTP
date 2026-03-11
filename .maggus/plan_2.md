# Plan: TurboHttpClient Integration & RFC Compliance Pipeline

## Introduction

Wire all existing protocol handlers (RedirectHandler, CookieJar, RetryEvaluator, CacheFreshnessEvaluator, ContentEncodingDecoder, ConnectionReuseEvaluator) into the Akka.Streams Engine pipeline as composable GraphStages. Build end-to-end integration tests against real Kestrel servers and achieve 100% RFC compliance for Http10Engine, Http11Engine, and Http20Engine with Caching, Redirect, Cookies, Connection Reuse, Retry, and Content Encoding.

**Current gap**: The Engine pipeline only does encode → TCP → decode → correlate. All protocol handlers exist as standalone classes with unit tests but are NOT wired into the pipeline. Zero end-to-end integration tests exist despite 60+ Kestrel fixture routes being defined.

## Goals

- Create 8 new Akka.Streams GraphStages (CookieInjection, CookieStorage, Decompression, CacheLookup, CacheStorage, Redirect, Retry, ConnectionReuse)
- Wire all stages into the Engine pipeline with feature flags (disabled by default for backward compat)
- Activate TurboClientStreamManager graph materialization so `TurboHttpClient.SendAsync` works end-to-end
- Add ~45 new Kestrel fixture routes for redirect, cookie, retry, cache, compression, and connection reuse scenarios
- Write ~309 new tests (79 stage unit tests + 230 integration tests)
- Achieve 100% RFC compliance across RFC 1945, 9112, 9113, 9110, 7541, 6265, 9111

## User Stories

---

### TASK-001: CookieInjectionStage
**Description:** As a developer, I want a CookieInjectionStage that injects cookies from a CookieJar into outgoing requests so that cookies are automatically sent with each request.

**Acceptance Criteria:**
- [x] File created: `src/TurboHttp/Streams/Stages/CookieInjectionStage.cs`
- [x] Implements `GraphStage<FlowShape<HttpRequestMessage, HttpRequestMessage>>`
- [x] On push: calls `CookieJar.AddCookiesToRequest(request.RequestUri, ref request)` then pushes downstream
- [x] Constructor takes `CookieJar` instance; when null, pass-through (no-op)
- [x] Unit tests written and successful
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors

### TASK-002: CookieStorageStage
**Description:** As a developer, I want a CookieStorageStage that extracts Set-Cookie headers from responses and stores them in the CookieJar so that cookies accumulate across requests.

**Acceptance Criteria:**
- [x] File created: `src/TurboHttp/Streams/Stages/CookieStorageStage.cs`
- [x] Implements `GraphStage<FlowShape<HttpResponseMessage, HttpResponseMessage>>`
- [x] On push: calls `CookieJar.ProcessResponse(response.RequestMessage.RequestUri, response)` then pushes downstream
- [x] Side-effect only — response is NOT modified
- [x] Unit tests written and successful
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors

### TASK-003: DecompressionStage
**Description:** As a developer, I want a DecompressionStage that automatically decompresses gzip/deflate/brotli response bodies so that the consumer receives uncompressed content.

**Acceptance Criteria:**
- [x] File created: `src/TurboHttp/Streams/Stages/DecompressionStage.cs`
- [x] Implements `GraphStage<FlowShape<HttpResponseMessage, HttpResponseMessage>>`
- [x] Reads `Content-Encoding` header, calls `ContentEncodingDecoder.Decompress(body, encoding)`
- [x] Replaces `response.Content` with decompressed `ByteArrayContent`
- [x] Removes `Content-Encoding` header, updates `Content-Length`
- [x] Pass-through if no `Content-Encoding` or `identity`
- [x] Handles: gzip, deflate, br (brotli), x-gzip
- [x] ~10 unit tests written and successful
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors

### TASK-004: CacheLookupStage
**Description:** As a developer, I want a CacheLookupStage that checks the in-memory cache before sending requests to the network so that fresh cached responses are served instantly.

**Acceptance Criteria:**
- [x] File created: `src/TurboHttp/Streams/Stages/CacheLookupStage.cs`
- [x] Implements `GraphStage<FanOutShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage>>`
- [x] Outlet 0 (cache miss): request passes to engine
- [x] Outlet 1 (cache hit): emits cached `HttpResponseMessage` directly
- [x] On push: calls `CacheFreshnessEvaluator.Evaluate(entry, request, now)` — Fresh → outlet 1, MustRevalidate → conditional request via `CacheValidationRequestBuilder` → outlet 0, Miss → outlet 0
- [x] Constructor takes `HttpCacheStore` and `CachePolicy`
- [x] ~12 unit tests written and successful
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors

### TASK-005: CacheStorageStage
**Description:** As a developer, I want a CacheStorageStage that stores cacheable responses and merges 304 Not Modified responses with cached entries so that the cache stays up-to-date.

**Acceptance Criteria:**
- [x] File created: `src/TurboHttp/Streams/Stages/CacheStorageStage.cs`
- [x] Implements `GraphStage<FlowShape<HttpResponseMessage, HttpResponseMessage>>`
- [x] If `304 Not Modified`: merges with cached entry via `CacheValidationRequestBuilder.MergeNotModifiedResponse`
- [x] If `2xx`: calls `HttpCacheStore.Put(request, response, body, requestTime, responseTime)`
- [x] If unsafe method (POST/PUT/DELETE/PATCH): calls `HttpCacheStore.Invalidate(uri)`
- [x] Pushes response downstream (possibly merged 200 from 304)
- [x] ~10 unit tests written and successful
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors

### TASK-006: RedirectStage
**Description:** As a developer, I want a RedirectStage that automatically follows HTTP redirects (301/302/303/307/308) with correct method rewriting, loop detection, and cross-origin header stripping so that redirects are handled transparently.

**Acceptance Criteria:**
- [x] File created: `src/TurboHttp/Streams/Stages/RedirectStage.cs`
- [x] Implements `GraphStage<FlowShape<HttpResponseMessage, HttpResponseMessage>>` (or `BidiShape` wrapping engine)
- [x] On push: if `RedirectHandler.IsRedirect(response)` → calls `BuildRedirectRequest`, re-emits upstream
- [x] Tracks redirect count via `RedirectHandler.RedirectCount`; on max redirects or non-redirect → pushes final response downstream
- [x] Integrates with `CookieJar` for cross-redirect cookie handling
- [x] Constructor takes `RedirectHandler` (or `RedirectPolicy`)
- [x] ~15 unit tests written and successful
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors

### TASK-007: RetryStage
**Description:** As a developer, I want a RetryStage that automatically retries idempotent requests on 408/503 responses with Retry-After support so that transient failures are handled transparently.

**Acceptance Criteria:**
- [x] File created: `src/TurboHttp/Streams/Stages/RetryStage.cs`
- [x] Implements `GraphStage<FlowShape<HttpResponseMessage, HttpResponseMessage>>` (or `RestartFlow`/`RecoverWith` pattern)
- [x] On push: calls `RetryEvaluator.Evaluate(request, response, networkFailure, ...)`
- [x] If `ShouldRetry` → re-emits original request upstream (after optional `RetryAfter` delay)
- [x] If not → pushes response downstream
- [x] Preserves original `HttpRequestMessage` for re-emission
- [x] Constructor takes `RetryPolicy`
- [x] ~10 unit tests written and successful
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors

### TASK-008: ConnectionReuseStage
**Description:** As a developer, I want a ConnectionReuseStage that evaluates whether a TCP connection can be reused based on HTTP version and Connection headers so that connections are pooled efficiently.

**Acceptance Criteria:**
- [x] File created: `src/TurboHttp/Streams/Stages/ConnectionReuseStage.cs`
- [x] Implements `GraphStage<FlowShape<HttpResponseMessage, HttpResponseMessage>>`
- [x] On push: calls `ConnectionReuseEvaluator.Evaluate(response, version, bodyFullyConsumed)`
- [x] If `CanReuse` → signals connection pool to keep connection alive
- [x] If not → signals connection pool to close connection
- [x] Side-effect: signals via `ITransportItem` or actor message to `ClientManager`
- [x] ~8 unit tests written and successful
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors

### TASK-009: TurboClientOptions Extension
**Description:** As a developer, I want feature flags on TurboClientOptions so that each pipeline stage can be enabled/disabled independently with backward compatibility.

**Acceptance Criteria:**
- [x] File modified: `src/TurboHttp/Client/TurboClientOptions.cs`
- [x] Added properties: `EnableRedirectHandling`, `RedirectPolicy?`, `EnableCookies`, `EnableRetry`, `RetryPolicy?`, `EnableCaching`, `CachePolicy?`, `EnableDecompression`, `ConnectionPolicy?`
- [x] All flags default to `false` for backward compatibility
- [x] Existing tests continue to pass without modification
- [x] Unit tests written and successful
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors

### TASK-010: Engine.CreateFlow Pipeline Wiring
**Description:** As a developer, I want Engine.CreateFlow to conditionally insert the new stages based on TurboClientOptions feature flags so that the full pipeline is assembled dynamically.

**Acceptance Criteria:**
- [x] File modified: `src/TurboHttp/Streams/Engine.cs`
- [x] Accepts `TurboClientOptions` (or `PipelineOptions` record)
- [x] Conditionally inserts stages: RequestEnricher → CookieInjection → CacheLookup → [Engine core] → Decompression → CookieStorage → CacheStorage → Redirect → merge
- [x] Retry wraps the engine core (not a linear stage)
- [x] With all flags `false`, pipeline is identical to current behavior
- [x] Unit tests written and successful
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors

### TASK-011: TurboClientStreamManager Activation
**Description:** As a developer, I want TurboClientStreamManager to materialize the full graph so that `TurboHttpClient.SendAsync` works end-to-end.

**Acceptance Criteria:**
- [x] File modified: `src/TurboHttp/Client/TurboClientStreamManager.cs`
- [x] Uncommented and completed graph materialization code
- [x] Wired: `ChannelSource → RequestEnricher → Engine.CreateFlow(options) → ChannelSink`
- [x] Passes `CookieJar`, `HttpCacheStore` instances from `TurboClientOptions` or creates defaults
- [x] `CookieJar` — one per `TurboHttpClient` instance (thread-safe)
- [x] `HttpCacheStore` — one per `TurboHttpClient` instance (thread-safe, LRU)
- [x] `RedirectHandler` — one per request chain (stateful: redirect count)
- [x] ⚠️ BLOCKED: `PerHostConnectionLimiter` — one per `TurboHttpClient` (thread-safe) — No pipeline stage exists to consume PerHostConnectionLimiter. The class exists and is tested standalone, but there is no Akka.Streams stage to wire it into the Engine pipeline. A future task should create a throttling stage.
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors

### TASK-012: Kestrel Redirect Routes
**Description:** As a test author, I want Kestrel fixture routes for redirect scenarios so that integration tests can verify redirect behavior end-to-end.

**Acceptance Criteria:**
- [x] Routes added to both `KestrelFixture` and `KestrelH2Fixture`
- [x] `GET /redirect/{code}/{target}` — responds with status `{code}`, `Location: {target}`
- [x] `GET /redirect/chain/{n}` — chain of n redirects ending at `/hello`
- [x] `GET /redirect/loop` — infinite redirect loop
- [x] `GET /redirect/relative` — redirect with relative Location header
- [x] `GET /redirect/cross-scheme` — HTTPS → HTTP downgrade redirect
- [x] `POST /redirect/307` — 307 preserves method & body
- [x] `POST /redirect/303` — 303 changes to GET
- [x] Unit tests written and successful
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors

### TASK-013: Kestrel Cookie Routes
**Description:** As a test author, I want Kestrel fixture routes for cookie scenarios so that integration tests can verify cookie injection, storage, and attribute handling.

**Acceptance Criteria:**
- [x] Routes added to both `KestrelFixture` and `KestrelH2Fixture`
- [x] `GET /cookie/set/{name}/{value}` — Set-Cookie: {name}={value}; Path=/
- [x] `GET /cookie/set-secure/{name}/{value}` — Set-Cookie with Secure flag
- [x] `GET /cookie/set-httponly/{name}/{value}` — Set-Cookie with HttpOnly flag
- [x] `GET /cookie/set-samesite/{name}/{value}/{policy}` — Set-Cookie with SameSite
- [x] `GET /cookie/set-expires/{name}/{value}/{seconds}` — Set-Cookie with Max-Age
- [x] `GET /cookie/set-domain/{name}/{value}/{domain}` — Set-Cookie with Domain
- [x] `GET /cookie/set-path/{name}/{value}/{path}` — Set-Cookie with Path
- [x] `GET /cookie/echo` — returns all received Cookie headers as JSON body
- [x] `GET /cookie/set-multiple` — multiple Set-Cookie headers
- [x] `GET /cookie/delete/{name}` — Set-Cookie with Max-Age=0
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors

### TASK-014: Kestrel Retry Routes
**Description:** As a test author, I want Kestrel fixture routes for retry scenarios so that integration tests can verify retry behavior with Retry-After, idempotency, and succeed-after-N patterns.

**Acceptance Criteria:**
- [x] Routes added to both `KestrelFixture` and `KestrelH2Fixture`
- [x] `GET /retry/408` — responds with 408 Request Timeout
- [x] `GET /retry/503` — responds with 503 Service Unavailable
- [x] `GET /retry/503-retry-after/{seconds}` — 503 with Retry-After header
- [x] `GET /retry/503-retry-after-date` — 503 with Retry-After as HTTP-date
- [x] `GET /retry/succeed-after/{n}` — fail first N-1 times with 503, then 200 (stateful)
- [x] `POST /retry/non-idempotent-503` — 503 on POST (should NOT retry)
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors

### TASK-015: Kestrel Cache Routes
**Description:** As a test author, I want Kestrel fixture routes for caching scenarios so that integration tests can verify cache freshness, validation, and invalidation.

**Acceptance Criteria:**
- [x] Routes added to both `KestrelFixture` and `KestrelH2Fixture`
- [x] `GET /cache/max-age/{seconds}` — Cache-Control: max-age={seconds}, body = timestamp
- [x] `GET /cache/no-cache` — Cache-Control: no-cache
- [x] `GET /cache/no-store` — Cache-Control: no-store
- [x] `GET /cache/etag/{id}` — ETag header, supports If-None-Match → 304
- [x] `GET /cache/last-modified/{id}` — Last-Modified, supports If-Modified-Since → 304
- [x] `GET /cache/vary/{header}` — Vary: {header}, body changes based on header value
- [x] `GET /cache/must-revalidate` — Cache-Control: max-age=0, must-revalidate
- [x] `GET /cache/s-maxage/{seconds}` — Cache-Control: s-maxage={seconds}
- [x] `GET /cache/expires` — Expires header (absolute date)
- [x] `GET /cache/private` — Cache-Control: private
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors

### TASK-016: Kestrel Content Encoding Routes
**Description:** As a test author, I want Kestrel fixture routes for content encoding scenarios so that integration tests can verify automatic decompression.

**Acceptance Criteria:**
- [x] Routes added to both `KestrelFixture` and `KestrelH2Fixture`
- [x] `GET /compress/gzip/{kb}` — gzip-compressed response
- [x] `GET /compress/deflate/{kb}` — deflate-compressed response
- [x] `GET /compress/br/{kb}` — brotli-compressed response
- [x] `GET /compress/identity/{kb}` — no compression (control)
- [x] `GET /compress/negotiate` — honors Accept-Encoding, responds with matching encoding
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors

### TASK-017: Kestrel Connection Reuse Routes
**Description:** As a test author, I want Kestrel fixture routes for connection reuse scenarios so that integration tests can verify keep-alive and close behavior.

> **Infra note:** `KestrelFixture.RegisterConnectionReuseRoutes()` already exists with all 4 routes implemented ([KestrelFixture.cs L1000-L1039](../src/TurboHttp.IntegrationTests/Shared/KestrelFixture.cs)), but is **not called** from `RegisterRoutes()`. Only a one-line call needs to be added.

**Acceptance Criteria:**
- [x] `RegisterConnectionReuseRoutes(app)` call added to `KestrelFixture.RegisterRoutes()` (routes already implemented)
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors

### TASK-018: Http10Engine Basic Integration Tests
**Engine:** [`Http10Engine.cs`](../src/TurboHttp/Streams/Http10Engine.cs)
**Description:** As a developer, I want integration tests for Http10Engine basic RFC 1945 compliance so that GET, HEAD, POST, PUT, DELETE, status codes, and large bodies are verified end-to-end.

> **Infra:** See [`01_Http10EngineBasicTests.cs`](../src/TurboHttp.IntegrationTests/Http10/01_Http10EngineBasicTests.cs) — this is the **reference implementation** for all integration tests. Uses `TestKit` base class ([TestKit.cs](../src/TurboHttp.IntegrationTests/TestKit.cs)), `IClassFixture<KestrelFixture>`, and a `SendAsync` helper that wires `Http10Engine.CreateFlow()` → `ConnectionStage` → `ClientManager` with `Source.Queue` + `Sink.First`.

**Acceptance Criteria:**
- [x] File created: `src/TurboHttp.IntegrationTests/Http10/01_Http10BasicTests.cs`
- [x] Uses `KestrelFixture` with `request.Version = HttpVersion.Version10`
- [x] 10 tests: GET 200, HEAD, POST, PUT, DELETE, status code theory, large body (100KB), custom headers, multi-value headers, empty body
- [x] All tests pass: `dotnet test --filter "FullyQualifiedName~Http10BasicTests"`

### TASK-019: Http10Engine Connection Integration Tests
**Engine:** [`Http10Engine.cs`](../src/TurboHttp/Streams/Http10Engine.cs)
**Description:** As a developer, I want integration tests for Http10Engine connection management so that HTTP/1.0 no-keep-alive default and opt-in keep-alive are verified.

> **Infra:** Follow `SendAsync` pattern from [`01_Http10EngineBasicTests.cs`](../src/TurboHttp.IntegrationTests/Http10/01_Http10EngineBasicTests.cs). Connection reuse routes require TASK-017 (wiring `RegisterConnectionReuseRoutes` call). Uses `KestrelFixture` routes: `/conn/keep-alive`, `/conn/close`, `/conn/default`.

**Acceptance Criteria:**
- [x] File created: `src/TurboHttp.IntegrationTests/Http10/02_Http10ConnectionTests.cs`
- [x] 5 tests: no keep-alive default, Keep-Alive opt-in, sequential requests new connection, reuse with Keep-Alive, server close overrides
- [x] All tests pass: `dotnet test --filter "FullyQualifiedName~Http10ConnectionTests"`

### TASK-020: Http10Engine Redirect Integration Tests
**Engine:** [`Http10Engine.cs`](../src/TurboHttp/Streams/Http10Engine.cs)
**Description:** As a developer, I want integration tests for Http10Engine redirect handling so that 301/302 follows, method rewriting, chains, loops, and cross-origin header stripping are verified.

> **Infra:** Redirect routes already registered in `KestrelFixture.RegisterRedirectRoutes()` ([KestrelFixture.cs L575-L658](../src/TurboHttp.IntegrationTests/Shared/KestrelFixture.cs)): `/redirect/{code}/{target}`, `/redirect/chain/{n}`, `/redirect/loop`, `/redirect/relative`, `/redirect/cross-scheme`, `POST /redirect/307`, `POST /redirect/303`, `POST /redirect/302`, `/redirect/cross-origin`.

**Acceptance Criteria:**
- [x] File created: `src/TurboHttp.IntegrationTests/Http10/03_Http10RedirectTests.cs`
- [x] 7 tests: 301/302 GET follow, 302 POST→GET, chain (3 hops), loop detection, max limit, relative Location, cross-origin Auth strip
- [x] All tests pass: `dotnet test --filter "FullyQualifiedName~Http10RedirectTests"`

### TASK-021: Http10Engine Cookie Integration Tests
**Engine:** [`Http10Engine.cs`](../src/TurboHttp/Streams/Http10Engine.cs)
**Description:** As a developer, I want integration tests for Http10Engine cookie handling so that Set-Cookie storage, accumulation, Path restriction, deletion, expiry, and cross-redirect persistence are verified.

> **Infra:** Cookie routes already registered in `KestrelFixture.RegisterCookieRoutes()` ([KestrelFixture.cs L489-L573](../src/TurboHttp.IntegrationTests/Shared/KestrelFixture.cs)): `/cookie/set/{name}/{value}`, `/cookie/echo`, `/cookie/set-multiple`, `/cookie/delete/{name}`, `/cookie/set-expires/...`, `/cookie/set-path/...`, etc.

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Http10/04_Http10CookieTests.cs`
- [ ] 6 tests: Set-Cookie stored+sent, multiple cookies, Path attribute, Max-Age=0 deletion, expired not sent, cookies across redirects
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~Http10CookieTests"`

### TASK-022: Http10Engine Retry Integration Tests
**Engine:** [`Http10Engine.cs`](../src/TurboHttp/Streams/Http10Engine.cs)
**Description:** As a developer, I want integration tests for Http10Engine retry handling so that idempotent retry on 503/408, non-retry on POST, Retry-After, max count, and succeed-after-N are verified.

> **Infra:** Retry routes already registered in `KestrelFixture.RegisterRetryRoutes()` ([KestrelFixture.cs L663-L727](../src/TurboHttp.IntegrationTests/Shared/KestrelFixture.cs)): `/retry/408`, `/retry/503`, `/retry/503-retry-after/{seconds}`, `/retry/503-retry-after-date`, `/retry/succeed-after/{n}`, `POST /retry/non-idempotent-503`. Stateful counter uses `ConcurrentDictionary` with `?key=` param.

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Http10/05_Http10RetryTests.cs`
- [ ] 6 tests: GET retry 503, GET retry 408, POST no retry 503, Retry-After, max count, succeed after N
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~Http10RetryTests"`

### TASK-023: Http10Engine Content Encoding Integration Tests
**Engine:** [`Http10Engine.cs`](../src/TurboHttp/Streams/Http10Engine.cs)
**Description:** As a developer, I want integration tests for Http10Engine content encoding so that gzip, deflate decompression, passthrough, header removal, and Content-Length update are verified.

> **Infra:** Content encoding routes already registered in `KestrelFixture.RegisterContentEncodingRoutes()` ([KestrelFixture.cs L865-L998](../src/TurboHttp.IntegrationTests/Shared/KestrelFixture.cs)): `/compress/gzip/{kb}`, `/compress/deflate/{kb}`, `/compress/br/{kb}`, `/compress/identity/{kb}`, `/compress/negotiate`. Server-side compression uses `GZipStream`/`DeflateStream`/`BrotliStream`.

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Http10/06_Http10ContentEncodingTests.cs`
- [ ] 5 tests: gzip decompressed, deflate decompressed, identity passthrough, Content-Encoding removed, Content-Length updated
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~Http10ContentEncodingTests"`

### TASK-024: Http11Engine Basic Integration Tests
**Engine:** [`Http11Engine.cs`](../src/TurboHttp/Streams/Http11Engine.cs)
**Description:** As a developer, I want integration tests for Http11Engine basic RFC 9112 compliance so that all methods, Host header, status codes, and large bodies are verified.

> **Infra:** Adapt `SendAsync` pattern from [`01_Http10EngineBasicTests.cs`](../src/TurboHttp.IntegrationTests/Http10/01_Http10EngineBasicTests.cs) — replace `Http10Engine` with `Http11Engine`, set `Version = HttpVersion.Version11`. Uses same `TestKit` base, `IClassFixture<KestrelFixture>`, `ConnectionStage` + `ClientManager`. Basic routes: `/hello`, `/any`, `/echo`, `/status/{code}`, `/large/{kb}`, `/headers/echo`, `/multiheader`.

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Http11/01_Http11BasicTests.cs`
- [ ] Uses `KestrelFixture` with `request.Version = HttpVersion.Version11`
- [ ] 10 tests: GET+Host, HEAD, POST, PUT, DELETE, PATCH, OPTIONS, status codes, large (1MB), header round-trip
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~Http11BasicTests"`

### TASK-025: Http11Engine Chunked Transfer Integration Tests
**Engine:** [`Http11Engine.cs`](../src/TurboHttp/Streams/Http11Engine.cs)
**Description:** As a developer, I want integration tests for Http11Engine chunked transfer encoding so that chunked responses, multi-chunk reassembly, trailers, and large chunked bodies are verified.

> **Infra:** Chunked routes already in `KestrelFixture` ([L204-L280](../src/TurboHttp.IntegrationTests/Shared/KestrelFixture.cs)): `/chunked/{kb}`, `/chunked/exact/{count}/{chunkBytes}`, `POST /echo/chunked`, `/chunked/trailer`, `/chunked/md5`. Uses `StartAsync()` to force chunked encoding.

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Http11/02_Http11ChunkedTests.cs`
- [ ] 8 tests: chunked decoded, multi-chunk, chunked POST, zero-length final, trailers, large (100KB), HEAD for chunked, MD5 trailer
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~Http11ChunkedTests"`

### TASK-026: Http11Engine Connection Integration Tests
**Engine:** [`Http11Engine.cs`](../src/TurboHttp/Streams/Http11Engine.cs)
**Description:** As a developer, I want integration tests for Http11Engine connection management so that keep-alive default, Connection: close, pipelining, per-host limits, and reuse are verified.

> **Infra:** Connection routes: `/close` (already in KestrelFixture L285-L292) + TASK-017 connection reuse routes (`/conn/keep-alive`, `/conn/close`, `/conn/default`). Depends on TASK-017 being wired.

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Http11/03_Http11ConnectionTests.cs`
- [ ] 8 tests: keep-alive default, multiple on same conn, Connection:close, server close, pipelining, per-host limit (6), reuse after success, no reuse after error
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~Http11ConnectionTests"`

### TASK-027: Http11Engine Redirect Integration Tests
**Engine:** [`Http11Engine.cs`](../src/TurboHttp/Streams/Http11Engine.cs)
**Description:** As a developer, I want integration tests for Http11Engine redirect handling so that all redirect codes, method rewriting, chains, loops, cross-origin, HTTPS downgrade, and cookie preservation are verified.

> **Infra:** Reuses same redirect routes as TASK-020 — see `KestrelFixture.RegisterRedirectRoutes()` ([L575-L658](../src/TurboHttp.IntegrationTests/Shared/KestrelFixture.cs)).

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Http11/04_Http11RedirectTests.cs`
- [ ] 10 tests: 301/302/307/308 GET, 303 POST→GET, 307 preserves POST, 308 preserves POST, chain (5 hops), loop, cross-origin Auth strip, HTTPS→HTTP blocked, relative Location, cookies across redirects
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~Http11RedirectTests"`

### TASK-028: Http11Engine Cookie Integration Tests
**Engine:** [`Http11Engine.cs`](../src/TurboHttp/Streams/Http11Engine.cs)
**Description:** As a developer, I want integration tests for Http11Engine cookie handling so that all RFC 6265 attributes, SameSite, sorting, and cross-redirect persistence are verified.

> **Infra:** Reuses same cookie routes as TASK-021 — see `KestrelFixture.RegisterCookieRoutes()` ([L489-L573](../src/TurboHttp.IntegrationTests/Shared/KestrelFixture.cs)). Also includes `/cookie/set-secure/...`, `/cookie/set-httponly/...`, `/cookie/set-samesite/...`.

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Http11/05_Http11CookieTests.cs`
- [ ] 12 tests: store+send, accumulate, Path, Domain, Secure, HttpOnly, Max-Age=0, expired, multiple Set-Cookie, sorting, redirects, SameSite
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~Http11CookieTests"`

### TASK-029: Http11Engine Retry Integration Tests
**Engine:** [`Http11Engine.cs`](../src/TurboHttp/Streams/Http11Engine.cs)
**Description:** As a developer, I want integration tests for Http11Engine retry handling so that all idempotent methods, POST non-retry, Retry-After (seconds+date), max count, and succeed-after-N are verified.

> **Infra:** Reuses same retry routes as TASK-022 — see `KestrelFixture.RegisterRetryRoutes()` ([L663-L727](../src/TurboHttp.IntegrationTests/Shared/KestrelFixture.cs)).

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Http11/06_Http11RetryTests.cs`
- [ ] 10 tests: GET 503, GET 408, HEAD 503, PUT 503, DELETE 503, POST no-retry, Retry-After seconds, Retry-After date, max count (3), succeed after 2
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~Http11RetryTests"`

### TASK-030: Http11Engine Cache Integration Tests
**Engine:** [`Http11Engine.cs`](../src/TurboHttp/Streams/Http11Engine.cs)
**Description:** As a developer, I want integration tests for Http11Engine caching so that RFC 9111 freshness, validation, no-store, no-cache, Vary, POST invalidation, must-revalidate, min-fresh, max-stale, and LRU eviction are verified.

> **Infra:** Reuses cache routes from `KestrelFixture.RegisterCacheRoutes()` ([L729-L863](../src/TurboHttp.IntegrationTests/Shared/KestrelFixture.cs)): `/cache/max-age/{s}`, `/cache/no-cache`, `/cache/no-store`, `/cache/etag/{id}`, `/cache/last-modified/{id}`, `/cache/vary/{header}`, `/cache/must-revalidate`, `/cache/s-maxage/{s}`, `/cache/expires`, `/cache/private`. Also existing ETag routes: `/etag`, `/if-modified-since`.

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Http11/07_Http11CacheTests.cs`
- [ ] 15 tests: cacheable stored, cached served, stale revalidation, 304 merge, ETag, Last-Modified, no-store, no-cache, Vary, POST invalidation, must-revalidate, HEAD cached, min-fresh, max-stale, LRU eviction
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~Http11CacheTests"`

### TASK-031: Http11Engine Content Encoding Integration Tests
**Engine:** [`Http11Engine.cs`](../src/TurboHttp/Streams/Http11Engine.cs)
**Description:** As a developer, I want integration tests for Http11Engine content encoding so that gzip, deflate, brotli, identity, header removal, Accept-Encoding, and large compressed bodies are verified.

> **Infra:** Reuses same content encoding routes as TASK-023 — see `KestrelFixture.RegisterContentEncodingRoutes()` ([L865-L998](../src/TurboHttp.IntegrationTests/Shared/KestrelFixture.cs)).

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Http11/08_Http11ContentEncodingTests.cs`
- [ ] 7 tests: gzip, deflate, brotli, identity, Content-Encoding removed, Accept-Encoding sent, large (500KB)
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~Http11ContentEncodingTests"`

### TASK-032: Http20Engine Basic Integration Tests
**Engine:** [`Http20Engine.cs`](../src/TurboHttp/Streams/Http20Engine.cs)
**Description:** As a developer, I want integration tests for Http20Engine basic RFC 9113 compliance so that GET, HEAD, POST, PUT, status codes, large bodies, pseudo-headers, and binary round-trip are verified.

> **Infra:** Uses `IClassFixture<KestrelH2Fixture>` ([KestrelH2Fixture.cs](../src/TurboHttp.IntegrationTests/Shared/KestrelH2Fixture.cs)) — h2c on random port, `HttpProtocols.Http2`. Adapt `SendAsync` from TASK-018 with `Http20Engine`. H2-specific routes: `/h2/settings`, `/h2/many-headers`, `/h2/echo-binary`, `/h2/echo-path`, `/h2/large-headers/{kb}`. Shared routes reused via `KestrelFixture.RegisterRedirectRoutes(app)`, etc.

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Http20/01_Http20BasicTests.cs`
- [ ] Uses `KestrelH2Fixture` with `request.Version = HttpVersion.Version20`
- [ ] 9 tests: GET, HEAD, POST, PUT, status codes, large (1MB), pseudo-headers, custom headers, binary body
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~Http20BasicTests"`

### TASK-033: Http20Engine Multiplexing Integration Tests
**Engine:** [`Http20Engine.cs`](../src/TurboHttp/Streams/Http20Engine.cs)
**Description:** As a developer, I want integration tests for Http20Engine multiplexing so that concurrent requests, parallel GET, interleaving, stream IDs, MAX_CONCURRENT_STREAMS, and non-blocking are verified.

> **Infra:** Uses `KestrelH2Fixture`. H2-specific routes: `/h2/settings/max-concurrent` (echoes X-Stream-Id), `/h2/priority/{kb}`, `/slow/{count}` (streaming delay). Need multi-request `SendAsync` variant using `Source.Queue` with capacity > 1.

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Http20/02_Http20MultiplexTests.cs`
- [ ] 6 tests: concurrent on same conn, 10 parallel GETs, interleaved ordering, odd stream IDs, MAX_CONCURRENT_STREAMS, slow doesn't block fast
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~Http20MultiplexTests"`

### TASK-034: Http20Engine Flow Control Integration Tests
**Engine:** [`Http20Engine.cs`](../src/TurboHttp/Streams/Http20Engine.cs)
**Description:** As a developer, I want integration tests for Http20Engine flow control so that large POST bodies, WINDOW_UPDATE, connection/stream-level independence, and small initial windows are verified.

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Http20/03_Http20FlowControlTests.cs`
- [ ] 4 tests: large POST respects windows, WINDOW_UPDATE increases, independent levels, response despite small window
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~Http20FlowControlTests"`

### TASK-035: Http20Engine HPACK Integration Tests
**Engine:** [`Http20Engine.cs`](../src/TurboHttp/Streams/Http20Engine.cs)
**Description:** As a developer, I want integration tests for Http20Engine HPACK so that dynamic table reuse, Huffman decoding, CONTINUATION frames, many headers, and sensitive header indexing are verified.

> **Infra:** Uses `KestrelH2Fixture` — configured with `MaxRequestHeaderCount = 2000` and `MaxRequestHeadersTotalSize = 512KB` for large header tests. Route: `/h2/many-headers` (20 custom headers), `/headers/count`.

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Http20/04_Http20HpackTests.cs`
- [ ] 5 tests: dynamic table reuse, Huffman decoded, CONTINUATION for large headers, 100+ headers round-trip, Authorization NeverIndex
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~Http20HpackTests"`

### TASK-036: Http20Engine Settings & Ping Integration Tests
**Engine:** [`Http20Engine.cs`](../src/TurboHttp/Streams/Http20Engine.cs)
**Description:** As a developer, I want integration tests for Http20Engine SETTINGS and PING so that handshake, MAX_CONCURRENT_STREAMS, INITIAL_WINDOW_SIZE, and PING round-trip are verified.

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Http20/05_Http20SettingsPingTests.cs`
- [ ] 4 tests: SETTINGS exchange, MAX_CONCURRENT_STREAMS applied, INITIAL_WINDOW_SIZE propagated, PING round-trip
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~Http20SettingsPingTests"`

### TASK-037: Http20Engine Redirect Integration Tests
**Engine:** [`Http20Engine.cs`](../src/TurboHttp/Streams/Http20Engine.cs)
**Description:** As a developer, I want integration tests for Http20Engine redirect handling so that all redirect codes, method rewriting, chains, loops, and same-connection reuse are verified over HTTP/2.

> **Infra:** `KestrelH2Fixture` reuses redirect routes via `KestrelFixture.RegisterRedirectRoutes(app)` ([KestrelH2Fixture.cs L264](../src/TurboHttp.IntegrationTests/Shared/KestrelH2Fixture.cs)).

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Http20/06_Http20RedirectTests.cs`
- [ ] 6 tests: 301/302/307/308 follow, 303 POST→GET, 307 preserves POST, chain (5 hops), loop, same connection reuse
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~Http20RedirectTests"`

### TASK-038: Http20Engine Cookie Integration Tests
**Engine:** [`Http20Engine.cs`](../src/TurboHttp/Streams/Http20Engine.cs)
**Description:** As a developer, I want integration tests for Http20Engine cookie handling so that cookie storage, multiple Set-Cookie, HPACK compression, cross-redirect persistence, and Path restriction are verified over HTTP/2.

> **Infra:** `KestrelH2Fixture` reuses cookie routes via `KestrelFixture.RegisterCookieRoutes(app)` ([KestrelH2Fixture.cs L267](../src/TurboHttp.IntegrationTests/Shared/KestrelH2Fixture.cs)). Also has H2-specific `/h2/cookie` route.

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Http20/07_Http20CookieTests.cs`
- [ ] 5 tests: store+send, multiple Set-Cookie, HPACK compressed cookie header, cookies across redirects, Path restriction
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~Http20CookieTests"`

### TASK-039: Http20Engine Retry Integration Tests
**Engine:** [`Http20Engine.cs`](../src/TurboHttp/Streams/Http20Engine.cs)
**Description:** As a developer, I want integration tests for Http20Engine retry handling so that GET retry, POST non-retry, new stream on same connection, REFUSED_STREAM, and GOAWAY retry are verified over HTTP/2.

> **Infra:** `KestrelH2Fixture` reuses retry routes via `KestrelFixture.RegisterRetryRoutes(app)` ([KestrelH2Fixture.cs L270](../src/TurboHttp.IntegrationTests/Shared/KestrelH2Fixture.cs)).

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Http20/08_Http20RetryTests.cs`
- [ ] 5 tests: GET retry 503, POST no-retry, retry new stream, RST_STREAM REFUSED_STREAM trigger, GOAWAY non-zero last-stream retry
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~Http20RetryTests"`

### TASK-040: Http20Engine Cache Integration Tests
**Engine:** [`Http20Engine.cs`](../src/TurboHttp/Streams/Http20Engine.cs)
**Description:** As a developer, I want integration tests for Http20Engine caching so that cache hit, stale revalidation, 304 merge, no-store, and POST invalidation are verified over HTTP/2.

> **Infra:** `KestrelH2Fixture` reuses cache routes via `KestrelFixture.RegisterCacheRoutes(app)` ([KestrelH2Fixture.cs L273](../src/TurboHttp.IntegrationTests/Shared/KestrelH2Fixture.cs)).

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Http20/09_Http20CacheTests.cs`
- [ ] 5 tests: cached served, stale conditional, 304 merge, no-store bypass, POST invalidation
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~Http20CacheTests"`

### TASK-041: Http20Engine Content Encoding Integration Tests
**Engine:** [`Http20Engine.cs`](../src/TurboHttp/Streams/Http20Engine.cs)
**Description:** As a developer, I want integration tests for Http20Engine content encoding so that gzip, deflate, brotli, and large compressed bodies are verified over HTTP/2.

> **Infra:** `KestrelH2Fixture` reuses content encoding routes via `KestrelFixture.RegisterContentEncodingRoutes(app)` ([KestrelH2Fixture.cs L276](../src/TurboHttp.IntegrationTests/Shared/KestrelH2Fixture.cs)).

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Http20/10_Http20ContentEncodingTests.cs`
- [ ] 4 tests: gzip, deflate, brotli, large (500KB)
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~Http20ContentEncodingTests"`

### TASK-042: Http20Engine Error Handling Integration Tests
**Engine:** [`Http20Engine.cs`](../src/TurboHttp/Streams/Http20Engine.cs)
**Description:** As a developer, I want integration tests for Http20Engine error handling so that RST_STREAM isolation, GOAWAY graceful shutdown, protocol error reporting, and automatic reconnection are verified.

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Http20/11_Http20ErrorHandlingTests.cs`
- [ ] 4 tests: RST_STREAM single stream doesn't kill conn, GOAWAY graceful, protocol error as exception, auto-reconnect on closed conn
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~Http20ErrorHandlingTests"`

### TASK-043: TurboHttpClient SendAsync Integration Tests
**Description:** As a developer, I want integration tests for TurboHttpClient.SendAsync so that the public API (BaseAddress, DefaultHeaders, CancellationToken, Timeout, CancelPendingRequests, Dispose, Channel API) is verified end-to-end.

> **Infra:** Uses `KestrelFixture` for HTTP/1.x and `KestrelH2Fixture` for HTTP/2. Unlike engine tests, these test through the public `TurboHttpClient` API (depends on TASK-011 graph materialization). Basic routes `/hello`, `/ping`, `/echo` already available in all fixtures.

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Shared/01_TurboHttpClientTests.cs`
- [ ] 10 tests: SendAsync returns response, BaseAddress, DefaultHeaders, DefaultRequestVersion, CancellationToken, Timeout, CancelPendingRequests, 10 parallel, Dispose, Channel API
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~TurboHttpClientTests"`

### TASK-044: Version Negotiation Integration Tests
**Description:** As a developer, I want integration tests for version negotiation so that HTTP/1.0, 1.1, and 2.0 requests are routed to the correct engine and mixed-version demultiplexing works.

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Shared/02_VersionNegotiationTests.cs`
- [ ] 5 tests: HTTP/1.0 → Http10Engine, HTTP/1.1 → Http11Engine, HTTP/2 → Http20Engine, mixed demux, DefaultRequestVersion override
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~VersionNegotiationTests"`

### TASK-045: Cross-Feature Interaction Integration Tests
**Description:** As a developer, I want integration tests for cross-feature interactions so that combinations like redirect+cookies, cache+redirect, decompression+cache, and all-features-enabled are verified.

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Shared/03_CrossFeatureTests.cs`
- [ ] 8 tests: redirect+cookies, redirect+retry, cache+redirect, cache+cookies, decompression+cache, retry+decompression, all features, flags disabled = passthrough
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~CrossFeatureTests"`

### TASK-046: TLS Integration Tests
**Description:** As a developer, I want integration tests for TLS so that HTTPS with self-signed certs, Secure cookies, HTTPS redirects, and HTTP↔HTTPS transitions are verified.

> **Infra:** Uses [`KestrelTlsFixture`](../src/TurboHttp.IntegrationTests/Shared/KestrelTlsFixture.cs) — HTTPS on random port with self-signed cert (`HttpProtocols.Http1`). Has basic routes (`/hello`, `/ping`, `/echo`, etc.), chunked, caching, range, edge-case routes. No redirect/cookie/retry/cache routes registered — may need to add if cross-scheme tests require them.

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Shared/04_TlsTests.cs`
- [ ] Uses `KestrelTlsFixture`
- [ ] 5 tests: HTTPS self-signed, Secure cookie HTTPS-only, HTTPS redirect preserves TLS, HTTP→HTTPS upgrade, HTTPS→HTTP blocked
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~TlsTests"`

### TASK-047: Edge Case & Error Handling Integration Tests
**Description:** As a developer, I want integration tests for edge cases so that mid-response close, large headers, empty bodies, unknown encoding, non-existent host, non-listening port, timeouts, and multi-host concurrency are verified.

**Acceptance Criteria:**
- [ ] File created: `src/TurboHttp.IntegrationTests/Shared/05_EdgeCaseTests.cs`
- [ ] 8 tests: server closes mid-response, 32KB header, empty body, unknown Content-Encoding passthrough, non-existent host throws, non-listening port throws, connection timeout, concurrent multi-host
- [ ] All tests pass: `dotnet test --filter "FullyQualifiedName~EdgeCaseTests"`

### TASK-048: Validation Gate
**Description:** As a developer, I want a final validation gate that verifies all existing tests still pass, all new tests pass, zero compiler warnings, and a documented RFC compliance matrix.

**Acceptance Criteria:**
- [ ] All existing unit tests pass (310+ protocol, 100+ stream)
- [ ] All new stage unit tests pass (~79 tests)
- [ ] All Http10Engine integration tests pass (~42 tests)
- [ ] All Http11Engine integration tests pass (~82 tests)
- [ ] All Http20Engine integration tests pass (~68 tests)
- [ ] All cross-engine/client tests pass (~38 tests)
- [ ] Zero compiler warnings (excluding [Obsolete] deprecations)
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` succeeds
- [ ] `dotnet test src/TurboHttp.sln` all pass
- [ ] RFC compliance documented: RFC 1945, 9112, 9113, 9110, 7541, 6265, 9111 at 100%
- [ ] Performance baseline: benchmarks show < 5% overhead from new stages

## Functional Requirements

- FR-1: Each new stage must implement a single responsibility as an Akka.Streams `GraphStage`
- FR-2: All stages must be composable — testable in isolation with mocked upstream/downstream
- FR-3: Each stage must be optional via feature flags on `TurboClientOptions` (disabled by default)
- FR-4: Existing tests must continue to pass without modification (backward compatibility)
- FR-5: `CookieInjectionStage` must call `CookieJar.AddCookiesToRequest` and pass-through when CookieJar is null
- FR-6: `CookieStorageStage` must call `CookieJar.ProcessResponse` as side-effect without modifying the response
- FR-7: `DecompressionStage` must handle gzip, deflate, br, x-gzip and remove Content-Encoding after decompression
- FR-8: `CacheLookupStage` must emit to two outlets: cache hit (response) and cache miss (request to engine)
- FR-9: `CacheStorageStage` must merge 304 responses, store 2xx, and invalidate on unsafe methods
- FR-10: `RedirectStage` must follow 301/302/303/307/308 with correct method rewriting per RFC 9110 §15.4
- FR-11: `RedirectStage` must detect infinite loops and enforce max redirect count
- FR-12: `RetryStage` must only retry idempotent methods (GET/HEAD/PUT/DELETE) per RFC 9110 §9.2
- FR-13: `RetryStage` must respect `Retry-After` header (both seconds and HTTP-date formats)
- FR-14: `ConnectionReuseStage` must signal connection pool to keep-alive or close based on protocol version and headers
- FR-15: `Engine.CreateFlow` must conditionally wire stages based on `TurboClientOptions` feature flags
- FR-16: `TurboClientStreamManager` must materialize the full graph so `SendAsync` works end-to-end
- FR-17: All Kestrel fixture routes must be registered in both `KestrelFixture` and `KestrelH2Fixture` (except connection-reuse routes which are HTTP/1.x only)
- FR-18: All integration tests must use real TCP connections via Kestrel — no mocks for the network layer

## Non-Goals

- No server-side HTTP implementation — this is client-only
- No HTTP/3 (QUIC) support — post-v1.0
- No NuGet packaging or distribution — separate initiative
- No Microsoft.Extensions.DependencyInjection integration — future work
- No changes to existing protocol handlers (RedirectHandler, CookieJar, etc.) — wrap as-is
- No performance optimization of the stages — correctness first, optimize later
- No WebSocket or HTTP upgrade protocol support
- No proxy or SOCKS support

## Technical Considerations

- **Akka.Streams GraphStage complexity**: RedirectStage and RetryStage require internal feedback loops. Consider `BidiShape` or `RestartFlow` patterns to avoid back-pressure deadlocks.
- **Shared state thread safety**: `CookieJar` and `HttpCacheStore` are accessed from stream materialization threads. Both are already thread-safe but verify under concurrent integration test load.
- **RedirectHandler is per-request stateful** (redirect count). Must create new instance per request chain, not share across streams.
- **Kestrel fixture routes with state**: `/retry/succeed-after/{n}` requires server-side request counting. Use `ConcurrentDictionary` or similar in the fixture.
- **CacheLookupStage FanOutShape**: This is the most complex stage topologically. Must handle back-pressure from both outlets correctly.
- **Test isolation**: Each integration test should use its own `TurboHttpClient` instance to avoid CookieJar/Cache state leaking between tests.
- **Platform**: .NET 10.0, Akka.Streams 1.5.60, xunit 2.9.3

## Success Metrics

- All 309 new tests pass (79 stage + 230 integration)
- All 310+ existing tests continue to pass
- `TurboHttpClient.SendAsync` works end-to-end for HTTP/1.0, HTTP/1.1, and HTTP/2
- 100% RFC compliance documented for all 7 RFCs
- Zero compiler errors, zero compiler warnings (excluding [Obsolete])
- Performance overhead from new stages < 5% on existing benchmarks

## Open Questions

- Should `RedirectStage` be implemented as a `GraphStage` with internal feedback loop, or as a `BidiShape` wrapping the engine? The feedback loop approach is simpler but may have back-pressure issues. I would prefer Bidishape.
- Should caching be per-host or global? Currently `HttpCacheStore` is per-client, but RFC 9111 allows shared caches. I would prefer per client
- Should `RetryStage` delay be implemented via `Task.Delay` inside the stage or via Akka.Streams' built-in `DelayFlow`? I would prefer DelayFlow.
- What should the default max redirect count be? .NET's `HttpClient` uses 50, browsers typically use 20. I would prefer 50
- Should `ConnectionReuseStage` signal via actor messages or via a shared concurrent data structure? I would prefer shared structures.
- Do we need HTTP/2 GOAWAY-triggered retry in `RetryStage`, or should that be handled within `Http20Engine` internally? Would prefer in the Http20Engine
