<!-- maggus-id: baca5268-2d1b-422e-b801-4c8512dcd0df -->
# Feature 002: HTTP/3 Configuration Foundation

## Introduction

HTTP/3's `Http3Options` is an empty placeholder class, while HTTP/2 has 5 configurable properties that flow through `ProtocolCoreBuilder` into `Http20Engine`. Additionally, `ProtocolCoreBuilder` applies HTTP/2's concurrency limits to HTTP/3 requests via a `Major >= 2` check. This feature populates `Http3Options`, wires it through the build pipeline, and decouples HTTP/3 limits from HTTP/2.

### Architecture Context

- **Vision alignment:** Configuration parity across protocol versions is a prerequisite for HTTP/3 reaching production quality (currently 75/100 in ARCHITECTURE.md)
- **Components involved:** Client Layer (`Http3Options`, `TurboClientOptions`), Streams Layer (`ProtocolCoreBuilder`, `Http30Engine`, `Http30ConnectionStage`)
- **No new patterns:** Follows the exact pattern established by `Http2Options` → `ProtocolCoreBuilder` → `Http20Engine`
- **Architecture update needed:** Update ARCHITECTURE.md Implementation Status scores after completion

## Goals

- Populate `Http3Options` with QUIC/HTTP/3-equivalent configuration properties matching `Http2Options` coverage
- Wire `Http3Options` values through `ProtocolCoreBuilder` into `Http30Engine` and `Http30ConnectionStage`
- Decouple HTTP/3 concurrency and connection limits from HTTP/2 in `ProtocolCoreBuilder`
- Update ARCHITECTURE.md to reflect configuration parity

## Tasks

### TASK-002-001: Populate Http3Options with configuration properties
**Description:** As a library consumer, I want HTTP/3-specific configuration options so that I can tune QUIC connection behavior independently from HTTP/2.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-002-002, TASK-002-003
**Parallel:** yes — can run alongside nothing (foundation task)

**Acceptance Criteria:**
- [ ] `Http3Options` has these properties with XML doc comments:
  - `MaxConnectionsPerServer` (int, default 4) — fewer than H2's 6 since QUIC multiplexes better
  - `QpackMaxTableCapacity` (int, default 4096) — QPACK dynamic table size
  - `QpackBlockedStreams` (int, default 100) — max streams blocked on encoder instructions
  - `MaxFieldSectionSize` (int, default 65536) — HTTP/3 equivalent of header size limits
  - `IdleTimeout` (TimeSpan, default 30s) — QUIC idle timeout
  - `MaxReconnectAttempts` (int, default 3) — reconnect limit for QUIC connection drops
- [ ] Property style matches `Http2Options` (public getters/setters, summary comments referencing relevant RFCs)
- [ ] `TurboClientOptions.Http3` property already exists and returns `Http3Options` — verify it works with new properties
- [ ] Unit test validates defaults match documented values
- [ ] Typecheck passes: `dotnet build --configuration Release ./src/TurboHTTP.sln`

**Files to modify:**
- `src/TurboHTTP/Http3Options.cs`

**Reference files:**
- `src/TurboHTTP/Http2Options.cs` (pattern to follow)

---

### TASK-002-002: Wire Http3Options through ProtocolCoreBuilder and Http30Engine
**Description:** As the streams layer, I want Http3Options values passed into Http30Engine so that QPACK table capacity and idle timeout are configurable at runtime.

**Token Estimate:** ~35k tokens
**Predecessors:** TASK-002-001
**Successors:** TASK-002-004
**Parallel:** yes — can run alongside TASK-002-003

**Acceptance Criteria:**
- [ ] `ProtocolCoreBuilder.Build()` extracts `clientOptions.Http3.QpackMaxTableCapacity` and `clientOptions.Http3.IdleTimeout`
- [ ] `Http30Engine` constructor signature updated: `Http30Engine(int maxTableCapacity, TimeSpan idleTimeout)`
- [ ] `Http30Engine` passes `idleTimeout` to `Http30ConnectionStage` constructor
- [ ] `ProtocolCoreBuilder` line `new Http30Engine()` changed to `new Http30Engine(clientOptions.Http3.QpackMaxTableCapacity, clientOptions.Http3.IdleTimeout)`
- [ ] Default parameterless `Http30Engine()` constructor preserved for backward compatibility (delegates to new constructor with defaults)
- [ ] All existing tests still pass unchanged
- [ ] Typecheck passes

