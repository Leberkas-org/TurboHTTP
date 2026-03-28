<!-- maggus-id: cf72e87a-4fba-4084-b501-eebd41322c42 -->

# Feature 030: HTTP/1.0 Deadlock Elimination (Short-Term Fixes + Long-Term Architecture)

## Introduction

HTTP/1.0 integration tests are systematically flaky (1-4 failures per run at ~88 tests) due to a fundamental architectural mismatch: TurboHttp's Akka.Streams pipeline assumes long-lived TCP connections, but HTTP/1.0 closes the connection after every response (RFC 1945 §6.1). This creates race conditions in the feedback loop (retry/redirect/cache re-injection) that manifest as 13 documented deadlock patterns. 11 are fixed, 2 remain active:

- **DL-009**: RetryBidi `_inFlightCount` race — pipeline hangs ~10-15s after 503 retry on HTTP/1.0
- **DL-010**: CacheBidi `ReadAsByteArrayAsync` blocking — pipeline hangs when caching HTTP/1.0 response body

HTTP/1.1 and HTTP/2 are unaffected because their connections persist across requests.

### Architecture Context

- **Components involved:** RetryBidiStage, CacheBidiStage, GroupByHostKeyStage, ExtractOptionsStage, ConnectionReuseStage, ConnectionStage, TcpTransportHandler
- **Pattern:** Akka.Streams GraphStage pipeline with BidiFlow stacking and per-host substreaming via GroupByHostKey
- **Key file:** `ProtocolCoreGraphBuilder.BuildConnectionFlow()` (lines 119-173) — the feedback loop where all HTTP/1.0 deadlocks originate
- **New components (long-term):** IConnectionScope, SingleRequestConnectionScope, PersistentConnectionScope, ConnectionScopeStage

## Goals

- Eliminate DL-009 and DL-010 active bugs (short-term)
- Achieve 0 flaky HTTP/1.0 integration test failures across 3 consecutive runs
- Eliminate the architectural root cause of HTTP/1.0 deadlocks (long-term)
- Remove protocol-specific branches from ExtractOptionsStage, ConnectionReuseStage, and TcpTransportHandler
- Maintain 100% backward compatibility for HTTP/1.1, HTTP/2, and HTTP/3
- No public API changes

## Tasks

### TASK-030-001: RetryBidiStage Atomic Transaction Guard (DL-009)

**Description:** As a developer, I want RetryBidiStage to treat the entire retry decision (evaluate → enqueue → emit → decrement) as an atomic transaction, so that `TryCompleteIfDone()` cannot fire mid-decision and close the outlet prematurely.

**Token Estimate:** ~30k tokens
**Predecessors:** none
**Successors:** TASK-030-004
**Parallel:** yes — can run alongside TASK-030-002, TASK-030-003

**Acceptance Criteria:**
- [x] Add `_retryTransactionActive` boolean field to RetryBidiStage.Logic
- [x] Set `_retryTransactionActive = true` before retry evaluation in response handler (~line 219)
- [x] Set `_retryTransactionActive = false` after `_inFlightCount--` and `TryPullResponse()` (~line 239)
- [x] Call `TryCompleteIfDone()` only after transaction completes
- [x] `TryCompleteIfDone()` returns early if `_retryTransactionActive == true`
- [x] Same pattern applied to `OnTimer()` path (delayed retries)
- [x] All existing RetryBidiStage unit tests pass
- [x] All H10 retry integration tests pass (Retry-H10-001 through Retry-H10-009)
- [x] Build succeeds with zero warnings

### TASK-030-002: GroupByHostKeyStage Substream Liveness Guard (DL-009 + DL-010)

**Description:** As a developer, I want GroupByHostKeyStage's `TryCompleteStage()` to not complete while substreams are alive but idle (between "no pending offers" and "WatchTask fires"), so that async work in downstream stages (RetryBidi retries, CacheBidi body reads) is not interrupted.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** TASK-030-004
**Parallel:** yes — can run alongside TASK-030-001, TASK-030-003

