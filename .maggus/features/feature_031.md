<!-- maggus-id: d7321f45-84cb-48a8-97ae-2f0ba2ae6c12 -->

# Feature 031: Comment & Summary Cleanup

## Introduction

Systematic cleanup of all comments and XML documentation summaries across the TurboHttp codebase. Removes TASK-XXX references from inline comments, strips decorative section dividers, removes #region directives, and verifies XML summaries are current and accurate.

### Architecture Context

- **Components involved:** All layers — Protocol (RFC7541, RFC9110, RFC9112, RFC9114, RFC9204), Streams (Stages/Features), Transport tests, Diagnostics, Hosting tests, StreamTests
- **No functional changes:** Pure comment/documentation cleanup — no code logic is modified
- **No new components:** This is a housekeeping feature

## Goals

- Remove all TASK-XXX references from inline comments and XML docs (12 occurrences across 6 files)
- Remove all decorative section divider comments (~70+ lines across 25+ files)
- Remove all #region/#endregion directives (37 pairs across 6 files)
- Preserve all RFC compliance comments, NOTE comments, and behavioral explanations
- Zero compilation errors, zero test regressions

## Tasks

### TASK-031-001: Remove TASK references from production code

**Description:** As a developer, I want TASK-XXX references removed from production source code so that comments describe behavior, not internal tracking IDs.

**Token Estimate:** ~15k tokens
**Predecessors:** none
**Successors:** TASK-031-005
**Parallel:** yes — can run alongside TASK-031-002, TASK-031-003, TASK-031-004

**Files:**
- Modify: `src/TurboHttp/Streams/Stages/Features/ConnectionReuseStage.cs`

**Changes:**
- Line 63: `// unconditionally for H10 (Protocol-Flag, TASK-001-001) because HTTP/1.0` → remove `TASK-001-001` reference, keep descriptive text
- Line 239: `// protocol-flag detection (TASK-001-001).` → remove `TASK-001-001` reference, keep descriptive text

**Acceptance Criteria:**
- [ ] No TASK-XXX references remain in `ConnectionReuseStage.cs`
- [ ] Comments still describe the HTTP/1.0 protocol behavior clearly
- [ ] `dotnet build --configuration Release ./src/TurboHttp.sln` passes
- [ ] `dotnet test ./src/TurboHttp.sln` passes

---

### TASK-031-002: Remove TASK references from StreamTests (batch 1)

**Description:** As a developer, I want TASK-XXX references removed from test comments so that test documentation is self-contained and descriptive.

**Token Estimate:** ~20k tokens
**Predecessors:** none
**Successors:** TASK-031-005
**Parallel:** yes — can run alongside TASK-031-001, TASK-031-003, TASK-031-004

**Files:**
- Modify: `src/TurboHttp.StreamTests/Streams/02_TaskFixVerificationTests.cs`
- Modify: `src/TurboHttp.StreamTests/Streams/05_ConnectionStageTests.cs`

**Changes in `02_TaskFixVerificationTests.cs`:**
- Line 671: `// Version preserved (TASK-002)` → `// Version preserved in redirect`
- Line 729: `// Cookie stored (TASK-003)` → `// Cookie stored by CookieBidiStage`
- Line 732: `// Response cached (TASK-008)` → `// Response cached by CacheBidiStage`

**Changes in `05_ConnectionStageTests.cs`:**
- Line 267: `// Inbound channel completion now emits a CloseSignalItem (TASK-007-004).` → `// Inbound channel completion now emits a CloseSignalItem.`
- Line 485: same as above (duplicate comment)

**Acceptance Criteria:**
- [ ] No TASK-XXX references remain in either file (outside of DisplayName attributes)
- [ ] Replacement comments are descriptive and accurate
- [ ] `dotnet build --configuration Release ./src/TurboHttp.sln` passes
- [ ] `dotnet test ./src/TurboHttp.sln` passes

---

