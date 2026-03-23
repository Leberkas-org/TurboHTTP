# Feature 001: Integration Tests — Cookies (RFC 6265)

## Introduction

Add end-to-end integration tests for cookie handling using the existing Kestrel fixtures and Routes.cs cookie routes. Validates that the TurboHttp CookieJar correctly sets, reads, expires, and scopes cookies across real HTTP connections.

### Architecture Context

- **Components involved:** CookieBidiStage (Streams/Features), CookieJar (Protocol/RFC6265), KestrelFixture + KestrelH2Fixture
- **Existing infrastructure:** 11 cookie routes already defined in Routes.RegisterCookieRoutes(), ClientHelper factory, SmokeTests pattern
- **No new components** — pure test coverage over existing functionality

## Mandatory Testing Policy

> **Tests are required and must never be skipped, disabled, or marked as `[Skip]`.** Every acceptance criterion that specifies "All tests pass" is a hard gate — the feature is not complete until every test passes. No `[Fact(Skip = "...")]`, no `#if` guards, no `Assert.Skip()`. If a test fails, fix the code or the test — never suppress it.

## Goals

- Verify cookie Set/Read/Expire lifecycle works end-to-end over real TCP
- Verify cookie scoping attributes (Secure, HttpOnly, SameSite, Domain, Path) are respected
- Verify multi-cookie and cookie-redirect interactions
- Cover both HTTP/1.1 (KestrelFixture) and HTTP/2 (KestrelH2Fixture)

## Tasks

### TASK-001-001: Cookie Integration Tests — HTTP/1.1
**Description:** As a library consumer, I want cookies to work correctly over HTTP/1.1 so that session management is reliable.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** TASK-001-003
**Parallel:** yes — can run alongside TASK-001-002

**Acceptance Criteria:**
- [x] Test file `CookieIntegrationTests.cs` in `src/TurboHttp.IntegrationTests/`
- [x] Collection: `"Http1Integration"`, uses `KestrelFixture`
- [x] Tests cover all 11 cookie routes:
  - `GET /cookie/set/{name}/{value}` — basic set + echo roundtrip
  - `GET /cookie/set-secure/{name}/{value}` — Secure flag (skipped over HTTP)
  - `GET /cookie/set-httponly/{name}/{value}` — HttpOnly flag
  - `GET /cookie/set-samesite/{name}/{value}/{policy}` — SameSite=Strict/Lax/None
  - `GET /cookie/set-expires/{name}/{value}/{seconds}` — Max-Age expiry
  - `GET /cookie/set-domain/{name}/{value}/{domain}` — Domain scoping
  - `GET /cookie/set-path/{name}/{value}/{path}` — Path scoping
  - `GET /cookie/echo` — echo all cookies as JSON
  - `GET /cookie/set-multiple` — multiple Set-Cookie headers
  - `GET /cookie/delete/{name}` — Max-Age=0 deletion
  - `GET /cookie/set-and-redirect` — set + 302 redirect to /cookie/echo
- [x] Each test uses `CancellationTokenSource(TimeSpan.FromSeconds(30))`
- [x] All tests pass with `dotnet test --filter "FullyQualifiedName~CookieIntegrationTests"`

### TASK-001-002: Cookie Integration Tests — HTTP/2
**Description:** As a library consumer, I want cookies to work correctly over HTTP/2 so that cookie handling is protocol-agnostic.

**Token Estimate:** ~30k tokens
**Predecessors:** none
**Successors:** TASK-001-003
**Parallel:** yes — can run alongside TASK-001-001

**Acceptance Criteria:**
- [x] Test file `CookieH2IntegrationTests.cs` in `src/TurboHttp.IntegrationTests/`
- [x] Collection: `"Http2Integration"`, uses `KestrelH2Fixture`
- [x] Covers same 11 cookie routes as TASK-001-001 over h2c
- [x] All tests pass

### TASK-001-003: Verify Full Cookie Suite
**Description:** Run the complete integration test suite to confirm no regressions.

**Token Estimate:** ~5k tokens
**Predecessors:** TASK-001-001, TASK-001-002
**Successors:** none
**Parallel:** no

**Acceptance Criteria:**
- [x] `dotnet test src/TurboHttp.IntegrationTests/` passes with 0 failures
- [x] All new cookie tests appear in test output

## Task Dependency Graph

```
TASK-001-001 ──→ TASK-001-003
TASK-001-002 ──┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-001-001 | ~40k | none | yes (with 002) | — |
| TASK-001-002 | ~30k | none | yes (with 001) | — |
| TASK-001-003 | ~5k | 001, 002 | no | haiku |

**Total estimated tokens:** ~75k

## Functional Requirements

- FR-1: Cookies set via Set-Cookie header must be sent back on subsequent requests to the same host
- FR-2: Cookie deletion via Max-Age=0 must remove the cookie from subsequent requests
- FR-3: Multiple Set-Cookie headers in a single response must all be stored
- FR-4: Cookies must persist across a redirect chain (set-and-redirect scenario)
- FR-5: Path-scoped cookies must only be sent for matching request paths

## Non-Goals

- No HTTP/3 cookie tests (HTTP/3 explicitly excluded from production scope)
- No cross-origin cookie isolation tests (single-host fixtures)
- No performance testing of CookieJar under high concurrency

## Technical Considerations

- ClientHelper creates a fresh client per test class — cookie state is isolated per test class
- The `set-and-redirect` route tests the interaction between CookieBidiStage and RedirectBidiStage
- Secure cookies won't be sent over plaintext HTTP — test should verify cookie is NOT echoed

## Success Metrics

- All 22+ cookie integration tests (11 routes x 2 protocols) pass consistently
- No flaky tests due to cookie state leaking between test methods
