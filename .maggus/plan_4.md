# Plan: Test Cleanup v2 — Smaller Tasks, Reliable Execution

## Introduction

Revised plan (replaces plan_4). Same goals, but broken into significantly smaller, independent tasks (max ~100 methods / ~10 files per task). Each task can be completed by an agent in a single session without issues.

**Execution order**: Horizontal — all renames first, then all comments, then all summaries.

**Scope**:
- `src/TurboHttp.Tests/` — 88 files, ~1,521 tests (816 need renaming)
- `src/TurboHttp.StreamTests/` — 75 files, ~574 tests (all need renaming)

## Goals

- Consistent `Should_Action_When_Condition` naming across **all** test methods in both projects
- Consistent file names (`NN_` prefix, protocol prefix in StreamTests)
- Remove redundant comments (DisplayName duplicates, section headers)
- XML `<summary>` + `<remarks>` above every test class
- Delete integration test files from the unit test project
- Update documentation

---

## Phase 0: Preparation (completed)

### TASK-001: Naming Convention Reference
**Status:** Done

**Acceptance Criteria:**
- [x] `.maggus/naming-convention.md` created
- [x] `Should_Action_When_Condition` defined as standard
- [x] Edge cases documented

---

### TASK-002: Standardize File Names — TurboHttp.Tests
**Status:** Done

**Acceptance Criteria:**
- [x] All RFC files have `NN_` prefix
- [x] No numbering gaps
- [x] Build + tests green

---

### TASK-003: RFC1945 Method Renames
**Status:** Done (100% already correct)

**Acceptance Criteria:**
- [x] All ~232 methods in RFC1945 follow `Should_Action_When_Condition`

---

### TASK-004: StreamTests Folder Structure → RFC Folders
**Status:** Done

**Acceptance Criteria:**
- [x] All files moved to RFC folders
- [x] Namespaces updated
- [x] Old folders deleted

---

## Phase 1: Method Renames — TurboHttp.Tests (~816 remaining)

> **Parallelization**: TASK-005 through TASK-014 are independent and can run in parallel.

### TASK-005: Rename RFC1945 — Remaining (~17 methods, 18 files)

**Description:** Convert the remaining ~8% of RFC1945 methods to `Should_Action_When_Condition`.

**Scope:** `src/TurboHttp.Tests/RFC1945/` — only methods that do NOT yet start with `Should_`

**Acceptance Criteria:**
- [x] 100% of methods in RFC1945 follow `Should_Action_When_Condition`
- [x] `DisplayName` attributes unchanged
- [x] `dotnet test --filter "FullyQualifiedName~RFC1945"` — all green

---

### TASK-006: Rename RFC9112 Encoder (01–07, 7 files, ~0 remaining)

**Description:** Verify that all encoder tests in RFC9112 are already correctly named. Fix any stragglers if found.

**Scope:** `src/TurboHttp.Tests/RFC9112/01_*.cs` through `07_*.cs`

**Acceptance Criteria:**
- [x] 100% of methods follow `Should_Action_When_Condition`
- [x] `DisplayName` attributes unchanged
- [x] `dotnet test --filter "FullyQualifiedName~RFC9112"` — all green

---

### TASK-007: Rename RFC9112 Decoder (08–14, 7 files, ~40 methods)

**Description:** Convert decoder tests in RFC9112 to `Should_Action_When_Condition`.

**Scope:** `src/TurboHttp.Tests/RFC9112/08_*.cs` through `14_*.cs`

**Current pattern:** `Test_9112_StatusLine_200Ok()`, `Test_Missing_Path_Normalized()`
**New pattern:** `Should_Parse200Ok_When_StatusLineIsValid()`, `Should_NormalizePath_When_PathIsMissing()`

**Acceptance Criteria:**
- [x] All methods in 7 files follow `Should_Action_When_Condition`
- [x] `DisplayName` attributes unchanged
- [x] `dotnet test --filter "FullyQualifiedName~RFC9112"` — all green
- [x] No `Test_` prefix remaining

---

### TASK-008: Rename RFC9112 RoundTrip + Legacy (15–26, 12 files, ~70 methods)

**Description:** Rename round-trip and legacy tests in RFC9112.

**Scope:** `src/TurboHttp.Tests/RFC9112/15_*.cs` through `26_*.cs`

**Acceptance Criteria:**
- [x] All methods follow `Should_Action_When_Condition`
- [x] `DisplayName` attributes unchanged
- [x] `dotnet test --filter "FullyQualifiedName~RFC9112"` — all green

