# Iteration 05 — TASK-015: Kestrel Cache Routes

## Task
TASK-015: Add Kestrel fixture routes for caching scenarios so that integration tests can verify cache freshness, validation, and invalidation.

## Commands Run
1. `dotnet build --configuration Release src/TurboHttp.sln` → **0 errors, 2 pre-existing warnings** ✅

## Changes Made

### `src/TurboHttp.IntegrationTests/Shared/KestrelFixture.cs`
- Removed standalone `/cache/no-store` route from Phase 14 section (moved to shared method)
- Added `RegisterCacheRoutes(app)` call in `RegisterRoutes`
- Added `internal static void RegisterCacheRoutes(WebApplication app)` with 10 routes:
  - `GET /cache/max-age/{seconds}` — Cache-Control: max-age, body = ISO 8601 timestamp
  - `GET /cache/no-cache` — Cache-Control: no-cache
  - `GET /cache/no-store` — Cache-Control: no-store
  - `GET /cache/etag/{id}` — ETag header, If-None-Match → 304 support
  - `GET /cache/last-modified/{id}` — Last-Modified, If-Modified-Since → 304 support
  - `GET /cache/vary/{header}` — Vary header, body varies by header value
  - `GET /cache/must-revalidate` — max-age=0, must-revalidate with ETag validation
  - `GET /cache/s-maxage/{seconds}` — s-maxage directive
  - `GET /cache/expires` — Expires header (1 hour from now)
  - `GET /cache/private` — Cache-Control: private

### `src/TurboHttp.IntegrationTests/Shared/KestrelH2Fixture.cs`
- Added `KestrelFixture.RegisterCacheRoutes(app)` call in `RegisterRoutes`

## Acceptance Criteria
All 12 criteria met ✅. No deviations or skips.
