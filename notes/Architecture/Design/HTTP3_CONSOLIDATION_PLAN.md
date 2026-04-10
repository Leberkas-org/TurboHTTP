---
title: HTTP/3 Consolidation Plan
description: >-
  Analysis of Http30Engine's 11-stage structure and a proposed consolidation
  path to ~5 stages, informed by lessons from HTTP/1.x unification (Feature 001)
tags:
  - architecture
  - http3
  - stages
  - consolidation
  - design
status: proposal
created: '2026-04-10'
feature: Feature-001 (design note only)
---
# HTTP/3 Consolidation Plan

> **Context:** This note was written as TASK-001-004 after HTTP/1.0 and HTTP/1.1 were each consolidated from 3 stages into a single unified `ConnectionStage` (TASK-001-001 through TASK-001-003). Lessons from that work directly inform the analysis below.
>
> **Scope:** Design note only. No code changes are proposed for the current feature. This informs a future feature.

## TL;DR

HTTP/3 currently uses **11 custom `GraphStage` instances** wired into `Http30Engine`. A principled consolidation can reduce this to **5 stages** by merging encoding, connection management, and QPACK feedback paths — while keeping the QUIC-specific unidirectional stream setup stages separate. Estimated effort: ~150k tokens (comparable to TASK-001-001 + TASK-001-002 combined).

---

## 1. Current 11-Stage Structure

### 1.1 Encoding Stages (request → wire)

| # | Stage | File | Shape | Purpose |
|---|-------|------|-------|---------|
| 1 | `Http30Request2FrameStage` | `Encoding/` | 1-in, 2-out (custom `Http30Request2FrameShape`) | Converts `HttpRequestMessage` → `Http3Frame` sequence (HEADERS + DATA) via QPACK. Emits QPACK encoder instructions on second outlet (`Out.Encoder`). |
| 2 | `Http30EncoderStage` | `Encoding/` | `FlowShape<Http3Frame, IOutputItem>` | Serializes `Http3Frame` objects to `NetworkBuffer` bytes via `Http3Frame.WriteTo()`. |
| 3 | `Http30ControlStreamPrefaceStage` | `Encoding/` | `FlowShape<IOutputItem, IOutputItem>` | Emits HTTP/3 control stream preface (stream type VarInt `0x00` + SETTINGS frame) on `PreStart`, then passes items through. Tags output with `OutputStreamType.Control`. |
| 4 | `Http30QpackEncoderPrefaceStage` | `Encoding/` | `FlowShape<ReadOnlyMemory<byte>, IOutputItem>` | Prepends QPACK encoder stream type (VarInt `0x02`) once on first emission, then passes QPACK instructions through. Tags output with `OutputStreamType.QpackEncoder`. |
| 5 | `QpackEncoderStreamStage` | `Encoding/` | `FlowShape<EncoderInstruction, ReadOnlyMemory<byte>>` | Serializes `EncoderInstruction` objects to bytes for the QPACK encoder unidirectional stream (RFC 9204 §4.3). |

### 1.2 Decoding Stages (wire → response)

| # | Stage | File | Shape | Purpose |
|---|-------|------|-------|---------|
| 6 | `Http30DecoderStage` | `Decoding/` | `FlowShape<IInputItem, Http3Frame>` | Deserializes raw bytes to `Http3Frame` objects. Filters unknown frame types (RFC 9114 §7.2.8). |
| 7 | `Http30ConnectionStage` | `Decoding/` | 2-in, 2-out (custom `Http30ConnectionShape`) | HTTP/3 connection-level state machine: SETTINGS/GOAWAY handling, idle timeout (30s default), push promise limits. Consolidated from 7 prior handlers. Routes frames between app and server paths. |
| 8 | `Http30StreamStage` | `Decoding/` | `FlowShape<Http3Frame, HttpResponseMessage>` | Assembles HEADERS + DATA frames → `HttpResponseMessage` using QPACK decoder. Unlike HTTP/2: no stream IDs in frames, no CONTINUATION frames. |
| 9 | `QpackDecoderStreamStage` | `Decoding/` | `FlowShape<ReadOnlyMemory<byte>, DecoderInstruction>` | Deserializes bytes from QPACK decoder unidirectional stream (RFC 9204 §4.4) to `DecoderInstruction` objects. |
| 10 | `QpackDecoderFeedbackStage` | `Decoding/` | `SinkShape<DecoderInstruction>` | Applies decoder instructions back to `QpackEncoder` state (Section Acknowledgment, Stream Cancellation, Insert Count Increment). Terminal sink — no output. |

