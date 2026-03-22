# Feature 005: Integration Tests — Cookies, Caching & Content Encoding

## Introduction

Building on Feature 002 infrastructure, this feature tests three middleware features that are enabled via the client builder: cookie persistence (RFC 6265), HTTP caching (RFC 9111), and automatic content decompression (RFC 9110 §8.4). These features are implemented as BidiStages in the pipeline and activated via `.WithCookies()`, `.WithCache()`, and `.WithDecompression()`.

### Architecture Context

- **Components involved:**
  - `CookieBidiStage` — RFC 6265 §5.3–5.4 cookie injection/storage
  - `CookieJar` — domain/path matching, Secure/HttpOnly/SameSite, Max-Age/Expires
  - `CacheBidiStage` — RFC 9111 cache lookup/storage with short-circuit on hits
  - `HttpCacheStore` — thread-safe in-memory LRU cache with Vary support
  - `CacheFreshnessEvaluator` — freshness lifetime, current age
  - `CacheValidationRequestBuilder` — conditional requests (If-None-Match, If-Modified-Since)
  - `DecompressionBidiStage` — gzip/deflate/brotli automatic decompression
  - `ContentEncodingDecoder` — handles stacked encodings
  - Pipeline stacking: Handler → Redirect → **Cookie** → Retry → Expect100 → **Cache** → **Decomp** → Engine
- **Existing routes used:** `/cookie/*`, `/cache/*`, `/compress/*`, `/edge/unknown-encoding`
- **Depends on:** Feature 002 (ClientHelper, Collections)

## Goals

- Verify cookie set/get roundtrip across multiple requests
- Test cookie attributes (Secure, HttpOnly, SameSite, Domain, Path, Max-Age)
- Verify cookie persistence across redirects
- Test Cache-Control directives (max-age, no-cache, no-store, must-revalidate, s-maxage)
- Verify ETag/If-None-Match and Last-Modified/If-Modified-Since validation (304 handling)
- Test Vary header cache splitting
- Verify automatic gzip/deflate/brotli decompression
- Test unknown encoding passthrough

## Tasks

### TASK-005-001: Cookie Tests
**Description:** As a developer, I want cookie persistence tests for HTTP/1.1, so that RFC 6265 cookie set/get, attributes, expiration, and redirect persistence are verified end-to-end.

**Token Estimate:** ~70k tokens
**Predecessors:** Feature 002 (TASK-002-001)
**Successors:** none
**Parallel:** yes — can run alongside TASK-005-002 and TASK-005-003

**Acceptance Criteria:**
- [ ] `Http1CookieTests.cs` with `[Collection("Http1Integration")]` (~16 tests):
  - GET /cookie/set/name/value → next GET /cookie/echo includes cookie in JSON response
  - GET /cookie/set-multiple → 3 cookies (alpha, beta, gamma) all persist
  - GET /cookie/set-secure/s/v → Secure flag set (only sent over HTTPS, verified via /cookie/echo)
  - GET /cookie/set-httponly/h/v → HttpOnly flag set, cookie still sent in Cookie header
  - `[Theory]` GET /cookie/set-samesite/n/v/{policy} for Strict, Lax, None
  - GET /cookie/set-expires/n/v/1 → wait 2s → GET /cookie/echo → cookie absent (expired)
  - GET /cookie/set-path/n/v/api → GET /api/... includes cookie, GET /other/... does not
  - GET /cookie/set-domain/n/v/localhost → domain matching applied
  - GET /cookie/delete/name → next GET /cookie/echo → cookie absent
  - GET /cookie/set-and-redirect → 302 to /cookie/echo → cookie survives redirect
  - Multiple cookies set in sequence → all present in subsequent request
- [ ] Client configured with `.WithCookies()` for all cookie tests
- [ ] All tests green: `dotnet test src/TurboHttp.IntegrationTests/ --filter "CookieTests"`

### TASK-005-002: Cache Tests
**Description:** As a developer, I want HTTP caching tests for HTTP/1.1, so that Cache-Control directives, ETag/Last-Modified validation, and Vary header behavior are verified end-to-end.

**Token Estimate:** ~80k tokens
**Predecessors:** Feature 002 (TASK-002-001)
**Successors:** none
**Parallel:** yes — can run alongside TASK-005-001 and TASK-005-003

**Acceptance Criteria:**
- [ ] `Http1CacheTests.cs` with `[Collection("Http1Integration")]` (~16 tests):
  - GET /cache/max-age/3600 → second identical request returns cached response (no network)
  - GET /cache/no-cache → revalidation required on every request
  - GET /cache/no-store → response never cached, second request hits server
  - GET /cache/must-revalidate (max-age=0) → always revalidates with server
  - GET /cache/etag/1 → ETag set; second request sends If-None-Match → 304 Not Modified
  - GET /cache/etag/1 with mismatched If-None-Match → 200 with fresh body
  - GET /cache/last-modified/1 → Last-Modified set; second request sends If-Modified-Since → 304
  - GET /cache/vary/Accept with Accept: application/json → cached per Accept value
  - GET /cache/vary/Accept with different Accept → cache miss, separate entry
  - GET /cache/s-maxage/60 → s-maxage used for shared cache freshness
  - GET /cache/expires → absolute expiration time respected
  - GET /cache/private → marked private, still cacheable for this client
  - Two requests to same cacheable URL → second is faster (cache hit)
