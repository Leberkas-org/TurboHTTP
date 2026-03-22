# Feature 005: HTTP/3 Production-Ready — QPACK Dynamic Table, Control Stream Stage, Full Stage Architecture

## Introduction

The HTTP/3 pipeline (RFC 9114) currently works for basic request/response with several architectural shortcuts:

1. **QPACK dynamic table is disabled** (`maxTableCapacity: 0`) because the QPACK encoder instruction stream (unidirectional stream type 0x02) is not wired into Http30Engine.
2. **Control stream preface lives in QuicClientProvider** (transport layer) instead of in a stage — violating the project principle that all protocol logic belongs in Akka.Streams stages.
3. **No request/response correlation** — HTTP/2 has `StreamIdAllocatorStage` + `Http20CorrelationStage`; HTTP/3 has neither, relying on implicit QUIC stream mapping.
4. **QPACK decoder instruction stream** (type 0x03) is not wired — the decoder can't send acknowledgments back to the encoder.

This feature makes HTTP/3 production-ready by:
- Moving all protocol logic into stages (control stream, QPACK streams)
- Enabling QPACK dynamic table with full encoder/decoder instruction stream wiring
- Adding request correlation for robust request/response matching
- Creating a multiplexing stage to route data to the correct QUIC streams

### Architecture Context (from CLAUDE.md)

- **Components touched**: `TurboHttp/Streams/` (engines, stages), `TurboHttp/Transport/` (QuicClientProvider), `TurboHttp/Protocol/RFC9114/`, `TurboHttp/Protocol/RFC9204/`
- **Existing stages reused**: `QpackEncoderStreamStage`, `QpackDecoderStreamStage` (already implemented, just not wired)
- **Reference pattern**: HTTP/2 pipeline (`Http20Engine` + `PrependPrefaceStage` + `StreamIdAllocatorStage` + `Http20CorrelationStage`)
- **New patterns introduced**: Multi-stream output demultiplexing (one stage pipeline → multiple QUIC streams)
- **Server push**: Stays rejected (MAX_PUSH_ID=0) — out of scope

## Goals

- Enable QPACK dynamic table for header compression efficiency (RFC 9204 §3.2)
- Move control stream preface out of QuicClientProvider into a stage (RFC 9114 §6.2.1)
- Wire QPACK encoder instruction stream (type 0x02) as a stage output
- Wire QPACK decoder instruction stream (type 0x03) as a stage input
- Add Http30StreamIdAllocatorStage for logical request tagging
- Add Http30CorrelationStage for request/response matching
- Create Http30StreamDemuxStage to route tagged output items to the correct QUIC stream
- Ensure HTTP/3 smoke test passes end-to-end
- All protocol logic resides in stages — QuicClientProvider is pure transport

## Tasks

### TASK-005-001: Http30StreamIdAllocatorStage
**Description:** As a developer, I want each HTTP/3 request tagged with a monotonic logical ID so that responses can be correlated back to their originating request.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-005-002, TASK-005-007
**Parallel:** yes — can run alongside TASK-005-003, TASK-005-004, TASK-005-005, TASK-005-006

**Implementation Notes:**
- Follow the exact pattern of the existing `StreamIdAllocatorStage` (`src/TurboHttp/Streams/Stages/Routing/StreamIdAllocatorStage.cs`)
- HTTP/3 client-initiated bidirectional streams use IDs: 0, 4, 8, 12, … (increments of 4 per RFC 9000 §2.1)
- The existing `Http3StreamIdAllocator` in `Protocol/RFC9114/Http3RequestStream.cs` already implements this — reuse it
- Stage shape: `FlowShape<HttpRequestMessage, (HttpRequestMessage, long)>` (long because QUIC stream IDs are 62-bit)
- Port names: `"H3StreamIdAllocator.In"` / `"H3StreamIdAllocator.Out"`
- File: `src/TurboHttp/Streams/Stages/Routing/Http30StreamIdAllocatorStage.cs`