### 1.3 Routing Stage

| # | Stage | File | Shape | Purpose |
|---|-------|------|-------|---------|
| 11 | `Http30CorrelationStage` | `Routing/` | `FanInShape<HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>` | FIFO correlation of requests and responses. Sets `response.RequestMessage = request`. HTTP/3 preserves per-connection request order. |

### 1.4 Built-in Operators in Http30Engine (not custom stages, listed for completeness)

- `Broadcast(2)` — splits requests for frame encoding and correlation
- `Partition(2, ClassifyInputItem)` — separates HTTP/3 frames from QPACK decoder feedback bytes
- `BatchWeighted` — coalesces output buffers up to 65 KB before write
- `Merge(2)` — combines frame bytes and QPACK encoder instruction bytes

---

## 2. Consolidation Targets

### 2.1 Group A: QPACK Encoder Stream (2 stages → 1)

**Merge:** `QpackEncoderStreamStage` + `Http30QpackEncoderPrefaceStage` → **`QpackEncoderStreamStage`**

**Rationale:**
- `Http30QpackEncoderPrefaceStage` has a single responsibility: prepend VarInt `0x02` (QPACK encoder stream type) on the first item, then pass through. This is identical in structure to `Http20PrependPrefaceStage` which was already absorbed directly into the HTTP/2 encoder stage.
- Emitting the stream type byte belongs naturally inside the encoder stream stage as a `_prefaceSent` flag in `PreStart` / on first push — a 5-line change.
- Eliminates one `FlowShape` in the encoding fan-out.

**Resulting shape:** `FlowShape<EncoderInstruction, IOutputItem>` (absorbs both serialization and stream-type tagging).

**Risk:** Low. Pure inline logic with no state shared across stages.

---

### 2.2 Group B: QPACK Decoder Stream + Feedback (2 stages + Partition → 1)

**Merge:** `QpackDecoderStreamStage` + `QpackDecoderFeedbackStage` + `Partition` routing → **`QpackDecoderStage`**

**Rationale:**
- The two stages are always wired sequentially with no branching: `Partition.Out1 → QpackDecoderStreamStage → QpackDecoderFeedbackStage`.
- `QpackDecoderFeedbackStage` is a `SinkShape` with zero outputs. The combined stage becomes a `SinkShape<ReadOnlyMemory<byte>>` that parses instructions and applies them inline — eliminating the intermediate `DecoderInstruction` materialization.
- Removes the `Partition(2)` operator from the engine (its QPACK branch disappears; the remaining non-QPACK branch becomes the single input to the decoder).
- Simplifies engine wiring significantly.

**Resulting shape:** `SinkShape<ReadOnlyMemory<byte>>` (consumes QPACK feedback bytes, applies to encoder, produces nothing).

**Risk:** Low-medium. The combined stage accesses `QpackEncoder` directly. Must ensure thread safety if the encoder is accessed from both the encoding path and the feedback sink. Akka.Streams fused graphs guarantee single-thread execution within a fused island — verify the QPACK encoder lives in the same island.

---

### 2.3 Group C: Frame Encoding (2 stages → 1)

**Merge:** `Http30Request2FrameStage` + `Http30EncoderStage` → **`Http30EncoderStage`**

**Rationale:**
- `Http30Request2FrameStage` outputs `Http3Frame` objects; `Http30EncoderStage` immediately consumes them and outputs `IOutputItem` bytes. There is no other consumer of the intermediate `Http3Frame`.
- Merging eliminates the intermediate materialization of `Http3Frame` structs and the edge between stages.
- The resulting stage takes `HttpRequestMessage` directly and emits `IOutputItem` bytes + QPACK encoder instructions on a second outlet.
- Retains the 2-outlet `Http30Request2FrameShape` but makes the outer type simpler: `In` is `HttpRequestMessage`, `Out.Frame` becomes `Out.Network` (encoded bytes), `Out.Encoder` unchanged.

**Resulting shape:** Custom 1-in, 2-out: `In` (`HttpRequestMessage`), `Out.Network` (`IOutputItem`), `Out.Encoder` (`EncoderInstruction`).

**Risk:** Low. Both stages are pure data transformations with no side effects. The merge is additive.

---

