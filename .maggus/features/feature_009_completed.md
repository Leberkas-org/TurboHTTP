# Feature 009: Akka Logging Bridge to Microsoft.Extensions.Logging

## Introduction

Bridge Akka.NET's internal ILoggingAdapter to Microsoft.Extensions.Logging (ILogger) so that all Akka actor and stream logging flows through the standard .NET logging pipeline. This enables consumers to use any ILogger provider (Serilog, NLog, console, Application Insights, etc.) without Akka-specific configuration.

### Architecture Context

- **Components involved:** Pooling layer (ConnectionActorBase, HostPool, Http*ConnectionActor), Transport layer (ClientByteMover), Hosting layer (DI registration)
- **Existing logging:** 41 files use Akka's `ILoggingAdapter` via `Context.GetLogger()` — these log warnings for reconnect failures, connection errors, stream read errors
- **No changes to Protocol/Client layer** — pure encoders/decoders that throw exceptions on errors
- **New dependency:** `Akka.Logger.Extensions.Logging` NuGet package

## Mandatory Testing Policy

> **Tests are required and must never be skipped, disabled, or marked as `[Skip]`.** Every acceptance criterion that specifies "All tests pass" is a hard gate — the feature is not complete until every test passes. No `[Fact(Skip = "...")]`, no `#if` guards, no `Assert.Skip()`. If a test fails, fix the code or the test — never suppress it.

## Goals

- All Akka logging output flows through ILogger without code changes in actors/stages
- Consumers can configure logging via standard `ILoggingConfiguration` / `AddLogging()` in DI
- Existing log messages (Warning, Info, Error) maintain their severity levels
- Zero behavioral change — logging bridge is transparent

## Tasks

### TASK-009-001: Add Akka.Logger.Extensions.Logging Package
**Description:** As a library author, I want to add the Akka logging bridge package so that Akka's logging integrates with Microsoft.Extensions.Logging.

**Token Estimate:** ~15k tokens
**Predecessors:** none
**Successors:** TASK-009-002
**Parallel:** yes — can run alongside nothing (dependency for 002)

**Acceptance Criteria:**
- [x] `Akka.Logger.Extensions.Logging` added to `Directory.Packages.props` with version matching Akka 1.5.62
- [x] `PackageReference` added to `TurboHttp.csproj`
- [x] `dotnet restore` succeeds
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with 0 errors

### TASK-009-002: Configure Logging Bridge in Hosting Layer
**Description:** As a library consumer, I want the logging bridge to be configured automatically when I register TurboHttp in DI.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-009-001
**Successors:** TASK-009-003
**Parallel:** no

**Acceptance Criteria:**
- [x] `TurboClientServiceCollectionExtensions.AddTurboHttpClient()` configures the Akka ActorSystem to use `LoggerFactory` from DI
- [x] Akka HOCON config includes `akka.loggers = ["Akka.Logger.Extensions.Logging.LoggingLogger, Akka.Logger.Extensions.Logging"]`
- [x] When `ILoggerFactory` is available in DI, Akka logs flow through it
- [x] When no `ILoggerFactory` is registered (standalone usage), fallback to Akka's default logger
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds
- [x] Existing unit tests and stream tests pass (no behavioral change)

### TASK-009-003: Integration Test for Logging Bridge
**Description:** As a library author, I want to verify the logging bridge works end-to-end in integration tests.

**Token Estimate:** ~20k tokens
**Predecessors:** TASK-009-002
**Successors:** none
**Parallel:** no

**Acceptance Criteria:**
- [~] ⚠️ BLOCKED: Test in integration test project that configures a test ILogger and verifies Akka log messages appear — `Akka.Logger.Extensions.Logging` 1.4.22 has a runtime `MissingMethodException` on `LogMessage.get_Args()` with Akka.NET 1.5.62; no compatible version exists yet
- [x] Or: verify that adding `ILoggerFactory` to the DI container in `ClientHelper` does not break existing smoke tests
- [x] All existing tests pass (`dotnet test src/TurboHttp.sln`)

## Task Dependency Graph

```
TASK-009-001 ──→ TASK-009-002 ──→ TASK-009-003
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-009-001 | ~15k | none | no | haiku |
| TASK-009-002 | ~30k | 001 | no | — |
| TASK-009-003 | ~20k | 002 | no | — |

**Total estimated tokens:** ~65k

## Functional Requirements

- FR-1: Akka ILoggingAdapter messages must appear in ILogger output with correct log level mapping
- FR-2: Akka Warning → ILogger Warning, Akka Info → ILogger Information, Akka Error → ILogger Error
- FR-3: Logger category must include actor path or stage name for traceability
- FR-4: No mandatory dependency on ILoggerFactory — graceful fallback

## Non-Goals

- No structured logging properties (Akka's ILoggingAdapter uses string formatting)
- No custom log event enrichment
- No log filtering configuration (consumer's responsibility)
- No changes to existing log messages in actors/stages

## Technical Considerations

- `Akka.Logger.Extensions.Logging` version must match `Akka` version (1.5.62) to avoid assembly conflicts
- The bridge requires `ILoggerFactory` to be passed to the ActorSystem — either via HOCON config or `BootstrapSetup`
- `ClientHelper` in integration tests creates its own `ActorSystem` — must be updated to pass through logging

## Success Metrics

- `dotnet test src/TurboHttp.sln` passes with 0 failures
- Akka log messages visible in ILogger output when configured