**Acceptance Criteria:**
- [x] Modify `TryCompleteStage()` to require ALL substreams to have `IsDead == true` before completing
- [x] If any substream has `!IsDead && !Offering && Pending.Count == 0`, defer completion and register WatchTask continuation
- [x] Existing two-phase completion pattern preserved (deferred → actual)
- [x] All existing GroupByHostKeyStage stream tests pass
- [x] Build succeeds with zero warnings

### TASK-030-003: CacheBidiStage Async Read Backpressure Guard (DL-010)

**Description:** As a developer, I want CacheBidiStage to maintain backpressure on its request inlet while `_pendingAsyncRead` is active, so that the substream queue doesn't appear idle to GroupByHostKeyStage during async body reads.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-030-004
**Parallel:** yes — can run alongside TASK-030-001, TASK-030-002

**Acceptance Criteria:**
- [x] Add guard `if (_pendingAsyncRead) return;` to all `TryPull*` methods in CacheBidiStage
- [x] After async callback fires and `_pendingAsyncRead = false`, resume pulling
- [x] Verify `_pendingAsyncRead` flag is correctly set/unset in all paths (success + failure)
- [x] All existing CacheBidiStage stream tests pass
- [x] Build succeeds with zero warnings

### TASK-030-004: Short-Term Regression Tests + Stress Test

**Description:** As a developer, I want comprehensive regression tests that verify DL-009 and DL-010 are fixed, including a stress test that runs multiple retry+cache operations sequentially on HTTP/1.0.

**Token Estimate:** ~35k tokens
**Predecessors:** TASK-030-001, TASK-030-002, TASK-030-003
**Successors:** TASK-030-005
**Parallel:** no — requires all short-term fixes

**Acceptance Criteria:**
- [x] Existing DLH10-001 (RetryBidi race) passes without Skip annotation
- [x] Existing DLH10-002 (CacheBidi async read) passes without Skip annotation
- [x] New stress test: 10 sequential retry+cache operations on HTTP/1.0 within 60s timeout
- [x] Build succeeds with zero warnings

### TASK-030-005: IConnectionScope Interface + Implementations

**Description:** As a developer, I want a connection scope abstraction that encapsulates protocol-specific connection lifecycle (acquire, use, return), so that the pipeline doesn't need protocol-aware branches for HTTP/1.0 vs HTTP/1.1+.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-030-004
**Successors:** TASK-030-006
**Parallel:** no — should only start after short-term fixes are verified
**Model:** opus — complex abstraction design

**Acceptance Criteria:**
- [x] `IConnectionScope` interface with `AcquireAsync()`, `ReturnAsync()`, `CanReuse()`, `CleanupAsync()`
- [x] `SingleRequestConnectionScope` for HTTP/1.0: always new connection, always close
- [x] `PersistentConnectionScope` for HTTP/1.1+: reuse if keep-alive, close on Connection: close
- [x] Factory method `ConnectionScope.Create(version, pool, options)` dispatches to correct implementation
- [x] Unit tests for both scope types: acquire, return-close, return-reuse, cleanup, double-dispose safety
- [x] Files created in `src/TurboHttp/Transport/ConnectionScope/`
- [x] Build succeeds with zero warnings (new code, not yet integrated)

### TASK-030-006: ConnectionScopeStage Implementation

**Description:** As a developer, I want a GraphStage that wraps the encoder+transport+decoder triplet and manages the connection lifecycle via IConnectionScope, so that connection management is transparent to the rest of the pipeline.

**Token Estimate:** ~80k tokens
**Predecessors:** TASK-030-005
**Successors:** TASK-030-007
**Parallel:** no — requires IConnectionScope
**Model:** opus — complex GraphStage with async lifecycle management

