# Plan: Integration Test Coverage

## Introduction

TurboHttp has 1,827 unit tests and 487 stream tests covering all RFC sections — but zero integration tests. The Kestrel test infrastructure (3 fixtures, 100+ unique routes, TestKit base class) is fully prepared and waiting. This plan fills the gap with ~300 end-to-end integration tests across ~30 test files that validate cross-cutting behaviors only observable when all layers interact over real TCP/TLS connections: connection pooling, stream multiplexing, cookie+redirect interaction, cache timing, content encoding, retry with stateful servers, range requests, form handling, streaming responses, error resilience, and TLS security enforcement.

## Goals

- Achieve integration test coverage across all 7 RFCs (1945, 9112, 9113, 7541, 9110, 6265, 9111)
- Validate full pipeline: request enrichment → cookie → cache → engine → TCP → decode → decompress → cookie store → cache store → retry → redirect → response
- Validate actor + stream + channel coordination through real TCP (PoolRouterActor → HostPoolActor → ConnectionActor → ConnectionHandle)
- Cover **every registered route** across all 3 Kestrel fixtures (100+ unique routes)
- Produce ~300 integration tests across ~30 test files
- All tests use DisplayName convention: `INT-{RFC}-{section}-{CAT}-{NNN}: description`

## User Stories

---

### Phase 1: Infrastructure

---

### TASK-001: Integration Test Helper — `CreateClient` Factory Method
**Description:** As a test author, I want a shared helper that creates a fully wired `ITurboHttpClient` pointing at a Kestrel fixture so that every integration test can send real HTTP requests with minimal boilerplate.

**Acceptance Criteria:**
- [ ] `TestKit` base class extended with `CreateClient(int port, Version httpVersion)` method
- [ ] Returns `ITurboHttpClient` wired with: `BaseAddress = http://127.0.0.1:{port}`, `DefaultRequestVersion = httpVersion`, full pipeline (cookie jar, cache, redirect, retry, decompression)
- [ ] Client is `IAsyncDisposable` — tears down actor system and materializer
- [ ] A second overload `CreateClient(int port, Version httpVersion, TurboClientOptions options)` allows custom config (e.g. max redirects, retry policy, custom CookieJar)
- [ ] For TLS fixture: option to provide `ServerCertificateCustomValidationCallback` that trusts the self-signed cert
- [ ] Smoke test: one `[Fact]` sends GET `/hello` via `KestrelFixture` with HTTP/1.1, asserts 200 + "Hello World"
- [ ] `dotnet build` and `dotnet test --filter "FullyQualifiedName~IntegrationTests"` pass

---

### Phase 2: HTTP/1.1 Tests

---

### TASK-002: HTTP/1.1 Basic Request/Response Tests
**Description:** As a maintainer, I want basic HTTP/1.1 end-to-end tests so that the simplest request/response path is validated through real TCP.

**File:** `Http11/01_Http11BasicTests.cs`
**Fixture:** `KestrelFixture` (IClassFixture)

**Routes covered:** `/hello`, `/ping`, `/echo`, `/status/{code}`, `/large/{kb}`, `/headers/echo`, `/multiheader`, `/empty-cl`, `/any`, `/methods`, `/content/{ct}`

**Acceptance Criteria:**
- [ ] `INT-9112-2-BAS-001`: GET `/hello` → 200, body = "Hello World"
- [ ] `INT-9112-2-BAS-002`: HEAD `/hello` → 200, no body, Content-Length present
- [ ] `INT-9112-2-BAS-003`: POST `/echo` with "test body" → 200, body echoed back
- [ ] `INT-9112-2-BAS-004`: PUT `/echo` with JSON body → 200, body + Content-Type preserved
- [ ] `INT-9112-2-BAS-005`: PATCH `/echo` with body → 200, body echoed
- [ ] `INT-9112-2-BAS-006`: DELETE `/any` → 200, body = "DELETE"
- [ ] `INT-9112-2-BAS-007`: OPTIONS `/any` → 200, body = "OPTIONS"
- [ ] `INT-9112-2-BAS-008`: GET `/status/204` → 204, no body
- [ ] `INT-9112-2-BAS-009`: GET `/status/404` → 404
- [ ] `INT-9112-2-BAS-010`: GET `/status/500` → 500
- [ ] `INT-9112-2-BAS-011`: GET `/large/100` → 200, body = 102400 bytes of repeating A-Z pattern
- [ ] `INT-9112-2-BAS-012`: GET `/headers/echo` with custom X-Test header → header echoed in response
- [ ] `INT-9112-2-BAS-013`: GET `/multiheader` → response has two X-Value headers (alpha, beta)
- [ ] `INT-9112-2-BAS-014`: GET `/empty-cl` → 200, Content-Length: 0, empty body
- [ ] `INT-9112-2-BAS-015`: GET `/ping` → 200, body = "pong"
- [ ] `INT-9112-2-BAS-016`: GET `/methods` → 200, body = "GET"
- [ ] `INT-9112-2-BAS-017`: GET `/content/application/xml` → 200, Content-Type: application/xml
- [ ] All tests use `DisplayName` with `INT-9112` prefix
- [ ] `dotnet test --filter "FullyQualifiedName~Http11BasicTests"` — all pass

---

### TASK-003: HTTP/1.1 Chunked Transfer Tests
**Description:** As a maintainer, I want chunked transfer encoding validated end-to-end so that HTTP/1.1's most complex framing feature is proven through real Kestrel responses.

**File:** `Http11/02_Http11ChunkedTests.cs`
**Fixture:** `KestrelFixture`

**Routes covered:** `/chunked/{kb}`, `/chunked/exact/{count}/{bytes}`, `/chunked/trailer`, `/chunked/md5`, `/echo/chunked`

**Acceptance Criteria:**
- [ ] `INT-9112-7-CHK-001`: GET `/chunked/1` → 200, body = 1024 bytes, Transfer-Encoding: chunked
- [ ] `INT-9112-7-CHK-002`: GET `/chunked/100` → 200, body = 102400 bytes (large chunked)
- [ ] `INT-9112-7-CHK-003`: GET `/chunked/exact/5/100` → 200, body = 500 bytes
- [ ] `INT-9112-7-CHK-004`: HEAD `/chunked/1` → 200, no body
- [ ] `INT-9112-7-CHK-005`: GET `/chunked/trailer` → 200, response has trailer headers (X-Checksum)
- [ ] `INT-9112-7-CHK-006`: GET `/chunked/md5` → 200, Content-MD5 header present
- [ ] `INT-9112-7-CHK-007`: POST `/echo/chunked` with body → 200, body echoed via chunked response
- [ ] `INT-9112-7-CHK-008`: GET `/chunked/exact/1/1` → 200, minimal chunked (1 chunk, 1 byte)
- [ ] `INT-9112-7-CHK-009`: GET `/chunked/exact/50/20` → 200, many small chunks (50 x 20 bytes)
- [ ] `dotnet test --filter "FullyQualifiedName~Http11ChunkedTests"` — all pass

---

### TASK-004: HTTP/1.1 Connection Management Tests
**Description:** As a maintainer, I want connection reuse, keep-alive, and close behavior validated against real TCP so that the I/O actor pool is proven to work correctly.

**File:** `Http11/03_Http11ConnectionTests.cs`
**Fixture:** `KestrelFixture`

**Routes covered:** `/conn/default`, `/conn/close`, `/conn/keep-alive`, `/hello`, `/close`, `/large/{kb}`, `/slow/{count}`, `/delay/{ms}`

