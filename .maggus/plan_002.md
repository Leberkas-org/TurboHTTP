# Plan: Rename StreamTests Files — Consistent NN_ Prefix Convention

## Introduction

Standardise file naming in `src/TurboHttp.StreamTests/` to match the `NN_<ThemaTests>.cs` convention already established in `src/TurboHttp.Tests/`. Currently, StreamTests uses a mix of plain descriptive names, `*RfcTests` suffixes, and occasional `NN_` prefixes (only 2 files in `Streams/`). This creates inconsistency and makes it hard to see logical grouping at a glance.

**Changes:**
- Add `NN_` two-digit prefixes to ALL test files, grouped by topic within each folder
- Drop the `*RfcTests` / `*RfcRoundTripTests` suffixes — merge into descriptive names
- Keep folder structure unchanged (RFC subfolders + Streams/ + IO/)

Max 5 files per task. Only file renames — no code changes.

## Goals

- Every test file in StreamTests follows the `NN_DescriptiveNameTests.cs` pattern
- Files within each folder are logically grouped by topic via numbering
- The `*RfcTests` suffix is eliminated — all files use plain descriptive names
- No code changes, no namespace changes, no test logic changes
- `dotnet build` and `dotnet test` succeed after each task

## Naming Convention

```
Pattern:  NN_DescriptiveNameTests.cs
Example:  01_Http10EncoderStageTests.cs
```

- Two-digit prefix groups files by topic within each RFC folder
- Descriptive name matches the stage or feature under test
- `Tests` suffix on every file
- No `RfcTests` or `RfcRoundTripTests` suffixes

## Rename Mappings

### RFC1945/ (HTTP/1.0) — 8 files

| # | Current Name | New Name | Rationale |
|---|-------------|----------|-----------|
| 01 | `Http10EncoderStageTests.cs` | `01_Http10EncoderStageTests.cs` | Stage behaviour in isolation |
| 02 | `Http10EncoderStageRfcTests.cs` | `02_Http10EncoderStageWireFormatTests.cs` | Tests request-line format, headers, Content-Length injection per §5 |
| 03 | `Http10DecoderStageTests.cs` | `03_Http10DecoderStageTests.cs` | Stage behaviour in isolation |
| 04 | `Http10DecoderStageRfcTests.cs` | `04_Http10DecoderStageResponseParsingTests.cs` | Tests status-code parsing, header fields, body framing per §6 |
| 05 | `Http10StageRoundTripMethodTests.cs` | `05_Http10RoundTripMethodTests.cs` | GET/POST/HEAD encode→decode per §8 |
| 06 | `Http10StageRoundTripHeaderBodyTests.cs` | `06_Http10RoundTripHeaderBodyPreservationTests.cs` | Verifies headers+body survive encode→decode intact |
| 07 | `Http10StageTcpFragmentationTests.cs` | `07_Http10TcpFragmentationReassemblyTests.cs` | Decoder reassembly across split TCP chunks |
| 08 | `Http10EngineRfcRoundTripTests.cs` | `08_Http10EngineEndToEndTests.cs` | Full engine flow (not just stages) per §4.1 |

**Grouping:** Encoder (01-02) → Decoder (03-04) → RoundTrip (05-06) → Fragmentation (07) → Engine (08)

---

### RFC6265/ (Cookies) — 2 files

| # | Current Name | New Name |
|---|-------------|----------|
| 01 | `CookieInjectionStageTests.cs` | `01_CookieInjectionStageTests.cs` |
| 02 | `CookieStorageStageTests.cs` | `02_CookieStorageStageTests.cs` |

**Grouping:** Injection (01) → Storage (02)

---

### RFC7541/ (HPACK) — 1 file

| # | Current Name | New Name |
|---|-------------|----------|
| 01 | `Http20HpackStreamTests.cs` | `01_HpackStreamTests.cs` |

---

### RFC9110/ (HTTP Semantics) — 3 files

| # | Current Name | New Name |
|---|-------------|----------|
| 01 | `DecompressionStageTests.cs` | `01_DecompressionStageTests.cs` |
| 02 | `RedirectStageTests.cs` | `02_RedirectStageTests.cs` |
| 03 | `RetryStageTests.cs` | `03_RetryStageTests.cs` |

**Grouping:** Content-Encoding (01) → Redirects (02) → Retries (03)

---

### RFC9111/ (Caching) — 2 files

