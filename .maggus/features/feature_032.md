<!-- maggus-id: 7e9a0927-8f51-4482-b1fb-b665d71991f0 -->
# Feature 032: Obsidian Vault — Architecture Restructuring

## Introduction

The `Architecture/` folder in the Obsidian vault is a flat dump of 17 files mixing core design docs, per-layer details, audits, investigations, and conventions. This makes browsing and discovery difficult. This feature reorganises Architecture/ into thematic subfolders, creates index notes for each subfolder, and fixes all wikilinks broken by the moves.

### Architecture Context

- **Components involved:** Obsidian vault (`notes/Architecture/`), vault index (`notes/00-Index.md`), style guide, CLAUDE.md
- **New patterns:** Subfolder `_INDEX.md` files for browsable navigation within each category
- **No code changes** — documentation-only restructuring

## Goals

- Organise 16 Architecture files into 5 thematic subfolders for intuitive browsing
- Create `_INDEX.md` in each subfolder with wikilinks and one-line descriptions
- Fix all broken wikilinks across the entire vault after moves
- Update CLAUDE.md references to Architecture notes
- Update VAULT_STYLE_GUIDE.md folder tree diagram

## Tasks

### TASK-032-001: Move Architecture files into subfolders
**Description:** As a developer browsing the vault, I want Architecture notes grouped by theme so I can find what I need without scanning 17 unrelated files.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-032-002, TASK-032-003
**Parallel:** no — moves must complete before link fixes

**Moves:**

| Source | Target Subfolder |
|--------|-----------------|
| `01-LAYERED_ARCHITECTURE.md` | `Design/` |
| `02-STAGE_PATTERNS.md` | `Design/` |
| `06-DECODER_PIPELINE_ARCHITECTURE.md` | `Design/` |
| `13-CLIENT_LAYER.md` | `Layers/` |
| `14-TRANSPORT_LAYER.md` | `Layers/` |
| `15-STREAMS_LAYER.md` | `Layers/` |
| `16-PROTOCOL_LAYER.md` | `Layers/` |
| `03-KNOWN_GAPS_AND_LIMITATIONS.md` | `Status/` |
| `04-CURRENT_STATE_SUMMARY.md` | `Status/` |
| `07-HTTP10_RECONNECTION_LIMITATION.md` | `Analysis/` |
| `08-HTTP2_DECODER_MIGRATION.md` | `Analysis/` |
| `10-DEADLOCK_ANALYSIS.md` | `Analysis/` |
| `11-STAGE_COMPLETION_AUDIT.md` | `Analysis/` |
| `05-BENCHMARK_PATTERNS.md` | `Guides/` |
| `09-CLAUDE_PREFERENCES.md` | `Guides/` |
| `12-TEST_ORGANIZATION.md` | `Guides/` |
| `17-DIAGNOSTICS_INTEGRATION.md` | `Guides/` |

`00-ONBOARDING.md` stays at `Architecture/` root as entry point.

**Acceptance Criteria:**
- [x] 16 files moved via Obsidian MCP `move_note`
- [x] `00-ONBOARDING.md` remains at `Architecture/` root
- [x] 5 subfolders exist: `Design/`, `Layers/`, `Status/`, `Analysis/`, `Guides/`
- [x] No files left in `Architecture/` root except `00-ONBOARDING.md` and `.gitkeep`

---

### TASK-032-002: Create subfolder index notes
**Description:** As a vault browser, I want each subfolder to have an `_INDEX.md` so I immediately see what's inside.

**Token Estimate:** ~15k tokens
**Predecessors:** TASK-032-001
**Successors:** TASK-032-003
**Parallel:** no — needs files in place first

**Acceptance Criteria:**
- [ ] `Architecture/Design/_INDEX.md` created with frontmatter + wikilinks to 3 files
- [ ] `Architecture/Layers/_INDEX.md` created with frontmatter + wikilinks to 4 files
- [ ] `Architecture/Status/_INDEX.md` created with frontmatter + wikilinks to 2 files
- [ ] `Architecture/Analysis/_INDEX.md` created with frontmatter + wikilinks to 4 files
- [ ] `Architecture/Guides/_INDEX.md` created with frontmatter + wikilinks to 4 files
- [ ] Each index has proper frontmatter (title, description, tags) per VAULT_STYLE_GUIDE

---

### TASK-032-003: Fix all broken wikilinks from Architecture moves
**Description:** As a vault user, I want all wikilinks to resolve after the restructuring so no navigation is broken.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-032-001
**Successors:** none
**Parallel:** no — must verify moves completed

**Files to update (search for `[[Architecture/` patterns):**
- `00-Index.md` — ~17 Architecture links
- `Architecture/00-ONBOARDING.md` — internal cross-references
- `Architecture/**/*.md` — cross-references between Architecture notes
- `RFC/*/RFC*.md` — "See Also" sections referencing `Architecture/03-KNOWN_GAPS_AND_LIMITATIONS`
- `VAULT_STYLE_GUIDE.md` — folder tree diagram in Section 10
- `CLAUDE.md` (project root) — Key Notes Reference section

**Acceptance Criteria:**
- [ ] `00-Index.md` updated with all new Architecture paths
- [ ] All Architecture notes' internal cross-references updated
- [ ] All RFC index "See Also" links to Architecture notes updated
- [ ] `VAULT_STYLE_GUIDE.md` folder tree diagram reflects new structure
- [ ] `CLAUDE.md` references updated (09-CLAUDE_PREFERENCES, 04-CURRENT_STATE_SUMMARY, 05-BENCHMARK_PATTERNS, 06-DECODER_PIPELINE_ARCHITECTURE)
- [ ] Grep for `[[Architecture/0` and `[[Architecture/1` returns 0 hits (old flat paths gone)

## Task Dependency Graph

```
TASK-032-001 ──→ TASK-032-002
             ──→ TASK-032-003
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-032-001 | ~25k | none | no | — |
| TASK-032-002 | ~15k | 001 | no | — |
| TASK-032-003 | ~40k | 001 | no | — |

**Total estimated tokens:** ~80k

## Functional Requirements

- FR-1: All 16 Architecture files must be moved to the correct thematic subfolder
- FR-2: Each subfolder must have a `_INDEX.md` with frontmatter and wikilinks to all contained files
- FR-3: Every wikilink in the vault that previously pointed to `Architecture/NN-NAME` must be updated to `Architecture/Subfolder/NN-NAME`
- FR-4: `CLAUDE.md` key notes reference section must reflect new paths
- FR-5: Zero broken wikilinks after completion

## Non-Goals

- No content changes to the Architecture notes themselves
- No renaming of files (only moving)
- No changes to RFC/ structure
- No changes to Features/ (handled by feature_033)

## Technical Considerations

- Use Obsidian MCP `move_note` for all moves — never use file system tools on vault files
- Use `patch_note` for wikilink updates — more efficient than full rewrites
- Wikilink format: `[[Architecture/Design/01-LAYERED_ARCHITECTURE|Layered Architecture]]`
- Must verify links resolve in Obsidian after completion

## Success Metrics

- Zero broken wikilinks (verified by vault-wide grep)
- All Architecture notes reachable via subfolder `_INDEX.md` browsing
- CLAUDE.md references resolve correctly

## Open Questions

None — all resolved during planning.
