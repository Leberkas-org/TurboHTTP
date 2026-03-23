# Feature 019: Stream Never Dies — Enforce Immortal Pipeline Principle

## Introduction

The TurboHttp Akka.Streams pipeline must be immortal: the only legitimate reason for the stream to fail or complete is client disposal (via `queue.Complete()`). Individual errors — transport failures, protocol violations, corrupt data — must be absorbed gracefully without killing the pipeline.

An audit of the codebase identified three classes of violations:

1. **ConnectionStage** (`FailStage` on outbound write failure) — a single TCP write error kills the entire client stream
2. **TracingBidiStage** (`Fail(outResponse, ex)` on upstream failure) — as the outermost stage, this propagates any upstream failure to the sink, killing the stream
3. **Http30ConnectionStage** (3× `FailStage` on protocol errors) — HTTP/3 push rejection, SETTINGS errors, and GOAWAY errors all kill the stream

Additionally, three correlation stages (`Http1XCorrelationStage`, `Http20CorrelationStage`, `Http30CorrelationStage`) lack explicit `onUpstreamFailure` handlers, relying on Akka's default (stop stage = propagate failure).

This feature also blocks HTTP/3 from being routed at the version partition level, since the HTTP/3 pipeline is not yet production-ready and contains unresolved `FailStage` violations.

### Architecture Context

- **Principle source:** `StreamSurvivalTests` (17_StreamSurvivalTests.cs) already codifies this as SURV-001 through SURV-008
- **Components touched:** Transport layer (ConnectionStage), Features layer (TracingBidiStage), Decoding layer (Http30ConnectionStage), Routing layer (3 correlation stages, ProtocolCoreGraphBuilder)
- **Pattern:** All other stages already follow the "absorb and log" pattern — these are the remaining gaps

## Goals

- Eliminate all `FailStage` / `Fail(outlet, ex)` calls from stages that are reachable during normal operation
- Add explicit `onUpstreamFailure` absorption handlers to all correlation stages
- Block HTTP/3 at the version router level so HTTP/3 requests are rejected before entering the pipeline
- Ensure existing tests still pass (update tests that assert `FailStage` behavior)
- No new test files — only adjust or remove tests that validate the old fail-on-error behavior

## Tasks

### TASK-019-001: ConnectionStage — Replace FailStage with ConnectionReuseItem Signal + Error Response
**Description:** As a developer, I want the ConnectionStage to emit a `ConnectionReuseItem(Close)` signal and continue operating when an outbound write fails, so that a single TCP error does not kill the entire client stream.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** TASK-019-006
**Parallel:** yes — can run alongside TASK-019-002, TASK-019-003, TASK-019-004, TASK-019-005

**Acceptance Criteria:**
- [x] `_onOutboundWriteFailed` callback in `ConnectionStage.Logic` no longer calls `FailStage(ex)`
- [x] Instead, it emits a `ConnectionReuseItem` with `Close` decision (signaling the pool to tear down the connection) via `_pendingReads.Enqueue()` or `Push()`
- [x] After emitting the signal, the stage clears `_handle = null` (connection is dead) and calls `TryPull()` to accept the next `ConnectItem`
- [x] Test `CS-011` in `05_ConnectionStageTests.cs` is updated: instead of asserting `ThrowsAnyAsync<Exception>`, verify that a `ConnectionReuseItem` with `Close` is emitted and the stream does not fault
- [x] Full `TurboHttp.StreamTests` suite passes
- [x] Build succeeds with zero errors

**Files to modify:**
- `src/TurboHttp/Transport/ConnectionStage.cs`
- `src/TurboHttp.StreamTests/Streams/05_ConnectionStageTests.cs`

### TASK-019-002: TracingBidiStage — Absorb Upstream Failure Instead of Propagating
**Description:** As a developer, I want the TracingBidiStage to absorb upstream failures on the response path (log + stop activity), so that errors from inner stages do not propagate to the sink and kill the stream.

**Token Estimate:** ~15k tokens
**Predecessors:** none
**Successors:** TASK-019-006
**Parallel:** yes — can run alongside TASK-019-001, TASK-019-003, TASK-019-004, TASK-019-005

**Acceptance Criteria:**
- [x] `onUpstreamFailure` handler on `_inResponse` (line 101-111) changed from `Fail(stage._outResponse, ex)` to `Log.Warning("TracingBidiStage: Upstream failure absorbed: {0}", ex.Message)`
- [x] The activity error recording (`SetError` + `Stop`) is preserved before the absorption
- [x] No tests assert that TracingBidiStage propagates failures (check and remove if found)
- [x] Full `TurboHttp.StreamTests` suite passes
- [x] Build succeeds with zero errors

**Files to modify:**
- `src/TurboHttp/Streams/Stages/Features/TracingBidiStage.cs`