| # | Current Name | New Name |
|---|-------------|----------|
| 01 | `CacheLookupStageTests.cs` | `01_CacheLookupStageTests.cs` |
| 02 | `CacheStorageStageTests.cs` | `02_CacheStorageStageTests.cs` |

**Grouping:** Lookup (01) → Storage (02)

---

### RFC9112/ (HTTP/1.1) — 13 files

| # | Current Name | New Name | Rationale |
|---|-------------|----------|-----------|
| 01 | `Http11EncoderStageTests.cs` | `01_Http11EncoderStageTests.cs` | Stage behaviour in isolation |
| 02 | `Http11EncoderStageRfcTests.cs` | `02_Http11EncoderStageWireFormatTests.cs` | Tests request-line, Host header, Content-Length, chunked TE per §3–§6 |
| 03 | `Http11BatchEncodingTests.cs` | `03_Http11BatchEncodingTests.cs` | Batch/bulk encoding scenarios |
| 04 | `Http11DecoderStageTests.cs` | `04_Http11DecoderStageTests.cs` | Stage behaviour in isolation |
| 05 | `Http11DecoderStageChunkedRfcTests.cs` | `05_Http11DecoderStageChunkedTransferTests.cs` | Chunk reassembly, chunk extensions, trailing headers per §7.1 |
| 06 | `Http11CorrelationStageTests.cs` | `06_Http11CorrelationStageTests.cs` | FIFO request-response matching |
| 07 | `Http11ResponseCorrelationTests.cs` | `07_Http11ResponseCorrelationTests.cs` | Response-to-request pairing |
| 08 | `Http11StageRoundTripPipelineTests.cs` | `08_Http11RoundTripPipeliningTests.cs` | Pipelined requests matched in FIFO order per §9.3 |
| 09 | `Http11StageStatusCodeTests.cs` | `09_Http11StatusCodeParsingTests.cs` | 1xx/2xx/3xx/4xx/5xx parsing per RFC 9110 §15.1 |
| 10 | `Http11StageFragmentationTests.cs` | `10_Http11TcpFragmentationReassemblyTests.cs` | Decoder reassembly across partial TCP segments |
| 11 | `Http11StageConnectionMgmtTests.cs` | `11_Http11KeepAliveCloseTests.cs` | Connection header, keep-alive/close semantics per §9.6–§9.8 |
| 12 | `Http11ConnectionReuseStageTests.cs` | `12_Http11ConnectionReuseStageTests.cs` | Connection reuse decision stage |
| 13 | `Http11EngineRfcRoundTripTests.cs` | `13_Http11EngineEndToEndTests.cs` | Full engine flow incl. chunked encoding per §3–§9 |

**Grouping:** Encoder (01-03) → Decoder (04-05) → Correlation (06-07) → RoundTrip (08-10) → Connection (11-12) → Engine (13)

---

### RFC9113/ (HTTP/2) — 22 files

