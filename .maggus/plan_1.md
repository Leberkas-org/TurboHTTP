# STREAMS_TEST_PLAN.md — RFC-Coverage Extension for Akka.Streams Stages

> **Goal:** Every RFC requirement previously covered only by unit tests at the protocol level
> will additionally be verified through stream tests — i.e. the same RFC requirement flows through the
> real Akka.Streams pipeline (Encoder Stage → Fake TCP → Decoder Stage) and is validated end-to-end.

**Created:** 2026-03-10
**Branch:** `poc2`
**Project:** `TurboHttp.StreamTests`

---

## Overview

| Phase | Area | Tasks | New Tests (approx.) |
|-------|------|-------|----------------------|
| 1 | GAP-Closure: Missing Stage Tests | 4 | ~30 |
| 2 | HTTP/1.0 Stages — RFC 1945 | 5 | ~25 |
| 3 | HTTP/1.1 Stages — RFC 9112 | 6 | ~30 |
| 4 | HTTP/2 Stages — RFC 9113 | 7 | ~35 |
| 5 | HTTP/2 Connection-Level — RFC 9113 §6 | 5 | ~25 |
| 6 | Engine-Integration — RFC Round-Trip | 4 | ~20 |
| 7 | Cross-Cutting: Error & Edge Cases | 4 | ~15 |
| **Σ** | | **35 Tasks** | **~180 Tests** |

---

## Phase 1 — GAP-Closure: Missing Dedicated Stage Tests

> Closes GAP-005, GAP-006, GAP-007, GAP-008 from the RFC_TEST_MATRIX.

### TASK-GAP-01: StreamIdAllocator Stage Tests

**File:** `Http20/StreamIdAllocatorStageTests.cs`
**RFC:** 9113 §5.1.1 — Stream Identifiers

- [x] `SID-001`: First stream ID is 1 (client initiates with odd number)
- [x] `SID-002`: Consecutive IDs: 1, 3, 5, 7 (strictly ascending +2)
- [x] `SID-003`: 10 requests → 10 distinct, monotonically increasing stream IDs
- [x] `SID-004`: Stream ID is always odd (no even value)
- [x] `SID-005`: Request object is passed through unchanged (reference equality)
- [x] `SID-006`: Stage terminates cleanly on UpstreamFinish (CompleteStage)

**Test pattern:**
```csharp
// Source.From(requests) → StreamIdAllocator → Sink.Seq
// Assert: outputTuple.Item2 == 1, 3, 5, ...
```

---

### TASK-GAP-02: CorrelationHttp1XStage Tests

**File:** `Http11/CorrelationHttp1XStageTests.cs`
**RFC:** 9112 §9 — Request/Response Ordering (HTTP/1.x pipeline)

- [x] `COR1X-001`: Single request/response pairing → `response.RequestMessage == request`
- [x] `COR1X-002`: 5 sequential requests → FIFO order maintained
- [x] `COR1X-003`: Request reference is the exact same object (not copied)
- [x] `COR1X-004`: Response arrives before request → correctly buffered and correlated
- [x] `COR1X-005`: Request arrives before response → correctly buffered and correlated
- [x] `COR1X-006`: Stage terminates on empty queue after UpstreamFinish on both inlets
- [x] `COR1X-007`: Stage remains open while pending requests still exist

**Test pattern:**
```csharp
// GraphDSL: Source<Request> → CorrelationHttp1XStage ← Source<Response>
// Assert: output.RequestMessage == originalRequest
```

---

### TASK-GAP-03: CorrelationHttp20Stage Tests

**File:** `Http20/CorrelationHttp20StageTests.cs`
**RFC:** 9113 §5.1 — Stream Multiplexing

