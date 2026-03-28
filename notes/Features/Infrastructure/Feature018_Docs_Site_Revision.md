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
| **Scope** | 7 steps |

## Description

Comprehensive revision of the VitePress documentation site (`docs/`) to adopt user-goal-oriented language (what the user wants to accomplish, not what the library does internally). Updated all guide pages and architecture diagrams.

| # | Scope |
|---|-------|
| 1 | `guide/redirects.md` — user-goal-oriented language |
| 2 | `guide/configuration.md`, `retries.md`, `caching.md`, `connection-pooling.md` |
| 3 | Split `guide/advanced.md` — Channel API to Getting Started; custom stages to Architecture |
| 4 | `architecture/pipeline.md` and `handlers.md` — goal-oriented language |
| 5 | Updated LikeC4 diagrams — renamed HTTP/2 stages, improved pipeline labels, added missing engine view stages |
| 6 | Site build verification, dead link detection, SVG fallback alignment |
| 7 | Final cross-reference check — all internal links resolve, no orphaned pages |

The VitePress site uses Node.js 20+ and is served from `docs/`. Live reload via `npm run docs:dev`.

## Key Source Files

| File | Role |
|------|------|
| `docs/guide/` | All user-facing guide pages |
| `docs/architecture/` | Architecture documentation |
| `docs/.vitepress/` | VitePress configuration and theme |

## See Also

- [[Architecture/00-ONBOARDING\|Developer Onboarding Guide]] — internal developer docs (Obsidian vault)
- [[Architecture/Design/01-LAYERED_ARCHITECTURE\|Layered Architecture]] — architecture reference
