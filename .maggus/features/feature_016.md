# Feature 016: Remove Duplicate Tracing from HandlerBidiStage

## Introduction

`HandlerBidiStage` contains tracing/diagnostics code in its "entry" path (`index == 0`) that duplicates what `TracingBidiStage` already handles as the outermost pipeline layer. This was introduced by TASK-012-003 ("Wire diagnostics into stages and transport") which added EventSource, DiagnosticListener, ActivitySource, and Metrics calls into HandlerBidiStage — without noticing that `TracingBidiStage` (from TASK-010-002) already manages ActivitySource spans and Metrics at the outermost position.

The result: every request gets traced **twice** for ActivitySource and Metrics, and the stage has an unnecessary `_isEntry` branch that splits it into two code paths.

### Architecture Context

- **Affected stages:**
  - `src/TurboHttp/Streams/Stages/Features/HandlerBidiStage.cs` — contains duplicate tracing in `_isEntry` branch
  - `src/TurboHttp/Streams/Stages/Features/TracingBidiStage.cs` — proper home for all request lifecycle tracing
- **Pipeline wiring:** `Engine.cs:149` creates HandlerBidiStages per handler, `Engine.cs:155` wires TracingBidiStage as outermost layer
- **Diagnostics infrastructure:** `src/TurboHttp/Diagnostics/` — TurboHttpInstrumentation, TurboHttpEventSource, TurboHttpDiagnosticListener, TurboHttpMetrics
- **Current duplication:**
  - ActivitySource spans: duplicated in both stages
  - Metrics (RequestCount, RequestDuration): duplicated in both stages
  - EventSource events (RequestStart/Stop/Failed): only in HandlerBidiStage — needs to move to TracingBidiStage
  - DiagnosticListener events (OnRequestStart/Stop/Failed): only in HandlerBidiStage — needs to move to TracingBidiStage

## Goals

- Eliminate duplicate tracing by removing all diagnostics code from HandlerBidiStage
- Consolidate EventSource + DiagnosticListener events into TracingBidiStage where they belong
- Simplify HandlerBidiStage to a uniform pass-through handler wrapper (no entry/non-entry split)

## Tasks

### TASK-016-001: Move EventSource + DiagnosticListener into TracingBidiStage
**Description:** As a developer, I want all request lifecycle events (EventSource, DiagnosticListener) emitted from TracingBidiStage so that tracing is consolidated in one place.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-016-002
**Parallel:** no — TASK-016-002 removes the same code from HandlerBidiStage

**Acceptance Criteria:**
- [x] TracingBidiStage request onPush emits `TurboHttpEventSource.Log.RequestStart(method, uri)` and `TurboHttpDiagnosticListener.OnRequestStart(request)`
- [x] TracingBidiStage response onPush emits `TurboHttpEventSource.Log.RequestStop(statusCode, durationMs)` and `TurboHttpDiagnosticListener.OnRequestStop(response, duration)`
- [x] TracingBidiStage response onUpstreamFailure emits `TurboHttpEventSource.Log.RequestFailed(...)` and `TurboHttpDiagnosticListener.OnRequestFailed(ex)` (alongside existing Activity error handling)
- [x] TracingBidiStage request onUpstreamFailure added: emits RequestFailed events for request-path failures
- [x] `using TurboHttp.Diagnostics;` already present (verify EventSource/DiagnosticListener types are accessible)
- [x] Build succeeds with 0 errors
- [x] All existing tests pass

### TASK-016-002: Simplify HandlerBidiStage — Remove Tracing and Entry/Non-Entry Split
**Description:** As a developer, I want HandlerBidiStage to be a pure pass-through handler wrapper so that it has a single code path with no diagnostics coupling.

**Token Estimate:** ~20k tokens
**Predecessors:** TASK-016-001
**Successors:** none
**Parallel:** no — depends on TASK-016-001

**Acceptance Criteria:**
- [ ] `_isEntry` field removed
- [ ] Entire `if (stage._isEntry)` branch removed (was lines 54–138)
- [ ] Only the non-entry pass-through logic remains — used for all handler instances regardless of index
- [ ] `RequestTimestampKey` field removed
- [ ] Unused usings removed: `System`, `System.Diagnostics`, `TurboHttp.Diagnostics`
- [ ] `index` parameter kept in constructor for unique port naming only
- [ ] Build succeeds with 0 errors
- [ ] All existing tests pass (HandlerBidiStageTests + full suite)

## Task Dependency Graph

```
TASK-016-001 ──→ TASK-016-002
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-016-001 | ~25k | none | no | — |
| TASK-016-002 | ~20k | 001 | no | — |

**Total estimated tokens:** ~45k

## Functional Requirements

- FR-1: TracingBidiStage must emit EventSource events (RequestStart, RequestStop, RequestFailed) for every request lifecycle
- FR-2: TracingBidiStage must emit DiagnosticListener events (OnRequestStart, OnRequestStop, OnRequestFailed) for every request lifecycle
- FR-3: TracingBidiStage must continue to emit ActivitySource spans and Metrics as before
- FR-4: HandlerBidiStage must not contain any tracing, metrics, or diagnostics code
- FR-5: HandlerBidiStage must behave identically for all handler indices (no entry/non-entry distinction)
- FR-6: No functional regression — all existing tests must pass

## Non-Goals

- No changes to other feature stages (RedirectBidiStage, RetryBidiStage, CacheBidiStage already emit their own child events correctly)
- No changes to the diagnostics infrastructure classes themselves (TurboHttpEventSource, TurboHttpDiagnosticListener, TurboHttpMetrics)
- No new test files — existing HandlerBidiStageTests and diagnostics tests cover the behavior

## Test Impact Analysis

- **`TurboHttp.Tests/Diagnostics/04_TurboHttpEventSourceTests.cs`** (31 tests) — Tests EventSource methods directly, not through stages. **No changes needed.**
- **`TurboHttp.Tests/Diagnostics/03_TurboHttpDiagnosticListenerTests.cs`** (16 tests) — Tests DiagnosticListener methods directly, not through stages. **No changes needed.**
- **`TurboHttp.Tests/Diagnostics/01_TurboHttpInstrumentationTests.cs`** — Tests ActivitySource methods directly. **No changes needed.**
- **`TurboHttp.StreamTests/Streams/20_HandlerBidiStageTests.cs`** (7 tests) — Tests handler delegation behavior (header injection, Atop composition, completion). Creates stages with `index == 0`. After simplification these use the uniform pass-through path, which is functionally equivalent for handler delegation. **No changes needed.**
- **Full suite (2100+ tests)** — Must pass unchanged as regression gate.

## Technical Considerations

- TracingBidiStage already has `RequestTimestampKey` and duration calculation — EventSource/DiagnosticListener calls plug in naturally alongside existing Metrics calls
- The `using System;` import may be needed in TracingBidiStage for `TimeSpan.FromMilliseconds` if not already present
- HandlerBidiStage tests (`20_HandlerBidiStageTests.cs`) create stages with `index == 0` — these will now use the simplified path, which is functionally equivalent for the handler delegation logic

## Success Metrics

- HandlerBidiStage reduced from ~168 lines to ~80 lines
- Zero duplicate tracing calls per request
- All 2100+ tests pass unchanged

## Open Questions

*None — scope and approach confirmed with user.*
