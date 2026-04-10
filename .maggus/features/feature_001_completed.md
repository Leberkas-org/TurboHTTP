<!-- maggus-id: 2e45907b-8abc-4c1b-90d0-a0c130f90e8b -->
# Feature 001: Unified HTTP/1.x ConnectionStages + HTTP/3 Consolidation Prep

## Introduction

Consolidate HTTP/1.0 and HTTP/1.1 from 3 separate GraphStages each (encoder + decoder + correlation) into a single unified `ConnectionStage` per protocol version, matching the existing HTTP/2 `Http20ConnectionStage` pattern. Also produce a design note for future HTTP/3 consolidation.

### Architecture Context

- **Vision alignment:** Reduces architectural inconsistency across protocol versions; simplifies engine wiring from complex GraphDsl (Broadcast + MergePreferred + 3 stages) to trivial single-stage wrapping
- **Components involved:** Streams Layer (engines + stages), Protocol Layer (new StateMachine classes for Http10/Http11)
- **Existing pattern to follow:** `Http20ConnectionStage` + `Http2.StateMachine` + `IHttp2StageOperations` (in `src/TurboHTTP/Streams/Stages/Decoding/Http20ConnectionStage.cs` and `src/TurboHTTP/Protocol/Http2/StateMachine.cs`)
- **Reused as-is (no modification):** `Http10Encoder`, `Http10Decoder`, `Http11Encoder`, `Http11Decoder`, `ConnectionReuseEvaluator`
- **No new patterns:** Uses existing EmitMultiple buffering, callback interface, and custom Shape patterns

## Goals

- Consolidate HTTP/1.0 into a single `Http10ConnectionStage` (currently 3 stages: `Http10EncoderStage`, `Http10DecoderStage`, `Http10CorrelationStage`)
- Consolidate HTTP/1.1 into a single `Http11ConnectionStage` (currently 3 stages: `Http11EncoderStage`, `Http11DecoderStage`, `Http11CorrelationStage`)
- Maintain identical `BidiFlow` signatures from `Http10Engine.CreateFlow()` and `Http11Engine.CreateFlow()` — no upstream breaking changes
- Preserve all protocol behavior: pipelining, connection reuse evaluation, orphaned request retry, CloseSignal handling
- Delete the 6 old stage files + shared `Http1XCorrelationShape`
- Create HTTP/3 consolidation design note for future work

## Tasks

### TASK-001-001: Create Http10ConnectionStage + Http10StateMachine
**Description:** As a developer, I want HTTP/1.0 to use a single unified ConnectionStage so that its architecture matches HTTP/2 and is simpler to maintain.

**Token Estimate:** ~75k tokens
**Predecessors:** none
**Successors:** TASK-001-003
**Parallel:** yes — can run alongside TASK-001-002
**Model:** opus

**Files to create:**
- `src/TurboHTTP/Protocol/Http10/Http10StateMachine.cs` — Protocol logic: encode requests via `Http10Encoder`, decode responses via `Http10Decoder`, emit control signals (`StreamAcquireItem`, `ConnectionReuseItem`, `PipelineRetryItem`), handle `CloseSignalItem`
- `src/TurboHTTP/Streams/Stages/Decoding/Http10ConnectionStage.cs` — Unified stage with `Http10ConnectionShape` (inline), 4 ports: `Http10Connection.In.Server`, `Http10Connection.In.App`, `Http10Connection.Out.Response`, `Http10Connection.Out.Network`

**Files to modify:**
- `src/TurboHTTP/Streams/Http10Engine.cs` — Replace 3-stage GraphDsl with single `Http10ConnectionStage` + `BatchWeighted` on OutNetwork

**Files to delete (after tests pass):**
- `src/TurboHTTP/Streams/Stages/Encoding/Http10EncoderStage.cs`
- `src/TurboHTTP/Streams/Stages/Decoding/Http10DecoderStage.cs`
- `src/TurboHTTP/Streams/Stages/Routing/Http10CorrelationStage.cs`

