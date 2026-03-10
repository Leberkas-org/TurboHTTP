# Iteration 02 — TASK-11-05: Http11 Stage — Fragmentation & Reassembly

## Task
**ID:** TASK-11-05
**Title:** Http11 Stage — Fragmentation & Reassembly
**File:** `src/TurboHttp.StreamTests/Http11/Http11StageFragmentationTests.cs`

## Commands Run
1. `dotnet build src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj --configuration Release` → 0 errors, 2 warnings (pre-existing)
2. `dotnet test ... --filter "FullyQualifiedName~Http11StageFragmentation"` → **11 passed, 0 failed**

## Acceptance Criteria
- [x] `11F-001`: Chunked response over 4 TCP segments → correctly reassembled (2 tests)
- [x] `11F-002`: Header/body boundary on TCP segment boundary → correctly separated (2 tests)
- [x] `11F-003`: Chunk-size line split across 2 segments → correctly parsed (2 tests)
- [x] `11F-004`: Content-Length body in 3 fragments → fully read (2 tests)
- [x] `11F-005`: Very small fragments (1–2 bytes) → decoder handles gracefully (3 tests)

## Deviations
None. All 5 acceptance criteria met with 11 tests total (extra sub-tests for edge cases).

## Files Changed
- **Created:** `src/TurboHttp.StreamTests/Http11/Http11StageFragmentationTests.cs` — 11 tests
- **Updated:** `.maggus/plan_1.md` — marked 11F-001..005 as complete
- **Created:** `COMMIT.md` — commit message
