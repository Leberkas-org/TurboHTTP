# Iteration 04 — TASK-014: Kestrel Retry Routes

## Task
TASK-014: Add Kestrel fixture routes for retry scenarios.

## Changes Made

### `src/TurboHttp.IntegrationTests/Shared/KestrelFixture.cs`
- Added `using System.Collections.Concurrent` import
- Added `_retryCounters` static `ConcurrentDictionary<string, int>` for stateful succeed-after-N tracking
- Added `RegisterRetryRoutes(WebApplication app)` internal static method with 6 routes:
  - `GET /retry/408` → 408 Request Timeout
  - `GET /retry/503` → 503 Service Unavailable
  - `GET /retry/503-retry-after/{seconds}` → 503 with Retry-After header (seconds format)
  - `GET /retry/503-retry-after-date` → 503 with Retry-After as HTTP-date (10s from now)
  - `GET /retry/succeed-after/{n}` → stateful: fail N-1 times with 503, then 200 "success"
  - `POST /retry/non-idempotent-503` → 503 on POST
- Called `RegisterRetryRoutes(app)` from `RegisterRoutes()`

### `src/TurboHttp.IntegrationTests/Shared/KestrelH2Fixture.cs`
- Added `KestrelFixture.RegisterRetryRoutes(app)` call in `RegisterRoutes()`

## Commands Run
- `dotnet build --configuration Release src/TurboHttp.sln` → 0 errors, 2 pre-existing warnings

## Acceptance Criteria
- [x] Routes added to both `KestrelFixture` and `KestrelH2Fixture`
- [x] `GET /retry/408` — responds with 408 Request Timeout
- [x] `GET /retry/503` — responds with 503 Service Unavailable
- [x] `GET /retry/503-retry-after/{seconds}` — 503 with Retry-After header
- [x] `GET /retry/503-retry-after-date` — 503 with Retry-After as HTTP-date
- [x] `GET /retry/succeed-after/{n}` — fail first N-1 times with 503, then 200 (stateful)
- [x] `POST /retry/non-idempotent-503` — 503 on POST (should NOT retry)
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors

## Deviations
None.
