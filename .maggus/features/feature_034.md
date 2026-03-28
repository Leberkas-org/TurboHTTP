<!-- maggus-id: 03d3c5ef-12a0-4d11-bf18-a25dc8c329ba -->
# Feature 034: Obsidian Vault — Content Cleanup & RFC Navigation

## Introduction

Three independent cleanup tasks: (1) Remove all TASK-NNN and .maggus references from vault documentation, (2) Add navigation footers to all ~400 RFC section files so they link back to their parent index, (3) Fix outdated QPACK data in the RFC Status Matrix. These tasks have no dependencies on each other or on Features 032/033.

### Architecture Context

- **Components involved:** All `notes/Features/` files, `notes/Architecture/` analysis files, `notes/Templates/`, all `notes/RFC/*/sections/` files, `notes/RFC/00-RFC_STATUS_MATRIX.md`
- **No code changes** — documentation-only cleanup

## Goals

- Remove all TASK-NNN identifiers and .maggus path references from vault notes
- Connect all ~400 RFC section files to their parent index via navigation footers
- Correct outdated QPACK compliance data in the Status Matrix

## Tasks

### TASK-034-001: Remove TASK/maggus references from Architecture notes
**Description:** As a documentation reader, I want Architecture notes free of internal tooling references (TASK-NNN, .maggus paths) so the documentation reads cleanly.

**Token Estimate:** ~30k tokens
**Predecessors:** none
**Successors:** none
**Parallel:** yes — can run alongside TASK-034-002, 003, 004, 005

**Files to patch:**
- `Architecture/00-ONBOARDING.md` — 4 occurrences (`.maggus` folder mention, maggus-plan skill, TASK format ref)
- `Architecture/04-CURRENT_STATE_SUMMARY.md` — 2 occurrences (`.maggus/features/` references)
- `Architecture/07-HTTP10_RECONNECTION_LIMITATION.md` — 1 occurrence ("TASK-021-003")
- `Architecture/10-DEADLOCK_ANALYSIS.md` — 11 occurrences (replace "Fixed (TASK-NNN)" with "Fixed")
- `Architecture/11-STAGE_COMPLETION_AUDIT.md` — 21 occurrences (replace "TASK-030-NNN" refs with "Fixed")

**Note:** File paths may have changed if Feature 032 ran first. Use current paths.

**Acceptance Criteria:**
- [ ] All `TASK-` references replaced with descriptive text
- [ ] All `.maggus` path references removed
- [ ] Grep for `TASK-` in Architecture/ returns 0 hits
- [ ] Grep for `.maggus` in Architecture/ returns 0 hits
- [ ] Content still reads naturally after replacements

---

### TASK-034-002: Remove TASK/maggus references from Feature notes
**Description:** As a documentation reader, I want Feature notes free of internal tooling references.

**Token Estimate:** ~35k tokens
**Predecessors:** none
**Successors:** none
**Parallel:** yes — can run alongside TASK-034-001, 003, 004, 005

**Files to patch (all 19 Feature files):**
- Remove `| **Maggus Plan** | ... |` row from Quick Reference table in each file
- Remove `| **Scope** | N tasks (TASK-NNN-001 through ...) |` row or rephrase without TASK IDs
- Replace `**TASK-NNN-XXX**: Description` with `**Step X**: Description` or descriptive bullets
- Replace task ID references in tables (e.g., `| TASK-020-001 | ... |`) with step numbers

**Note:** File paths may have changed if Feature 033 ran first. Use current paths.

**Acceptance Criteria:**
- [ ] All 19 Feature files patched
- [ ] No `Maggus Plan` rows remain in any Quick Reference table
- [ ] No `TASK-` identifiers remain in any Feature file
- [ ] No `.maggus` paths remain
- [ ] Content still reads naturally — steps described with plain language

---

### TASK-034-003: Remove TASK/maggus references from Templates
**Description:** As a template user, I want templates free of .maggus references.

**Token Estimate:** ~10k tokens
**Predecessors:** none
**Successors:** none
**Parallel:** yes — can run alongside all other tasks
**Model:** haiku

**Files to patch:**
- `Templates/RFC-Note.md` — remove `[[../../.maggus/features/feature_XXX.md|Feature Plan]]` link
- `Templates/Bug-Investigation.md` — remove `.maggus/features/` and `.maggus/analysis/` links
- `VAULT_STYLE_GUIDE.md` — remove `[Linked from .maggus/features/]` from tree diagram

**Acceptance Criteria:**
- [ ] All 3 files patched
- [ ] Grep for `.maggus` in Templates/ and VAULT_STYLE_GUIDE returns 0 hits

---

### TASK-034-004: Add navigation footers to RFC section files
**Description:** As a vault user navigating RFC sections, I want each section file to link back to its parent RFC index and the Status Matrix so I can navigate without going to the file tree.

**Token Estimate:** ~60k tokens (split across parallel agents)
**Predecessors:** none
**Successors:** none
**Parallel:** yes — split into 10 parallel haiku agents, one per RFC folder

**Footer format:**
```markdown

---
**Navigation:** [[../RFCXXXX|RFC XXXX]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
```

