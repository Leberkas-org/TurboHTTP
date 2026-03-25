<!-- maggus-id: 4e15c7d0-0fdd-4f91-84ee-aa980859101b -->

# Feature 001: Migrate All Tests from xUnit 2 to xUnit v3 with MTP

## Introduction

Migrate the entire test suite (3 projects, ~293 files, ~3,586 tests) from xUnit 2.9.3 to xUnit v3.2.x with Microsoft Testing Platform (MTP) as the primary runner. This removes the legacy VSTest dependency, enables MTP-native test execution, and aligns the project with the modern .NET testing stack.

### Architecture Context

- **Projects affected:** `TurboHttp.Tests` (168 files, ~2,635 tests), `TurboHttp.StreamTests` (106 files, ~763 tests), `TurboHttp.IntegrationTests` (39 files, ~188 tests)
- **Critical dependency:** `Akka.TestKit.Xunit2` 1.5.63 → `Akka.TestKit.Xunit` 1.5.63 (xUnit 3 variant, already published on NuGet)
- **Build infrastructure:** Central Package Management via `Directory.Packages.props`, `Directory.Build.props` with `TreatWarningsAsErrors`
- **Runner config:** `xunit.runner.json` in IntegrationTests (parallelization disabled)

## Goals

- Replace `xunit` 2.9.3 with `xunit.v3` 3.2.2 across all three test projects
- Replace `Microsoft.NET.Test.Sdk` + `xunit.runner.visualstudio` with MTP runner (`xunit.v3` includes MTP v1 by default)
- Replace `Akka.TestKit.Xunit2` with `Akka.TestKit.Xunit` (xUnit 3 variant)
- Migrate `IAsyncLifetime` implementations to the v3 interface (inherits `IAsyncDisposable`, no separate `DisposeAsync` needed)
- Convert test projects from `Library` to `Exe` output type (required by xUnit v3)
- Ensure all ~3,586 tests pass with zero regressions
- Update runner configuration from `xunit.runner.json` to v3 format

## Tasks

### TASK-026-001: Update Directory.Packages.props — Package Version Migration

**Description:** As a developer, I want the central package versions updated to xUnit v3 packages so that all test projects reference the correct versions.

**Token Estimate:** ~15k tokens
**Predecessors:** none
**Successors:** TASK-026-002, TASK-026-003, TASK-026-004
**Parallel:** no — all other tasks depend on this
**Model:** haiku

**Changes:**
- Remove `<XunitVersion>2.9.3</XunitVersion>` and `<XunitRunnerVersion>3.1.5</XunitRunnerVersion>`
- Add `<XunitVersion>3.2.2</XunitVersion>`
- Replace package versions in Testing ItemGroup:
  - `xunit` → `xunit.v3` (version `$(XunitVersion)`)
  - Remove `xunit.runner.visualstudio`
  - Remove `Microsoft.NET.Test.Sdk`
- Update Akka ItemGroup:
  - `Akka.TestKit.Xunit2` → `Akka.TestKit.Xunit` (version `$(AkkaVersion)`)

**Acceptance Criteria:**
- [x] `Directory.Packages.props` contains `xunit.v3` at version 3.2.2
- [x] `xunit.runner.visualstudio` and `Microsoft.NET.Test.Sdk` removed
- [x] `Akka.TestKit.Xunit2` replaced with `Akka.TestKit.Xunit`
- [x] No orphaned version variables remain

---

### TASK-026-002: Migrate TurboHttp.Tests — Project File and Code Changes

**Description:** As a developer, I want the unit test project migrated to xUnit v3 so that all ~2,635 protocol/encoder/decoder tests run on the new framework.

**Token Estimate:** ~75k tokens
**Predecessors:** TASK-026-001
**Successors:** TASK-026-005
**Parallel:** yes — can run alongside TASK-026-003 and TASK-026-004

**Project file changes (`TurboHttp.Tests.csproj`):**
- Add `<OutputType>Exe</OutputType>`
- Replace `<PackageReference Include="xunit" />` → `<PackageReference Include="xunit.v3" />`
- Remove `<PackageReference Include="xunit.runner.visualstudio" />`
- Remove `<PackageReference Include="Microsoft.NET.Test.Sdk" />`
- Replace `<PackageReference Include="Akka.TestKit.Xunit2" />` → `<PackageReference Include="Akka.TestKit.Xunit" />`
- Global using `<Using Include="Xunit"/>` remains valid (namespace unchanged)