### TASK-031-003: Remove TASK references from StreamTests (batch 2)

**Description:** As a developer, I want TASK-XXX references removed from remaining stream test files.

**Token Estimate:** ~20k tokens
**Predecessors:** none
**Successors:** TASK-031-005
**Parallel:** yes — can run alongside TASK-031-001, TASK-031-002, TASK-031-004

**Files:**
- Modify: `src/TurboHttp.StreamTests/Streams/16_EngineBidiFlowCompositionTests.cs`
- Modify: `src/TurboHttp.StreamTests/Streams/23_ConnectionStageReconnectionRaceTests.cs`

**Changes in `16_EngineBidiFlowCompositionTests.cs`:**
- Line 337: `// Protocol-level decoders no longer decompress (TASK-020-001/002/003), so raw compressed` → `// Protocol-level decoders no longer decompress, so raw compressed`

**Changes in `23_ConnectionStageReconnectionRaceTests.cs`:**
- Line 15: XML summary — replace `(TASK-026)` with descriptive text about the reconnection race condition
- Line 24: XML remarks — replace `TASK-026-001/002/003` with `the generation guard implementation`
- Line 159: Comment — replace `Without TASK-026 fixes,` with `Without the generation guard,`

**Acceptance Criteria:**
- [ ] No TASK-XXX references remain in either file
- [ ] XML documentation accurately describes the reconnection race condition fix
- [ ] `dotnet build --configuration Release ./src/TurboHttp.sln` passes
- [ ] `dotnet test ./src/TurboHttp.sln` passes

---

### TASK-031-004: Remove TASK reference from RFC9114 tests

**Description:** As a developer, I want the last TASK reference removed from the RFC test suite.

**Token Estimate:** ~10k tokens
**Predecessors:** none
**Successors:** TASK-031-005
**Parallel:** yes — can run alongside TASK-031-001, TASK-031-002, TASK-031-003

**Files:**
- Modify: `src/TurboHttp.Tests/RFC9114/32_QuicMultiStreamTests.cs`

**Changes:**
- Line 7: `/// Tests for TASK-007-001: QuicClientProvider reentrant multi-stream support` → `/// Tests for QuicClientProvider reentrant multi-stream support`

**Acceptance Criteria:**
- [ ] No TASK-XXX references remain in `32_QuicMultiStreamTests.cs`
- [ ] `dotnet build --configuration Release ./src/TurboHttp.sln` passes
- [ ] `dotnet test ./src/TurboHttp.sln` passes

---

### TASK-031-005: Verify zero TASK references remain

**Description:** As a developer, I want to confirm that no TASK-XXX references remain in any comments across the entire codebase (DisplayName attributes excluded).

**Token Estimate:** ~10k tokens
**Predecessors:** TASK-031-001, TASK-031-002, TASK-031-003, TASK-031-004
**Successors:** TASK-031-006
**Parallel:** no

**Steps:**
1. Run: `grep -rn "TASK-[0-9]" src/ --include="*.cs" | grep -v "DisplayName"` — expect zero results
2. If any remain, fix them following the same pattern (replace with descriptive text)

**Acceptance Criteria:**
- [ ] Grep returns zero matches for TASK-XXX outside DisplayName attributes
- [ ] Full build + test passes

---

### TASK-031-006: Remove #region directives from production code

**Description:** As a developer, I want #region directives removed from production code so that code structure relies on natural grouping rather than IDE folding.

**Token Estimate:** ~15k tokens
**Predecessors:** TASK-031-005
**Successors:** TASK-031-008
**Parallel:** yes — can run alongside TASK-031-007

**Files:**
- Modify: `src/TurboHttp/Diagnostics/TurboHttpEventSource.cs` (6 region pairs)

**Changes:**
- Remove all `#region` and `#endregion` lines
- Do NOT add replacement comments — the method/event names are self-documenting

