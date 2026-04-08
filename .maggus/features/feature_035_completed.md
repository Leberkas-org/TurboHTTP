<!-- maggus-id: 7b0dad3c-262e-475f-a18a-e2cc2d1cbb3c -->
# Feature 035: Corporate Identity Rename — TurboHttp → TurboHTTP

## Introduction

Rename the project identity from `TurboHttp` to `TurboHTTP` across the entire codebase. This is a Corporate Identity (CI) rename affecting folder names, solution file, project files, namespaces, using statements, documentation, CI workflows, agent definitions, and Obsidian notes.

**Key decision:** Public type names (e.g. `TurboHttpClient`, `TurboHttpException`, `TurboHttpMetrics`) keep their `TurboHttp` prefix for readability. Only namespaces, project names, folder names, and branding text change to `TurboHTTP`.

### Architecture Context

- **Components involved:** Every layer — Client, Streams, Protocol, Transport, Tests, Benchmarks, Docs, CI
- **Git challenge:** Windows case-insensitive filesystem requires two-step `git mv` via temp name (`TurboHttp` → `_TurboHTTP_tmp` → `TurboHTTP`)
- **Breaking change:** NuGet package ID changes from `TurboHttp` to `TurboHTTP`
- **Branch:** `feature/better-graph` (current branch)

### Scope Summary

| Category | What changes | Example |
|----------|-------------|---------|
| Folder names | `src/TurboHttp/` → `src/TurboHTTP/` | All 5 project folders |
| Solution file | `TurboHttp.sln` → `TurboHTTP.sln` | 1 file |
| csproj files | `TurboHttp.csproj` → `TurboHTTP.csproj` | 5 files + internal references |
| Namespaces | `namespace TurboHttp.*` → `namespace TurboHTTP.*` | ~537 files |
| Using statements | `using TurboHttp.*` → `using TurboHTTP.*` | ~423 files |
| InternalsVisibleTo | `TurboHttp.Tests` → `TurboHTTP.Tests` | 4 entries in main csproj |
| NuGet metadata | Title, PackageProjectUrl, RepositoryUrl | 1 csproj |
| CI workflows | Release names, URLs, badges | `build-and-release.yml` |
| Documentation | README, CONTRIBUTING, ARCHITECTURE, CLAUDE.md, docs site | ~20 files |
| Agent definitions | `.claude/agents/*.md` | 5 files |
| Obsidian notes | `notes/**/*.md` | ~15 files |
| Maggus features | `.maggus/features/*.md` | ~6 files |
| Config files | `.gitignore`, likec4, VitePress config | ~10 files |
| **NOT changed** | Public type names | `TurboHttpClient`, `TurboHttpException`, `TurboHttpMetrics` stay |

## Goals

- Consistent `TurboHTTP` branding across all project artifacts
- NuGet package published as `TurboHTTP`
- Build compiles and all tests pass after rename
- No functional changes — pure rename refactor

## Tasks

### TASK-035-001: Rename project folders via two-step git mv
**Description:** As a developer, I want all project folders renamed from `TurboHttp*` to `TurboHTTP*` so that the on-disk layout matches the new branding.

**Token Estimate:** ~15k tokens
**Predecessors:** none
**Successors:** TASK-035-002, TASK-035-003
**Parallel:** no — must complete before any file content changes

**Details:**
Each folder requires a two-step rename to work around Windows case-insensitive filesystem:
```bash
git mv src/TurboHttp src/_TurboHTTP_tmp && git mv src/_TurboHTTP_tmp src/TurboHTTP
git mv src/TurboHttp.Tests src/_TurboHTTP.Tests_tmp && git mv src/_TurboHTTP.Tests_tmp src/TurboHTTP.Tests
git mv src/TurboHttp.StreamTests src/_TurboHTTP.StreamTests_tmp && git mv src/_TurboHTTP.StreamTests_tmp src/TurboHTTP.StreamTests
git mv src/TurboHttp.IntegrationTests src/_TurboHTTP.IntegrationTests_tmp && git mv src/_TurboHTTP.IntegrationTests_tmp src/TurboHTTP.IntegrationTests
git mv src/TurboHttp.Benchmarks src/_TurboHTTP.Benchmarks_tmp && git mv src/_TurboHTTP.Benchmarks_tmp src/TurboHTTP.Benchmarks
```

