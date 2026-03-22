# Feature 010: Fix HTTP/3 Control Stream Ordering, Simplify Correlation Stage, Clean Up Dead Code

## Introduction

After removing `Http30StreamIdAllocatorStage` from `Http30Engine`, three issues remain:

1. **Correlation stage mismatch**: `Http30Engine` currently uses `ZipWith` for FIFO request-response pairing, but `Http30CorrelationStage` should be used instead. The stage needs to be simplified from stream-ID-based dictionary matching to FIFO queue-based correlation, since HTTP/3 frames carry no stream ID (QUIC handles multiplexing natively).

2. **Control stream ordering violation**: `Http30ControlStreamPrefaceStage` emits SETTINGS only on downstream Pull, violating RFC 9114 §6.2.1 which requires the control stream + SETTINGS frame to be sent BEFORE any request stream. The `Http30StreamDemuxStage` has 3 outlets with independent backpressure — if `OutRequest` pulls first, the request opens before the control stream is established, causing the server to reject/ignore requests (manifesting as a 30-second `TaskCanceledException` in the HTTP/3 smoke test).

3. **Dead code**: `Http30StreamIdAllocatorStage` (plus its test file) is no longer referenced anywhere in the codebase.

### Architecture Context

- **Components involved**: `Http30CorrelationStage` (Routing), `Http30ControlStreamPrefaceStage` (Encoding), `Http30StreamDemuxStage` (Routing), `Http30Engine` (Streams)
- **Vision alignment**: Enables HTTP/3 end-to-end functionality — a core protocol in TurboHttp's multi-protocol support
- **No new components**: This simplifies and fixes existing architecture

## Goals

- Replace `ZipWith` in `Http30Engine` with a simplified `Http30CorrelationStage` (FIFO, no stream IDs)
- Fix RFC 9114 §6.2.1 compliance: control stream + SETTINGS emitted before any request stream frames
- HTTP/3 smoke test (`Http3SmokeTests.Get_Hello_Returns_200_HelloWorld`) passes
- Remove dead code left from the StreamIdAllocator removal
- Zero build warnings, zero test regressions

## Tasks

### TASK-010-001: Simplify Http30CorrelationStage to FIFO (No Stream IDs)
**Description:** As a developer, I want `Http30CorrelationStage` to perform FIFO request-response correlation without stream IDs, so that it can replace `ZipWith` in `Http30Engine` while providing proper stage semantics (independent inlet pulling, upstream finish handling).

**Token Estimate:** ~35k tokens
**Predecessors:** none
**Successors:** TASK-010-002
**Parallel:** yes — can run alongside TASK-010-003, TASK-010-004

**Acceptance Criteria:**
- [x] `Http30CorrelationStage` shape changed to `FanInShape<HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>`
- [x] Inlet types changed: `_inRequest` accepts `HttpRequestMessage`, `_inResponse` accepts `HttpResponseMessage` (no tuples)
- [x] Correlation logic changed from dictionary-based stream ID matching to FIFO queue matching
- [x] `response.RequestMessage = request` assignment happens inside the stage
- [x] Port string names updated if needed to reflect the simplified shape (keep `Http30Correlation.In.Request`, `Http30Correlation.In.Response`, `Http30Correlation.Out`)
- [x] Tests in `13_Http30CorrelationStageTests.cs` updated to match new shape (no stream ID tuples)
- [x] `dotnet build --configuration Release src/TurboHttp.sln` — 0 errors, 0 warnings

**Implementation Notes:**
- Current shape: `FanInShape<(HttpRequestMessage, long), (HttpResponseMessage, long), HttpResponseMessage>` with `Dictionary<long, T>` correlation
- New shape: `FanInShape<HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>` with `Queue<HttpRequestMessage>` FIFO
- The stage already handles independent upstream finish correctly — preserve that logic
- Remove the `_pending` and `_waiting` dictionaries, replace with a single `Queue<HttpRequestMessage>`