| # | Current Name | New Name | Rationale |
|---|-------------|----------|-----------|
| 01 | `Http20EncoderStageTests.cs` | `01_Http20EncoderStageTests.cs` | Stage behaviour in isolation |
| 02 | `Http20EncoderStageRfcTests.cs` | `02_Http20EncoderStageFrameSerializationTests.cs` | 9-byte frame header + payload format per §4–§6 |
| 03 | `Http20BatchEncodingTests.cs` | `03_Http20BatchEncodingTests.cs` | Batch/bulk encoding scenarios |
| 04 | `Http20DecoderStageTests.cs` | `04_Http20DecoderStageTests.cs` | Stage behaviour in isolation |
| 05 | `Http20DecoderStageRfcTests.cs` | `05_Http20DecoderStageFrameParsingTests.cs` | Frame type detection, flags, stream IDs, errors per §4–§6 |
| 06 | `Http20ConnectionStageSettingsTests.cs` | `06_Http20ConnectionStageSettingsTests.cs` | SETTINGS frame handling per §6.5 |
| 07 | `Http20ConnectionStagePingTests.cs` | `07_Http20ConnectionStagePingTests.cs` | PING frame handling per §6.7 |
| 08 | `Http20ConnectionStageGoAwayTests.cs` | `08_Http20ConnectionStageGoAwayTests.cs` | GOAWAY frame handling per §6.8 |
| 09 | `Http20ConnectionStageFlowControlTests.cs` | `09_Http20ConnectionStageFlowControlTests.cs` | WINDOW_UPDATE and flow control per §5.2 |
| 10 | `Http20ConnectionStageBackpressureTests.cs` | `10_Http20ConnectionStageBackpressureTests.cs` | Backpressure when flow control window exhausted |
| 11 | `Http20ConnectionStageStreamAcquireTests.cs` | `11_Http20ConnectionStageStreamAcquireTests.cs` | Stream acquisition and lifecycle |
| 12 | `Http20ConnectionPrefaceRfcTests.cs` | `12_Http20ConnectionPrefaceEmissionTests.cs` | 24-byte client magic emitted exactly once per §3.4 |
| 13 | `Http20PrependPrefaceStageTests.cs` | `13_Http20PrependPrefaceStageTests.cs` | Preface injection stage behaviour |
| 14 | `Http20StreamStageTests.cs` | `14_Http20StreamStageTests.cs` | Stream stage frame assembly |
| 15 | `Http20StreamStageMemoryTests.cs` | `15_Http20StreamStageMemoryTests.cs` | Memory lifecycle and buffer ownership |
| 16 | `Http20StreamIdAllocatorStageTests.cs` | `16_Http20StreamIdAllocatorStageTests.cs` | Stream ID allocator stage behaviour |
| 17 | `Http20StreamIdRfcTests.cs` | `17_Http20StreamIdOddMonotonicTests.cs` | Odd-numbered IDs (1,3,5…), monotonic increase, exhaustion per §5.1.1 |
| 18 | `Http20RequestToFrameStageTests.cs` | `18_Http20RequestToFrameStageTests.cs` | Request→frame conversion stage |
| 19 | `Http20PseudoHeaderRfcTests.cs` | `19_Http20PseudoHeaderGenerationTests.cs` | :method/:scheme/:path/:authority generation per §8.3 |
| 20 | `Http20ForbiddenHeaderRfcTests.cs` | `20_Http20ForbiddenHeaderStrippingTests.cs` | Connection/TE headers stripped from HTTP/2 per §8.2.2 |
| 21 | `Http20CorrelationStageTests.cs` | `21_Http20CorrelationStageTests.cs` | Stream-ID-based request-response matching |
| 22 | `Http20EngineRfcRoundTripTests.cs` | `22_Http20EngineEndToEndTests.cs` | Full engine flow with HPACK, SETTINGS, frames per §3–§8 |

**Grouping:** Encoder (01-03) → Decoder (04-05) → Connection (06-13) → Stream (14-17) → Request/Headers (18-20) → Correlation (21) → Engine (22)

---

### Streams/ (16 files) — Stage Infrastructure

| # | Current Name | New Name |
|---|-------------|----------|
| 01 | `01_StageOrderingTests.cs` | `01_StageOrderingTests.cs` (unchanged) |
| 02 | `02_TaskFixVerificationTests.cs` | `02_TaskFixVerificationTests.cs` (unchanged) |
| 03 | `StageLifecycleTests.cs` | `03_StageLifecycleTests.cs` |
| 04 | `AsyncBoundaryTests.cs` | `04_AsyncBoundaryTests.cs` |
| 05 | `ConnectionStageTests.cs` | `05_ConnectionStageTests.cs` |
| 06 | `RequestEnricherStageTests.cs` | `06_RequestEnricherStageTests.cs` |
| 07 | `ExtractOptionsStageTests.cs` | `07_ExtractOptionsStageTests.cs` |
| 08 | `EncoderStageBufferTests.cs` | `08_EncoderStageBufferTests.cs` |
| 09 | `DecoderStagePartialTests.cs` | `09_DecoderStagePartialTests.cs` |
| 10 | `EngineVersionRoutingTests.cs` | `10_EngineVersionRoutingTests.cs` |
| 11 | `EnginePipelineWiringTests.cs` | `11_EnginePipelineWiringTests.cs` |
| 12 | `HostKeySubFlowTests.cs` | `12_HostKeySubFlowTests.cs` |
| 13 | `GroupByHostKeyQueueSizeTests.cs` | `13_GroupByHostKeyQueueSizeTests.cs` |
| 14 | `FeedbackBufferOptimizationTests.cs` | `14_FeedbackBufferOptimizationTests.cs` |
| 15 | `MaterializerBufferTuningTests.cs` | `15_MaterializerBufferTuningTests.cs` |
| 16 | `LoopbackBenchmarkStageTests.cs` | `16_LoopbackBenchmarkStageTests.cs` |

