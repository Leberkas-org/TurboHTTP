# TurboHttp — HTTP/2 Test Analysis

## Question

Which unit tests in `RFC9113/` could be replaced by Akka.Streams stage tests,
and which ones **must** remain as unit tests?

---

## Decision Criteria

A unit test cannot be replaced by a stage test when it:

1. **Tests atomic byte boundary conditions** (e.g. 8 bytes → no frame, 9 bytes → frame decoded)
2. **Verifies precise error codes at the bit level** (FRAME_SIZE_ERROR vs. PROTOCOL_ERROR)
3. **Directly exercises production code**: `Http2FrameDecoder.Decode()`, `Http2Encoder.Encode()`, `HpackEncoder`/`HpackDecoder`
4. **Validates encoding invariants at the raw-byte level** (stream ID, big-endian layout, flag bits)

A stage test can verify graph-level behaviour (backpressure, TCP reassembly, multiplexing),
but it **cannot isolate atomic parser boundary conditions**.

---

## Category A — MUST remain as unit tests

### A.1 Frame Parsing: `Http2FrameDecoder` directly

**File:** `src/TurboHttp.Tests/RFC9113/02_FrameParsingTests.cs`
**Class under test:** `Http2FrameDecoder` (production code, no session wrapper)

| Test ID | Display Name | RFC Reference | Why not replaceable |
|---------|-------------|--------------|----------------------|
| FP-001 | Zero bytes returns empty (NeedMoreData) | RFC 9113 §4.1 | Boundary: 0 bytes → empty list, no error |
| FP-002 | 8 bytes (one short of frame header) returns empty | RFC 9113 §4.1 | Boundary: 8 bytes = partial header, no decode |
| FP-003 | Exactly 9 bytes with zero-length payload is decoded | RFC 9113 §4.1 | Boundary: 9 bytes = minimum valid frame |
| FP-004 | Frame with 0 payload length field accepted | RFC 9113 §4.1 | SETTINGS ACK: length field = 0 is legal |
| FP-005 | Frame buffered across two decode calls (fragmented) | RFC 9113 §4.1 | TCP fragmentation at decoder level, not stage level |
| FP-006 | Length field uses all 24 bits (payload > 65535) | RFC 9113 §4.1 | 24-bit length field, payload > 64 KB |
| FP-007 | Default MAX_FRAME_SIZE is 16384 (2^14) | RFC 9113 §4.2 | Exact default boundary: 16384 bytes accepted |
| FP-008 | Frame 1 byte over MAX_FRAME_SIZE causes FRAME_SIZE_ERROR | RFC 9113 §4.2 | Off-by-one: 16385 → FRAME_SIZE_ERROR, connection error |
| FP-010 | SETTINGS_MAX_FRAME_SIZE below 16384 is PROTOCOL_ERROR | RFC 9113 §4.2, §6.5.2 | Lower bound of parameter (16383 = illegal) |
| FP-011 | SETTINGS_MAX_FRAME_SIZE above 16777215 is PROTOCOL_ERROR | RFC 9113 §4.2, §6.5.2 | Upper bound: 2^24 = illegal |
| FP-012 | SETTINGS_MAX_FRAME_SIZE of exactly 16777215 is accepted | RFC 9113 §4.2, §6.5.2 | Exact upper bound accepted |
| FP-013 | Unknown frame type is decoded | RFC 9113 §4.1 | Unknown frame types MUST be ignored |
| FP-014 | Multiple unknown frame types in sequence are decoded | RFC 9113 §4.1 | Sequence of unknown types — no abort |
| FP-015 | Unknown frame type with maximum payload is handled | RFC 9113 §4.1 | Unknown type + maximum payload size |
| FP-016 | SETTINGS on non-zero stream causes PROTOCOL_ERROR | RFC 9113 §6.5 | Stream-ID rule: SETTINGS MUST be on stream 0 |
| FP-017 | PING on non-zero stream causes PROTOCOL_ERROR | RFC 9113 §6.7 | Stream-ID rule: PING MUST be on stream 0 |
| FP-018 | GOAWAY on non-zero stream causes PROTOCOL_ERROR | RFC 9113 §6.8 | Stream-ID rule: GOAWAY MUST be on stream 0 |
| FP-019 | WINDOW_UPDATE on stream 0 (connection-level) is accepted | RFC 9113 §6.9 | Stream 0 = connection-level is legal |
| FP-020 | WINDOW_UPDATE on non-zero stream (stream-level) is accepted | RFC 9113 §6.9 | Stream N = stream-level is legal |
| FP-021 | SETTINGS payload not multiple of 6 is FRAME_SIZE_ERROR | RFC 9113 §6.5 | Payload structure: exactly 6 bytes per entry |
| FP-022 | SETTINGS ACK with non-empty payload is FRAME_SIZE_ERROR | RFC 9113 §6.5 | ACK frame MUST have zero payload length |
| FP-023 | PING with 7-byte payload is FRAME_SIZE_ERROR | RFC 9113 §6.7 | PING payload MUST be exactly 8 bytes |
| FP-024 | PING with 9-byte payload is FRAME_SIZE_ERROR | RFC 9113 §6.7 | PING payload MUST be exactly 8 bytes |
| FP-025 | WINDOW_UPDATE with 3-byte payload is FRAME_SIZE_ERROR | RFC 9113 §6.9 | WINDOW_UPDATE payload MUST be exactly 4 bytes |
| FP-026 | RST_STREAM with 3-byte payload is FRAME_SIZE_ERROR | RFC 9113 §6.4 | RST_STREAM payload MUST be exactly 4 bytes |
| FP-027 | RST_STREAM with 5-byte payload is FRAME_SIZE_ERROR | RFC 9113 §6.4 | RST_STREAM payload MUST be exactly 4 bytes |
| FP-028 | SETTINGS with unknown flag bits set is processed normally | RFC 9113 §4.1 | Unknown flags MUST be ignored |
| FP-029 | PING ACK with unknown flag bits set is processed normally | RFC 9113 §4.1 | Unknown flags combined with ACK bit |
| FP-030 | GoAway frame with debug data parsed correctly | RFC 9113 §6.8 | Debug data parsing after error code |