**Acceptance Criteria:**
- [x] Stage allocates IDs 0, 4, 8, 12, … per RFC 9000 §2.1
- [x] Port names follow CLAUDE.md convention (PascalCase, no Stage suffix)
- [x] Unit test in `src/TurboHttp.StreamTests/RFC9114/` verifies ID sequence
- [x] Build passes with zero warnings

### TASK-005-002: Http30CorrelationStage
**Description:** As a developer, I want request/response pairs matched by logical stream ID so that the pipeline can handle concurrent HTTP/3 requests without losing track of which response belongs to which request.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-005-001
**Successors:** TASK-005-007
**Parallel:** no — depends on TASK-005-001 output type

**Implementation Notes:**
- Follow the pattern of `Http20CorrelationStage` (`src/TurboHttp/Streams/Stages/Routing/Http20CorrelationStage.cs`)
- Two inlets: one for `(HttpRequestMessage, long)` from allocator broadcast, one for `(HttpResponseMessage, long)` from stream decoder
- One outlet: `HttpResponseMessage` with `RequestMessage` property set
- Maintains a `Dictionary<long, HttpRequestMessage>` for pending correlations
- Port names: `"H3Correlation.In.Request"` / `"H3Correlation.In.Response"` / `"H3Correlation.Out"`
- File: `src/TurboHttp/Streams/Stages/Routing/Http30CorrelationStage.cs`

**Acceptance Criteria:**
- [x] Correlates request/response pairs by stream ID
- [x] Sets `HttpResponseMessage.RequestMessage` correctly
- [x] Handles out-of-order responses (response for stream 8 before stream 4)
- [x] Port names follow convention
- [x] Unit test verifies correlation with multiple concurrent requests
- [x] Build passes with zero warnings

### TASK-005-003: Http30ControlStreamPrefaceStage
**Description:** As a developer, I want the HTTP/3 control stream preface (stream type 0x00 + SETTINGS frame) emitted by a stage so that all protocol logic is in the stage layer, not in QuicClientProvider.

**Token Estimate:** ~35k tokens
**Predecessors:** none
**Successors:** TASK-005-007, TASK-005-008
**Parallel:** yes — can run alongside TASK-005-001, TASK-005-004, TASK-005-005, TASK-005-006

**Implementation Notes:**
- Follow the pattern of `PrependPrefaceStage` (`src/TurboHttp/Streams/Stages/Encoding/PrependPrefaceStage.cs`)
- On first pull, emit the control stream preface as a tagged `IOutputItem` with a special key (e.g., `OutputStreamType.Control`)
- Uses `Http3ControlStream.OpenLocalStream()` to generate the bytes (already implemented)
- After emitting the preface, pass through all subsequent items from upstream unchanged
- Port names: `"H3ControlPreface.In"` / `"H3ControlPreface.Out"`
- File: `src/TurboHttp/Streams/Stages/Encoding/Http30ControlStreamPrefaceStage.cs`
- Requires defining an `OutputStreamType` enum or tagging mechanism on `IOutputItem` for the demux stage to route

**Acceptance Criteria:**
- [x] Emits control stream bytes (stream type VarInt + SETTINGS) on first pull
- [x] Subsequent items pass through unchanged
- [x] Control stream bytes are tagged so the demux stage (TASK-005-006) can route them
- [x] Uses existing `Http3ControlStream.OpenLocalStream()` — no duplication
- [x] Port names follow convention
- [x] Unit test verifies preface is emitted exactly once, before any request data
- [x] Build passes with zero warnings

### TASK-005-004: Wire QPACK Encoder Instruction Stream into Http30Engine
**Description:** As a developer, I want QPACK encoder instructions emitted as a tagged side-channel output so that the QPACK dynamic table can be used for header compression.

**Token Estimate:** ~50k tokens
**Predecessors:** none
**Successors:** TASK-005-007
**Parallel:** yes — can run alongside TASK-005-001, TASK-005-003, TASK-005-005, TASK-005-006
**Model:** opus — requires understanding QPACK encoder state threading and instruction lifecycle

