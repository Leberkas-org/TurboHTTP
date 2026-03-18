# Plan 6: RFC Test Coverage Audit, Reorganisation & Documentation Consolidation

## Introduction

A full audit of the TurboHttp test suite revealed four categories of work:

1. **Misplaced RFC tests** — 5 files in `Integration/` test a single RFC class and belong in their RFC folder
2. **Non-RFC integration files to delete** — 4 files in `Integration/` test no specific RFC behavior and should be removed
3. **Naming convention violations** — 9 RFC9113 files do not follow the `NN_<ThemaTests>.cs` convention
4. **Coverage gaps** — 1 MUST gap (RFC 9113 Push-Promise state machine) + missing dedicated StreamTests for 3 stages

Additionally, two RFC documents (`RFC_COMPLIANCE.md` + `RFC_TEST_MATRIX.md`) contain partially outdated data and will be merged into a single `RFC_COVERAGE.md`.

> **Scope:** Protocol-Layer Unit Tests + Stream Tests. No end-to-end tests against Kestrel (→ GAP_LIST.md GAP-004).

---

## Audit Result: Integration/ Folder

| File | Production Class | RFC | Verdict |
|------|-----------------|-----|---------|
| `RedirectHandlerTests.cs` | `RedirectHandler` | RFC 9110 §15.4 | **MOVE** → `RFC9110/01_` |
| `RetryEvaluatorTests.cs` | `RetryEvaluator` | RFC 9110 §9.2 | **MOVE** → `RFC9110/02_` |
| `ConnectionReuseEvaluatorTests.cs` | `ConnectionReuseEvaluator` | RFC 9112 §9 | **MOVE** → `RFC9112/22_` |
| `PerHostConnectionLimiterTests.cs` | `PerHostConnectionLimiter` | RFC 9112 §9 | **MOVE** → `RFC9112/23_` |
| `CookieJarTests.cs` | `CookieJar` | RFC 6265 | **MOVE** → `RFC6265/01_` |
| `CrossFeatureIntegrityTests.cs` | Multiple (cross-RFC) | None | **DELETE** |
| `HttpDecodeErrorMessagesTests.cs` | `HttpDecoderException` | None | **DELETE** |
| `Phase60ValidationGateTests.cs` | Multiple decoders | None | **DELETE** |
| `TurboClientOptionsTests.cs` | `TurboClientOptions` | None | **DELETE** |

After all moves and deletions the `Integration/` folder will be **empty and removed**.

---

## Goals

- All RFC 9110 tests live in `RFC9110/` with correct `NN_` prefix
- All RFC 9112 tests live in `RFC9112/` (including connection reuse and per-host limits)
- New `RFC6265/` folder holds all cookie tests
- `Integration/` folder is completely removed
- All RFC 9113 files follow the `NN_` naming convention
- RFC 9113 §5.1.1 Push-Promise state machine (MUST) has dedicated tests
- RFC 9113 §6.1/§6.2 PADDED flag (SHOULD) has dedicated tests
- `CorrelationHttp1XStage`, `CorrelationHttp20Stage`, `StreamIdAllocatorStage` have dedicated StreamTest files
- `RFC_COMPLIANCE.md` and `RFC_TEST_MATRIX.md` replaced by `RFC_COVERAGE.md`
- `CLAUDE.md` accurately reflects the current project state

---

## User Stories

### TASK-RFC-001: Relocate RFC 9110 Tests — Redirect & Retry

**Description:** As a developer, I want `RedirectHandlerTests.cs` and `RetryEvaluatorTests.cs` moved from `Integration/` to `RFC9110/` and correctly numbered, so that RFC 9110 §15.4 and §9.2 are visible in the compliance matrix.

**Acceptance Criteria:**
- [x] `Integration/RedirectHandlerTests.cs` → `RFC9110/01_RedirectHandlerTests.cs`
- [x] `Integration/RetryEvaluatorTests.cs` → `RFC9110/02_RetryEvaluatorTests.cs`
- [x] Namespace of both files: `namespace TurboHttp.Tests.RFC9110;`
- [x] All existing `DisplayName` values preserved (RFC tags, IDs, descriptions)
- [x] Source files in `Integration/` deleted
- [x] `dotnet test --filter "FullyQualifiedName~RFC9110"` returns all Redirect and Retry tests
- [x] Build: 0 errors

---

### TASK-RFC-002: Relocate RFC 9112 Tests — Connection Reuse & Per-Host Limits

