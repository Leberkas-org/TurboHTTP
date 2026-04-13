<!-- maggus-id: a1fc20a3-766b-4fa3-8865-c0162b0ac25f -->
# Feature 004: HTTP/3 Test Parity

## Introduction

HTTP/3 has 1 integration test file (skipped smoke test) compared to HTTP/2's 16 and HTTP/1.0's 14. HTTP/3 also lacks frame-level fuzzing and security tests that HTTP/2 has (7+ files). This feature brings HTTP/3 test coverage to parity with the other protocol versions across integration tests and security/fuzz tests.

### Architecture Context

- **Vision alignment:** Test parity ensures HTTP/3 reliability matches HTTP/2 before production use
- **Components involved:** `TurboHTTP.IntegrationTests/H3/` (expand from 1 to 16 files), `TurboHTTP.Tests/Http3/Security/` (expand from 1 to 4 files)
- **Existing patterns:** H2 integration tests in `src/TurboHTTP.IntegrationTests/H2/` serve as direct templates — each test copies the H2 version and adapts for HTTP/3
- **Test infrastructure:** QUIC test server is available and functional
- **Prerequisite:** Feature 002 (config) and ideally Feature 003 (architecture) should be complete for full coverage, but basic tests can start after Feature 002

## Goals

- Create HTTP/3 integration tests matching all 16 categories from HTTP/2
- Create HTTP/3 security and fuzz tests matching HTTP/2's coverage
- Unskip `Http3SmokeSpec.cs`
- Validate all shared feature stages (cache, cookies, compression, redirect, retry, etc.) work correctly over HTTP/3

## Tasks

### TASK-004-001: Unskip and validate Http3SmokeSpec
**Description:** As a test maintainer, I want the HTTP/3 smoke test to run and pass so that we have a green baseline before adding more tests.

**Token Estimate:** ~20k tokens
**Predecessors:** none
**Successors:** TASK-004-002 through TASK-004-008
**Parallel:** no — must pass before parallel test writing begins

**Acceptance Criteria:**
- [ ] `Http3SmokeSpec.cs` — remove skip annotation ("QUIC is wild")
- [ ] Smoke test passes: basic GET, POST, status codes, headers
- [ ] If test infrastructure needs fixes, document and fix them
- [ ] `dotnet run --project TurboHTTP.IntegrationTests/TurboHTTP.IntegrationTests.csproj -- -class "TurboHTTP.IntegrationTests.H3.Http3SmokeSpec"` passes

**Files to modify:**
- `src/TurboHTTP.IntegrationTests/H3/Http3SmokeSpec.cs`

---

### TASK-004-002: HTTP/3 Connection & Concurrency integration tests
**Description:** As a test maintainer, I want connection lifecycle and concurrency tests for HTTP/3 so that multiplexing and connection reuse are validated end-to-end.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-004-001
**Successors:** TASK-004-009
**Parallel:** yes — can run alongside TASK-004-003 through TASK-004-008

**Acceptance Criteria:**
- [ ] `ConnectionSpec.cs` created in `H3/`: connection reuse, idle timeout, graceful close via GOAWAY
- [ ] `ConcurrencySpec.cs` created in `H3/`: multiple concurrent requests, stream multiplexing
- [ ] Tests adapted from H2 equivalents for HTTP/3 semantics (QUIC streams vs TCP)
- [ ] All tests pass with `dotnet run --project ... -- -class` per class
- [ ] Follow test conventions: `sealed`, `Spec` suffix, BDD method names, `[Fact(Timeout = 5000)]`

**Files to create:**
- `src/TurboHTTP.IntegrationTests/H3/ConnectionSpec.cs`
- `src/TurboHTTP.IntegrationTests/H3/ConcurrencySpec.cs`

**Reference files:**
- `src/TurboHTTP.IntegrationTests/H2/ConnectionSpec.cs`
- `src/TurboHTTP.IntegrationTests/H2/ConcurrencySpec.cs`

---

