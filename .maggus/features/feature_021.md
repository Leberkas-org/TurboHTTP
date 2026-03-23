# Feature 021: Uniform Integration Test Coverage Across HTTP Versions

## Introduction

TurboHttp has 139 integration tests but coverage is heavily skewed toward HTTP/1.1 and HTTP/2. HTTP/1.0 has only 1 smoke test, TLS has 9 basic tests, and several feature categories (ExpectContinue, Connection for HTTP/2, Concurrency) have zero integration coverage. This feature brings all HTTP versions to parity by creating matching test classes for every feature category across HTTP/1.0, HTTP/1.1 (existing), HTTP/2, and HTTPS/TLS.

### Architecture Context

- **Components involved:** `src/TurboHttp.IntegrationTests/` — test files, `Shared/Routes.cs` (server routes), `Shared/ClientHelper.cs` (client factory)
- **Pattern:** Separate test classes per version (e.g., `CompressionIntegrationTests` for HTTP/1.1, `CompressionH2IntegrationTests` for HTTP/2, `CompressionH10IntegrationTests` for HTTP/1.0, `CompressionTlsIntegrationTests` for TLS)
- **Fixtures:** `KestrelFixture` (HTTP/1.0 + 1.1), `KestrelH2Fixture` (HTTP/2), `KestrelTlsFixture` (HTTPS/TLS)
- **Collections:** `Http1Integration`, `Http2Integration`, `TlsIntegration`
- **HTTP/3 is explicitly excluded** — `KestrelH3Fixture` exists but QUIC is not stable

## Goals

- Achieve uniform feature test coverage across HTTP/1.0, HTTP/1.1, HTTP/2, and HTTPS/TLS
- Fill the HTTP/1.0 gap: from 1 test to ~67 tests (matching HTTP/1.1 feature parity)
- Fill the HTTP/2 gap: add Connection/Multiplexing tests (currently missing)
- Fill the TLS gap: from 9 mixed tests to ~67 dedicated feature tests
- Add ExpectContinue integration tests (currently zero across all versions)
- Add Edge Case tests covering chunked trailers, range requests, large bodies per version
- Add Concurrency/Stress tests covering parallel requests and multiplexing per version
- All tests follow existing `DisplayName` conventions and `ClientHelper.CreateClient()` pattern

## Current Coverage Matrix

| Feature | HTTP/1.0 | HTTP/1.1 | HTTP/2 | TLS | Tests/Version |
|---------|----------|----------|--------|-----|---------------|
| Compression | ❌ | 7 ✅ | 7 ✅ | ❌ | 7 |
| Cookie | ❌ | 11 ✅ | 11 ✅ | ❌ | 11 |
| Redirect | ❌ | 14 ✅ | 14 ✅ | ❌ | 14 |
| Retry | ❌ | 9 ✅ | 9 ✅ | ❌ | 9 |
| Cache | ❌ | 11 ✅ | 11 ✅ | ❌ | 11 |
| Connection | ❌ | 5 ✅ | ❌ | ❌ | 3-5 |
| ErrorHandling | ❌ | 10 ✅ | 8 ✅ | ❌ | 8-10 |
| ExpectContinue | ❌ | ❌ | ❌ | ❌ | 3 |
| Edge Cases | ❌ | ❌ | ❌ | ❌ | 5-8 |
| Concurrency | ❌ | ❌ | ❌ | ❌ | 3-5 |

## Target Coverage Matrix

| Feature | HTTP/1.0 | HTTP/1.1 | HTTP/2 | TLS |
|---------|----------|----------|--------|-----|
| Compression | NEW | ✅ | ✅ | NEW |
| Cookie | NEW | ✅ | ✅ | NEW |
| Redirect | NEW | ✅ | ✅ | NEW |
| Retry | NEW | ✅ | ✅ | NEW |
| Cache | NEW | ✅ | ✅ | NEW |
| Connection | NEW | ✅ | NEW | NEW |
| ErrorHandling | NEW | ✅ | ✅ | NEW |
| ExpectContinue | NEW | NEW | NEW | NEW |
| Edge Cases | NEW | NEW | NEW | — |
| Concurrency | NEW | NEW | NEW | — |