**Description:** As a developer, I want `ConnectionReuseEvaluatorTests.cs` and `PerHostConnectionLimiterTests.cs` moved from `Integration/` to `RFC9112/`, so that RFC 9112 §9 (Connection Management) is fully contained in its RFC folder.

**Acceptance Criteria:**
- [x] `Integration/ConnectionReuseEvaluatorTests.cs` → `RFC9112/22_ConnectionReuseTests.cs`
- [x] `Integration/PerHostConnectionLimiterTests.cs` → `RFC9112/23_PerHostLimiterTests.cs`
- [x] Namespace of both files: `namespace TurboHttp.Tests.RFC9112;`
- [x] All existing `DisplayName` values preserved
- [x] Source files in `Integration/` deleted
- [x] `dotnet test --filter "FullyQualifiedName~RFC9112"` returns all connection management tests
- [x] Build: 0 errors

---

### TASK-RFC-003: Create RFC6265/ Folder and Relocate Cookie Tests

**Description:** As a developer, I want a new `RFC6265/` test folder with `CookieJarTests.cs` moved into it, so that RFC 6265 cookie tests are organised consistently with all other RFC folders.

**Acceptance Criteria:**
- [x] New folder `src/TurboHttp.Tests/RFC6265/` created
- [x] `Integration/CookieJarTests.cs` → `RFC6265/01_CookieJarTests.cs`
- [x] Namespace: `namespace TurboHttp.Tests.RFC6265;`
- [x] All existing `DisplayName` values preserved
- [x] Source file in `Integration/` deleted
- [x] `dotnet test --filter "FullyQualifiedName~RFC6265"` returns all cookie tests
- [x] Build: 0 errors

---

### TASK-RFC-004: Delete Non-RFC Integration Files and Remove Integration/ Folder

**Description:** As a developer, I want the 4 remaining files in `Integration/` that test no specific RFC behavior to be deleted, and the now-empty `Integration/` folder removed entirely, so the test project has no unmaintained catch-all folder.

**Files to delete:**

| File | Reason for deletion |
|------|---------------------|
| `Integration/CrossFeatureIntegrityTests.cs` | Tests cross-RFC interactions with no single RFC owner; covered by individual RFC tests |
| `Integration/HttpDecodeErrorMessagesTests.cs` | Tests error message strings; no RFC requirement governs specific error message wording |
| `Integration/Phase60ValidationGateTests.cs` | Validation gate artifact from a build phase; not ongoing RFC coverage |
| `Integration/TurboClientOptionsTests.cs` | Tests client configuration classes; no RFC governs `TurboClientOptions` |

**Acceptance Criteria:**
- [x] All 4 files deleted
- [x] `src/TurboHttp.Tests/Integration/` folder no longer exists
- [x] No other test file references these deleted types or helper methods
- [x] `dotnet test ./src/TurboHttp.sln` → 0 failures (deletions remove no essential coverage)
- [x] Build: 0 errors

---

### TASK-RFC-005: Rename Unnumbered RFC9113 Test Files

**Description:** As a developer, I want the 8 unnumbered RFC9113 files to receive `NN_` prefixes, so that test coverage per RFC section is immediately visible from the file list.

**Mapping (old → new):**

| Old File | New File | RFC Section |
|---|---|---|
| `Http2EncoderPseudoHeaderValidationTests.cs` | `22_EncoderPseudoHeaderTests.cs` | RFC 9113 §8.3.1 |
| `Http2EncoderSensitiveHeaderTests.cs` | `23_EncoderSensitiveHeaderTests.cs` | RFC 7541 §7.1.3 |
| `Http2FuzzHarnessTests.cs` | `24_FuzzHarnessTests.cs` | RFC 9113 §4.2/5.5/6.4 |
| `Http2MaxConcurrentStreamsTests.cs` | `25_SettingsMaxConcurrentTests.cs` | RFC 9113 §6.5.2 |
| `Http2ResourceExhaustionTests.cs` | `26_ResourceExhaustionTests.cs` | RFC 9113 §5.1/§6.9 |
| `Http2HighConcurrencyTests.cs` | `27_HighConcurrencyTests.cs` | RFC 9113 §5.1.2 |
| `Http2CrossComponentValidationTests.cs` | `28_CrossComponentValidationTests.cs` | RFC 9113 §4.3/§6.8 |
| `Http2SecurityTests.cs` | `29_SecurityTests.cs` | RFC 9113 §6.4/§6.10 |