**Acceptance Criteria:**
- [ ] No #region/#endregion directives remain in `TurboHttpEventSource.cs`
- [ ] `dotnet build --configuration Release ./src/TurboHttp.sln` passes
- [ ] `dotnet test ./src/TurboHttp.sln` passes

---

### TASK-031-007: Remove #region directives from test files

**Description:** As a developer, I want #region directives removed from all test files.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-031-005
**Successors:** TASK-031-008
**Parallel:** yes — can run alongside TASK-031-006

**Files:**
- Modify: `src/TurboHttp.Tests/Transport/ConnectionPoolTests.cs` (11 regions)
- Modify: `src/TurboHttp.Tests/Transport/ConnectionLeaseTests.cs` (8 regions)
- Modify: `src/TurboHttp.Tests/Transport/DirectConnectionFactoryTests.cs` (5 regions)
- Modify: `src/TurboHttp.Tests/Transport/ConnectionPoolDeadlockTests.cs` (5 regions)
- Modify: `src/TurboHttp.StreamTests/RFC9204/QpackStreamStageTests.cs` (2 regions)

**Changes:**
- Remove all `#region` and `#endregion` lines from all 5 files

**Acceptance Criteria:**
- [ ] No #region/#endregion directives remain in any of the 5 files
- [ ] `dotnet build --configuration Release ./src/TurboHttp.sln` passes
- [ ] `dotnet test ./src/TurboHttp.sln` passes

---

### TASK-031-008: Verify zero #region directives remain

**Description:** As a developer, I want to confirm all #region directives are gone.

**Token Estimate:** ~10k tokens
**Predecessors:** TASK-031-006, TASK-031-007
**Successors:** TASK-031-009
**Parallel:** no

**Steps:**
1. Run: `grep -rn "#region\|#endregion" src/ --include="*.cs"` — expect zero results
2. If any remain, remove them

**Acceptance Criteria:**
- [ ] Grep returns zero matches for #region/#endregion
- [ ] Full build + test passes

---

### TASK-031-009: Remove decorative dividers from Protocol layer (RFC7541)

**Description:** As a developer, I want decorative divider comments removed from the HPACK encoder/decoder so that code is clean and readable without visual noise.

**Token Estimate:** ~20k tokens
**Predecessors:** TASK-031-008
**Successors:** TASK-031-012
**Parallel:** yes — can run alongside TASK-031-010, TASK-031-011

**Files:**
- Modify: `src/TurboHttp/Protocol/RFC7541/HpackEncoder.cs` (~10 dividers)
- Modify: `src/TurboHttp/Protocol/RFC7541/HpackDecoder.cs` (~2 dividers)

**What to remove:**
- Lines consisting only of `// ----...`, `// ====...`, or `// ── Section Name ──...`
- Do NOT remove comments that contain actual information (e.g., RFC references, behavioral explanations)

**Acceptance Criteria:**
- [ ] No decorative-only divider lines remain in either file
- [ ] All RFC compliance comments are preserved
- [ ] `dotnet build --configuration Release ./src/TurboHttp.sln` passes
- [ ] `dotnet test ./src/TurboHttp.sln` passes

---

### TASK-031-010: Remove decorative dividers from Protocol layer (RFC9110/9112)

**Description:** As a developer, I want decorative divider comments removed from the HTTP/1.1 encoder, decoder, and redirect handler.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-031-008
**Successors:** TASK-031-012
**Parallel:** yes — can run alongside TASK-031-009, TASK-031-011
**Model:** sonnet

**Files:**
- Modify: `src/TurboHttp/Protocol/RFC9110/RedirectHandler.cs`
- Modify: `src/TurboHttp/Protocol/RFC9112/Http11Decoder.cs` (~13 dividers)
- Modify: `src/TurboHttp/Protocol/RFC9112/Http11Encoder.cs`

**What to remove:**
- Lines consisting only of `// ----...`, `// ====...`, or `// ── Section Name ──...`
- Preserve all RFC section references and behavioral comments