**Acceptance Criteria:**
- [ ] `INT-9112-9-CON-001`: Two sequential GETs to `/conn/default` → both succeed (HTTP/1.1 default keep-alive)
- [ ] `INT-9112-9-CON-002`: GET `/conn/close` → 200, connection closed after response (Connection: close header)
- [ ] `INT-9112-9-CON-003`: GET `/conn/keep-alive` → 200, Connection: Keep-Alive header present
- [ ] `INT-9112-9-CON-004`: 5 sequential GETs to `/hello` → all 200 (connection reuse)
- [ ] `INT-9112-9-CON-005`: GET `/close` → subsequent GET `/hello` succeeds on new connection
- [ ] `INT-9112-9-CON-006`: GET `/large/1000` → 200, large body (1024000 bytes) fully received
- [ ] `INT-9112-9-CON-007`: GET `/slow/50` → 200, slow response fully received (50 bytes streamed at 1ms each)
- [ ] `INT-9112-9-CON-008`: GET `/delay/500` → 200, "delayed" (server-side 500ms delay, client waits)
- [ ] `INT-9112-9-CON-009`: GET `/delay/1000` → 200, 1 second server delay still completes
- [ ] `INT-9112-9-CON-010`: 10 sequential GETs to different routes → all succeed on same connection
- [ ] `dotnet test --filter "FullyQualifiedName~Http11ConnectionTests"` — all pass

---

### TASK-005: HTTP/1.1 Redirect Tests
**Description:** As a maintainer, I want all RFC 9110 §15.4 redirect behaviors validated end-to-end so that method rewriting, chain following, loop detection, and cross-origin rules are proven.

**File:** `Http11/04_Http11RedirectTests.cs`
**Fixture:** `KestrelFixture`

**Routes covered:** `/redirect/{code}/{target}`, `/redirect/chain/{n}`, `/redirect/loop`, `/redirect/relative`, `/redirect/cross-origin`, `/redirect/cross-origin-auth`, `/redirect/307`, `/redirect/303`, `/redirect/302`, `/redirect/308`

**Acceptance Criteria:**
- [ ] `INT-9110-15.4-RDR-001`: GET `/redirect/301/hello` → follows redirect, final 200 "Hello World"
- [ ] `INT-9110-15.4-RDR-002`: GET `/redirect/302/hello` → follows redirect, final 200
- [ ] `INT-9110-15.4-RDR-003`: POST `/redirect/302` → method rewritten to GET (RFC 9110 §15.4.3), final 200
- [ ] `INT-9110-15.4-RDR-004`: POST `/redirect/303` → method rewritten to GET, final 200
- [ ] `INT-9110-15.4-RDR-005`: POST `/redirect/307` → method preserved (POST), body preserved, final echoed body
- [ ] `INT-9110-15.4-RDR-006`: POST `/redirect/308` → method preserved (POST), body preserved
- [ ] `INT-9110-15.4-RDR-007`: GET `/redirect/chain/3` → follows 3 hops, final 200 "Hello World"
- [ ] `INT-9110-15.4-RDR-008`: GET `/redirect/chain/10` → follows 10 hops (or fails if max exceeded)
- [ ] `INT-9110-15.4-RDR-009`: GET `/redirect/loop` → throws `RedirectException` (loop detected)
- [ ] `INT-9110-15.4-RDR-010`: GET `/redirect/relative` → resolves relative Location, final 200
- [ ] `INT-9110-15.4-RDR-011`: GET `/redirect/cross-origin` → follows cross-origin redirect to `/headers/echo`
- [ ] `INT-9110-15.4-RDR-012`: GET `/redirect/cross-origin-auth` with Authorization header → header stripped after cross-origin redirect
- [ ] `INT-9110-15.4-RDR-013`: GET `/redirect/301/hello` + GET `/redirect/302/hello` → both work sequentially
- [ ] `INT-9110-15.4-RDR-014`: POST `/redirect/301/hello` → POST with 301 → method rewritten to GET (tests POST→GET on 301)
- [ ] `dotnet test --filter "FullyQualifiedName~Http11RedirectTests"` — all pass

---

### TASK-006: HTTP/1.1 Retry Tests
**Description:** As a maintainer, I want RFC 9110 §9.2 retry behavior validated end-to-end with a stateful server so that idempotency rules and Retry-After parsing are proven.

**File:** `Http11/05_Http11RetryTests.cs`
**Fixture:** `KestrelFixture`

**Routes covered:** `/retry/408`, `/retry/503`, `/retry/503-retry-after/{s}`, `/retry/503-retry-after-date`, `/retry/succeed-after/{n}`, `/retry/non-idempotent-503`

**Acceptance Criteria:**
- [ ] `INT-9110-9.2-RTY-001`: GET `/retry/408` → retried, eventually succeeds or returns 408
- [ ] `INT-9110-9.2-RTY-002`: GET `/retry/503` → retried (idempotent GET)
- [ ] `INT-9110-9.2-RTY-003`: GET `/retry/503-retry-after/1` → retried after ~1 second, Retry-After respected
- [ ] `INT-9110-9.2-RTY-004`: GET `/retry/503-retry-after-date` → retried after HTTP-date delay
- [ ] `INT-9110-9.2-RTY-005`: GET `/retry/succeed-after/3?key=test1` → fails twice, succeeds on 3rd attempt, final 200
- [ ] `INT-9110-9.2-RTY-006`: PUT `/retry/503` → retried (PUT is idempotent)
- [ ] `INT-9110-9.2-RTY-007`: DELETE `/retry/503` → retried (DELETE is idempotent)
- [ ] `INT-9110-9.2-RTY-008`: POST `/retry/non-idempotent-503` → NOT retried, returns 503 directly
- [ ] `INT-9110-9.2-RTY-009`: HEAD `/retry/503` → retried (HEAD is idempotent)
- [ ] `INT-9110-9.2-RTY-010`: GET `/retry/succeed-after/2?key=test2` → fails once, succeeds on 2nd attempt
- [ ] `dotnet test --filter "FullyQualifiedName~Http11RetryTests"` — all pass

---

### TASK-007: HTTP/1.1 Cookie Tests
**Description:** As a maintainer, I want RFC 6265 cookie behavior validated end-to-end so that Set-Cookie storage, Cookie injection, domain/path matching, and redirect interaction are proven.

**File:** `Http11/06_Http11CookieTests.cs`
**Fixture:** `KestrelFixture`

**Routes covered:** `/cookie/set/{name}/{value}`, `/cookie/echo`, `/cookie/set-httponly/{name}/{value}`, `/cookie/set-expires/{name}/{value}/{s}`, `/cookie/set-multiple`, `/cookie/delete/{name}`, `/cookie/set-path/{name}/{value}/{path}`, `/cookie/set-domain/{name}/{value}/{domain}`, `/cookie/set-samesite/{name}/{value}/{policy}`, `/cookie/set-and-redirect`

**Acceptance Criteria:**
- [ ] `INT-6265-5.3-CKE-001`: GET `/cookie/set/session/abc123` then GET `/cookie/echo` → Cookie: session=abc123 sent
- [ ] `INT-6265-5.3-CKE-002`: GET `/cookie/set-httponly/token/xyz` then GET `/cookie/echo` → cookie present
- [ ] `INT-6265-5.3-CKE-003`: GET `/cookie/set-expires/temp/val/2` then wait 3s then GET `/cookie/echo` → cookie expired, not sent
- [ ] `INT-6265-5.3-CKE-004`: GET `/cookie/set-multiple` then GET `/cookie/echo` → all 3 cookies (alpha, beta, gamma) sent
- [ ] `INT-6265-5.3-CKE-005`: GET `/cookie/delete/session` (after setting) then GET `/cookie/echo` → cookie removed
- [ ] `INT-6265-5.1.4-CKE-006`: GET `/cookie/set-path/scoped/val/api` then GET `/cookie/echo` → cookie NOT sent (path mismatch)
- [ ] `INT-6265-5.1.3-CKE-007`: GET `/cookie/set-domain/dom/val/127.0.0.1` then GET `/cookie/echo` → domain matching applied
- [ ] `INT-6265-5.3-CKE-008`: GET `/cookie/set-samesite/strict/val/Strict` then GET `/cookie/echo` → SameSite stored
- [ ] `INT-6265-5.3-CKE-009`: GET `/cookie/set-and-redirect` → follows redirect, cookie available at `/cookie/echo`
- [ ] `INT-6265-5.3-CKE-010`: Two separate client instances → cookies are NOT shared (isolated CookieJar)
- [ ] `INT-6265-5.3-CKE-011`: GET `/cookie/set-secure/sec/val` over HTTP → Secure cookie NOT stored (requires HTTPS)
- [ ] `dotnet test --filter "FullyQualifiedName~Http11CookieTests"` — all pass

