# Feature 008: Simplify Http30Engine Graph Topology

## Introduction

The `Http30Engine` currently wires ~15 graph nodes in its `CreateFlow()` method, making it significantly more complex than `Http20Engine` (~10 nodes). Much of this complexity comes from the QPACK decoder feedback path (a `SinkShape` dead-end requiring 4 dedicated nodes), an outbound demux→merge round-trip that splits and immediately re-merges tagged items, and a fragile closure-based response stream ID assignment. This feature simplifies the graph to ~8 nodes by absorbing side-paths into existing stages and eliminating unnecessary fan-out/fan-in patterns.

### Architecture Context (from CLAUDE.md)

- **Layer affected**: Streams (`TurboHttp/Streams/`) — Engine + Stages only
- **Protocol Layer unchanged**: No changes to `Http3RequestEncoder`, `QpackEncoder`, `QpackDecoder`, `QpackInstructionDecoder`, or any frame/settings types
- **Transport Layer unchanged**: `ConnectionStage`, `ClientByteMover`, QUIC transport unaffected
- **IHttpProtocolEngine contract unchanged**: `BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed>` remains the same
- **Existing patterns**: Follows the same absorption pattern used in `Http20ConnectionStage` (which absorbs SETTINGS/PING/GOAWAY internally and exposes a signal outlet)

### Current vs Target Graph

**Current (~15 nodes):**
```
Request:  StreamIdAllocator → Broadcast → [stripStreamId → Request2Frame, Correlation.In0]
          Request2Frame.OutFrame → Connection.InApp
          Request2Frame.OutEncoder → QpackEncoderPreface
Outbound: Connection.OutServer → FrameEncoder → Batch → preDemuxMerge ← QpackEncoderPreface
          preDemuxMerge → ControlPreface → Demux(3) → demuxMerge(3) → [wire]
Inbound:  [wire] → Partition(2) → [FrameDecoder, extractBytes → QpackDecoderStream → Feedback(SINK)]
          FrameDecoder → Connection.InServer → Connection.OutApp → StreamDecoder → responseIdFlow → Correlation
```

**Target (~8 nodes):**
```
Request:  StreamIdAllocator → Broadcast → [stripStreamId → Request2Frame, Correlation.In0]
          Request2Frame.OutFrame → Connection.InApp
          Request2Frame.OutEncoder → QpackEncoderPreface
Outbound: Connection.OutServer → FrameEncoder → Batch → Merge ← QpackEncoderPreface
          Merge → ControlPreface → [wire]
Inbound:  [wire] → FrameDecoder → Connection(+QpackFeedback) → StreamDecoder(+streamId) → Correlation
```

## Goals

- Reduce `Http30Engine.CreateFlow()` from ~15 graph nodes to ~8 graph nodes
- Eliminate the `SinkShape` (`QpackDecoderFeedbackStage`) — the only sink in any engine graph
- Remove the outbound demux→merge round-trip (`Http30StreamDemuxStage` + `Merge(3)`)
- Remove the fragile `responseStreamId` closure hack from the engine
- Keep all existing RFC 9114 and RFC 9204 compliance behaviour intact
- All existing stream tests and unit tests remain green

## Tasks

### TASK-008-001: Absorb QPACK Decoder Feedback into Http30ConnectionStage
**Description:** As a developer, I want the `Http30ConnectionStage` to handle QPACK decoder instructions internally so that the engine graph no longer needs a separate `Partition`, `extractBytes` flow, `QpackDecoderStreamStage`, or `QpackDecoderFeedbackStage` (SinkShape).

**Token Estimate:** ~75k tokens
**Predecessors:** none
**Successors:** TASK-008-004
**Parallel:** yes — can run alongside TASK-008-002 and TASK-008-003
**Model:** opus — complex stage logic change with backpressure implications

**Context:**

The `Http30ConnectionStage` currently has a custom `Http30ConnectionShape` with 2 inlets (`InServer`, `InApp`) and 2 outlets (`OutApp`, `OutServer`). It already classifies server frames by type (SETTINGS → absorb, GOAWAY → absorb, PUSH_PROMISE → reject, DATA/HEADERS → forward to app).

