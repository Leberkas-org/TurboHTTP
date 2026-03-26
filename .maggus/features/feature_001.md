<!-- maggus-id: 20260326-160046-feature-026 -->
# Feature 026: RFC Vault Cleanup — Obsidian Formatting & Filename Improvements (Extended)

## Introduction

The 10 RFC index notes under `notes/RFC/` were created over multiple sessions with slightly
different conventions. Problems include: missing frontmatter fields, inconsistent wikilink syntax
(`[[sections/...]]` vs `[[RFCXXXX/sections/...]]`, `\|` vs `|` separators), embedded raw RFC
text blocks (500–2000 lines per file) that make navigation painful, plain-text `.txt` analysis
files that don't render as Markdown, a duplicate file, and RFC section links that have never been
verified to actually resolve to existing files.

Additionally, all 393 RFC section files across the 10 RFC folders use a hybrid format with
structural issues: H2 headings instead of H1, plain-text RFC-style subsection headings that should
be Markdown headings, and inconsistent normative keyword formatting (MUST/SHOULD/MAY/SHALL).

This feature brings every RFC index note AND every section file up to the current `VAULT_STYLE_GUIDE.md`
standard so the vault is clean, consistent, and fully navigable in Obsidian.

### Architecture Context

- **No code changes** — documentation/knowledge-vault only
- **Affected directories:** `notes/RFC/` (both index files and all 393 section files), `notes/Templates/`
- **Style authority:** `notes/VAULT_STYLE_GUIDE.md`
- **No VISION.md / ARCHITECTURE.md** — plan based on vault conventions
- **Scope expanded:** Originally index files only; now includes all 393 section files across 10 RFC folders

---

## Goals

- All 10 RFC index files have complete, consistent frontmatter (including `source_url`)
- Embedded raw RFC text blocks removed; replaced with compact external link callouts
- Wikilinks standardized to `[[RFCXXXX/sections/NN_topic|Display]]` format throughout (indices)
- `\|` separator replaced with `|` everywhere (except inside Markdown tables where `\|` is required for escaping)
- Every section link in every RFC index is verified to point to a file that actually exists in `sections/`
- BOM characters eliminated
- `.txt` analysis files converted to proper Markdown
- Duplicate `RFC9114_SUMMARY.txt` deleted
- Template updated so future RFC notes start correct
- All index files under 200 lines after cleanup
- **NEW:** All 393 RFC section files converted from hybrid format to proper Markdown:
  - H2 headings converted to H1, duplicate title repeats removed
  - Plain-text RFC-style subsection headings (e.g., `2.1.  Topic`) converted to Markdown headings
  - MUST/SHOULD/MAY/SHALL/REQUIRED normative keywords wrapped in callouts for clarity
- All section files comply with `VAULT_STYLE_GUIDE.md` formatting standards

---

## Tasks

### TASK-026-001: Update RFC-Index Template
**Description:** As a developer, I want the canonical RFC-Index template updated with `source_url`
as a required field and the correct wikilink format so all future RFC notes are created correctly.

**Token Estimate:** ~10k tokens
**Predecessors:** none
**Successors:** TASK-026-002, TASK-026-003
**Parallel:** yes — can run alongside TASK-026-007

**Acceptance Criteria:**
- [x] `notes/Templates/RFC-Index.md` frontmatter includes `source_url: https://www.rfc-editor.org/rfc/rfcXXXX`
- [x] `## Full RFC Document` section replaced with `> 📌 **External Source**: [RFC XXXX — Title](url)` callout pattern
- [x] Wikilink examples in template use `[[RFCXXXX/sections/NN_topic|Display]]` with `|` separator
- [x] File stays under 80 lines

---

### TASK-026-002: Remove Embedded Raw RFC Text — Older RFCs (RFC1945, RFC6265, RFC7541, RFC9000)
**Description:** As a vault user, I want the raw RFC text blocks removed from the four older index
files so they load instantly in Obsidian without scrolling past thousands of lines.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-026-001
**Successors:** TASK-026-004, TASK-026-005
**Parallel:** yes — can run alongside TASK-026-003

**Acceptance Criteria:**
- [x] `RFC1945.md` — Keep lines 1–122 (frontmatter, Quick Reference, Core Concepts, Sections table, etc.); replace everything from line 123 onwards (the `## Full RFC Document` code block and trailing sections) with:
  ```markdown
  ## Full RFC Document

  > 📌 **External Source**: [RFC 1945 — HTTP/1.0](https://www.rfc-editor.org/rfc/rfc1945)
  >
  > The complete RFC text is available online. See the `sections/` subfolder for individual section references.
  ```
