---
title: Obsidian Vault Workflow
tags: [architecture, workflow, knowledge-management]
created: 2026-04-13
updated: 2026-04-13
---

# Obsidian Vault Workflow

The project knowledge base lives in `notes/` as an Obsidian vault. This is the single source of truth for all non-code knowledge.

## Access Rules

- **ALWAYS use Obsidian MCP tools** (`search_notes`, `read_note`, `write_note`, `patch_note`, etc.) to interact with the vault — NEVER use `Read`/`Write`/`Edit` file tools on `notes/` files
- MCP ensures Obsidian indexes stay consistent and frontmatter is properly handled

## When to READ from Obsidian

- Before working on any RFC-related task → `search_notes("RFC XXXX section Y")`
- Before architecture decisions → `search_notes("component name")`
- When you don't know something about the project → search the vault first
- When investigating bugs → check `notes/Debugging/` and `notes/Architecture/`
- Before implementing features → check `notes/Features/`

## When to WRITE to Obsidian

| Discovery Type | Destination | MCP Action |
|----------------|-------------|------------|
| RFC compliance gaps | `RFC/` | `write_note` with RFC-Note template structure |
| Architecture decisions | `Architecture/` | `write_note` with ADR template structure |
| Protocol limitations | `Architecture/` | `write_note` or `patch_note` |
| Bug investigations | `Debugging/` | `write_note` with Bug-Investigation structure |
| Feature learnings | `Features/` | `write_note` |
| Benchmark findings | `Architecture/` | `patch_note` on existing benchmark note |

**Before ending any session**: Check — did I discover something important? If yes → `write_note` or `patch_note` in Obsidian.

## Vault Structure

```
notes/
├── 00-Index.md            # Central hub — START HERE
├── Architecture/          # ADRs, design decisions, patterns, preferences, limitations
│   ├── Analysis/          # Deep-dive analysis notes
│   ├── Design/            # Core architecture documents
│   ├── Guides/            # How-to guides and conventions
│   └── Status/            # Project status tracking
├── RFC/                   # Per-RFC compliance tracking (with sections/ subfolders)
├── rfc/                   # RFC reference documents (quick refs, analysis)
├── Features/              # Feature plans and progress
│   ├── Diagnostics/
│   ├── Infrastructure/
│   ├── Performance/
│   ├── Protocol/
│   └── Testing/
├── Templates/             # Session-Log, RFC-Note, ADR, Bug-Investigation
└── Debugging/             # (git-ignored) Bug investigations
```

## Key Notes Reference

- [[01-LAYERED_ARCHITECTURE]] — Full layer-by-layer architecture
- [[02-STAGE_PATTERNS]] — GraphStage patterns and conventions
- [[04-CURRENT_STATE_SUMMARY]] — Project status, completeness scores
- [[05-BENCHMARK_PATTERNS]] — BDN conventions, port assignments, TCP workarounds
- [[06-DECODER_PIPELINE_ARCHITECTURE]] — Three-layer decoder pattern
- [[09-CLAUDE_PREFERENCES]] — Language, workflow, response style preferences
- [[Architecture/Guides/10-TEST_CONVENTIONS|Test Conventions]] — Test naming, structure, migration strategy
- [[Architecture/Guides/11-STAGE_PORT_NAMING|Stage Port Naming]] — Inlet/outlet port naming reference
