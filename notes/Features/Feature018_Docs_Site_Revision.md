---
title: "Feature 018: Documentation Site Revision"
description: "User-goal-oriented rewrite of VitePress documentation site — guides, architecture diagrams, and LikeC4 diagram updates"
tags: [features, history, documentation, vitepress, likec4, guides]
status: completed
---

# Feature 018: Documentation Site Revision

## Summary

| Field | Value |
|-------|-------|
| **Status** | ✅ Completed |
| **Category** | Documentation |
| **Scope** | 7 tasks (TASK-018-001 through TASK-018-007) |
| **Maggus Plan** | Not available |

## Description

Comprehensive revision of the VitePress documentation site (`docs/`) to adopt user-goal-oriented language (what the user wants to accomplish, not what the library does internally). Updated all guide pages and architecture diagrams.

| Task | Scope |
|------|-------|
| TASK-018-001 | `guide/redirects.md` — user-goal-oriented language |
| TASK-018-002 | `guide/configuration.md`, `retries.md`, `caching.md`, `connection-pooling.md` |
| TASK-018-003 | Split `guide/advanced.md` — Channel API to Getting Started; custom stages to Architecture |
| TASK-018-004 | `architecture/pipeline.md` and `handlers.md` — goal-oriented language |
| TASK-018-005 | Updated LikeC4 diagrams — renamed HTTP/2 stages, improved pipeline labels, added missing engine view stages |
| TASK-018-006 | Site build verification, dead link detection, SVG fallback alignment |
| TASK-018-007 | Final cross-reference check — all internal links resolve, no orphaned pages |

The VitePress site uses Node.js 20+ and is served from `docs/`. Live reload via `npm run docs:dev`.

## Key Source Files

| File | Role |
|------|------|
| `docs/guide/` | All user-facing guide pages |
| `docs/architecture/` | Architecture documentation |
| `docs/.vitepress/` | VitePress configuration and theme |

## See Also

- [[Architecture/00-ONBOARDING\|Developer Onboarding Guide]] — internal developer docs (Obsidian vault)
- [[Architecture/01-LAYERED_ARCHITECTURE\|Layered Architecture]] — architecture reference
