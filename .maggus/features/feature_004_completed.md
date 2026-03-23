# Feature 004: Integration Tests — Caching (RFC 9111)

## Introduction

Add end-to-end integration tests for HTTP caching. Validates that TurboHttp's CacheBidiStage correctly caches, revalidates, and respects cache control directives over real connections.

### Architecture Context

- **Components involved:** CacheBidiStage (Streams/Features), HttpCacheStore, CacheFreshnessEvaluator, CacheValidationRequestBuilder (Protocol/RFC9111), KestrelFixture + KestrelH2Fixture
- **Existing infrastructure:** 10 cache routes in Routes.RegisterCacheRoutes()

## Mandatory Testing Policy

> **Tests are required and must never be skipped, disabled, or marked as `[Skip]`.** Every acceptance criterion that specifies "All tests pass" is a hard gate — the feature is not complete until every test passes. No `[Fact(Skip = "...")]`, no `#if` guards, no `Assert.Skip()`. If a test fails, fix the code or the test — never suppress it.

## Goals

- Verify cache storage and retrieval (max-age, Expires)
- Verify conditional revalidation (ETag/If-None-Match, Last-Modified/If-Modified-Since)
- Verify cache bypass directives (no-cache, no-store)
- Verify Vary header support
- Verify must-revalidate behavior

## Tasks

### TASK-004-001: Cache Integration Tests — HTTP/1.1
**Description:** As a library consumer, I want HTTP caching to work transparently so that repeated requests are served from cache when appropriate.

**Token Estimate:** ~45k tokens
**Predecessors:** none
**Successors:** TASK-004-003
**Parallel:** yes — can run alongside TASK-004-002

**Acceptance Criteria:**
- [x] Test file `CacheIntegrationTests.cs` in `src/TurboHttp.IntegrationTests/`
- [x] Collection: `"Http1Integration"`, uses `KestrelFixture`
- [x] Tests cover all cache routes:
  - `GET /cache/max-age/{seconds}` — first request stores, second serves from cache (same body timestamp)
  - `GET /cache/no-cache` — always revalidates with server
  - `GET /cache/no-store` — never cached (different body timestamp on each request)
  - `GET /cache/etag/{id}` — second request sends If-None-Match → 304
  - `GET /cache/last-modified/{id}` — second request sends If-Modified-Since → 304
  - `GET /cache/vary/{header}` — different header values produce different cache entries
  - `GET /cache/must-revalidate` — max-age=0 forces revalidation on every request
  - `GET /cache/s-maxage/{seconds}` — shared cache max-age
  - `GET /cache/expires` — Expires header (1 hour from now)
  - `GET /cache/private` — Cache-Control: private
- [x] Cache hit verified by comparing response bodies (timestamp-based routes)
- [x] All tests pass

### TASK-004-002: Cache Integration Tests — HTTP/2
**Description:** As a library consumer, I want caching to work identically over HTTP/2.

**Token Estimate:** ~35k tokens
**Predecessors:** none
**Successors:** TASK-004-003
**Parallel:** yes — can run alongside TASK-004-001

**Acceptance Criteria:**
- [x] Test file `CacheH2IntegrationTests.cs` in `src/TurboHttp.IntegrationTests/`
- [x] Collection: `"Http2Integration"`, uses `KestrelH2Fixture`
- [x] Covers same cache routes over h2c
- [x] All tests pass

### TASK-004-003: Verify Full Cache Suite
**Description:** Run the complete integration test suite to confirm no regressions.

**Token Estimate:** ~5k tokens
**Predecessors:** TASK-004-001, TASK-004-002
**Successors:** none
**Parallel:** no

**Acceptance Criteria:**
- [x] `dotnet test src/TurboHttp.IntegrationTests/` passes with 0 failures

## Task Dependency Graph

```
TASK-004-001 ──→ TASK-004-003
TASK-004-002 ──┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-004-001 | ~45k | none | yes (with 002) | — |
| TASK-004-002 | ~35k | none | yes (with 001) | — |
| TASK-004-003 | ~5k | 001, 002 | no | haiku |

**Total estimated tokens:** ~85k

## Functional Requirements

- FR-1: Response with `Cache-Control: max-age=N` must be served from cache within freshness lifetime
- FR-2: Response with `no-store` must never be cached
- FR-3: ETag revalidation must send If-None-Match and handle 304 correctly
- FR-4: Last-Modified revalidation must send If-Modified-Since and handle 304
- FR-5: Vary header must produce separate cache entries per varying header value
- FR-6: `must-revalidate` with `max-age=0` must revalidate on every request

## Non-Goals

- No cache eviction testing (LRU behavior is unit-tested in HttpCacheStore)
- No concurrent cache access testing
- No cache size limit configuration testing

## Technical Considerations

- Cache routes return ISO timestamps — comparing two responses' bodies proves cache hit (same timestamp) vs miss (different timestamp)
- Tests must account for timing: max-age tests should use large values (e.g., 3600) to avoid expiry during test
- Each test class gets its own client → separate cache instance → no cross-test contamination

## Success Metrics

- All 20+ cache integration tests pass consistently
- Cache hit tests prove the same body is returned without server roundtrip
