# CLAUDE.md — Documentation Site

This file guides Claude Code when working on files inside `docs/`.

## Audience

Every page targets **library users** — .NET developers who use TurboHttp in their applications and want to understand how it works. The reader is NOT a protocol implementor, RFC editor, or contributor to this library's internals.

## Content Rules

### No RFC References

- Never cite RFC numbers, section numbers, or specification language (e.g. "RFC 9110 §15.4", "per RFC 9112")
- Describe **behaviour** instead: "TurboHttp follows redirects automatically" not "implements RFC 9110 §15.4 redirect semantics"
- If a feature exists because of a spec requirement, explain the *user-visible effect*, not the spec clause

### No 1:1 Implementation Mapping

- LikeC4 diagrams show **conceptual architecture** — layers, data flow, key components
- Do not add every internal class, stage, or actor to diagrams; keep the current abstraction level
- When adding new components to diagrams, ask: "Does a user need to know this exists?" — if not, leave it out

### Tone and Style

- Practical, example-driven — lead with code snippets and "what this does for you"
- Explain *what* stages and layers do, not *which spec section* they implement
- Use plain language: "keeps connections alive" not "evaluates connection persistence per §9.3"
- Tables for comparison (methods, status codes, options), callout boxes (`::: tip`, `::: warning`) for emphasis
- Keep headings scannable: H1 = page title, H2 = major sections, H3 = subsections

### Architecture Pages

- Stage names (e.g. `CookieBidiStage`, `RetryBidiStage`) are fine — they help users understand the pipeline
- Describe stages by **what they do for the user**: "injects cookies into outgoing requests" not "implements RFC 6265 domain matching"
- Actor and transport details are OK at the current level — don't go deeper into mailbox internals or byte-level framing

### Guide Pages

- Focus on: installation, configuration, usage patterns, code examples, troubleshooting
- Every feature page should answer: "How do I use this?" and "What happens automatically?"
- Warnings and tips should address practical concerns ("POST is never retried") not spec rationale

## Build Commands

See root `CLAUDE.md` for VitePress dev server, build, and preview commands.