### TASK-004-003: HTTP/3 Cache & Cookie integration tests
**Description:** As a test maintainer, I want cache and cookie tests for HTTP/3 so that shared feature stages are validated over QUIC transport.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-004-001
**Successors:** TASK-004-009
**Parallel:** yes — can run alongside TASK-004-002, TASK-004-004 through TASK-004-008

**Acceptance Criteria:**
- [ ] `CacheSpec.cs` created in `H3/`: Cache-Control, ETag, 304 Not Modified, cache bypass
- [ ] `CookieSpec.cs` created in `H3/`: Set-Cookie, Cookie header, domain/path matching
- [ ] All tests pass per-class
- [ ] Follow test conventions

**Files to create:**
- `src/TurboHTTP.IntegrationTests/H3/CacheSpec.cs`
- `src/TurboHTTP.IntegrationTests/H3/CookieSpec.cs`

**Reference files:**
- `src/TurboHTTP.IntegrationTests/H2/CacheSpec.cs`
- `src/TurboHTTP.IntegrationTests/H2/CookieSpec.cs`

---

### TASK-004-004: HTTP/3 Compression integration tests
**Description:** As a test maintainer, I want compression tests for HTTP/3 so that Content-Encoding negotiation works over QUIC.

**Token Estimate:** ~35k tokens
**Predecessors:** TASK-004-001
**Successors:** TASK-004-009
**Parallel:** yes — can run alongside TASK-004-002, TASK-004-003, TASK-004-005 through TASK-004-008

**Acceptance Criteria:**
- [ ] `CompressionSpec.cs` created in `H3/`: gzip, deflate, brotli response decompression
- [ ] `RequestCompressionSpec.cs` created in `H3/`: request body compression
- [ ] All tests pass per-class
- [ ] Follow test conventions

**Files to create:**
- `src/TurboHTTP.IntegrationTests/H3/CompressionSpec.cs`
- `src/TurboHTTP.IntegrationTests/H3/RequestCompressionSpec.cs`

**Reference files:**
- `src/TurboHTTP.IntegrationTests/H2/CompressionSpec.cs`
- `src/TurboHTTP.IntegrationTests/H2/RequestCompressionSpec.cs`

---

### TASK-004-005: HTTP/3 Redirect & Retry integration tests
**Description:** As a test maintainer, I want redirect and retry tests for HTTP/3 so that these shared features work correctly over QUIC.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-004-001
**Successors:** TASK-004-009
**Parallel:** yes — can run alongside TASK-004-002 through TASK-004-004, TASK-004-006 through TASK-004-008

**Acceptance Criteria:**
- [ ] `RedirectSpec.cs` created in `H3/`: 301/302/303/307/308, POST→GET rewrite, redirect loops
- [ ] `RetrySpec.cs` created in `H3/`: idempotent retry on 503, 429, connection reset
- [ ] All tests pass per-class
- [ ] Follow test conventions

**Files to create:**
- `src/TurboHTTP.IntegrationTests/H3/RedirectSpec.cs`
- `src/TurboHTTP.IntegrationTests/H3/RetrySpec.cs`

**Reference files:**
- `src/TurboHTTP.IntegrationTests/H2/RedirectSpec.cs`
- `src/TurboHTTP.IntegrationTests/H2/RetrySpec.cs`

---

### TASK-004-006: HTTP/3 Error handling & Edge case integration tests
**Description:** As a test maintainer, I want error handling and edge case tests for HTTP/3 so that failure modes are validated.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-004-001
**Successors:** TASK-004-009
**Parallel:** yes — can run alongside TASK-004-002 through TASK-004-005, TASK-004-007, TASK-004-008

**Acceptance Criteria:**
- [ ] `ErrorHandlingSpec.cs` created in `H3/`: 4xx/5xx status codes, network errors, GOAWAY handling
- [ ] `EdgeCaseSpec.cs` created in `H3/`: malformed headers, missing content-length, empty body, large payload
- [ ] All tests pass per-class
- [ ] Follow test conventions

**Files to create:**
- `src/TurboHTTP.IntegrationTests/H3/ErrorHandlingSpec.cs`
- `src/TurboHTTP.IntegrationTests/H3/EdgeCaseSpec.cs`