## Tasks

### TASK-021-001: Add ExpectContinue and Edge Case Routes to Routes.cs
**Description:** As a test author, I want server-side routes for ExpectContinue (100-continue) and edge case scenarios so that integration tests can exercise these features.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-021-008, TASK-021-009
**Parallel:** yes — can run alongside TASK-021-002, TASK-021-003, TASK-021-004, TASK-021-005, TASK-021-006, TASK-021-007

**New Routes to Add:**
- `POST /expect/echo` — reads `Expect: 100-continue`, sends 100 Continue, then echoes body with 200
- `POST /expect/reject` — reads `Expect: 100-continue`, responds 417 Expectation Failed (no 100)
- `POST /expect/large` — accepts large body (>1KB) with 100-continue flow
- Ensure all new routes are registered in `RegisterExpectContinueRoutes()` and called from all fixtures (KestrelFixture, KestrelH2Fixture, KestrelTlsFixture)

**Acceptance Criteria:**
- [x] `Routes.RegisterExpectContinueRoutes(app)` added with 3 routes
- [x] All 3 Kestrel fixtures call `RegisterExpectContinueRoutes`
- [~] ⚠️ BLOCKED: Routes manually verified via `dotnet run` or existing smoke test pattern — No standalone `dotnet run` entry point exists for the integration test server; routes are verified structurally via build success and will be exercised by TASK-021-008 integration tests
- [x] Build passes with zero warnings

**Files:**
- `src/TurboHttp.IntegrationTests/Shared/Routes.cs` — add `RegisterExpectContinueRoutes`
- `src/TurboHttp.IntegrationTests/Shared/KestrelFixture.cs` — call new registration
- `src/TurboHttp.IntegrationTests/Shared/KestrelH2Fixture.cs` — call new registration
- `src/TurboHttp.IntegrationTests/Shared/KestrelTlsFixture.cs` — call new registration

---

### TASK-021-002: HTTP/1.0 Compression + Cookie Tests
**Description:** As a developer, I want integration tests for HTTP/1.0 compression (gzip/deflate/brotli) and cookie handling so that the feature BidiStages are verified with HTTP/1.0 framing.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** TASK-021-011
**Parallel:** yes — can run alongside TASK-021-001, TASK-021-003, TASK-021-004, TASK-021-005, TASK-021-006, TASK-021-007

**Pattern:** Copy `CompressionIntegrationTests.cs` / `CookieIntegrationTests.cs`, change to `new Version(1, 0)`, rename DisplayNames to `Compression-H10-NNN` / `Cookie-H10-NNN`.

**Acceptance Criteria:**
- [ ] `CompressionH10IntegrationTests.cs` created with 7 tests mirroring `CompressionIntegrationTests`
- [ ] `CookieH10IntegrationTests.cs` created with 11 tests mirroring `CookieIntegrationTests`
- [ ] All tests use `[Collection("Http1Integration")]` and `ClientHelper.CreateClient(port, new Version(1, 0))`
- [ ] DisplayNames follow `Compression-H10-001` / `Cookie-H10-001` pattern
- [ ] All 18 tests pass: `dotnet test --filter "FullyQualifiedName~H10IntegrationTests"`
- [ ] Build passes with zero warnings

**Files:**
- `src/TurboHttp.IntegrationTests/CompressionH10IntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/CookieH10IntegrationTests.cs` (NEW)

---

### TASK-021-003: HTTP/1.0 Redirect + Retry Tests
**Description:** As a developer, I want integration tests for HTTP/1.0 redirect following and retry logic so that RFC 9110 features are verified with HTTP/1.0 framing.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** TASK-021-011
**Parallel:** yes — can run alongside TASK-021-001, TASK-021-002, TASK-021-004, TASK-021-005, TASK-021-006, TASK-021-007