**Rationale:** All FP-tests call `new Http2FrameDecoder().Decode(frame)` directly.
A stage test materialises the decoder inside an Akka graph — at that level it is impossible
to cleanly isolate "exactly 8 bytes → no frame", because the stage buffers internally.

---

### A.2 Connection Preface

**File:** `src/TurboHttp.Tests/RFC9113/01_ConnectionPrefaceTests.cs`
**Class under test:** `Http2FrameUtils.BuildConnectionPreface()` (production code)

| Test ID | Display Name | RFC Reference | Why not replaceable |
|---------|-------------|--------------|----------------------|
| CP-001 | Client preface starts with exact magic octets | RFC 9113 §3.4 | Exact 24 magic bytes verified at byte level |
| CP-002 | Client preface magic is exactly 24 bytes | RFC 9113 §3.4 | Length invariant of the preface |
| CP-003 | SETTINGS frame follows magic immediately at byte 24 | RFC 9113 §3.4 | Frame type at position 24+3 must equal SETTINGS |
| CP-004 | SETTINGS frame after magic is on stream 0 | RFC 9113 §3.4 | Preface SETTINGS stream ID MUST be 0 |
| CP-005 | Server preface: first frame must be SETTINGS | RFC 9113 §3.4 | First server response MUST be SETTINGS |
| CP-006 | Server preface: SETTINGS on non-zero stream is PROTOCOL_ERROR | RFC 9113 §3.4 | Invalid server preface |
| CP-007 | Server preface: non-SETTINGS first frame is PROTOCOL_ERROR | RFC 9113 §3.4 | Wrong frame type as first response |
| CP-008 | Partial server preface (< 9 bytes) needs more data | RFC 9113 §3.4 | Incomplete preface → no error, wait for more |

**Rationale:** `PrependPrefaceStage` prepends the preface but does not verify that the
bytes are correctly assembled. Only a unit test can inspect the raw byte structure directly.

---

### A.3 SETTINGS Parameter Validation

**File:** `src/TurboHttp.Tests/RFC9113/04_SettingsTests.cs`
**Class under test:** `Http2FrameDecoder` (via `Http2ProtocolSession.Process()`)

