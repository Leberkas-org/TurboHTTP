<!-- maggus-id: a6bceb01-0381-45eb-b10d-860c1d00286b -->
# Feature 033: Obsidian Vault — Features Restructuring

## Introduction

The `Features/` folder contains 19 feature documentation files in a flat structure covering unrelated areas (protocol fixes, test suites, diagnostics, infrastructure). This feature reorganises Features/ into area-based subfolders for intuitive browsing and updates all wikilinks.

### Architecture Context

- **Components involved:** Obsidian vault (`notes/Features/`), vault index (`notes/00-Index.md`)
- **No code changes** — documentation-only restructuring

## Goals

- Organise 19 Feature files into 5 area-based subfolders
- Create `_INDEX.md` in each subfolder
- Fix all broken wikilinks across the vault

## Tasks

### TASK-033-001: Move Feature files into area subfolders
**Description:** As a developer browsing features, I want them grouped by area so I can find protocol features separate from test features separate from diagnostics.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-033-002, TASK-033-003
**Parallel:** yes — can run alongside Feature 032 tasks (different folders)

**Moves:**

| File | Target Subfolder |
|------|-----------------|
| `Feature003_Decompression_Stage.md` | `Protocol/` |
| `Feature004_HTTP10_Deadlock_Fix.md` | `Protocol/` |
| `Feature017_ConnectionStage_Race.md` | `Protocol/` |
| `Feature020_ContentEncoding_Consolidation.md` | `Protocol/` |
| `Feature005_H10_Flakiness_Mitigation.md` | `Testing/` |
| `Feature006_Connection_Management_Tests.md` | `Testing/` |
| `Feature007_Error_Handling_Tests.md` | `Testing/` |
| `Feature008_TLS_Integration_Tests.md` | `Testing/` |
| `Feature013_Security_Tests.md` | `Testing/` |
| `Feature014_Decoder_Fuzzing.md` | `Testing/` |
| `Feature015_H2_HPACK_Fuzzing.md` | `Testing/` |
| `Feature009_Akka_Logging_Bridge.md` | `Diagnostics/` |
| `Feature010_Tracing_Infrastructure.md` | `Diagnostics/` |
| `Feature011_OTel_Metrics.md` | `Diagnostics/` |
| `Feature012_Diagnostic_EventSource.md` | `Diagnostics/` |
| `Feature016_TracingBidi_Consolidation.md` | `Infrastructure/` |
| `Feature018_Docs_Site_Revision.md` | `Infrastructure/` |
| `Feature019_Stream_Survival.md` | `Infrastructure/` |
| `Feature024_Benchmark_Comparison.md` | `Performance/` |

**Acceptance Criteria:**
- [ ] 19 files moved via Obsidian MCP `move_note`
- [ ] 5 subfolders exist: `Protocol/`, `Testing/`, `Diagnostics/`, `Infrastructure/`, `Performance/`
- [ ] No files left in `Features/` root except `.gitkeep`

---

### TASK-033-002: Create subfolder index notes
**Description:** As a vault browser, I want each Features subfolder to have an `_INDEX.md` showing what's inside.

**Token Estimate:** ~15k tokens
**Predecessors:** TASK-033-001
**Successors:** TASK-033-003
**Parallel:** no — needs files in place first

**Acceptance Criteria:**
- [ ] `Features/Protocol/_INDEX.md` created with wikilinks to 4 files
- [ ] `Features/Testing/_INDEX.md` created with wikilinks to 7 files
- [ ] `Features/Diagnostics/_INDEX.md` created with wikilinks to 4 files
- [ ] `Features/Infrastructure/_INDEX.md` created with wikilinks to 3 files
- [ ] `Features/Performance/_INDEX.md` created with wikilinks to 1 file
- [ ] Each index has proper frontmatter per VAULT_STYLE_GUIDE

---

### TASK-033-003: Fix all broken wikilinks from Feature moves
**Description:** As a vault user, I want all Feature wikilinks to resolve after restructuring.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-033-001
**Successors:** none
**Parallel:** no — must verify moves completed

**Files to update:**
- `00-Index.md` — ~19 Feature links in the Features section
- Any Architecture notes that reference Features
- Any cross-references between Feature notes

**Acceptance Criteria:**
- [ ] `00-Index.md` updated with all new Feature paths
- [ ] All cross-references between Feature notes updated
- [ ] Grep for `[[Features/Feature` (old flat paths) returns 0 hits

## Task Dependency Graph

```
TASK-033-001 ──→ TASK-033-002
             ──→ TASK-033-003
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-033-001 | ~25k | none | yes (with F032 tasks) | — |
| TASK-033-002 | ~15k | 001 | no | — |
| TASK-033-003 | ~30k | 001 | no | — |

**Total estimated tokens:** ~70k

## Functional Requirements

- FR-1: All 19 Feature files moved to correct area subfolder
- FR-2: Each subfolder has `_INDEX.md` with frontmatter and wikilinks
- FR-3: Every wikilink to `Features/FeatureNNN_*` updated to `Features/Area/FeatureNNN_*`
- FR-4: Zero broken wikilinks after completion

## Non-Goals

- No content changes to Feature files (content cleanup is feature_034)
- No renaming of files
- No changes to Architecture/ or RFC/

## Technical Considerations

- Use Obsidian MCP `move_note` for all moves
- Use `patch_note` for wikilink updates
- Can run in parallel with Feature 032 since they touch different folder trees

## Success Metrics

- Zero broken wikilinks (verified by grep)
- All Feature notes reachable via subfolder `_INDEX.md`

## Open Questions

None — all resolved during planning.