**Grouping:** Infrastructure (01-04) → Stages (05-09) → Engine (10-11) → HostKey/Routing (12-13) → Performance (14-16)

---

### IO/ (9 files) — Actor & Connection Tests

| # | Current Name | New Name |
|---|-------------|----------|
| — | `IoActorTestBase.cs` | `IoActorTestBase.cs` (base class, no prefix) |
| 01 | `ConnectionActorTests.cs` | `01_ConnectionActorTests.cs` |
| 02 | `ConnectionHandleTests.cs` | `02_ConnectionHandleTests.cs` |
| 03 | `ConnectionStateTests.cs` | `03_ConnectionStateTests.cs` |
| 04 | `HostPoolTests.cs` | `04_HostPoolTests.cs` |
| 05 | `HostPoolActorEnsureHostTests.cs` | `05_HostPoolActorEnsureHostTests.cs` |
| 06 | `HostPoolActorSelectConnectionTests.cs` | `06_HostPoolActorSelectConnectionTests.cs` |
| 07 | `HostPoolActorStreamLifecycleTests.cs` | `07_HostPoolActorStreamLifecycleTests.cs` |

**Grouping:** Connection (01-03) → HostPool (04-07). Base class keeps no prefix.

---

## User Stories

---

### TASK-001: Rename RFC1945/ files (HTTP/1.0 stream tests)
**Description:** As a developer, I want RFC1945/ stream test files to follow the `NN_` prefix convention so that file ordering reflects logical grouping.

**Files (5):**
1. `Http10EncoderStageTests.cs` → `01_Http10EncoderStageTests.cs`
2. `Http10EncoderStageRfcTests.cs` → `02_Http10EncoderStageWireFormatTests.cs`
3. `Http10DecoderStageTests.cs` → `03_Http10DecoderStageTests.cs`
4. `Http10DecoderStageRfcTests.cs` → `04_Http10DecoderStageResponseParsingTests.cs`
5. `Http10StageRoundTripMethodTests.cs` → `05_Http10RoundTripMethodTests.cs`

**Acceptance Criteria:**
- [x] Files renamed via `git mv` to preserve history
- [x] Class names inside files updated to match new file names
- [x] Namespaces remain unchanged (`TurboHttp.StreamTests.RFC1945`)
- [x] `dotnet build` succeeds
- [x] `dotnet test --filter "FullyQualifiedName~StreamTests.RFC1945"` passes

---

### TASK-002: Rename remaining RFC1945/ files
**Description:** As a developer, I want the remaining RFC1945/ stream test files renamed.

**Files (3):**
1. `Http10StageRoundTripHeaderBodyTests.cs` → `06_Http10RoundTripHeaderBodyPreservationTests.cs`
2. `Http10StageTcpFragmentationTests.cs` → `07_Http10TcpFragmentationReassemblyTests.cs`
3. `Http10EngineRfcRoundTripTests.cs` → `08_Http10EngineEndToEndTests.cs`

**Acceptance Criteria:**
- [x] Files renamed via `git mv`
- [x] Class names updated to match new file names
- [x] Namespaces unchanged
- [x] `dotnet build` succeeds
- [x] `dotnet test --filter "FullyQualifiedName~StreamTests.RFC1945"` passes

---

### TASK-003: Rename RFC6265/, RFC7541/, RFC9110/ files
**Description:** As a developer, I want the smaller RFC folders renamed to `NN_` convention.

**Files (6 total, but split: 2 + 1 + 3):**
1. `RFC6265/CookieInjectionStageTests.cs` → `RFC6265/01_CookieInjectionStageTests.cs`
2. `RFC6265/CookieStorageStageTests.cs` → `RFC6265/02_CookieStorageStageTests.cs`
3. `RFC7541/Http20HpackStreamTests.cs` → `RFC7541/01_HpackStreamTests.cs`
4. `RFC9110/DecompressionStageTests.cs` → `RFC9110/01_DecompressionStageTests.cs`
5. `RFC9110/RedirectStageTests.cs` → `RFC9110/02_RedirectStageTests.cs`

