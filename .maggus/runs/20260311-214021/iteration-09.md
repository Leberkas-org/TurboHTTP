# Iteration 09 — TASK-038: Http20Engine Cookie Integration Tests

## Task
TASK-038: Add integration tests for Http20Engine cookie handling over HTTP/2.

## Commands Run
1. `dotnet test --filter "FullyQualifiedName~Http20CookieTests"` — 4/5 passed, 1 failed (HPACK test used URL-encoded chars that Kestrel decoded)
2. Fixed HPACK test to use plain alphanumeric cookie values instead of URL-encoded special chars
3. `dotnet test --filter "FullyQualifiedName~Http20CookieTests"` — 5/5 passed

## Files Created
- `src/TurboHttp.IntegrationTests/Http20/07_Http20CookieTests.cs` — 5 tests

## Tests (all passing)
| ID | Test | Status |
|----|------|--------|
| 20E-INT-035 | Set-Cookie stored and sent back on subsequent HTTP/2 request | PASS |
| 20E-INT-036 | Multiple Set-Cookie headers in single HTTP/2 response all stored | PASS |
| 20E-INT-037 | Cookie header survives HPACK compression round-trip | PASS |
| 20E-INT-038 | Cookies preserved across 302 redirect over HTTP/2 | PASS |
| 20E-INT-039 | Cookie with Path attribute restricts scope over HTTP/2 | PASS |

## Deviations
- HPACK test originally used URL-encoded special chars (`%3D`, `%26`) but Kestrel decodes these in route parameters. Changed to use multiple plain cookies to still exercise HPACK dynamic table indexing.

## Acceptance Criteria
- [x] File created: `src/TurboHttp.IntegrationTests/Http20/07_Http20CookieTests.cs`
- [x] 5 tests: store+send, multiple Set-Cookie, HPACK compressed cookie header, cookies across redirects, Path restriction
- [x] All tests pass: `dotnet test --filter "FullyQualifiedName~Http20CookieTests"`
