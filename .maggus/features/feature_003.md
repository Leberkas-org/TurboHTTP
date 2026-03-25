<!-- maggus-id: 20260325-125500-feature-003 -->

# Feature 003: Migrate to Microsoft Testing Platform V2

## Introduction

The project already partially uses MTP via `TestingPlatformDotnetTestSupport=true` and xUnit v3 (`OutputType=Exe`), but still runs through the VSTest bridge. This feature completes the migration to the native MTP V2 runner, replaces `coverlet.collector` with Microsoft's built-in `Microsoft.Testing.Extensions.CodeCoverage`, and updates the GitHub Actions pipeline so that coverage (Cobertura) and test results (TRX via MTP native `--report-trx`) are cleanly produced, with an HTML coverage report uploaded as a build artifact.

Additionally, `TurboHttp.IntegrationTests` is excluded from the default `dotnet test` run (solution-level) because integration tests require running Kestrel fixtures and are slow/environment-dependent. They can still be run explicitly via `dotnet test src/TurboHttp.IntegrationTests/...`. The CI pipeline runs unit/stream tests in the main step and integration tests in a separate optional step.

### Architecture Context

- **Components involved:** All 3 test `.csproj` files, `Directory.Packages.props` (CPM), `Directory.Build.props`, `.github/workflows/build-and-release.yml`
- **No production code changes** — this is purely test infrastructure and CI pipeline
- **New dependencies:** `Microsoft.Testing.Extensions.CodeCoverage`, `dotnet-reportgenerator-globaltool` (dotnet tool)

## Goals

- Switch from VSTest bridge to native MTP V2 runner (`UseMicrosoftTestingPlatformRunner=true`)
- Replace `coverlet.collector` with `Microsoft.Testing.Extensions.CodeCoverage` across all test projects
- Output coverage in Cobertura format
- Generate HTML coverage report via ReportGenerator and upload as CI artifact
- Use MTP native `--report-trx` for test result output (replaces `--logger trx`)
- All existing tests continue to pass with zero functional changes
- Exclude `TurboHttp.IntegrationTests` from default `dotnet test` at solution level
- Provide explicit opt-in command to run integration tests separately
- CI pipeline runs unit/stream tests by default, integration tests in a separate step
- Pipeline stays green on `main` and PRs

## Tasks

### TASK-003-001: Switch test projects to native MTP V2 runner and exclude IntegrationTests from default run
**Description:** As a developer, I want all test projects to use the native MTP V2 runner instead of the VSTest bridge, and I want IntegrationTests excluded from the default `dotnet test` at solution level, so that a plain `dotnet test src/TurboHttp.sln` only runs fast unit/stream tests.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-003-002, TASK-003-003
**Parallel:** no — foundation for all subsequent tasks

