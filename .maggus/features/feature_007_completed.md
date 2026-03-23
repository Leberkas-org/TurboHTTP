# Feature 007: Integration Tests — Error Handling & Edge Cases

## Introduction

Add end-to-end integration tests for error handling, timeouts, and edge cases. Validates that TurboHttp gracefully handles server errors, connection aborts, malformed responses, and unusual but valid HTTP semantics.

### Architecture Context

- **Components involved:** All layers — from Client (timeout handling) through Streams (stage error propagation) to Transport (connection abort detection)
- **Existing infrastructure:** Edge case routes in Routes (KestrelFixture), H2 error routes in Routes.RegisterErrorHandlingRoutes()

## Mandatory Testing Policy

> **Tests are required and must never be skipped, disabled, or marked as `[Skip]`.** Every acceptance criterion that specifies "All tests pass" is a hard gate — the feature is not complete until every test passes. No `[Fact(Skip = "...")]`, no `#if` guards, no `Assert.Skip()`. If a test fails, fix the code or the test — never suppress it.

## Goals

- Verify timeout handling (delay routes)
- Verify connection abort mid-response
- Verify large header handling
- Verify empty body and Content-Length: 0 semantics
- Verify unknown Content-Encoding passthrough
- Verify HTTP/2 abort/RST_STREAM handling

## Tasks

### TASK-007-001: Error Handling Integration Tests — HTTP/1.1
**Description:** As a library consumer, I want the client to handle server errors gracefully without hanging or crashing.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** TASK-007-003
**Parallel:** yes — can run alongside TASK-007-002

**Acceptance Criteria:**
- [x] Test file `ErrorHandlingIntegrationTests.cs` in `src/TurboHttp.IntegrationTests/`
- [x] Collection: `"Http1Integration"`, uses `KestrelFixture`
- [x] Tests cover:
  - `GET /delay/{ms}` — request completes after delay, timeout cancellation works
  - `GET /edge/close-mid-response` — connection abort mid-body → exception or partial error
  - `GET /edge/large-header/{kb}` — large response header handling (1KB, 4KB)
  - `GET /edge/unknown-encoding` — Content-Encoding: x-custom causes graceful failure (RFC 9110 §8.4)
  - `GET /edge/empty-body` — 200 with no body, no Content-Length
  - `GET /empty-cl` — 200 with Content-Length: 0
  - `GET /status/{code}` — 4xx and 5xx status codes are returned (not thrown)
  - `GET /unknown-headers` — custom X-Unknown-* headers are accessible
- [x] Timeout test uses short CancellationToken (5s) with /delay/10000 → OperationCanceledException
- [x] All tests pass

### TASK-007-002: Error Handling Integration Tests — HTTP/2
**Description:** As a library consumer, I want HTTP/2-specific error handling to work correctly.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-007-003
**Parallel:** yes — can run alongside TASK-007-001

**Acceptance Criteria:**
- [x] Test file `ErrorHandlingH2IntegrationTests.cs` in `src/TurboHttp.IntegrationTests/`
- [x] Collection: `"Http2Integration"`, uses `KestrelH2Fixture`
- [x] Tests cover:
  - `GET /h2/abort` — RST_STREAM handling → exception
  - `GET /h2/delay/{ms}` — timeout cancellation over HTTP/2
  - `GET /status/{code}` — 4xx/5xx over HTTP/2
  - `GET /h2/many-headers` — 20 custom response headers decoded correctly
  - `POST /h2/echo-binary` — binary body roundtrip
  - `GET /h2/large-headers/{kb}` — large HPACK-compressed headers
- [x] All tests pass

### TASK-007-003: Verify Full Error Handling Suite
**Description:** Run the complete integration test suite to confirm no regressions.

**Token Estimate:** ~5k tokens
**Predecessors:** TASK-007-001, TASK-007-002
**Successors:** none
**Parallel:** no

**Acceptance Criteria:**
- [x] `dotnet test src/TurboHttp.IntegrationTests/` passes with 0 failures

## Task Dependency Graph

```
TASK-007-001 ──→ TASK-007-003
TASK-007-002 ──┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-007-001 | ~40k | none | yes (with 002) | — |
| TASK-007-002 | ~25k | none | yes (with 001) | — |
| TASK-007-003 | ~5k | 001, 002 | no | haiku |

**Total estimated tokens:** ~70k

## Functional Requirements

- FR-1: CancellationToken must abort in-flight requests within tolerance
- FR-2: Mid-response connection abort must surface as an exception, not hang
- FR-3: Large headers must be received correctly (up to Kestrel's 512KB limit)
- FR-4: Unknown Content-Encoding must pass the raw body through
- FR-5: HTTP/2 RST_STREAM must surface as an appropriate exception
- FR-6: 4xx/5xx status codes must be returned as HttpResponseMessage, not thrown

## Non-Goals

- No connection pool exhaustion testing
- No DNS resolution failure testing
- No TLS handshake failure testing (covered in Feature 008)

## Success Metrics

- All 14+ error handling tests pass consistently
- No test hangs due to unhandled timeouts or aborts