> `Http2FrameTests.cs` stays **unnumbered** — it is a utility helper file, not an RFC-section-specific test file.

**Acceptance Criteria:**
- [ ] All 8 files renamed (old names no longer exist)
- [ ] Class names updated to match new file names (e.g. `Http2EncoderPseudoHeaderValidationTests` → `Http2EncoderPseudoHeaderTests`)
- [ ] Namespaces remain `namespace TurboHttp.Tests.RFC9113;`
- [ ] `dotnet test --filter "FullyQualifiedName~RFC9113"` returns all tests
- [ ] Build: 0 errors, 0 xunit1000 warnings

---

### TASK-RFC-006: Add RFC 9113 §5.1.1 Push-Promise State Machine Tests (MUST Gap)

**Description:** As a developer, I want dedicated tests for the Push-Promise stream state transition (RFC 9113 §5.1.1), so that the only remaining MUST gap in RFC 9113 is closed.

**Context:** RFC 9113 §5.1.1: a server may send `PUSH_PROMISE`, which moves the promised stream into the `reserved (remote)` state. The client can reject that stream with `RST_STREAM` (CANCEL) or receive it passively. Currently `PUSH_PROMISE` is parsed (covered by RT-2-013), but the state-machine transition `idle → reserved(remote)` has no dedicated test.

**Acceptance Criteria:**
- [ ] New file: `RFC9113/15_DecoderPushPromiseTests.cs` (`public sealed class Http2DecoderPushPromiseTests`)
- [ ] Namespace: `namespace TurboHttp.Tests.RFC9113;`
- [ ] Test: `PUSH_PROMISE` received → promised stream enters `reserved(remote)` state (`DisplayName: "RFC9113-5.1.1-PP-001: PUSH_PROMISE moves stream to reserved(remote) state"`)
- [ ] Test: client rejects `reserved(remote)` stream with `RST_STREAM` CANCEL
- [ ] Test: `PUSH_PROMISE` on stream ID 0 → `PROTOCOL_ERROR`
- [ ] Test: `PUSH_PROMISE` with an even promised-stream-ID is decodable
- [ ] Test: `PUSH_PROMISE` referencing an invalid/already-open stream → `PROTOCOL_ERROR`
- [ ] Minimum 5 tests with RFC tags (`"RFC9113-5.1.1-PP-XXX: ..."`)
- [ ] Build: 0 errors

---

### TASK-RFC-007: Add RFC 9113 §6.1/§6.2 PADDED Flag Tests (SHOULD Gaps)

**Description:** As a developer, I want explicit tests for `PADDED`-flag handling in `DATA` and `HEADERS` frames, so that the ⚠-marked SHOULD gaps are covered.

**Context:** RFC 9113 §6.1 (DATA): when `PADDED` is set, the frame contains a Pad Length byte followed by padding that must be stripped. RFC 9113 §6.2 (HEADERS): same padding pattern, plus optional priority data when `PRIORITY` is also set.

**Acceptance Criteria:**
- [ ] New tests added to `RFC9113/10_DecoderBasicFrameTests.cs`, or in a new file `16_DecoderPaddingTests.cs` if the existing file is too large
- [ ] Test: `DATA` frame with `PADDED` flag → padding stripped, payload correct
- [ ] Test: `DATA` frame with maximum pad length (254 bytes of padding)
- [ ] Test: `DATA` frame where Pad-Length > Frame-Length → `PROTOCOL_ERROR`
- [ ] Test: `HEADERS` frame with `PADDED` flag → padding stripped, header block correct
- [ ] Test: `HEADERS` frame with `PRIORITY` flag → priority fields skipped, headers correct
- [ ] Test: `HEADERS` frame with `PADDED` + `PRIORITY` combined
- [ ] All tests use RFC tags (`"RFC9113-6.1-PAD-001"`, `"RFC9113-6.2-PAD-001"`, etc.)
- [ ] Build: 0 errors

---

### TASK-RFC-008: Add StreamTests for CorrelationHttp1XStage and CorrelationHttp20Stage

**Description:** As a developer, I want dedicated stream tests for `CorrelationHttp1XStage` and `CorrelationHttp20Stage`, so that these correlation stages can be verified in isolation.

**Context:**
- `CorrelationHttp1XStage`: FIFO request-response matching (RFC 9112 §9.3 Pipelining)
- `CorrelationHttp20Stage`: stream-ID-based matching (RFC 9113 §5.1)