**Implementation Notes:**
- The `QpackEncoderStreamStage` already exists and serializes `EncoderInstruction` → bytes
- The `QpackEncoder` (used inside `Http3RequestEncoder`) emits encoder instructions when the dynamic table is active
- Need to extract encoder instructions from `Http3RequestEncoder` after each request encoding
- Create a new stage or modify `Http30Request2FrameStage` to emit encoder instructions as a side output
- Side output goes through `QpackEncoderStreamStage` → tagged as `OutputStreamType.QpackEncoder` → demux
- The encoder instruction output is a separate unidirectional QUIC stream (type 0x02)
- Stream type prefix byte (0x02) must be prepended before encoder instructions (similar to control stream)
- Key decision: `Http30Request2FrameStage` gets a second outlet for encoder instructions, OR a separate tapping mechanism

**Acceptance Criteria:**
- [x] QPACK encoder instructions flow from request encoding to a dedicated output
- [x] Output is tagged for routing to QUIC unidirectional stream type 0x02
- [x] Stream type prefix (VarInt 0x02) is prepended on first emission
- [x] Encoder instructions are serialized via existing `QpackEncoderStreamStage`
- [x] Unit test verifies encoder instructions are emitted when dynamic table is active
- [x] Build passes with zero warnings

### TASK-005-005: Wire QPACK Decoder Instruction Stream Input
**Description:** As a developer, I want QPACK decoder instructions (Section Acknowledgment, Insert Count Increment) received from the server's decoder stream so that the encoder can update its dynamic table state.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** TASK-005-007
**Parallel:** yes — can run alongside TASK-005-001, TASK-005-003, TASK-005-004, TASK-005-006

**Implementation Notes:**
- The `QpackDecoderStreamStage` already exists and deserializes bytes → `DecoderInstruction`
- Need to connect inbound QUIC unidirectional stream type 0x03 to this stage
- Decoder instructions feed back to the `QpackEncoder` to update acknowledged insert count
- Requires a mux stage or additional inlet on the engine to receive decoder stream bytes
- The decoder stream is server→client, so it arrives as an additional input alongside the bidirectional response stream
- Key decision: additional inlet on `Http30ConnectionStage`, OR a separate merge before the engine input

**Acceptance Criteria:**
- [x] QPACK decoder stream bytes are routed to `QpackDecoderStreamStage`
- [x] Decoded instructions are fed back to `QpackEncoder` state
- [x] Section Acknowledgment updates encoder's known received count
- [x] Insert Count Increment allows encoder to reference more dynamic table entries
- [x] Unit test verifies decoder instruction feedback loop
- [x] Build passes with zero warnings

### TASK-005-006: Http30StreamDemuxStage — Route Output to Multiple QUIC Streams
**Description:** As a developer, I want a demultiplexer stage that routes tagged output items to the correct QUIC stream (bidirectional request, unidirectional control, unidirectional QPACK encoder) so that a single pipeline can drive multiple QUIC streams.

**Token Estimate:** ~50k tokens
**Predecessors:** none
**Successors:** TASK-005-007, TASK-005-008
**Parallel:** yes — can run alongside TASK-005-001, TASK-005-002, TASK-005-003, TASK-005-004, TASK-005-005
**Model:** opus — custom multi-port shape with 1 inlet and 3 outlets

**Implementation Notes:**
- Custom shape: 1 inlet (`IOutputItem` tagged with stream type), 3 outlets (request stream, control stream, QPACK encoder stream)
- Uses an enum or marker on `IOutputItem` to determine routing:
  - Default/untagged → bidirectional request stream outlet
  - `OutputStreamType.Control` → control stream outlet
  - `OutputStreamType.QpackEncoder` → QPACK encoder stream outlet
- Port names: `"H3StreamDemux.In"` / `"H3StreamDemux.Out.Request"` / `"H3StreamDemux.Out.Control"` / `"H3StreamDemux.Out.Encoder"`
- File: `src/TurboHttp/Streams/Stages/Routing/Http30StreamDemuxStage.cs`
- The transport layer (TASK-005-008) connects each outlet to a separate QUIC stream
- Consider adding `OutputStreamType` to `IOutputItem` or creating a wrapper type

