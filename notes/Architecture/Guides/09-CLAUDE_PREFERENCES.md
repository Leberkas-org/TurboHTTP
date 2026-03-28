---
title: Claude Code Preferences & Workflow Guidelines
description: >-
  User preferences for Claude Code interactions — language, documentation style,
  knowledge capture workflow, and response format
tags:
  - preferences
  - workflow
  - claude
aliases:
  - Preferences
  - Claude Guidelines
---
# Claude Code Preferences & Workflow Guidelines

**Last Updated**: 2026-03-26

## Language

- **Always respond in English** — regardless of input language
- Feature plans, documentation, code comments, and all outputs: English
- Obsidian notes: English

## Knowledge Capture

Every session must document important findings in the Obsidian vault (`notes/`):

| Discovery Type | Destination | Template |
|----------------|-------------|----------|
| RFC compliance gaps | `notes/RFC/` or `notes/rfc/` | RFC-Note |
| Architecture decisions | `notes/Architecture/` | ADR |
| Protocol limitations or workarounds | `notes/Architecture/` | ADR |
| Bug investigations & root causes | `notes/Debugging/` (git-ignored) | Bug-Investigation |
| Feature learnings | `notes/Features/` | — |
| Session work logs | `notes/Sessions/` (git-ignored) | Session-Log |

**Before ending session**: Check — did I discover something important that future sessions should know? If yes, create/update an Obsidian note.

## Response Style

- Terse responses, no trailing summaries (user reads the diff)
- Go straight to the point
- No emojis unless requested