### TASK-019-003: Correlation Stages — Add Explicit onUpstreamFailure Absorption
**Description:** As a developer, I want all three correlation stages to explicitly absorb upstream failures with log warnings, so that they follow the same pattern as all other stages and don't rely on Akka's default stop behavior.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-019-006
**Parallel:** yes — can run alongside TASK-019-001, TASK-019-002, TASK-019-004, TASK-019-005

**Acceptance Criteria:**
- [x] `Http1XCorrelationStage`: all three inlets (`InRequest`, `InResponse`, `InReset`) have `onUpstreamFailure: ex => Log.Warning("Http1XCorrelationStage: Upstream failure absorbed: {0}", ex.Message)` handlers
- [x] `Http20CorrelationStage`: both inlets (`_inRequest`, `_inResponse`) have `onUpstreamFailure` absorption handlers
- [x] `Http30CorrelationStage`: both inlets have `onUpstreamFailure` absorption handlers
- [x] No existing tests break
- [x] Full `TurboHttp.StreamTests` suite passes
- [x] Build succeeds with zero errors

**Files to modify:**
- `src/TurboHttp/Streams/Stages/Routing/Http1XCorrelationStage.cs`
- `src/TurboHttp/Streams/Stages/Routing/Http20CorrelationStage.cs`
- `src/TurboHttp/Streams/Stages/Routing/Http30CorrelationStage.cs`

### TASK-019-004: Block HTTP/3 at Version Router
**Description:** As a developer, I want the version partition router in `ProtocolCoreGraphBuilder` to reject HTTP/3 requests with a `NotSupportedException`, so that HTTP/3 traffic never enters the unfinished HTTP/3 pipeline.

**Token Estimate:** ~30k tokens
**Predecessors:** none
**Successors:** TASK-019-006
**Parallel:** yes — can run alongside TASK-019-001, TASK-019-002, TASK-019-003, TASK-019-005

**Acceptance Criteria:**
- [x] `ProtocolCoreGraphBuilder.Router()` partition function: the `{ Major: 3, Minor: 0 } => 3` case is replaced with a `throw new NotSupportedException("HTTP/3 is not yet supported.")`
- [x] The `Merge<HttpResponseMessage>(4)` is reduced to `Merge<HttpResponseMessage>(3)` (3 outputs: HTTP/1.0, HTTP/1.1, HTTP/2)
- [x] The `http30` flow builder and its wiring to the merge are removed from `Build()`
- [x] The test-mode `CreateFlow` overload in `Engine.cs` that accepts `http30Factory` still compiles (parameter kept but unused, or removed if no callers need it)
- [x] `ProtocolCoreGraphBuilder.BuildProtocolFlow<Http30Engine>` call is removed
- [x] Tests in `TurboHttp.StreamTests/RFC9114/` that test `Http30ConnectionStage` in isolation (not through the router) still pass — they don't go through the partition
- [x] Tests in `TurboHttp.StreamTests/Streams/` that route HTTP/3 through the engine are updated or removed (e.g., version routing tests that expect HTTP/3 to work)
- [x] Full solution builds with zero errors
- [x] Full test suite passes

**Files to modify:**
- `src/TurboHttp/Streams/ProtocolCoreGraphBuilder.cs`
- `src/TurboHttp/Streams/Engine.cs` (if `http30Factory` parameter needs cleanup)
- `src/TurboHttp.StreamTests/Streams/10_EngineVersionRoutingTests.cs` (update/remove HTTP/3 routing tests)
- Any other test files that send `Version = HttpVersion.Version30` through the full engine

### TASK-019-005: Http30ConnectionStage — Replace FailStage with Log + Absorb (Defensive, Not Reachable After TASK-019-004)
**Description:** As a developer, I want the three `FailStage` calls in `Http30ConnectionStage` replaced with log-and-absorb patterns, so that if HTTP/3 is re-enabled in the future, it follows the immortal pipeline principle.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-019-006
**Parallel:** yes — can run alongside TASK-019-001, TASK-019-002, TASK-019-003, TASK-019-004

**Acceptance Criteria:**
- [x] `HandleServerFrame` — `Http3PushPromiseFrame` case (line 226-235): replace `FailStage(ex)` with `Log.Warning(...)` + `Pull(_stage._inServer)` (absorb the frame)
- [x] `HandleSettings` (line 296-300): replace `FailStage(ex)` with `Log.Warning(...)` + absorb (no further action needed since SETTINGS is control-level)
- [x] `HandleGoAway` (line 314-317): replace `FailStage(ex)` with `Log.Warning(...)` + set `_goAwayReceived = true` (the error case should still trigger GOAWAY semantics)
- [x] Tests in `12_Http30PushRejectionStageTests.cs`: `Should_FailStage_When_PushPromiseReceived` and `Should_FailStage_When_PushPromiseWithNonZeroPushIdReceived` are updated — instead of `Assert.ThrowsAsync<Http3Exception>`, verify that the frame is absorbed (no exception, no downstream output for that frame)
- [x] Full test suite passes
- [x] Build succeeds with zero errors