**Reference files:**
- `src/TurboHTTP.IntegrationTests/H2/ErrorHandlingSpec.cs`
- `src/TurboHTTP.IntegrationTests/H2/EdgeCaseSpec.cs`

---

### TASK-004-007: HTTP/3 Feature interaction & Handler pipeline integration tests
**Description:** As a test maintainer, I want feature interaction and handler pipeline tests for HTTP/3 so that combined feature behavior is validated.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-004-001
**Successors:** TASK-004-009
**Parallel:** yes — can run alongside TASK-004-002 through TASK-004-006, TASK-004-008

**Acceptance Criteria:**
- [ ] `FeatureInteractionSpec.cs` created in `H3/`: cache + compression, retry + redirect, cookie + redirect
- [ ] `HandlerPipelineSpec.cs` created in `H3/`: custom TurboHandler middleware invocation order
- [ ] All tests pass per-class
- [ ] Follow test conventions

**Files to create:**
- `src/TurboHTTP.IntegrationTests/H3/FeatureInteractionSpec.cs`
- `src/TurboHTTP.IntegrationTests/H3/HandlerPipelineSpec.cs`

**Reference files:**
- `src/TurboHTTP.IntegrationTests/H2/FeatureInteractionSpec.cs`
- `src/TurboHTTP.IntegrationTests/H2/HandlerPipelineSpec.cs`

---

### TASK-004-008: HTTP/3 Resilience, ExpectContinue & MaxStreams integration tests
**Description:** As a test maintainer, I want resilience, expect-continue, and max streams tests for HTTP/3 so that protocol-specific and shared behaviors are fully covered.

**Token Estimate:** ~45k tokens
**Predecessors:** TASK-004-001
**Successors:** TASK-004-009
**Parallel:** yes — can run alongside TASK-004-002 through TASK-004-007

**Acceptance Criteria:**
- [ ] `ResilienceSpec.cs` created in `H3/`: timeout handling, partial response, connection drops
- [ ] `ExpectContinueSpec.cs` created in `H3/`: Expect: 100-continue handshake
- [ ] `MaxStreamConcurrencySpec.cs` created in `H3/`: enforce server MAX_STREAMS limit (QUIC-specific)
- [ ] All tests pass per-class
- [ ] Follow test conventions

**Files to create:**
- `src/TurboHTTP.IntegrationTests/H3/ResilienceSpec.cs`
- `src/TurboHTTP.IntegrationTests/H3/ExpectContinueSpec.cs`
- `src/TurboHTTP.IntegrationTests/H3/MaxStreamConcurrencySpec.cs`

**Reference files:**
- `src/TurboHTTP.IntegrationTests/H2/ResilienceSpec.cs`
- `src/TurboHTTP.IntegrationTests/H2/ExpectContinueSpec.cs`
- `src/TurboHTTP.IntegrationTests/H2/MaxConcurrentStreamsSpec.cs`

---

### TASK-004-009: HTTP/3 security and fuzz tests
**Description:** As a security-conscious developer, I want frame-level fuzzing and bomb/DoS tests for HTTP/3 so that the protocol layer is hardened against malicious input.

**Token Estimate:** ~60k tokens
**Predecessors:** TASK-004-001 (minimum), ideally TASK-004-002 through TASK-004-008
**Successors:** TASK-004-010
**Parallel:** no — should run after integration tests validate happy paths

**Acceptance Criteria:**
- [ ] `QpackBombSpec.cs` created: oversized dynamic table updates, large integer-encoded fields, rapid table churn
- [ ] `Http3FrameFuzzSpec.cs` created: corrupt frame types, invalid stream IDs, DATA on control stream, oversized payloads
- [ ] `Http3FieldValidationFuzzSpec.cs` created: invalid pseudo-headers, duplicate `:method`, uppercase names, CR/LF injection
- [ ] `Http3SecuritySpec.cs` created: header compression ratio attack, SETTINGS bomb, control stream starvation
- [ ] All tests reject malicious input gracefully (proper error codes, no crashes)
- [ ] All tests pass: `dotnet run --project TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -namespace "TurboHTTP.Tests.Http3.Security"`
- [ ] Follow test conventions