---

### TASK-009: Rename RFC9113 Frame Parsing (01–09, 9 files, ~85 methods)

**Description:** Rename frame parsing tests in RFC9113.

**Scope:** `src/TurboHttp.Tests/RFC9113/01_*.cs` through `09_*.cs`

**Current pattern:** `FrameHeader_ZeroBytes_ReturnsFalse()`, `FrameHeader_Exactly9Bytes_IsDecoded()`
**New pattern:** `Should_ReturnFalse_When_FrameHeaderHasZeroBytes()`, `Should_DecodeFrame_When_HeaderIsExactly9Bytes()`

**Acceptance Criteria:**
- [x] All methods in 9 files follow `Should_Action_When_Condition`
- [x] `DisplayName` attributes unchanged
- [x] `dotnet test --filter "FullyQualifiedName~RFC9113"` — all green

---

### TASK-010: Rename RFC9113 Decoder + Flow Control (10–15, 6 files, ~80 methods)

**Description:** Rename decoder and flow control tests in RFC9113.

**Scope:** `src/TurboHttp.Tests/RFC9113/10_*.cs` through `15_*.cs`

**Acceptance Criteria:**
- [x] All methods follow `Should_Action_When_Condition`
- [x] `DisplayName` attributes unchanged
- [x] `dotnet test --filter "FullyQualifiedName~RFC9113"` — all green

---

### TASK-011: Rename RFC9113 Encoder + Stress (16–27, 12 files, ~90 methods)

**Description:** Rename encoder, stress, and security tests in RFC9113.

**Scope:** `src/TurboHttp.Tests/RFC9113/16_*.cs` through `27_*.cs`

**Acceptance Criteria:**
- [x] All methods follow `Should_Action_When_Condition`
- [x] `DisplayName` attributes unchanged
- [x] `dotnet test --filter "FullyQualifiedName~RFC9113"` — all green

---

### TASK-012: Rename RFC7541 Part 1 (01–03, 3 files, ~100 methods)

**Description:** Rename Static Table, Dynamic Table, and HPACK tests.

**Scope:** `src/TurboHttp.Tests/RFC7541/01_*.cs` through `03_*.cs`

**Current pattern:** `DynamicTable_Empty_HasSizeZero()`, `StaticTable_Count_IsExactly61()`
**New pattern:** `Should_HaveSizeZero_When_DynamicTableIsEmpty()`, `Should_ContainExactly61Entries_When_QueryingStaticTable()`

**Acceptance Criteria:**
- [x] All methods in 3 files follow `Should_Action_When_Condition`
- [x] `DisplayName` attributes unchanged
- [x] `dotnet test --filter "FullyQualifiedName~RFC7541"` — all green

---

### TASK-013: Rename RFC7541 Part 2 (04–07, 4 files, ~115 methods)

**Description:** Rename Huffman, Header Block, Table Size, and Sensitive Header tests.

**Scope:** `src/TurboHttp.Tests/RFC7541/04_*.cs` through `07_*.cs`

**Acceptance Criteria:**
- [x] All methods in 4 files follow `Should_Action_When_Condition`
- [x] `DisplayName` attributes unchanged
- [x] `dotnet test --filter "FullyQualifiedName~RFC7541"` — all green

---

### TASK-014: Rename RFC9110 + RFC9111 + RFC6265 (10 files, ~100 methods)

**Description:** Remaining small RFC folders combined into one task.

**Scope:**
- `src/TurboHttp.Tests/RFC9110/` — 3 files, ~15 methods (86% already correct)
- `src/TurboHttp.Tests/RFC9111/` — 5 files, ~67 methods (0% correct)
- `src/TurboHttp.Tests/RFC6265/` — 2 files, ~49 methods (0% correct)

**Current pattern (RFC9111):** `NullInput_ReturnsNull()`, `MaxAge_FreshnessLifetime_60s()`
**New pattern:** `Should_ReturnNull_When_InputIsNull()`, `Should_CalculateFreshnessLifetimeAs60s_When_MaxAgeIs60()`

**Acceptance Criteria:**
- [x] 100% of methods in all 3 folders follow `Should_Action_When_Condition`
- [x] `DisplayName` attributes unchanged
- [x] ⚠️ BLOCKED: `dotnet test --filter "FullyQualifiedName~RFC9110"` — all green — RFC9110-15.4-RH-015 fails with pre-existing `file:///` URI resolution bug (was failing before this task)
- [x] `dotnet test --filter "FullyQualifiedName~RFC9111"` — all green
- [x] `dotnet test --filter "FullyQualifiedName~RFC6265"` — all green