### TASK-010-002: Wire Http30CorrelationStage into Http30Engine (Replace ZipWith)
**Description:** As a developer, I want `Http30Engine` to use the simplified `Http30CorrelationStage` instead of `ZipWith` for request-response pairing, restoring the proper stage-based correlation pattern.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-010-001
**Successors:** TASK-010-005
**Parallel:** no — requires simplified correlation stage

**Acceptance Criteria:**
- [ ] `ZipWith.Apply<HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>(...)` replaced with `Http30CorrelationStage`
- [ ] `Broadcast<HttpRequestMessage>(2)` output 0 → `requestToFrame.In`, output 1 → `correlation._inRequest`
- [ ] `streamDecoder.Outlet` → `correlation._inResponse`
- [ ] `correlation._out` → BidiShape output (replacing `zip.Out`)
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` — 0 errors, 0 warnings
- [ ] `dotnet test src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj --filter "FullyQualifiedName~RFC9114"` — all pass

### TASK-010-003: Fix Http30ControlStreamPrefaceStage to Eagerly Emit SETTINGS
**Description:** As a developer, I want `Http30ControlStreamPrefaceStage` to emit the control stream preface eagerly (on `PreStart`) so that the SETTINGS frame is always sent before any request stream frames, satisfying RFC 9114 §6.2.1.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** TASK-010-005
**Parallel:** yes — can run alongside TASK-010-001, TASK-010-004
**Model:** opus — subtle Akka.Streams backpressure semantics

**Acceptance Criteria:**
- [ ] `Http30ControlStreamPrefaceStage` emits the control stream preface in `PreStart` using `Emit()` or equivalent pattern (not waiting for Pull)
- [ ] First downstream Pull still triggers `Pull(in)` to begin passing upstream items
- [ ] Existing 9 tests in `14_Http30ControlStreamPrefaceStageTests.cs` pass
- [ ] Tests in `04_Http30ConnectionStageTests.cs` and `12_Http30PushRejectionStageTests.cs` still pass (they reference this stage)
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` — 0 errors, 0 warnings

**Implementation Notes:**
- Current bug: the `onPull` handler emits preface on first Pull, then returns without pulling upstream. This means the preface only flows when downstream demands — but `Http30StreamDemuxStage` has 3 independent outlets, and `OutRequest` may pull before `OutControl`.
- Fix approach: Override `PreStart()` to call `Emit(_out, prefaceItem)`. The `Emit` helper queues the element and delivers it on the next downstream Pull, ensuring the preface is always the first item through the stage regardless of pull order.
- Alternative: Use `GetAsyncCallback` + `scheduleOnce` to emit on materialisation, but `Emit()` in `PreStart` is simpler and idiomatic for Akka.Streams.

### TASK-010-004: Delete Dead Code (StreamIdAllocator Stage + Tests)
**Description:** As a developer, I want to remove unused `Http30StreamIdAllocatorStage` and its test file so that the codebase has no dead code from the completed StreamIdAllocator removal.

**Token Estimate:** ~10k tokens
**Predecessors:** none
**Successors:** TASK-010-005
**Parallel:** yes — can run alongside TASK-010-001, TASK-010-003
**Model:** haiku — simple file deletion

**Acceptance Criteria:**
- [ ] `src/TurboHttp/Streams/Stages/Routing/Http30StreamIdAllocatorStage.cs` deleted
- [ ] `src/TurboHttp.StreamTests/RFC9114/06_Http30StreamIdAllocatorStageTests.cs` deleted
- [ ] No remaining references to `Http30StreamIdAllocatorStage` in any `.cs` file
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` — 0 errors, 0 warnings

**Files to delete (2 files, ~160 lines total):**
| File | Lines | Tests |
|------|-------|-------|
| `Http30StreamIdAllocatorStage.cs` | 61 | — |
| `06_Http30StreamIdAllocatorStageTests.cs` | ~100 | 7 |

### TASK-010-005: Validate HTTP/3 Smoke Test Passes
**Description:** As a developer, I want to verify the HTTP/3 integration smoke test passes end-to-end after the control stream fix and correlation stage changes, confirming the full pipeline (client → QUIC → Kestrel → response) works correctly.

**Token Estimate:** ~15k tokens
**Predecessors:** TASK-010-001, TASK-010-002, TASK-010-003, TASK-010-004
**Successors:** none
**Parallel:** no — requires all predecessors complete

**Acceptance Criteria:**
- [ ] `dotnet test src/TurboHttp.IntegrationTests/TurboHttp.IntegrationTests.csproj --filter "FullyQualifiedName~Http3SmokeTests"` — passes (no `TaskCanceledException`)
- [ ] `dotnet test src/TurboHttp.sln` — full solution test suite green
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` — 0 errors, 0 warnings
- [ ] If smoke test still fails: document the next failure point and root cause for follow-up