Also rename the solution file:
```bash
git mv src/TurboHttp.sln src/_TurboHTTP_tmp.sln && git mv src/_TurboHTTP_tmp.sln src/TurboHTTP.sln
```

And rename csproj files inside each folder:
```bash
git mv src/TurboHTTP/TurboHttp.csproj src/TurboHTTP/_TurboHTTP_tmp.csproj && git mv src/TurboHTTP/_TurboHTTP_tmp.csproj src/TurboHTTP/TurboHTTP.csproj
# ... repeat for all 5 projects
```

**Acceptance Criteria:**
- [x] All 5 project folders renamed to `TurboHTTP*`
- [x] Solution file renamed to `TurboHTTP.sln`
- [x] All 5 csproj files renamed to `TurboHTTP*.csproj`
- [x] Git tracks all renames correctly (verify with `git status`)
- [x] No orphaned files or temp folders remain

### TASK-035-002: Update solution file and csproj references
**Description:** As a developer, I want the solution file and all csproj files to reference the new folder/project names so that `dotnet build` resolves paths correctly.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-035-001
**Successors:** TASK-035-003
**Parallel:** no — must complete before namespace changes

**Details:**
Update `src/TurboHTTP.sln`:
- All `Project(...)` lines: folder paths and project names (`TurboHttp` → `TurboHTTP`)

Update all 5 csproj files:
- `<ProjectReference>` paths (e.g. `..\TurboHttp\TurboHttp.csproj` → `..\TurboHTTP\TurboHTTP.csproj`)
- `<InternalsVisibleTo>` entries (`TurboHttp.Tests` → `TurboHTTP.Tests`, etc.)
- NuGet metadata in main csproj: `<Title>`, `<PackageProjectUrl>`, `<RepositoryUrl>`

Update `src/global.json` or `Directory.Build.props` if they contain `TurboHttp` references.

Update CI workflow: `PROJECT_PATH: './src/TurboHTTP.sln'`

**Acceptance Criteria:**
- [x] Solution file references all 5 projects with correct paths
- [x] All ProjectReference paths point to `TurboHTTP*` folders/files
- [x] InternalsVisibleTo entries use `TurboHTTP.*` assembly names
- [x] NuGet metadata shows `TurboHTTP` as title and in URLs
- [x] CI workflow `PROJECT_PATH` env var updated
- [x] `dotnet restore ./src/TurboHTTP.sln` succeeds

### TASK-035-003: Rename all C# namespaces and using statements
**Description:** As a developer, I want all `namespace TurboHttp*` and `using TurboHttp*` declarations updated to `TurboHTTP*` so that the code compiles under the new assembly names.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-035-002
**Successors:** TASK-035-004, TASK-035-005, TASK-035-006
**Parallel:** no — must complete before verification
**Model:** sonnet — high-volume mechanical text replacement

**Details:**
This is the largest task — ~537 files with `namespace TurboHttp` and ~423 files with `using TurboHttp`. Use bulk find-and-replace:

1. Replace `namespace TurboHttp` → `namespace TurboHTTP` in all `.cs` files
2. Replace `using TurboHttp` → `using TurboHTTP` in all `.cs` files
3. Replace `"TurboHttp` string literals (e.g. `ActivitySource("TurboHttp")`, `DiagnosticListener("TurboHttp")`) → `"TurboHTTP`
4. Replace `nameof(TurboHttp` if any exist

**Important:** Do NOT rename type names like `TurboHttpClient`, `TurboHttpException`, `TurboHttpMetrics` etc. — only namespace/using/string-literal references to the namespace.

Watch for edge cases:
- File-scoped namespaces (`namespace TurboHttp.Something;`)
- Block-scoped namespaces (`namespace TurboHttp.Something { }`)
- Global usings in `GlobalUsings.cs` or `Usings.cs` if they exist
- String literals containing namespace references (e.g. in test DisplayNames, diagnostic names)

**Acceptance Criteria:**
- [x] Zero occurrences of `namespace TurboHttp` remain (except inside type names)
- [x] Zero occurrences of `using TurboHttp` remain (except inside type names)
- [x] String literals for ActivitySource/DiagnosticListener updated
- [x] `dotnet build --configuration Release ./src/TurboHTTP.sln` succeeds with zero errors
- [x] All type names (`TurboHttpClient`, `TurboHttpException`, etc.) remain unchanged

