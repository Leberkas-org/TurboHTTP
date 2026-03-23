# Feature 018: User-Goal-Oriented Documentation Revision

## Introduction

The TurboHttp documentation site (`docs/`) is well-structured but sometimes drifts into implementation details that library users don't need. This feature revises the docs to be consistently user-goal-oriented: every page should answer "What does this do for me?" and "How do I use it?" — not "How is this implemented internally?"

The `docs/CLAUDE.md` guardrails are already in place. This feature applies those principles to the existing content.

### Key Decisions

- **Guide pages**: Same scope, but rewrite technical details as user benefits
- **Architecture pages**: Keep stage names, cut engineering justifications and internal details
- **Advanced page**: Split — Channel API moves to Getting Started, Custom Stages moves to Architecture
- **LikeC4 diagrams**: Update where too deep, keep current conceptual level
- **Language**: Entire documentation in English (already is, but ensure consistency)

## Goals

- Every guide page explains features through user outcomes, not implementation mechanics
- Architecture pages describe "what it does" not "why we built it this way"
- No orphan content after `advanced.md` split
- LikeC4 diagrams reflect current codebase state at appropriate abstraction
- Zero RFC references in any docs page
- All pages consistent in tone: practical, example-driven, benefit-focused

## Tasks

### TASK-018-001: Revise guide/redirects.md
**Description:** As a library user, I want the redirects page to explain redirect behavior in plain language so that I understand what happens automatically without needing HTTP spec knowledge.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-018-007
**Parallel:** yes — can run alongside TASK-018-002, TASK-018-003, TASK-018-004, TASK-018-005

**Acceptance Criteria:**
- [x] Status Code Behaviour table rewritten with user language ("POST becomes GET for legacy compatibility" not spec semantics)
- [x] "Origin" defined inline where first used (scheme + hostname + port)
- [x] Loop Detection section adds user context ("prevents infinite loops from misconfigured servers")
- [x] `AllowHttpsToHttpDowngrade` includes practical advice ("rarely needed; only in fully-trusted internal networks")
- [x] No RFC section numbers anywhere on the page
- [x] Page reads as "what TurboHttp does for you" not "what the HTTP spec says"

### TASK-018-002: Revise guide/configuration.md, retries.md, caching.md, connection-pooling.md
**Description:** As a library user, I want these guide pages to explain options and behavior with enough context that I don't need to look up technical terms or implementation details.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** TASK-018-007
**Parallel:** yes — can run alongside TASK-018-001, TASK-018-003, TASK-018-004, TASK-018-005

**Acceptance Criteria:**
- [x] **configuration.md**: `ReconnectInterval` and `MaxReconnectAttempts` have one-sentence context explaining when/why a user needs them
- [x] **retries.md**: "idempotent" defined inline at first use ("safe to repeat without side effects")
- [x] **retries.md**: "Partially-consumed request body" simplified to "stream that cannot be rewound"
- [x] **caching.md**: Status codes grouped by category instead of listed as numbers
- [x] **caching.md**: Heuristic freshness formula explained in plain language
- [x] **caching.md**: `no-cache` split into separate Request/Response rows in table
- [x] **connection-pooling.md**: Opening simplified to one clear sentence about automatic pool management
- [x] **connection-pooling.md**: Backoff table demoted to a callout/note, not prominent section
- [x] **connection-pooling.md**: Default per-host limit stated explicitly (6 for HTTP/1.1, 1 multiplexed for HTTP/2)
- [x] No RFC references on any of these pages

### TASK-018-003: Split guide/advanced.md
**Description:** As a library user, I want the Channel API explained in the Getting Started guide where I first learn about TurboHttp, and custom stages explained in Architecture where they belong.

**Token Estimate:** ~30k tokens
**Predecessors:** none
**Successors:** TASK-018-007
**Parallel:** yes — can run alongside TASK-018-001, TASK-018-002, TASK-018-004, TASK-018-005

**Acceptance Criteria:**
- [ ] Channel API section (high-throughput, backpressure, batch pattern) moved into `guide/index.md` as a "High-Throughput Usage" section
- [ ] Custom Stage section moved into `architecture/pipeline.md` or a new `architecture/extending.md` with prerequisite callout ("Requires Akka.Streams knowledge")
- [ ] `advanced.md` file removed
- [ ] VitePress sidebar config updated — `advanced.md` removed, no broken links
- [ ] All cross-references from other pages updated (migration.md, troubleshooting.md link to advanced)
- [ ] Channel API explanation simplified: focus on "when and why" not Akka internals

### TASK-018-004: Revise architecture/pipeline.md and handlers.md
**Description:** As an interested user looking under the hood, I want architecture pages that explain what pipeline stages do for me, without engineering justifications or unexplained jargon.

**Token Estimate:** ~30k tokens
**Predecessors:** none
**Successors:** TASK-018-007
**Parallel:** yes — can run alongside TASK-018-001, TASK-018-002, TASK-018-003, TASK-018-005

**Acceptance Criteria:**
- [ ] **pipeline.md**: Request/Response chain table uses user-friendly names ("Request Enrichment", "Cookie Injection", "Cache Lookup") with stage class name in parentheses only
- [ ] **pipeline.md**: "MergePreferred" pattern replaced with plain explanation ("signal sent back to decide connection reuse")
- [ ] **handlers.md**: "Why a Standalone ClientBuilder Does Not Fit" section removed or reduced to one sentence
- [ ] **handlers.md**: `ITurboHttpClientBuilder` interface definition trimmed (remove `IServiceCollection Services` property)
- [ ] **handlers.md**: "ASYNC BOUNDARY" in diagram replaced with user-friendly note
- [ ] No engineering justifications ("we chose X because of Y internal constraint")