**Acceptance Criteria:**
- [x] Files renamed via `git mv`
- [x] Class names updated (e.g. `Http20HpackStreamTests` → `HpackStreamTests`)
- [x] Namespaces unchanged
- [x] `dotnet build` succeeds
- [x] `dotnet test --filter "FullyQualifiedName~StreamTests.RFC6265"` passes
- [x] `dotnet test --filter "FullyQualifiedName~StreamTests.RFC7541"` passes
- [x] `dotnet test --filter "FullyQualifiedName~StreamTests.RFC9110"` passes

---

### TASK-004: Rename remaining RFC9110/ and RFC9111/ files
**Description:** As a developer, I want the remaining RFC9110 and RFC9111 files renamed.

**Files (3):**
1. `RFC9110/RetryStageTests.cs` → `RFC9110/03_RetryStageTests.cs`
2. `RFC9111/CacheLookupStageTests.cs` → `RFC9111/01_CacheLookupStageTests.cs`
3. `RFC9111/CacheStorageStageTests.cs` → `RFC9111/02_CacheStorageStageTests.cs`

**Acceptance Criteria:**
- [ ] Files renamed via `git mv`
- [ ] Class names updated to match
- [ ] Namespaces unchanged
- [ ] `dotnet build` succeeds
- [ ] `dotnet test --filter "FullyQualifiedName~StreamTests.RFC9110"` passes
- [ ] `dotnet test --filter "FullyQualifiedName~StreamTests.RFC9111"` passes

---

### TASK-005: Rename RFC9112/ files — Encoder group (01-03)
**Description:** As a developer, I want RFC9112/ encoder test files renamed.

**Files (3):**
1. `Http11EncoderStageTests.cs` → `01_Http11EncoderStageTests.cs`
2. `Http11EncoderStageRfcTests.cs` → `02_Http11EncoderStageWireFormatTests.cs`
3. `Http11BatchEncodingTests.cs` → `03_Http11BatchEncodingTests.cs`

**Acceptance Criteria:**
- [ ] Files renamed via `git mv`
- [ ] Class names updated (`Http11EncoderStageRfcTests` → `Http11EncoderStageWireFormatTests`)
- [ ] Namespaces unchanged
- [ ] `dotnet build` succeeds

---

### TASK-006: Rename RFC9112/ files — Decoder & Correlation group (04-07)
**Description:** As a developer, I want RFC9112/ decoder and correlation test files renamed.

**Files (4):**
1. `Http11DecoderStageTests.cs` → `04_Http11DecoderStageTests.cs`
2. `Http11DecoderStageChunkedRfcTests.cs` → `05_Http11DecoderStageChunkedTransferTests.cs`
3. `Http11CorrelationStageTests.cs` → `06_Http11CorrelationStageTests.cs`
4. `Http11ResponseCorrelationTests.cs` → `07_Http11ResponseCorrelationTests.cs`

**Acceptance Criteria:**
- [ ] Files renamed via `git mv`
- [ ] Class names updated (`Http11DecoderStageChunkedRfcTests` → `Http11DecoderStageChunkedTransferTests`)
- [ ] Namespaces unchanged
- [ ] `dotnet build` succeeds

---

### TASK-007: Rename RFC9112/ files — RoundTrip, Connection & Engine group (08-13)
**Description:** As a developer, I want the remaining RFC9112/ test files renamed.

**Files (6 — exception to 5-file limit due to simple renames):**
1. `Http11StageRoundTripPipelineTests.cs` → `08_Http11RoundTripPipeliningTests.cs`
2. `Http11StageStatusCodeTests.cs` → `09_Http11StatusCodeParsingTests.cs`
3. `Http11StageFragmentationTests.cs` → `10_Http11TcpFragmentationReassemblyTests.cs`
4. `Http11StageConnectionMgmtTests.cs` → `11_Http11KeepAliveCloseTests.cs`
5. `Http11ConnectionReuseStageTests.cs` → `12_Http11ConnectionReuseStageTests.cs`

**Acceptance Criteria:**
- [ ] Files renamed via `git mv`
- [ ] Class names updated (drop `Stage` prefix from round-trip/status/fragmentation/connection names)
- [ ] Namespaces unchanged
- [ ] `dotnet build` succeeds

---

### TASK-008: Rename RFC9112/ Engine file + RFC9113/ Encoder group (01-03)
**Description:** As a developer, I want the RFC9112 engine file and RFC9113 encoder files renamed.