---

### TASK-008: HTTP/1.1 Cache Tests
**Description:** As a maintainer, I want RFC 9111 caching behavior validated end-to-end so that freshness, revalidation, 304 merge, and Vary support are proven with real server timing.

**File:** `Http11/07_Http11CacheTests.cs`
**Fixture:** `KestrelFixture`

**Routes covered:** `/cache/max-age/{s}`, `/cache/no-store`, `/cache/no-cache`, `/cache/etag/{id}`, `/cache/last-modified/{id}`, `/cache/vary/{header}`, `/cache/must-revalidate`, `/cache/s-maxage/{s}`, `/cache/expires`, `/cache/private`

**Acceptance Criteria:**
- [ ] `INT-9111-4.2-CCH-001`: GET `/cache/max-age/60` twice → second response served from cache (same body)
- [ ] `INT-9111-4.2-CCH-002`: GET `/cache/max-age/1` → wait 2s → GET again → cache stale, re-fetched (different timestamp body)
- [ ] `INT-9111-3-CCH-003`: GET `/cache/no-store` twice → both hit server (body timestamps differ)
- [ ] `INT-9111-4.2-CCH-004`: GET `/cache/no-cache` twice → second triggers revalidation
- [ ] `INT-9111-4.3-CCH-005`: GET `/cache/etag/v1` twice → second request sends `If-None-Match: "v1"`, gets 304
- [ ] `INT-9111-4.3-CCH-006`: GET `/cache/last-modified/item1` twice → second sends `If-Modified-Since`, gets 304
- [ ] `INT-9111-4.2-CCH-007`: GET `/cache/vary/Accept` with `Accept: application/json` then with `Accept: text/html` → different cached entries
- [ ] `INT-9111-4.2-CCH-008`: GET `/cache/must-revalidate` twice → second triggers revalidation via ETag
- [ ] `INT-9111-4.2-CCH-009`: GET `/cache/s-maxage/60` twice → second served from cache
- [ ] `INT-9111-4.2-CCH-010`: GET `/cache/expires` twice → second served from cache (Expires 1 hour)
- [ ] `INT-9111-3-CCH-011`: GET `/cache/private` → response cached (client cache stores it)
- [ ] `dotnet test --filter "FullyQualifiedName~Http11CacheTests"` — all pass

---

### TASK-009: HTTP/1.1 Content Encoding Tests
**Description:** As a maintainer, I want RFC 9110 §8.4 content encoding validated end-to-end so that gzip/deflate/brotli decompression is proven through real compressed server responses.

**File:** `Http11/08_Http11ContentEncodingTests.cs`
**Fixture:** `KestrelFixture`

**Routes covered:** `/compress/gzip/{kb}`, `/compress/deflate/{kb}`, `/compress/br/{kb}`, `/compress/identity/{kb}`, `/compress/negotiate`

**Acceptance Criteria:**
- [ ] `INT-9110-8.4-ENC-001`: GET `/compress/gzip/10` → 200, body decompressed to 10240 bytes
- [ ] `INT-9110-8.4-ENC-002`: GET `/compress/deflate/10` → 200, body decompressed to 10240 bytes
- [ ] `INT-9110-8.4-ENC-003`: GET `/compress/br/10` → 200, body decompressed to 10240 bytes
- [ ] `INT-9110-8.4-ENC-004`: GET `/compress/identity/10` → 200, body = 10240 bytes (no decompression)
- [ ] `INT-9110-8.4-ENC-005`: GET `/compress/gzip/1` → 200, small payload (1024 bytes) decompressed correctly
- [ ] `INT-9110-8.4-ENC-006`: GET `/compress/gzip/500` → 200, large payload (512000 bytes) decompressed correctly
- [ ] `INT-9110-8.4-ENC-007`: GET `/compress/negotiate` with `Accept-Encoding: gzip` → server-driven negotiation, body decompressed
- [ ] `INT-9110-8.4-ENC-008`: GET `/compress/negotiate` with `Accept-Encoding: identity` → no compression applied
- [ ] `INT-9110-8.4-ENC-009`: GET `/compress/negotiate` with `Accept-Encoding: br` → brotli negotiated and decompressed
- [ ] `INT-9110-8.4-ENC-010`: GET `/compress/negotiate` with `Accept-Encoding: deflate` → deflate negotiated and decompressed
- [ ] `dotnet test --filter "FullyQualifiedName~Http11ContentEncodingTests"` — all pass

---

### TASK-010: HTTP/1.1 Content Negotiation Tests
**Description:** As a maintainer, I want RFC 9110 §12.5 content negotiation validated end-to-end so that Accept header handling and Vary behavior are proven.

**File:** `Http11/09_Http11ContentNegotiationTests.cs`
**Fixture:** `KestrelFixture`

**Routes covered:** `/negotiate`, `/negotiate/vary`, `/gzip-meta`

**Acceptance Criteria:**
- [ ] `INT-9110-12.5-NEG-001`: GET `/negotiate` with `Accept: application/json` → Content-Type: application/json, JSON body
- [ ] `INT-9110-12.5-NEG-002`: GET `/negotiate` with `Accept: text/html` → Content-Type: text/html, HTML body
- [ ] `INT-9110-12.5-NEG-003`: GET `/negotiate` with `Accept: text/plain` → Content-Type: text/plain
- [ ] `INT-9110-12.5-NEG-004`: GET `/negotiate/vary` → response contains Vary: Accept header
- [ ] `INT-9110-12.5-NEG-005`: GET `/gzip-meta` → 200 "encoded-body" with Content-Encoding: identity
- [ ] `dotnet test --filter "FullyQualifiedName~Http11ContentNegotiationTests"` — all pass

---

### TASK-011: HTTP/1.1 Range Request Tests
**Description:** As a maintainer, I want RFC 9110 §14 range request behavior validated end-to-end so that partial content retrieval is proven through real Kestrel responses.

**File:** `Http11/10_Http11RangeTests.cs`
**Fixture:** `KestrelFixture`

**Routes covered:** `/range/{kb}`, `/range/etag`

**Acceptance Criteria:**
- [ ] `INT-9110-14-RNG-001`: GET `/range/10` without Range header → 200, full body (10240 bytes, sequential 0-255 pattern)
- [ ] `INT-9110-14-RNG-002`: GET `/range/10` with `Range: bytes=0-99` → 206 Partial Content, 100 bytes
- [ ] `INT-9110-14-RNG-003`: GET `/range/10` with `Range: bytes=0-0` → 206, 1 byte
- [ ] `INT-9110-14-RNG-004`: GET `/range/10` with `Range: bytes=-100` → 206, last 100 bytes
- [ ] `INT-9110-14-RNG-005`: GET `/range/10` with `Range: bytes=5000-` → 206, bytes from offset 5000 to end
- [ ] `INT-9110-14-RNG-006`: GET `/range/etag` → 200, body = 512 bytes, ETag: "range-v1"
- [ ] `INT-9110-14-RNG-007`: GET `/range/etag` with `If-Range: "range-v1"` + `Range: bytes=0-99` → 206 (ETag matches)
- [ ] `INT-9110-14-RNG-008`: GET `/range/etag` with `If-Range: "wrong"` + `Range: bytes=0-99` → 200 full body (ETag mismatch)
- [ ] `dotnet test --filter "FullyQualifiedName~Http11RangeTests"` — all pass

---

### TASK-012: HTTP/1.1 Form Submission Tests
**Description:** As a maintainer, I want form submission (urlencoded + multipart) validated end-to-end so that POST body encoding is proven.

**File:** `Http11/11_Http11FormTests.cs`
**Fixture:** `KestrelFixture`

**Routes covered:** `/form/urlencoded`, `/form/multipart`