### 2.4 Group D: Stream Assembly + Connection + Correlation (3 stages → 1)

**Merge:** `Http30StreamStage` + `Http30ConnectionStage` + `Http30CorrelationStage` → **unified `Http30ConnectionStage`**

**Rationale:**
- This is the exact same consolidation performed for HTTP/1.0 (TASK-001-001) and HTTP/1.1 (TASK-001-002), following the established `Http20ConnectionStage` pattern.
- `Http30StreamStage` performs per-stream HEADERS+DATA assembly — analogous to the `Http11StateMachine.DecodeServerData()` function.
- `Http30CorrelationStage` performs FIFO request/response correlation — analogous to `_inFlightQueue` management.
- `Http30ConnectionStage` already houses the `ConnectionState` nested class; stream assembly and correlation become `StreamState` and `_pendingRequests` respectively, following `Http20ConnectionStage` verbatim.
- Removes one `FanInShape` routing stage and simplifies the encoding/decoding split in the engine.

**Resulting shape:** The existing 4-port `Http30ConnectionShape` is preserved: `In.Server`, `In.App`, `Out.App`, `Out.Server`. The `Out.App` outlet now emits fully-assembled, correlated `HttpResponseMessage` directly.

**Risk:** Medium. This is the most complex consolidation. `Http30ConnectionStage.ConnectionState` currently handles connection-level signals only; adding stream assembly brings QPACK decoding and response body buffering inside. Care required for:
- QPACK decoder state shared with `QpackDecoderFeedbackStage` (see Group B — resolve Group B first)
- Push promise handling (currently validated at connection level, may interact with stream assembly)
- Memory pool lifetime for response body buffers (must call `Dispose` in `PostStop`)

---

## 3. Stages That Must Remain Separate

### 3.1 `Http30ControlStreamPrefaceStage` — Keep as-is

**Reason:** HTTP/3 requires the control stream preface (stream type `0x00` + SETTINGS frame) to be sent on a **dedicated unidirectional stream**, distinct from request streams. The stage tags its output with `OutputStreamType.Control` for demux routing by the transport layer. This tagging logic is control-stream-specific and does not compose naturally with request encoding. Inlining it would introduce transport-layer concerns (stream type tagging) into the encoder.

**RFC reference:** RFC 9114 §6.2.1 — control stream is a separate QUIC unidirectional stream.

### 3.2 QPACK as Separate Sub-pipeline (encoder + decoder)

**Reason:** QPACK encoder instructions and decoder feedback travel on **separate QUIC unidirectional streams** (stream types `0x02` and `0x03`). The encoding and decoding paths are separate from request/response data streams by design. While the stages can be simplified (Groups A and B above), the QPACK sub-pipeline must remain architecturally separate from the request/response sub-pipeline — it cannot be folded into `Http30ConnectionStage` without conflating two independent QUIC stream types.

**RFC reference:** RFC 9204 §4.2–§4.4.

---

## 4. Proposed Target Architecture

### 4.1 Stage Count

| Before | After |
|--------|-------|
| 11 custom stages | 5 custom stages |
| 4 built-in operators | 2 built-in operators (`BatchWeighted`, `Broadcast`) |

### 4.2 Target Stage List

| Stage | Consolidated From | Notes |
|-------|-------------------|-------|
| `Http30EncoderStage` | `Http30Request2FrameStage` + `Http30EncoderStage` | Custom 1-in, 2-out shape: `In`, `Out.Network`, `Out.Encoder` |
| `Http30ControlStreamPrefaceStage` | (unchanged) | Must remain separate — see §3.1 |
| `Http30ConnectionStage` | `Http30DecoderStage` + `Http30ConnectionStage` + `Http30StreamStage` + `Http30CorrelationStage` | 4-port shape preserved; absorbs stream assembly and FIFO correlation |
| `QpackEncoderStreamStage` | `QpackEncoderStreamStage` + `Http30QpackEncoderPrefaceStage` | Absorbs preface emission in `PreStart`; emits `IOutputItem` directly |
| `QpackDecoderStage` | `QpackDecoderStreamStage` + `QpackDecoderFeedbackStage` | New `SinkShape<ReadOnlyMemory<byte>>`; removes `Partition` from engine |

### 4.3 Simplified Engine Wiring