The change: The stage's `InServer` inlet type changes from `Http3Frame` to `IInputItem`. Before the frame decoder, the stage receives raw `IInputItem` instances. If an item is `Http3InputTaggedItem { StreamType: QpackDecoder }`, extract the bytes, decode them via `QpackInstructionDecoder`, and apply to the `QpackEncoder` — then pull again (absorb, don't forward). All other items are cast to their frame types and processed as before.

This means the `Http30DecoderStage` must move upstream of the connection stage in the engine wiring, OR the connection stage receives `IInputItem` directly (before decoding) and decodes regular frames internally. The cleaner approach: keep `Http30DecoderStage` where it is, but have the connection stage accept `IInputItem` and route internally.

**Implementation steps:**

1. Add `QpackEncoder` and `QpackInstructionDecoder` as constructor parameters to `Http30ConnectionStage`
2. Change `Http30ConnectionShape.InServer` from `Inlet<Http3Frame>` to `Inlet<IInputItem>`
3. In the `InServer` push handler:
   - If item is `Http3InputTaggedItem { StreamType: QpackDecoder }`: extract bytes → `_instructionDecoder.DecodeAllDecoderInstructions()` → `_encoder.ApplyDecoderInstruction()` for each → pull again
   - Otherwise: cast `IInputItem` to the expected frame type and process as before (the `Http30DecoderStage` output is `IInputItem`-compatible)
4. Update `Http30ConnectionShape.DeepCopy()` and `CopyFromPorts()` for the new inlet type
5. Update all tests in `04_Http30ConnectionStageTests.cs` to supply `IInputItem` instead of `Http3Frame` on `InServer`
6. Update `QpackDecoderFeedbackStageTests.cs` — move relevant feedback tests to `Http30ConnectionStageTests`

**Acceptance Criteria:**
- [ ] `Http30ConnectionStage` constructor accepts `QpackEncoder` and optional `QpackInstructionDecoder`
- [ ] `Http30ConnectionShape.InServer` type is `Inlet<IInputItem>`
- [ ] QPACK decoder instructions are absorbed internally (not forwarded to `OutApp`)
- [ ] All SETTINGS/GOAWAY/PUSH_PROMISE handling unchanged
- [ ] Existing `04_Http30ConnectionStageTests.cs` tests updated and green
- [ ] New tests: QPACK decoder instruction absorption, multiple instructions in one buffer, empty instruction buffer
- [ ] `QpackDecoderFeedbackStage` is no longer referenced by any engine code (may still exist for now)
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` — 0 errors
- [ ] `dotnet test src/TurboHttp.sln` — all tests green

### TASK-008-002: Eliminate Outbound Demux→Merge Round-Trip
**Description:** As a developer, I want to remove the `Http30StreamDemuxStage` and `Merge(3)` from the outbound path so that tagged items flow directly to the wire without unnecessary fan-out/fan-in.

**Token Estimate:** ~50k tokens
**Predecessors:** none
**Successors:** TASK-008-004
**Parallel:** yes — can run alongside TASK-008-001 and TASK-008-003

**Context:**

Currently the outbound path is:
```
preDemuxMerge(2) → ControlPreface → Demux(3 outlets) → demuxMerge(3 inlets) → [wire]
```

The `Http30ControlStreamPrefaceStage` emits a control-stream preface tagged with `OutputStreamType.Control` on first pull. The `Http30QpackEncoderPrefaceStage` already wraps its output as `Http3TaggedItem` with `OutputStreamType.QpackEncoder`. The `Http30StreamDemuxStage` then classifies by tag and routes to 3 outlets — which are immediately merged back into one.

The demux→merge round-trip is unnecessary because:
1. All items are already tagged with their `OutputStreamType`
2. The downstream (transport) can inspect the tag directly
3. No independent per-stream backpressure is actually needed at this point

**Simplified outbound:**
```
Merge(2) → ControlPreface → [wire]
```

The `ControlPreface` stage already passes through all items after emitting the preface. The `QpackEncoderPreface` stage already tags its output. The `FrameEncoder → Batch` output is untagged (request stream by default). This is sufficient.

**Implementation steps:**

1. In `Http30Engine.CreateFlow()`, remove the `demux` and `demuxMerge` graph nodes
2. Wire `preDemuxMerge.Out` (rename to just `outMerge`) → `controlPreface` → BidiShape outlet directly
3. Verify that the `Http30ControlStreamPrefaceStage` still emits its tagged preface correctly
4. Update `06_Http30EngineEndToEndTests.cs` if it asserts on demux behaviour
5. Do NOT delete `Http30StreamDemuxStage` or `Http30StreamDemuxShape` files yet (TASK-008-005 handles cleanup)

**Acceptance Criteria:**
- [ ] `Http30Engine.CreateFlow()` no longer creates `Http30StreamDemuxStage` or `Merge<IOutputItem>(3)`
- [ ] Outbound path: `Merge(2) → ControlPreface → [wire]`
- [ ] Control stream preface still emitted as `Http3TaggedItem(Control)` on first pull
- [ ] QPACK encoder instructions still emitted as `Http3TaggedItem(QpackEncoder)`
- [ ] Request frames pass through untagged (default request stream)
- [ ] `06_Http30EngineEndToEndTests.cs` updated and green
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` — 0 errors
- [ ] `dotnet test src/TurboHttp.sln` — all tests green