- [x] `RFC6265.md` — Keep lines 1–96; replace lines 97+ with the same external link callout pattern
- [x] `RFC7541.md` — Keep lines 1–86; replace lines 87+ with the same external link callout pattern
- [x] `RFC9000.md` — Keep lines 1–165; replace lines 166+ with the same external link callout pattern
- [x] Each file is now under 150 lines
- [x] No raw RFC text remains in any of the four files
- [x] No `## How to Search This RFC` section (if present, it is part of the old embedded text and should be removed)

---

### TASK-026-003: Remove Embedded Raw RFC Text — Newer RFCs (RFC9110–RFC9204)
**Description:** As a vault user, I want the raw RFC text blocks removed from the six newer index
files for the same navigability reason.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-026-001
**Successors:** TASK-026-004, TASK-026-005
**Parallel:** yes — can run alongside TASK-026-002

**Acceptance Criteria:**
- [x] `RFC9110.md`, `RFC9111.md`, `RFC9112.md`, `RFC9113.md`, `RFC9114.md`, `RFC9204.md` — each raw text block replaced with external link callout
- [x] Each file is now under 200 lines
- [x] BOM character `﻿` (Unicode FEFF) no longer present in any file (it was only in the raw text blocks)

---

### TASK-026-004: Standardize Frontmatter — Older RFCs
**Description:** As a vault user, I want `source_url` added to the four older RFC files whose
frontmatter predates that field being required.

**Token Estimate:** ~15k tokens
**Predecessors:** TASK-026-002
**Successors:** TASK-026-008
**Parallel:** yes — can run alongside TASK-026-005

**Acceptance Criteria:**
- [x] `RFC1945.md` frontmatter: `source_url: https://www.rfc-editor.org/rfc/rfc1945` added
- [x] `RFC6265.md` frontmatter: `source_url: https://www.rfc-editor.org/rfc/rfc6265` added
- [x] `RFC7541.md` frontmatter: `source_url: https://www.rfc-editor.org/rfc/rfc7541` added
- [x] `RFC9000.md` frontmatter: `source_url: https://www.rfc-editor.org/rfc/rfc9000` added
- [x] All four have an `aliases` field (empty array `[]` if unused)

---

### TASK-026-005: Standardize Wikilinks — All 10 RFC Index Files
**Description:** As a vault user, I want all section wikilinks to use the vault-root-relative
format with `|` separator consistently, so that Obsidian resolves them correctly from any view.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-026-002, TASK-026-003
**Successors:** TASK-026-006
**Parallel:** no — all 10 files, must be done after raw text removal to avoid touching 2000-line blocks

**Acceptance Criteria:**
- [x] Older RFCs (1945, 6265, 7541, 9000): all `[[sections/NN_topic|...]]` → `[[RFCXXXX/sections/NN_topic|...]]`
- [x] All RFCs: wikilinks outside tables: `\|` → `|` separator (wikilinks inside Markdown tables keep `\|` for escaping)
- [x] Cross-RFC links use vault-root-relative form: `[[RFC/RFCXXXX/RFCXXXX|RFC XXXX]]`
- [x] Core Concepts and Sections table use consistent wikilink format (no mixing of styles within a file)
- [x] No bare `[[sections/...]]` links remain in any file

---

### TASK-026-006: Verify Section Links Resolve to Existing Files
**Description:** As a vault user, I want every section wikilink in every RFC index to point to a
file that actually exists in the corresponding `sections/` folder, so that clicking a link in
Obsidian opens a note rather than creating a phantom.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-026-005
**Successors:** TASK-026-008
**Parallel:** no — must run after wikilinks are standardized

**Acceptance Criteria:**
- [x] For each of the 10 RFC index files, enumerate files in its `sections/` subdirectory
- [x] For each wikilink in the Sections table, confirm the linked `.md` file exists
- [x] Any broken link is either: fixed (if file exists under slightly different name), or marked with `⚠️` (if section note was never created)
- [x] A `## Link Verification` section added to each RFC index (can be a collapsed callout) documenting the check
- [x] No unresolved phantom wikilinks remain without a `⚠️` marker

---

