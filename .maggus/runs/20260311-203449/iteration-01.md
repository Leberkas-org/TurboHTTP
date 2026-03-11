# Iteration 01 — TASK-017: Kestrel Connection Reuse Routes

## Task
TASK-017: Wire `RegisterConnectionReuseRoutes(app)` call into `KestrelFixture.RegisterRoutes()`.

## Commands & Outcomes
1. Read `KestrelFixture.cs` L980-1039 — confirmed `RegisterConnectionReuseRoutes` method exists with 4 routes.
2. Found `RegisterRoutes()` ends at L487 after `RegisterContentEncodingRoutes(app)`.
3. Added `RegisterConnectionReuseRoutes(app)` call at L489.
4. `dotnet build --configuration Release src/TurboHttp.sln` — 0 errors, 2 warnings (pre-existing).

## Acceptance Criteria
- [x] `RegisterConnectionReuseRoutes(app)` call added to `RegisterRoutes()`
- [x] Build succeeds with zero errors

## Deviations
None.
