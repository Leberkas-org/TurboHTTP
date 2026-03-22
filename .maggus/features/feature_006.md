# Feature 006: Unified Stage Port Naming Convention

## Introduction

GraphStage port strings in TurboHttp use inconsistent prefixes: HTTP/2 stages use abbreviated `H2` (e.g., `"H2Connection.In.Server"`), HTTP/3 stages use `H3` (e.g., `"H3Correlation.In.Request"`), and HTTP/1.x uses `H1X` — while the actual class names use `Http20`, `Http30`, `Http1X`. The CLAUDE.md convention requires port strings to match the class name minus the `Stage` suffix.

### Architecture Context (from CLAUDE.md)

- **Convention affected**: "Stage Inlet/Outlet Naming" section in CLAUDE.md
- **Components**: `TurboHttp/Streams/Stages/` (Encoding, Decoding, Routing)
- **No logic changes**: Purely cosmetic string refactoring
- **Port strings are internal**: They appear only in Akka logs and debugging, not in the public API

## Goals

- All stage port strings follow the convention: class name minus `Stage` suffix
- Zero inconsistencies between class names and port string prefixes
- `stage-port-validator` agent reports no violations

## Tasks

### TASK-006-001: Update port strings in HTTP/2 and HTTP/1.x stages
**Description:** As a developer, I want HTTP/2 and HTTP/1.x stage port strings to consistently use `Http20` and `Http1X` as prefixes so the naming convention is uniform.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-006-003
**Parallel:** yes — can run alongside TASK-006-002

**Acceptance Criteria:**
- [x] `Http20ConnectionStage`: `"H2Connection.*"` → `"Http20Connection.*"` (5 ports)
- [x] `Http20StreamStage`: `"H2Stream.*"` → `"Http20Stream.*"` (2 ports)
- [x] `Http20CorrelationStage`: `"H2Correlation.*"` → `"Http20Correlation.*"` (3 ports)
- [x] `Http1XCorrelationStage`: `"H1XCorrelation.*"` → `"Http1XCorrelation.*"` (5 ports)
- [x] `dotnet build --configuration Release src/TurboHttp.sln` — 0 errors

### TASK-006-002: Update port strings in HTTP/3 stages
**Description:** As a developer, I want HTTP/3 stage port strings to consistently use `Http30` as prefix so the naming convention is uniform.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-006-003
**Parallel:** yes — can run alongside TASK-006-001

**Acceptance Criteria:**
- [x] `Http30ConnectionStage`: `"H3Connection.*"` → `"Http30Connection.*"` (4 ports)
- [x] `Http30CorrelationStage`: `"H3Correlation.*"` → `"Http30Correlation.*"` (3 ports)
- [x] `Http30StreamIdAllocatorStage`: `"H3StreamIdAllocator.*"` → `"Http30StreamIdAllocator.*"` (2 ports)
- [x] `Http30ControlStreamPrefaceStage`: `"H3ControlPreface.*"` → `"Http30ControlStreamPreface.*"` (2 ports)
- [x] `Http30QpackEncoderPrefaceStage`: `"H3QpackEncoderPreface.*"` → `"Http30QpackEncoderPreface.*"` (2 ports)
- [x] `dotnet build --configuration Release src/TurboHttp.sln` — 0 errors

### TASK-006-003: Update tests and validation gate
**Description:** As a developer, I want to ensure all test assertions and the stage-port-validator pass after the rename.

**Token Estimate:** ~15k tokens
**Predecessors:** TASK-006-001, TASK-006-002
**Successors:** none
**Parallel:** no

**Acceptance Criteria:**
- [ ] `04_Http30ConnectionStageTests.cs`: 4 port name assertions updated (`"H3Connection.*"` → `"Http30Connection.*"`)
- [ ] Grep for `"H[123]\w+\.(In|Out)` in production code returns 0 matches
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` — 0 errors, 0 warnings
- [ ] `dotnet test src/TurboHttp.sln` — all tests green
- [ ] `stage-port-validator` agent — no violations

## Task Dependency Graph

```
TASK-006-001 ──→ TASK-006-003
TASK-006-002 ──┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-006-001 | ~25k | none | yes (with 002) | — |
| TASK-006-002 | ~25k | none | yes (with 001) | — |
| TASK-006-003 | ~15k | 001, 002 | no | — |

**Total estimated tokens:** ~65k

## Functional Requirements

- FR-1: Every stage port string must follow the pattern `ClassNameWithoutStage.Direction[.Role]`
- FR-2: No two stages may share the same port string (globally unique)
- FR-3: C# field names (`_in`, `_out`, `_inRole`, `_outRole`) remain unchanged

## Non-Goals

- No class name changes (e.g., `Http20ConnectionStage` stays as is)
- No C# field name changes (`_inServer`, `_outStream` etc.)
- No logic changes in stages
- No changes to Feature/BidiStage port strings (already correct)

## Technical Considerations

- Port strings are purely for logging/debugging — no wire format impact
- Akka.Streams graph composition is compile-time checked: mismatched port connections cause build errors
- `TreatWarningsAsErrors` is globally enabled — all warnings are errors

## Success Metrics

- 0 inconsistent port string prefixes (verified by stage-port-validator)
- All tests green
- CLAUDE.md convention fully adhered to

## Open Questions

_None — all questions resolved._
