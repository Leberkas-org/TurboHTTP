# Feature 008: Remove Stream ID Closure Hack from Http30Engine

## Introduction

The `Http30Engine.CreateFlow()` uses a fragile closure-based response stream ID assignment: a captured `long responseStreamId` variable that increments by 4 in a `Select` lambda. This creates a synthetic `responseIdFlow` graph node that duplicates the allocator logic, captures mutable state outside the graph, and assumes sequential response ordering. This feature moves stream ID tracking into `Http30StreamStage` where it belongs, removing the closure hack and the extra graph node.

### Architecture Context (from CLAUDE.md)

- **Layer affected**: Streams (`TurboHttp/Streams/`) — Engine + `Http30StreamStage` only
- **Protocol Layer unchanged**: No changes to `Http3RequestEncoder`, `QpackEncoder`, `QpackDecoder`, frame types, or settings types
- **Transport Layer unchanged**: `ConnectionStage`, `ClientByteMover`, QUIC transport unaffected
- **IHttpProtocolEngine contract unchanged**: `BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed>` remains the same
- **Demux/QPACK feedback pipeline unchanged**: The `Partition → QpackDecoderStream → Feedback(SINK)` and `Demux → Merge(3)` paths remain as-is

### Current vs Target (response path only)

**Current:**
```
Connection.OutApp → StreamDecoder → responseIdFlow(closure hack) → Correlation.In1
```

**Target:**
```
Connection.OutApp → StreamDecoder(+streamId) → Correlation.In1
```

One fewer graph node. The `Http30StreamStage` outputs `(HttpResponseMessage, long)` directly, matching what `Http30CorrelationStage.In1` already expects.

## Goals

- Remove the `responseStreamId` closure hack from `Http30Engine.CreateFlow()`
- Eliminate the synthetic `responseIdFlow` graph node
- Move stream ID tracking into `Http30StreamStage` where it belongs
- Keep all existing RFC 9114 and RFC 9204 compliance behaviour intact
- All existing stream tests and unit tests remain green

## Tasks

### TASK-008-001: Move Response Stream ID into Http30StreamStage
**Description:** As a developer, I want the `Http30StreamStage` to output `(HttpResponseMessage, long)` tuples with the response stream ID so the engine no longer needs a synthetic closure-based `responseIdFlow`.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** TASK-008-002
**Parallel:** no

**Context:**

Currently `Http30StreamStage` has `FlowShape<Http3Frame, HttpResponseMessage>`. The engine adds stream IDs via a closure:
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
- It assumes responses arrive in sequential order (0, 4, 8, 12...)
- It duplicates the allocator pattern that `Http30StreamIdAllocatorStage` already owns

The fix: `Http30StreamStage` tracks the stream ID internally using the same `4n` pattern (0, 4, 8, ...) as the allocator, and outputs `(HttpResponseMessage, long)` directly. The `Http30CorrelationStage.In1` already accepts `(HttpResponseMessage, long)`.

**Implementation steps:**

1. Change `Http30StreamStage` shape from `FlowShape<Http3Frame, HttpResponseMessage>` to `FlowShape<Http3Frame, (HttpResponseMessage, long)>`
2. Add a `_nextStreamId` counter (long, starting at 0, incrementing by 4) to `Logic`
3. In `EmitResponse()`, push `(response, streamId)` and increment the counter
4. Update port names: `_out` string from `"Http30Stream.Out"` stays (type change doesn't affect port name)
5. Update `03_Http30StreamStageTests.cs` for new output type `(HttpResponseMessage, long)`

**Acceptance Criteria:**
- [x] `Http30StreamStage` outputs `(HttpResponseMessage, long)` with stream ID
- [x] Stream IDs follow the `4n` pattern: 0, 4, 8, 12, ...
- [x] Port names follow CLAUDE.md naming conventions
- [x] `03_Http30StreamStageTests.cs` updated and green
- [x] `dotnet build --configuration Release src/TurboHttp.sln` — 0 errors
- [x] `dotnet test src/TurboHttp.sln` — all tests green

### TASK-008-002: Rewire Http30Engine and Clean Up
**Description:** As a developer, I want `Http30Engine.CreateFlow()` to wire the updated `Http30StreamStage` directly to the correlation stage, removing the `responseIdFlow` node and the static closure.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-008-001
**Successors:** none
**Parallel:** no

**Context:**

After TASK-008-001, the `Http30StreamStage` outputs `(HttpResponseMessage, long)` which is exactly what `Http30CorrelationStage.In1` expects. The `responseIdFlow` node and the `responseStreamId` closure variable become dead code.

**Implementation steps:**

1. In `Http30Engine.CreateFlow()`, remove the `responseStreamId` variable and `responseIdFlow` node (lines 100–107)
2. Wire `streamDecoder.Outlet` directly to `correlation.In1` (replacing `b.From(streamDecoder.Outlet).Via(responseIdFlow).To(correlation.In1)`)
3. Verify end-to-end engine tests pass

**Acceptance Criteria:**
- [ ] `Http30Engine.CreateFlow()` no longer contains `responseStreamId` variable
- [ ] `Http30Engine.CreateFlow()` no longer creates `responseIdFlow` node
- [ ] `streamDecoder.Outlet` wired directly to `correlation.In1`
- [ ] One fewer `b.Add()` call in `CreateFlow()`
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` — 0 errors
- [ ] `dotnet test src/TurboHttp.sln` — all tests green

## Task Dependency Graph

```
TASK-008-001 ──→ TASK-008-002
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-008-001 | ~40k | none | no | — |
| TASK-008-002 | ~25k | 001 | no | — |

**Total estimated tokens:** ~65k

## Functional Requirements

- FR-1: `Http30Engine.CreateFlow()` returns a `BidiFlow` with the same type signature as before: `BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed>`
- FR-2: Response stream IDs follow the `4n` pattern (0, 4, 8, ...) matching the allocator on the request side
- FR-3: Request-response correlation by stream ID works correctly
- FR-4: All other engine behaviour unchanged (QPACK feedback, demux, control preface, connection stage)

## Non-Goals

- No changes to QPACK feedback pipeline (`Partition → QpackDecoderStream → Feedback`)
- No changes to outbound demux→merge path (`Demux → Merge(3)`)
- No changes to `Http30ConnectionStage` or its shape
- No changes to `Http30DecoderStage`
- No changes to the `IHttpProtocolEngine` interface
- No changes to Transport layer
- No HTTP/2 engine changes

## Technical Considerations

- **Port naming**: The `Http30StreamStage` outlet changes output type but keeps the same port name string (`"Http30Stream.Out"`). Type changes don't require port name changes.
- **`TreatWarningsAsErrors`**: The removed `responseIdFlow` references must not leave dangling code.
- **Counter consistency**: Both `Http30StreamIdAllocatorStage` (request side) and `Http30StreamStage` (response side) use the same `4n` pattern. This is intentional — HTTP/3 client-initiated bidirectional streams use IDs 0, 4, 8, 12 per RFC 9000 §2.1.

## Success Metrics

- `responseStreamId` closure and `responseIdFlow` node removed from `Http30Engine`
- One fewer `b.Add()` call in `CreateFlow()`
- All RFC9114 stream tests green
- All RFC9204 stream tests green
- Full solution build + test: 0 errors, 0 warnings, all tests green

## Open Questions

_None — all questions resolved._
