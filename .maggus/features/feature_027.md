<!-- maggus-id: 20260326-163700-feature-027 -->

# Feature 027: Phase 2 — HTTP/3 QPACK Encoder (RFC Compliance Blocker)

## Introduction

Implement full HTTP/3 QPACK encoder (RFC 9204 §4) — the critical blocker for HTTP/3 client support. Currently, only the decoder exists; the encoder is missing, making it impossible for the client to send requests over HTTP/3.

QPACK is header compression for HTTP/3 (analogous to HPACK for HTTP/2). It differs from HPACK by supporting *out-of-order* decompression via blocking references and separate encoder/decoder instruction streams.

### Architecture Context

- **Vision alignment:** TurboHttp aims to support "HTTP/1.0, 1.1, 2.0, and 3.0 (QUIC)" — Phase 2 is required for H3 support
- **Components involved:**
  - `TurboHttp/Protocol/RFC9204/QpackEncoder` — main encoder (missing, needs implementation)
  - `TurboHttp/Protocol/RFC9204/QpackDecoder` — existing (used for decoding)
  - `TurboHttp/Streams/Stages/Encoding/Http30RequestEncoder` — uses QPACK encoder to serialize requests
  - `TurboHttp/Streams/Http30Engine` — HTTP/3 multiplexing engine
- **New patterns:** Encoder state machine (dynamic table, instruction streaming, section acknowledgment)
- **Architecture updates needed:** CLAUDE.md should document QPACK encoder/decoder interaction and instruction stream mechanics

## Goals

1. **Implement QPACK Encoder:** Full encoder with dynamic table, literal representations, and instruction generation
2. **Support Blocking References:** Encoder produces encoder instructions for decoder to synchronize state
3. **HTTP/3 Integration:** Http30RequestEncoder uses QPACK encoder to compress headers
4. **Stream Synchronization:** Encoder/decoder streams (separate from request stream) exchange instructions
5. **Comprehensive Testing:** Unit + integration + interop (real Kestrel H3) coverage

## Tasks

### TASK-027-001: Design QPACK Encoder State Machine
**Description:** As an architect, I want to design the QPACK encoder state machine so that implementation is systematic and maintainable.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-027-002, TASK-027-003
**Parallel:** yes — can run alongside TASK-027-004

**Acceptance Criteria:**
- [ ] Create design document (`docs/QPACK_ENCODER_DESIGN.md`) covering:
  - [ ] Encoder state (dynamic table, block reservation counter, highest inserted name index)
  - [ ] Instruction types: INSERT_WITH_NAME_REF, INSERT_WITH_LITERAL_NAME, DUPLICATE, SECTION_ACKNOWLEDGMENT
  - [ ] Literal representation types (indexed, literal w/ name ref, literal w/o name ref)
  - [ ] Blocking reference semantics (client can reference decoder entries not yet known)
  - [ ] Required order of operations (encode headers, generate instructions, serialize instructions)
- [ ] Document example: encode `{"content-type": "text/html", "content-length": "1234"}` with step-by-step state transitions
- [ ] Document public API (`QpackEncoder.Encode()` signature, return type)
- [ ] Identify edge cases: sensitive headers, large header values, dynamic table eviction

---

### TASK-027-002: Implement Core QPACK Encoder (Indexing & Literal Logic)
**Description:** As a developer, I want the core encoder logic so that headers can be compressed into QPACK representation.

**Token Estimate:** ~60k tokens
**Predecessors:** TASK-027-001
**Successors:** TASK-027-004
**Parallel:** no — depends on design

**Acceptance Criteria:**
- [ ] Create `src/TurboHttp/Protocol/RFC9204/QpackEncoder.cs` with:
  - [ ] Public method: `void Encode(IEnumerable<(string name, string value)> headers, ref Span<byte> output, out QpackEncoderInstructions instructions)`
  - [ ] Or similar signature that returns both encoded block AND instructions
  - [ ] Dynamic table management:
    - [ ] `_dynamicTable: List<(string name, string value)>` with max size (default 4096 bytes)
    - [ ] Track insertion point, eviction FIFO
    - [ ] Update table size via encoder instructions
  - [ ] Encoding logic per header:
    - [ ] Check static table (RFC 9204 Appendix A) — if match, use indexed (6-bit prefix)
    - [ ] Check dynamic table — if match, generate INSERT_WITH_NAME_REF or duplicate
    - [ ] If no match, generate INSERT_WITH_LITERAL_NAME with value
    - [ ] Sensitive headers (Authorization, Cookie) — use literal never-indexed (per RFC 9110)
  - [ ] Output:
    - [ ] Encoded header block (bytes) — field line representations packed into output buffer
    - [ ] Encoder instructions — separate byte sequence for encoder stream (INSERT, DUPLICATE, etc.)