**Acceptance Criteria:**
- [ ] `RedirectH10IntegrationTests.cs` created with 14 tests mirroring `RedirectIntegrationTests`
- [ ] `RetryH10IntegrationTests.cs` created with 9 tests mirroring `RetryIntegrationTests`
- [ ] All tests use `new Version(1, 0)` and `[Collection("Http1Integration")]`
- [ ] DisplayNames follow `Redirect-H10-001` / `Retry-H10-001` pattern
- [ ] All 23 tests pass
- [ ] Build passes with zero warnings

**Files:**
- `src/TurboHttp.IntegrationTests/RedirectH10IntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/RetryH10IntegrationTests.cs` (NEW)

---

### TASK-021-004: HTTP/1.0 Cache + ErrorHandling + Connection Tests
**Description:** As a developer, I want integration tests for HTTP/1.0 caching, error handling, and connection management so that all feature categories have HTTP/1.0 coverage.

**Token Estimate:** ~50k tokens
**Predecessors:** none
**Successors:** TASK-021-011
**Parallel:** yes — can run alongside TASK-021-001, TASK-021-002, TASK-021-003, TASK-021-005, TASK-021-006, TASK-021-007

**HTTP/1.0 Connection specifics:**
- HTTP/1.0 does NOT default to keep-alive (unlike HTTP/1.1)
- Connection close is the default behavior
- `Connection: Keep-Alive` is opt-in
- No chunked encoding in HTTP/1.0

**Acceptance Criteria:**
- [ ] `CacheH10IntegrationTests.cs` created with 11 tests mirroring `CacheIntegrationTests`
- [ ] `ErrorHandlingH10IntegrationTests.cs` created with ~8 tests (adapted from `ErrorHandlingIntegrationTests`, excluding HTTP/1.1-specific edge cases like chunked)
- [ ] `ConnectionH10IntegrationTests.cs` created with ~4 tests:
  - Default no keep-alive (connection closes after single request)
  - Explicit `Connection: Keep-Alive` opt-in
  - Sequential requests on keep-alive connection
  - `Connection: close` explicitly
- [ ] All tests use `new Version(1, 0)` and `[Collection("Http1Integration")]`
- [ ] DisplayNames follow `Cache-H10-001` / `Error-H10-001` / `Conn-H10-001` pattern
- [ ] All ~23 tests pass
- [ ] Build passes with zero warnings

**Files:**
- `src/TurboHttp.IntegrationTests/CacheH10IntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/ErrorHandlingH10IntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/ConnectionH10IntegrationTests.cs` (NEW)

---

### TASK-021-005: HTTP/2 Connection + Multiplexing Tests
**Description:** As a developer, I want HTTP/2-specific connection and multiplexing integration tests so that stream-based connection reuse is verified end-to-end.

**Token Estimate:** ~35k tokens
**Predecessors:** none
**Successors:** TASK-021-011
**Parallel:** yes — can run alongside TASK-021-001, TASK-021-002, TASK-021-003, TASK-021-004, TASK-021-006, TASK-021-007

**HTTP/2 Connection specifics:**
- No `Connection` header — multiplexed streams over single connection
- Multiple concurrent requests on same connection
- Sequential requests reuse connection automatically
- RST_STREAM for individual stream errors (already in ErrorHandlingH2)

**Acceptance Criteria:**
- [ ] `ConnectionH2IntegrationTests.cs` created with ~5 tests:
  - Sequential requests reuse same HTTP/2 connection
  - Concurrent requests multiplexed (3-5 parallel `Task.WhenAll`)
  - Large body transfer over HTTP/2 (POST /echo with 64KB body)
  - Multiple endpoints on same connection
  - POST with body followed by GET on same connection