**Acceptance Criteria:**
- [ ] No decorative-only divider lines remain in any of the 3 files
- [ ] All RFC compliance comments are preserved
- [ ] `dotnet build --configuration Release ./src/TurboHttp.sln` passes
- [ ] `dotnet test ./src/TurboHttp.sln` passes

---

### TASK-031-011: Remove decorative dividers from Protocol layer (RFC9114/9204 + core)

**Description:** As a developer, I want decorative dividers removed from the remaining protocol files and core stream components.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-031-008
**Successors:** TASK-031-012
**Parallel:** yes — can run alongside TASK-031-009, TASK-031-010

**Files:**
- Modify: `src/TurboHttp/Protocol/HttpDecoderError.cs`
- Modify: `src/TurboHttp/Protocol/RFC9204/QpackDecoder.cs`
- Modify: `src/TurboHttp/Protocol/RFC9204/QpackEncoder.cs`
- Modify: `src/TurboHttp/Streams/ClientStreamOwner.cs`

**What to remove:**
- Lines consisting only of `// ----...`, `// ====...`, or `// ── Section Name ──...`

**Acceptance Criteria:**
- [ ] No decorative-only divider lines remain in any of the 4 files
- [ ] `dotnet build --configuration Release ./src/TurboHttp.sln` passes
- [ ] `dotnet test ./src/TurboHttp.sln` passes

---

### TASK-031-012: Remove decorative dividers from test files

**Description:** As a developer, I want decorative dividers removed from all test files.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-031-009, TASK-031-010, TASK-031-011
**Successors:** TASK-031-013
**Parallel:** no
**Model:** sonnet

**Files:**
- Modify: `src/TurboHttp.Tests/Hosting/TurboHttpClientBuilderHandlerTests.cs` (~14 dividers)
- Modify: `src/TurboHttp.Tests/Hosting/NamedClientIsolationTests.cs` (~8 dividers)
- Modify: `src/TurboHttp.StreamTests/Streams/01_StageOrderingTests.cs`
- Modify: `src/TurboHttp.StreamTests/Streams/17_StreamSurvivalTests.cs` (~18 dividers)
- Modify: `src/TurboHttp.StreamTests/Streams/20_HandlerBidiStageTests.cs`
- Modify: `src/TurboHttp.StreamTests/RFC9110/05_RetryBidiStageTests.cs`
- Modify: `src/TurboHttp.StreamTests/RFC9110/06_RedirectBidiStageTests.cs`
- Modify: `src/TurboHttp.StreamTests/RFC9111/03_CacheBidiStageTests.cs`

**What to remove:**
- Lines consisting only of `// ----...`, `// ====...`, or `// ── Section Name ──...`

**Acceptance Criteria:**
- [ ] No decorative-only divider lines remain in any of the 8 files
- [ ] `dotnet build --configuration Release ./src/TurboHttp.sln` passes
- [ ] `dotnet test ./src/TurboHttp.sln` passes

---

### TASK-031-013: Final verification sweep

**Description:** As a developer, I want a final sweep confirming that all cleanup targets have been addressed and the codebase builds and tests clean.

**Token Estimate:** ~15k tokens
**Predecessors:** TASK-031-012
**Successors:** none
**Parallel:** no

**Steps:**
1. Run: `grep -rn "TASK-[0-9]" src/ --include="*.cs" | grep -v "DisplayName"` — expect zero results
2. Run: `grep -rn "#region\|#endregion" src/ --include="*.cs"` — expect zero results
3. Run: `grep -Prn "^\s*//\s*[-=─]{10,}" src/ --include="*.cs"` — expect zero results (decorative dividers)
4. Run: `dotnet build --configuration Release ./src/TurboHttp.sln`
5. Run: `dotnet test ./src/TurboHttp.sln`

**Acceptance Criteria:**
- [ ] All three grep searches return zero results
- [ ] Full build passes with zero warnings
- [ ] All tests pass