- [x] `COR20-001`: Single (Request, streamId=1) + (Response, streamId=1) → correctly correlated
- [x] `COR20-002`: 3 requests (IDs 1,3,5) + 3 responses (IDs 5,1,3) → out-of-order correlation
- [x] `COR20-003`: Response stream ID with no matching request → stays in queue
- [x] `COR20-004`: Reference equality: `response.RequestMessage` is exactly the sent object
- [x] `COR20-005`: 10 interleaved requests/responses → all correctly matched
- [x] `COR20-006`: Stage terminates on empty dictionaries after UpstreamFinish
- [x] `COR20-007`: Interleaved push: Request(1), Response(3), Request(3) → correlation immediately on match

**Test pattern:**
```csharp
// GraphDSL: Source<(Request,int)> → CorrelationHttp20Stage ← Source<(Response,int)>
// Assert: each response.RequestMessage == requests[streamId]
```

---

### TASK-GAP-04: ExtractOptionsStage Tests

**File:** `Streams/ExtractOptionsStageTests.cs`
**RFC:** N/A — internal architecture (connection initialization)

- [x] `EXT-001`: First request → Out0 emits `InitialInput(TcpOptions)`, Out1 emits `RequestMessage`
- [x] `EXT-002`: Second request → only Out1 emits (no repeated options event)
- [x] `EXT-003`: 5 requests → exactly 1× Out0, 5× Out1
- [x] `EXT-004`: Options extracted only on very first request (`_initialSent` flag)
- [x] `EXT-005`: UpstreamFinish → stage terminates cleanly
- [x] `EXT-006`: Pending request after InitialInput correctly delivered via Out1

**Test pattern:**
```csharp
// Source.From(requests) → ExtractOptionsStage → (Sink.Seq<Options>, Sink.Seq<Request>)
// Assert: optionsList.Count == 1, requestList.Count == N
```

---

## Phase 2 — HTTP/1.0 Stages: RFC 1945 Through the Pipeline

> Existing stream tests: 22 (4 files). Supplementing RFC-specific gaps here.

### TASK-10-01: Http10EncoderStage — Request-Line Compliance

**File:** `Http10/Http10EncoderStageRfcTests.cs`
**RFC:** 1945 §5.1 — Request-Line

- [x] `10E-RFC-001`: Request-line format: `GET /path HTTP/1.0\r\n`
- [x] `10E-RFC-002`: POST with body → `Content-Length` header is set
- [x] `10E-RFC-003`: No `Host` header in HTTP/1.0 (not mandatory)
- [x] `10E-RFC-004`: `Connection: close` is not sent (no keep-alive in 1.0)
- [x] `10E-RFC-005`: Query string correctly in request target: `/search?q=foo`

---

### TASK-10-02: Http10DecoderStage — Status-Line & Headers

**File:** `Http10/Http10DecoderStageRfcTests.cs`
**RFC:** 1945 §6.1 — Status-Line, §4.2 — Headers

- [x] `10D-RFC-001`: Status-line `HTTP/1.0 200 OK` → StatusCode=200, Version=1.0
- [x] `10D-RFC-002`: Status-line `HTTP/1.0 404 Not Found` → StatusCode=404
- [x] `10D-RFC-003`: Response headers correctly parsed (Content-Type, Content-Length)
- [x] `10D-RFC-004`: Body with Content-Length correctly read
- [x] `10D-RFC-005`: Connection-Close: stream ends after body

---

### TASK-10-03: Http10 Stage Round-Trip — Methods

**File:** `Http10/Http10StageRoundTripMethodTests.cs`
**RFC:** 1945 §8 — Method Definitions

- [x] `10RT-M-001`: GET → 200 OK — request-line + response correct
- [x] `10RT-M-002`: POST with body → body in wire format + 200 response
- [x] `10RT-M-003`: HEAD → response without body, but with Content-Length header
- [x] `10RT-M-004`: DELETE → 204 No Content (empty body)
- [x] `10RT-M-005`: PUT → body correctly transmitted and response parsed

---

### TASK-10-04: Http10 Stage Round-Trip — Headers & Body

**File:** `Http10/Http10StageRoundTripBodyTests.cs`
**RFC:** 1945 §7.2 — Entity Body, §10.4 — Content-Length