**Key implementation details:**
- StateMachine callback interface: `IHttp10StageOperations { OnResponse(), OnOutbound(), OnWarning() }`
- No pipelining — single `_inFlightRequest` field (not a queue)
- HTTP/1.0 default: `Connection: close` (RFC 1945)
- Handle CloseSignalItem for EOF-delimited body (`TryDecodeEof`)
- Handle AbruptClose → `FailStage` with Content-Length mismatch message
- Orphaned request on upstream finish → emit `PipelineRetryItem`
- Stage Logic pattern: `_pendingOutbound` + `_pendingResponses` lists, flushed via `EmitMultiple`

**Acceptance Criteria:**
- [x] Http10ConnectionStage compiles with correct port naming convention
- [x] Http10StateMachine encodes requests and decodes responses correctly
- [x] Control signals (StreamAcquireItem, ConnectionReuseItem) emitted through OutNetwork
- [x] CloseSignalItem and AbruptClose handled (EOF-delimited body)
- [x] Orphaned request emits PipelineRetryItem on upstream finish/failure
- [x] Http10Engine.CreateFlow() returns same BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed> signature
- [x] All existing Http10 stream tests pass unchanged: `dotnet run --project TurboHTTP.StreamTests/TurboHTTP.StreamTests.csproj -- -namespace "TurboHTTP.StreamTests.Http10"`
- [x] Old encoder/decoder/correlation stage files deleted
- [x] Typecheck/build passes: `dotnet build --configuration Release ./src/TurboHTTP.sln`

### TASK-001-002: Create Http11ConnectionStage + Http11StateMachine
**Description:** As a developer, I want HTTP/1.1 to use a single unified ConnectionStage so that its architecture matches HTTP/2 and pipelining logic is centralized in a testable StateMachine.

**Token Estimate:** ~100k tokens
**Predecessors:** none
**Successors:** TASK-001-003
**Parallel:** yes — can run alongside TASK-001-001
**Model:** opus

**Files to create:**
- `src/TurboHTTP/Protocol/Http11/Http11StateMachine.cs` — Protocol logic with pipelining: encode via `Http11Encoder`, decode via `Http11Decoder`, manage `Queue<HttpRequestMessage>` up to `_effectivePipelineDepth`, evaluate connection reuse via `ConnectionReuseEvaluator`, handle orphaned pipelined requests
- `src/TurboHTTP/Streams/Stages/Decoding/Http11ConnectionStage.cs` — Unified stage with `Http11ConnectionShape` (inline), 4 ports: `Http11Connection.In.Server`, `Http11Connection.In.App`, `Http11Connection.Out.Response`, `Http11Connection.Out.Network`, constructor accepts `int maxPipelineDepth = 8`

**Files to modify:**
- `src/TurboHTTP/Streams/Http11Engine.cs` — Replace 3-stage GraphDsl with single `Http11ConnectionStage` + `BatchWeighted` on OutNetwork. Keep `MaxBatchWeight`, `BatchConsolidate` (reused by Http10Engine)

**Files to delete (after tests pass):**
- `src/TurboHTTP/Streams/Stages/Encoding/Http11EncoderStage.cs`
- `src/TurboHTTP/Streams/Stages/Decoding/Http11DecoderStage.cs`
- `src/TurboHTTP/Streams/Stages/Routing/Http11CorrelationStage.cs`

**Key implementation details:**
- StateMachine callback interface: `IHttp11StageOperations { OnResponse(), OnOutbound(), OnWarning() }`
- Pipelining: `Queue<HttpRequestMessage> _inFlightQueue`, `int _effectivePipelineDepth` (starts at maxPipelineDepth)
- `EncodeRequest()`: enqueue request → encode → emit `StreamAcquireItem` + `NetworkBuffer`
- `DecodeServerData()`: decode → dequeue → correlate → `ConnectionReuseEvaluator.Evaluate()` → emit `ConnectionReuseItem` + response
- `Connection: close` header → reduce `_effectivePipelineDepth` to 1, log warning if pipelined requests were in-flight
- Orphaned requests on upstream finish → emit `PipelineRetryItem` for each remaining queued request
- `CanAcceptRequest` → `_inFlightQueue.Count < _effectivePipelineDepth`
- Stage Logic: TryPullRequest checks `_sm.CanAcceptRequest` (matches Http20ConnectionStage pattern)

