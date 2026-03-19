# Plan: Unified RFC DisplayName Standardization

## Introduction

All test `DisplayName` attributes across TurboHttp's three test projects are inconsistent — mixing legacy RFC numbers (7540, 7230), varying formats (`RFC-9111-§3.1` vs `7540-3.5-001` vs `RH-001`), and having ~30% of tests with no DisplayName at all. This plan standardizes every DisplayName to a single canonical format using current RFC numbers, and adds DisplayNames where an RFC reference is clearly applicable.

## Goals

- Replace all legacy RFC references: 7540→9113, 7230→9112, 7231→9110
- Unify all DisplayName attributes to one format: `RFC{N}-{section}-{CAT}-{NNN}: {description}`
- Add DisplayNames to tests that have a clear RFC section mapping but currently lack one
- Maintain parameter substitution style consistency per file
- Zero test breakage — DisplayName changes are metadata only

## RFC Number Migration Map

| Old RFC | New RFC | Domain |
|---------|---------|--------|
| RFC 7540 | RFC 9113 | HTTP/2 |
| RFC 7230 | RFC 9112 | HTTP/1.1 Message Syntax |
| RFC 7231 | RFC 9110 | HTTP Semantics |
| RFC 7232 | RFC 9110 | Conditional Requests |
| RFC 7233 | RFC 9110 | Range Requests |
| RFC 7234 | RFC 9111 | Caching |
| RFC 7235 | RFC 9110 | Authentication |
| RFC 1945 | RFC 1945 | HTTP/1.0 (no successor, stays) |
| RFC 7541 | RFC 7541 | HPACK (no successor, stays) |
| RFC 6265 | RFC 6265 | Cookies (no successor, stays) |

## Target Format

```
RFC{number}-{section}-{CAT}-{NNN}: {description}
```

**Examples:**
- `RFC9113-3.4-CP-001: Client preface starts with exact magic octets`
- `RFC9112-4-RL-001: Request-line uses HTTP/1.1`
- `RFC7541-2.3-ST-001: Static table contains exactly 61 entries`
- `RFC9110-15.4-RH-001: IsRedirect returns true for redirect status codes`
- `RFC9111-5.2-CC-001: null input returns null`
- `RFC1945-5.1-RL-001: GET request produces correct request-line`

**Rules:**
- `{number}` = current RFC number (no hyphens in RFC prefix: `RFC9113` not `RFC-9113`)
- `{section}` = RFC section number (e.g. `3.4`, `5.2`, `15.4`; omit if no clear mapping)
- `{CAT}` = 2-3 letter uppercase category code (keep existing where sensible)
- `{NNN}` = 3-digit zero-padded sequential number within category
- Parameter substitution: keep existing style per file (`[{method}]` or `{0}`) — just make it consistent within each file

## User Stories

### TASK-001: RFC 9113 — Replace 7540 references (28 files, ~514 DisplayNames)
**Description:** As a developer reading test output, I want HTTP/2 tests to reference the current RFC 9113 so that section lookups match the current specification.

**Acceptance Criteria:**
- [x] All `7540-` prefixes in DisplayName replaced with `RFC9113-`
- [x] Format unified to `RFC9113-{section}-{CAT}-{NNN}: {description}`
- [x] Category codes preserved where already 2-3 chars (CP, SP, GA, RST, etc.)
- [x] Shorthand prefixes like `enc5-ph-` and `enc5-set-` converted to `RFC9113-{section}-{CAT}-{NNN}`
- [x] Files affected: all 28 files in `src/TurboHttp.Tests/RFC9113/`
- [x] `dotnet test --filter "FullyQualifiedName~RFC9113"` passes with 0 failures
- [x] No non-DisplayName code changes (test logic untouched)

### TASK-002: RFC 9112 — Replace 7230 references & unify format (26 files, ~278 DisplayNames)
**Description:** As a developer, I want HTTP/1.1 tests to reference RFC 9112 consistently so that mixed RFC7230/RFC9112 citations disappear.