**Files to modify:**
- `src/TurboHTTP/Streams/ProtocolCoreBuilder.cs` (line 63)
- `src/TurboHTTP/Streams/Http30Engine.cs` (constructor + field)

**Reference files:**
- `src/TurboHTTP/Streams/Http20Engine.cs` (pattern to follow — takes `initialWindowSize` + `maxConcurrentStreams`)

---

### TASK-002-003: Decouple HTTP/3 concurrency limits from HTTP/2
**Description:** As a library consumer, I want HTTP/3 to use its own connection and concurrency limits so that tuning HTTP/2 doesn't inadvertently affect HTTP/3 behavior.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-002-001
**Successors:** TASK-002-004
**Parallel:** yes — can run alongside TASK-002-002

**Acceptance Criteria:**
- [ ] `ProtocolCoreBuilder.Build()` extracts `maxConnsH3` from `clientOptions.Http3.MaxConnectionsPerServer`
- [ ] `MaxSubstreamsPerKey()` has separate branch: `endpoint.Version.Major == 3 ? maxConnsH3`
- [ ] `MaxConcurrencyPerSlot()` has separate branch: `endpoint.Version.Major == 3 ? int.MaxValue` (QUIC handles stream limits at transport level)
- [ ] Existing `Major >= 2` changed to `Major == 2` for HTTP/2-only path
- [ ] Unit test: H2 endpoint uses H2 limits, H3 endpoint uses H3 limits
- [ ] All existing tests still pass
- [ ] Typecheck passes

**Files to modify:**
- `src/TurboHTTP/Streams/ProtocolCoreBuilder.cs` (lines 28-50)

---

### TASK-002-004: Update ARCHITECTURE.md Implementation Status
**Description:** As a maintainer, I want ARCHITECTURE.md to reflect the current HTTP/3 configuration state so that the documentation stays accurate.

**Token Estimate:** ~10k tokens
**Predecessors:** TASK-002-002, TASK-002-003
**Successors:** none
**Parallel:** no
**Model:** haiku

**Acceptance Criteria:**
- [ ] ARCHITECTURE.md "Implementation Status" table: HTTP/3 score updated (was 75/100)
- [ ] "Open gaps" list: remove "QUIC transport" if now wired, update QPACK status
- [ ] `TurboClientOptions` description mentions `Http3Options` alongside `Http2Options`

**Files to modify:**
- `ARCHITECTURE.md` (lines 318-331)

## Task Dependency Graph

```
TASK-002-001 ──→ TASK-002-002 ──→ TASK-002-004
             └──→ TASK-002-003 ──┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-002-001 | ~25k | none | — | — |
| TASK-002-002 | ~35k | 001 | yes (with 003) | — |
| TASK-002-003 | ~30k | 001 | yes (with 002) | — |
| TASK-002-004 | ~10k | 002, 003 | no | haiku |

**Total estimated tokens:** ~100k

## Functional Requirements

- FR-1: `Http3Options` must expose `MaxConnectionsPerServer`, `QpackMaxTableCapacity`, `QpackBlockedStreams`, `MaxFieldSectionSize`, `IdleTimeout`, `MaxReconnectAttempts` with sensible defaults
- FR-2: `ProtocolCoreBuilder` must pass `Http3Options` values to `Http30Engine` construction
- FR-3: HTTP/3 endpoint substreams must use `Http3Options.MaxConnectionsPerServer` (not HTTP/2's value)
- FR-4: HTTP/3 concurrency per slot must be independent of `Http2Options.MaxConcurrentStreams`

## Non-Goals

- No reconnection logic implementation (Feature 003)
- No StateMachine extraction (Feature 003)
- No new test files beyond unit tests for options/builder (Feature 004)
- No changes to QUIC transport layer

## Technical Considerations

- `Http30ConnectionStage` already accepts `idleTimeout` in constructor — just needs wiring from options
- `Http30Engine` already accepts `maxTableCapacity` — just needs the second parameter added
- `ProtocolCoreBuilder` change from `>= 2` to `== 2` is a semantic fix, not just a refactor — test thoroughly

## Success Metrics

- All 6 `Http3Options` properties are configurable and flow through to the engine/stage
- HTTP/3 and HTTP/2 connection limits are fully independent
- Zero regressions in existing test suites

## Open Questions

None — all questions resolved.