- [ ] All tests use `[Collection("Http2Integration")]` and `new Version(2, 0)`
- [ ] DisplayNames follow `Conn-H2-001` pattern
- [ ] All tests pass
- [ ] Build passes with zero warnings

**Files:**
- `src/TurboHttp.IntegrationTests/ConnectionH2IntegrationTests.cs` (NEW)

---

### TASK-021-006: TLS Uniform Coverage Part 1 (Compression + Cookie + Redirect + Retry)
**Description:** As a developer, I want full TLS integration tests for compression, cookies, redirects, and retries so that HTTPS transport is verified for all feature BidiStages.

**Token Estimate:** ~50k tokens
**Predecessors:** none
**Successors:** TASK-021-011
**Parallel:** yes — can run alongside TASK-021-001, TASK-021-002, TASK-021-003, TASK-021-004, TASK-021-005, TASK-021-007

**Pattern:** Copy HTTP/1.1 test classes, change to `KestrelTlsFixture`, `[Collection("TlsIntegration")]`, `scheme: "https"`, `new Version(1, 1)`. Rename DisplayNames to `*-TLS-NNN`.

**Note:** The existing `TlsIntegrationTests.cs` (9 tests) covers a mix of basic features. The new files provide dedicated per-feature coverage. The old file can remain as a cross-cutting sanity check.

**Acceptance Criteria:**
- [ ] `CompressionTlsIntegrationTests.cs` created with 7 tests
- [ ] `CookieTlsIntegrationTests.cs` created with 11 tests
- [ ] `RedirectTlsIntegrationTests.cs` created with 14 tests
- [ ] `RetryTlsIntegrationTests.cs` created with 9 tests
- [ ] All tests use `KestrelTlsFixture`, `[Collection("TlsIntegration")]`, `scheme: "https"`
- [ ] DisplayNames follow `Compression-TLS-001` / `Cookie-TLS-001` / `Redirect-TLS-001` / `Retry-TLS-001`
- [ ] All 41 tests pass
- [ ] Build passes with zero warnings

**Files:**
- `src/TurboHttp.IntegrationTests/CompressionTlsIntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/CookieTlsIntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/RedirectTlsIntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/RetryTlsIntegrationTests.cs` (NEW)

---

### TASK-021-007: TLS Uniform Coverage Part 2 (Cache + ErrorHandling + Connection)
**Description:** As a developer, I want full TLS integration tests for caching, error handling, and connection management so that HTTPS coverage is complete.

**Token Estimate:** ~45k tokens
**Predecessors:** none
**Successors:** TASK-021-011
**Parallel:** yes — can run alongside TASK-021-001, TASK-021-002, TASK-021-003, TASK-021-004, TASK-021-005, TASK-021-006

**Acceptance Criteria:**
- [ ] `CacheTlsIntegrationTests.cs` created with 11 tests
- [ ] `ErrorHandlingTlsIntegrationTests.cs` created with ~8 tests (adapted from HTTP/1.1 version)
- [ ] `ConnectionTlsIntegrationTests.cs` created with 5 tests (keep-alive over TLS, Connection: close over TLS, sequential reuse)
- [ ] All tests use `KestrelTlsFixture`, `[Collection("TlsIntegration")]`, `scheme: "https"`
- [ ] DisplayNames follow `Cache-TLS-001` / `Error-TLS-001` / `Conn-TLS-001`
- [ ] All ~24 tests pass
- [ ] Build passes with zero warnings

**Files:**
- `src/TurboHttp.IntegrationTests/CacheTlsIntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/ErrorHandlingTlsIntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/ConnectionTlsIntegrationTests.cs` (NEW)

---

### TASK-021-008: ExpectContinue Integration Tests (All Versions)
**Description:** As a developer, I want integration tests for the `ExpectContinueBidiStage` across all HTTP versions so that 100-continue handling is verified end-to-end.