**Acceptance Criteria:**
- [x] All `RFC7230-` references in DisplayName replaced with `RFC9112-`
- [x] Bare `RFC9112-4` format extended to `RFC9112-{section}-{CAT}-{NNN}: {description}`
- [x] Category codes assigned where missing (e.g. RL=RequestLine, HH=HostHeader, HD=Headers, CN=Connection, BD=Body, RR=RangeRequest, SL=StatusLine, CH=Chunked, NB=NoBody, FG=Fragmentation, MT=Methods, PL=Pipelining, CR=ConnectionReuse, PH=PerHostLimiter)
- [x] Legacy/preserved files (Http11DecoderChunkExtensionTests, Http11NegativePathTests, Http11SecurityTests) also updated
- [x] `dotnet test --filter "FullyQualifiedName~RFC9112"` passes with 0 failures

### TASK-003: RFC 1945 — Unify format & add missing DisplayNames (17 files, ~153 existing)
**Description:** As a developer, I want HTTP/1.0 tests to follow the same `RFC1945-{section}-{CAT}-{NNN}` format, including tests that currently lack DisplayNames.

**Acceptance Criteria:**
- [x] Existing DisplayNames like `enc1-m-001` converted to `RFC1945-{section}-{CAT}-{NNN}`
- [x] DisplayNames added to files 04 (Security) and 05 (Integration) where RFC section is clear
- [x] Category codes: RL=RequestLine, HD=Headers, BD=Body, SC=Security, IT=Integration, SL=StatusLine, CN=Connection, FG=Fragmentation, ST=State, MT=Methods, SC=StatusCodes, RT=RoundTrip
- [x] `dotnet test --filter "FullyQualifiedName~RFC1945"` passes with 0 failures

### TASK-004: RFC 7541 — Unify HPACK format (6 files, ~228 DisplayNames)
**Description:** As a developer, I want HPACK tests to use the canonical `RFC7541-{section}-{CAT}-{NNN}` format consistently.

**Acceptance Criteria:**
- [x] Bare category codes like `ST-001`, `DT-001` get RFC prefix: `RFC7541-2.3-ST-001`
- [x] Mixed prefixes like `7541-st-001`, `hpack-int-001` converted to standard format
- [x] HpackTests.cs — tests without DisplayName get one added where RFC section is clear
- [x] Section mapping: Static Table=2.3, Dynamic Table=2.3.2, Huffman=5, Integer Repr=5.1, Header Block=4
- [x] `dotnet test --filter "FullyQualifiedName~RFC7541"` passes with 0 failures

### TASK-005: RFC 9110 — Unify Semantics format (3 files, ~104 DisplayNames)
**Description:** As a developer, I want HTTP Semantics tests (redirects, retries, content encoding) to use the `RFC9110-{section}-{CAT}-{NNN}` format.

**Acceptance Criteria:**
- [ ] `RH-001` becomes `RFC9110-15.4-RH-001: ...`
- [ ] `RE-001` (retry) becomes `RFC9110-9.2-RE-001: ...`
- [ ] Content encoding tests (01-03 files) get DisplayNames where RFC section is clear
- [ ] Any `RFC7231-` or `RFC7232-` references migrated to `RFC9110-`
- [ ] `dotnet test --filter "FullyQualifiedName~RFC9110"` passes with 0 failures

### TASK-006: RFC 9111 — Unify Caching format (5 files, ~67 DisplayNames)
**Description:** As a developer, I want caching tests to use `RFC9111-{section}-{CAT}-{NNN}` instead of `RFC-9111-§5.2`.

**Acceptance Criteria:**
- [ ] `RFC-9111-§5.2` becomes `RFC9111-5.2-CC-001` (no hyphens in RFC prefix, no §)
- [ ] Category codes: CC=CacheControl, CF=CacheFreshness, CR=ConditionalRequest, CS=CacheStore, CI=CacheIntegration
- [ ] `dotnet test --filter "FullyQualifiedName~RFC9111"` passes with 0 failures

### TASK-007: RFC 6265 — Add RFC prefix to Cookies (1 file, ~42 DisplayNames)
**Description:** As a developer, I want cookie tests to include the RFC prefix for consistency.