- [ ] Handle edge cases:
  - [ ] Header name/value with special characters (percent-encoding if needed)
  - [ ] Large header values (>4KB) — decide on truncation or streaming
  - [ ] Dynamic table full — evict oldest entries (FIFO)
  - [ ] Duplicate detection — reuse existing entry instead of inserting
- [ ] Compile with zero warnings
- [ ] Performance: encoding 50 headers < 100µs

---

### TASK-027-003: Implement QPACK Encoder Instruction Generation
**Description:** As a developer, I want encoder instruction generation so that the decoder stream can synchronize encoder state.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-027-001
**Successors:** TASK-027-005
**Parallel:** no — depends on design; can start after TASK-027-002

**Acceptance Criteria:**
- [ ] Create instruction serialization in `QpackEncoder`:
  - [ ] Instruction types:
    - [ ] INSERT_WITH_NAME_REF (single entry static/dynamic table reference)
    - [ ] INSERT_WITH_LITERAL_NAME (full literal name+value)
    - [ ] DUPLICATE (copy existing entry in dynamic table)
    - [ ] TABLE_SIZE_SYNCHRONIZATION (update max table size)
  - [ ] Serialize instructions to byte sequence (separate from header block)
  - [ ] Each instruction uses variable-length integer encoding for indices (RFC 9204 §3.2)
- [ ] Instructions are emitted in chronological order (deterministic)
- [ ] Separate method: `void GetEncoderInstructions(ref Span<byte> output)` — returns pending instructions
- [ ] After instructions are serialized, clear pending instruction queue (idempotent)
- [ ] Unit tests verify instruction byte sequences match RFC 9204 examples
- [ ] Compile with zero warnings

---

### TASK-027-004: Implement QPACK Encoder Configuration & Thread Safety
**Description:** As a developer, I want encoder configuration and thread-safe state management so that the encoder works correctly in concurrent scenarios.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-027-001
**Successors:** TASK-027-005
**Parallel:** yes — can run alongside TASK-027-002

**Acceptance Criteria:**
- [ ] Create `QpackEncoderOptions` class:
  - [ ] `MaxDynamicTableSize` (default 4096 bytes, RFC 9204 default)
  - [ ] `BlockedStreamsAllowed` (default true — allow blocking references)
  - [ ] `SensitiveHeadersNeverIndexed` (default true — per RFC 9110)
- [ ] Constructor: `QpackEncoder(QpackEncoderOptions options)`
- [ ] Thread safety:
  - [ ] If encoder is used from multiple threads, synchronize dynamic table updates with `lock`
  - [ ] Instruction queue must be thread-safe (ConcurrentQueue or lock-protected)
  - [ ] Document: "QpackEncoder is NOT thread-safe by default; wrap in lock if used concurrently"
- [ ] Configuration:
  - [ ] Allow custom table size (for testing, limits)
  - [ ] Allow disabling blocked references (for strict interop)
- [ ] Unit tests verify thread safety (if applicable)
- [ ] Compile with zero warnings

---

### TASK-027-005: Integrate QPACK Encoder into Http30RequestEncoder
**Description:** As a developer, I want Http30RequestEncoder to use the QPACK encoder so that HTTP/3 requests can be sent.

**Token Estimate:** ~35k tokens
**Predecessors:** TASK-027-002, TASK-027-003
**Successors:** TASK-027-007
**Parallel:** no — depends on encoder being complete

**Acceptance Criteria:**
- [ ] Modify `src/TurboHttp/Streams/Stages/Encoding/Http30RequestEncoder.cs`:
  - [ ] Add `_qpackEncoder: QpackEncoder` field
  - [ ] Constructor accepts or creates QpackEncoder instance
  - [ ] Modify encoding method to:
    - [ ] Extract headers from `HttpRequestMessage`
    - [ ] Serialize pseudo-headers (`:method`, `:scheme`, `:authority`, `:path`)
    - [ ] Call `QpackEncoder.Encode()` to compress all headers
    - [ ] Serialize resulting header block into Http3 HEADERS frame payload
    - [ ] Queue encoder instructions for sending on encoder stream (separate from request stream)