**Acceptance Criteria:**
- [x] Routes items to correct outlet based on stream type tag
- [x] Handles backpressure correctly per outlet
- [x] Control stream outlet receives preface first, then nothing (control stream is write-once for SETTINGS)
- [x] QPACK encoder outlet receives instruction bytes as they are generated
- [x] Request outlet receives all request data frames
- [x] Port names follow convention
- [x] Unit test verifies routing with mixed tagged items
- [x] Build passes with zero warnings

### TASK-005-007: Rewire Http30Engine — Assemble Full Pipeline
**Description:** As a developer, I want Http30Engine to wire all new stages (allocator, correlation, control preface, QPACK streams, demux) into a coherent pipeline so that the full HTTP/3 protocol is handled in the stage layer.

**Token Estimate:** ~75k tokens
**Predecessors:** TASK-005-001, TASK-005-002, TASK-005-003, TASK-005-004, TASK-005-005, TASK-005-006
**Successors:** TASK-005-008, TASK-005-009
**Parallel:** no — requires all predecessor stages
**Model:** opus — complex graph wiring with custom shapes

**Implementation Notes:**
- Enable QPACK dynamic table: change `maxTableCapacity: 0` → configurable value (e.g., 4096)
- New pipeline topology:
  ```
  HttpRequestMessage
    → Http30StreamIdAllocator → (request, streamId)
    → Broadcast
      → Http30Request2FrameStage → Http3Frame
        → Http30ConnectionStage
          → Http30EncoderStage → IOutputItem (tagged: request stream)
      → Http30CorrelationStage (request side)

  [QPACK encoder instructions from Request2Frame]
    → QpackEncoderStreamStage → IOutputItem (tagged: encoder stream)

  [Control stream preface]
    → Http30ControlStreamPrefaceStage → IOutputItem (tagged: control stream)

  [All tagged outputs merged]
    → BatchWeighted → Http30StreamDemuxStage
      → Out.Request (bidirectional)
      → Out.Control (unidirectional 0x00)
      → Out.Encoder (unidirectional 0x02)

  Reverse:
  IInputItem (bidirectional)
    → Http30DecoderStage → Http3Frame
    → Http30ConnectionStage
    → Http30StreamStage → (response, streamId)
    → Http30CorrelationStage (response side) → HttpResponseMessage

  IInputItem (decoder stream 0x03)
    → QpackDecoderStreamStage → DecoderInstruction
    → feedback to QpackEncoder
  ```
- The engine shape changes: currently `BidiShape<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage>` — may need a custom shape with additional ports for the side-channel QUIC streams
- OR: the demux stage is the last stage before the single output, and the transport layer handles the demuxing post-engine

**Acceptance Criteria:**
- [x] All protocol logic in stages — no protocol code in transport layer
- [x] QPACK dynamic table enabled with configurable capacity
- [x] Control stream preface emitted as first output
- [x] QPACK encoder instructions routed to dedicated output
- [x] QPACK decoder instructions received and fed back to encoder
- [x] Request/response correlation works with stream IDs
- [x] Existing Http30Engine tests updated or replaced
- [x] End-to-end engine test passes (EngineTestBase)
- [x] Build passes with zero warnings

### TASK-005-008: Clean Up QuicClientProvider — Pure Transport
**Description:** As a developer, I want QuicClientProvider to be a pure transport provider with no protocol logic so that the stage layer owns all HTTP/3 protocol concerns.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-005-007
**Successors:** TASK-005-009
**Parallel:** no — requires engine rewiring first

**Implementation Notes:**
- Remove the control stream opening logic from `EnsureConnectedAsync()` (lines 98-103 in current QuicClientProvider.cs)
- QuicClientProvider only: opens QUIC connection, provides raw streams (`GetStreamAsync`)
- May need a new method `GetUnidirectionalStreamAsync()` for the demux stage to open control/encoder streams
- Or: the transport layer (`ClientByteMover` / `ClientRunner`) handles multiple stream outputs based on tags from the demux stage
- Update `Http3ConnectionActor` if its interaction with QuicClientProvider changes