## Task Dependency Graph

```
TASK-010-001 ──→ TASK-010-002 ──→ TASK-010-005
TASK-010-003 ────────────────────┘
TASK-010-004 ────────────────────┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-010-001 | ~35k | none | yes (with 003, 004) | — |
| TASK-010-002 | ~25k | 001 | no | — |
| TASK-010-003 | ~40k | none | yes (with 001, 004) | opus |
| TASK-010-004 | ~10k | none | yes (with 001, 003) | haiku |
| TASK-010-005 | ~15k | 001, 002, 003, 004 | no | — |

**Total estimated tokens:** ~125k

## Functional Requirements

- FR-1: `Http30CorrelationStage` must perform FIFO request-response correlation without stream IDs
- FR-2: `Http30CorrelationStage` must set `response.RequestMessage = request` for each correlated pair
- FR-3: `Http30Engine` must use `Http30CorrelationStage` (not `ZipWith`) for request-response pairing
- FR-4: The HTTP/3 control stream preface (stream type 0x00 + SETTINGS frame) MUST be emitted before any request stream frames leave the pipeline
- FR-5: The control stream preface emission must not block or delay subsequent request frame processing
- FR-6: All unused stage files from the StreamIdAllocator removal must be deleted with zero remaining references
- FR-7: The HTTP/3 smoke test must complete successfully without timeout

## Non-Goals

- Not changing `Http30StreamDemuxStage` outlet ordering or backpressure strategy
- Not adding new HTTP/3 integration test coverage beyond verifying the existing smoke test
- Not implementing QPACK encoder instruction stream preface (already handled by `Http30QpackEncoderPrefaceStage`)
- Not modifying the `Http30Engine` graph topology beyond swapping ZipWith → CorrelationStage

## Technical Considerations

- **FIFO correlation correctness**: HTTP/3 over QUIC delivers responses in request order on each bidirectional stream. Since TurboHttp opens one request stream per request and processes them sequentially through the engine, FIFO ordering is guaranteed. The correlation stage's queue-based approach is correct for this model.
- **Akka.Streams `Emit()` in `PreStart`**: The `Emit()` helper in `GraphStageLogic` buffers one element and delivers it on the next downstream demand signal. Calling it in `PreStart()` is safe and idiomatic — it does not violate backpressure because it only queues; actual delivery waits for Pull.
- **`Http30StreamDemuxStage` interaction**: The demux stage partitions `IOutputItem` into 3 outlets (`OutRequest`, `OutControl`, `OutEncoder`). With eager preface emission, the control stream tagged item is guaranteed to be the first element seen by the demux, regardless of which outlet pulls first.
- **Test file renumbering**: After deleting file `06_`, the remaining RFC9114 test files will have a gap in numbering (05, 07, 08, ...). This is acceptable — renumbering is out of scope to minimize churn.

## Success Metrics

- HTTP/3 smoke test passes in < 5 seconds (was timing out at 30s)
- `Http30CorrelationStage` wired into `Http30Engine` replacing `ZipWith`
- Zero dead code files related to StreamIdAllocator remain
- Full solution builds and tests green: 0 errors, 0 warnings, 0 test failures

## Open Questions

None — all design decisions are clear from the analysis and user direction.
