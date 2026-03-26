# TurboHttp Knowledge Base

This is the central hub for all TurboHttp project knowledge — connecting session logs, architecture decisions, RFC compliance notes, and feature planning.

## Architecture & Design Decisions

Key architectural patterns and decisions:

- [[Architecture/01-LAYERED_ARCHITECTURE|Layered Architecture]] — Client → Handlers → Streams → Protocol → Transport
- [[Architecture/02-STAGE_PATTERNS|GraphStage Patterns]] — Port naming, conventions, stage lifecycle
- [[Architecture/04-CURRENT_STATE_SUMMARY|Current State Summary]] — Implementation completeness, status, next milestones
- [[Architecture/03-KNOWN_GAPS_AND_LIMITATIONS|Known Gaps & Limitations]] — Critical issues, workarounds, priority roadmap

See [Architecture Notes](./Architecture/) for full decision records.

## RFC Compliance & Coverage

**Overall Compliance**: 86/100 — Production-Ready for HTTP/1.0, 1.1, 2.0

Quick reference:
- [[RFC/00-RFC_STATUS_MATRIX|RFC Status Matrix]] — Detailed compliance scores, gaps, and priorities (⭐ START HERE)
- [RFC 1945 (HTTP/1.0)](https://www.rfc-editor.org/rfc/rfc1945) — 85/100 ✅
- [RFC 9112 (HTTP/1.1)](https://www.rfc-editor.org/rfc/rfc9112) — 92/100 ✅
- [RFC 9113 (HTTP/2)](https://www.rfc-editor.org/rfc/rfc9113) — 87/100 ✅
- [RFC 7541 (HPACK)](https://www.rfc-editor.org/rfc/rfc7541) — 90/100 ✅
- [RFC 9114 (HTTP/3)](https://www.rfc-editor.org/rfc/rfc9114) — 60/100 🔶 (QPACK encoder missing)
- [RFC 9000 (QUIC)](https://www.rfc-editor.org/rfc/rfc9000) — 50/100 🔶 (primitives only)
- [RFC 9204 (QPACK)](https://www.rfc-editor.org/rfc/rfc9204) — 40/100 🔶 (decoder only)
- [RFC 9110 (HTTP Semantics)](https://www.rfc-editor.org/rfc/rfc9110) — 82/100 (redirects, retries)
- [RFC 6265 (Cookies)](https://www.rfc-editor.org/rfc/rfc6265) — 80/100 (RFC 6265 domain/path)
- [RFC 9111 (Caching)](https://www.rfc-editor.org/rfc/rfc9111) — 78/100 (freshness, validation)

All RFC reference documents are in the [rfc/](./rfc/) folder — quick references and compliance analysis.

See [RFC Notes](./RFC/00-RFC_STATUS_MATRIX) for detailed compliance notes.

## Active Debugging

Ongoing investigations and bug reports:

See [Debugging Notes](./Debugging/) for active investigations and trace logs.

## Recent Sessions

Session work logs and session notes:

See [Session Logs](./Sessions/) for session-by-session activity logs and decisions.

## Project Resources

**External Documentation:**
- [VitePress Site](../docs/) — User guides, architecture diagrams, API reference
- [README](../README.md) — Project overview
- [CLAUDE.md](../CLAUDE.md) — Dev environment setup and conventions

**Project Directories:**
- [`.maggus/`](../.maggus/) — Feature plans, bug reports, diagnostic logs
- [`docs/`](../docs/) — VitePress documentation and LikeC4 diagrams
- [`rfc/`](../rfc/) — RFC reference documents

## Getting Started with This Vault

### 1. Understand the Structure
- [[VAULT_STYLE_GUIDE|Vault Style Guide]] — Unified structure, frontmatter standards, formatting conventions
- [[OBSIDIAN_CSS_SETUP|CSS Setup Instructions]] — How to enable unified styling (visual consistency)

### 2. Create Notes
1. **Create a new session log:** Use `Insert Template > Session-Log` to capture daily work
2. **Document decisions:** Use `Insert Template > ADR` for architecture decisions
3. **Track RFC compliance:** Use `Insert Template > RFC-Note` for RFC sections you're implementing
4. **Investigate bugs:** Use `Insert Template > Bug-Investigation` for debugging sessions

All templates are in [Templates](./Templates/) folder.

### 3. Follow Conventions
- Frontmatter required: `title`, `description`, `tags`
- Single H1 heading per note (matches frontmatter title)
- Proper heading hierarchy (no skipped levels)
- WikiLinks for internal references: `[[path|Display Text]]`
- Status emojis for consistency: ✅ 🔶 ❌ 🟡 🟢