---

## Phase 2: Method Renames — StreamTests (~574 remaining)

> **Parallelization**: TASK-015 through TASK-023 are independent and can run in parallel.
> **Prerequisite**: Phase 1 should be completed (naming convention is established).

### TASK-015: Rename StreamTests RFC1945 (7 files, ~36 methods)

**Scope:** `src/TurboHttp.StreamTests/RFC1945/`

**Acceptance Criteria:**
- [x] All methods follow `Should_Action_When_Condition`
- [x] `dotnet test src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj --filter "FullyQualifiedName~RFC1945"` — all green

---

### TASK-016: Rename StreamTests RFC9112 (12 files, ~89 methods)

**Scope:** `src/TurboHttp.StreamTests/RFC9112/`

**Acceptance Criteria:**
- [x] All methods follow `Should_Action_When_Condition`
- [x] `dotnet test src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj --filter "FullyQualifiedName~RFC9112"` — all green

---

### TASK-017: Rename StreamTests RFC9113 Part 1 — Connection Stages (10 files, ~80 methods)

**Scope:** `src/TurboHttp.StreamTests/RFC9113/` — only `Http20Connection*`, `Http20Correlation*`, `Http20Preface*` files

**Acceptance Criteria:**
- [x] All methods in the 10 files follow `Should_Action_When_Condition`
- [x] Tests compile and pass

---

### TASK-018: Rename StreamTests RFC9113 Part 2 — Encoder/Decoder/Stream (11 files, ~79 methods)

**Scope:** `src/TurboHttp.StreamTests/RFC9113/` — remaining files (`Http20Encoder*`, `Http20Decoder*`, `Http20Stream*`, `Http20Batch*`, `Http20Forbidden*`, `Http20Pseudo*`, `Http20StreamId*`, `Http20RequestToFrame*`)

**Acceptance Criteria:**
- [x] All methods follow `Should_Action_When_Condition`
- [x] `dotnet test src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj --filter "FullyQualifiedName~RFC9113"` — all green

---

### TASK-019: Rename StreamTests RFC7541 + RFC9110 + RFC9111 + RFC6265 (8 files, ~95 methods)

**Scope:**
- `src/TurboHttp.StreamTests/RFC7541/` — 1 file, ~8 methods
- `src/TurboHttp.StreamTests/RFC9110/` — 3 files, ~47 methods
- `src/TurboHttp.StreamTests/RFC9111/` — 2 files, ~28 methods
- `src/TurboHttp.StreamTests/RFC6265/` — 2 files, ~12 methods

**Acceptance Criteria:**
- [x] All methods follow `Should_Action_When_Condition`
- [x] All affected tests green

---

### TASK-020: Rename StreamTests Engine (7 files, ~52 methods)

**Scope:** `src/TurboHttp.StreamTests/Engine/`

**Acceptance Criteria:**
- [x] All methods follow `Should_Action_When_Condition`
- [x] `dotnet test src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj --filter "FullyQualifiedName~Engine"` — all green

---

### TASK-021: Rename StreamTests IO (8 files, ~45 methods)

**Scope:** `src/TurboHttp.StreamTests/IO/`

**Acceptance Criteria:**
- [ ] All methods follow `Should_Action_When_Condition`
- [ ] `dotnet test src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj --filter "FullyQualifiedName~IO"` — all green

---

### TASK-022: Rename StreamTests Pipeline (9 files, ~78 methods)

**Scope:** `src/TurboHttp.StreamTests/Pipeline/`

**Acceptance Criteria:**
- [ ] All methods follow `Should_Action_When_Condition`
- [ ] Tests green

---

### TASK-023: Rename StreamTests Stages (3 files, ~20 methods)

**Scope:** `src/TurboHttp.StreamTests/Stages/`

**Acceptance Criteria:**
- [ ] All methods follow `Should_Action_When_Condition`
- [ ] Tests green

---

## Phase 3: Remove Redundant Comments

> **Parallelization**: TASK-024 through TASK-027 can run in parallel.
> **Prerequisite**: Phase 1+2 completed (methods have final names).

**Rules (same for all tasks):**
- **Remove:** Comments that duplicate `DisplayName` or method name, section headers (`// ──`, `// ===`)
- **Keep:** Hex explanations (`// 0x82 = ...`), bit patterns, calculations, technical rationale, Akka-specific explanations

### TASK-024: Remove Comments — TurboHttp.Tests RFC1945 + RFC9112 (44 files)