- [x] `10RT-B-001`: Empty body → `Content-Length: 0`
- [x] `10RT-B-002`: Large body (64 KB) → correctly serialized and deserialized
- [x] `10RT-B-003`: Binary body (bytes 0x00–0xFF) → byte-for-byte identical
- [x] `10RT-B-004`: Custom headers in request → present in wire format
- [x] `10RT-B-005`: Response with multiple headers → all correctly parsed

---

### TASK-10-05: Http10 Stage — TCP Fragmentation

**File:** `Http10/Http10StageFragmentationTests.cs`
**RFC:** 1945 §4.1 — Message Framing (implicit: TCP segments)

- [x] `10F-001`: Response split into 3 TCP fragments → correctly reassembled
- [x] `10F-002`: Headers split across 2 fragments → correctly parsed
- [x] `10F-003`: Body fragment arrives in separate chunk → content complete
- [x] `10F-004`: 1-byte fragments → decoder handles gracefully
- [x] `10F-005`: Fragment boundary in the middle of `\r\n\r\n` → header end correctly detected

---

## Phase 3 — HTTP/1.1 Stages: RFC 9112 Through the Pipeline

> Existing stream tests: 27 (5 files). Adding RFC-critical scenarios.

### TASK-11-01: Http11EncoderStage — Host Header & Request-Line

**File:** `Http11/Http11EncoderStageRfcTests.cs`
**RFC:** 9112 §3.2 — Request-Line, 9112 §7.2 — Host Header

- [x] `11E-RFC-001`: Request-line: `GET /path HTTP/1.1\r\n`
- [x] `11E-RFC-002`: Host header MUST be present (RFC 9112 §7.2)
- [x] `11E-RFC-003`: Host header value = `authority` of the URI
- [x] `11E-RFC-004`: POST → `Content-Length` or `Transfer-Encoding: chunked`
- [x] `11E-RFC-005`: Hop-by-hop headers (TE, Keep-Alive, Proxy-Connection) are stripped

---

### TASK-11-02: Http11DecoderStage — Chunked Transfer

**File:** `Http11/Http11DecoderStageChunkedRfcTests.cs`
**RFC:** 9112 §7.1 — Chunked Transfer Coding

- [x] `11D-CH-001`: Single chunk `5\r\nhello\r\n0\r\n\r\n` → body = "hello"
- [x] `11D-CH-002`: Multiple chunks → bodies correctly concatenated
- [x] `11D-CH-003`: Zero-length terminator `0\r\n\r\n` → stream ends
- [x] `11D-CH-004`: Chunk extension (`;ext=val`) is ignored
- [x] `11D-CH-005`: Trailers after last chunk → correctly parsed or ignored

---

### TASK-11-03: Http11 Stage Round-Trip — Pipelining

**File:** `Http11/Http11StageRoundTripPipelineTests.cs`
**RFC:** 9112 §9.3 — Pipelining

- [x] `11RT-P-001`: 3 sequential GET requests → 3 responses in FIFO order
- [x] `11RT-P-002`: Each response has correct `RequestMessage` reference
- [x] `11RT-P-003`: Mixed methods (GET, POST, DELETE) → correct assignment
- [x] `11RT-P-004`: 10 requests → all 10 responses received
- [x] `11RT-P-005`: Response order matches request order (FIFO guarantee)

---

### TASK-11-04: Http11 Stage Round-Trip — Connection Management

**File:** `Http11/Http11StageConnectionMgmtTests.cs`
**RFC:** 9112 §9.6 — Connection: close, §9.8 — Keep-Alive

- [x] `11RT-C-001`: Response with `Connection: close` → version correctly set
- [x] `11RT-C-002`: Response without `Connection` header → keep-alive (default for HTTP/1.1)
- [x] `11RT-C-003`: `Transfer-Encoding: chunked` + `Connection: keep-alive` → stream stays open
- [x] `11RT-C-004`: Content-Length body → correctly read, connection not prematurely closed
- [x] `11RT-C-005`: Empty body with Content-Length: 0 → response emitted immediately