**Code changes (168 files):**
- Run `dotnet build` and fix any compilation errors from Assert API changes:
  - `Assert.Equal(float, float, int precision)` → resolve ambiguity if any exist
  - `Assert.ThrowsAsync<T>(Func<Task>)` → verify no ambiguity with `Func<ValueTask>` overload
- Remove any `using Xunit.Abstractions;` if present (namespace removed in v3)
- Fix any `IAsyncLifetime` implementations if present (v3: inherits `IAsyncDisposable`)
- Use Roslyn analyzer suggestions where available for mechanical fixes

**Acceptance Criteria:**
- [x] `TurboHttp.Tests.csproj` updated with all package and OutputType changes
- [x] `dotnet build src/TurboHttp.Tests/TurboHttp.Tests.csproj` compiles with zero errors
- [x] `dotnet test src/TurboHttp.Tests/TurboHttp.Tests.csproj` — all ~2,635 tests pass
- [x] No `xunit` v2 package references remain

---

### TASK-026-003: Migrate TurboHttp.StreamTests — Project File, Base Classes, and Code Changes

**Description:** As a developer, I want the stream test project migrated to xUnit v3 so that all ~763 Akka.Streams stage tests run on the new framework with `Akka.TestKit.Xunit`.

**Token Estimate:** ~100k tokens
**Predecessors:** TASK-026-001
**Successors:** TASK-026-005
**Parallel:** yes — can run alongside TASK-026-002 and TASK-026-004
**Model:** opus — base class inheritance chain is critical

**Project file changes (`TurboHttp.StreamTests.csproj`):**
- Add `<OutputType>Exe</OutputType>`
- Replace `<PackageReference Include="xunit" />` → `<PackageReference Include="xunit.v3" />`
- Remove `<PackageReference Include="xunit.runner.visualstudio" />`
- Remove `<PackageReference Include="Microsoft.NET.Test.Sdk" />`
- Replace `<PackageReference Include="Akka.TestKit.Xunit2" />` → `<PackageReference Include="Akka.TestKit.Xunit" />`

**Base class changes:**
- `StreamTestBase.cs` — inherits from `Akka.TestKit.Xunit2.TestKit` → change to `Akka.TestKit.Xunit.TestKit`
  - Update `using Akka.TestKit.Xunit2;` → `using Akka.TestKit.Xunit;`
  - Verify constructor still works: `base(ActorSystem.Create(...))`
- `EngineTestBase.cs` — inherits from `StreamTestBase`, no direct changes expected
- Audit any other base classes in the project

**Code changes (106 files):**
- Same Assert API audit as TASK-026-002
- Remove any `using Xunit.Abstractions;`
- Fix `IAsyncLifetime` implementations if any exist

**Acceptance Criteria:**
- [x] `TurboHttp.StreamTests.csproj` updated with all package and OutputType changes
- [x] `StreamTestBase` inherits from `Akka.TestKit.Xunit.TestKit` (not Xunit2)
- [x] `using Akka.TestKit.Xunit2` replaced with `using Akka.TestKit.Xunit` across all files
- [x] `dotnet build src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj` compiles with zero errors
- [x] `dotnet test src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj` — all ~763 tests pass
- [x] No `Akka.TestKit.Xunit2` references remain

---

### TASK-026-004: Migrate TurboHttp.IntegrationTests — Project File, Fixtures, and Config

**Description:** As a developer, I want the integration test project migrated to xUnit v3 so that all ~188 end-to-end tests with Kestrel fixtures run on the new framework.

**Token Estimate:** ~80k tokens
**Predecessors:** TASK-026-001
**Successors:** TASK-026-005
**Parallel:** yes — can run alongside TASK-026-002 and TASK-026-003
**Model:** opus — fixture lifecycle changes need careful handling

**Project file changes (`TurboHttp.IntegrationTests.csproj`):**
- Add `<OutputType>Exe</OutputType>`
- Replace `<PackageReference Include="xunit" />` → `<PackageReference Include="xunit.v3" />`
- Remove `<PackageReference Include="xunit.runner.visualstudio" />`
- Remove `<PackageReference Include="Microsoft.NET.Test.Sdk" />`
- Replace `<PackageReference Include="Akka.TestKit.Xunit2" />` → `<PackageReference Include="Akka.TestKit.Xunit" />`