### TASK-008-003: Move Response Stream ID into Http30StreamStage
**Description:** As a developer, I want the `Http30StreamStage` to output `(HttpResponseMessage, long)` tuples with the actual QUIC stream ID so the engine no longer needs a synthetic `responseIdFlow` closure.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** TASK-008-004
**Parallel:** yes — can run alongside TASK-008-001 and TASK-008-002

**Context:**

Currently `Http30StreamStage` outputs `HttpResponseMessage` and the engine adds stream IDs via a closure:
```csharp
long responseStreamId = 0;
var responseIdFlow = b.Add(
    Flow.Create<HttpResponseMessage>().Select(r =>
    {
        var id = responseStreamId;
        responseStreamId += 4;
        return (r, id);
    }));
```

This is fragile because:
- The closure captures mutable state outside the graph
- It assumes responses arrive in order (0, 4, 8, 12...)
- It doesn't use the actual stream ID from the HTTP/3 frames

The `Http30StreamStage` assembles HEADERS + DATA frames into responses. The frames carry stream context implicitly through QUIC. The `Http30ConnectionStage` routes frames to the stream stage — and it knows which stream each frame belongs to.

**Implementation approach:**

The simplest fix: `Http30StreamStage` tracks the stream ID from the frame context. Since HTTP/3 frames don't carry an explicit stream ID field (QUIC provides that at the transport layer), the stream ID should be passed through from the `Http30ConnectionStage` via a wrapper or by changing the inlet type.

Option A: Change `Http30StreamStage` inlet from `Http3Frame` to `(Http3Frame, long)` — the ConnectionStage pairs each frame with its stream ID.
Option B: Add a `StreamId` property to `Http3Frame` base class (set by the decoder when QUIC stream context is available).

**Recommended: Option A** — keeps frame types clean, minimal blast radius.

**Implementation steps:**

1. Change `Http30StreamStage` shape from `FlowShape<Http3Frame, HttpResponseMessage>` to `FlowShape<Http3Frame, (HttpResponseMessage, long)>`
2. Track the stream ID from the first frame received (or accept it as constructor parameter)
3. Output `(response, streamId)` in `EmitResponse()`
4. In `Http30Engine`, remove the `responseIdFlow` node and wire `streamDecoder.Outlet` directly to `correlation.In1`
5. Adjust `Http30ConnectionStage.OutApp` to emit `(Http3Frame, long)` if needed for stream ID propagation
6. Update `03_Http30StreamStageTests.cs` for new output type

