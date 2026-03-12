# Plan 3: Test Audit — RFC Compliance, Direct Production Code Testing, Http2ProtocolSession Removal

## Introduction

The TurboHttp test suite has three structural problems:

1. **`Http2ProtocolSession`** (700 lines, test-only) is a standalone mini HTTP/2 stack that tests production code _indirectly_ instead of directly. 24 test files in RFC9113 route through this wrapper instead of calling `Http2FrameDecoder`, `HpackDecoder`, and the Frame classes directly.
2. **DisplayNames are missing RFC references** in Integration tests (`CM-001`, `RE-001`, `RH-001`), RFC9113 tests (`SEC-h2-003`), and almost all StreamTests (`COR1X-001`, `11D-CH-001`, `EROUTE-001`).
3. **RFC folder structure is missing** in `TurboHttp.StreamTests/` (currently sorted by HTTP version) and in `TurboHttp.Tests/Integration/` (flat).

Goal: tests exercise production code directly, all DisplayNames carry RFC references, all three projects are sorted into RFC folders, and `Http2ProtocolSession` is deleted.

---

## Goals

- Audit report created and maintained as `docs/test-audit-report.md`
- `Http2ProtocolSession.cs` and `Http2StreamLifecycleState.cs` deleted (after all dependent tests are migrated)
- Every `[Fact]`/`[Theory]` test across all three projects has an RFC reference in its `DisplayName`
- `TurboHttp.StreamTests/` restructured into RFC folders (RFC1945/, RFC9112/, RFC9113/, Streams/)
- `TurboHttp.Tests/Integration/` dissolved into RFC folders (RFC6265/, RFC9110/, RFC9112/)
- `dotnet test` runs green with 0 compile errors and 0 regressions
- Progress tracked in `.maggus/PROGRESS_3.md` after every task

---

## User Stories

### TASK-ANA-001: Create the Audit Report
**Description:** As a developer, I want a complete audit report of all tests so that I know which tests go through `Http2ProtocolSession`, which RFC references are missing, and how the folder structure needs to be corrected.

**Acceptance Criteria:**
- [ ] File `docs/test-audit-report.md` created
- [ ] Section 1: All test files that use `Http2ProtocolSession`, listed with test count and which RFC sections they cover
- [ ] Section 2: Which RFC sections `Http2ProtocolSession` internally covers (§5.1 Stream States, §6.5 Settings, §6.7 Ping, §6.8 GoAway, §6.9 Flow Control, §8.2/§8.3 Headers/Pseudo-Headers)
- [ ] Section 3: List of all tests missing an RFC reference in DisplayName, grouped by project and file
- [ ] Section 4: Mapping table — which Integration test file belongs in which RFC folder
- [ ] Section 5: Mapping table — which StreamTests file belongs in which RFC folder
- [ ] `dotnet build` remains green (no code changes in this task)
- [ ] `.maggus/PROGRESS_3.md` created with the status of this task

---

### TASK-PSS-001: Replace Http2ProtocolSession — Stream State Tests (RFC9113 §5.1)
**Description:** As a developer, I want RFC9113-§5.1 stream state tests to call `Http2FrameDecoder` directly so that `Http2ProtocolSession` is no longer needed as an intermediary.

Affected file: `03_StreamStateMachineTests.cs`

**Background:** `Http2ProtocolSession.HandleHeaders()` and `HandleData()` track stream states. In production this logic lives in `Http20StreamStage` / `Http20ConnectionStage`. `Http2FrameDecoder` itself is stateless — it only decodes bytes → frames. Tests should verify: (a) the decoder produces correct frames, (b) invalid frames throw the correct exceptions.

**Acceptance Criteria:**
- [ ] `03_StreamStateMachineTests.cs` only calls production classes: `Http2FrameDecoder`, `Http2Frame` subclasses, `HpackDecoder`
- [ ] No import of `Http2ProtocolSession` in this file
- [ ] Every test DisplayName contains `RFC-9113-§5.1`
- [ ] At minimum these RFC scenarios covered: Idle→Open (HEADERS), Open→Closed (DATA+END_STREAM), HEADERS on stream 0 → Exception, DATA on idle stream → Exception
- [ ] `dotnet test --filter FullyQualifiedName~StreamStateMachine` green
- [ ] `.maggus/PROGRESS_3.md` updated

---

### TASK-PSS-002: Replace Http2ProtocolSession — Settings Tests (RFC9113 §6.5)
**Description:** As a developer, I want §6.5 SETTINGS tests to call `SettingsFrame` and `Http2FrameDecoder` directly.