Both are currently covered only indirectly through pipeline tests.

**Acceptance Criteria:**
- [ ] New file: `src/TurboHttp.StreamTests/Http11/CorrelationHttp1XStageTests.cs`
  - [ ] Test: single request-response correlation
  - [ ] Test: multiple pipelined requests arrive and are matched in FIFO order
  - [ ] Test: response arriving before any request is registered is handled gracefully
  - [ ] Minimum 5 tests
- [ ] New file: `src/TurboHttp.StreamTests/Http20/CorrelationHttp20StageTests.cs`
  - [ ] Test: single stream-ID correlation
  - [ ] Test: multiple concurrent streams correlated by stream ID
  - [ ] Test: unknown stream-ID response does not crash the stage
  - [ ] Minimum 5 tests
- [ ] Both files extend `StreamTestBase`
- [ ] Build: 0 errors, `dotnet test` green

---

### TASK-RFC-009: Add StreamTests for StreamIdAllocatorStage

**Description:** As a developer, I want a dedicated stream-test file for `StreamIdAllocatorStage`, so that odd-ID allocation (RFC 9113 §5.1.1) is verifiable in isolation.

**Acceptance Criteria:**
- [ ] New file: `src/TurboHttp.StreamTests/Http20/StreamIdAllocatorStageTests.cs`
- [ ] Test: first allocated ID = 1
- [ ] Test: IDs are odd and strictly ascending (1, 3, 5, 7, …)
- [ ] Test: after N requests, next stream ID = 2N+1
- [ ] Test: overflow behaviour when max stream ID (2^31−1) is reached
- [ ] Minimum 4 tests, class extends `StreamTestBase`
- [ ] Build: 0 errors

---

### TASK-RFC-010: Create RFC_COVERAGE.md and Delete Old RFC Documents

**Description:** As a developer, I want a single `RFC_COVERAGE.md` that replaces both `RFC_COMPLIANCE.md` and `RFC_TEST_MATRIX.md` with accurate, post-Plan-11 data.

**Acceptance Criteria:**
- [ ] New file `RFC_COVERAGE.md` created in the repository root
- [ ] Contains: compliance summary table (per RFC: sections covered, coverage %, unit / stream / integration test counts — updated to post-Plan-11 numbers)
- [ ] Contains: compact gap table listing only gaps that remain open after Plan 11
- [ ] Contains: test structure convention description (`NN_<ThemaTests>.cs`, `DisplayName` format, exceptions list)
- [ ] Contains: folder → RFC section → test file mapping
- [ ] `RFC_COMPLIANCE.md` deleted
- [ ] `RFC_TEST_MATRIX.md` deleted
- [ ] `CLAUDE.md` "Test Organisation" section references `RFC_COVERAGE.md` instead of the old files

---

### TASK-RFC-011: Update CLAUDE.md

**Description:** As a developer, I want `CLAUDE.md` to accurately reflect the post-Plan-11 project state, with no stale limitations or outdated test-file counts.

**Context:** Per GAP_LIST.md GAP-008, 4 of 5 "Current Limitations" in `CLAUDE.md` are outdated. The test organisation also changes substantially from Plan 11.

**Acceptance Criteria:**
- [ ] "Current Limitations" section updated:
  - Stale entries removed (pipeline not wired, SendAsync not implemented, no business logic stages, etc.)
  - Real remaining limitations kept: no E2E integration tests against Kestrel, `PerHostConnectionLimiter` not wired in the actor pool
- [ ] "Test Organisation" section updated:
  - Table reflects post-Plan-11 state: RFC9110 (3 files), RFC9112 (23+ files), RFC9113 (29+ files), RFC6265 (1 file), Integration folder removed
  - Reference to `RFC_COVERAGE.md` as the central RFC reference
- [ ] No factual inaccuracies remain
- [ ] Build: 0 errors

---

## Functional Requirements