---

### TASK-11-05: Http11 Stage — Fragmentation & Reassembly

**File:** `Http11/Http11StageFragmentationTests.cs`
**RFC:** 9112 §6 — Message Body (TCP boundary handling)

- [x] `11F-001`: Chunked response over 4 TCP segments → correctly reassembled
- [x] `11F-002`: Header/body boundary on TCP segment boundary → correctly separated
- [x] `11F-003`: Chunk-size line split across 2 segments → correctly parsed
- [x] `11F-004`: Content-Length body in 3 fragments → fully read
- [x] `11F-005`: Very small fragments (1–2 bytes) → decoder handles gracefully

---

### TASK-11-06: Http11 Stage — Status Codes

**File:** `Http11/Http11StageStatusCodeTests.cs`
**RFC:** 9110 §15 — Status Codes

- [x] `11SC-001`: 200 OK → StatusCode=200
- [x] `11SC-002`: 301 Moved Permanently → StatusCode=301, Location header present
- [x] `11SC-003`: 404 Not Found → StatusCode=404
- [x] `11SC-004`: 500 Internal Server Error → StatusCode=500
- [x] `11SC-005`: 204 No Content → StatusCode=204, no body

---

## Phase 4 — HTTP/2 Stages: RFC 9113 Through the Pipeline

> Existing stream tests: 32 (5 files). Extension with frame-level RFC compliance.

### TASK-20-01: Http20EncoderStage — Frame Serialization

**File:** `Http20/Http20EncoderStageRfcTests.cs`
**RFC:** 9113 §4.1 — Frame Format (9-byte header)

- [x] `20E-RFC-001`: HEADERS frame → 9-byte header + HPACK payload
- [x] `20E-RFC-002`: DATA frame → 9-byte header + body payload
- [x] `20E-RFC-003`: Frame-length field (3 bytes) → correct payload length
- [x] `20E-RFC-004`: Frame type (1 byte): 0x0=DATA, 0x1=HEADERS
- [x] `20E-RFC-005`: Stream ID in big-endian (4 bytes), highest bit = 0

---

### TASK-20-02: Http20DecoderStage — Frame Parsing

**File:** `Http20/Http20DecoderStageRfcTests.cs`
**RFC:** 9113 §4.1 — Frame Format

- [x] `20D-RFC-001`: Complete frame → correctly decoded
- [x] `20D-RFC-002`: Frame split across 2 TCP segments → reassembled
- [x] `20D-RFC-003`: 2 frames in one TCP segment → both decoded
- [x] `20D-RFC-004`: SETTINGS frame (Type 0x4) → flags and parameters correct
- [x] `20D-RFC-005`: DATA frame → stream ID and payload correct

---

### TASK-20-03: Http20StreamStage — Response Reassembly

**File:** `Http20/Http20StreamStageTests.cs`
**RFC:** 9113 §8.1 — HTTP Request/Response Exchange

- [x] `20S-001`: HEADERS with END_STREAM → response without body
- [x] `20S-002`: HEADERS + DATA with END_STREAM → response with body
- [x] `20S-003`: HEADERS + CONTINUATION + DATA → header block reassembled
- [x] `20S-004`: Multiple streams (ID 1, 3) → separate responses
- [x] `20S-005`: `:status` pseudo-header → correct HttpStatusCode
- [x] `20S-006`: Content-Encoding header → decompression applied (gzip)
- [x] `20S-007`: Regular headers (non-pseudo) → present in Response.Headers

---

### TASK-20-04: Http20ConnectionStage — SETTINGS Handling

**File:** `Http20/Http20ConnectionStageSettingsTests.cs`
**RFC:** 9113 §6.5 — SETTINGS

- [x] `20CS-001`: Server SETTINGS received → SETTINGS ACK sent
- [x] `20CS-002`: SETTINGS with ACK flag → no ACK sent back
- [x] `20CS-003`: INITIAL_WINDOW_SIZE parameter → `_initialStreamWindow` updated
- [x] `20CS-004`: SETTINGS frame forwarded downstream
- [x] `20CS-005`: Multiple consecutive SETTINGS → one ACK each