### TASK-035-004: Update documentation files
**Description:** As a developer, I want all documentation to reference `TurboHTTP` consistently so that branding is uniform.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-035-003
**Successors:** TASK-035-007
**Parallel:** yes — can run alongside TASK-035-005 and TASK-035-006

**Details:**
Files to update (replace `TurboHttp` → `TurboHTTP` in branding/display text):

Root files:
- `README.md` (~24 occurrences)
- `CONTRIBUTING.md` (~11 occurrences)
- `ARCHITECTURE.md` (~18 occurrences)
- `CLAUDE.md` (~22 occurrences)

Docs site (`docs/`):
- `docs/.vitepress/config.ts` (~5 occurrences)
- `docs/index.md`, `docs/why/index.md`
- `docs/guide/*.md` (installation, configuration, http2, cookies, caching, retries, redirects, migration, troubleshooting, content-encoding, connection-pooling)
- `docs/architecture/*.md` (layers, pipeline, handlers, scenarios, extending)
- `docs/api/index.md`
- `docs/logo/logo.svg`, `docs/logo/logo_small.svg`
- `docs/.vitepress/components/LikeC4Diagram.vue`
- `docs/.vitepress/theme/custom.css`
- `docs/likec4/*.c4` (model, views, specification)
- `docs/CLAUDE.md`
- `docs/benchmarks/baseline_030.md` (~77 occurrences — benchmark results text)

**Important:** In documentation, `TurboHttpClient` (the class name) stays as-is, but "TurboHttp" as a product/project name becomes "TurboHTTP".

**Acceptance Criteria:**
- [x] All docs reference "TurboHTTP" as the project name
- [x] Code examples in docs use `TurboHTTP` namespace but `TurboHttpClient` type name
- [x] VitePress config uses `TurboHTTP`
- [x] SVG logos updated if they contain text "TurboHttp"
- [x] `docs/benchmarks/baseline_030.md` updated (display text only, not class names)


### TASK-035-005: Update agent definitions and maggus features
**Description:** As a developer, I want all agent definitions and feature plans to reference `TurboHTTP` so that tooling is consistent.

**Token Estimate:** ~15k tokens
**Predecessors:** TASK-035-003
**Successors:** TASK-035-007
**Parallel:** yes — can run alongside TASK-035-004 and TASK-035-006

**Details:**
Agent definitions (`.claude/agents/`):
- `akka-stage-builder.md` (~12 occurrences)
- `build-guardian.md` (~6 occurrences)
- `namespace-refactorer.md` (~22 occurrences)
- `spec-naming-validator.md` (~16 occurrences)
- `stage-port-validator.md` (~9 occurrences)

Maggus features (`.maggus/features/`):
- `feature_029.md` (~3 occurrences)
- `feature_030_completed.md` (~76 occurrences)
- `feature_031_completed.md` (~16 occurrences)
- `feature_032_completed.md` (~18 occurrences)
- `feature_033.md` (~28 occurrences)
- `feature_034.md` (~22 occurrences)

**Acceptance Criteria:**
- [x] All agent definitions reference `TurboHTTP` for project/namespace references
- [x] All feature plans updated
- [x] Agent file paths (e.g. `TurboHttp.Tests/`) reference new folder names

### TASK-035-006: Update Obsidian notes and misc config files
**Description:** As a developer, I want Obsidian notes, config files, and remaining references updated for consistency.

**Token Estimate:** ~20k tokens
**Predecessors:** TASK-035-003
**Successors:** TASK-035-007
**Parallel:** yes — can run alongside TASK-035-004 and TASK-035-005

**Details:**
Obsidian notes (`notes/`):
- `notes/VAULT_STYLE_GUIDE.md`
- `notes/Templates/RFC-Note.md`, `notes/Templates/RFC-Index.md`
- `notes/RFC/RFC9112/**/*.md`
- `notes/RFC/RFC9113/**/*.md`
- `notes/RFC/RFC9114/**/*.md`
- `notes/RFC/RFC9204/**/*.md`
- `notes/RFC/RFC9111/**/*.md`

Config files:
- `.gitignore` (1 occurrence)
- `docs/likec4/likec4.config.json`

**Important:** Use Obsidian MCP tools for notes/ files as per CLAUDE.md rules.

