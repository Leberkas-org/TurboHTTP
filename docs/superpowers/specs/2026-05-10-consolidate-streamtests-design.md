# Consolidate StreamTests into TurboHTTP.Tests

**Date:** 2026-05-10
**Status:** Draft

## Motivation

The `TurboHTTP.StreamTests` project (64 files) tests stage/stream composition for the same components as `TurboHTTP.Tests` (260 files). Both share infrastructure via `TurboHTTP.Tests.Shared`, use xUnit v3, and follow the same folder structure. The separation adds project maintenance overhead without meaningful benefit — the distinction between "unit" and "stage" tests is not strong enough to justify a separate project.

## Design

### File Migration

Move all source files from `TurboHTTP.StreamTests/` into `Stages/` subfolders within matching component folders in `TurboHTTP.Tests/`. Update namespaces to match the new location.

| Source (StreamTests/)      | Target (Tests/)               | New Namespace                               |
|----------------------------|-------------------------------|---------------------------------------------|
| `Caching/*.cs` (3)         | `Caching/Stages/*.cs`         | `TurboHTTP.Tests.Caching.Stages`            |
| `Cookies/*.cs` (1)         | `Cookies/Stages/*.cs`         | `TurboHTTP.Tests.Cookies.Stages`            |
| `Http10/*.cs` (4)          | `Http10/Stages/*.cs`          | `TurboHTTP.Tests.Http10.Stages`             |
| `Http11/*.cs` (5)          | `Http11/Stages/*.cs`          | `TurboHTTP.Tests.Http11.Stages`             |
| `Http2/*.cs` (9)           | `Http2/Stages/*.cs`           | `TurboHTTP.Tests.Http2.Stages`              |
| `Http3/*.cs` (3)           | `Http3/Stages/*.cs`           | `TurboHTTP.Tests.Http3.Stages`              |
| `Semantics/*.cs` (14)      | `Semantics/Stages/*.cs`       | `TurboHTTP.Tests.Semantics.Stages`          |
| `Streams/*.cs` (11)        | `Streams/Stages/*.cs`         | `TurboHTTP.Tests.Streams.Stages`            |
| `Streams/Lifecycle/*.cs` (3)| `Streams/Stages/Lifecycle/*.cs`| `TurboHTTP.Tests.Streams.Stages.Lifecycle` |

**Total:** 53 source files moved (excluding `obj/` generated files and `ModuleInit.cs`).

### Files Dropped

- `StreamTests/ModuleInit.cs` — identical to `Tests/ModuleInit.cs` (both call `TransportBuffer.ConfigurePoolSize(0)`)

### Project Changes

1. **Remove `TurboHTTP.StreamTests.csproj`** from the solution (`TurboHTTP.slnx`)
2. **Delete `TurboHTTP.StreamTests/`** folder after migration
3. **No changes to `TurboHTTP.Tests.csproj`** — it already references `TurboHTTP.Tests.Shared` which provides `StreamTestBase`, `EngineTestBase`, and all Akka.Streams test infrastructure
4. **Update `TurboHTTP.Tests.Shared.csproj`** — remove `TurboHTTP.StreamTests` from `InternalsVisibleTo`
5. **Update `TurboHTTP.csproj`** — remove `TurboHTTP.StreamTests` from `InternalsVisibleTo` (if present)

### Documentation Updates

- **CLAUDE.md**: Remove `dotnet test --project TurboHTTP.StreamTests` command, remove `spec-refactorer` references to StreamTests
- **Obsidian test conventions guide**: Update if it references StreamTests as a separate project

### Running Stage Tests After Merge

```bash
# All stage tests for a component
dotnet run --project TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -namespace "TurboHTTP.Tests.Http2.Stages"

# All stage tests
dotnet run --project TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -namespace "TurboHTTP.Tests" -filter "*.Stages.*"
```

## Out of Scope

- No changes to test logic or assertions
- No changes to `TurboHTTP.IntegrationTests` or `TurboHTTP.AcceptanceTests`
- No changes to `TurboHTTP.Tests.Shared` infrastructure (beyond InternalsVisibleTo cleanup)
- No renaming of test classes or methods

## Risks

- **Namespace collisions**: Unlikely — StreamTests class names include "Stage" or "Engine" suffixes that don't overlap with unit test names
- **Build time increase**: Marginal — 53 additional files in a 260-file project
- **Test isolation**: None lost — tests use shared static ActorSystem/Materializer already