**Acceptance Criteria:**
- [ ] `ConnectionScopeStage` is a `GraphStage<FlowShape<HttpRequestMessage, HttpResponseMessage>>`
- [ ] Port names: `ConnectionScope.In` / `ConnectionScope.Out`
- [ ] For HTTP/1.0: acquires new connection per request, closes after response
- [ ] For HTTP/1.1+: reuses connection across requests, closes on keep-alive=false
- [ ] Internal wiring: request → encoder → transport → decoder → response
- [ ] Uses `GetAsyncCallback` for all async operations (acquire, return, cleanup)
- [ ] Handles upstream finish: cleanup scope, close connection
- [ ] Handles downstream cancel: cleanup scope, close connection
- [ ] Stream tests in `src/TurboHttp.StreamTests/Transport/ConnectionScopeStageTests.cs`
- [ ] Tests cover: single request, sequential requests (H10), connection reuse (H11), upstream failure, downstream cancel
- [ ] Build succeeds with zero warnings

### TASK-030-007: ProtocolCoreGraphBuilder Integration

**Description:** As a developer, I want to rewrite `BuildConnectionFlow()` to use ConnectionScopeStage instead of the current feedback loop (ExtractOptionsStage + ConnectionReuseStage + Broadcast + MergePreferred), so that the connection lifecycle is simplified.

**Token Estimate:** ~75k tokens
**Predecessors:** TASK-030-006
**Successors:** TASK-030-008
**Parallel:** no — requires ConnectionScopeStage
**Model:** opus — complex graph rewiring

**Acceptance Criteria:**
- [ ] `BuildConnectionFlow()` uses `ConnectionScopeStage` wrapping the TEngine BidiFlow
- [ ] `ExtractOptionsStage` simplified: remove `_needsReconnect`, remove `InReuse` inlet, remove `isHttp10` check
- [ ] `ConnectionReuseStage` simplified or removed: reuse evaluation moved into ConnectionScopeStage
- [ ] Feedback loop removed: no more Broadcast + MergePreferred wiring (lines 145-169)
- [ ] `TcpTransportHandler`: remove `_connectionGen` generation counter
- [ ] All existing unit tests updated and pass
- [ ] All stream tests updated and pass
- [ ] Build succeeds with zero warnings

### TASK-030-008: Full Integration Test Verification + Cleanup

**Description:** As a developer, I want to verify the entire test suite passes with the new architecture and clean up obsolete code, comments, and documentation.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-030-007
**Successors:** none
**Parallel:** no — final verification

**Acceptance Criteria:**
- [ ] All H10 integration tests pass (0 failures, 3 consecutive runs)
- [ ] All H11 integration tests pass
- [ ] All H2 integration tests pass
- [ ] All unit tests pass (260+ in TurboHttp.Tests)
- [ ] All stream tests pass (TurboHttp.StreamTests)
- [ ] Remove obsolete deadlock workaround comments (DL-004, DL-005, DL-006 references)
- [ ] Update Obsidian vault: mark DL-009 and DL-010 as Fixed in `Architecture/10-DEADLOCK_ANALYSIS.md`
- [ ] Update `Architecture/07-HTTP10_RECONNECTION_LIMITATION.md` status to Resolved
- [ ] No new compiler warnings
- [ ] Performance: H10 benchmark within 5% of baseline

## Task Dependency Graph

