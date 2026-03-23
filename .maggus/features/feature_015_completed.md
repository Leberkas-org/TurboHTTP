# Feature 015: Fuzzing — HTTP/2 Frame Parser & HPACK Decoder

## Introduction

Extend the existing HTTP/2 fuzz harness with additional attack vectors and add comprehensive HPACK decoder fuzzing. The existing `RFC9113/21_FuzzHarnessTests.cs` covers random valid frame sequences — this feature adds adversarial, malformed, and boundary-value inputs.

### Architecture Context

- **Components involved:** Http2FrameDecoder (Protocol/RFC9113), HpackDecoder (Protocol/RFC7541), HpackDynamicTable
- **Existing fuzz tests:** `21_FuzzHarnessTests.cs` — 4+ tests with seeded Random for valid frame sequences (HEADERS, DATA, RST_STREAM, WINDOW_UPDATE, PING/SETTINGS interleaving)
- **Gap:** No adversarial frame fuzzing, no HPACK decoder fuzzing, no boundary-value testing

## Mandatory Testing Policy

> **Tests are required and must never be skipped, disabled, or marked as `[Skip]`.** Every acceptance criterion that specifies "All tests pass" is a hard gate — the feature is not complete until every test passes. No `[Fact(Skip = "...")]`, no `#if` guards, no `Assert.Skip()`. If a test fails, fix the code or the test — never suppress it.

## Goals

- Verify Http2FrameDecoder handles malformed frames gracefully
- Verify HpackDecoder handles adversarial compressed headers without OOM or crash
- Verify boundary values (max frame size, max table size, max header count)
- Extend existing fuzz harness, don't replace it

## Tasks

### TASK-015-001: HTTP/2 Frame Parser Adversarial Fuzzing
**Description:** As a security engineer, I want to fuzz the HTTP/2 frame parser with malformed and adversarial inputs.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** none
**Parallel:** yes — can run alongside TASK-015-002

**Acceptance Criteria:**
- [x] Test file `src/TurboHttp.Tests/Security/Http2FrameFuzzTests.cs`
- [x] Tests with `[Theory]` + `[InlineData(seed)]` for fixed seeds
- [x] Fuzz categories:
  - Pure random bytes (9–1024 bytes, mimicking frame header + payload) → no crash
  - Valid 9-byte frame header + random payload → graceful error or valid parse
  - Frame with length field > actual payload → partial frame handling
  - Frame with length field = 0 → valid for some types, error for others
  - Unknown frame type (0xFF) → must be ignored per RFC 9113 §4.1
  - Frame with reserved bit set → must be ignored
  - Oversized frame (length > 16384 default max) → `Http2Exception` or skip
  - Rapid alternation of valid/invalid frames → state machine consistency
  - SETTINGS frame with unknown parameters → ignored, not error
  - WINDOW_UPDATE with 0 increment → `PROTOCOL_ERROR`
- [x] Each iteration bounded by 5-second timeout
- [x] All tests pass

### TASK-015-002: HPACK Decoder Fuzzing
**Description:** As a security engineer, I want to fuzz the HPACK decoder with adversarial compressed header blocks.

**Token Estimate:** ~50k tokens
**Predecessors:** none
**Successors:** none
**Parallel:** yes — can run alongside TASK-015-001
**Model:** opus

**Acceptance Criteria:**
- [x] Test file `src/TurboHttp.Tests/Security/HpackFuzzTests.cs`
- [x] Tests with `[Theory]` + `[InlineData(seed)]` for fixed seeds
- [x] Fuzz categories:
  - Pure random bytes (1–4KB) → `HpackException`, never crash or hang
  - Huffman-encoded random data → decoded or `HpackException`, never crash
  - Dynamic table size update to MAX (16384) followed by flood of indexed headers → bounded memory
  - Indexed reference to entry 0 (invalid) → `HpackException`
  - Indexed reference beyond table size → `HpackException`
  - String literal with length prefix > remaining bytes → graceful error
  - Incremental indexing with very long name+value (>8KB each) → bounded
  - Never-indexed sensitive header reconstruction → correct decode
  - Dynamic table with 1000+ entries → eviction works, no OOM
  - Truncated header block (cut mid-instruction) → `HpackException`
- [x] Boundary value tests (not randomized):
  - Max header table size (SETTINGS_HEADER_TABLE_SIZE = 65536)
  - Max header list size (SETTINGS_MAX_HEADER_LIST_SIZE = 65536)
  - Header with 0-length name → `HpackException`
  - Header with 0-length value → valid (empty value is allowed)
  - Integer overflow in HPACK integer decoding (>2^31) → `HpackException`
- [x] Memory assertion: decoder + dynamic table stay below 256KB for any single header block ≤4KB input
- [x] All tests pass

## Task Dependency Graph

```
TASK-015-001 (standalone)
TASK-015-002 (standalone)
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-015-001 | ~40k | none | yes (with 002) | — |
| TASK-015-002 | ~50k | none | yes (with 001) | opus |

**Total estimated tokens:** ~90k

## Functional Requirements

- FR-1: Http2FrameDecoder must never crash on any 9+ byte input
- FR-2: HpackDecoder must never crash or hang on any byte sequence
- FR-3: HpackDynamicTable must respect configured size limits under all conditions
- FR-4: Huffman decoding must terminate on all inputs (no infinite loop)
- FR-5: Integer decoding must detect overflow (HPACK uses 7-bit prefix integers that can encode arbitrary values)
- FR-6: All fuzz tests must be deterministically reproducible

## Non-Goals

- No QPACK fuzzing (separate future work)
- No coverage-guided fuzzing
- No HTTP/2 session-level fuzzing (connection state machine with multiple frames)

## Technical Considerations

- HPACK decoder is stateful (dynamic table) — fuzz sequences should test multi-decode patterns
- Huffman codec uses a static lookup table — adversarial inputs should test all 256 byte values
- The existing `21_FuzzHarnessTests.cs` tests valid frame sequences — this feature tests INVALID inputs
- Frame decoder returns `ReadOnlyMemory<byte>` slices — ensure no out-of-bounds reads

## Success Metrics

- 1000+ fuzz iterations per category with 0 crashes
- Memory stays bounded: dynamic table never exceeds configured max + overhead
- No test hangs (5-second hard timeout per iteration)