### TASK-018-005: Update LikeC4 diagrams
**Description:** As a documentation reader, I want the architecture diagrams to reflect the current codebase and stay at an appropriate conceptual level.

**Token Estimate:** ~35k tokens
**Predecessors:** none
**Successors:** TASK-018-006
**Parallel:** yes — can run alongside TASK-018-001, TASK-018-002, TASK-018-003, TASK-018-004
**Model:** opus

**Acceptance Criteria:**
- [ ] All LikeC4 model files (`model.c4`, `model-pipeline.c4`) reflect current codebase (new stages, renamed components)
- [ ] Streams Layer view reviewed — if individual stages add clutter, group by logical role (request chain, response chain, feedback)
- [ ] Pipeline Flow view reviewed — ensure labels describe user-visible behavior not internal mechanics
- [ ] SVGs regenerated via `npx likec4 export svg --output docs/public/diagrams docs/likec4`
- [ ] No broken diagram embeds in markdown pages
- [ ] Diagrams render correctly in dev server (`npm run docs:dev`)

### TASK-018-006: Regenerate SVG fallbacks and verify site build
**Description:** As a documentation maintainer, I want the static site to build cleanly with all diagram fallbacks up to date.

**Token Estimate:** ~15k tokens
**Predecessors:** TASK-018-005
**Successors:** TASK-018-007
**Parallel:** no — requires updated LikeC4 from TASK-018-005

**Acceptance Criteria:**
- [ ] `npm run docs:build` completes without errors
- [ ] All SVG fallbacks in `docs/public/diagrams/` are up to date
- [ ] No broken links in built site (check VitePress output for warnings)

### TASK-018-007: Final review and cross-reference check
**Description:** As a documentation reader, I want consistent navigation with no dead links or orphan pages after all changes.

**Token Estimate:** ~15k tokens
**Predecessors:** TASK-018-001, TASK-018-002, TASK-018-003, TASK-018-004, TASK-018-006
**Successors:** none
**Parallel:** no — final verification after all content tasks

**Acceptance Criteria:**
- [ ] VitePress sidebar in `config.ts` matches actual file structure (no `advanced.md` reference)
- [ ] All cross-page links verified (grep for `](./` and `](/` patterns)
- [ ] No RFC references anywhere in `docs/**/*.md`
- [ ] Consistent tone across all pages — spot-check 3 random pages
- [ ] `npm run docs:build` clean
- [ ] `npm run docs:dev` serves without errors

## Task Dependency Graph

```
TASK-018-001 ─────────────────────────────────────┐
TASK-018-002 ─────────────────────────────────────┤
TASK-018-003 ─────────────────────────────────────┤
TASK-018-004 ─────────────────────────────────────┼──→ TASK-018-007
TASK-018-005 ──→ TASK-018-006 ────────────────────┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-018-001 | ~25k | none | yes (with 002–005) | — |
| TASK-018-002 | ~40k | none | yes (with 001, 003–005) | — |
| TASK-018-003 | ~30k | none | yes (with 001, 002, 004, 005) | — |
| TASK-018-004 | ~30k | none | yes (with 001–003, 005) | — |
| TASK-018-005 | ~35k | none | yes (with 001–004) | opus |
| TASK-018-006 | ~15k | 005 | no | — |
| TASK-018-007 | ~15k | 001–004, 006 | no | — |

**Total estimated tokens:** ~190k

## Functional Requirements

- FR-1: Every guide page must explain features through user outcomes ("TurboHttp reconnects automatically") not implementation mechanics ("exponential backoff with jitter")
- FR-2: Architecture pages may use stage names but must explain each stage's user-visible purpose
- FR-3: No RFC numbers, section references, or spec language in any `docs/**/*.md` file
- FR-4: Channel API content must be accessible from the Getting Started guide
- FR-5: Custom Stage content must live in Architecture with a prerequisite callout
- FR-6: `advanced.md` must be removed with all references updated
- FR-7: LikeC4 diagrams must reflect current codebase components
- FR-8: VitePress site must build and serve without errors after all changes
- FR-9: All documentation in English (consistent language throughout)

## Non-Goals

- No new documentation pages (beyond potentially `architecture/extending.md`)
- No changes to `why/index.md` (already good)
- No changes to `api/index.md` (API reference is user-facing by nature)
- No changes to `guide/installation.md`, `guide/migration.md`, `guide/troubleshooting.md`, `guide/cookies.md`, `guide/content-encoding.md`, `guide/http2.md` (already rated GOOD)
- No restructuring of the VitePress site navigation beyond removing `advanced.md`
- No changes to the root `CLAUDE.md` or project code

## Technical Considerations

- VitePress config at `docs/.vitepress/config.ts` must be updated when `advanced.md` is removed
- LikeC4 SVG regeneration requires Node.js 20+ — build command: `npx likec4 export svg --output docs/public/diagrams docs/likec4`
- Cross-references between pages use relative markdown links (`./configuration.md`) — verify all after moves
- The `LikeC4Diagram.vue` component has SVG fallback — ensure fallback SVGs match interactive versions

## Success Metrics

- Zero RFC references in `docs/**/*.md` (verifiable via grep)
- All guide pages pass the "5-second test": a .NET developer can scan the page and know what the feature does for them
- `npm run docs:build` completes with zero errors/warnings
- No dead links in the built site

## Open Questions

_None — all questions resolved._
