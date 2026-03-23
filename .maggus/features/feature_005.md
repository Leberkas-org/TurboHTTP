# Feature 005: Integration Tests — Compression (RFC 9110)

## Introduction

Add end-to-end integration tests for content encoding/decoding. Validates that TurboHttp's DecompressionBidiStage correctly handles gzip, deflate, brotli, and content negotiation.

### Architecture Context

- **Components involved:** DecompressionBidiStage (Streams/Features), ContentEncodingDecoder (Protocol/RFC9110), KestrelFixture + KestrelH2Fixture
- **Existing infrastructure:** 5 compression routes in Routes.RegisterContentEncodingRoutes()

## Mandatory Testing Policy

> **Tests are required and must never be skipped, disabled, or marked as `[Skip]`.** Every acceptance criterion that specifies "All tests pass" is a hard gate — the feature is not complete until every test passes. No `[Fact(Skip = "...")]`, no `#if` guards, no `Assert.Skip()`. If a test fails, fix the code or the test — never suppress it.

## Goals

- Verify transparent decompression of gzip, deflate, and brotli responses
- Verify content negotiation via Accept-Encoding
- Verify identity encoding passthrough
- Verify correct Content-Length after decompression

## Tasks

### TASK-005-001: Compression Integration Tests — HTTP/1.1
**Description:** As a library consumer, I want compressed responses to be transparently decompressed.

**Token Estimate:** ~30k tokens
**Predecessors:** none
**Successors:** TASK-005-003
**Parallel:** yes — can run alongside TASK-005-002

**Acceptance Criteria:**
- [x] Test file `CompressionIntegrationTests.cs` in `src/TurboHttp.IntegrationTests/`
- [x] Collection: `"Http1Integration"`, uses `KestrelFixture`
- [x] Tests cover:
  - `GET /compress/gzip/{kb}` — gzip decompression, verify body length = kb*1024
  - `GET /compress/deflate/{kb}` — deflate decompression
  - `GET /compress/br/{kb}` — brotli decompression
  - `GET /compress/identity/{kb}` — no compression, baseline
  - `GET /compress/negotiate` with Accept-Encoding: gzip → gzip response
  - `GET /compress/negotiate` with Accept-Encoding: br → brotli response
  - `GET /compress/negotiate` with no Accept-Encoding → identity
- [x] All tests pass

### TASK-005-002: Compression Integration Tests — HTTP/2
**Description:** As a library consumer, I want compression to work identically over HTTP/2.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-005-003
**Parallel:** yes — can run alongside TASK-005-001

**Acceptance Criteria:**
- [x] Test file `CompressionH2IntegrationTests.cs` in `src/TurboHttp.IntegrationTests/`
- [x] Collection: `"Http2Integration"`, uses `KestrelH2Fixture`
- [x] Covers same compression routes over h2c
- [x] All tests pass

### TASK-005-003: Verify Full Compression Suite
**Description:** Run the complete integration test suite to confirm no regressions.

**Token Estimate:** ~5k tokens
**Predecessors:** TASK-005-001, TASK-005-002
**Successors:** none
**Parallel:** no

**Acceptance Criteria:**
- [ ] `dotnet test src/TurboHttp.IntegrationTests/` passes with 0 failures

## Task Dependency Graph

```
TASK-005-001 ──→ TASK-005-003
TASK-005-002 ──┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-005-001 | ~30k | none | yes (with 002) | — |
| TASK-005-002 | ~25k | none | yes (with 001) | — |
| TASK-005-003 | ~5k | 001, 002 | no | haiku |

**Total estimated tokens:** ~60k

## Functional Requirements

- FR-1: gzip-compressed responses must be transparently decompressed to original size
- FR-2: deflate-compressed responses must be transparently decompressed
- FR-3: brotli-compressed responses must be transparently decompressed
- FR-4: Accept-Encoding negotiation must result in matching Content-Encoding
- FR-5: Identity-encoded responses must pass through unchanged

## Non-Goals

- No request body compression testing (RequestCompressionBidiStage)
- No stacked encoding tests (gzip+deflate — already unit-tested)
- No unknown encoding handling tests

## Success Metrics

- All 14+ compression integration tests pass consistently
- Decompressed body sizes match expected values exactly