| Test ID | Display Name | RFC Reference | Why not replaceable |
|---------|-------------|--------------|----------------------|
| SS-001 | BuildConnectionPreface produces magic + SETTINGS frame | RFC 9113 §3.5 | Preface structure at byte level |
| SS-002 | Connection preface SETTINGS is on stream 0 | RFC 9113 §3.5 | Stream ID = 0 invariant |
| SS-003 | Connection preface SETTINGS contains HeaderTableSize=4096 | RFC 9113 §3.5 | Concrete SETTINGS parameter values |
| SS-004 | Connection preface SETTINGS contains EnablePush=0 | RFC 9113 §3.5 | EnablePush=0 for clients |
| SS-005 | Connection preface SETTINGS contains MaxFrameSize=16384 | RFC 9113 §3.5 | MaxFrameSize default value |
| SS-006 | MaxFrameSize=16383 is PROTOCOL_ERROR | RFC 9113 §6.5.2 | Lower bound: 16383 < 16384 = illegal |
| SS-007 | MaxFrameSize=16777216 is PROTOCOL_ERROR | RFC 9113 §6.5.2 | Upper bound: > 2^24-1 = illegal |
| SS-009 | InitialWindowSize above 2^31-1 is FLOW_CONTROL_ERROR | RFC 9113 §6.5.2 | Integer overflow boundary, FLOW_CONTROL_ERROR |
| SS-010 | InitialWindowSize of exactly 2^31-1 is accepted | RFC 9113 §6.5.2 | Exact upper bound (2147483647 = legal) |
| SS-011 | EnablePush=0 is accepted | RFC 9113 §6.5.2 | Valid value for client-side deactivation |
| SS-012 | EnablePush=1 is accepted | RFC 9113 §6.5.2 | Valid value (server perspective) |
| SS-013 | EnablePush=2 is PROTOCOL_ERROR | RFC 9113 §6.5.2 | Value > 1 = illegal |
| SS-019 | Non-ACK SETTINGS produces one SETTINGS ACK to send | RFC 9113 §6.5 | ACK obligation: every non-ACK SETTINGS must be acknowledged |
| SS-020 | SETTINGS ACK frame produces no new ACK in return | RFC 9113 §6.5 | ACK-to-ACK is forbidden |
| SS-021 | Three SETTINGS frames produce three ACKs to send | RFC 9113 §6.5 | Strict 1:1 ACK correspondence |
| SS-022 | Empty SETTINGS frame (zero parameters) produces ACK | RFC 9113 §6.5 | Empty SETTINGS is legal and MUST be acknowledged |
| SS-023 | Encoded SETTINGS ACK is a valid 9-byte frame | RFC 9113 §6.5 | Wire format of the ACK verified at byte level |
| SS-027 | Unknown SETTINGS parameter ID is silently ignored | RFC 9113 §6.5 | Unknown parameter IDs MUST be ignored |
| SS-028 | Multiple parameters in one SETTINGS frame are all applied | RFC 9113 §6.5 | Multiple parameters in a single frame |
| SS-029 | InitialWindowSize increase overflows open stream send window | RFC 9113 §6.9.2 | Overflow detection: SETTINGS update against an open stream |

**Rationale:** `Http2ConnectionStage` applies SETTINGS values at runtime but does not
test the validation boundaries themselves. That logic lives in `Http2FrameDecoder`,
which the unit tests exercise directly via the session.

---

### A.4 Error Code Mapping

**File:** `src/TurboHttp.Tests/RFC9113/07_ErrorHandlingTests.cs`
**File:** `src/TurboHttp.Tests/RFC9113/14_DecoderErrorCodeTests.cs`

| Test ID | RFC Reference | What is verified | Why not replaceable |
|---------|-------------|--------------|----------------------|
| EM-001..EM-010 | RFC 9113 §5.4.1 | Connection error → `IsConnectionError = true` | Error scope injected at byte level and verified precisely |
| EM-011..EM-020 | RFC 9113 §5.4.2 | Stream error → `IsConnectionError = false`, correct StreamId | Error affects only the stream, not the connection |
| EM-021..EM-025 | RFC 9113 §6.1, §6.4 | PROTOCOL_ERROR vs. FLOW_CONTROL_ERROR vs. FRAME_SIZE_ERROR | Error code precision: only a unit test can inject a targeted malformed frame |
| DC-001..DC-010 | RFC 9113 §6.6 | Error code serialisation (uint32 big-endian) | Wire format of the error code in RST_STREAM and GOAWAY |

**Rationale:** A stage test can verify that the stage reacts to an error, but not
which exact `Http2ErrorCode` is thrown with which `IsConnectionError` value.
That level of precision requires calling the decoder directly.

---

### A.5 CONTINUATION Frame Sequencing