**Acceptance Criteria:**
- [x] All Obsidian notes reference `TurboHTTP` for project/namespace
- [x] `.gitignore` updated
- [x] likec4 config updated
- [x] No stale `TurboHttp` references remain outside of type names

### TASK-035-007: Full build and test verification
**Description:** As a developer, I want to verify the entire rename is correct by running a full build and test suite.

**Token Estimate:** ~10k tokens
**Predecessors:** TASK-035-004, TASK-035-005, TASK-035-006
**Successors:** none
**Parallel:** no — final verification gate

**Details:**
```bash
dotnet restore ./src/TurboHTTP.sln
dotnet build --configuration Release ./src/TurboHTTP.sln
dotnet test --project TurboHTTP.sln    # from src/ directory
```

Also verify:
- `git status` shows clean renames, no untracked temp files
- grep for remaining `TurboHttp` occurrences that aren't type names — should be zero
- CI workflow YAML is syntactically valid

**Acceptance Criteria:**
- [x] `dotnet restore` succeeds
- [x] `dotnet build --configuration Release` succeeds with zero errors
- [x] All unit tests pass (`TurboHTTP.Tests`)
- [x] All stream tests pass (`TurboHTTP.StreamTests`)
- [x] No remaining `TurboHttp` references outside of type names (class/method names)
- [x] `git status` shows only expected changes

## Task Dependency Graph

```
TASK-035-001 ──→ TASK-035-002 ──→ TASK-035-003 ──→ TASK-035-004 ──┐
                                                 ├→ TASK-035-005 ──┼→ TASK-035-007
                                                 └→ TASK-035-006 ──┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-035-001 | ~15k | none | no | — |
| TASK-035-002 | ~25k | 001 | no | — |
| TASK-035-003 | ~30k | 002 | no | sonnet |
| TASK-035-004 | ~25k | 003 | yes (with 005, 006) | sonnet |
| TASK-035-005 | ~15k | 003 | yes (with 004, 006) | sonnet |
| TASK-035-006 | ~20k | 003 | yes (with 004, 005) | sonnet |
| TASK-035-007 | ~10k | 004, 005, 006 | no | — |

**Total estimated tokens:** ~140k

## Functional Requirements

- FR-1: All folder names under `src/` must use `TurboHTTP` prefix
- FR-2: Solution file must be named `TurboHTTP.sln` and reference all projects correctly
- FR-3: All csproj files must be named `TurboHTTP*.csproj` with correct internal references
- FR-4: All C# namespaces must use `TurboHTTP` root (e.g. `TurboHTTP.Protocol.Http3`)
- FR-5: All using statements must reference `TurboHTTP.*` namespaces
- FR-6: NuGet package ID must be `TurboHTTP`
- FR-7: Public type names (`TurboHttpClient`, `TurboHttpException`, `TurboHttpMetrics`, etc.) must remain unchanged
- FR-8: CI workflows must use `TurboHTTP` in all display text, URLs, and badges
- FR-9: All documentation must reference "TurboHTTP" as the project name
- FR-10: Build must compile and all tests must pass after rename

## Non-Goals

- No renaming of public type names (classes, records, enums starting with `TurboHttp`)
- No functional code changes — this is purely a rename refactor
- No GitHub repository rename (that's a separate manual step)
- No NuGet package deprecation/redirect setup (handled separately)
- No changes to the `docs/benchmarks/baseline_030_raw.txt` raw benchmark output (601 occurrences — machine-generated)

## Technical Considerations

- **Windows case-insensitive FS:** Two-step `git mv` required for every rename. Must verify git tracks them as renames, not delete+add.
- **Build order:** Folder renames must happen before file content changes, otherwise paths break mid-edit.
- **String literals:** Some diagnostic strings (ActivitySource, DiagnosticListener, EventSource names) contain `"TurboHttp"` — these should become `"TurboHTTP"` since they're branding, not type names.
- **Benchmark raw output:** `docs/benchmarks/baseline_030_raw.txt` has 601 occurrences of `TurboHttp` — these are machine-generated benchmark results and should NOT be modified (listed in Non-Goals).
- **ARCHITECTURE.md needs update** after this feature to reflect new namespace naming.

## Success Metrics

- Zero occurrences of `TurboHttp` as namespace/project/folder name across the codebase
- Full green build + test suite
- NuGet package publishes as `TurboHTTP`
- All documentation consistently shows "TurboHTTP"

## Open Questions

None — all questions resolved during planning.
