# Iteration 02 — TASK-002: Per-Host Connection Lifecycle

## Task
**TASK-002**: Per-Host Connection Lifecycle — First Connection via ConnectionStage

## Changes Made

### `src/TurboHttp/IO/Stages/ConnectionPoolStage.cs`
- Replaced `HostRegistration` with `HostPool` (TcpOptions, List<ConnectionSlot>, ConnectionCounter)
- Added `ConnectionSlot` (ISourceQueueWithComplete, Task completion, Active, Idle, PendingRequestCount)
- Changed factory type from `Func<ConnectionStage>` to `Func<IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed>>` for testability
- Implemented `RouteDataItem` — materializes sub-graph on first DataItem via `Source.Queue` → connection flow → `Sink.ForEach`
- Sub-graph materialization uses `Materializer` property (SubFusingActorMaterializer) from GraphStageLogic
- Added `GetAsyncCallback` bridge for thread-safe response delivery from sub-graph to stage logic
- Added `_inFlightCount` tracking for correct stream completion (don't complete while responses pending)
- PostStop completes all sub-graph queues

### `src/TurboHttp.StreamTests/Stages/ConnectionPoolStageTests.cs`
- Added `CreateEchoFlow()` — fake ConnectionStage that filters ConnectItems and echoes DataItems
- Updated `CreateStage()` to use echo flow factory
- Updated POOL-007 to verify response is produced (was previously no-response in TASK-001)
- Added POOL-009: single-host single-connection roundtrip with payload verification
- Added POOL-010: multiple DataItems for same host reuse connection slot

## Commands Run
- `dotnet build src/TurboHttp.sln --configuration Release` — ✅ 0 errors, 0 warnings
- `dotnet test src/TurboHttp.StreamTests/ --filter ConnectionPoolStageTests` — ✅ 10/10 pass
- `dotnet test src/TurboHttp.StreamTests/` — ✅ 421/421 pass
- `dotnet test src/TurboHttp.Tests/` — ✅ 2158/2158 pass

## Acceptance Criteria
All 11 criteria met. No deviations or skips.

## Notes
- Factory type generalized from `Func<ConnectionStage>` to `Func<IGraph<FlowShape<...>, NotUsed>>` to enable test doubles (echo flow). Real ConnectionStage still satisfies this interface since `GraphStage<TShape>` implements `IGraph<TShape, NotUsed>`.
- Stream completion logic uses `_inFlightCount` to prevent premature CompleteStage when sub-graph responses are still in flight.