```text
Encoding Path:
  Broadcast(2)
    ├── Http30EncoderStage (Out.Network) → BatchWeighted → Http30ControlStreamPrefaceStage
    └── Http30EncoderStage (Out.Encoder) → QpackEncoderStreamStage

Decoding Path:
  Http30ConnectionStage (Out.App) → correlated HttpResponseMessage
  Http30ConnectionStage (In.Server) ← raw IInputItem bytes

QPACK Feedback:
  inbound QPACK bytes → QpackDecoderStage (sink)
```

Compared to current engine: removes `Merge(2)`, `Partition(2)`, `Http30DecoderStage`, `Http30StreamStage`, `Http30CorrelationStage`, `Http30QpackEncoderPrefaceStage`, `QpackDecoderFeedbackStage`.

---

## 5. Recommended Implementation Order

Tackle Group B first (lowest risk, removes complexity from engine routing), then Group A, then Group C, then Group D last (highest risk, most reward).

1. **Group B** — `QpackDecoderStage` consolidation: eliminates `Partition`, simplifies engine
2. **Group A** — `QpackEncoderStreamStage` absorbs preface: pure additive change
3. **Group C** — `Http30EncoderStage` absorbs request2frame: eliminates intermediate `Http3Frame` edge
4. **Group D** — Unified `Http30ConnectionStage`: largest change, implement after QPACK is clean

---

## 6. Blockers and Risks

| Blocker / Risk | Severity | Mitigation |
|----------------|----------|------------|
| QPACK encoder thread-safety between Group C (encoder instruction emission) and Group B (feedback sink) | Medium | Confirm both stages fuse into the same Akka.Streams island. If so, single-threaded execution guarantees eliminate the concern. |
| Push promise handling in `Http30ConnectionStage.ConnectionState` interacts with stream assembly (Group D) | Medium | Push promises are currently validated and rejected at connection level (limit = 0). Stream-level assembly will not see push promise frames in current configuration. Future push support would require revisiting. |
| `Http30Request2FrameShape` is a custom type — callers outside the engine may reference it | Low | Grep confirms it is only referenced inside `Http30Engine.cs`. Safe to replace. |
| QPACK dynamic table is shared state between encoder path and decoder feedback path | Low-Medium | Encapsulate `IQpackEncoder` / `IQpackDecoder` lifecycle within the new `QpackDecoderStage` constructor injection, matching how HTTP/2's `IHpackEncoder` is injected into `Http20ConnectionStage`. |
| HTTP/3 integration tests are slow — full suite regressions may not surface until CI | Low | Run per-class: `dotnet run --project TurboHTTP.IntegrationTests -- -namespace "TurboHTTP.IntegrationTests.H3"` after each group. |
| `MemoryPool<byte>` response body buffers in `Http30StreamStage` must be properly disposed when consolidated | Medium | Follow `Http11StateMachine` pattern: call `Dispose` in `PostStop` of the unified `Http30ConnectionStage`. Add dedicated test for teardown during mid-response connection close. |

---

## 7. Effort Estimate

| Group | Stages Removed | Complexity | Estimated Tokens |
|-------|---------------|------------|------------------|
| A — QpackEncoderStream | 1 | Low | ~15k |
| B — QpackDecoderStage | 2 + Partition | Low-Medium | ~25k |
| C — Http30EncoderStage | 1 + intermediate edge | Low | ~20k |
| D — Http30ConnectionStage | 3 + FanIn | Medium-High | ~90k |
| Tests + cleanup | — | Medium | ~40k |
| **Total** | **7 stages + 2 operators** | — | **~190k** |

---

## 8. Success Metrics

- Net removal of 7 custom stage files + 1 custom shape class (`Http30Request2FrameShape`)
- `Http30Engine.cs` wiring: from ~80 lines of `GraphDsl` to ~30 lines
- Architectural consistency: `Http30ConnectionStage` follows the same pattern as `Http10ConnectionStage`, `Http11ConnectionStage`, and `Http20ConnectionStage`
- Zero regressions across all test projects

---

## See Also

- [[Architecture/Design/02-STAGE_PATTERNS|GraphStage Patterns]] — Port naming and stage lifecycle conventions
- [[Architecture/Layers/15-STREAMS_LAYER|Streams Layer]] — Full pipeline data flow and per-version engine assembly
- `src/TurboHTTP/Streams/Http30Engine.cs` — Current engine wiring
- `src/TurboHTTP/Streams/Stages/Decoding/Http20ConnectionStage.cs` — Pattern to follow for Group D