**Acceptance Criteria:**
- [x] Http11ConnectionStage compiles with correct port naming convention
- [x] Http11StateMachine handles pipelining (queue up to maxPipelineDepth)
- [x] Connection: close reduces effective pipeline depth to 1
- [x] Orphaned pipelined requests emit PipelineRetryItem
- [x] ConnectionReuseEvaluator.Evaluate() called correctly for each response
- [x] Http11Engine.CreateFlow() returns same BidiFlow signature
- [x] All existing Http11 stream tests pass unchanged: `dotnet run --project TurboHTTP.StreamTests/TurboHTTP.StreamTests.csproj -- -namespace "TurboHTTP.StreamTests.Http11"`
- [x] Old encoder/decoder/correlation stage files deleted
- [x] Typecheck/build passes

### TASK-001-003: Test Migration + Cleanup + Architecture Update
**Description:** As a developer, I want tests that directly referenced old stages migrated to test the new unified stages, and all dead code removed.

**Token Estimate:** ~60k tokens
**Predecessors:** TASK-001-001, TASK-001-002
**Successors:** TASK-001-004
**Parallel:** no

**Test files to migrate or delete:**
- `src/TurboHTTP.StreamTests/Http11/Http11CorrelationStageSpec.cs` — migrate to test Http11ConnectionStage or delete if covered by E2E
- `src/TurboHTTP.StreamTests/Http11/Http11ConnectionReuseStageSpec.cs` — migrate or delete
- `src/TurboHTTP.StreamTests/Http11/Http1XCorrelationBackPressureSpec.cs` — migrate to unified stage backpressure tests
- `src/TurboHTTP.StreamTests/Http11/Http1XCorrelationPipelineSpec.cs` — migrate to unified stage pipelining tests

**Test files to create:**
- `src/TurboHTTP.StreamTests/Http10/Http10ConnectionStageSpec.cs` — unit tests for unified Http10 stage
- `src/TurboHTTP.StreamTests/Http11/Http11ConnectionStageSpec.cs` — unit tests for unified Http11 stage (pipelining, backpressure, orphan retry)

**Files to update:**
- `ARCHITECTURE.md` — update Per-Version Engine Assembly table (HTTP/1.0 and HTTP/1.1 rows)

**Cleanup verification:**
- Grep for `Http10EncoderStage`, `Http10DecoderStage`, `Http10CorrelationStage`, `Http11EncoderStage`, `Http11DecoderStage`, `Http11CorrelationStage`, `Http1XCorrelationShape` — zero hits
- Run `stage-port-validator` agent
- Run `spec-naming-validator` agent on new test files

**Acceptance Criteria:**
- [x] All migrated tests pass
- [x] No references to old stage classes remain in codebase (grep confirms zero hits)
- [x] Http1XCorrelationShape class removed
- [x] New ConnectionStage specs follow post-Feature-040 naming conventions (sealed class, Spec suffix, BDD method names, [Trait("RFC", ...)])
- [x] ARCHITECTURE.md Per-Version Engine Assembly table updated
- [x] Full build + all test projects pass
- [x] stage-port-validator reports no violations for new stages (pre-existing "Network" role gap consistent with Http20ConnectionStage)
- [x] spec-naming-validator reports no violations for new test files

### TASK-001-004: HTTP/3 Consolidation Design Note
**Description:** As a developer, I want a design note analyzing HTTP/3's 11-stage structure and proposing a consolidation path, so future work is well-informed.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-001-001, TASK-001-002
**Successors:** none
**Parallel:** no — benefit from lessons learned in TASK-001/002
**Model:** sonnet

**Create:** Obsidian note via MCP at `Architecture/Design/HTTP3_CONSOLIDATION_PLAN`