**Acceptance Criteria:**
- [ ] `INT-9110-POST-FRM-001`: POST `/form/urlencoded` with `application/x-www-form-urlencoded` body → 200, "received:{length}"
- [ ] `INT-9110-POST-FRM-002`: POST `/form/urlencoded` with empty body → 200, "received:0"
- [ ] `INT-9110-POST-FRM-003`: POST `/form/multipart` with multipart/form-data body → 200, "received:{length}"
- [ ] `INT-9110-POST-FRM-004`: POST `/form/multipart` with large multipart body (100KB) → 200, correct length echoed
- [ ] `INT-9110-POST-FRM-005`: POST `/form/urlencoded` with special characters (UTF-8) → 200, correct length
- [ ] `dotnet test --filter "FullyQualifiedName~Http11FormTests"` — all pass

---

### TASK-013: HTTP/1.1 Slow/Streaming Response Tests
**Description:** As a maintainer, I want slow and streaming server responses validated end-to-end so that the client handles incremental data delivery correctly without timeouts or data loss.

**File:** `Http11/12_Http11StreamingTests.cs`
**Fixture:** `KestrelFixture`

**Routes covered:** `/slow/{count}`, `/delay/{ms}`

**Acceptance Criteria:**
- [ ] `INT-9112-STR-001`: GET `/slow/10` → 200, body = 10 bytes of 'x' (streamed 1ms apart)
- [ ] `INT-9112-STR-002`: GET `/slow/100` → 200, body = 100 bytes (streamed)
- [ ] `INT-9112-STR-003`: GET `/slow/500` → 200, body = 500 bytes (longer streaming, ~500ms)
- [ ] `INT-9112-STR-004`: GET `/delay/100` → 200, "delayed" (100ms server delay)
- [ ] `INT-9112-STR-005`: GET `/delay/2000` → 200, "delayed" (2s server delay, within client timeout)
- [ ] `INT-9112-STR-006`: 3 concurrent GETs to `/slow/50` → all complete with correct bodies
- [ ] `dotnet test --filter "FullyQualifiedName~Http11StreamingTests"` — all pass

---

### TASK-014: HTTP/1.1 Edge Case / Resilience Tests
**Description:** As a maintainer, I want edge-case server behaviors tested so that the client handles unusual or hostile responses gracefully.

**File:** `Http11/13_Http11EdgeCaseTests.cs`
**Fixture:** `KestrelFixture`

**Routes covered:** `/edge/close-mid-response`, `/edge/large-header/{kb}`, `/edge/unknown-encoding`, `/edge/empty-body`, `/unknown-headers`, `/headers/set`

**Acceptance Criteria:**
- [ ] `INT-9112-EDGE-001`: GET `/edge/close-mid-response` → client detects incomplete response (exception or partial body)
- [ ] `INT-9112-EDGE-002`: GET `/edge/large-header/1` → 200, X-Large-Header with 1024 characters parsed correctly
- [ ] `INT-9112-EDGE-003`: GET `/edge/large-header/10` → 200, 10KB header value parsed correctly
- [ ] `INT-9112-EDGE-004`: GET `/edge/unknown-encoding` → 200, body = "raw-payload" (Content-Encoding: x-custom not decompressed)
- [ ] `INT-9112-EDGE-005`: GET `/edge/empty-body` → 200, empty body (no Content-Length)
- [ ] `INT-9112-EDGE-006`: GET `/unknown-headers` → 200, X-Unknown-Foo and X-Unknown-Bar present in response
- [ ] `INT-9112-EDGE-007`: GET `/headers/set?X-Custom=value` → 200, X-Custom: value in response headers
- [ ] `INT-9112-EDGE-008`: GET `/large/1000` → 200, 1024000 bytes fully received (1MB edge case)
- [ ] `INT-9112-EDGE-009`: GET `/status/100` → handled correctly (informational 1xx)
- [ ] `INT-9112-EDGE-010`: GET `/status/301` without Location → 301 returned as-is (no redirect without Location)
- [ ] `dotnet test --filter "FullyQualifiedName~Http11EdgeCaseTests"` — all pass

---

### TASK-015: HTTP/1.1 Auth Header Tests
**Description:** As a maintainer, I want Authorization header handling validated end-to-end including cross-origin stripping.

**File:** `Http11/14_Http11AuthTests.cs`
**Fixture:** `KestrelFixture`

**Routes covered:** `/auth`, `/redirect/cross-origin-auth`

**Acceptance Criteria:**
- [ ] `INT-9110-AUTH-001`: GET `/auth` without Authorization → 401
- [ ] `INT-9110-AUTH-002`: GET `/auth` with `Authorization: Bearer token123` → 200
- [ ] `INT-9110-AUTH-003`: GET `/auth` with `Authorization: Basic base64data` → 200
- [ ] `INT-9110-AUTH-004`: GET `/redirect/cross-origin-auth` with Authorization → follows redirect, Authorization stripped at `/auth` → 401
- [ ] `INT-9110-AUTH-005`: GET `/auth` with custom X-API-Key header → 200 (Authorization present is enough)
- [ ] `dotnet test --filter "FullyQualifiedName~Http11AuthTests"` — all pass

---

### Phase 3: HTTP/2 Tests

---

### TASK-016: HTTP/2 Basic Tests
**Description:** As a maintainer, I want basic HTTP/2 end-to-end tests so that connection preface, SETTINGS exchange, HPACK, and basic request/response are validated over real h2c TCP.

**File:** `Http20/01_Http20BasicTests.cs`
**Fixture:** `KestrelH2Fixture`

**Routes covered:** `/hello`, `/ping`, `/echo`, `/status/{code}`, `/h2/settings`, `/h2/echo-path`, `/methods`, `/any`

**Acceptance Criteria:**
- [ ] `INT-9113-3.4-BAS-001`: GET `/hello` → 200, body = "Hello World" (proves preface + SETTINGS + HPACK all work)
- [ ] `INT-9113-3.4-BAS-002`: GET `/ping` → 200, body = "pong"
- [ ] `INT-9113-8-BAS-003`: HEAD `/hello` → 200, no body
- [ ] `INT-9113-8-BAS-004`: POST `/echo` with body → 200, body echoed
- [ ] `INT-9113-8-BAS-005`: PUT `/echo` with body → 200, body echoed
- [ ] `INT-9113-8-BAS-006`: GET `/status/204` → 204
- [ ] `INT-9113-8-BAS-007`: GET `/status/404` → 404
- [ ] `INT-9113-8-BAS-008`: GET `/status/500` → 500
- [ ] `INT-9113-8-BAS-009`: GET `/h2/settings` → 200, "h2-ok" (confirms HTTP/2 active)
- [ ] `INT-9113-8-BAS-010`: GET `/h2/echo-path` → body contains request path
- [ ] `INT-9113-8-BAS-011`: GET `/methods` → body = "GET"
- [ ] `INT-9113-8-BAS-012`: DELETE `/any` → 200, body = "DELETE"
- [ ] `dotnet test --filter "FullyQualifiedName~Http20BasicTests"` — all pass

---

### TASK-017: HTTP/2 Multiplexing Tests
**Description:** As a maintainer, I want HTTP/2 stream multiplexing validated so that concurrent requests on a single connection are proven to work — something impossible to test without real TCP.

**File:** `Http20/02_Http20MultiplexingTests.cs`
**Fixture:** `KestrelH2Fixture`

**Routes covered:** `/hello`, `/ping`, `/h2/settings`, `/large/{kb}`, `/slow/{count}`

