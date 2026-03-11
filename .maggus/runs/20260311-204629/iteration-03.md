# Iteration 03 — TASK-022: Http10Engine Retry Integration Tests

## Task
**ID:** TASK-022
**Title:** Http10Engine Retry Integration Tests

## What was done
Created `src/TurboHttp.IntegrationTests/Http10/05_Http10RetryTests.cs` with 6 integration tests verifying Http10Engine retry handling against real Kestrel retry routes.

### Tests implemented
| Test | DisplayName | Verifies |
|------|-------------|----------|
| `Get_503_RetriesIdempotentRequest` | RETRY-INT-001 | GET retries on 503, exhausts 3 attempts |
| `Get_408_RetriesIdempotentRequest` | RETRY-INT-002 | GET retries on 408, exhausts 3 attempts |
| `Post_503_NotRetried` | RETRY-INT-003 | POST not retried (RFC 9110 §9.2.2) |
| `Get_503_RetryAfter_ParsesDelay` | RETRY-INT-004 | Retry-After header parsed as seconds delay |
| `MaxRetryCount_Enforced` | RETRY-INT-005 | MaxRetries=2 stops after 2 attempts |
| `SucceedAfterN_RetriesUntilSuccess` | RETRY-INT-006 | /retry/succeed-after/3 succeeds on 3rd attempt |

### Commands run
- `dotnet test --filter "FullyQualifiedName~Http10RetryTests"` → **6/6 passed** (408ms)

## Deviations
None. All acceptance criteria met.

## Acceptance Criteria
- [x] File created: `src/TurboHttp.IntegrationTests/Http10/05_Http10RetryTests.cs`
- [x] 6 tests: GET retry 503, GET retry 408, POST no retry 503, Retry-After, max count, succeed after N
- [x] All tests pass: `dotnet test --filter "FullyQualifiedName~Http10RetryTests"`