**Files to create:**
- `src/TurboHTTP.Tests/Http3/Security/QpackBombSpec.cs`
- `src/TurboHTTP.Tests/Http3/Security/Http3FrameFuzzSpec.cs`
- `src/TurboHTTP.Tests/Http3/Security/Http3FieldValidationFuzzSpec.cs`
- `src/TurboHTTP.Tests/Http3/Security/Http3SecuritySpec.cs`

**Reference files:**
- `src/TurboHTTP.Tests/Http2/Security/SecuritySpec.cs`
- `src/TurboHTTP.Tests/Http2/Security/FuzzHarnessPart1Spec.cs`
- `src/TurboHTTP.Tests/Http2/Security/HpackBombSpec.cs`
- `src/TurboHTTP.Tests/Http3/Security/QpackSecuritySpec.cs` (existing, extend patterns)

---

### TASK-004-010: Update ARCHITECTURE.md test coverage status
**Description:** As a maintainer, I want ARCHITECTURE.md to reflect HTTP/3 test parity so that documentation is accurate.

**Token Estimate:** ~10k tokens
**Predecessors:** TASK-004-009
**Successors:** none
**Parallel:** no
**Model:** haiku

**Acceptance Criteria:**
- [ ] ARCHITECTURE.md "Implementation Status" HTTP/3 score updated to reflect test parity
- [ ] "Open gaps" section updated to remove test-related items
- [ ] Testing Structure table reflects H3 integration test coverage

**Files to modify:**
- `ARCHITECTURE.md`

## Task Dependency Graph

```
TASK-004-001 ──→ TASK-004-002 ──┐
             ├──→ TASK-004-003  │
             ├──→ TASK-004-004  │
             ├──→ TASK-004-005  ├──→ TASK-004-009 ──→ TASK-004-010
             ├──→ TASK-004-006  │
             ├──→ TASK-004-007  │
             └──→ TASK-004-008 ──┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-004-001 | ~20k | none | no | — |
| TASK-004-002 | ~40k | 001 | yes (with 003-008) | — |
| TASK-004-003 | ~40k | 001 | yes (with 002,004-008) | — |
| TASK-004-004 | ~35k | 001 | yes (with 002-003,005-008) | — |
| TASK-004-005 | ~40k | 001 | yes (with 002-004,006-008) | — |
| TASK-004-006 | ~40k | 001 | yes (with 002-005,007-008) | — |
| TASK-004-007 | ~40k | 001 | yes (with 002-006,008) | — |
| TASK-004-008 | ~45k | 001 | yes (with 002-007) | — |
| TASK-004-009 | ~60k | 001 (ideally 002-008) | no | — |
| TASK-004-010 | ~10k | 009 | no | haiku |

**Total estimated tokens:** ~370k

## Functional Requirements

- FR-1: Every integration test category present in H2 must have an equivalent in H3
- FR-2: Integration tests must run against real QUIC test server (not mocked)
- FR-3: Security tests must validate graceful rejection of malicious input with proper HTTP/3 error codes
- FR-4: Fuzz tests must cover frame, QPACK, and field validation attack surfaces
- FR-5: All tests must follow project test conventions (sealed, Spec suffix, BDD names, Timeout, RFC traits)

## Non-Goals

- No changes to production code (unless test failures reveal bugs — document and fix in Feature 003)
- No performance benchmarks (separate concern)
- No HTTP/1.x or HTTP/2 test changes

## Technical Considerations

- Integration tests may need a QUIC-capable test server fixture — check if `ServerFixture` supports HTTP/3 or needs extension
- Some H2 tests may not apply to H3 (e.g., TCP-specific connection behavior) — note and skip with reason
- QUIC on Windows requires TLS 1.3 — ensure test environment supports it
- Max 500 lines per test file per CLAUDE.md conventions — split large specs into Part1/Part2 if needed

## Success Metrics

- 16 integration test files in `H3/` (matching H2 count)
- 4 security/fuzz test files in `Http3/Security/`
- All tests pass consistently (no flaky tests)
- HTTP/3 integration test coverage matches HTTP/2 category-for-category

## Open Questions

None — all questions resolved.
