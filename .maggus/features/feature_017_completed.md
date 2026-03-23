# Feature 017: Fix 5 Failing Tests (RedirectHandler Body Preservation + ConnectionStage Race Condition)

## Introduction

5 tests are currently failing on branch `poc2`:
- **4 RedirectHandler tests** (RFC9110): `Assert.Same()` fails because `BuildRedirectRequest()` correctly buffers body content into a new `ByteArrayContent` on 307/308 redirects, but tests expect the original `StringContent` instance to be preserved.
- **1 ConnectionStage test** (StreamTests): `onUpstreamFinish` force-cancels the inbound pump before it can emit its `CloseSignalItem`, causing a race condition where only 1 item is collected instead of 2.

Both are correctness issues — the RedirectHandler tests are too strict, and the ConnectionStage has a real race condition in its shutdown logic.

### Architecture Context

- **RedirectHandler** (`src/TurboHttp/Protocol/RFC9110/RedirectHandler.cs`) — RFC 9110 §15.4 redirect handling. The body buffering at lines 124-138 is intentional: the encoder disposes the content stream on first use, so redirects need a re-readable copy. The tests incorrectly demand reference identity.
- **ConnectionStage** (`src/TurboHttp/Transport/ConnectionStage.cs`) — Akka.Streams `GraphStage` bridging the stream pipeline to TCP/QUIC channels. The `onUpstreamFinish` handler (line 70-74) cancels the inbound pump CTS, which causes the pump's `OperationCanceledException` catch block to `return` without emitting `CloseSignalItem`.

## Goals

- Fix all 5 failing tests so the full test suite passes
- Correct the ConnectionStage shutdown race condition for production correctness (not just test fix)
- Maintain existing behavior — no functional regressions

## Tasks

### TASK-017-001: Fix RedirectHandler Test Assertions — Replace Assert.Same with Content Equivalence
**Description:** As a developer, I want the 4 RedirectHandler body preservation tests to verify content equivalence (same bytes, same headers) instead of reference identity, so that the tests correctly validate the buffering behavior.

**Token Estimate:** ~15k tokens
**Predecessors:** none
**Successors:** none
**Parallel:** yes — can run alongside TASK-017-002

**Acceptance Criteria:**
- [x] `Assert.Same(content, redirected.Content)` replaced in all 4 tests:
  - `Should_PreservePostMethodAndBody_When_307TemporaryRedirect` (line 105)
  - `Should_PreservePutMethodAndBody_When_307TemporaryRedirect` (line 122)
  - `Should_PreservePostMethodAndBody_When_308PermanentRedirect` (line 152)
  - `Should_PreservePatchMethodAndBody_When_308PermanentRedirect` (line 169)
- [x] Replacement assertions verify: content is not null, byte content is equal, Content-Type header is preserved
- [x] Test methods changed from `void` to `async Task` (required for `ReadAsByteArrayAsync`)
- [x] All 4 tests pass
- [x] Full `TurboHttp.Tests` suite passes (0 failures)

**Files to modify:**
- `src/TurboHttp.Tests/RFC9110/01_RedirectHandlerTests.cs`

### TASK-017-002: Fix ConnectionStage Race Condition — Don't Cancel Inbound Pump on Upstream Finish
**Description:** As a developer, I want the ConnectionStage to let the inbound pump drain naturally when upstream finishes, so that `CloseSignalItem` is always emitted before the stage completes.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** none
**Parallel:** yes — can run alongside TASK-017-001

**Acceptance Criteria:**
- [x] New `_upstreamFinished` boolean field added to `ConnectionStage.Logic`
- [x] `onUpstreamFinish` handler (line 70-74) changed: sets `_upstreamFinished = true` instead of calling `StopInboundPump()` + `CompleteStage()`; only calls `CompleteStage()` immediately if `_handle is null` (no active connection)
- [x] `_onInboundComplete` callback (line 132-143) extended: after pushing/enqueuing `CloseSignalItem`, checks `_upstreamFinished` and calls `CompleteStage()` if true
- [x] `onDownstreamFinish` handler (line 84-88) unchanged — it should still force-stop the pump (downstream cancelled = nobody to receive data)
- [x] CS-004 round-trip test passes reliably
- [x] Full `TurboHttp.StreamTests` suite passes (0 failures)
- [x] Full solution test suite passes

**Files to modify:**
- `src/TurboHttp/Transport/ConnectionStage.cs`

## Task Dependency Graph

```
TASK-017-001 (RedirectHandler tests)
TASK-017-002 (ConnectionStage race condition)
```

Both tasks are fully independent and can run in parallel.

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-017-001 | ~15k | none | yes (with 002) | — |
| TASK-017-002 | ~25k | none | yes (with 001) | — |

**Total estimated tokens:** ~40k

## Functional Requirements

- FR-1: RedirectHandler must buffer body content on 307/308 redirects (existing behavior — no change)
- FR-2: RedirectHandler tests must verify byte-level content equivalence and header preservation
- FR-3: ConnectionStage must emit `CloseSignalItem` when the inbound channel completes, regardless of upstream finish timing
- FR-4: ConnectionStage must still force-stop the pump on downstream cancellation (no data consumer)
- FR-5: All 4,264 tests across the solution must pass after changes

## Non-Goals

- No changes to `RedirectHandler.BuildRedirectRequest()` implementation — the buffering is correct
- No changes to other ConnectionStage handlers or the inbound pump loop itself
- No new test files — only modifying existing tests and implementation

## Technical Considerations

- The `onUpstreamFinish` change means the stage stays alive slightly longer (until inbound pump completes). This is correct for production: the TCP connection may still have inbound data even after the request pipeline finishes sending.
- The `_upstreamFinished` flag is only accessed from within Akka stage callbacks (single-threaded by design) — no synchronization needed.
- `PostStop()` (line 298) still calls `StopInboundPump()` as a final safety net when the stage is torn down.

## Success Metrics

- All 5 previously failing tests pass
- Full solution test suite: 0 failures
- No new warnings introduced

## Open Questions

*None — root causes are clear and fixes are straightforward.*