**Token Estimate:** ~45k tokens
**Predecessors:** TASK-021-001 (routes must exist)
**Successors:** TASK-021-011
**Parallel:** no — depends on TASK-021-001

**Test scenarios per version (3 tests each):**
1. Large body (>1KB) triggers Expect: 100-continue → server sends 100 → body sent → 200 OK
2. Server rejects with 417 → body NOT sent → error/417 returned
3. Small body (<1KB) does NOT send Expect header

**Acceptance Criteria:**
- [ ] `ExpectContinueIntegrationTests.cs` created (HTTP/1.1, 3 tests)
- [ ] `ExpectContinueH10IntegrationTests.cs` created (HTTP/1.0, 3 tests)
- [ ] `ExpectContinueH2IntegrationTests.cs` created (HTTP/2, 3 tests)
- [ ] `ExpectContinueTlsIntegrationTests.cs` created (TLS, 3 tests)
- [ ] DisplayNames follow `Expect-001` / `Expect-H10-001` / `Expect-H2-001` / `Expect-TLS-001`
- [ ] All 12 tests pass against the routes from TASK-021-001
- [ ] Build passes with zero warnings

**Files:**
- `src/TurboHttp.IntegrationTests/ExpectContinueIntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/ExpectContinueH10IntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/ExpectContinueH2IntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/ExpectContinueTlsIntegrationTests.cs` (NEW)

---

### TASK-021-009: Edge Case Tests (Per Version)
**Description:** As a developer, I want edge case integration tests covering chunked trailers, large bodies, range requests, and version-specific quirks so that boundary conditions are verified.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-021-001 (for any new routes if needed)
**Successors:** TASK-021-011
**Parallel:** yes — can run alongside TASK-021-008 (different files)

**HTTP/1.1 Edge Cases (~8 tests):**
- Chunked response with trailers (`/chunked/trailer`)
- Chunked response with exact boundaries (`/chunked/exact/5/1024`)
- Chunked response with MD5 trailer (`/chunked/md5`)
- POST with chunked request body (`/echo/chunked`)
- Large body 256KB (`/large/256`)
- Multiple response headers with same name (`/multiheader`)
- Form URL-encoded POST (`/form/urlencoded`)
- Range request partial content (`/range/64`)

**HTTP/1.0 Edge Cases (~5 tests):**
- Large body via connection-close boundary (`/large/256`)
- POST with body echoed (`/echo`)
- Multiple status codes (`/status/200`, `/status/404`, `/status/500`)
- Headers echo roundtrip (`/headers/echo`)
- Empty body with no Content-Length (`/edge/empty-body`)

**HTTP/2 Edge Cases (~5 tests):**
- Binary body roundtrip (already exists in ErrorH2, add POST large binary 64KB)
- Many custom headers (`/h2/many-headers`)
- Large HPACK headers (`/h2/large-headers/4`)
- Stream priority weighted (`/h2/priority/16` — route exists, never tested!)
- Echo path (`/h2/echo-path` — route exists, never tested!)

**Acceptance Criteria:**
- [ ] `EdgeCaseIntegrationTests.cs` created (HTTP/1.1, ~8 tests)
- [ ] `EdgeCaseH10IntegrationTests.cs` created (HTTP/1.0, ~5 tests)
- [ ] `EdgeCaseH2IntegrationTests.cs` created (HTTP/2, ~5 tests)
- [ ] DisplayNames follow `Edge-001` / `Edge-H10-001` / `Edge-H2-001`
- [ ] All tests use existing routes (no new routes needed)
- [ ] All ~18 tests pass
- [ ] Build passes with zero warnings

**Files:**
- `src/TurboHttp.IntegrationTests/EdgeCaseIntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/EdgeCaseH10IntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/EdgeCaseH2IntegrationTests.cs` (NEW)

---

### TASK-021-010: Concurrency and Stress Tests (Per Version)
**Description:** As a developer, I want concurrency integration tests covering parallel requests and connection pool behavior so that the client works correctly under load.