---

### TASK-20-05: Http20ConnectionStage — PING Handling

**File:** `Http20/Http20ConnectionStagePingTests.cs`
**RFC:** 9113 §6.7 — PING

- [x] `20CP-001`: PING without ACK → PING with ACK sent back
- [x] `20CP-002`: PING payload (8 bytes) → identical in ACK
- [x] `20CP-003`: PING with ACK flag → no new PING sent
- [x] `20CP-004`: PING on stream 0 → response on stream 0

---

### TASK-20-06: Http20ConnectionStage — GOAWAY Handling

**File:** `Http20/Http20ConnectionStageGoAwayTests.cs`
**RFC:** 9113 §6.8 — GOAWAY

- [x] `20CG-001`: GOAWAY received → `_goAwayReceived` flag set
- [x] `20CG-002`: GOAWAY frame forwarded downstream
- [x] `20CG-003`: After GOAWAY → new requests rejected (on stage extension)

---

### TASK-20-07: Http20ConnectionStage — Flow Control (WINDOW_UPDATE)

**File:** `Http20/Http20ConnectionStageFlowControlTests.cs`
**RFC:** 9113 §6.9 — WINDOW_UPDATE

- [x] `20CW-001`: Inbound DATA → connection window decremented
- [x] `20CW-002`: Inbound DATA → stream window decremented
- [x] `20CW-003`: Inbound DATA → WINDOW_UPDATE(stream=0) sent
- [x] `20CW-004`: Inbound DATA → WINDOW_UPDATE(stream=N) sent
- [x] `20CW-005`: Connection window < 0 → stage fails with exception
- [x] `20CW-006`: Stream window < 0 → stage fails with exception
- [x] `20CW-007`: Outbound DATA → connection window decremented
- [x] `20CW-008`: WINDOW_UPDATE(stream=0) received → connection window incremented
- [x] `20CW-009`: WINDOW_UPDATE(stream=N) received → stream window incremented

---

## Phase 5 — HTTP/2 Connection-Level: RFC 9113 §3

### TASK-H2C-01: Connection Preface End-to-End

**File:** `Http20/Http20ConnectionPrefaceRfcTests.cs`
**RFC:** 9113 §3.4 — HTTP/2 Connection Preface

- [x] `H2P-001`: First 24 bytes = `PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n`
- [x] `H2P-002`: SETTINGS frame directly after magic (byte 24+)
- [x] `H2P-003`: Preface is sent exactly once (not repeated on second request)
- [x] `H2P-004`: SETTINGS frame on stream 0

---

### TASK-H2C-02: Stream ID Management End-to-End

**File:** `Http20/Http20StreamIdRfcTests.cs`
**RFC:** 9113 §5.1.1 — Stream Identifiers

- [x] `H2S-001`: First request → stream ID 1
- [x] `H2S-002`: Second request → stream ID 3
- [x] `H2S-003`: 5 requests → IDs 1, 3, 5, 7, 9
- [x] `H2S-004`: All HEADERS frames have correct stream ID
- [x] `H2S-005`: DATA frames have same stream ID as associated HEADERS

---

### TASK-H2C-03: Pseudo-Header End-to-End

**File:** `Http20/Http20PseudoHeaderRfcTests.cs`
**RFC:** 9113 §8.3.1 — Request Pseudo-Header Fields

- [x] `H2PH-001`: `:method` = HTTP method (GET, POST, etc.)
- [x] `H2PH-002`: `:path` = absolute path + query
- [x] `H2PH-003`: `:scheme` = URI scheme (http/https)
- [x] `H2PH-004`: `:authority` = host:port
- [x] `H2PH-005`: Pseudo-headers appear BEFORE regular headers

---

### TASK-H2C-04: Forbidden Header Stripping End-to-End