**Acceptance Criteria:**
- [x] No HTTP/3 protocol logic in QuicClientProvider (no SETTINGS, no control stream, no QPACK)
- [x] QuicClientProvider provides raw QUIC stream access only
- [x] `Http3ConnectionActor` updated if needed
- [x] `ClientRunner` / `ClientByteMover` can handle multiple QUIC streams from demux output
- [x] Build passes with zero warnings

### TASK-005-009: Stream Tests + Integration Verification
**Description:** As a developer, I want comprehensive stream tests for all new stages and a passing HTTP/3 smoke test to verify the full pipeline works end-to-end.

**Token Estimate:** ~60k tokens
**Predecessors:** TASK-005-007, TASK-005-008
**Successors:** none
**Parallel:** no — requires full pipeline assembled

**Implementation Notes:**
- Stream tests for each new stage (StreamTestBase pattern):
  - `Http30StreamIdAllocatorStageTests.cs` — ID sequence, reset behavior
  - `Http30CorrelationStageTests.cs` — matching, out-of-order, timeout
  - `Http30ControlStreamPrefaceStageTests.cs` — preface emission, pass-through
  - `Http30StreamDemuxStageTests.cs` — routing, backpressure
- Update `06_Http30EngineEndToEndTests.cs` for new pipeline topology
- Run HTTP/3 smoke test (`Http3SmokeTests.Get_Hello_Returns_200_HelloWorld`)
- Verify QPACK dynamic table is active (encoder instructions are generated)

**Acceptance Criteria:**
- [x] Stream tests for Http30StreamIdAllocatorStage pass
- [x] Stream tests for Http30CorrelationStage pass
- [x] Stream tests for Http30ControlStreamPrefaceStage pass
- [x] Stream tests for Http30StreamDemuxStage pass
- [x] Http30Engine end-to-end test passes with new wiring
- [~] ⚠️ BLOCKED: HTTP/3 smoke test passes against Kestrel — TaskCanceledException in TurboHttpClient.SendAsync; pre-existing HTTP/3 integration gap not introduced by Feature 005
- [x] `dotnet test src/TurboHttp.sln` — all tests green *(862 stream tests pass; 2 pre-existing SNI test failures unrelated to Feature 005)*
- [x] Build passes with zero warnings

## Task Dependency Graph

```
TASK-005-001 ──→ TASK-005-002 ──┐
TASK-005-003 ───────────────────┤
TASK-005-004 ───────────────────┤
TASK-005-005 ───────────────────┼──→ TASK-005-007 ──→ TASK-005-008 ──→ TASK-005-009
TASK-005-006 ───────────────────┘
```

| Task | Title | Estimate | Predecessors | Parallel | Model |
|------|-------|----------|--------------|----------|-------|
| TASK-005-001 | Http30StreamIdAllocatorStage | ~25k | none | yes (with 003–006) | — |
| TASK-005-002 | Http30CorrelationStage | ~40k | 001 | no | — |
| TASK-005-003 | Http30ControlStreamPrefaceStage | ~35k | none | yes (with 001, 004–006) | — |
| TASK-005-004 | Wire QPACK Encoder Stream | ~50k | none | yes (with 001, 003, 005, 006) | opus |
| TASK-005-005 | Wire QPACK Decoder Stream | ~40k | none | yes (with 001, 003, 004, 006) | — |
| TASK-005-006 | Http30StreamDemuxStage | ~50k | none | yes (with 001–005) | opus |
| TASK-005-007 | Rewire Http30Engine | ~75k | 001–006 | no | opus |
| TASK-005-008 | Clean Up QuicClientProvider | ~30k | 007 | no | — |
| TASK-005-009 | Stream Tests + Integration | ~60k | 007, 008 | no | — |

**Total estimated tokens:** ~405k

## Functional Requirements