**Token Estimate:** ~45k tokens
**Predecessors:** none
**Successors:** TASK-021-011
**Parallel:** yes — can run alongside all other tasks

**HTTP/1.1 Concurrency (~4 tests):**
- 10 parallel GET requests all succeed
- 5 parallel POST requests with different bodies
- Sequential burst: 20 requests in rapid succession
- Mixed methods (GET + POST + PUT) concurrently

**HTTP/1.0 Concurrency (~3 tests):**
- 5 parallel GET requests (each gets own connection — no keep-alive)
- Sequential burst: 10 requests rapidly
- Mixed GET + POST concurrently

**HTTP/2 Concurrency (~4 tests):**
- 10 parallel GET requests multiplexed over single connection
- 20 parallel requests stress test
- Mixed GET + POST multiplexed
- Concurrent requests to different endpoints

**Acceptance Criteria:**
- [ ] `ConcurrencyIntegrationTests.cs` created (HTTP/1.1, ~4 tests)
- [ ] `ConcurrencyH10IntegrationTests.cs` created (HTTP/1.0, ~3 tests)
- [ ] `ConcurrencyH2IntegrationTests.cs` created (HTTP/2, ~4 tests)
- [ ] All tests use `Task.WhenAll` for parallel execution
- [ ] Timeout set to 60s for stress tests
- [ ] DisplayNames follow `Concurrency-001` / `Concurrency-H10-001` / `Concurrency-H2-001`
- [ ] All ~11 tests pass reliably (no flaky tests)
- [ ] Build passes with zero warnings

**Files:**
- `src/TurboHttp.IntegrationTests/ConcurrencyIntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/ConcurrencyH10IntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/ConcurrencyH2IntegrationTests.cs` (NEW)

---

### TASK-021-011: Verification Gate
**Description:** As a developer, I want to verify that all new integration tests pass, the build is clean, and the coverage matrix is uniform.

**Token Estimate:** ~15k tokens
**Predecessors:** TASK-021-002, TASK-021-003, TASK-021-004, TASK-021-005, TASK-021-006, TASK-021-007, TASK-021-008, TASK-021-009, TASK-021-010
**Successors:** none
**Parallel:** no — final gate

**Acceptance Criteria:**
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` passes with zero errors and zero warnings
- [ ] `dotnet test src/TurboHttp.IntegrationTests/TurboHttp.IntegrationTests.csproj` — all tests pass
- [ ] Test count increased from ~139 to ~280+ tests
- [ ] Coverage matrix shows all feature categories covered for HTTP/1.0, HTTP/1.1, HTTP/2, TLS
- [ ] No flaky tests in 3 consecutive runs

**Files:** (read-only verification)
- All new test files from TASK-021-002 through TASK-021-010

---

## Task Dependency Graph

```
TASK-021-001 (Routes) ──────────→ TASK-021-008 (ExpectContinue) ──→ TASK-021-011 (Verify)
                       └────────→ TASK-021-009 (Edge Cases) ──────┘
TASK-021-002 (H10 Compress+Cookie) ──────────────────────────────→ TASK-021-011
TASK-021-003 (H10 Redirect+Retry) ──────────────────────────────→ TASK-021-011
TASK-021-004 (H10 Cache+Error+Conn) ────────────────────────────→ TASK-021-011
TASK-021-005 (H2 Connection) ──────────────────────────────────→ TASK-021-011
TASK-021-006 (TLS Part 1) ────────────────────────────────────→ TASK-021-011
TASK-021-007 (TLS Part 2) ────────────────────────────────────→ TASK-021-011
TASK-021-010 (Concurrency) ───────────────────────────────────→ TASK-021-011
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-021-001 | ~25k | none | yes (with 002-007, 010) | — |
| TASK-021-002 | ~40k | none | yes (with 001, 003-007, 010) | — |
| TASK-021-003 | ~40k | none | yes (with 001, 002, 004-007, 010) | — |
| TASK-021-004 | ~50k | none | yes (with 001-003, 005-007, 010) | — |
| TASK-021-005 | ~35k | none | yes (with 001-004, 006-007, 010) | — |
| TASK-021-006 | ~50k | none | yes (with 001-005, 007, 010) | — |
| TASK-021-007 | ~45k | none | yes (with 001-006, 010) | — |
| TASK-021-008 | ~45k | 001 | yes (with 009) | — |
| TASK-021-009 | ~50k | 001 | yes (with 008) | — |
| TASK-021-010 | ~45k | none | yes (with 001-009) | — |
| TASK-021-011 | ~15k | 002-010 | no | — |