Affected file: `04_SettingsTests.cs`

**Background:** `Http2ProtocolSession.HandleSettings()` / `ApplySettingsParameters()` contains validation logic (MAX_FRAME_SIZE range, ENABLE_PUSH 0/1, INITIAL_WINDOW_SIZE ≤ 2^31-1). In production this validation belongs to `Http20ConnectionStage`. `Http2FrameDecoder` is the direct test candidate.

**Acceptance Criteria:**
- [ ] `04_SettingsTests.cs` only uses: `SettingsFrame`, `Http2FrameDecoder`, `SettingsParameter`
- [ ] No import of `Http2ProtocolSession`
- [ ] Every test DisplayName contains `RFC-9113-§6.5`
- [ ] RFC scenarios covered: ACK flag, parameter parsing, MAX_FRAME_SIZE range, ENABLE_PUSH value, INITIAL_WINDOW_SIZE overflow
- [ ] `dotnet test --filter FullyQualifiedName~SettingsTests` green
- [ ] `.maggus/PROGRESS_3.md` updated

---

### TASK-PSS-003: Replace Http2ProtocolSession — Flow Control Tests (RFC9113 §6.9)
**Description:** As a developer, I want §6.9 flow control tests to call `WindowUpdateFrame` and `Http2FrameDecoder` directly.

Affected files: `05_FlowControlTests.cs`, `13_DecoderStreamFlowControlTests.cs`

**Background:** Flow control logic (window size, overflow checks) belongs to `Http20ConnectionStage`. The decoder only decodes `WindowUpdateFrame` bytes. Tests should exercise the decoder directly: correct frame fields, increment value, stream-0 vs stream-N.

**Acceptance Criteria:**
- [ ] Both files only use: `WindowUpdateFrame`, `DataFrame`, `Http2FrameDecoder`
- [ ] No import of `Http2ProtocolSession`
- [ ] Every test DisplayName contains `RFC-9113-§6.9`
- [ ] RFC scenarios covered: connection window (stream 0), stream window (stream N), increment values
- [ ] Tests green
- [ ] `.maggus/PROGRESS_3.md` updated

---

### TASK-PSS-004: Replace Http2ProtocolSession — GoAway/Ping/RST Tests (RFC9113 §6.4/§6.7/§6.8)
**Description:** As a developer, I want GoAway, Ping, and RST_STREAM tests to call the frame classes directly.

Affected files: `07_ErrorHandlingTests.cs`, `08_GoAwayTests.cs`

**Background:** `Http2ProtocolSession.HandleGoAway()`, `HandlePing()`, `HandleRst()` are test-only logic. In production: `Http20ConnectionStage`. Tests should verify: `GoAwayFrame`, `PingFrame`, `RstStreamFrame` decoded correctly, fields correct (LastStreamId, ErrorCode, Data).

**Acceptance Criteria:**
- [ ] Both files only use: `GoAwayFrame`, `PingFrame`, `RstStreamFrame`, `Http2FrameDecoder`
- [ ] No import of `Http2ProtocolSession`
- [ ] DisplayNames: `RFC-9113-§6.8` (GoAway), `RFC-9113-§6.7` (Ping), `RFC-9113-§6.4` (RST_STREAM)
- [ ] Tests green
- [ ] `.maggus/PROGRESS_3.md` updated

---

### TASK-PSS-005: Replace Http2ProtocolSession — Header/Pseudo-Header Tests (RFC9113 §8.2/§8.3)
**Description:** As a developer, I want §8.2/§8.3 tests to call `HpackDecoder` and `HeadersFrame` directly.

Affected files: `06_HeadersTests.cs`, `09_ContinuationFrameTests.cs`, `11_DecoderStreamValidationTests.cs`

**Background:** Header validation (uppercase errors, forbidden connection headers, duplicate :status) is production logic in `Http20StreamStage`. `HpackDecoder` itself has RFC-7541 compliance. Tests should: exercise HPACK decoding directly, verify `HeadersFrame` fields.

**Acceptance Criteria:**
- [ ] All three files only use: `HpackDecoder`, `HpackEncoder`, `HeadersFrame`, `ContinuationFrame`, `Http2FrameDecoder`
- [ ] No import of `Http2ProtocolSession`
- [ ] DisplayNames: `RFC-9113-§8.2` or `RFC-9113-§8.3`
- [ ] Scenarios covered: END_HEADERS flag, CONTINUATION chain, header block decoding
- [ ] Tests green
- [ ] `.maggus/PROGRESS_3.md` updated