**Acceptance Criteria:**
- [ ] `INT-9113-5-MUX-001`: Send 3 concurrent GETs (`/hello`, `/ping`, `/h2/settings`) → all 3 return 200 with correct bodies
- [ ] `INT-9113-5-MUX-002`: Send 10 concurrent GETs to `/hello` → all 10 return 200
- [ ] `INT-9113-5-MUX-003`: Send 5 concurrent GETs to `/large/100` → all return 102400 bytes each
- [ ] `INT-9113-5-MUX-004`: Mix GET + POST concurrently → both complete correctly
- [ ] `INT-9113-5-MUX-005`: Send concurrent requests where one is slow (`/slow/100`) → fast requests complete before slow one
- [ ] `INT-9113-5-MUX-006`: Send 20 concurrent GETs → all succeed (tests max concurrent streams)
- [ ] `INT-9113-5-MUX-007`: Send 50 concurrent GETs → all succeed or graceful backpressure (tests beyond typical stream limit)
- [ ] `dotnet test --filter "FullyQualifiedName~Http20MultiplexingTests"` — all pass

---

### TASK-018: HTTP/2 HPACK Header Tests
**Description:** As a maintainer, I want HTTP/2 HPACK header compression and large header handling validated end-to-end.

**File:** `Http20/03_Http20HpackHeaderTests.cs`
**Fixture:** `KestrelH2Fixture`

**Routes covered:** `/h2/many-headers`, `/headers/echo`, `/h2/large-headers/{kb}`, `/headers/count`, `/auth`, `/multiheader`

**Acceptance Criteria:**
- [ ] `INT-9113-6.2-HDR-001`: GET `/h2/many-headers` → response has 20 custom X-Custom-NNN headers (HPACK compression works)
- [ ] `INT-9113-6.2-HDR-002`: GET `/headers/echo` with 10 custom X-* headers → all echoed back
- [ ] `INT-9113-6.2-HDR-003`: GET `/h2/large-headers/10` → response with 10KB+ header data spread across 10 headers
- [ ] `INT-9113-6.2-HDR-004`: GET `/h2/large-headers/50` → response with 50KB+ header data (tests CONTINUATION frames)
- [ ] `INT-9113-6.2-HDR-005`: GET `/auth` with Authorization header over H2 → 200 (sensitive header via HPACK NeverIndex)
- [ ] `INT-9113-6.2-HDR-006`: GET `/headers/count` with 100 custom headers → X-Header-Count: 100+ confirmed
- [ ] `INT-9113-6.2-HDR-007`: GET `/multiheader` over H2 → two X-Value headers correctly decoded
- [ ] `INT-9113-6.2-HDR-008`: Sequential requests reusing HPACK dynamic table → second request benefits from table (implicit — just verify correctness)
- [ ] `dotnet test --filter "FullyQualifiedName~Http20HpackHeaderTests"` — all pass

---

### TASK-019: HTTP/2 DATA Frame and Flow Control Tests
**Description:** As a maintainer, I want HTTP/2 DATA frame delivery and flow control window management validated with varying payload sizes.

**File:** `Http20/04_Http20DataFlowTests.cs`
**Fixture:** `KestrelH2Fixture`

**Routes covered:** `/large/{kb}`, `/h2/echo-binary`, `/h2/priority/{kb}`, `/echo`

**Acceptance Criteria:**
- [ ] `INT-9113-6.1-DAT-001`: GET `/large/1` → 1024 bytes received correctly
- [ ] `INT-9113-6.1-DAT-002`: GET `/large/100` → 102400 bytes received correctly
- [ ] `INT-9113-6.1-DAT-003`: GET `/large/1000` → 1024000 bytes (flow control window updates work)
- [ ] `INT-9113-6.1-DAT-004`: POST `/h2/echo-binary` with 10KB binary body → round-trip preserved
- [ ] `INT-9113-6.1-DAT-005`: POST `/h2/echo-binary` with 100KB binary body → round-trip preserved (multiple DATA frames)
- [ ] `INT-9113-6.1-DAT-006`: GET `/h2/priority/100` → 102400 bytes of 'P' received
- [ ] `INT-9113-6.1-DAT-007`: POST `/echo` with empty body over H2 → 200, empty response
- [ ] `INT-9113-6.1-DAT-008`: 5 concurrent GETs to `/large/100` → all 5 receive correct 102400 bytes (concurrent flow control)
- [ ] `dotnet test --filter "FullyQualifiedName~Http20DataFlowTests"` — all pass

---

### TASK-020: HTTP/2 Error Handling Tests
**Description:** As a maintainer, I want HTTP/2 error scenarios (stream abort, RST_STREAM, GOAWAY) validated end-to-end so that the client handles protocol-level errors gracefully.

**File:** `Http20/05_Http20ErrorHandlingTests.cs`
**Fixture:** `KestrelH2Fixture`

**Routes covered:** `/h2/abort`, `/h2/delay/{ms}`, `/status/{code}`