- [ ] Maintain state across multiple requests:
  - [ ] Reuse same QpackEncoder instance for connection lifetime
  - [ ] Dynamicable updates synchronize between encoder and decoder
- [ ] Error handling:
  - [ ] If encoding fails, throw Http3Exception
  - [ ] If instruction queue overflows, propagate error
- [ ] Unit tests verify header encoding and instruction queueing
- [ ] Compile with zero warnings

---

### TASK-027-006: Implement QPACK Encoder Stream Management (RFC 9204 §4.1)
**Description:** As a developer, I want separate encoder instruction streaming so that decoder stays in sync.

**Token Estimate:** ~35k tokens
**Predecessors:** TASK-027-005
**Successors:** TASK-027-008
**Parallel:** no — requires Http30RequestEncoder using encoder

**Acceptance Criteria:**
- [ ] Create encoder instruction stream management:
  - [ ] Encoder instructions are sent on a separate unidirectional stream (stream ID = 0x2 for H3, per RFC 9114)
  - [ ] Http30Engine routes encoder instructions to encoder stream
  - [ ] Decoder instruction stream (stream ID = 0x3) is handled separately for decoder state sync
- [ ] Implement buffer/queue for pending instructions:
  - [ ] Instructions are buffered until encoder stream is ready
  - [ ] Batched sending (e.g., send 5 instructions per frame) to reduce overhead
  - [ ] Backpressure: if queue > 1000 instructions, apply backpressure to request encoding
- [ ] State synchronization:
  - [ ] Encoder and decoder must both acknowledge receipt (RFC 9204 §4.3.1 — SECTION_ACKNOWLEDGMENT)
  - [ ] After encoder sends instructions, wait for decoder to acknowledge before encoding next header block
- [ ] Error recovery:
  - [ ] If instruction stream closes unexpectedly, close connection with QPACK_ERROR
- [ ] Unit tests verify instruction streaming and buffering
- [ ] Compile with zero warnings

---

### TASK-027-007: Unit Tests — QPACK Encoder (RFC 9204 Examples)
**Description:** As a developer, I want comprehensive unit tests so that QPACK encoding is verified against RFC examples.

**Token Estimate:** ~45k tokens
**Predecessors:** TASK-027-005
**Successors:** TASK-027-009
**Parallel:** yes — can run alongside TASK-027-006

**Acceptance Criteria:**
- [ ] Create `src/TurboHttp.Tests/RFC9204/NN_QpackEncoderTests.cs` with 30+ test cases:
  - **Basic Encoding:**
    - [ ] Single header (static table hit) → encoded correctly
    - [ ] Single header (dynamic table hit) → encoded correctly
    - [ ] Single header (no match) → INSERT_WITH_LITERAL_NAME generated
    - [ ] Multiple headers (mixed static/dynamic) → all encoded correctly
  - **Dynamic Table:**
    - [ ] Insert header → dynamic table size increases
    - [ ] Dynamic table full → oldest entry evicted (FIFO)
    - [ ] Table update (SETTINGS) → max size changes
  - **Sensitive Headers:**
    - [ ] Authorization header → never-indexed flag set
    - [ ] Cookie header → never-indexed flag set
    - [ ] Regular header → indexed allowed
  - **RFC 9204 Examples:**
    - [ ] Appendix A.1 example (e.g., GET request) → output matches RFC byte-for-byte
    - [ ] Appendix A.2 example (POST request) → output matches RFC
    - [ ] Multiple header encoding → block format matches RFC
  - **Instructions:**
    - [ ] INSERT_WITH_NAME_REF instruction format matches RFC
    - [ ] DUPLICATE instruction format matches RFC
    - [ ] TABLE_SIZE instruction format matches RFC
  - **Edge Cases:**
    - [ ] Empty header list → encoded correctly
    - [ ] Very long header value (>4KB) → handled gracefully
    - [ ] Unicode header value → UTF-8 encoded
- [ ] All tests use `[Fact(Timeout = 5000)]`
- [ ] All tests have clear `DisplayName` with RFC 9204 section reference
- [ ] Total test methods ≥ 30, all passing

---

### TASK-027-008: Integration Tests — HTTP/3 with Kestrel H3
**Description:** As a developer, I want end-to-end tests so that HTTP/3 requests work with real Kestrel H3 server.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-027-006
**Successors:** TASK-027-009
**Parallel:** no — requires full encoder + instruction stream

