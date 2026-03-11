# Iteration 04 — TASK-033: Http20Engine Multiplexing Integration Tests

## Task
**ID:** TASK-033
**Title:** Http20Engine Multiplexing Integration Tests

## Commands Run
1. `dotnet build src/TurboHttp.IntegrationTests/TurboHttp.IntegrationTests.csproj --configuration Release` → **Success** (0 errors, 1 pre-existing warning)
2. `dotnet test --filter "FullyQualifiedName~Http20MultiplexTests" --configuration Release` → **6/6 passed** (1.87s)

## What Was Done
- Created `src/TurboHttp.IntegrationTests/Http20/02_Http20MultiplexTests.cs` with 6 tests:
  - **20E-INT-010**: Two concurrent GETs on same connection both succeed
  - **20E-INT-011**: 10 parallel GETs all return expected body
  - **20E-INT-012**: Interleaved responses arrive for distinct endpoints
  - **20E-INT-013**: Client stream IDs are odd (RFC 9113 §5.1.1)
  - **20E-INT-014**: MAX_CONCURRENT_STREAMS — multiple requests within server limit
  - **20E-INT-015**: Slow response does not block fast response
- Added `SendManyAsync` helper using `Source.Queue` with capacity > 1 and `ConcurrentBag` + `TaskCompletionSource` for collecting multiplexed responses
- Updated plan_2.md acceptance criteria to [x]

## Acceptance Criteria
- [x] File created: `src/TurboHttp.IntegrationTests/Http20/02_Http20MultiplexTests.cs`
- [x] 6 tests: concurrent on same conn, 10 parallel GETs, interleaved ordering, odd stream IDs, MAX_CONCURRENT_STREAMS, slow doesn't block fast
- [x] All tests pass: `dotnet test --filter "FullyQualifiedName~Http20MultiplexTests"`

## Deviations
None. All acceptance criteria met.