**Acceptance Criteria:**
- [ ] `Http30StreamStage` outputs `(HttpResponseMessage, long)` with actual stream ID
- [ ] `Http30Engine.CreateFlow()` no longer creates the `responseIdFlow` node
- [ ] `Http30CorrelationStage.In1` receives `(HttpResponseMessage, long)` directly from stream stage (already compatible)
- [ ] Stream IDs in responses match the actual QUIC stream IDs (0, 4, 8, ...)
- [ ] `03_Http30StreamStageTests.cs` updated and green
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` — 0 errors
- [ ] `dotnet test src/TurboHttp.sln` — all tests green

### TASK-008-004: Rewire Http30Engine and Integration Verification
**Description:** As a developer, I want the simplified `Http30Engine.CreateFlow()` to wire all the updated stages together and verify the full engine works end-to-end.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-008-001, TASK-008-002, TASK-008-003
**Successors:** TASK-008-005
**Parallel:** no — requires all three preceding tasks
**Model:** opus — full graph wiring with multiple stage interactions

**Context:**

After TASK-001 through TASK-003, the individual stages have been updated but the engine wiring in `Http30Engine.CreateFlow()` still uses the old topology. This task rewires the engine to the simplified graph.

**Target wiring:**
```csharp
// Request path (unchanged)
StreamIdAllocator → Broadcast → [stripStreamId → Request2Frame, Correlation.In0]
Request2Frame.OutFrame → Connection.InApp

// Outbound: frame encoder + QPACK instructions → merge → control preface → wire
Connection.OutServer → FrameEncoder → Batch → outMerge.In(0)
Request2Frame.OutEncoder → QpackEncoderPreface → outMerge.In(1)
outMerge → ControlPreface → [BidiShape outlet]

// Inbound: wire → decoder → connection (absorbs QPACK) → stream stage → correlation
[BidiShape inlet] → FrameDecoder → Connection.InServer (IInputItem)
Connection.OutApp → StreamDecoder → Correlation.In1
```

**Implementation steps:**

1. Rewrite `Http30Engine.CreateFlow()` with simplified wiring
2. Remove: `partition`, `extractBytes`, `decoderStream`, `feedback`, `demux`, `demuxMerge`, `responseIdFlow`
3. Update `Connection` instantiation to pass `requestEncoder.QpackEncoder`
4. Wire `frameDecoder.Outlet` directly to `connection.InServer` (now accepts `IInputItem`)
5. Wire `streamDecoder.Outlet` directly to `correlation.In1` (now outputs `(HttpResponseMessage, long)`)
6. Remove static helpers: `ClassifyInputItem`, `ExtractDecoderStreamBytes` (no longer needed)
7. Run full end-to-end tests: `06_Http30EngineEndToEndTests.cs`
8. Run all RFC9114 and RFC9204 stream tests

**Acceptance Criteria:**
- [ ] `Http30Engine.CreateFlow()` uses ~8 graph nodes (down from ~15)
- [ ] `ClassifyInputItem` and `ExtractDecoderStreamBytes` static methods removed
- [ ] No references to `Partition`, `QpackDecoderStreamStage`, `QpackDecoderFeedbackStage`, or `Http30StreamDemuxStage` in engine
- [ ] `06_Http30EngineEndToEndTests.cs` — all tests green
- [ ] `dotnet test src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj --filter "FullyQualifiedName~RFC9114"` — all green
- [ ] `dotnet test src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj --filter "FullyQualifiedName~RFC9204"` — all green
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` — 0 errors
- [ ] `dotnet test src/TurboHttp.sln` — all tests green

### TASK-008-005: Delete Obsolete Stages and Clean Up
**Description:** As a developer, I want to remove stage files and shapes that are no longer used by any engine or test so the codebase stays lean.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-008-004
**Successors:** none
**Parallel:** no

**Implementation steps:**

1. Delete `src/TurboHttp/Streams/Stages/Decoding/QpackDecoderFeedbackStage.cs`
2. Delete `src/TurboHttp/Streams/Stages/Routing/Http30StreamDemuxStage.cs` (includes `Http30StreamDemuxShape`)
3. Delete test files that tested the removed stages:
   - `src/TurboHttp.StreamTests/RFC9204/QpackDecoderFeedbackStageTests.cs`
   - `src/TurboHttp.StreamTests/RFC9114/15_Http30StreamDemuxStageTests.cs`
4. Grep for any remaining references to deleted types — fix or remove
5. If `QpackDecoderStreamStage` is no longer used anywhere, delete it and `src/TurboHttp.StreamTests/RFC9204/QpackStreamStageTests.cs`
6. Verify no orphaned `using` directives remain