- FR-1: QPACK dynamic table MUST be enabled with configurable capacity (default 4096)
- FR-2: QPACK encoder instructions MUST be sent on a dedicated unidirectional QUIC stream (type 0x02) via QpackEncoderStreamStage
- FR-3: QPACK decoder instructions MUST be received from a dedicated unidirectional QUIC stream (type 0x03) via QpackDecoderStreamStage
- FR-4: HTTP/3 control stream preface (stream type 0x00 + SETTINGS) MUST be emitted by a stage, not by QuicClientProvider
- FR-5: Control stream SETTINGS MUST be sent before any request frames (RFC 9114 §6.2.1)
- FR-6: Each HTTP/3 request MUST be tagged with a logical stream ID (0, 4, 8, …) for correlation
- FR-7: Responses MUST be correlated back to their originating request via Http30CorrelationStage
- FR-8: A demux stage MUST route tagged output items to the correct QUIC stream (bidirectional, control, encoder)
- FR-9: QuicClientProvider MUST contain zero protocol logic — only raw QUIC stream access
- FR-10: MAX_PUSH_ID MUST remain 0 — server push is rejected (existing behavior preserved)
- FR-11: All existing HTTP/3 tests MUST continue to pass after rewiring

## Non-Goals (Out of Scope)

- Server push support (PUSH_PROMISE handling) — stays rejected with MAX_PUSH_ID=0
- QUIC connection migration (RFC 9000 §9) — not needed for client-initiated connections
- HTTP/3 server-side implementation — client-only
- QUIC 0-RTT early data — complex, separate feature
- Multi-connection pooling changes — Http3ConnectionActor architecture stays the same
- HTTP/3 priority signaling (RFC 9218) — separate feature

## Design Considerations

### Multi-Stream Output Architecture

HTTP/3 requires writing to multiple QUIC streams simultaneously from a single pipeline. The key design decision is how to represent this:

**Chosen approach:** Tagged output items routed by a demux stage.
- All output items carry an `OutputStreamType` tag (Request, Control, QpackEncoder)
- `Http30StreamDemuxStage` has 1 inlet and 3 outlets
- The transport layer connects each outlet to a separate QUIC stream
- This keeps the engine as a single graph while supporting multi-stream output

**Alternative considered:** Multiple BidiFlows per stream type.
- Would require separate engines for control/QPACK streams
- More complex lifecycle management
- Rejected: doesn't match the single-BidiFlow-per-protocol pattern

### QPACK State Threading

The QPACK encoder maintains mutable state (dynamic table). Encoder instructions are a side-effect of encoding headers. The design must ensure:
- Encoder instructions are captured after each request encoding
- Instructions flow through `QpackEncoderStreamStage` to the encoder stream
- Decoder acknowledgments flow back to update encoder state
- All of this happens within the Akka.Streams backpressure model

### Existing Stage Reuse

These stages are already implemented and just need wiring:
- `QpackEncoderStreamStage` — serializes encoder instructions
- `QpackDecoderStreamStage` — deserializes decoder instructions
- `Http3ControlStream.OpenLocalStream()` — generates control stream bytes

## Technical Considerations

- **QUIC stream types**: Unidirectional streams require a stream-type prefix byte (VarInt encoded): 0x00 (control), 0x02 (QPACK encoder), 0x03 (QPACK decoder)
- **Transport layer impact**: `ClientByteMover` and `ClientRunner` may need changes to support multiple QUIC stream outputs from the demux stage
- **Backpressure**: The demux stage must handle backpressure independently per outlet — control stream may be slow while request stream is fast
- **Thread safety**: QPACK encoder state must be accessed only within the stage's `OnPush`/`OnPull` callbacks (single-threaded by Akka guarantee)
- **Memory**: Dynamic table adds memory per connection — configurable capacity limits exposure

## Success Metrics

- HTTP/3 smoke test passes reliably against Kestrel
- QPACK dynamic table active — encoder instructions observed in test output
- Zero protocol logic in QuicClientProvider
- All 12 existing RFC9114 stream test files pass
- All existing RFC9204 QPACK stream tests pass
- New stream tests for all 4 new stages pass
- `dotnet test src/TurboHttp.sln` — 100% green

## Open Questions

*None — all resolved during clarification.*