- **FR-1:** All RFC 9110 protocol handler tests live in `src/TurboHttp.Tests/RFC9110/` with `NN_` prefix
- **FR-2:** All RFC 9112 connection management tests live in `src/TurboHttp.Tests/RFC9112/`
- **FR-3:** RFC 6265 cookie tests live in `src/TurboHttp.Tests/RFC6265/`
- **FR-4:** The `src/TurboHttp.Tests/Integration/` folder no longer exists
- **FR-5:** All RFC 9113 files that test RFC-specific behaviour carry an `NN_` prefix (exception: `Http2FrameTests.cs` — utility file)
- **FR-6:** RFC 9113 §5.1.1 Push-Promise state machine has ≥5 dedicated MUST-level tests
- **FR-7:** RFC 9113 §6.1/§6.2 PADDED flag is covered by ≥6 dedicated tests
- **FR-8:** `CorrelationHttp1XStage` has its own StreamTest file with ≥5 tests
- **FR-9:** `CorrelationHttp20Stage` has its own StreamTest file with ≥5 tests
- **FR-10:** `StreamIdAllocatorStage` has its own StreamTest file with ≥4 tests
- **FR-11:** `RFC_COVERAGE.md` fully replaces both old RFC documents
- **FR-12:** `CLAUDE.md` contains no stale limitations

---

## Non-Goals

- No end-to-end integration tests against Kestrel (→ GAP-004, separate plan)
- No HTTP/3 tests (→ GAP-012)
- No new Akka stages or protocol implementations
- `Http11DecoderChunkExtensionTests.cs`, `Http11NegativePathTests.cs`, `Http11SecurityTests.cs` intentionally remain unnumbered (documented exceptions in CLAUDE.md)
- Root test utilities (`Http2FrameUtils.cs`, `Http2StageTestHelper.cs`, `Http2StreamLifecycleState.cs`) are kept as-is — they are actively used by RFC9113 tests

---

## Technical Considerations

- Run `dotnet test` **before and after** each file move or deletion to catch regressions immediately
- When deleting `CrossFeatureIntegrityTests.cs` — verify no unique coverage is lost; all constituent behaviors (redirect, cookies, decompression) are individually covered in their RFC folders
- When deleting `Phase60ValidationGateTests.cs` — verify it does not gate the CI pipeline in `.github/` or any build script before deleting
- Preserve all RFC `DisplayName` format when moving: `"RFC[section]-[cat]-[num]: description"` — e.g. `"RFC9110-15.4-RD-001: 301 redirect rewrites method to GET"`
- Stream tests extend `StreamTestBase` from `TurboHttp.StreamTests/`
- If `CorrelationHttp1XStage` / `CorrelationHttp20Stage` APIs are not directly testable in isolation, build as BidiFlow tests (same pattern as existing `Http11/` and `Http20/` stream tests)
- `RFC_COVERAGE.md` is hand-written Markdown — not generated

---

## Success Metrics

- `dotnet test ./src/TurboHttp.sln` → 0 failures after full plan implementation
- `src/TurboHttp.Tests/Integration/` folder does not exist
- `src/TurboHttp.Tests/RFC6265/` folder exists with ≥1 file
- `RFC9110/` contains exactly 3 files (01_Redirect, 02_Retry, 03_ContentEncoding)
- `RFC9112/` contains 23+ files (21 numbered + 22_ConnectionReuse + 23_PerHostLimiter + 3 preserved)
- `RFC9113/` all RFC-specific files are numbered (01–29+)
- `RFC_TEST_MATRIX.md` and `RFC_COMPLIANCE.md` no longer exist
- `CLAUDE.md` describes only real, current limitations

---

## Open Questions

- Does `Phase60ValidationGateTests.cs` gate any CI script outside the solution? → Check `.github/` workflows and any `Makefile`/`build.ps1` before deleting (addressed in TASK-RFC-004)
- `Http2FrameTests.cs` (RFC9113) — remains unnumbered as a utility file; revisit only if its content grows beyond helpers
- TASK-RFC-010 and TASK-RFC-011 (docs) can be worked on in parallel with the test tasks

---

## Recommended Execution Order

| Phase | Tasks | Notes |
|-------|-------|-------|
| **Phase 1: Relocate** | TASK-RFC-001, TASK-RFC-002, TASK-RFC-003 | Independent of each other; run `dotnet test` after each |
| **Phase 2: Delete** | TASK-RFC-004 | After Phase 1 so `Integration/` is truly empty before removal |
| **Phase 3: Rename** | TASK-RFC-005 | After Phase 1–2 so RFC9113 numbering is correct |
| **Phase 4: Fill Gaps** | TASK-RFC-006, TASK-RFC-007, TASK-RFC-008, TASK-RFC-009 | Can run in parallel |
| **Phase 5: Docs** | TASK-RFC-010, TASK-RFC-011 | After Phase 1–4 so all file counts are accurate |