**Acceptance Criteria:**
- [ ] Create Kestrel H3 fixture (if not exist): `KestrelH3Fixture` with HTTP/3 enabled
  - [ ] Listen on HTTPS with self-signed cert
  - [ ] Register routes: GET /hello, POST /echo, GET /largebody
- [ ] Create integration test in `src/TurboHttp.IntegrationTests/Http3EncoderTests.cs`:
  - [ ] Send GET request over HTTP/3 → verify response received
  - [ ] Send POST request with body over HTTP/3 → verify echo response
  - [ ] Send 10 pipelined requests over HTTP/3 → all succeed
  - [ ] Send request with large response body (>10MB) → stream correctly
  - [ ] Verify encoder instructions are sent correctly (may need to inspect wire format)
- [ ] Error scenarios:
  - [ ] Send malformed request → Kestrel rejects with 400 or QPACK_ERROR
  - [ ] Kill connection mid-stream → cleanup correctly
- [ ] Performance:
  - [ ] 100 HTTP/3 requests < 1s (all serial)
  - [ ] Memory stable (no leaks)
- [ ] Run via `dotnet test src/TurboHttp.IntegrationTests` — all passing

---

### TASK-027-009: Validation & Interop Testing (vs. HTTP/3 Spec)
**Description:** As a developer, I want interop validation so that the encoder works with multiple HTTP/3 servers and tools.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-027-007, TASK-027-008
**Successors:** none
**Parallel:** no — requires all encoder work complete

**Acceptance Criteria:**
- [ ] Create `src/TurboHttp.Tests/RFC9204/NN_QpackInteropTests.cs`:
  - [ ] Encode a request and decode with `QpackDecoder` (roundtrip test)
  - [ ] Verify instruction stream format matches RFC 9204 word-for-word
  - [ ] Verify encoder/decoder state stays synchronized after 100 requests
  - [ ] Test against example vectors from RFC 9204 Appendix B (if available)