---

### TASK-PSS-006: Replace Http2ProtocolSession — Security/Fuzz/Concurrency Tests
**Description:** As a developer, I want security, fuzz, and concurrency tests to call production classes directly.

Affected files: `Http2SecurityTests.cs`, `Http2FuzzHarnessTests.cs`, `Http2ResourceExhaustionTests.cs`, `Http2HighConcurrencyTests.cs`, `Http2MaxConcurrentStreamsTests.cs`, `Http2CrossComponentValidationTests.cs`

**Background:** These tests verify attack protection (CONTINUATION flood, RST Rapid Reset, PING flood, SETTINGS flood). In production this protection belongs to `Http20ConnectionStage`. Tests should use real stage tests (via `Http2StageTestHelper` or direct classes) or `Http2FrameDecoder` with explicit exception assertions.

**Acceptance Criteria:**
- [ ] All 6 files have no `Http2ProtocolSession` import
- [ ] Security tests call `Http20ConnectionStage` or `Http2FrameDecoder` directly
- [ ] DisplayNames contain RFC references (e.g. `RFC-9113-§6.5` for SETTINGS flood, `RFC-9113-§5.1` for RST Rapid Reset protection)
- [ ] `SEC-h2-XXX` codes replaced or prefixed with RFC references
- [ ] Tests green
- [ ] `.maggus/PROGRESS_3.md` updated

---

### TASK-PSS-007: Delete Http2ProtocolSession and Http2StreamLifecycleState
**Description:** As a developer, I want to delete `Http2ProtocolSession.cs` and `Http2StreamLifecycleState.cs` from the project after all dependent tests have been migrated.

**Prerequisite:** TASK-PSS-001 through TASK-PSS-006 all completed.

**Acceptance Criteria:**
- [ ] `grep -r "Http2ProtocolSession" src/ --include="*.cs"` → 0 matches (excluding deleted file)
- [ ] `src/TurboHttp.Tests/Http2ProtocolSession.cs` deleted
- [ ] `src/TurboHttp.Tests/Http2StreamLifecycleState.cs` deleted
- [ ] `dotnet build ./src/TurboHttp.sln` → 0 errors
- [ ] `dotnet test ./src/TurboHttp.sln` → all tests green
- [ ] `.maggus/PROGRESS_3.md` updated

---

### TASK-DISP-001: Add RFC References to Integration Test DisplayNames
**Description:** As a developer, I want all `TurboHttp.Tests/Integration/` tests to have RFC references in their DisplayName.

**Mapping:**
| File | RFC | Example prefix |
|------|-----|----------------|
| `CookieJarTests.cs` | RFC 6265 | `RFC-6265-§5.3-CJ-001:` |
| `RetryEvaluatorTests.cs` | RFC 9110 §9.2 | `RFC-9110-§9.2-RE-001:` |
| `RedirectHandlerTests.cs` | RFC 9110 §15.4 | `RFC-9110-§15.4-RH-001:` |
| `ConnectionReuseEvaluatorTests.cs` | RFC 9112 §9 | `RFC-9112-§9-CM-001:` |
| `TcpFragmentationTests.cs` | RFC 1945 / RFC 9112 | `RFC-1945-FRAG-001:` / `RFC-9112-FRAG-001:` |
| `HttpDecodeErrorMessagesTests.cs` | RFC 9112 | `RFC-9112-§4-34-msg-001:` |
| `PerHostConnectionLimiterTests.cs` | RFC 9110 | `RFC-9110-CONN-001:` |
| `CrossFeatureIntegrityTests.cs` | RFC 9112 / RFC 9113 | appropriate RFC refs |
| `Phase60ValidationGateTests.cs` | per content | appropriate RFC refs |

**Acceptance Criteria:**
- [ ] Every `[Fact]`/`[Theory]` in `Integration/` has an RFC reference in its DisplayName
- [ ] Existing short codes (e.g. `CM-001`) are preserved; RFC prefix is prepended
- [ ] `dotnet test --filter "FullyQualifiedName~Integration"` green
- [ ] `.maggus/PROGRESS_3.md` updated

---

### TASK-DISP-002: Add RFC References to RFC9113 Tests Without Prefix
**Description:** As a developer, I want all RFC9113 test files that lack an RFC prefix in their DisplayName (`Http2SecurityTests`, `Http2FrameTests`, `Http2EncoderSensitiveHeaderTests`, `Http2EncoderPseudoHeaderValidationTests`) to receive RFC references.