Where `XXXX` is the RFC number of the parent folder.

**Agent split:**

| Agent | RFC Folder | Section Files |
|-------|-----------|---------------|
| Agent 1 | RFC1945/sections/ | 36 files |
| Agent 2 | RFC6265/sections/ | 24 files |
| Agent 3 | RFC7541/sections/ | 12 files |
| Agent 4 | RFC9000/sections/ | 91 files |
| Agent 5 | RFC9110/sections/ | 112 files |
| Agent 6 | RFC9111/sections/ | 20 files |
| Agent 7 | RFC9112/sections/ | 25 files |
| Agent 8 | RFC9113/sections/ | 35 files |
| Agent 9 | RFC9114/sections/ | 21 files |
| Agent 10 | RFC9204/sections/ | 17 files |

Each agent:
1. Lists all `.md` files in its assigned `sections/` folder
2. For each file, appends the navigation footer via `patch_note` (append mode)
3. Verifies footer was added by reading back the last 3 lines

**Acceptance Criteria:**
- [ ] All ~393 section files across 10 RFC folders have navigation footer
- [ ] Each footer links to the correct parent RFC index (e.g., RFC9113 sections link to `../RFC9113`)
- [ ] Each footer links to `../../00-RFC_STATUS_MATRIX`
- [ ] Spot-check: 5 random section files verified to have correct footer

---

### TASK-034-005: Fix RFC Status Matrix — QPACK data
**Description:** As a project status reviewer, I want the RFC Status Matrix to reflect the actual implementation state, not outdated data.

**Token Estimate:** ~15k tokens
**Predecessors:** none
**Successors:** none
**Parallel:** yes — can run alongside all other tasks
**Model:** haiku

**Changes to `RFC/00-RFC_STATUS_MATRIX.md`:**

| Field | Current (wrong) | Corrected |
|-------|-----------------|-----------|
| QPACK row: Status | `🟡 Draft` | `✅ Complete` |
| QPACK row: Score | `40/100` | `90/100` |
| QPACK row: Notes | "streamed decoder, blocking references" | "Full encoder + decoder, dynamic table sync, Huffman, instruction processing" |
| QPACK "Implemented" section | Only lists decoder features | Add encoder features (585 LOC): instruction processing, capacity management, section ack, stream cancellation |
| QPACK "NOT Implemented" section | Lists encoder as missing | Remove or reduce to edge cases only |
| Critical Gaps #2 | "HTTP/3 QPACK Encoder — QPACK decoder works, encoder missing" | Remove this entry entirely |
| Critical Gaps note | "QPACK is the main blocker for HTTP/3" | Remove this note |

**Acceptance Criteria:**
- [ ] QPACK score updated to 90/100
- [ ] QPACK status shows `✅ Complete`
- [ ] No mention of "QPACK encoder missing" anywhere in the file
- [ ] HTTP/3 critical gap about QPACK removed
- [ ] QPACK "Implemented" section lists both encoder and decoder features

## Task Dependency Graph

```
TASK-034-001 (Architecture cleanup)  ──┐
TASK-034-002 (Feature cleanup)       ──┤
TASK-034-003 (Template cleanup)      ──┤── all parallel, no dependencies
TASK-034-004 (RFC section navs)      ──┤
TASK-034-005 (Status Matrix fix)     ──┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-034-001 | ~30k | none | yes | — |
| TASK-034-002 | ~35k | none | yes | — |
| TASK-034-003 | ~10k | none | yes | haiku |
| TASK-034-004 | ~60k | none | yes (10 haiku agents) | haiku |
| TASK-034-005 | ~15k | none | yes | haiku |

**Total estimated tokens:** ~150k

## Functional Requirements

- FR-1: Zero `TASK-` references remaining in the vault after cleanup
- FR-2: Zero `.maggus` path references remaining in the vault after cleanup
- FR-3: Every RFC section file has a navigation footer linking to parent index and status matrix
- FR-4: QPACK compliance data in Status Matrix reflects actual implementation (encoder exists, 585 LOC, fully tested)
- FR-5: All content still reads naturally after TASK-ID removal — replaced with descriptive text, not just deleted

## Non-Goals

- No structural moves (handled by features 032 and 033)
- No changes to actual .maggus/ files — only removing references from vault notes
- No changes to RFC section content (only appending footers)
- No new RFC section files

## Technical Considerations

- Use Obsidian MCP `patch_note` for all edits — never use file system tools
- Feature note paths may differ depending on whether Feature 033 ran first — always use `list_directory` to find current paths
- Architecture note paths may differ depending on whether Feature 032 ran first
- RFC section footer append: use `patch_note` with the last line of the file as `oldString` and replacement including the footer
- The 10 parallel haiku agents for TASK-034-004 should each handle one RFC folder independently

## Success Metrics

- `grep -r "TASK-" notes/` returns 0 hits
- `grep -r ".maggus" notes/` returns 0 hits
- Random sample of 10 RFC section files all have navigation footer
- RFC Status Matrix QPACK section is accurate

## Open Questions

None — all resolved during planning.