### TASK-026-007: Fix Individual Data Errors
**Description:** As a vault user, I want specific factual errors corrected so that the data I
read is accurate.

**Token Estimate:** ~15k tokens
**Predecessors:** none
**Successors:** TASK-026-008
**Parallel:** yes — independent of all other index tasks

**Acceptance Criteria:**
- [x] `RFC9204.md` H1 reads `# RFC 9204 — QPACK: Field Compression for HTTP/3` (was abbreviated)
- [x] `RFC9204.md` Core Concepts: static table entry count verified against RFC 9204 Appendix A and corrected
- [x] `RFC9111.md` Quick Reference compliance score updated to `78/100` (aligns with `00-RFC_STATUS_MATRIX.md`)
- [x] `RFC9000.md` Quick Reference test path clarified: `TurboHttp.Tests/RFC9114/ (shared with HTTP/3)`
- [x] `rfc_metadata.json` RFC9111 `compliance_score` updated to `78` to match STATUS_MATRIX

---

### TASK-026-008: Convert Analysis .txt Files to Markdown + Delete Duplicate
**Description:** As a vault user, I want the plain-text analysis files converted to proper Markdown
so they render in Obsidian, and the duplicate RFC9114 summary deleted.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-026-003, TASK-026-004, TASK-026-006, TASK-026-007
**Successors:** none
**Parallel:** no — final index cleanup step

**Acceptance Criteria:**
- [x] `RFC9111_ANALYSIS_SUMMARY.txt` renamed to `RFC9111_ANALYSIS_SUMMARY.md`; body converted: `===` banners removed, ALL CAPS headings → `##` Markdown headings, data formatted as Markdown tables
- [x] `RFC9114_ANALYSIS_SUMMARY.txt` renamed to `RFC9114_ANALYSIS_SUMMARY.md`; same conversion applied
- [x] `RFC9114_SUMMARY.txt` deleted (confirmed near-identical duplicate of `RFC9114_ANALYSIS_SUMMARY.txt`)
- [x] Both converted `.md` files retain their existing YAML frontmatter
- [x] No `.txt` files remain anywhere under `notes/RFC/`

---

### TASK-026-009: Convert Section Files — RFC1945, RFC6265, RFC7541, RFC9000
**Description:** As a vault user, I want all 163 RFC section files from the four older RFCs
converted to proper Markdown so they are consistent with `VAULT_STYLE_GUIDE.md` and display
cleanly in Obsidian.

**Token Estimate:** ~80k tokens
**Predecessors:** none (can run independently of index tasks)
**Successors:** none (standalone section conversion)
**Parallel:** yes — can run alongside TASK-026-010, TASK-026-011, and all index tasks
**Model:** haiku — repetitive structural conversion across 163 files

**Acceptance Criteria:**
- [ ] RFC1945 section files (36): top heading changed H2 → H1; duplicate title repeat removed
- [ ] RFC6265 section files (24): same conversion applied
- [ ] RFC7541 section files (12): same conversion applied
- [ ] RFC9000 section files (91): same conversion applied
- [ ] Every section file in batch has exactly one `# H1` heading matching frontmatter `title` field
- [ ] No plain-text subsection headings remain (all `N.N.  Title` patterns converted to `##` or `###` as appropriate)
- [ ] No duplicate title-repeat line below the H1 (RFC source verbatim text removed)
- [ ] Prose lines containing MUST/SHOULD/MAY/SHALL/REQUIRED/SHALL NOT wrapped as `> **KEYWORD**:` callouts
- [ ] YAML frontmatter remains unchanged
- [ ] ABNF code blocks remain unchanged
- [ ] Lines already inside blockquotes or code blocks are NOT double-processed
- [ ] Total: 163 files converted, all verified for proper structure

---

### TASK-026-010: Convert Section Files — RFC9110, RFC9111, RFC9112
**Description:** As a vault user, I want all 157 RFC section files from three newer RFCs
converted to proper Markdown using the same rules as TASK-026-009.

**Token Estimate:** ~80k tokens
**Predecessors:** none (can run independently of index tasks)
**Successors:** none (standalone section conversion)
**Parallel:** yes — can run alongside TASK-026-009, TASK-026-011, and all index tasks
**Model:** haiku — repetitive structural conversion across 157 files