**File:** `src/TurboHttp.Tests/RFC9113/09_ContinuationFrameTests.cs`

| Test ID | Display Name | RFC Reference | Why not replaceable |
|---------|-------------|--------------|----------------------|
| CF-001 | HEADERS with END_HEADERS completes immediately | RFC 9113 §6.10 | No CONTINUATION required when END_HEADERS is set |
| CF-002 | HEADERS without END_HEADERS requires CONTINUATION | RFC 9113 §6.10 | Header block fragmentation |
| CF-003 | Multiple CONTINUATION frames reassemble complete header block | RFC 9113 §6.10 | N-part fragmentation |
| CF-004 | CONTINUATION on wrong stream causes PROTOCOL_ERROR | RFC 9113 §6.10 | Stream ID switch during fragmentation = fatal |
| CF-005 | Non-CONTINUATION after HEADERS-without-END_HEADERS is PROTOCOL_ERROR | RFC 9113 §6.10 | Interleaving other frame types = fatal |
| CF-006 | CONTINUATION without preceding HEADERS is PROTOCOL_ERROR | RFC 9113 §6.10 | Orphaned CONTINUATION = fatal |
| CF-007..CF-016 | Flood protection, partial blocks, END_STREAM semantics | RFC 9113 §6.10 | Boundary conditions of the CONTINUATION state machine |

**Rationale:** The CONTINUATION state machine (`_continuationStreamId`, `_continuationBuffer`)
is protocol-critical. The sequencing rules (no other frame type permitted during
fragmentation) can only be reliably tested by a unit test that injects specific byte
sequences, not by a stage test that controls only the graph-level stream.

---

### A.6 HTTP/2 Encoder

**Files:**
- `src/TurboHttp.Tests/RFC9113/18_EncoderBaselineTests.cs`
- `src/TurboHttp.Tests/RFC9113/19_EncoderRfcTaggedTests.cs`
- `src/TurboHttp.Tests/RFC9113/20_EncoderStreamSettingsTests.cs`
- `src/TurboHttp.Tests/RFC9113/Http2EncoderPseudoHeaderValidationTests.cs`
- `src/TurboHttp.Tests/RFC9113/Http2EncoderSensitiveHeaderTests.cs`
- `src/TurboHttp.Tests/RFC9113/Http2FrameTests.cs`

**Classes under test:** `Http2RequestEncoder`, `Http2FrameUtils`, `Http2Frame` subclasses

| Test Group | RFC Reference | What is verified | Why not replaceable |
|-------------|-------------|--------------|----------------------|
| Encoder Baseline | RFC 9113 §4.1, §4.2 | `data[3] == FrameType.Headers`, stream ID = 1, 3, 5 | Raw-byte inspection of encoder output |
| Pseudo-Header Validation | RFC 9113 §8.3 | `:method`, `:path`, `:scheme`, `:authority` present and ordered | Order and mandatory fields in the HPACK block |
| Sensitive-Header NEVERINDEX | RFC 7541 §7.1 | `Authorization`/`Cookie` → HPACK never-index bit set | Security invariant at the bit level |
| Frame Serialisation | RFC 9113 §4.1 | 9-byte header, 24-bit length, big-endian stream ID | Wire format directly verified |
| SETTINGS–Encoder Interaction | RFC 9113 §6.5 | Encoder respects MaxFrameSize and MaxConcurrentStreams | Encoder behaviour after a SETTINGS update |

**Rationale:** `Http2FrameEncoderStage` serialises frames but does not verify that
the encoder produces correct pseudo-headers, NEVERINDEX bits, or stream ID increments.
Those are encoder invariants, not stage invariants.

---

### A.7 HPACK (RFC 7541)

**Folder:** `src/TurboHttp.Tests/RFC7541/`
**Classes under test:** `HpackEncoder`, `HpackDecoder`, `HpackDynamicTable`, `HuffmanCodec`

These tests are completely independent of Akka.Streams and can **never** be replaced
by stage tests:

| RFC Reference | Invariant tested |
|-------------|---------------------|
| RFC 7541 §2.3 | Static table: 61 entries, exact indices |
| RFC 7541 §4.1 | Dynamic table: FIFO eviction, 32-byte overhead per entry |
| RFC 7541 §4.2 | Table size update, shrinking on capacity change |
| RFC 7541 §5.1 | Huffman encoding: bit-accurate compression |
| RFC 7541 §6.1 | Indexed representation: 1 byte for known headers |
| RFC 7541 §6.2 | Literal without indexing vs. never index |
| RFC 7541 §7.1 | Sensitive headers: NEVERINDEX bit = 1 |

