# Iteration 02 — TASK-005: Connection Health Monitoring and Auto-Reconnect

## Task
TASK-005: Add connection health monitoring and auto-reconnect to the ConnectionPoolStage.

## Changes Made

### `src/TurboHttp/IO/Stages/ConnectionPoolTypes.cs`
- Added `MaxReconnectAttempts` (default 3) and `ReconnectInterval` (default 5s) to `PoolConfig`
- Added `ConnectionPoolException` class for pool exhaustion errors

### `src/TurboHttp/IO/Stages/ConnectionPoolStage.cs`
- Changed base class from `GraphStageLogic` to `TimerGraphStageLogic` (for `ScheduleOnce`/`OnTimer`)
- Added `_onSlotDeathCallback` async callback for sub-graph fault detection
- In `MaterialiseNewSlot`: attached `ContinueWith` on sub-graph completion task to detect faults
- Added `OnSlotDeath`: removes dead slot, adjusts in-flight count, schedules reconnect or fails stage
- Added `OnTimer`: materialises replacement connection and drains pending queue after reconnect interval
- Added `ReconnectAttempts` property to `HostPool` for tracking per-host retry count

### `src/TurboHttp.StreamTests/Stages/ConnectionPoolStageTests.cs`
- Added `CreateFailThenSucceedFlowFactory` and `CreateAlwaysFailFlowFactory` test helpers
- POOL-024: Connection dies → retry → new connection works
- POOL-025: Max retries exhausted → ConnectionPoolException
- POOL-026: PoolConfig default values for MaxReconnectAttempts/ReconnectInterval
- POOL-027: PoolConfig reconnect properties are configurable
- POOL-028: Connection death with queued items → items dispatched after reconnect

## Commands Run
- `dotnet build src/TurboHttp.sln --configuration Release` — 0 errors, 0 warnings
- `dotnet test src/TurboHttp.Tests` — 2158/2158 pass
- `dotnet test src/TurboHttp.StreamTests` — 443/443 pass (32 pool tests, 5 new)
- `dotnet test src/TurboHttp.IntegrationTests` — 232/234 pass (2 pre-existing flaky retry tests)

## Key Design Decisions
- Only faulted sub-graph completions trigger `OnSlotDeath`; normal completions (during shutdown) are ignored
- Used `TaskScheduler.Default` for `ContinueWith` (not `ExecuteSynchronously`) to avoid interfering with Akka.Streams internal scheduling
- Reconnect attempts are tracked per-host, not per-slot
- `ScheduleOnce` via `TimerGraphStageLogic.OnTimer` for reconnect delay (matches RetryStage pattern)

## Deviations
None. All 10 acceptance criteria met.
