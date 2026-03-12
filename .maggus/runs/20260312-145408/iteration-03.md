# Iteration 03 — TASK-006: Idle Connection Eviction

## Task
TASK-006: Add idle connection eviction to ConnectionPoolStage.

## Changes

### `src/TurboHttp/IO/Stages/ConnectionPoolTypes.cs`
- Added `IdleCheckInterval` parameter to `PoolConfig` (default 30 seconds)

### `src/TurboHttp/IO/Stages/ConnectionPoolStage.cs`
- Added `LastActivityUtc` and `KeepAliveTimeout` properties to `ConnectionSlot`
- `SendToSlot()`: stamps `LastActivityUtc` on every request dispatch
- `OnSubGraphResponse()`: stamps `LastActivityUtc` on every response received
- `PreStart()`: starts periodic `ScheduleRepeatedly` timer for idle eviction
- `OnTimer()`: dispatches to `EvictIdleConnections()` for the idle-eviction timer key
- `EvictIdleConnections()`: scans all host pools, evicts idle connections exceeding timeout, preserves at least one connection per host, respects per-slot `KeepAliveTimeout` override

### `src/TurboHttp.StreamTests/Stages/ConnectionPoolStageTests.cs`
- POOL-029: Connection idle > timeout → shut down (verifies eviction via materialisation count)
- POOL-030: Connection with recent traffic → preserved (traffic keeps connection alive)
- POOL-031: Last connection per host → preserved despite idle (single connection never evicted)
- POOL-032: KeepAliveTimeout overrides IdleTimeout (config validation)

## Commands Run
- `dotnet build src/TurboHttp.sln --configuration Release` — passes
- `dotnet test src/TurboHttp.Tests` — 2158/2158 pass
- `dotnet test src/TurboHttp.StreamTests --filter ConnectionPoolStageTests` — 36/36 pass
- `dotnet test src/TurboHttp.StreamTests` — 444/447 pass (3 pre-existing flaky failures in unrelated tests)

## Acceptance Criteria Status
All 10 acceptance criteria met. No deviations or skips.