**Files (4):**
1. `RFC9112/Http11EngineRfcRoundTripTests.cs` → `RFC9112/13_Http11EngineEndToEndTests.cs`
2. `RFC9113/Http20EncoderStageTests.cs` → `RFC9113/01_Http20EncoderStageTests.cs`
3. `RFC9113/Http20EncoderStageRfcTests.cs` → `RFC9113/02_Http20EncoderStageFrameSerializationTests.cs`
4. `RFC9113/Http20BatchEncodingTests.cs` → `RFC9113/03_Http20BatchEncodingTests.cs`

**Acceptance Criteria:**
- [ ] Files renamed via `git mv`
- [ ] Class names updated
- [ ] Namespaces unchanged
- [ ] `dotnet build` succeeds
- [ ] `dotnet test --filter "FullyQualifiedName~StreamTests.RFC9112"` passes

---

### TASK-009: Rename RFC9113/ Decoder & Connection group (04-08)
**Description:** As a developer, I want the RFC9113/ decoder and connection stage files renamed.

**Files (5):**
1. `Http20DecoderStageTests.cs` → `04_Http20DecoderStageTests.cs`
2. `Http20DecoderStageRfcTests.cs` → `05_Http20DecoderStageFrameParsingTests.cs`
3. `Http20ConnectionStageSettingsTests.cs` → `06_Http20ConnectionStageSettingsTests.cs`
4. `Http20ConnectionStagePingTests.cs` → `07_Http20ConnectionStagePingTests.cs`
5. `Http20ConnectionStageGoAwayTests.cs` → `08_Http20ConnectionStageGoAwayTests.cs`

**Acceptance Criteria:**
- [ ] Files renamed via `git mv`
- [ ] Class names updated (`Http20DecoderStageRfcTests` → `Http20DecoderStageFrameParsingTests`)
- [ ] Namespaces unchanged
- [ ] `dotnet build` succeeds

---

### TASK-010: Rename RFC9113/ Connection group continued (09-13)
**Description:** As a developer, I want the remaining RFC9113/ connection stage files renamed.

**Files (5):**
1. `Http20ConnectionStageFlowControlTests.cs` → `09_Http20ConnectionStageFlowControlTests.cs`
2. `Http20ConnectionStageBackpressureTests.cs` → `10_Http20ConnectionStageBackpressureTests.cs`
3. `Http20ConnectionStageStreamAcquireTests.cs` → `11_Http20ConnectionStageStreamAcquireTests.cs`
4. `Http20ConnectionPrefaceRfcTests.cs` → `12_Http20ConnectionPrefaceEmissionTests.cs`
5. `Http20PrependPrefaceStageTests.cs` → `13_Http20PrependPrefaceStageTests.cs`

**Acceptance Criteria:**
- [ ] Files renamed via `git mv`
- [ ] Class names updated (`Http20ConnectionPrefaceRfcTests` → `Http20ConnectionPrefaceEmissionTests`)
- [ ] Namespaces unchanged
- [ ] `dotnet build` succeeds

---

### TASK-011: Rename RFC9113/ Stream & StreamId group (14-17)
**Description:** As a developer, I want the RFC9113/ stream stage and stream ID files renamed.

**Files (4):**
1. `Http20StreamStageTests.cs` → `14_Http20StreamStageTests.cs`
2. `Http20StreamStageMemoryTests.cs` → `15_Http20StreamStageMemoryTests.cs`
3. `Http20StreamIdAllocatorStageTests.cs` → `16_Http20StreamIdAllocatorStageTests.cs`
4. `Http20StreamIdRfcTests.cs` → `17_Http20StreamIdOddMonotonicTests.cs`

**Acceptance Criteria:**
- [ ] Files renamed via `git mv`
- [ ] Class names updated (`Http20StreamIdRfcTests` → `Http20StreamIdOddMonotonicTests`)
- [ ] Namespaces unchanged
- [ ] `dotnet build` succeeds

---

### TASK-012: Rename RFC9113/ Request, Headers, Correlation & Engine group (18-22)
**Description:** As a developer, I want the remaining RFC9113/ files renamed.

**Files (5):**
1. `Http20RequestToFrameStageTests.cs` → `18_Http20RequestToFrameStageTests.cs`
2. `Http20PseudoHeaderRfcTests.cs` → `19_Http20PseudoHeaderGenerationTests.cs`
3. `Http20ForbiddenHeaderRfcTests.cs` → `20_Http20ForbiddenHeaderStrippingTests.cs`
4. `Http20CorrelationStageTests.cs` → `21_Http20CorrelationStageTests.cs`
5. `Http20EngineRfcRoundTripTests.cs` → `22_Http20EngineEndToEndTests.cs`