**Scope:** `src/TurboHttp.Tests/RFC1945/` + `src/TurboHttp.Tests/RFC9112/`

**Acceptance Criteria:**
- [ ] No comments duplicating DisplayName/method name
- [ ] No section header comments
- [ ] Technical comments preserved
- [ ] Build + tests green

---

### TASK-025: Remove Comments — TurboHttp.Tests RFC9113 + RFC7541 (34 files)

**Scope:** `src/TurboHttp.Tests/RFC9113/` + `src/TurboHttp.Tests/RFC7541/`

**Acceptance Criteria:**
- [ ] Same rules as TASK-024
- [ ] Hex value explanations and HPACK bit pattern comments preserved
- [ ] Build + tests green

---

### TASK-026: Remove Comments — TurboHttp.Tests RFC9110 + RFC9111 + RFC6265 (10 files)

**Scope:** `src/TurboHttp.Tests/RFC9110/` + `src/TurboHttp.Tests/RFC9111/` + `src/TurboHttp.Tests/RFC6265/`

**Acceptance Criteria:**
- [ ] Same rules as TASK-024
- [ ] Build + tests green

---

### TASK-027: Remove Comments — StreamTests (all folders, 75 files)

**Scope:** All files in `src/TurboHttp.StreamTests/` (RFC folders + Engine + IO + Pipeline + Stages)

**Acceptance Criteria:**
- [ ] Same rules as TASK-024
- [ ] Akka-specific explanations preserved (Materializer, TestKit, Stage lifecycle)
- [ ] Build + tests green

---

## Phase 4: XML Summaries

> **Parallelization**: TASK-028 through TASK-033 can run in parallel.
> **Prerequisite**: Phase 3 completed (no redundant comments remaining).

**Format (same for all tasks):**
```csharp
/// <summary>
/// Tests [what is tested] per RFC [number] §[section].
/// [Second line: what is verified.]
/// </summary>
/// <remarks>
/// Class under test: <see cref="[SUT class]"/>.
/// RFC [number] §[section]: [short spec description].
/// </remarks>
```

### TASK-028: XML Summaries — TurboHttp.Tests RFC1945 + RFC6265 (20 files)

**Scope:** `src/TurboHttp.Tests/RFC1945/` (18) + `src/TurboHttp.Tests/RFC6265/` (2)

**Acceptance Criteria:**
- [ ] Every test class has `<summary>` + `<remarks>`
- [ ] `<see cref="..."/>` pointing to the class under test
- [ ] Valid XML — build without warnings
- [ ] Build green

---

### TASK-029: XML Summaries — TurboHttp.Tests RFC9112 (26 files)

**Scope:** `src/TurboHttp.Tests/RFC9112/`

**Acceptance Criteria:**
- [ ] Every test class has `<summary>` + `<remarks>`
- [ ] `<see cref="..."/>` pointing to the class under test
- [ ] Build green

---

### TASK-030: XML Summaries — TurboHttp.Tests RFC9113 + RFC7541 (34 files)

**Scope:** `src/TurboHttp.Tests/RFC9113/` (27) + `src/TurboHttp.Tests/RFC7541/` (7)

**Acceptance Criteria:**
- [ ] Every test class has `<summary>` + `<remarks>`
- [ ] `<see cref="..."/>` pointing to the class under test
- [ ] Build green

---

### TASK-031: XML Summaries — TurboHttp.Tests RFC9110 + RFC9111 (8 files)

**Scope:** `src/TurboHttp.Tests/RFC9110/` (3) + `src/TurboHttp.Tests/RFC9111/` (5)

**Acceptance Criteria:**
- [ ] Every test class has `<summary>` + `<remarks>`
- [ ] Build green

---

### TASK-032: XML Summaries — StreamTests RFC Folders (45 files)

**Scope:** `src/TurboHttp.StreamTests/RFC1945/` + `RFC9112/` + `RFC9113/` + `RFC7541/` + `RFC9110/` + `RFC9111/` + `RFC6265/`

**Acceptance Criteria:**
- [ ] Every test class has `<summary>` + `<remarks>`
- [ ] `<see cref="..."/>` pointing to the stage/class under test
- [ ] Build green

---

### TASK-033: XML Summaries — StreamTests Infrastructure (27 files)

**Scope:** `src/TurboHttp.StreamTests/Engine/` + `IO/` + `Pipeline/` + `Stages/`

**Acceptance Criteria:**
- [ ] Every test class has `<summary>` + `<remarks>`
- [ ] Build green

