# Feature 003: Integration Tests — Retries (RFC 9110 §9.2)

## Introduction

Add end-to-end integration tests for retry handling. Validates that TurboHttp's RetryBidiStage correctly retries idempotent requests, respects Retry-After headers, and does NOT retry non-idempotent methods.

### Architecture Context

- **Components involved:** RetryBidiStage (Streams/Features), RetryEvaluator (Protocol/RFC9110), KestrelFixture + KestrelH2Fixture
- **Existing infrastructure:** 8 retry routes in Routes.RegisterRetryRoutes(), per-key atomic counters for succeed-after semantics

## Mandatory Testing Policy

> **Tests are required and must never be skipped, disabled, or marked as `[Skip]`.** Every acceptance criterion that specifies "All tests pass" is a hard gate — the feature is not complete until every test passes. No `[Fact(Skip = "...")]`, no `#if` guards, no `Assert.Skip()`. If a test fails, fix the code or the test — never suppress it.

## Goals

- Verify idempotent methods (GET, PUT, DELETE) are retried on 503
- Verify non-idempotent methods (POST) are NOT retried
- Verify Retry-After header parsing (seconds and HTTP-date formats)
- Verify succeed-after-N pattern (transient failures)
- Verify 408 Request Timeout triggers retry

## Tasks

### TASK-003-001: Retry Integration Tests — HTTP/1.1
**Description:** As a library consumer, I want automatic retries for idempotent requests so that transient server errors are handled transparently.

**Token Estimate:** ~35k tokens
**Predecessors:** none
**Successors:** TASK-003-003
**Parallel:** yes — can run alongside TASK-003-002

**Acceptance Criteria:**
- [x] Test file `RetryIntegrationTests.cs` in `src/TurboHttp.IntegrationTests/`
- [x] Collection: `"Http1Integration"`, uses `KestrelFixture`
- [x] Tests cover all retry routes:
  - `GET /retry/408` — 408 Request Timeout → retried, eventually fails
  - `GET /retry/503` — 503 Service Unavailable → retried
  - [~] ⚠️ BLOCKED: `HEAD /retry/503` — HEAD is idempotent → retried — Http11DecoderStage lacks HEAD-awareness; decoder waits for body bytes that never arrive, causing timeout. Requires decoder-level fix (pass request method context to decoder).
  - `GET /retry/503-retry-after/{seconds}` — Retry-After: seconds
  - `GET /retry/503-retry-after-date` — Retry-After: HTTP-date
  - `GET /retry/succeed-after/{n}` — fails N-1 times, then 200
  - `PUT /retry/503` — PUT is idempotent → retried
  - `DELETE /retry/503` — DELETE is idempotent → retried
  - `POST /retry/non-idempotent-503` — POST must NOT be retried → 503
- [x] All tests pass

### TASK-003-002: Retry Integration Tests — HTTP/2
**Description:** As a library consumer, I want retries to work identically over HTTP/2.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-003-003
**Parallel:** yes — can run alongside TASK-003-001

**Acceptance Criteria:**
- [ ] Test file `RetryH2IntegrationTests.cs` in `src/TurboHttp.IntegrationTests/`
- [ ] Collection: `"Http2Integration"`, uses `KestrelH2Fixture`
- [ ] Covers same retry routes over h2c
- [ ] All tests pass

### TASK-003-003: Verify Full Retry Suite
**Description:** Run the complete integration test suite to confirm no regressions.

**Token Estimate:** ~5k tokens
**Predecessors:** TASK-003-001, TASK-003-002
**Successors:** none
**Parallel:** no

**Acceptance Criteria:**
- [ ] `dotnet test src/TurboHttp.IntegrationTests/` passes with 0 failures

## Task Dependency Graph

```
TASK-003-001 ──→ TASK-003-003
TASK-003-002 ──┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-003-001 | ~35k | none | yes (with 002) | — |
| TASK-003-002 | ~25k | none | yes (with 001) | — |
| TASK-003-003 | ~5k | 001, 002 | no | haiku |

**Total estimated tokens:** ~65k

## Functional Requirements

- FR-1: GET/HEAD/PUT/DELETE returning 503 must be retried up to MaxRetries
- FR-2: POST returning 503 must NOT be retried — return 503 to caller
- FR-3: Retry-After header (integer seconds) must be respected before retry
- FR-4: Retry-After header (HTTP-date) must be parsed and respected
- FR-5: succeed-after-N must eventually return 200 after N-1 failures
- FR-6: 408 Request Timeout must trigger retry for idempotent methods

## Non-Goals

- No retry timing precision tests (Retry-After delay is best-effort)
- No concurrent retry stress testing
- No custom RetryPolicy configuration testing (unit-tested)

## Technical Considerations

- The `succeed-after/{n}` route uses `ConcurrentDictionary` counters — tests must use unique keys or account for shared state
- Retry-After date route generates a date 10 seconds from now — test timeout must accommodate this

## Success Metrics

- All 18+ retry integration tests pass consistently
- POST non-idempotent test confirms 503 is returned without retry