**Files to modify:**
- `src/TurboHttp/Streams/Stages/Decoding/Http30ConnectionStage.cs`
- `src/TurboHttp.StreamTests/RFC9114/12_Http30PushRejectionStageTests.cs`

### TASK-019-006: Verify Stream Survival End-to-End
**Description:** As a developer, I want to verify that the existing `StreamSurvivalTests` (SURV-001 through SURV-008) all pass after the changes, confirming the immortal pipeline principle holds end-to-end.

**Token Estimate:** ~10k tokens
**Predecessors:** TASK-019-001, TASK-019-002, TASK-019-003, TASK-019-004, TASK-019-005
**Successors:** none
**Parallel:** no — must run after all other tasks

**Acceptance Criteria:**
- [x] `dotnet test --filter "FullyQualifiedName~StreamSurvivalTests"` — all 8 SURV tests pass
- [x] `dotnet test ./src/TurboHttp.sln` — full solution: 0 failures
- [x] `dotnet build --configuration Release ./src/TurboHttp.sln` — 0 errors
- [x] No `FailStage` or `Fail(` calls remain in any stage under `Streams/Stages/` (grep verification)
- [x] The only `FailStage` usage in the entire `Streams/` + `Transport/` directories is gone (or explicitly documented as intentional)

**Files to modify:** none (verification only)

## Task Dependency Graph

```
TASK-019-001 (ConnectionStage) ────────────┐
TASK-019-002 (TracingBidiStage) ───────────┤
TASK-019-003 (Correlation Stages) ─────────┼──→ TASK-019-006 (Verify)
TASK-019-004 (Block HTTP/3 Router) ────────┤
TASK-019-005 (Http30ConnectionStage) ──────┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-019-001 | ~40k | none | yes (with 002–005) | — |
| TASK-019-002 | ~15k | none | yes (with 001, 003–005) | — |
| TASK-019-003 | ~25k | none | yes (with 001–002, 004–005) | — |
| TASK-019-004 | ~30k | none | yes (with 001–003, 005) | — |
| TASK-019-005 | ~25k | none | yes (with 001–004) | — |
| TASK-019-006 | ~10k | 001–005 | no | — |

**Total estimated tokens:** ~145k

## Functional Requirements

- FR-1: No stage in `Streams/Stages/` or `Transport/` may call `FailStage()` or `Fail(outlet, ex)` during normal operation
- FR-2: Transport-level write failures must emit a `ConnectionReuseItem(Close)` signal to trigger pool-level reconnection
- FR-3: All `onUpstreamFailure` handlers must absorb the failure with `Log.Warning` — never propagate
- FR-4: HTTP/3 requests (`HttpVersion.Version30`) must be rejected at the version router with `NotSupportedException` before entering the pipeline
- FR-5: The pipeline must only complete when `queue.Complete()` is called (client disposal)
- FR-6: Existing `StreamSurvivalTests` (SURV-001 through SURV-008) must continue to pass

## Non-Goals

- No new test files — only modify existing tests to match new behavior
- No changes to the HTTP/1.0, HTTP/1.1, or HTTP/2 encoding/decoding stages (they already absorb correctly)
- No changes to the feature BidiStages other than TracingBidiStage (they already absorb correctly)
- No supervision or restart strategy changes at the Akka level
- No changes to `TurboClientStreamManager` or `TurboHttpClient` disposal logic
- HTTP/3 is not being removed from the codebase — only blocked at the router; all HTTP/3 stages, encoders, decoders remain compilable and testable in isolation

## Technical Considerations

- **ConnectionStage write failure recovery:** After emitting `ConnectionReuseItem(Close)`, the stage must clear `_handle = null` and be ready to receive a new `ConnectItem` on the next request. This mirrors what happens when `_onInboundComplete` fires (connection closed by server).
- **TracingBidiStage absorption:** The activity error recording must happen *before* the absorption — the span should still show the error, even though the stream doesn't die.
- **Correlation stage absorption:** These stages maintain internal dictionaries (`_pending`, `_waiting`). On upstream failure, they should log and stop pulling from the failed inlet, but not complete the stage — the other inlet may still deliver data.
- **HTTP/3 router blocking:** The `Partition<HttpRequestMessage>` function throwing `NotSupportedException` for version 3.0 means the stream will fail if a user sends an HTTP/3 request. This is intentional — the user gets an immediate clear error rather than undefined behavior from an unfinished pipeline.
- **Test adjustments:** Tests that previously asserted `FailStage` / `ThrowsAsync<Exception>` (CS-011, PR-003, PR-004) need to assert the new absorb-and-signal behavior instead.

## Success Metrics

- Zero `FailStage` / `Fail(outlet, ex)` calls in any stage under `Streams/Stages/` and `Transport/`
- All existing tests pass (with the behavioral adjustments noted above)
- `StreamSurvivalTests` SURV-001 through SURV-008 remain green
- HTTP/3 requests are cleanly rejected at the router level

## Open Questions

*None — the violations are identified, the patterns are established by existing stages, and the fixes are mechanical.*