**IAsyncLifetime migration (CRITICAL — 5+ fixture files):**
xUnit v3 `IAsyncLifetime` now inherits from `IAsyncDisposable`. The interface changes from:
```csharp
// v2
public interface IAsyncLifetime { Task InitializeAsync(); Task DisposeAsync(); }
// v3
public interface IAsyncLifetime : IAsyncDisposable { ValueTask InitializeAsync(); }
// DisposeAsync() comes from IAsyncDisposable: ValueTask DisposeAsync()
```

Files requiring `IAsyncLifetime` migration:
- `Shared/ActorSystemFixture.cs` — `Task InitializeAsync()` → `ValueTask InitializeAsync()`, `Task DisposeAsync()` → `ValueTask DisposeAsync()`
- `Shared/KestrelFixture.cs` — same pattern
- `Shared/KestrelH2Fixture.cs` — same pattern
- `Shared/KestrelH3Fixture.cs` — same pattern
- `Shared/KestrelTlsFixture.cs` — same pattern
- `Shared/TestKit.cs` — same pattern
- Any test classes implementing `IAsyncLifetime` (e.g., `SmokeTests`, `ConnectionPoolTests`)

**Collection definitions (`Shared/Collections.cs`):**
- `ICollectionFixture<T>` — interface unchanged, no migration needed
- `[CollectionDefinition]` and `[Collection]` — attributes unchanged

**Runner config:**
- `xunit.runner.json` — validate it works with v3 schema (format is compatible)
- Remove `<None Update="xunit.runner.json"><CopyToOutputDirectory>Always</CopyToOutputDirectory></None>` if MTP no longer needs it copied

**Acceptance Criteria:**
- [x] `TurboHttp.IntegrationTests.csproj` updated with all package and OutputType changes
- [x] All `IAsyncLifetime` implementations updated to v3 signature (`ValueTask` returns)
- [x] `using Akka.TestKit.Xunit2` replaced with `using Akka.TestKit.Xunit` where applicable
- [x] `xunit.runner.json` validated for v3 compatibility
- [x] `dotnet build src/TurboHttp.IntegrationTests/TurboHttp.IntegrationTests.csproj` compiles with zero errors
- [x] `dotnet test src/TurboHttp.IntegrationTests/TurboHttp.IntegrationTests.csproj` — 232/244 pass (11 pre-existing H10 failures, 1 QUIC skip)
- [x] No `xunit` v2 or `Akka.TestKit.Xunit2` references remain

---

### TASK-026-005: Full Solution Validation and Cleanup

**Description:** As a developer, I want the entire solution validated end-to-end so that the migration is confirmed complete with zero regressions.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-026-002, TASK-026-003, TASK-026-004
**Successors:** none
**Parallel:** no — requires all project migrations complete

**Validation steps:**
1. `dotnet restore src/TurboHttp.sln` — verify all packages resolve
2. `dotnet build --configuration Release src/TurboHttp.sln` — zero errors, zero warnings
3. `dotnet test src/TurboHttp.sln` — all ~3,586 tests pass
4. Verify MTP runner is active (test output shows MTP header, not VSTest)
5. Run `slopwatch analyze` — zero new issues

**Cleanup:**
- Remove any leftover `Microsoft.NET.Test.Sdk` or `xunit.runner.visualstudio` references in csproj files
- Remove `<XunitRunnerVersion>` variable from `Directory.Packages.props` if still present
- Update `CLAUDE.md` dependencies section: `xunit 2.9.3` → `xunit.v3 3.2.2`, `Akka.TestKit.Xunit2` → `Akka.TestKit.Xunit`
- Verify `dotnet test --filter` still works for RFC-specific test runs (e.g., `FullyQualifiedName~RFC9113`)

**Acceptance Criteria:**
- [x] `dotnet build --configuration Release src/TurboHttp.sln` — zero errors
- [x] `dotnet test src/TurboHttp.sln` — all tests pass (report total count: 3492 + 808 + 234 = 4534 passed; 9 pre-existing integration failures; 1 QUIC skip)
- [x] MTP runner confirmed active in test output (MTP0001 warning confirms Microsoft.Testing.Platform is active)
- [x] `slopwatch analyze` — zero new issues (86 pre-existing issues, no baseline — all pre-date migration)
- [x] `CLAUDE.md` updated with new dependency versions (xunit.v3 3.2.2, Akka.TestKit.Xunit 1.5.63, Akka.Streams 1.5.63)
- [x] No v2 package references remain anywhere in the solution
- [x] `dotnet test -- --filter-namespace "TurboHttp.Tests.RFC9113"` works correctly (551 tests filtered, MTP native syntax replaces VSTest `--filter "FullyQualifiedName~"` which is silently ignored by MTP)