- [ ] `Http2CacheTests.cs` with `[Collection("Http2Integration")]` (~6 tests):
  - Cache behavior over HTTP/2 connection
  - Cache hit avoids network round-trip (same multiplexed connection)
  - ETag validation over HTTP/2
- [ ] Client configured with `.WithCache(new CachePolicy())` for all cache tests
- [ ] All tests green: `dotnet test src/TurboHttp.IntegrationTests/ --filter "CacheTests"`

### TASK-005-003: Content Encoding / Decompression Tests
**Description:** As a developer, I want decompression tests for HTTP/1.1 and HTTP/2, so that automatic gzip/deflate/brotli decompression and unknown encoding passthrough are verified.

**Token Estimate:** ~60k tokens
**Predecessors:** Feature 002 (TASK-002-001)
**Successors:** none
**Parallel:** yes — can run alongside TASK-005-001 and TASK-005-002

**Acceptance Criteria:**
- [ ] `Http1CompressionTests.cs` with `[Collection("Http1Integration")]` (~12 tests):
  - GET /compress/gzip/10 → auto-decompressed, body is 10240 bytes of 'A'
  - GET /compress/deflate/10 → auto-decompressed
  - GET /compress/br/10 → auto-decompressed (brotli)
  - GET /compress/identity/10 → no encoding, raw body
  - GET /compress/negotiate with Accept-Encoding: br → server sends brotli, auto-decompressed
  - GET /compress/negotiate without Accept-Encoding → identity response
  - GET /edge/unknown-encoding → Content-Encoding: x-custom, body preserved as-is
  - `[Theory]` GET /compress/gzip/{kb} for 10, 100, 500 → large payloads decompressed correctly
  - Decompressed body matches expected size and content
- [ ] `Http2CompressionTests.cs` with `[Collection("Http2Integration")]` (~6 tests):
  - GET /compress/gzip/10 over HTTP/2 → decompressed
  - GET /compress/br/10 over HTTP/2 → decompressed
  - Large compressed payload over HTTP/2
- [ ] `Http3CompressionTests.cs` with `[Collection("Http3Integration")]` + `[Trait("Category", "Http3")]` (~4 tests):
  - GET /compress/gzip/10 over HTTP/3 → decompressed
  - GET /compress/br/10 over HTTP/3 → decompressed
- [ ] Client configured with `.WithDecompression(true)` (default) for compression tests
- [ ] All tests green: `dotnet test src/TurboHttp.IntegrationTests/ --filter "CompressionTests"`

## Task Dependency Graph

```
              ┌──→ TASK-005-001
Feature 002 ──┼──→ TASK-005-002
              └──→ TASK-005-003
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-005-001 | ~70k | Feature 002 | yes (with 002, 003) | — |
| TASK-005-002 | ~80k | Feature 002 | yes (with 001, 003) | — |
| TASK-005-003 | ~60k | Feature 002 | yes (with 001, 002) | — |

**Total estimated tokens:** ~210k

## Functional Requirements

- FR-1: Cookies set via Set-Cookie must be sent in subsequent requests to matching domain/path
- FR-2: Cookie expiration (Max-Age=0 or expired) must remove cookie from jar
- FR-3: Cookie SameSite policy must be enforced for cross-site requests
- FR-4: Cached responses must be returned without network round-trip when fresh
- FR-5: Stale cached responses must trigger revalidation (If-None-Match or If-Modified-Since)
- FR-6: 304 Not Modified must merge cached response with new headers
- FR-7: Vary header must create separate cache entries per header value
- FR-8: Automatic decompression must be transparent (Content-Encoding removed from final response)
- FR-9: Unknown Content-Encoding must preserve raw body without error

## Non-Goals

- No Secure cookie enforcement over HTTP (would need cross-fixture test with TLS)
- No cache size limits or eviction testing (internal implementation detail)
- No stacked content encodings (e.g., gzip+br)
- No request body compression

## Technical Considerations

- Cookie tests need a fresh `CookieJar` per test to avoid cross-test state leakage
- Cache tests need a fresh `HttpCacheStore` per test for isolation
- `/cache/etag/{id}` returns stable ETag per ID — use different IDs per test for isolation
- `/cache/last-modified/{id}` returns fixed date based on hash — deterministic
- Decompression happens in `DecompressionBidiStage` — verify body length matches uncompressed size
- `/compress/negotiate` requires Accept-Encoding header from client

## Success Metrics

- All 60 tests green
- Cache hit tests measurably faster than cache miss (at least 2x)
- Cookie state properly isolated between tests (no leakage)
- No flaky timing-dependent tests

## Open Questions

_None — all questions resolved._