- [ ] Manual interop testing (if available):
  - [ ] h2load (Google's HTTP/2 benchmarking tool, may support H3)
  - [ ] quiche (Cloudflare's QUIC library) echo server
  - [ ] Document interop results in `docs/INTEROP_RESULTS.md`
- [ ] CI/CD:
  - [ ] All tests pass in `dotnet test`
  - [ ] Zero compiler warnings
  - [ ] Memory profiling shows stable state (no leaks)
- [ ] Performance validation:
  - [ ] Encoding 50 headers < 100µs
  - [ ] Instruction generation < 50µs per request
  - [ ] CPU usage < 5% of total per-request time

---

## Task Dependency Graph

```
TASK-027-001 ──→ TASK-027-002 ──→ TASK-027-005 ──→ TASK-027-006 ──→ TASK-027-008
                                                                        ↓
TASK-027-001 ──→ TASK-027-003 ──────────────────→ TASK-027-005        (TASK-027-009)
                                                         ↓
TASK-027-001 ──→ TASK-027-004 ──────────────────→ TASK-027-005

TASK-027-005 ──→ TASK-027-007 ──→ TASK-027-009
```

### Dependency Table

| Task | Estimate | Predecessors | Successors | Parallel | Model |
|------|----------|--------------|-----------|----------|-------|
| TASK-027-001 | ~25k | none | 002, 003, 004 | yes (with 004) | opus |
| TASK-027-002 | ~60k | 001 | 004, 005 | no | opus |
| TASK-027-003 | ~40k | 001 | 005 | no | — |
| TASK-027-004 | ~30k | 001 | 005 | yes (with 001) | — |
| TASK-027-005 | ~35k | 002, 003 | 006, 007 | no | — |
| TASK-027-006 | ~35k | 005 | 008 | no | opus |
| TASK-027-007 | ~45k | 005 | 009 | yes (with 006) | — |
| TASK-027-008 | ~50k | 006 | 009 | no | — |
| TASK-027-009 | ~30k | 007, 008 | none | no | — |

**Total estimated tokens:** ~350k tokens (~1 week with two engineers, 8.75 days solo)

---

## Functional Requirements

1. **FR-1:** QPACK encoder SHALL compress HTTP headers into RFC 9204-compliant format
2. **FR-2:** Encoder SHALL maintain dynamic table (default 4096 bytes) and evict entries FIFO when full
3. **FR-3:** Encoder SHALL emit INSERT_WITH_NAME_REF, INSERT_WITH_LITERAL_NAME, DUPLICATE, and TABLE_SIZE instructions
4. **FR-4:** Sensitive headers (Authorization, Cookie) SHALL be encoded with never-indexed flag
5. **FR-5:** Encoder instructions SHALL be serialized to separate encoder stream (stream ID = 0x2)
6. **FR-6:** Decoder SHALL remain synchronized with encoder via instruction stream exchange
7. **FR-7:** Http30RequestEncoder SHALL use QpackEncoder to compress request headers
8. **FR-8:** Encoder/decoder state SHALL be deterministic and verifiable against RFC 9204 examples

---

## Non-Goals

- **No server-side QPACK** — Client encoder only
- **No decoder improvements** — Decoder is already complete; this feature is encoder-only
- **No QUIC transport** — Assumes QUIC transport layer exists (RFC 9000, out of scope)
- **No dynamic table size limits** — Default 4096 is used; custom sizes allowed but no enforcement of advertised limits
- **No instruction queue persistence** — Instructions are volatile; lost on connection close

---

## Technical Considerations

### Encoder State Machine
- Encoder maintains `_highestInsertedNameIndex` (RFC 9204 §4.1.2) to track insertion point
- Dynamic table inserts are FIFO; eviction removes oldest entries first
- Blocking references allow encoder to reference entries not yet in decoder (RFC 9204 §4.1.3) — decoder caches instructions until it can apply them

### Stream Synchronization
- Encoder stream (0x2) is separate from request/response streams
- Decoder stream (0x3) carries decoder instructions back to encoder
- SECTION_ACKNOWLEDGMENT messages confirm decoder has processed header blocks
- If instruction stream closes unexpectedly, send QPACK_ENCODER_ERROR or QPACK_DECODER_ERROR

### Integration Points
- Http30Engine routes encoder/decoder instructions to correct streams
- Http30RequestEncoder calls QpackEncoder before serializing HEADERS frame
- QpackDecoder is updated separately (outside this feature) for encoder instruction handling

### Testing Strategy
- Unit tests validate encoding against RFC examples
- Integration tests use Kestrel H3 (if available in test environment)
- Roundtrip tests (encode → decode) verify fidelity
- Interop tests with external H3 tools (h2load, quiche)

---

## Success Metrics

1. **Completeness:** All instruction types (INSERT, DUPLICATE, TABLE_SIZE) implemented and tested
2. **Correctness:** Encoder output matches RFC 9204 examples byte-for-byte
3. **Performance:** Encoding <100µs per 50-header request
4. **Interop:** Works with Kestrel H3 and other H3 implementations
5. **Stability:** Zero memory leaks, stable state after 1000+ requests
6. **Testing:** ≥ 30 unit tests + 5+ integration tests, all passing

---

## Open Questions

1. **Blocking References — Implementation:**
   - Should encoder allow blocking references, or require strict streaming (RFC 9204 §2.1.1)?
   - **Current assumption:** Blocking references allowed (more flexible); add option to disable for strict mode

2. **Instruction Buffering — Size Limit:**
   - What's the max pending instruction queue size before backpressure kicks in?
   - **Current assumption:** 1000 instructions (~8KB); configurable

3. **State Synchronization — Timing:**
   - Should encoder wait for SECTION_ACKNOWLEDGMENT before encoding next request, or speculatively encode ahead?
   - **Current assumption:** Wait for acknowledgment (safer, may impact latency)

4. **Dynamic Table Size — Synchronization:**
   - Should table size be coordinated with decoder via SETTINGS frame, or encoder-driven?
   - **Current assumption:** Encoder-driven (encoder controls table size, decoder follows instructions)

5. **Error Handling — Stream Failures:**
   - If encoder stream fails, should encoder queue instructions indefinitely or fail requests?
   - **Current assumption:** Fail requests with Http3Exception after timeout (30s)

---

## Implementation Notes

- **RFC Reference:** RFC 9204 §4 (Encoding) and RFC 9114 §4.1 (HTTP/3 field syntax)
- **Prerequisite:** Kestrel H3 support available in test environment (may require .NET 8+)
- **Dependency:** Assumes QUIC transport and HTTP/3 stream types already exist
- **Documentation:** Create `docs/QPACK_ENCODER_DESIGN.md` for architectural reference