**Acceptance Criteria:**
- [ ] In `src/Directory.Build.props`: add `<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>` to a conditional PropertyGroup that applies only to test projects (condition: `'$(IsPackable)' == 'false'` or explicit list)
- [ ] Remove `<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>` from all 3 test `.csproj` files (superseded by the new property)
- [ ] Add `<IsTestProject>false</IsTestProject>` to `TurboHttp.IntegrationTests.csproj` — this tells `dotnet test` at the solution level to skip this project. The project still builds and can be tested explicitly via `dotnet test src/TurboHttp.IntegrationTests/TurboHttp.IntegrationTests.csproj`
- [ ] Verify `<OutputType>Exe</OutputType>` remains in all test projects (required for MTP)
- [ ] `dotnet test src/TurboHttp.sln --configuration Release` runs only Tests + StreamTests (IntegrationTests is NOT executed)
- [ ] `dotnet test src/TurboHttp.IntegrationTests/TurboHttp.IntegrationTests.csproj --configuration Release` still runs integration tests when invoked explicitly
- [ ] All existing tests pass — zero failures, zero skipped-that-weren't-before
- [ ] Build succeeds with zero errors (IntegrationTests still compiles, just doesn't run via solution-level test)

### TASK-003-002: Replace coverlet with Microsoft.Testing.Extensions.CodeCoverage
**Description:** As a developer, I want to use Microsoft's built-in code coverage extension instead of coverlet, so that coverage collection is natively integrated with MTP V2 and doesn't rely on a third-party collector.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-003-001
**Successors:** TASK-003-004
**Parallel:** yes — can run alongside TASK-003-003

**Acceptance Criteria:**
- [ ] Remove `coverlet.collector` from `Directory.Packages.props` (`<PackageVersion Include="coverlet.collector" .../>`)
- [ ] Remove `<PackageReference Include="coverlet.collector" />` from all 3 test `.csproj` files
- [ ] Add `Microsoft.Testing.Extensions.CodeCoverage` to `Directory.Packages.props` with a pinned version (latest stable for .NET 10)
- [ ] Add `<PackageReference Include="Microsoft.Testing.Extensions.CodeCoverage" />` to Tests and StreamTests `.csproj` files (IntegrationTests gets it too for explicit runs)
- [ ] Verify coverage collection works locally: `dotnet test src/TurboHttp.sln --collect "Code Coverage;Format=cobertura"` produces `.cobertura.xml` files in the results directory
- [ ] Coverage output is Cobertura XML format (not OpenCover)
- [ ] Build succeeds with zero errors and zero new warnings

### TASK-003-003: Switch to MTP native TRX reporting
**Description:** As a developer, I want test results reported via MTP's native `--report-trx` flag instead of the legacy `--logger trx`, so that test result generation is fully integrated with the MTP V2 pipeline.

**Token Estimate:** ~15k tokens
**Predecessors:** TASK-003-001
**Successors:** TASK-003-004
**Parallel:** yes — can run alongside TASK-003-002

**Acceptance Criteria:**
- [ ] Add `Microsoft.Testing.Extensions.TrxReport` to `Directory.Packages.props` with a pinned version
- [ ] Add `<PackageReference Include="Microsoft.Testing.Extensions.TrxReport" />` to all 3 test `.csproj` files
- [ ] Verify locally: `dotnet test src/TurboHttp.sln --report-trx` produces `.trx` files in the results directory
- [ ] TRX files contain all test results (passed, failed, skipped) with correct counts
- [ ] Build succeeds with zero errors

### TASK-003-004: Add ReportGenerator as local dotnet tool
**Description:** As a developer, I want ReportGenerator available as a local dotnet tool, so that the CI pipeline can generate HTML coverage reports from Cobertura XML without installing global tools.

**Token Estimate:** ~10k tokens
**Predecessors:** TASK-003-002, TASK-003-003
**Successors:** TASK-003-005
**Parallel:** no — needs coverage format confirmed first

**Acceptance Criteria:**
- [ ] `dotnet tool install dotnet-reportgenerator-globaltool` added to `.config/dotnet-tools.json` (via `dotnet tool install --local dotnet-reportgenerator-globaltool`)
- [ ] Verify locally: after running tests with coverage, `dotnet tool run reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:"coveragereport" -reporttypes:"Html"` produces an HTML report
- [ ] HTML report opens in browser and shows per-assembly, per-class, per-method coverage breakdown
- [ ] Tool version is pinned in `dotnet-tools.json`
- [ ] `dotnet tool restore` restores it cleanly

### TASK-003-005: Update GitHub Actions pipeline
**Description:** As a developer, I want the `build-and-release.yml` workflow to use the new MTP V2 test command with native coverage and TRX reporting, and to generate + upload an HTML coverage report as a build artifact.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-003-004
**Successors:** TASK-003-006
**Parallel:** no — needs all prior tasks complete
**Model:** opus — CI pipeline changes are high-risk and must be precise

**Acceptance Criteria:**
- [ ] `Run tests` step updated:
  - Replace `--collect:"XPlat Code Coverage;Format=opencover"` with `--collect "Code Coverage;Format=cobertura"`
  - Replace `--logger trx` with `--report-trx`
  - Keep `--results-directory ${{ env.TEST_OUTPUT_DIRECTORY }}`
  - Keep `--configuration Release --no-build`
  - Keep `--verbosity normal`
- [ ] New step `Generate coverage report` added after test step:
  - Runs `dotnet tool run reportgenerator -reports:"${{ env.TEST_OUTPUT_DIRECTORY }}/**/coverage.cobertura.xml" -targetdir:"${{ env.TEST_OUTPUT_DIRECTORY }}/coveragereport" -reporttypes:"Html;TextSummary"`
  - Uses `TextSummary` to print coverage % in the CI log
- [ ] New step `Print coverage summary` added:
  - `cat ${{ env.TEST_OUTPUT_DIRECTORY }}/coveragereport/Summary.txt` to show coverage in the build log
- [ ] New step `Upload coverage report` added:
  - Uses `actions/upload-artifact@v6`
  - Uploads `${{ env.TEST_OUTPUT_DIRECTORY }}/coveragereport/` as artifact named `coverage-report`
- [ ] New step `Upload test results` added:
  - Uses `actions/upload-artifact@v6`
  - Uploads `${{ env.TEST_OUTPUT_DIRECTORY }}/**/*.trx` as artifact named `test-results`
- [ ] `Run tests` step now only runs unit + stream tests (IntegrationTests excluded via `IsTestProject=false`)
- [ ] New step `Run integration tests` added after the main test step:
  - Runs `dotnet test src/TurboHttp.IntegrationTests/TurboHttp.IntegrationTests.csproj --configuration Release --no-build --report-trx --results-directory ${{ env.TEST_OUTPUT_DIRECTORY }}/integration`
  - Uses `continue-on-error: true` so integration test failures don't block the build (they are informational on CI)
- [ ] `dotnet tool restore` step already exists and will pick up ReportGenerator
- [ ] Pipeline runs successfully on a test branch push (or local `act` if available)
- [ ] Coverage HTML artifact is downloadable from the GitHub Actions run page
- [ ] TRX artifact is downloadable from the GitHub Actions run page (includes both unit and integration results)

### TASK-003-006: Verify full pipeline end-to-end
**Description:** As a developer, I want to verify the complete migration works locally and in CI, ensuring zero regressions in test execution, coverage collection, and artifact generation.

**Token Estimate:** ~20k tokens
**Predecessors:** TASK-003-005
**Successors:** none
**Parallel:** no — final validation gate

**Acceptance Criteria:**
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` — zero errors, zero new warnings
- [ ] `dotnet test src/TurboHttp.sln --configuration Release --collect "Code Coverage;Format=cobertura" --report-trx --results-directory ./testresults` — runs only Tests + StreamTests (IntegrationTests excluded)
- [ ] `dotnet test src/TurboHttp.IntegrationTests/TurboHttp.IntegrationTests.csproj --configuration Release --report-trx` — integration tests pass when run explicitly
- [ ] Cobertura XML files exist in `./testresults/**/coverage.cobertura.xml`
- [ ] TRX files exist in `./testresults/**/*.trx`
- [ ] `dotnet tool run reportgenerator -reports:"./testresults/**/coverage.cobertura.xml" -targetdir:"./testresults/coveragereport" -reporttypes:"Html;TextSummary"` — produces HTML report
- [ ] HTML report shows coverage for `TurboHttp` assembly (not test assemblies)
- [ ] No `coverlet.collector` references remain anywhere in the solution
- [ ] No `TestingPlatformDotnetTestSupport` references remain in any `.csproj`
- [ ] `UseMicrosoftTestingPlatformRunner=true` is set correctly
- [ ] `IsTestProject=false` is set in IntegrationTests csproj
- [ ] CLAUDE.md build commands section is updated to reflect new test flags and integration test exclusion

## Task Dependency Graph

```
TASK-003-001 ──→ TASK-003-002 ──→ TASK-003-004 ──→ TASK-003-005 ──→ TASK-003-006
             └──→ TASK-003-003 ──┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-003-001 | ~25k | none | no | — |
| TASK-003-002 | ~25k | 001 | yes (with 003) | — |
| TASK-003-003 | ~15k | 001 | yes (with 002) | — |
| TASK-003-004 | ~10k | 002, 003 | no | — |
| TASK-003-005 | ~35k | 004 | no | opus |
| TASK-003-006 | ~20k | 005 | no | — |

**Total estimated tokens:** ~130k

## Functional Requirements

- FR-1: All test projects must use the native MTP V2 runner — no VSTest bridge fallback
- FR-2: Code coverage must be collected via `Microsoft.Testing.Extensions.CodeCoverage` in Cobertura format
- FR-3: Test results must be produced via MTP native `--report-trx` (not `--logger trx`)
- FR-4: CI pipeline must generate an HTML coverage report via ReportGenerator and upload it as a downloadable artifact
- FR-5: CI pipeline must upload TRX test result files as a downloadable artifact
- FR-6: Coverage summary (line/branch %) must be printed in the CI build log via `TextSummary`
- FR-7: ReportGenerator must be available as a local dotnet tool (restored via `dotnet tool restore`)
- FR-8: All 3 test projects (Tests, StreamTests, IntegrationTests) must be migrated to MTP V2 identically
- FR-9: `dotnet test src/TurboHttp.sln` must NOT run IntegrationTests — they are excluded via `<IsTestProject>false</IsTestProject>`
- FR-10: Integration tests must remain runnable via explicit project path: `dotnet test src/TurboHttp.IntegrationTests/TurboHttp.IntegrationTests.csproj`
- FR-11: CI pipeline must run integration tests in a separate step with `continue-on-error: true` (informational, not blocking)

## Non-Goals

- No Codecov or external coverage service integration (explicitly deferred — artifact-only)
- No PR coverage comments or badges
- No coverage thresholds or quality gates (no `--minimum-coverage` enforcement)
- No changes to test code itself — purely infrastructure
- No GitHub Pages coverage hosting
- No changes to the `codeql.yml` or `docs.yml` workflows

## Technical Considerations

- **MTP V2 + xUnit v3 compatibility:** xUnit v3 is built natively on MTP. Setting `UseMicrosoftTestingPlatformRunner=true` tells `dotnet test` to use the MTP host directly. The `TestingPlatformDotnetTestSupport=true` property is the older VSTest bridge — it must be removed to avoid conflicts.
- **Microsoft.Testing.Extensions.CodeCoverage** uses Microsoft's native code coverage engine (same as Visual Studio Enterprise). It supports Cobertura output natively via `Format=cobertura`. This replaces coverlet's `XPlat Code Coverage` collector.
- **`--report-trx`** is an MTP V2 built-in flag that requires `Microsoft.Testing.Extensions.TrxReport` as a package reference. It replaces the VSTest-era `--logger trx`.
- **ReportGenerator** is a well-established tool that reads Cobertura XML and produces HTML, TextSummary, and many other formats. Adding it as a local tool via `dotnet-tools.json` ensures CI and local dev use the same version.
- **Shared test properties:** Instead of repeating `UseMicrosoftTestingPlatformRunner` in each csproj, consider adding it to `Directory.Build.props` with a condition. The cleanest approach is a condition on `'$(IsPackable)' == 'false'` since all test projects set `IsPackable=false` and the main project doesn't. Alternatively, create a `src/tests/Directory.Build.props` if test projects are ever moved to a subdirectory.
- **Coverage exclusions:** The Microsoft coverage extension may collect coverage for test assemblies too. ReportGenerator's `-assemblyfilters:+TurboHttp;-TurboHttp.Tests;-TurboHttp.StreamTests;-TurboHttp.IntegrationTests` flag should be used to filter the HTML report to production code only.
- **CI artifact size:** HTML coverage reports are typically 1-5 MB. Artifact retention defaults to 90 days on GitHub Actions.
- **IntegrationTests exclusion:** Setting `<IsTestProject>false</IsTestProject>` in the IntegrationTests csproj is the cleanest approach. It prevents `dotnet test` at solution level from discovering the project as a test project, while `dotnet build` still compiles it. The project remains fully functional when targeted explicitly. This is the MSBuild-recommended way to exclude projects from solution-level test runs.
- **Integration tests in CI:** Running with `continue-on-error: true` ensures that integration test failures (which may be environment-dependent — Kestrel, TLS certs, port availability) don't fail the build. The TRX results are still uploaded as artifacts for debugging.

## Success Metrics

- Zero `coverlet.collector` references in the solution
- Zero `TestingPlatformDotnetTestSupport` references in any csproj
- `dotnet test` output shows MTP V2 runner (no VSTest references)
- CI pipeline produces downloadable coverage HTML artifact
- CI pipeline produces downloadable TRX test results artifact
- Coverage summary printed in CI log with line and branch percentages
- All existing tests pass — zero regressions
- `dotnet test src/TurboHttp.sln` skips IntegrationTests (only unit + stream tests run)
- Integration tests still runnable and passing via explicit project path

## Open Questions

*None — all design decisions resolved.*