**Acceptance Criteria:**
- [ ] RFC9110 section files (112): top heading H2 → H1; duplicate title removed
- [ ] RFC9111 section files (20): same conversion applied
- [ ] RFC9112 section files (25): same conversion applied
- [ ] Every section file in batch has exactly one `# H1` heading matching frontmatter `title` field
- [ ] No plain-text subsection headings remain
- [ ] No duplicate title-repeat lines
- [ ] Prose lines containing MUST/SHOULD/MAY/SHALL/REQUIRED/SHALL NOT wrapped as `> **KEYWORD**:` callouts
- [ ] YAML frontmatter unchanged, ABNF/code blocks unchanged, no double-processing of blockquotes/code
- [ ] Total: 157 files converted, all verified for proper structure

---

### TASK-026-011: Convert Section Files — RFC9113, RFC9114, RFC9204
**Description:** As a vault user, I want all 73 RFC section files from the three remaining RFCs
converted to proper Markdown using the same rules.

**Token Estimate:** ~50k tokens
**Predecessors:** none (can run independently of index tasks)
**Successors:** none (standalone section conversion)
**Parallel:** yes — can run alongside TASK-026-009, TASK-026-010, and all index tasks
**Model:** haiku — repetitive structural conversion across 73 files

**Acceptance Criteria:**
- [ ] RFC9113 section files (35): top heading H2 → H1; duplicate title removed
- [ ] RFC9114 section files (21): same conversion applied
- [ ] RFC9204 section files (17): same conversion applied
- [ ] Every section file in batch has exactly one `# H1` heading matching frontmatter `title` field
- [ ] No plain-text subsection headings remain
- [ ] No duplicate title-repeat lines
- [ ] Prose lines containing MUST/SHOULD/MAY/SHALL/REQUIRED/SHALL NOT wrapped as `> **KEYWORD**:` callouts
- [ ] YAML frontmatter unchanged, ABNF/code blocks unchanged, no double-processing of blockquotes/code
- [ ] Total: 73 files converted, all verified for proper structure

---

## Task Dependency Graph

```
TASK-026-001 ──→ TASK-026-002 ──→ TASK-026-005 ──→ TASK-026-006 ──┐
             └──→ TASK-026-003 ──┘                                  │
TASK-026-001 ──→ TASK-026-002 ──→ TASK-026-004 ──────────────────→ TASK-026-008
TASK-026-007 ────────────────────────────────────────────────────→ ┘

TASK-026-009 ──┐
TASK-026-010 ──┼─→ (independent parallel runs, no successors)
TASK-026-011 ──┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-026-001 | ~10k | none | yes (with 007, 009-011) | haiku |
| TASK-026-002 | ~30k | 001 | yes (with 003) | haiku |
| TASK-026-003 | ~30k | 001 | yes (with 002) | haiku |
| TASK-026-004 | ~15k | 002 | yes (with 005) | haiku |
| TASK-026-005 | ~40k | 002, 003 | no | haiku |
| TASK-026-006 | ~50k | 005 | no | — |
| TASK-026-007 | ~15k | none | yes (with 001, 009-011) | haiku |
| TASK-026-008 | ~25k | 003, 004, 006, 007 | no | — |
| TASK-026-009 | ~80k | none | yes (with 001-008, 010, 011) | haiku |
| TASK-026-010 | ~80k | none | yes (with 001-008, 009, 011) | haiku |
| TASK-026-011 | ~50k | none | yes (with 001-008, 009, 010) | haiku |

**Total estimated tokens:** ~425k (original ~215k + section conversion ~210k)

---

## Functional Requirements

### Index File Requirements (FR-1 through FR-11)
- FR-1: Every RFC index file has `title`, `description`, `tags`, `rfc_number`, `source_url` in frontmatter
- FR-2: No RFC index file contains an embedded raw-text code block; each has a `> 📌` external link callout instead
- FR-3: All wikilinks use `[[RFCXXXX/sections/NN_topic|Display Text]]` format (vault-root-relative)
- FR-4: The `|` separator is used consistently in all wikilinks outside tables (table wikilinks use `\|` for proper Markdown escaping)
- FR-5: Every section wikilink either resolves to an existing file or is marked `⚠️` as missing
- FR-6: No `.txt` files exist under `notes/RFC/`
- FR-7: `RFC9114_SUMMARY.txt` is deleted
- FR-8: `RFC9111.md` shows compliance score `78/100`
- FR-9: `RFC9204.md` H1 contains the full title
- FR-10: `rfc_metadata.json` is consistent with `00-RFC_STATUS_MATRIX.md` for all scores
- FR-11: All RFC index files are under 200 lines after cleanup

### Section File Requirements (FR-12 through FR-15)
- FR-12: Every RFC section file has exactly one `# H1` heading matching the frontmatter `title` field
- FR-13: No plain-text RFC-style subsection headings remain; all are converted to Markdown heading levels (`##`, `###`, etc.)
- FR-14: No duplicate title-repeat lines exist below the H1 (RFC source verbatim removed)
- FR-15: Prose sentences containing normative keywords (MUST, SHOULD, MAY, SHALL, REQUIRED) are wrapped in `> **KEYWORD**:` callouts for clarity