**Acceptance Criteria:**
- [ ] `INT-9113-6.4-ERR-001`: GET `/h2/abort` → client receives RST_STREAM or connection error, handles gracefully (no crash)
- [ ] `INT-9113-6.4-ERR-002`: GET `/h2/abort` then GET `/hello` → second request succeeds (recovery after stream error)
- [ ] `INT-9113-6.4-ERR-003`: GET `/status/500` over H2 → 500 returned (server error, not protocol error)
- [ ] `INT-9113-6.4-ERR-004`: GET `/h2/delay/100` over H2 → 200 "delayed" (delay doesn't trigger error)
- [ ] `INT-9113-6.4-ERR-005`: Concurrent GET `/h2/abort` + GET `/hello` → abort doesn't affect the other stream
- [ ] `INT-9113-6.4-ERR-006`: Multiple sequential aborts → client recovers each time
- [ ] `dotnet test --filter "FullyQualifiedName~Http20ErrorHandlingTests"` — all pass

---

### TASK-021: HTTP/2 Connection Management Tests
**Description:** As a maintainer, I want HTTP/2 connection-level features (settings, max concurrent streams, connection reuse) validated end-to-end.

**File:** `Http20/06_Http20ConnectionTests.cs`
**Fixture:** `KestrelH2Fixture`

**Routes covered:** `/h2/settings`, `/h2/settings/max-concurrent`, `/conn/default`, `/conn/close`, `/hello`, `/slow/{count}`

**Acceptance Criteria:**
- [ ] `INT-9113-6.5-CON-001`: GET `/h2/settings` → 200, confirms SETTINGS exchange completed
- [ ] `INT-9113-6.5-CON-002`: GET `/h2/settings/max-concurrent` → response includes X-Stream-Id header
- [ ] `INT-9113-5-CON-003`: 20 sequential GETs to `/hello` over H2 → all succeed on same connection
- [ ] `INT-9113-5-CON-004`: GET `/slow/200` over H2 → slow response (200ms) doesn't block connection for other requests
- [ ] `INT-9113-5-CON-005`: GET `/conn/close` over H2 → connection-level close handled gracefully
- [ ] `INT-9113-5-CON-006`: After connection close, next request → new connection established automatically
- [ ] `dotnet test --filter "FullyQualifiedName~Http20ConnectionTests"` — all pass

---

### TASK-022: HTTP/2 Cross-Cutting Feature Tests (Redirect, Cookie, Cache, Compression)
**Description:** As a maintainer, I want redirect, cookie, cache, and compression behavior validated over HTTP/2 so that business logic stages work correctly with the HTTP/2 engine.

**File:** `Http20/07_Http20CrossCuttingTests.cs`
**Fixture:** `KestrelH2Fixture`

**Routes covered:** `/redirect/*`, `/cookie/*`, `/cache/*`, `/compress/*`, `/h2/cookie`

**Acceptance Criteria:**
- [ ] `INT-9110-15.4-RDR-H2-001`: GET `/redirect/302/hello` over H2 → follows redirect, final 200
- [ ] `INT-9110-15.4-RDR-H2-002`: POST `/redirect/307` over H2 → method + body preserved
- [ ] `INT-9110-15.4-RDR-H2-003`: GET `/redirect/chain/3` over H2 → follows chain
- [ ] `INT-9110-15.4-RDR-H2-004`: GET `/redirect/loop` over H2 → loop detected
- [ ] `INT-6265-5.3-CKE-H2-001`: GET `/cookie/set/h2sess/val` then GET `/cookie/echo` over H2 → cookie sent
- [ ] `INT-6265-5.3-CKE-H2-002`: GET `/h2/cookie` then GET `/cookie/echo` over H2 → Set-Cookie from H2 route stored
- [ ] `INT-6265-5.3-CKE-H2-003`: GET `/cookie/set-multiple` then GET `/cookie/echo` over H2 → all 3 cookies present
- [ ] `INT-9111-4.2-CCH-H2-001`: GET `/cache/max-age/60` twice over H2 → second from cache
- [ ] `INT-9111-4.3-CCH-H2-002`: GET `/cache/etag/h2v1` twice over H2 → second sends If-None-Match
- [ ] `INT-9110-8.4-ENC-H2-001`: GET `/compress/gzip/10` over H2 → body decompressed correctly
- [ ] `INT-9110-8.4-ENC-H2-002`: GET `/compress/br/10` over H2 → brotli decompressed correctly
- [ ] `INT-9110-8.4-ENC-H2-003`: GET `/compress/negotiate` with `Accept-Encoding: gzip` over H2 → negotiated and decompressed
- [ ] `dotnet test --filter "FullyQualifiedName~Http20CrossCuttingTests"` — all pass

---

### TASK-023: HTTP/2 Retry Tests
**Description:** As a maintainer, I want retry behavior validated over HTTP/2 so that idempotent retries work correctly with stream multiplexing.

**File:** `Http20/08_Http20RetryTests.cs`
**Fixture:** `KestrelH2Fixture`

**Routes covered:** `/retry/503`, `/retry/succeed-after/{n}`, `/retry/non-idempotent-503`, `/retry/408`

**Acceptance Criteria:**
- [ ] `INT-9110-9.2-RTY-H2-001`: GET `/retry/503` over H2 → retried (idempotent)
- [ ] `INT-9110-9.2-RTY-H2-002`: GET `/retry/succeed-after/2?key=h2test1` over H2 → fails once, succeeds on retry
- [ ] `INT-9110-9.2-RTY-H2-003`: POST `/retry/non-idempotent-503` over H2 → NOT retried, 503 returned
- [ ] `INT-9110-9.2-RTY-H2-004`: PUT `/retry/503` over H2 → retried (PUT is idempotent)
- [ ] `INT-9110-9.2-RTY-H2-005`: Concurrent retryable + non-retryable over H2 → correct behavior for each
- [ ] `dotnet test --filter "FullyQualifiedName~Http20RetryTests"` — all pass

---

### Phase 4: HTTP/1.0 Tests

---

### TASK-024: HTTP/1.0 Basic Tests
**Description:** As a maintainer, I want HTTP/1.0 end-to-end tests so that the simplest protocol version is validated, specifically the connection-close-after-each-response behavior.

**File:** `Http10/01_Http10BasicTests.cs`
**Fixture:** `KestrelFixture`

**Routes covered:** `/hello`, `/ping`, `/echo`, `/status/{code}`, `/large/{kb}`, `/headers/echo`, `/multiheader`, `/empty-cl`, `/methods`, `/any`

**Acceptance Criteria:**
- [ ] `INT-1945-4-BAS-001`: GET `/hello` with HTTP/1.0 → 200, body = "Hello World"
- [ ] `INT-1945-5-BAS-002`: HEAD `/hello` with HTTP/1.0 → 200, no body
- [ ] `INT-1945-5-BAS-003`: POST `/echo` with HTTP/1.0 → 200, body echoed
- [ ] `INT-1945-6-BAS-004`: GET `/status/200` with HTTP/1.0 → 200
- [ ] `INT-1945-6-BAS-005`: GET `/status/404` with HTTP/1.0 → 404
- [ ] `INT-1945-6-BAS-006`: GET `/status/500` with HTTP/1.0 → 500
- [ ] `INT-1945-7-BAS-007`: GET `/headers/echo` with custom X-* headers → headers echoed
- [ ] `INT-1945-8-BAS-008`: GET `/large/10` with HTTP/1.0 → 10240 bytes received
- [ ] `INT-1945-10-BAS-009`: Two sequential GETs with HTTP/1.0 → both succeed (each on separate connection)
- [ ] `INT-1945-10-BAS-010`: GET `/close` with HTTP/1.0 → connection closed
- [ ] `INT-1945-7-BAS-011`: GET `/multiheader` with HTTP/1.0 → multi-value header parsed
- [ ] `INT-1945-8-BAS-012`: GET `/empty-cl` with HTTP/1.0 → empty body, Content-Length: 0
- [ ] `INT-1945-5-BAS-013`: GET `/ping` with HTTP/1.0 → 200, body = "pong"
- [ ] `INT-1945-5-BAS-014`: DELETE `/any` with HTTP/1.0 → 200
- [ ] All requests use `Version = HttpVersion.Version10`
- [ ] `dotnet test --filter "FullyQualifiedName~Http10BasicTests"` — all pass

---

### TASK-025: HTTP/1.0 Cross-Cutting Tests
**Description:** As a maintainer, I want redirect, cookie, cache, and compression validated over HTTP/1.0 so that business logic stages work correctly with the oldest supported protocol.

**File:** `Http10/02_Http10CrossCuttingTests.cs`
**Fixture:** `KestrelFixture`

**Acceptance Criteria:**
- [ ] `INT-1945-RDR-001`: GET `/redirect/302/hello` with HTTP/1.0 → follows redirect, final 200
- [ ] `INT-1945-RDR-002`: GET `/redirect/chain/3` with HTTP/1.0 → follows 3 hops
- [ ] `INT-1945-CKE-001`: GET `/cookie/set/v10sess/val` then GET `/cookie/echo` with HTTP/1.0 → cookie sent
- [ ] `INT-1945-CCH-001`: GET `/cache/max-age/60` twice with HTTP/1.0 → second from cache
- [ ] `INT-1945-ENC-001`: GET `/compress/gzip/10` with HTTP/1.0 → decompressed correctly
- [ ] `INT-1945-RTY-001`: GET `/retry/succeed-after/2?key=v10test` with HTTP/1.0 → retried, succeeds
- [ ] All requests use `Version = HttpVersion.Version10`
- [ ] `dotnet test --filter "FullyQualifiedName~Http10CrossCuttingTests"` — all pass

---

### Phase 5: TLS Tests

---

### TASK-026: TLS Basic Tests
**Description:** As a maintainer, I want HTTPS end-to-end tests so that TLS handshake, encrypted transport, and self-signed cert handling are validated.

**File:** `Tls/01_TlsBasicTests.cs`
**Fixture:** `KestrelTlsFixture`

**Routes covered:** `/hello`, `/echo`, `/large/{kb}`, `/status/{code}`, `/chunked/{kb}`, `/headers/echo`, `/compress/gzip/{kb}`, `/ping`, `/methods`, `/any`

**Acceptance Criteria:**
- [ ] `INT-TLS-BAS-001`: GET `/hello` over HTTPS → 200, "Hello World"
- [ ] `INT-TLS-BAS-002`: HEAD `/hello` over HTTPS → 200, no body
- [ ] `INT-TLS-BAS-003`: POST `/echo` over HTTPS with body → 200, body echoed
- [ ] `INT-TLS-BAS-004`: GET `/large/100` over HTTPS → 102400 bytes
- [ ] `INT-TLS-BAS-005`: GET `/status/404` over HTTPS → 404
- [ ] `INT-TLS-BAS-006`: GET `/chunked/10` over HTTPS → chunked response over TLS
- [ ] `INT-TLS-BAS-007`: GET `/headers/echo` with custom headers over HTTPS → headers echoed
- [ ] `INT-TLS-BAS-008`: GET `/compress/gzip/10` over HTTPS → decompressed over TLS
- [ ] `INT-TLS-BAS-009`: GET `/ping` over HTTPS → 200, "pong"
- [ ] `INT-TLS-BAS-010`: PUT `/echo` over HTTPS with body → 200, body echoed
- [ ] Client configured with `ServerCertificateCustomValidationCallback` to trust self-signed cert
- [ ] `dotnet test --filter "FullyQualifiedName~TlsBasicTests"` — all pass

---

### TASK-027: TLS Security Tests (Redirects + Cookies + Downgrade Protection)
**Description:** As a maintainer, I want TLS-specific security behaviors validated: HTTPS→HTTP downgrade protection and Secure cookie enforcement.

**File:** `Tls/02_TlsSecurityTests.cs`
**Fixtures:** `KestrelTlsFixture` + `KestrelFixture` (both needed for cross-scheme tests)

**Routes covered:** `/redirect/cross-scheme`, `/cookie/set-secure/{name}/{value}`, `/cookie/echo`, `/auth`, `/cache/etag/{id}`, `/redirect/302/hello`

**Acceptance Criteria:**
- [ ] `INT-TLS-SEC-001`: GET `/redirect/cross-scheme` over HTTPS → HTTPS→HTTP downgrade blocked (RedirectException or refused)
- [ ] `INT-TLS-SEC-002`: Set Secure cookie via `/cookie/set-secure/token/secret` over HTTPS → GET `/cookie/echo` over HTTPS → cookie present
- [ ] `INT-TLS-SEC-003`: Set Secure cookie over HTTPS → GET `/cookie/echo` over HTTP (different fixture) → cookie NOT sent
- [ ] `INT-TLS-SEC-004`: Redirect from HTTPS `/redirect/302/hello` → follows redirect, final 200 over HTTPS
- [ ] `INT-TLS-SEC-005`: GET `/auth` with Authorization header over HTTPS → 200
- [ ] `INT-TLS-SEC-006`: ETag cache over HTTPS → GET `/cache/etag/tlsv1` twice → 304 revalidation over TLS
- [ ] `INT-TLS-SEC-007`: GET `/cache/no-store` over HTTPS → response not cached
- [ ] `dotnet test --filter "FullyQualifiedName~TlsSecurityTests"` — all pass

---

### TASK-028: TLS Connection Reuse Tests
**Description:** As a maintainer, I want TLS connection reuse validated so that the TLS session and underlying TCP connection are properly pooled.

**File:** `Tls/03_TlsConnectionTests.cs`
**Fixture:** `KestrelTlsFixture`

**Routes covered:** `/conn/keep-alive`, `/conn/close`, `/conn/default`, `/hello`, `/close`

**Acceptance Criteria:**
- [ ] `INT-TLS-CON-001`: Two sequential GETs to `/hello` over HTTPS → both succeed (TLS connection reused)
- [ ] `INT-TLS-CON-002`: GET `/conn/close` over HTTPS → connection closed, next request on new TLS connection
- [ ] `INT-TLS-CON-003`: 5 sequential GETs over HTTPS → all succeed (TLS keep-alive)
- [ ] `INT-TLS-CON-004`: GET `/conn/keep-alive` over HTTPS → Connection: Keep-Alive header present
- [ ] `INT-TLS-CON-005`: GET `/large/100` over HTTPS → 102400 bytes fully received over TLS
- [ ] `dotnet test --filter "FullyQualifiedName~TlsConnectionTests"` — all pass

---

### Phase 6: Full Pipeline / Cross-Cutting Tests

---

### TASK-029: Full Pipeline Integration Tests
**Description:** As a maintainer, I want tests that exercise the complete pipeline with multiple features combined — cookie + redirect + cache + encoding in a single test — so that stage interaction is proven.

**File:** `Pipeline/01_FullPipelineTests.cs`
**Fixture:** `KestrelFixture`

**Acceptance Criteria:**
- [ ] `INT-PIP-001`: Cookie set → redirect → cookie echoed at final destination (cookie survives redirect)
- [ ] `INT-PIP-002`: Compressed response → cached → second request decompressed from cache
- [ ] `INT-PIP-003`: GET cached resource → redirect to different URL → redirect followed, result cached separately
- [ ] `INT-PIP-004`: Retry on 503 → succeed-after/2 → response then cached → third request from cache
- [ ] `INT-PIP-005`: Large body (500KB) + gzip + chunked transfer → fully decompressed end-to-end
- [ ] `INT-PIP-006`: 10 sequential different requests → all return correct responses (pipeline doesn't corrupt state)
- [ ] `INT-PIP-007`: Mixed HTTP versions: HTTP/1.1 request then HTTP/1.0 request on same client → both correct
- [ ] `INT-PIP-008`: Cookie + redirect + cache in one flow: set cookie → redirect to cached URL → cache hit includes cookie
- [ ] `INT-PIP-009`: Range request on cached resource → partial content served correctly
- [ ] `dotnet test --filter "FullyQualifiedName~FullPipelineTests"` — all pass

---

### TASK-030: Feedback Loop Tests
**Description:** As a maintainer, I want redirect and retry feedback loops validated so that the Akka.Streams feedback wiring (MergePreferred → Buffer → back to pipeline) is proven to work end-to-end.

**File:** `Pipeline/02_FeedbackLoopTests.cs`
**Fixture:** `KestrelFixture`

**Acceptance Criteria:**
- [ ] `INT-PIP-FBK-001`: Redirect chain of 5 → request re-enters pipeline 5 times, final response correct
- [ ] `INT-PIP-FBK-002`: Retry succeed-after/3 → request re-enters pipeline 2 times before success
- [ ] `INT-PIP-FBK-003`: Redirect to URL that returns 503 → redirect followed, then retried → final success
- [ ] `INT-PIP-FBK-004`: Redirect loop → loop detected before infinite recursion
- [ ] `INT-PIP-FBK-005`: Cache hit on redirected URL → cache bypass skips engine, response returned directly
- [ ] `INT-PIP-FBK-006`: Cookie set during redirect chain → cookie accumulates across hops
- [ ] `dotnet test --filter "FullyQualifiedName~FeedbackLoopTests"` — all pass

---

### TASK-031: Concurrency / Stress Tests
**Description:** As a maintainer, I want concurrent client usage validated so that the actor pool, connection management, and pipeline handle parallel requests correctly.

**File:** `Pipeline/03_ConcurrencyTests.cs`
**Fixture:** `KestrelFixture` + `KestrelH2Fixture`

**Acceptance Criteria:**
- [ ] `INT-PIP-CONC-001`: 20 concurrent HTTP/1.1 GETs to `/hello` → all 200 (connection pool scales)
- [ ] `INT-PIP-CONC-002`: 20 concurrent HTTP/2 GETs to `/hello` → all 200 (multiplexing works)
- [ ] `INT-PIP-CONC-003`: 10 concurrent requests to different endpoints → all return correct body
- [ ] `INT-PIP-CONC-004`: 5 concurrent clients each sending 5 requests → all 25 responses correct
- [ ] `INT-PIP-CONC-005`: Concurrent mix of fast (`/hello`) and slow (`/slow/100`) → fast complete first
- [ ] `INT-PIP-CONC-006`: Concurrent requests where some redirect and some retry → all resolve correctly
- [ ] `INT-PIP-CONC-007`: 100 sequential requests (rapid fire) to `/hello` → all succeed (connection pool doesn't exhaust)
- [ ] `dotnet test --filter "FullyQualifiedName~ConcurrencyTests"` — all pass

---

### TASK-032: Add Missing Routes for Edge Cases
**Description:** As a test author, I want additional server routes that expose edge-case behaviors not currently covered by the existing fixtures.

**Acceptance Criteria:**
- [ ] `KestrelFixture` — add `POST /redirect/301` → 301 with Location (tests POST→GET rewrite on 301)
- [ ] `KestrelFixture` — add `GET /conn/max-requests/{n}` → closes connection after N requests
- [ ] `KestrelH2Fixture` — add `GET /h2/goaway` → sends GOAWAY frame before response
- [ ] `KestrelH2Fixture` — add `GET /h2/rst-stream` → resets stream with REFUSED_STREAM
- [ ] `KestrelFixture` — add `GET /cache/age/{seconds}` → response with Age header pre-set
- [ ] `KestrelFixture` — add `GET /edge/trailers` → response with trailer headers (non-chunked, HTTP/1.1 TE: trailers)
- [ ] `KestrelH2Fixture` — add `GET /h2/push-promise` → sends PUSH_PROMISE frame (if Kestrel supports)
- [ ] `KestrelFixture` — add `GET /edge/100-continue` → requires Expect: 100-continue handling
- [ ] All existing tests still pass after route additions
- [ ] `dotnet build` succeeds

---

### TASK-033: HTTP/2 GOAWAY and RST_STREAM Tests (requires TASK-032 routes)
**Description:** As a maintainer, I want HTTP/2 GOAWAY and RST_STREAM handling validated end-to-end so that the client correctly handles server-initiated connection and stream closures.

**File:** `Http20/09_Http20GoAwayRstTests.cs`
**Fixture:** `KestrelH2Fixture`

**Routes covered:** `/h2/goaway`, `/h2/rst-stream`, `/hello` (from TASK-032)

**Acceptance Criteria:**
- [ ] `INT-9113-6.8-GAW-001`: GET `/h2/goaway` → client handles GOAWAY gracefully (no crash)
- [ ] `INT-9113-6.8-GAW-002`: After GOAWAY, next request → establishes new connection
- [ ] `INT-9113-6.8-GAW-003`: Concurrent requests during GOAWAY → in-flight streams complete, new streams on new connection
- [ ] `INT-9113-6.4-RST-001`: GET `/h2/rst-stream` → client handles RST_STREAM (REFUSED_STREAM) gracefully
- [ ] `INT-9113-6.4-RST-002`: After RST_STREAM, next request on same connection → succeeds (stream-level, not connection-level)
- [ ] `INT-9113-6.4-RST-003`: GET `/h2/rst-stream` then GET `/hello` → hello succeeds
- [ ] `dotnet test --filter "FullyQualifiedName~Http20GoAwayRstTests"` — all pass

---

## Functional Requirements

- FR-1: Every integration test must create a fresh `ITurboHttpClient` via `TestKit.CreateClient()` — no shared client state between tests
- FR-2: Each test class uses `IClassFixture<T>` for its Kestrel fixture — fixture starts once per test class, not per test
- FR-3: All tests use `DisplayName` with `INT-{RFC}-{section}-{CAT}-{NNN}` convention
- FR-4: Tests must have `Timeout = 30_000` (30 seconds) to catch hangs
- FR-5: No `Thread.Sleep` or `Task.Delay` for timing — use retry assertions with polling where timing matters (cache expiry)
- FR-6: Cookie and cache tests that need isolation must use separate `CookieJar`/`HttpCacheStore` instances (fresh per client)
- FR-7: TLS tests must configure `ServerCertificateCustomValidationCallback` to accept the self-signed cert from `KestrelTlsFixture`
- FR-8: HTTP/1.0 tests must set `Version = HttpVersion.Version10` on requests; HTTP/2 tests must set `Version = HttpVersion.Version20`
- FR-9: Test namespaces match folder structure: `TurboHttp.IntegrationTests.Http10`, `.Http11`, `.Http20`, `.Tls`, `.Pipeline`
- FR-10: All test classes are `public sealed class` (xUnit requirement)
- FR-11: Every registered Kestrel route must be covered by at least one integration test
- FR-12: All retry tests must use unique `?key=` query parameters to avoid ConcurrentDictionary state collision

## Non-Goals

- No load testing or benchmarking — this is functional correctness only
- No testing against external HTTP servers (only local Kestrel fixtures)
- No HTTP/2 over TLS (h2) — only h2c (cleartext) for HTTP/2 fixture (TLS fixture uses HTTP/1.1)
- No WebSocket upgrade testing (101 Switching Protocols route exists but WebSocket is out of scope)
- No testing of `ITurboHttpClient` internals — only public API (`SendAsync`)
- No re-testing of encoding/decoding correctness (covered by unit tests)
- No fuzz testing or malformed input testing (covered by unit tests)
- No performance assertions (response time, throughput)

## Technical Considerations

- **Fixture lifetime**: Kestrel fixtures implement `IAsyncLifetime`. Using `IClassFixture<T>` means the server starts once per test class and is shared across all tests in that class. This is efficient but means connection pool state could leak — mitigated by fresh `ActorSystem` per test via `TestKit`.
- **Port allocation**: All fixtures use `PortFinder.FindFreeLocalPort()` — port conflicts are already handled.
- **HTTP/2 h2c**: `KestrelH2Fixture` is configured for HTTP/2 only (no HTTP/1.1 fallback). The client must send the HTTP/2 connection preface directly — no upgrade negotiation. Header limits: MaxRequestHeaderCount=2000, MaxRequestHeadersTotalSize=512KB.
- **Self-signed cert**: `KestrelTlsFixture` generates an in-memory RSA 2048-bit self-signed certificate. Tests must configure the client to trust it.
- **Timing tests**: Cache freshness and retry-after tests should use short durations (1–2 seconds) and ±500ms tolerance. Avoid `Thread.Sleep` — use polling assertions.
- **Parallelism**: xunit.runner.json has `parallelizeTestCollections: false` — tests run sequentially within the integration test project. This is intentional to avoid port/resource conflicts.
- **Retry route state**: `/retry/succeed-after/{n}` uses a `ConcurrentDictionary` keyed by a query parameter. Each test must use a unique key to avoid state collision across parallel tests.
- **CI considerations**: Integration tests will run in the GitHub Actions pipeline. Kestrel binds to localhost — no firewall issues. Tests should pass on both Windows and Linux runners.
- **Route coverage matrix**: Every route listed in `Routes.cs`, `KestrelFixture.cs`, `KestrelH2Fixture.cs`, and `KestrelTlsFixture.cs` should be mapped to at least one TASK. The "Routes covered" line in each TASK documents this mapping.

## Success Metrics

- All ~300 integration tests pass on both local dev (Windows) and CI (Linux/Ubuntu)
- `dotnet test --filter "FullyQualifiedName~IntegrationTests"` completes in < 90 seconds
- Zero flaky tests (timing-dependent tests use polling, not fixed delays)
- RFC_COVERAGE.md updated with integration test counts per RFC
- 100% of registered Kestrel routes covered by at least one test
- Every critical path has integration coverage:
  1. Connection pooling and reuse (TASK-004, TASK-028, TASK-031)
  2. Stream multiplexing (TASK-017, TASK-019)
  3. Cookie + redirect interaction (TASK-007, TASK-029, TASK-030)
  4. Cache freshness with real timing (TASK-008)
  5. TLS + Secure cookie enforcement (TASK-027)
  6. Retry with stateful server (TASK-006, TASK-023)
  7. HTTP/2 error recovery (TASK-020, TASK-033)
  8. Range requests and partial content (TASK-011)
  9. Content negotiation and Vary (TASK-010)
  10. Streaming/slow responses (TASK-013)

## Open Questions

1. **`CreateClient` implementation** — Does `ITurboHttpClient` already support being constructed outside the DI container, or does the test helper need to wire up the full Akka.Streams materializer manually?
2. **HTTP/1.0 on Kestrel** — Kestrel may respond with HTTP/1.1 even when the client sends HTTP/1.0. Need to verify whether `KestrelFixture` honors the client's protocol version.
3. **Max redirect limit** — What is TurboHttp's configured maximum redirect count? Tests for `/redirect/chain/10` need to know the limit.
4. **Retry policy defaults** — What are TurboHttp's default retry settings (max attempts, delay)? Tests like `succeed-after/3` need the retry policy to allow at least 3 attempts.
5. **Cross-fixture tests** — TASK-027 needs both `KestrelTlsFixture` and `KestrelFixture` in the same test class. xUnit supports multiple `IClassFixture<T>` interfaces. Verify this works with the `TestKit` base class.
6. **GOAWAY/RST_STREAM in Kestrel** — Can Kestrel programmatically send GOAWAY/RST_STREAM frames, or do we need custom middleware? TASK-032 depends on this.
7. **Expect: 100-continue** — Does Kestrel handle 100-continue natively, or does the route need explicit handling?