**Acceptance Criteria:**
- [ ] `CM-001` becomes `RFC6265-5.3-CM-001: ...` (§5.3 = Storage Model)
- [ ] Domain matching (CM-036+) gets section `5.1.3`, path matching gets `5.1.4`
- [ ] `dotnet test --filter "FullyQualifiedName~RFC6265"` passes with 0 failures

### TASK-008: StreamTests — Unify format where RFC-applicable (~200+ DisplayNames)
**Description:** As a developer, I want StreamTests that test RFC-defined behavior to use the same `RFC{N}-{section}-{CAT}-{NNN}` format. Implementation-only tests (ConnectionActor, HostPoolActor, etc.) keep their current category-based format.

**Acceptance Criteria:**
- [ ] Protocol stage tests (Http10/, Http11/, Http20/) get RFC-prefixed DisplayNames
- [ ] Infrastructure tests (IO/, Stages/) keep `{CAT}-{NNN}` format (no RFC prefix) — these are implementation tests
- [ ] Streams/ tests (Redirect, Retry, Cache, Cookie, etc.) get RFC prefix where applicable
- [ ] `dotnet test --filter "FullyQualifiedName~StreamTests"` passes with 0 failures
- [ ] Add DisplayNames only where RFC reference is clearly identifiable

### TASK-009: IntegrationTests — Add DisplayNames to Kestrel fixtures
**Description:** As a developer, I want any integration tests to follow the naming convention.

**Acceptance Criteria:**
- [ ] Survey `src/TurboHttp.IntegrationTests/` for test methods
- [ ] Add `RFC{N}-{section}-{CAT}-{NNN}` DisplayNames where RFC behavior is being tested
- [ ] Skip if no actual test classes exist yet (currently fixtures only)
- [ ] Build passes

### TASK-010: Validation — Full build + test run
**Description:** Final validation that all changes are metadata-only and no tests broke.

**Acceptance Criteria:**
- [ ] `dotnet build --configuration Release ./src/TurboHttp.sln` — 0 errors
- [ ] `dotnet test ./src/TurboHttp.sln` — all tests pass (currently 2,111)
- [ ] Same test count before and after (no tests accidentally deleted or duplicated)
- [ ] Run build-guardian agent for RFC coverage report

## Functional Requirements

- FR-1: Every `DisplayName` attribute must start with `RFC{number}-` where an RFC section is identifiable
- FR-2: Legacy RFC numbers (7540, 7230, 7231, 7232, 7233, 7234, 7235) must not appear anywhere in DisplayName strings
- FR-3: Format must be `RFC{N}-{section}-{CAT}-{NNN}: {description}` — no `§`, no extra hyphens in RFC prefix
- FR-4: Category codes must be 2-3 uppercase letters, consistent within each file
- FR-5: Sequential numbering (NNN) must be zero-padded 3-digit, no gaps within a file
- FR-6: Parameter substitution style must be consistent within each file
- FR-7: No test logic, assertions, or method signatures may change — only `DisplayName` string values
- FR-8: Implementation-only tests (IO actors, buffer stages) may use `{CAT}-{NNN}` without RFC prefix

## Non-Goals

- No renaming of test method names (only DisplayName attributes change)
- No renaming of test files or classes
- No reorganization of test folder structure
- No changes to test logic, assertions, or setup
- No adding of new tests
- No changes to production code
- No updating RFC_COVERAGE.md (separate task)

## Technical Considerations

- This is a pure metadata refactor — only string literals inside `DisplayName = "..."` change
- Can be parallelized by RFC folder (TASK-001 through TASK-007 are independent)
- TASK-008 (StreamTests) depends on category codes established in TASK-001–007
- Each task should be validated independently with `dotnet test --filter`
- Total estimated: ~1,386 DisplayName edits + ~100-200 new DisplayNames added

## Success Metrics

- 0 legacy RFC numbers (7540, 7230, 7231) in any DisplayName string
- 100% of RFC-related tests follow `RFC{N}-{section}-{CAT}-{NNN}: {description}` format
- All 2,111+ tests still pass
- `dotnet test` output shows clean, sortable, RFC-traceable test names

## Open Questions

- Should RFC_COVERAGE.md be updated to reflect the new DisplayName format? (deferred — separate task)
- Should a CI lint rule enforce the DisplayName format going forward?
