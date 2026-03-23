# Feature 002: Integration Tests — Redirects (RFC 9110 §15.4)

## Introduction

Add end-to-end integration tests for redirect handling using the existing Kestrel fixtures and Routes.cs redirect routes. Validates that TurboHttp's RedirectBidiStage correctly follows redirects, preserves/rewrites methods, detects loops, and strips sensitive headers on cross-origin redirects.

### Architecture Context

- **Components involved:** RedirectBidiStage (Streams/Features), RedirectHandler (Protocol/RFC9110), KestrelFixture + KestrelH2Fixture
- **Existing infrastructure:** 11 redirect routes in Routes.RegisterRedirectRoutes(), ClientHelper factory
- **No new components** — pure test coverage

## Mandatory Testing Policy

> **Tests are required and must never be skipped, disabled, or marked as `[Skip]`.** Every acceptance criterion that specifies "All tests pass" is a hard gate — the feature is not complete until every test passes. No `[Fact(Skip = "...")]`, no `#if` guards, no `Assert.Skip()`. If a test fails, fix the code or the test — never suppress it.

## Goals

- Verify all redirect status codes (301, 302, 303, 307, 308) with correct method rewriting
- Verify redirect chain following up to configured limit
- Verify infinite loop detection
- Verify cross-origin header stripping (Authorization)
- Verify cross-scheme (HTTPS→HTTP) protection

## Tasks

### TASK-002-001: Redirect Integration Tests — HTTP/1.1
**Description:** As a library consumer, I want redirects to work correctly over HTTP/1.1 so that standard web navigation patterns are supported.

**Token Estimate:** ~45k tokens
**Predecessors:** none
**Successors:** TASK-002-003
**Parallel:** yes — can run alongside TASK-002-002

**Acceptance Criteria:**
- [x] Test file `RedirectIntegrationTests.cs` in `src/TurboHttp.IntegrationTests/`
- [x] Collection: `"Http1Integration"`, uses `KestrelFixture`
- [x] Tests cover all redirect routes:
  - `GET /redirect/{code}/{target}` — 301, 302, 307, 308 to /hello
  - `GET /redirect/chain/{n}` — chain of N redirects ending at /hello
  - `GET /redirect/loop` — infinite loop → expect exception or error status
  - `GET /redirect/relative` — relative Location header handling
  - `GET /redirect/cross-scheme` — HTTPS→HTTP downgrade protection
  - `POST /redirect/307` — 307 preserves method + body → POST /echo
  - `POST /redirect/303` — 303 rewrites to GET → GET /hello
  - `POST /redirect/302` — 302 rewrites to GET → GET /hello
  - `POST /redirect/308` — 308 preserves method + body → POST /echo
  - `GET /redirect/cross-origin` — 302 to different origin, verify header stripping
  - `GET /redirect/cross-origin-auth` — 302 stripping Authorization header
- [x] Each test asserts final status code AND response body
- [x] All tests pass

### TASK-002-002: Redirect Integration Tests — HTTP/2
**Description:** As a library consumer, I want redirects to work correctly over HTTP/2.

**Token Estimate:** ~35k tokens
**Predecessors:** none
**Successors:** TASK-002-003
**Parallel:** yes — can run alongside TASK-002-001

**Acceptance Criteria:**
- [x] Test file `RedirectH2IntegrationTests.cs` in `src/TurboHttp.IntegrationTests/`
- [x] Collection: `"Http2Integration"`, uses `KestrelH2Fixture`
- [x] Covers same redirect routes over h2c
- [x] All tests pass

### TASK-002-003: Verify Full Redirect Suite
**Description:** Run the complete integration test suite to confirm no regressions.

**Token Estimate:** ~5k tokens
**Predecessors:** TASK-002-001, TASK-002-002
**Successors:** none
**Parallel:** no

**Acceptance Criteria:**
- [x] `dotnet test src/TurboHttp.IntegrationTests/` passes with 0 failures

## Task Dependency Graph

```
TASK-002-001 ──→ TASK-002-003
TASK-002-002 ──┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-002-001 | ~45k | none | yes (with 002) | — |
| TASK-002-002 | ~35k | none | yes (with 001) | — |
| TASK-002-003 | ~5k | 001, 002 | no | haiku |

**Total estimated tokens:** ~85k

## Functional Requirements

- FR-1: 301/302 on GET must follow the redirect and return the final response
- FR-2: 307/308 on POST must preserve method and body to the redirected URI
- FR-3: 303 on POST must rewrite to GET (no body) at the redirected URI
- FR-4: Redirect chains must be followed up to the configured limit (default 50)
- FR-5: Infinite redirect loops must result in a RedirectException, not hang
- FR-6: Cross-origin redirects must strip Authorization header
- FR-7: Relative Location headers must be resolved against the request URI

## Non-Goals

- No cross-scheme redirect testing with TLS fixtures (would require KestrelTlsFixture→KestrelFixture interaction)
- No redirect limit configuration testing (unit-tested in RedirectHandler)

## Technical Considerations

- Cross-origin redirect routes redirect to `127.0.0.1:{port}` — technically same host but different origin semantics in the handler
- Loop detection must not cause test timeout — use short CancellationToken (10s)
- 302 POST→GET rewriting is historical browser behavior, not strictly RFC — verify TurboHttp follows this convention

## Success Metrics

- All 22+ redirect integration tests pass consistently
- Loop detection test completes within 5 seconds (no hanging)
