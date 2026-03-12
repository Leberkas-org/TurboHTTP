# Iteration 01 — TASK-045: Cross-Feature Interaction Integration Tests

## Task
**TASK-045**: Cross-Feature Interaction Integration Tests — integration tests for cross-feature interactions (redirect+cookies, cache+redirect, decompression+cache, all-features-enabled, flags-disabled passthrough).

## Commands Run

1. **Research**: Explored existing integration test patterns in `Http11/` folder (cookie, redirect, retry, cache, content-encoding tests) and `Shared/` infrastructure (KestrelFixture, Routes.cs, TestKit).
2. **Write**: Created `src/TurboHttp.IntegrationTests/Shared/03_CrossFeatureTests.cs` with 8 tests.
3. **Test**: `dotnet test --filter "FullyQualifiedName~CrossFeatureTests"` — **8/8 passed**.
4. **Plan update**: Marked all 3 acceptance criteria as `[x]` in `plan_2.md`.
5. **Stage**: `git add *` to stage all changed files.

## Tests Created

| Test ID | Combination | Description |
|---------|-------------|-------------|
| CROSS-001 | Redirect + Cookies | Set-Cookie during redirect preserved at target |
| CROSS-002 | Flags disabled | Plain SendAsync passthrough — no redirect follow, no cookie injection, no retry |
| CROSS-003 | Redirect + Retry | Retried request succeeds, then redirect followed independently |
| CROSS-004 | Cache + Redirect | Redirect target response cached on second access |
| CROSS-005 | Cache + Cookies | Cached response does not leak cookies across sessions |
| CROSS-006 | Decompression + Cache | Decompressed body stored and served from cache |
| CROSS-007 | Retry + Decompression | Retried request decompresses correctly after retry |
| CROSS-008 | All features | Redirect + cookies + cache + decompression + retry cooperate |

## Deviations
None. All 8 tests pass, all acceptance criteria met.