**Mapping:**
| File | RFC section |
|------|-------------|
| `Http2SecurityTests.cs` | RFC-9113-§6.5/§6.7/§6.8/§5.1 |
| `Http2FrameTests.cs` | RFC-9113-§4.1 (frame format) |
| `Http2EncoderSensitiveHeaderTests.cs` | RFC-7541-§7.1 |
| `Http2EncoderPseudoHeaderValidationTests.cs` | RFC-9113-§8.3 |

**Acceptance Criteria:**
- [ ] Every `[Fact]`/`[Theory]` in the listed files has an RFC reference in its DisplayName
- [ ] `SEC-h2-XXX` codes receive an RFC prefix (e.g. `RFC-9113-§6.5-SEC-h2-003:`)
- [ ] Tests green
- [ ] `.maggus/PROGRESS_3.md` updated

---

### TASK-DISP-003: Add RFC References to StreamTests DisplayNames
**Description:** As a developer, I want all `TurboHttp.StreamTests/` tests to have RFC references in their DisplayName.

**Affected files missing RFC refs (excerpt):**
- `Http11/CorrelationHttp1XStageTests.cs` → `COR1X-001` → `RFC-9112-§9.3-COR1X-001:`
- `Http11/Http11DecoderStageChunkedRfcTests.cs` → `11D-CH-001` → `RFC-9112-§7.1-11D-CH-001:`
- `Http11/Http11StageConnectionMgmtTests.cs` → RFC-9112-§9
- `Http20/CorrelationHttp20StageTests.cs` → RFC-9113-§5.1
- `Http20/PrependPrefaceStageTests.cs` → RFC-9113-§3.5
- `Http20/StreamIdAllocatorStageTests.cs` → RFC-9113-§5.1.1
- `Http20/Request2FrameStageTests.cs` → RFC-9113-§8.1
- `Streams/EngineVersionRoutingTests.cs` → `EROUTE-001` → RFC-9112/RFC-9113 version selection
- `Streams/RequestEnricherStageTests.cs` → RFC-9110-§7.1 (URI resolution)

**Acceptance Criteria:**
- [ ] Every `[Fact]`/`[Theory]` in `TurboHttp.StreamTests/` has an RFC reference or at minimum an RFC-adjacent code (for purely internal stages)
- [ ] Existing codes preserved; RFC prefix prepended
- [ ] Tests green
- [ ] `.maggus/PROGRESS_3.md` updated

---

### TASK-SORT-001: Move Integration Test Files into RFC Folders (TurboHttp.Tests)
**Description:** As a developer, I want `TurboHttp.Tests/Integration/` dissolved and all files moved to their matching RFC folders.

**Target structure:**
```
TurboHttp.Tests/
  RFC6265/
    CookieJarTests.cs
  RFC9110/
    (01_..03_ already present)
    04_RetryEvaluatorTests.cs
    05_RedirectHandlerTests.cs
    06_PerHostConnectionLimiterTests.cs
    07_CrossFeatureIntegrityTests.cs
  RFC9112/
    (01_..21_ already present)
    22_ConnectionReuseEvaluatorTests.cs
    23_HttpDecodeErrorMessagesTests.cs
    24_TcpFragmentationTests.cs
  RFC9113/
    (already present)
    22_Phase60ValidationGateTests.cs  ← or RFC9112 depending on content
```

**Acceptance Criteria:**
- [ ] `TurboHttp.Tests/Integration/` folder no longer exists (empty or deleted)
- [ ] All moved files: namespace updated to `TurboHttp.Tests` (without `.Integration`)
- [ ] `dotnet build` → 0 errors
- [ ] `dotnet test` → all tests green
- [ ] `.maggus/PROGRESS_3.md` updated

---

### TASK-SORT-002: Restructure StreamTests into RFC Folders
**Description:** As a developer, I want `TurboHttp.StreamTests/` sorted by RFC folder instead of by HTTP version.

**Current → Target structure:**
```
Current:          Target:
Http10/    →      RFC1945/
Http11/    →      RFC9112/
Http20/    →      RFC9113/
Streams/   →      Streams/  (unchanged — internal stages without RFC mapping)
```

**Acceptance Criteria:**
- [ ] Folders `Http10/`, `Http11/`, `Http20/` no longer exist
- [ ] Folders `RFC1945/`, `RFC9112/`, `RFC9113/`, `Streams/` exist
- [ ] All files moved, namespaces updated
- [ ] No duplicate file names
- [ ] `TurboHttp.StreamTests.csproj` contains no hard paths to old folders
- [ ] `dotnet build ./src/TurboHttp.StreamTests/` → 0 errors
- [ ] `dotnet test ./src/TurboHttp.StreamTests/` → all tests green
- [ ] `.maggus/PROGRESS_3.md` updated