**File:** `Http20/Http20ForbiddenHeaderRfcTests.cs`
**RFC:** 9113 §8.2.2 — Connection-Specific Header Fields

- [x] `H2FH-001`: `connection` header → not present in wire format
- [x] `H2FH-002`: `transfer-encoding` header → not present in wire format
- [x] `H2FH-003`: `upgrade` header → not present in wire format
- [x] `H2FH-004`: `keep-alive` header → not present in wire format
- [x] `H2FH-005`: Custom header (`x-custom`) → present in wire format

---

### TASK-H2C-05: HPACK in Stream Context

**File:** `Http20/Http20HpackStreamTests.cs`
**RFC:** 7541 §2 — HPACK in HTTP/2 Context

- [x] `H2HP-001`: Static table: `:method GET` transmitted as indexed
- [x] `H2HP-002`: Dynamic table: repeated custom headers → smaller block on 2nd request
- [x] `H2HP-003`: 3 requests with same host → progressive compression visible
- [x] `H2HP-004`: Huffman encoding enabled → header block smaller than without

---

## Phase 6 — Engine Integration: Complete RFC Round-Trips

### TASK-ENG-01: Http10Engine RFC Round-Trip

**File:** `Http10/Http10EngineRfcRoundTripTests.cs`
**RFC:** 1945 (combined)

- [x] `10ENG-001`: GET → 200 with body — version 1.0 in response
- [x] `10ENG-002`: POST with body → request body in wire, 200 response with body
- [x] `10ENG-003`: 404 response → StatusCode correct, ReasonPhrase present
- [x] `10ENG-004`: Custom request header → in wire and available in response
- [x] `10ENG-005`: Response correlation: `response.RequestMessage` == sent request

---

### TASK-ENG-02: Http11Engine RFC Round-Trip

**File:** `Http11/Http11EngineRfcRoundTripTests.cs`
**RFC:** 9112 (combined)

- [x] `11ENG-001`: GET → 200 with Content-Length body — version 1.1
- [x] `11ENG-002`: POST → chunked request + chunked response
- [x] `11ENG-003`: 5 sequential requests → FIFO correlation
- [x] `11ENG-004`: Host header in wire correct for each URI
- [x] `11ENG-005`: Hop-by-hop headers stripped in wire

---

### TASK-ENG-03: Http20Engine RFC Round-Trip

**File:** `Http20/Http20EngineRfcRoundTripTests.cs`
**RFC:** 9113 (combined)

- [ ] `20ENG-001`: GET → 200 — preface + SETTINGS + HEADERS round-trip
- [ ] `20ENG-002`: POST with body → HEADERS + DATA frames
- [ ] `20ENG-003`: gzip-compressed response → body correctly decompressed
- [ ] `20ENG-004`: Server SETTINGS ACK → correct in outbound frames
- [ ] `20ENG-005`: 3 requests → 3 responses with correct stream IDs

---

### TASK-ENG-04: Engine Version Routing

**File:** `Streams/EngineVersionRoutingTests.cs`
**RFC:** N/A — architecture (partition-by-version)

- [ ] `EROUTE-001`: HTTP/1.0 request → routed through Http10Engine
- [ ] `EROUTE-002`: HTTP/1.1 request → routed through Http11Engine
- [ ] `EROUTE-003`: HTTP/2.0 request → routed through Http20Engine
- [ ] `EROUTE-004`: Mixed versions → each response has correct version
- [ ] `EROUTE-005`: Unknown version → partition error (expected behavior)

---

## Phase 7 — Cross-Cutting: Error Handling & Edge Cases

### TASK-ERR-01: Encoder Stage Buffer Management

**File:** `Stages/EncoderStageBufferTests.cs`
**RFC:** N/A — performance & correctness

- [ ] `BUF-001`: Small request (< 4 KB) → adaptive buffer starts small
- [ ] `BUF-002`: Large request (> 64 KB) → buffer grows (no overflow)
- [ ] `BUF-003`: Sequential requests → buffer reuse (no memory leak)
- [ ] `BUF-004`: Binary body → bytes passed through correctly