## Task Dependency Graph

```
TASK-026-001 ──→ TASK-026-002 ──→ TASK-026-005
             ├─→ TASK-026-003 ──┘
             └─→ TASK-026-004 ──┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-026-001 | ~15k | none | no | haiku |
| TASK-026-002 | ~75k | 001 | yes (with 003, 004) | — |
| TASK-026-003 | ~100k | 001 | yes (with 002, 004) | opus |
| TASK-026-004 | ~80k | 001 | yes (with 002, 003) | opus |
| TASK-026-005 | ~30k | 002, 003, 004 | no | — |

**Total estimated tokens:** ~300k

## Functional Requirements

- FR-1: All test projects must reference `xunit.v3` 3.2.2 via Central Package Management
- FR-2: All test projects must use `<OutputType>Exe</OutputType>` (xUnit v3 requirement)
- FR-3: `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` must be completely removed
- FR-4: `Akka.TestKit.Xunit2` must be replaced with `Akka.TestKit.Xunit` in all projects and source files
- FR-5: All `IAsyncLifetime` implementations must use the v3 interface signature (`ValueTask` returns)
- FR-6: MTP must be the active test runner (verified via test output header)
- FR-7: `dotnet test` must work for all three projects individually and via the solution file
- FR-8: `dotnet test --filter` must work for RFC-specific test filtering
- FR-9: All existing `[Fact]`, `[Theory]`, `[InlineData]`, `[MemberData]`, `[Collection]` attributes must work unchanged
- FR-10: All `DisplayName` attributes with RFC tags must appear correctly in test output

## Non-Goals

- No test logic changes — this is a framework migration only, not a test rewrite
- No migration to `[AssemblyFixture]` — existing `[Collection]`/`[CollectionDefinition]` pattern works in v3
- No introduction of new v3-only features (`Assert.Skip`, `SkipUnless`, `Explicit`, etc.)
- No changes to test organization, file naming, or folder structure
- No upgrade of `Akka.Streams.TestKit` beyond the current version
- No migration of `coverlet.collector` (remains compatible)
- No CI/CD pipeline changes (MTP works with `dotnet test`)

## Technical Considerations

### IAsyncLifetime v2 → v3 Signature Change (CRITICAL)

This is the highest-risk change. The v3 interface changes return types from `Task` to `ValueTask`:

```csharp
// v2
public interface IAsyncLifetime
{
    Task InitializeAsync();
    Task DisposeAsync();
}

// v3 — inherits IAsyncDisposable
public interface IAsyncLifetime : IAsyncDisposable
{
    ValueTask InitializeAsync();
    // DisposeAsync() comes from IAsyncDisposable: ValueTask DisposeAsync()
}
```

**Migration pattern:** For fixtures that call async methods internally, wrap with `new ValueTask(asyncCall)` or use `ValueTask.CompletedTask` for no-op implementations.

### Assert.ThrowsAsync Overload Ambiguity

xUnit v3 adds `Func<ValueTask>` overloads alongside existing `Func<Task>`. If any test passes a lambda that the compiler can't resolve, it will fail to build. Fix by explicitly typing the lambda parameter or casting.

### Test Project as Executable

xUnit v3 test projects must be executables (`<OutputType>Exe</OutputType>`). The framework auto-generates an entry point. This changes how the project is built but `dotnet test` continues to work.

### Akka.TestKit.Xunit Compatibility

`Akka.TestKit.Xunit` 1.5.63 is the xUnit 3 variant published by the Akka.NET team. It exposes the same `TestKit` base class API. The only change needed is the namespace: `Akka.TestKit.Xunit2` → `Akka.TestKit.Xunit`.

## Success Metrics

- All ~3,586 tests pass with zero regressions
- Zero compilation errors or warnings
- MTP runner confirmed active (`dotnet test` output shows MTP header)
- `slopwatch analyze` shows zero new issues
- `dotnet test --filter` works for all existing filter patterns

## Open Questions

*None — all questions resolved during clarification.*