---

## Category B — PROBLEMATIC (test session-internal logic only)

These tests run through `Http2ProtocolSession` but exercise logic that is
implemented **only in the test helper**, not in any production stage.

### B.1 Flow Control Arithmetic

**File:** `src/TurboHttp.Tests/RFC9113/05_FlowControlTests.cs`
**File:** `src/TurboHttp.Tests/RFC9113/13_DecoderStreamFlowControlTests.cs`

The window-tracking fields (`_connectionReceiveWindow`, `_streamSendWindows`) exist
**only** in `Http2ProtocolSession`. In production, `Http2ConnectionStage` is responsible
for flow control enforcement.

**Risk:** These tests verify the test helper, not the production code.
`Http2ConnectionStage` could implement flow control incorrectly and these tests
would not catch it.

**Recommendation:** Add stage tests for `Http2ConnectionStage`:

```
TurboHttp.StreamTests/Http20/Http2FlowControlStageTests.cs
```

### B.2 Security / DoS Protection

**File:** `src/TurboHttp.Tests/RFC9113/Http2SecurityTests.cs`
**File:** `src/TurboHttp.Tests/RFC9113/Http2ResourceExhaustionTests.cs`

| Protection mechanism | In session | In production stage? |
|--------------------|------------|---------------------|
| CONTINUATION flood (max 1000) | `_continuationCount` | Unknown |
| Rapid RST_STREAM (CVE-2023-44487, max 100) | `_rstStreamCount` | Unknown |
| Empty DATA frame exhaustion (max 10,000) | `_emptyDataFrameCount` | Unknown |
| SETTINGS flood (max 100) | `_settingsCount` | Unknown |

**Risk:** If these protection mechanisms exist only in `Http2ProtocolSession`
and not in `Http2ConnectionStage`, the production implementation **does not
protect against these CVEs**.

**Recommendation:** First confirm whether `Http2ConnectionStage` also enforces
these limits. If not: implement the protection there and add stage tests.

### B.3 Stream Lifecycle State Machine

**File:** `src/TurboHttp.Tests/RFC9113/03_StreamStateMachineTests.cs`

The `_streamStates` dictionary lives in `Http2ProtocolSession`. Basic transitions
(Idle → Open → Closed) are also enforced by `Http2FrameDecoder`. However, the
extended rules (e.g. DATA on a closed stream) reside only in the session.

**Recommendation:** Keep basic transition tests as unit tests.
Add stage tests for multi-stream behaviour under real materialisation.

### B.4 MaxConcurrentStreams Enforcement

**File:** `src/TurboHttp.Tests/RFC9113/Http2MaxConcurrentStreamsTests.cs`

The rejection of new streams when `ActiveStreamCount >= MaxConcurrentStreams` is
implemented in `Http2ProtocolSession._activeStreamCount`, not in any stage.

**Recommendation:** Add a stage test for `Http2ConnectionStage` that verifies
that when `MAX_CONCURRENT_STREAMS = N`, the (N+1)th request is rejected with
`REFUSED_STREAM`.

---

## Summary

| Category | Files | Tests | Recommendation |
|-----------|---------|-------|-----------|
| **A: Always unit test** | 02, 01, 04, 07, 09, 14, 18, 19, 20, EncoderValidation, RFC7541/ | ~200 | Keep as-is |
| **B: Session logic only** | 03, 05, 13, SecurityTests, ResourceExhaustion, MaxConcurrentStreams | ~120 | Add stage tests alongside |
| **Neutral: Round-trip** | 15, 16, 17 | ~54 | Keep, add stage tests additionally |
| **Stage tests exist** | StreamTests/Http20/ | 23 | Extend |

### Recommended new stage tests

```
TurboHttp.StreamTests/Http20/
  Http2FlowControlStageTests.cs        RFC 9113 §6.9   — WINDOW_UPDATE, backpressure
  Http2SecurityStageTests.cs           RFC 7540 §6     — flood detection in stage
  Http2ConcurrentStreamsStageTests.cs  RFC 9113 §6.5.2 — MAX_CONCURRENT_STREAMS
  Http2GoAwayStageTests.cs             RFC 9113 §6.8   — graceful shutdown in graph
```
