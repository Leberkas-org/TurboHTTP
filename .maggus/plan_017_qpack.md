# Plan: QPACK (RFC 9204) — Header Compression for HTTP/3

## Introduction

QPACK is the header compression codec for HTTP/3, analogous to HPACK for HTTP/2. QPACK is NOT compatible with HPACK — it uses absolute indexing, encoder/decoder instruction streams, and a different block structure. This plan defines the complete QPACK implementation.

**Dependency**: plan_016 Phase 1-3 (HTTP/3 frame foundation) must be in place first.
**Reuse**: `HuffmanCodec` (RFC 7541 §5.2) is identical and directly reusable.

## Goals

- Complete QPACK encoder and decoder
- Dynamic table with absolute indexing
- Encoder/decoder instruction streams
- Sensitive header protection (NEVERINDEX)
- Integration with Http3RequestEncoder and Http3ResponseDecoder
- ~80 tests with RFC-tagged DisplayNames

## User Stories

### PHASE 1 — Static Table & Integer Codec (Week 1)

### TASK-001: QPACK Static Table

**RFC**: 9204 §3.1, Appendix A

**Implementation**: `src/TurboHttp/Protocol/RFC9204/QpackStaticTable.cs` — 99 entries
**Tests**: `src/TurboHttp.Tests/RFC9204/01_StaticTableTests.cs` — 6 tests
**Effort**: 3-4h

### TASK-002: QPACK Integer Representation

**RFC**: 9204 §4.1.1

**Implementation**: `src/TurboHttp/Protocol/RFC9204/QpackIntegerCodec.cs`
**Tests**: `src/TurboHttp.Tests/RFC9204/02_IntegerCodecTests.cs` — 8 tests
**Effort**: 2-3h

### TASK-003: QPACK String Literal Representation

**RFC**: 9204 §4.1.2. Reuses `HuffmanCodec` directly.

**Implementation**: `src/TurboHttp/Protocol/RFC9204/QpackStringCodec.cs`
**Tests**: `src/TurboHttp.Tests/RFC9204/03_StringCodecTests.cs` — 6 tests
**Effort**: 2-3h

### PHASE 2 — Dynamic Table (Week 1-2)

### TASK-004: QPACK Dynamic Table

**RFC**: 9204 §3.2. Absolute indexing, insert count tracking, capacity management.

**Implementation**: `src/TurboHttp/Protocol/RFC9204/QpackDynamicTable.cs`
**Tests**: `src/TurboHttp.Tests/RFC9204/04_DynamicTableTests.cs` — 8 tests
**Effort**: 4-6h

### PHASE 3 — Encoder/Decoder Instructions (Week 2-3)

### TASK-005: Encoder Instruction Stream Writer

**RFC**: 9204 §4.3

**Implementation**: `src/TurboHttp/Protocol/RFC9204/QpackEncoderInstructionWriter.cs`
**Tests**: `src/TurboHttp.Tests/RFC9204/05_EncoderInstructionTests.cs` — 8 tests
**Effort**: 4-6h

### TASK-006: Decoder Instruction Stream Writer

**RFC**: 9204 §4.4

**Implementation**: `src/TurboHttp/Protocol/RFC9204/QpackDecoderInstructionWriter.cs`
**Tests**: `src/TurboHttp.Tests/RFC9204/06_DecoderInstructionTests.cs` — 6 tests
**Effort**: 3-4h

### TASK-007: Instruction Stream Parser

**RFC**: 9204 §4.3, §4.4

**Implementation**: `src/TurboHttp/Protocol/RFC9204/QpackInstructionDecoder.cs`
**Tests**: `src/TurboHttp.Tests/RFC9204/07_InstructionDecoderTests.cs` — 8 tests
**Effort**: 4-6h

### PHASE 4 — Header Block Encoding/Decoding (Week 3-4)

### TASK-008: QPACK Header Block Encoder

**RFC**: 9204 §4.5

**Implementation**: `src/TurboHttp/Protocol/RFC9204/QpackEncoder.cs`

**Acceptance Criteria:**
- [ ] Static table indexed headers
- [ ] Dynamic table insert + reference
- [ ] Literal headers without indexing
- [ ] NEVERINDEX for sensitive headers (Authorization, Cookie, etc.)
- [ ] Required Insert Count prefix
- [ ] Huffman encoding support
- [ ] Encoder instructions emitted as side-effect

**Tests**: `src/TurboHttp.Tests/RFC9204/08_QpackEncoderTests.cs` — 10 tests
**Effort**: 10-14h

### TASK-009: QPACK Header Block Decoder

**RFC**: 9204 §4.5

**Implementation**: `src/TurboHttp/Protocol/RFC9204/QpackDecoder.cs`

**Acceptance Criteria:**
- [ ] All 5 encoding types decoded (indexed, indexed post-base, literal name ref, literal post-base ref, literal)
- [ ] Required Insert Count validation
- [ ] Blocked stream handling (SETTINGS_QPACK_BLOCKED_STREAMS)
- [ ] Huffman decoding support

**Tests**: `src/TurboHttp.Tests/RFC9204/09_QpackDecoderTests.cs` — 10 tests
**Effort**: 10-14h

### TASK-010: Encode-Decode Roundtrip Tests

**Tests**: `src/TurboHttp.Tests/RFC9204/10_QpackRoundTripTests.cs` — 8 tests
**Effort**: 3-4h

### PHASE 5 — Integration & Synchronization (Week 4-5)

### TASK-011: QPACK Table Synchronization

**RFC**: 9204 §2.1.1, §2.1.2

**Implementation**: `src/TurboHttp/Protocol/RFC9204/QpackTableSync.cs`
**Tests**: `src/TurboHttp.Tests/RFC9204/11_TableSyncTests.cs` — 8 tests
**Effort**: 6-8h

### TASK-012: QPACK → HTTP/3 Integration

**Implementation**:
- Modifies `src/TurboHttp/Protocol/RFC9114/Http3RequestEncoder.cs` — uses QpackEncoder
- Modifies `src/TurboHttp/Protocol/RFC9114/Http3ResponseDecoder.cs` — uses QpackDecoder

**Tests**: 6 integration tests
**Effort**: 4-6h

### TASK-013: QPACK Stream Stages

**Implementation**:
- `src/TurboHttp/Streams/Stages/Encoding/QpackEncoderStreamStage.cs`
  - Port names: `QpackEncoder.In`, `QpackEncoder.Out`
- `src/TurboHttp/Streams/Stages/Decoding/QpackDecoderStreamStage.cs`
  - Port names: `QpackDecoder.In`, `QpackDecoder.Out`

**Tests**: `src/TurboHttp.StreamTests/RFC9204/QpackStreamStageTests.cs` — 6 tests
**Effort**: 4-6h

---

## Non-Goals

- QPACK server-side encoding (client encode + decode only)
- QPACK without HTTP/3 (not standalone)
- Backwards compatibility with HPACK

## Effort Summary

| Phase | Tasks | Hours |
|-------|-------|-------|
| 1: Static Table + Codecs | TASK-001-003 | 7-10h |
| 2: Dynamic Table | TASK-004 | 4-6h |
| 3: Instructions | TASK-005-007 | 11-16h |
| 4: Header Block | TASK-008-010 | 23-32h |
| 5: Integration | TASK-011-013 | 14-20h |
| **Total** | **13 tasks** | **59-84h** |

## Compliance Impact

QPACK is a prerequisite for RFC 9114 Phase 4+ (plan_016). Without QPACK, HTTP/3 cannot send or receive headers. QPACK itself has no separate MUST requirements counted in the RFC 9114 tally — it is an enabler.