**Acceptance Criteria:**
- [ ] `QpackDecoderFeedbackStage.cs` deleted
- [ ] `Http30StreamDemuxStage.cs` deleted (includes `Http30StreamDemuxShape`)
- [ ] `QpackDecoderFeedbackStageTests.cs` deleted
- [ ] `15_Http30StreamDemuxStageTests.cs` deleted
- [ ] `QpackDecoderStreamStage.cs` deleted if no longer used (otherwise kept)
- [ ] Grep for `QpackDecoderFeedbackStage` across repo returns 0 matches
- [ ] Grep for `Http30StreamDemuxStage` across repo returns 0 matches
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` — 0 errors, 0 warnings
- [ ] `dotnet test src/TurboHttp.sln` — all tests green

## Task Dependency Graph

```
TASK-008-001 ──┐
TASK-008-002 ──├──→ TASK-008-004 ──→ TASK-008-005
TASK-008-003 ──┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-008-001 | ~75k | none | yes (with 002, 003) | opus |
| TASK-008-002 | ~50k | none | yes (with 001, 003) | — |
| TASK-008-003 | ~40k | none | yes (with 001, 002) | — |
| TASK-008-004 | ~50k | 001, 002, 003 | no | opus |
| TASK-008-005 | ~25k | 004 | no | — |

**Total estimated tokens:** ~240k

## Functional Requirements

- FR-1: `Http30Engine.CreateFlow()` returns a `BidiFlow` with the same type signature as before: `BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed>`
- FR-2: QPACK decoder instructions from the inbound stream are applied to the `QpackEncoder` (dynamic table stays synchronized)
- FR-3: QPACK encoder instructions are emitted on the outbound path tagged with `OutputStreamType.QpackEncoder`
- FR-4: Control stream preface (stream type VarInt 0x00 + SETTINGS) is emitted as `Http3TaggedItem(Control)` on first pull
- FR-5: SETTINGS, GOAWAY, PUSH_PROMISE frames are absorbed by the connection stage (not forwarded to the app)
- FR-6: Response stream IDs in correlation match the actual QUIC stream context (not synthetic counters)
- FR-7: Request-response correlation by stream ID works correctly for concurrent requests
- FR-8: All outbound items retain their `OutputStreamType` tags for transport-layer routing

## Non-Goals

- No changes to `Http3RequestEncoder`, `QpackEncoder`, `QpackDecoder`, or `QpackInstructionDecoder`
- No changes to the `IHttpProtocolEngine` interface
- No changes to `Http30EncoderStage` or `Http30DecoderStage` internals
- No changes to Transport layer (`ConnectionStage`, `ClientByteMover`, QUIC)
- No HTTP/2 engine changes (Http20Engine stays as-is)
- No new RFC compliance features — this is purely a topology simplification

## Technical Considerations

- **Backpressure**: The current `Partition → Sink` path has no downstream backpressure concerns (sink always pulls). After absorption into `ConnectionStage`, the same behaviour must hold — decoder instructions must be processed eagerly without blocking the main frame path.
- **Thread safety**: `QpackEncoder.ApplyDecoderInstruction()` is called from the same stage logic as `Encode()` (via the `Http3RequestEncoder`). Since Akka stage logic is single-threaded, this is safe — but only if both happen in the same stage or the encoder is not accessed concurrently. The `ConnectionStage` is the right place because it's the single routing point.
- **`Http30ConnectionShape` change**: Changing `InServer` from `Inlet<Http3Frame>` to `Inlet<IInputItem>` is a breaking shape change. All tests that wire this inlet must be updated. The `Http30DecoderStage` already outputs items compatible with `IInputItem`.
- **Port naming**: All new/changed ports must follow CLAUDE.md naming conventions (PascalCase, `StageName.Direction.Role`, globally unique).
- **`TreatWarningsAsErrors`**: Deleted types must not leave dangling references — the build will fail.

## Success Metrics

- `Http30Engine.CreateFlow()` graph node count: 15 → 8 (measured by counting `b.Add()` calls)
- No `SinkShape` stages in any engine graph
- No `Partition` or `Merge(3)` in `Http30Engine`
- All 16 RFC9114 stream test files green
- All 3 RFC9204 stream test files green
- Full solution build + test: 0 errors, 0 warnings, all tests green

## Open Questions

_None — all questions resolved._