---

## Task Dependency Graph

```
TASK-031-001 ──┐
TASK-031-002 ──┤
TASK-031-003 ──┼──→ TASK-031-005 ──┬──→ TASK-031-006 ──┐
TASK-031-004 ──┘                   │                    │
                                   └──→ TASK-031-007 ──┼──→ TASK-031-008 ──┬──→ TASK-031-009 ──┐
                                                        │                   ├──→ TASK-031-010 ──┼──→ TASK-031-012 ──→ TASK-031-013
                                                        │                   └──→ TASK-031-011 ──┘
                                                        │
                                                        └──────────────────────────────────────────────────────────┘
```

| Task | Title | Estimate | Predecessors | Parallel | Model |
|------|-------|----------|--------------|----------|-------|
| TASK-031-001 | TASK refs: production code | ~15k | none | yes (with 002-004) | — |
| TASK-031-002 | TASK refs: StreamTests batch 1 | ~20k | none | yes (with 001,003,004) | — |
| TASK-031-003 | TASK refs: StreamTests batch 2 | ~20k | none | yes (with 001,002,004) | — |
| TASK-031-004 | TASK refs: RFC9114 tests | ~10k | none | yes (with 001-003) | — |
| TASK-031-005 | Verify zero TASK refs | ~10k | 001-004 | no | — |
| TASK-031-006 | #region: production code | ~15k | 005 | yes (with 007) | — |
| TASK-031-007 | #region: test files | ~25k | 005 | yes (with 006) | — |
| TASK-031-008 | Verify zero #region | ~10k | 006, 007 | no | — |
| TASK-031-009 | Dividers: RFC7541 | ~20k | 008 | yes (with 010, 011) | — |
| TASK-031-010 | Dividers: RFC9110/9112 | ~30k | 008 | yes (with 009, 011) | sonnet |
| TASK-031-011 | Dividers: RFC9114/9204+core | ~25k | 008 | yes (with 009, 010) | — |
| TASK-031-012 | Dividers: test files | ~30k | 009-011 | no | sonnet |
| TASK-031-013 | Final verification sweep | ~15k | 012 | no | — |

**Total estimated tokens:** ~265k

## Functional Requirements

- FR-1: All TASK-XXX references in comments and XML docs must be replaced with descriptive text or removed entirely
- FR-2: TASK-XXX references inside `[Fact(DisplayName = "...")]` and `[Theory(DisplayName = "...")]` attributes must NOT be touched
- FR-3: All `#region` and `#endregion` directives must be removed
- FR-4: All decorative divider lines (lines consisting only of `//` followed by dashes, equals signs, or box-drawing characters) must be removed
- FR-5: All RFC compliance comments (e.g., "RFC 9112 §5.2") must be preserved
- FR-6: All NOTE comments explaining non-obvious behavior must be preserved
- FR-7: No functional code changes — only comment/directive removal and text replacement
- FR-8: Build must pass with `TreatWarningsAsErrors` enabled
- FR-9: All existing tests must continue to pass

## Non-Goals

- Adding new XML documentation where none exists
- Fixing "Gets or sets" anti-patterns in XML docs (cosmetic, low priority)
- Reformatting or restructuring code
- Renaming test files or classes
- Modifying any DisplayName attributes

## Technical Considerations

- `TreatWarningsAsErrors` is enabled globally in `Directory.Build.props` — any XML doc warning from removal could break the build
- Some divider comments may include section labels (e.g., `// ── Private Helpers ──`) — remove the entire line, do not convert to a plain comment
- Test files may have dividers between test methods that serve as visual separators — remove them, the test method names and attributes provide sufficient structure

## Success Metrics

- Zero TASK-XXX references in comments (grep verification)
- Zero #region directives (grep verification)
- Zero decorative divider lines (grep verification)
- Full build + all tests green

## Open Questions

*None — scope is well-defined and concrete.*