**Acceptance Criteria:**
- [ ] Files renamed via `git mv`
- [ ] Class names updated (drop `Rfc` from pseudo-header, forbidden-header, engine round-trip)
- [ ] Namespaces unchanged
- [ ] `dotnet build` succeeds
- [ ] `dotnet test --filter "FullyQualifiedName~StreamTests.RFC9113"` passes (full RFC9113 suite)

---

### TASK-013: Rename Streams/ files — Infrastructure group (01-05)
**Description:** As a developer, I want the Streams/ infrastructure test files to have consistent NN_ prefixes.

**Files (5):**
1. `01_StageOrderingTests.cs` — already correct (no change)
2. `02_TaskFixVerificationTests.cs` — already correct (no change)
3. `StageLifecycleTests.cs` → `03_StageLifecycleTests.cs`
4. `AsyncBoundaryTests.cs` → `04_AsyncBoundaryTests.cs`
5. `ConnectionStageTests.cs` → `05_ConnectionStageTests.cs`

**Acceptance Criteria:**
- [ ] 3 files renamed via `git mv` (01_ and 02_ already correct)
- [ ] Class names unchanged (only file prefix added)
- [ ] Namespaces unchanged
- [ ] `dotnet build` succeeds

---

### TASK-014: Rename Streams/ files — Stages group (06-11)
**Description:** As a developer, I want the Streams/ stage and engine test files renamed.

**Files (6 — but 2 files only need prefix, trivial renames):**
1. `RequestEnricherStageTests.cs` → `06_RequestEnricherStageTests.cs`
2. `ExtractOptionsStageTests.cs` → `07_ExtractOptionsStageTests.cs`
3. `EncoderStageBufferTests.cs` → `08_EncoderStageBufferTests.cs`
4. `DecoderStagePartialTests.cs` → `09_DecoderStagePartialTests.cs`
5. `EngineVersionRoutingTests.cs` → `10_EngineVersionRoutingTests.cs`

**Acceptance Criteria:**
- [ ] Files renamed via `git mv`
- [ ] Class names unchanged
- [ ] Namespaces unchanged
- [ ] `dotnet build` succeeds

---

### TASK-015: Rename Streams/ files — Engine, HostKey & Performance group (11-16)
**Description:** As a developer, I want the remaining Streams/ test files renamed.

**Files (5):**
1. `EnginePipelineWiringTests.cs` → `11_EnginePipelineWiringTests.cs`
2. `HostKeySubFlowTests.cs` → `12_HostKeySubFlowTests.cs`
3. `GroupByHostKeyQueueSizeTests.cs` → `13_GroupByHostKeyQueueSizeTests.cs`
4. `FeedbackBufferOptimizationTests.cs` → `14_FeedbackBufferOptimizationTests.cs`
5. `MaterializerBufferTuningTests.cs` → `15_MaterializerBufferTuningTests.cs`

**Acceptance Criteria:**
- [ ] Files renamed via `git mv`
- [ ] Class names unchanged
- [ ] Namespaces unchanged
- [ ] `dotnet build` succeeds

---

### TASK-016: Rename Streams/ last file + IO/ Connection group (01-03)
**Description:** As a developer, I want the last Streams/ file and IO/ connection files renamed.

**Files (4):**
1. `Streams/LoopbackBenchmarkStageTests.cs` → `Streams/16_LoopbackBenchmarkStageTests.cs`
2. `IO/ConnectionActorTests.cs` → `IO/01_ConnectionActorTests.cs`
3. `IO/ConnectionHandleTests.cs` → `IO/02_ConnectionHandleTests.cs`
4. `IO/ConnectionStateTests.cs` → `IO/03_ConnectionStateTests.cs`

**Acceptance Criteria:**
- [ ] Files renamed via `git mv`
- [ ] Class names unchanged
- [ ] `IoActorTestBase.cs` remains unchanged (base class, no prefix)
- [ ] Namespaces unchanged
- [ ] `dotnet build` succeeds
- [ ] `dotnet test --filter "FullyQualifiedName~StreamTests.Streams"` passes

---