---

### TASK-SORT-003: Clean Up Loose Helper Files in TurboHttp.Tests Root
**Description:** As a developer, I want root-level helper files in `TurboHttp.Tests/` moved into appropriate folders.

**Affected files in the root of `TurboHttp.Tests/`:**
- `Http2StageTestHelper.cs` → `RFC9113/` (helper for stage tests)
- `Http2ProtocolSession.cs` → deleted (TASK-PSS-007)
- `Http2StreamLifecycleState.cs` → deleted (TASK-PSS-007)

**Acceptance Criteria:**
- [ ] `Http2StageTestHelper.cs` moved to `RFC9113/Http2StageTestHelper.cs` (if still needed after PSS tasks)
- [ ] No `.cs` files directly in the root of `TurboHttp.Tests/` except `GlobalUsings.cs` / `*.csproj`
- [ ] `dotnet build` → 0 errors
- [ ] `dotnet test` → all tests green
- [ ] `.maggus/PROGRESS_3.md` final update (all tasks checked off)

---

## Functional Requirements

- FR-1: No test file may import `Http2ProtocolSession` after TASK-PSS-007 — the class will not exist
- FR-2: Every `[Fact]` and `[Theory]` must have a `DisplayName` attribute containing an RFC reference (format: `RFC-XXXX-§Y.Z-TAG-NNN: description`)
- FR-3: `TurboHttp.Tests/Integration/` must not exist after TASK-SORT-001
- FR-4: `TurboHttp.StreamTests/Http10/`, `Http11/`, `Http20/` must not exist after TASK-SORT-002
- FR-5: After each completed task `.maggus/PROGRESS_3.md` is updated with status, date, and a short note
- FR-6: Tests that exercise production code directly must not use test-infrastructure classes as intermediaries — only production classes plus xUnit assertions
- FR-7: `dotnet test ./src/TurboHttp.sln` runs green after every task — no regressions

## Non-Goals

- No new features or production code changes in this plan
- No deleting tests — every RFC section tested before must remain tested afterwards
- No restructuring of `TurboHttp.IntegrationTests/` (Kestrel fixtures) — only `TurboHttp.Tests/` and `TurboHttp.StreamTests/`
- No changes to the `RFC7541/` folder in `TurboHttp.Tests/` — already well structured
- No changes to production classes (`Http2FrameDecoder`, `HpackDecoder`, etc.)

## Technical Considerations

- **Order:** ANA-001 → PSS-001..006 (can run in parallel) → PSS-007 → DISP-001..003 (can run in parallel) → SORT-001..003 (sequential to avoid namespace conflicts)
- **Http2StageTestHelper.cs:** Keep as a helper class; after PSS tasks verify whether it is still needed
- **`Http2StreamLifecycleState`:** Enum with comment "Test-only copy" — if no test depends on it after PSS tasks, delete together with `Http2ProtocolSession`
- **Namespace convention:** After moving, all tests use `namespace TurboHttp.Tests;` or `namespace TurboHttp.StreamTests;` (file-scoped, flat — verify against existing pattern before changing)
- **RFC tag format:** The established format in StreamTests is `RFC-9113-§6.9-20CW-001:` — apply this same format across all tests
- **Build after every task:** `dotnet build --configuration Release ./src/TurboHttp.sln` must produce 0 errors

## Success Metrics

- `grep -r "Http2ProtocolSession" src/ --include="*.cs"` → 0 matches
- `grep -rn "DisplayName" src/ --include="*.cs" | grep -v "RFC-\|RFC " | wc -l` → 0 (all have RFC ref)
- `dotnet test ./src/TurboHttp.sln` → all tests green, no new failures
- `docs/test-audit-report.md` exists and is complete
- `.maggus/PROGRESS_3.md` shows all tasks completed

## Open Questions

- Does `Phase60ValidationGateTests.cs` belong to RFC9112 or RFC9113? → determine in ANA-001
- `EngineVersionRoutingTests.cs` tests internal routing logic without a direct RFC clause — is an internal code tag like `EROUTE-` sufficient, or should RFC-9112/RFC-9113 version negotiation be referenced?
- Should `Http2StageTestHelper.cs` still exist after TASK-PSS-007, or can it also be deleted?