---

## Non-Goals

- No changes to compliance analysis content — formatting only
- No renaming of RFC index files themselves (`RFC1945.md` stays `RFC1945.md`)
- No renaming of RFC section files themselves (filenames remain as-is)
- No changes to `00-RFC_STATUS_MATRIX.md`
- No commits — the developer commits manually
- No programmatic changes to YAML frontmatter in section files (keep as-is)
- No modification of ABNF code blocks or technical specifications within section files

---

## Technical Considerations

### Index-Related Considerations
- **BOM characters** (`﻿`, Unicode FEFF) only appear in the embedded raw text blocks — they disappear automatically via TASK-026-002/003
- **QPACK Static Table**: RFC 9204 Appendix A defines 99 static table entries (fields 0–98). Verify the exact count before correcting
- **Wikilink `\|` vs `|`**: Inside Markdown tables, pipes must be escaped (`\|`). Outside tables, use bare `|`. TASK-026-005 must handle this distinction — do not blindly replace all `\|` with `|`
- **Section link verification (TASK-026-006)**: Enumerate actual files in each `sections/` folder via glob, then cross-reference against the Sections table. Document any missing sections

### Section File Conversion Rules (TASK-026-009 to 011)

**Rule 1 — Top heading: H2 → H1 + remove duplicate repeat**
```
BEFORE:
## 4.  HTTP Message
4.  HTTP Message

AFTER:
# 4.  HTTP Message
```
The second line is the verbatim RFC plain-text repeat — delete it.

**Rule 2 — Subsection headings: plain-text → Markdown headings**
Detect lines matching `^\d+(\.\d+)*\.\s{2,}\S` on their own line (preceded and followed by blank line).
Heading depth: 1 segment (e.g., `4.`) → `##`; 2 segments (`4.1.`) → `##`; 3+ segments → `###`.
```
BEFORE:                              AFTER:
4.1.  General Structure          →   ## 4.1.  General Structure

4.1.2.  Message Body             →   ## 4.1.2.  Message Body

4.1.2.3.  Token Rules            →   ### 4.1.2.3.  Token Rules
```

**Rule 3 — Normative keyword callouts (conservative approach)**
Wrap complete sentences containing standalone RFC normative keywords: MUST NOT, MUST, SHOULD NOT, SHOULD, MAY NOT, MAY, REQUIRED, SHALL NOT, SHALL.
Only wrap prose (not blockquotes, code blocks, or headings). Never split sentences mid-way.
```
BEFORE:
   The server MUST include a Date header in all responses.

AFTER:
> **MUST**: The server MUST include a Date header in all responses.
```

**Safety guardrails:**
- Skip lines already starting with `>` (already in blockquotes)
- Skip lines inside ` ``` ` code fences
- Skip lines that are headings (start with `#`)
- Wrap whole sentences only (period-terminated)
- Do NOT double-process content already in callouts or code blocks

---

## Success Metrics

### Index Files
- All 10 RFC index files load in Obsidian with zero scroll needed to see the full structure
- `Ctrl+Click` on every non-`⚠️` wikilink opens the correct section note
- No `.txt` files in the vault
- Frontmatter search in Obsidian finds all RFCs via `source_url`
- Every index file is under 200 lines

### Section Files
- All 393 RFC section files display cleanly in Obsidian with proper Markdown formatting
- Top heading of each section file matches frontmatter `title` exactly
- All subsection headings are properly formatted as Markdown headings
- Normative keywords stand out visually in callout boxes for RFC compliance clarity
- Vault feels unified and consistent across all 403 files (10 index + 393 sections)

---

## Open Questions

*(none — all questions resolved during planning)*