---

### TASK-ERR-02: Decoder Stage Partial Frame Handling

**File:** `Stages/DecoderStagePartialTests.cs`
**RFC:** 9113 §4.1 — Frame Format (partial receive)

- [ ] `PART-001`: HTTP/1.x — incomplete header → decoder waits for next chunk
- [ ] `PART-002`: HTTP/1.x — body fragment → accumulates until Content-Length reached
- [ ] `PART-003`: HTTP/2 — 5 of 9 header bytes → frame waits for remainder
- [ ] `PART-004`: HTTP/2 — frame payload spread across 3 chunks → correctly reassembled

---

### TASK-ERR-03: Stage Lifecycle & Termination

**File:** `Stages/StageLifecycleTests.cs`
**RFC:** N/A — Akka.Streams compliance

- [ ] `LIFE-001`: UpstreamFinish → stage terminates without exception
- [ ] `LIFE-002`: DownstreamCancel → stage shuts down cleanly
- [ ] `LIFE-003`: Exception in encoder → stage fails with meaningful error message
- [ ] `LIFE-004`: Exception in decoder → stage fails with HttpDecoderException

---

### TASK-ERR-04: Http20StreamStage — Memory Management

**File:** `Http20/Http20StreamStageMemoryTests.cs`
**RFC:** N/A — resource management

- [ ] `MEM-001`: StreamState.Dispose() called after response emission
- [ ] `MEM-002`: BodyBuffer grows correctly for large body (Rent → Copy → Dispose old buffer)
- [ ] `MEM-003`: HeaderBuffer grows correctly for CONTINUATION frames
- [ ] `MEM-004`: Stream dictionary cleaned up after response emission (`_streams.Remove`)

---

## Execution Order

```
Phase 1 (GAP-Closure)     ← Highest priority: closes known gaps
  ├── TASK-GAP-01          StreamIdAllocator
  ├── TASK-GAP-02          CorrelationHttp1XStage
  ├── TASK-GAP-03          CorrelationHttp20Stage
  └── TASK-GAP-04          ExtractOptionsStage

Phase 2+3 (HTTP/1.x)      ← Can be worked on in parallel
  ├── TASK-10-01..05       HTTP/1.0 Stages
  └── TASK-11-01..06       HTTP/1.1 Stages

Phase 4+5 (HTTP/2)        ← Can be worked on in parallel
  ├── TASK-20-01..07       HTTP/2 Stages
  └── TASK-H2C-01..05      HTTP/2 Connection-Level

Phase 6 (Integration)     ← Depends on Phases 2–5
  └── TASK-ENG-01..04      Engine Round-Trips

Phase 7 (Cross-Cutting)   ← Independent, can be done anytime
  └── TASK-ERR-01..04      Error & Edge Cases
```

---

## Conventions

| Aspect | Rule |
|--------|------|
| **Namespace** | `namespace TurboHttp.StreamTests.<Folder>;` (file-scoped) |
| **Class** | `public sealed class <Name>Tests : StreamTestBase` or `: EngineTestBase` |
| **DisplayName** | `"<RFC>-<Section>-<Cat>-<NNN>: <Description>"` |
| **Timeout** | `[Fact(Timeout = 10_000)]` for all async stream tests |
| **No** `#nullable enable` | As per CLAUDE.md |
| **Helpers** | Use `StreamTestBase.Materializer` or `EngineTestBase.SendAsync/SendH2Async` |
| **Fake TCP** | `EngineFakeConnectionStage` for HTTP/1.x, `H2FakeConnectionStage` for HTTP/2 |

---

## Success Criteria

- [ ] All 35 tasks implemented
- [ ] ~180 new stream tests passing
- [ ] `dotnet test ./src/TurboHttp.StreamTests/` → 0 failures
- [ ] RFC_TEST_MATRIX.md updated: GAP-005..008 → ✅
- [ ] No regression in existing tests (`dotnet test ./src/TurboHttp.sln`)