### TASK-017: Rename IO/ HostPool group (04-07)
**Description:** As a developer, I want the IO/ host pool actor test files renamed.

**Files (4):**
1. `HostPoolTests.cs` → `04_HostPoolTests.cs`
2. `HostPoolActorEnsureHostTests.cs` → `05_HostPoolActorEnsureHostTests.cs`
3. `HostPoolActorSelectConnectionTests.cs` → `06_HostPoolActorSelectConnectionTests.cs`
4. `HostPoolActorStreamLifecycleTests.cs` → `07_HostPoolActorStreamLifecycleTests.cs`

**Acceptance Criteria:**
- [ ] Files renamed via `git mv`
- [ ] Class names unchanged
- [ ] Namespaces unchanged
- [ ] `dotnet build` succeeds
- [ ] `dotnet test --filter "FullyQualifiedName~StreamTests.IO"` passes
- [ ] Full test suite passes: `dotnet test` on TurboHttp.StreamTests project

---

## Functional Requirements

- FR-1: Every test file (except base classes) must follow the `NN_DescriptiveNameTests.cs` pattern
- FR-2: All renames must use `git mv` to preserve file history
- FR-3: Class names inside files must be updated to match the new file name (minus the `NN_` prefix)
- FR-4: The `*RfcTests` suffix is replaced with a descriptive name matching the actual test focus:
  - Encoder wire format tests → `*WireFormatTests` (request-line, headers, Content-Length)
  - Decoder parsing tests → `*ResponseParsingTests` or `*FrameParsingTests`
  - Frame serialization tests → `*FrameSerializationTests`
  - Chunked transfer tests → `*ChunkedTransferTests`
  - Connection preface tests → `*PrefaceEmissionTests`
  - Stream ID tests → `*OddMonotonicTests` (odd-numbered, monotonically increasing)
  - Pseudo-header tests → `*PseudoHeaderGenerationTests`
  - Forbidden header tests → `*ForbiddenHeaderStrippingTests`
- FR-5: The `*RfcRoundTripTests` suffix is replaced with `*EndToEndTests` for engine-level tests (full protocol flow)
- FR-6: The `*Stage` infix in round-trip/fragmentation/status test names is dropped (e.g. `Http11StageRoundTrip` → `Http11RoundTrip`), and names are made more specific (e.g. `StatusCodeTests` → `StatusCodeParsingTests`, `FragmentationTests` → `TcpFragmentationReassemblyTests`)
- FR-7: Namespaces must NOT change — they stay as `TurboHttp.StreamTests.<Folder>`
- FR-8: No test logic, assertions, or test data changes
- FR-9: Base classes (`StreamTestBase`, `EngineTestBase`, `SimpleMemoryOwner`, `IoActorTestBase`) keep their names (no prefix)
- FR-10: `dotnet build` and `dotnet test` must succeed after each task
- FR-11: Maximum 5 files per task (with minor exceptions for trivial prefix-only renames)

## Non-Goals

- No changes to test logic, assertions, or test data
- No changes to namespaces
- No moving files between folders
- No changes to production code
- No changes to the TurboHttp.Tests project (already uses NN_ convention)
- No changes to base classes or helper files

## Technical Considerations

- **`git mv` preserves history**: Always use `git mv old new` rather than delete + create, so `git log --follow` works
- **Class name must match file name**: C# convention — if file is `02_Http11EncoderStageComplianceTests.cs`, class should be `Http11EncoderStageComplianceTests` (without the `NN_` prefix)
- **No namespace changes**: The namespace is folder-based (`TurboHttp.StreamTests.RFC9113`), and the folder structure doesn't change
- **Cross-references**: Some test files may reference other test classes by name — check for `typeof()`, `nameof()`, or string-based references after class renames
- **IDE support**: After `git mv`, IDEs may need a reload to pick up the renames

## Success Metrics

- All 75 test files (excluding 4 base classes) follow the `NN_DescriptiveNameTests.cs` pattern
- Zero `*RfcTests` or `*RfcRoundTripTests` suffixes remain
- `dotnet test` passes with 0 failures on the full StreamTests project
- `git log --follow` works for every renamed file

## Open Questions

- Should `SimpleMemoryOwner.cs` and `StreamTestBase.cs` at root level get a `00_` prefix or stay as-is?
- If any test class is referenced by name in other files (e.g. collection fixtures), should those references be updated in the same task or a follow-up?