**Total estimated tokens:** ~440k

## Functional Requirements

- FR-1: Every feature category (Compression, Cookie, Redirect, Retry, Cache, Connection, ErrorHandling) must have a dedicated test class for HTTP/1.0, HTTP/1.1, HTTP/2, and TLS
- FR-2: HTTP/1.0 test classes use `new Version(1, 0)` and `[Collection("Http1Integration")]` with `KestrelFixture`
- FR-3: TLS test classes use `scheme: "https"` and `[Collection("TlsIntegration")]` with `KestrelTlsFixture`
- FR-4: DisplayName format: `Feature-VERSION-NNN: description` (e.g., `Compression-H10-001`, `Cookie-TLS-005`)
- FR-5: All tests follow the `CreateClient()` + `await using var helper` + `CancellationTokenSource(30s)` pattern
- FR-6: ExpectContinue routes support both accept (100 Continue → 200) and reject (417) flows
- FR-7: Edge case tests exercise all untested existing routes (`/chunked/trailer`, `/chunked/exact`, `/h2/priority`, `/range`)
- FR-8: Concurrency tests use `Task.WhenAll` and verify all responses succeeded
- FR-9: HTTP/1.0 Connection tests verify no-keep-alive-by-default behavior (different from HTTP/1.1)
- FR-10: HTTP/2 Connection tests verify multiplexed concurrent requests over single connection

## Non-Goals

- HTTP/3 integration tests (QUIC not stable)
- Performance benchmarking (separate concern in `TurboHttp.Benchmarks`)
- Modifying production code or BidiStages
- Changing existing test classes (only adding new ones)
- TLS certificate validation tests (requires cert infrastructure changes)
- WebSocket upgrade tests
- Proxy support tests

## Technical Considerations

- **HTTP/1.0 + KestrelFixture:** Both HTTP/1.0 and HTTP/1.1 tests share `KestrelFixture` (same server). The `ClientHelper.CreateClient(port, new Version(1, 0))` sets `DefaultRequestVersion` so requests are sent as HTTP/1.0. Kestrel handles both versions.
- **TLS Fixture Routes:** `KestrelTlsFixture` registers all the same routes as `KestrelFixture`. TLS tests can use all existing routes without changes.
- **Cookie Secure flag over plaintext HTTP/1.0:** The `CookieH10IntegrationTests` should adapt the Secure cookie test — Secure cookies should NOT be sent over plaintext HTTP/1.0 (verify this behavior).
- **Concurrency test reliability:** Use longer timeouts (60s) and modest parallelism (5-20 requests) to avoid flaky tests on CI.
- **ExpectContinue + Kestrel:** Kestrel natively supports 100-continue. The `POST /expect/echo` route just needs to read the body — Kestrel handles the 100 response automatically when the app reads the request body. For 417 rejection, set status before reading body.
- **Max 500 lines per test file:** Per CLAUDE.md convention. Most files will be well under this limit.

## Success Metrics

- Test count increases from ~139 to ~280+ integration tests
- Every feature × version cell in the coverage matrix is filled
- All tests pass in CI on first run (no flaky tests)
- Zero build warnings

## Open Questions

_None — all questions resolved._