```
TASK-030-001 (RetryBidi guard)  ──┐
TASK-030-002 (GroupByHostKey)   ──┼──→ TASK-030-004 (Regression tests)
TASK-030-003 (CacheBidi guard)  ──┘          │
                                             ▼
                                    TASK-030-005 (IConnectionScope)
                                             │
                                             ▼
                                    TASK-030-006 (ConnectionScopeStage)
                                             │
                                             ▼
                                    TASK-030-007 (Integration rewrite)
                                             │
                                             ▼
                                    TASK-030-008 (Verification + cleanup)
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-030-001 | ~30k | none | yes (with 002, 003) | -- |
| TASK-030-002 | ~40k | none | yes (with 001, 003) | -- |
| TASK-030-003 | ~25k | none | yes (with 001, 002) | -- |
| TASK-030-004 | ~35k | 001, 002, 003 | no | -- |
| TASK-030-005 | ~50k | 004 | no | opus |
| TASK-030-006 | ~80k | 005 | no | opus |
| TASK-030-007 | ~75k | 006 | no | opus |
| TASK-030-008 | ~40k | 007 | no | -- |

**Total estimated tokens:** ~375k

**Critical path:** 001+002+003 (parallel) → 004 → 005 → 006 → 007 → 008

## Functional Requirements

- FR-1: RetryBidiStage must not allow TryCompleteIfDone() to fire during an active retry transaction
- FR-2: GroupByHostKeyStage must not complete while any substream has `!IsDead` (WatchTask not yet completed)
- FR-3: CacheBidiStage must not pull from request inlet while `_pendingAsyncRead` is active
- FR-4: IConnectionScope.AcquireAsync() must return a valid ConnectionLease for every HTTP/1.0 request
- FR-5: IConnectionScope.ReturnAsync() must always close the connection for HTTP/1.0
- FR-6: PersistentConnectionScope must reuse connections when ConnectionReuseDecision.CanReuse is true
- FR-7: ConnectionScopeStage must handle upstream finish and downstream cancel by cleaning up the scope
- FR-8: BuildConnectionFlow() must produce the same wire-format output as the current implementation
- FR-9: All feature BidiStages (Retry, Cache, Redirect, Cookie, Compression) must remain unchanged
- FR-10: No public API changes (ITurboHttpClient, TurboClientOptions, ITurboHttpClientFactory)

## Non-Goals

- No HTTP/3 QUIC integration in ConnectionScope (HTTP/3 uses QuicTransportHandler separately)
- No connection prewarming or DNS refresh (tracked separately in Known Gaps)
- No changes to the Feature BidiStage stack ordering or composition
- No changes to the GroupByHostKey substreaming model (per-host pooling remains)
- No changes to ConnectionPool infrastructure (ConnectionPool, ConnectionLease, HostConnections)

## Technical Considerations

- **Akka.Streams actor semantics:** All GraphStage handlers are single-threaded (serialized by the actor mailbox). No locks needed, but operation ordering is critical.
- **Fused graph propagation:** `Push()` propagates synchronously through fused stages. Signal ordering (signal before response) is critical for HTTP/1.0 reconnection.
- **Source.Queue async boundary:** `GroupByHostKeyStage` uses `Source.Queue` which creates an async boundary. WatchTask callbacks run on different execution contexts than stage handlers.
- **GetAsyncCallback pattern:** All async operations in GraphStages must use `GetAsyncCallback` to ensure callbacks execute on the stage's actor thread.
- **TreatWarningsAsErrors:** Enabled globally — all code must compile with zero warnings.
- **Architecture update needed:** After TASK-030-007, update Obsidian vault notes:
  - `Architecture/14-TRANSPORT_LAYER.md` — add ConnectionScope section
  - `Architecture/15-STREAMS_LAYER.md` — update BuildConnectionFlow topology
  - `Architecture/10-DEADLOCK_ANALYSIS.md` — mark DL-009, DL-010 as Fixed

## Success Metrics

- 0 HTTP/1.0 integration test failures across 3 consecutive runs (currently 1-4 per run)
- DL-009 and DL-010 tests pass without Skip annotation
- No regressions in HTTP/1.1, HTTP/2 test suites
- Protocol-specific branches removed from ExtractOptionsStage (~20 lines), ConnectionReuseStage (~30 lines), TcpTransportHandler (generation counter)
- BuildConnectionFlow() reduced from ~50 lines of wiring to ~15 lines

## Open Questions

_All resolved during brainstorming session._