---

## Phase 5: Cleanup & Documentation

> **Prerequisite**: Phase 1–4 completed.

### TASK-034: Standardize File Names — StreamTests

**Description:** Apply consistent protocol prefixes to all StreamTests files.

**Renames:**
| File | Problem | New Name |
|------|---------|----------|
| `PrependPrefaceStageTests.cs` | Missing `Http20` prefix | `Http20PrependPrefaceStageTests.cs` |
| `Request2FrameStageTests.cs` | `2` instead of `To`, missing prefix | `Http20RequestToFrameStageTests.cs` |
| `StreamIdAllocatorStageTests.cs` | Missing `Http20` prefix | `Http20StreamIdAllocatorStageTests.cs` |
| `Http1XCorrelationStageTests.cs` | `Http1X` inconsistent | `Http11CorrelationStageTests.cs` |

**Acceptance Criteria:**
- [ ] All HTTP/1.0 stage tests start with `Http10`
- [ ] All HTTP/1.1 stage tests start with `Http11`
- [ ] All HTTP/2 stage tests start with `Http20`
- [ ] Class names inside files updated to match new file names
- [ ] Build + tests green

---

### TASK-035: Delete Integration Tests from Unit Test Project

**Description:** Remove pseudo-integration tests that don't belong in the unit test project.

**Files to delete:**
- `src/TurboHttp.Tests/RFC1945/05_EncoderIntegrationTests.cs`
- `src/TurboHttp.Tests/RFC9110/03_ContentEncodingIntegrationTests.cs`
- `src/TurboHttp.Tests/RFC9111/05_CacheIntegrationTests.cs`

**Acceptance Criteria:**
- [ ] All 3 files deleted
- [ ] Build green
- [ ] Remaining tests all green
- [ ] Numbering gaps closed by renaming if needed

---

### TASK-036: Update Documentation

**Description:** Update CLAUDE.md and RFC_COVERAGE.md to reflect the new state.

**Acceptance Criteria:**
- [ ] `CLAUDE.md` — Test table: file counts corrected, StreamTests structure documented, naming convention mentioned
- [ ] `RFC_COVERAGE.md` — test counts corrected
- [ ] Build green

---

## Functional Requirements

- FR-1: Every test method in both projects follows `Should_[Action]_When_[Condition]`
- FR-2: `DisplayName` attributes are **NEVER** changed
- FR-3: Comments are only removed when `DisplayName` or method name already carries the same information
- FR-4: Hex explanations, bit patterns, and calculations are **always** preserved
- FR-5: Every test class has `/// <summary>` + `/// <remarks>` with `<see cref="..."/>` to the SUT class
- FR-6: After each TASK, all tests in the affected area must be green
- FR-7: No production code is changed (`src/TurboHttp/`)
- FR-8: No `DisplayName` attributes are changed
- FR-9: No test logic or assertions are changed

## Non-Goals

- No changes to production code (`src/TurboHttp/`)
- No new tests — only rename/move/cleanup of existing ones
- No changes to the `TurboHttp.IntegrationTests` project
- No changes to test logic or assertions
- No restructuring of unit test folders in `TurboHttp.Tests/` (RFC folders stay as-is)

## Technical Considerations

- **Namespace changes**: Only needed in Phase 5 (TASK-034) for file renames
- **Git strategy**: One commit per TASK for clean history
- **csproj**: No changes needed — SDK-style projects auto-include all `.cs` files
- **Execution order**: Phase 1 → Phase 2 → Phase 3 → Phase 4 → Phase 5 (within each phase, tasks can run in parallel)

## Parallelization Matrix

| Phase | Parallel Tasks | Prerequisite |
|-------|---------------|--------------|
| Phase 1 | TASK-005 through TASK-014 (all 10 in parallel) | Naming convention exists |
| Phase 2 | TASK-015 through TASK-023 (all 9 in parallel) | Phase 1 completed |
| Phase 3 | TASK-024 through TASK-027 (all 4 in parallel) | Phase 2 completed |
| Phase 4 | TASK-028 through TASK-033 (all 6 in parallel) | Phase 3 completed |
| Phase 5 | TASK-034, TASK-035 in parallel; TASK-036 after | Phase 4 completed |

## Success Metrics

- 0 test methods with old naming patterns
- 0 redundant comments duplicating DisplayName/method name
- 100% of test classes with XML `<summary>` + `<remarks>`
- 0 integration test files in the unit test project
- All ~2,100 tests green after completion
- No task has more than ~100 methods to process