**Content to analyze:**
- Current Http30Engine stages: `Http30ConnectionStage`, `Http30StreamStage`, `Http30DecoderStage`, `Http30EncoderStage`, `Http30CorrelationStage`, `Http30Request2FrameStage`, `Http30ControlStreamPrefaceStage`, `Http30QpackEncoderPrefaceStage`, `QpackDecoderStreamStage`, `QpackDecoderFeedbackStage`, plus Partition/Merge for QPACK routing
- Which stages can be consolidated (stream-level encoder+decoder+correlation → unified stream stage)
- Which must stay separate (QPACK bidirectional feedback, control stream preface)
- Proposed target architecture (~4-5 stages)
- Blockers, risks, and estimated effort

**Acceptance Criteria:**
- [x] Design note created in Obsidian vault
- [x] Current 11-stage structure documented with purpose of each stage
- [x] Consolidation targets identified with rationale
- [x] Stages that must remain separate documented with reasoning
- [x] Blockers and risks listed
- [x] No code changes

## Task Dependency Graph

```
TASK-001-001 (Http10) ──┐
                        ├──→ TASK-001-003 (Tests + Cleanup) ──→ TASK-001-004 (Http3 Design)
TASK-001-002 (Http11) ──┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-001-001 | ~75k | none | yes (with 002) | opus |
| TASK-001-002 | ~100k | none | yes (with 001) | opus |
| TASK-001-003 | ~60k | 001, 002 | no | — |
| TASK-001-004 | ~25k | 001, 002 | no | sonnet |

**Total estimated tokens:** ~260k

## Functional Requirements

- FR-1: `Http10Engine.CreateFlow()` must return the same `BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed>` type — no upstream API change
- FR-2: `Http11Engine.CreateFlow()` must return the same BidiFlow type — no upstream API change
- FR-3: HTTP/1.0 must emit `StreamAcquireItem` before request data on OutNetwork, and `ConnectionReuseItem.Close` after response
- FR-4: HTTP/1.1 must support pipelining up to configurable depth (default 8)
- FR-5: HTTP/1.1 must reduce pipeline depth to 1 when `Connection: close` header received
- FR-6: Both protocols must emit `PipelineRetryItem` for orphaned requests when connection closes unexpectedly
- FR-7: HTTP/1.0 must handle CloseSignalItem for EOF-delimited body (RFC 1945 section 7.2.2)
- FR-8: BatchWeighted consolidation must remain external in the engine (not internalized in stage)
- FR-9: All existing stream tests, unit tests, and integration tests must continue to pass

## Non-Goals (Out of Scope)

- No shared `HttpConnectionShape` base class across protocols (each protocol defines its own inline)
- No shared `IHttpStageOperations` interface (each protocol defines its own)
- No HTTP/3 code changes (design note only)
- No performance optimization beyond matching current behavior
- No changes to Transport layer, Client layer, or Feature BidiFlow chain
- No changes to Protocol-layer encoders/decoders (`Http10Encoder`, `Http11Encoder`, etc.)
- No ARCHITECTURE.md restructuring beyond the engine table update

## Technical Considerations

- **EmitMultiple ordering:** When flushing `_pendingOutbound`, control signals and data buffers are emitted in insertion order. This differs from the old MergePreferred approach which guaranteed signals before data. Need to verify ordering is correct by emitting signals before data in the StateMachine callbacks.
- **BatchWeighted interaction:** The BatchWeighted operator sits between OutNetwork and the BidiShape outlet in the engine. Control signals have weight 0 and pass through immediately. This behavior is preserved.
- **PostStop cleanup:** Must return pooled items (`ConnectionReuseItem.Return()`, `StreamAcquireItem.Return()`) and dispose pending responses, matching the existing correlation stage cleanup patterns.
- **Existing encoder Attributes:** `Http10EncoderStage` reads `TurboAttributes.MemoryBuffer` from `inheritedAttributes`. The new StateMachine needs access to buffer size config — pass via constructor or use sensible defaults.

## Success Metrics

- Net deletion of 6 stage files + 1 shape class
- HTTP/1.0 engine wiring reduced from ~30 lines of GraphDsl to ~15 lines
- HTTP/1.1 engine wiring reduced from ~35 lines of GraphDsl to ~15 lines
- Architectural consistency: all 3 protocol engines (1.0, 1.1, 2.0) follow the same single-stage pattern
- Zero test regressions across all test projects

## Open Questions

*None — all design decisions resolved during exploration.*
