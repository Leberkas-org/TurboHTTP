# Feature 004: Integration Tests — Redirect, Retry & Connection Management

## Introduction

Building on the infrastructure from Feature 002, this feature tests redirect following (RFC 9110 §15.4), retry logic (RFC 9110 §9.2), and HTTP/1.1 connection reuse (RFC 9112 §9). These are critical client behaviors that involve the `RedirectBidiStage`, `RetryBidiStage`, and connection pool lifecycle.

### Architecture Context

- **Components involved:**
  - `RedirectBidiStage` — internal feedback loop for redirect following
  - `RetryBidiStage` — internal feedback loop for idempotent retries
  - `RedirectHandler` — RFC 9110 §15.4: method rewriting, HTTPS→HTTP protection, loop detection
  - `RetryEvaluator` — RFC 9110 §9.2: idempotency-based retry, Retry-After parsing
  - `ConnectionReuseEvaluator` — RFC 9112 §9: keep-alive/close decision
  - `HostPoolActor` — connection pool management, idle eviction
  - Pipeline: `.WithRedirect()` and `.WithRetry()` builder extensions
- **Existing routes used:** `/redirect/*`, `/retry/*`, `/conn/*`
- **Depends on:** Feature 002 (ClientHelper, Collections)

## Goals

- Verify all 5 redirect status codes (301, 302, 303, 307, 308) with correct method rewriting
- Test redirect chains, loop detection, relative redirects, cross-origin behavior
- Verify HTTPS→HTTP redirect protection
- Test retry on 408/503 with Retry-After header (seconds and HTTP-date)
- Verify idempotent vs non-idempotent retry behavior
- Test HTTP/1.1 connection reuse (keep-alive, close, default)
- Verify HTTP/2 redirect/retry over multiplexed connections

## Tasks

### TASK-004-001: Redirect Tests across HTTP Versions
**Description:** As a developer, I want redirect-following tests for HTTP/1.1 and HTTP/2, so that method rewriting, chain following, loop detection, and cross-origin behavior are verified end-to-end.

**Token Estimate:** ~80k tokens
**Predecessors:** Feature 002 (TASK-002-001)
**Successors:** none
**Parallel:** yes — can run alongside TASK-004-002 and TASK-004-003

**Acceptance Criteria:**
- [ ] `Http1RedirectTests.cs` with `[Collection("Http1Integration")]` (~18 tests):
  - GET /redirect/302/hello → follows to /hello → 200
  - `[Theory]` GET /redirect/{code}/hello for 301, 302, 303, 307, 308
  - POST /redirect/307 → method stays POST, body echoed at /echo
  - POST /redirect/308 → method stays POST, body echoed at /echo
  - POST /redirect/303 → becomes GET, no body
  - POST /redirect/302 → becomes GET (browser compatibility)
  - GET /redirect/chain/5 → 5 hops, ends at /hello
  - GET /redirect/loop → loop detection, exception thrown
  - GET /redirect/relative → relative Location resolved to /hello
  - GET /redirect/cross-origin with Authorization header → header stripped after redirect
  - GET /redirect/cross-origin → follows, verifies headers
- [ ] `Http2RedirectTests.cs` with `[Collection("Http2Integration")]` (~8 tests):
  - Same core redirect scenarios over HTTP/2
  - Redirect reuses same HTTP/2 connection (multiplexing)
  - Concurrent redirects multiplexed on single connection
- [ ] `Http3RedirectTests.cs` with `[Collection("Http3Integration")]` + `[Trait("Category", "Http3")]` (~6 tests):
  - Core redirect scenarios over HTTP/3
  - Redirect over QUIC connection
- [ ] All tests green: `dotnet test src/TurboHttp.IntegrationTests/ --filter "RedirectTests"`

### TASK-004-002: Retry Tests
**Description:** As a developer, I want retry-logic tests for HTTP/1.1, so that idempotent retry, Retry-After parsing, and non-idempotent rejection are verified.

**Token Estimate:** ~60k tokens
**Predecessors:** Feature 002 (TASK-002-001)
**Successors:** none
**Parallel:** yes — can run alongside TASK-004-001 and TASK-004-003

**Acceptance Criteria:**
- [ ] `Http1RetryTests.cs` with `[Collection("Http1Integration")]` (~14 tests):
  - GET /retry/408 → automatically retried
  - GET /retry/503 → automatically retried
  - HEAD /retry/503 → retried
  - PUT /retry/503 → retried (idempotent)
  - DELETE /retry/503 → retried (idempotent)
  - POST /retry/non-idempotent-503 → NOT retried (non-idempotent)
  - `[Theory]` GET /retry/succeed-after/{n} for n=2, 3, 5
  - GET /retry/503-retry-after/2 → respects Retry-After seconds
  - GET /retry/503-retry-after-date → respects Retry-After as HTTP-date
  - Max retry limit exceeded → original error returned
  - Retry preserves custom request headers
- [ ] All tests green: `dotnet test src/TurboHttp.IntegrationTests/ --filter "RetryTests"`

### TASK-004-003: Connection Reuse Tests
**Description:** As a developer, I want connection reuse tests for HTTP/1.1, so that keep-alive, connection close, and idle eviction are verified.

**Token Estimate:** ~60k tokens
**Predecessors:** Feature 002 (TASK-002-001)
**Successors:** none
**Parallel:** yes — can run alongside TASK-004-001 and TASK-004-002

**Acceptance Criteria:**
- [ ] `Http1ConnectionReuseTests.cs` with `[Collection("Http1Integration")]` (~10 tests):
  - GET /conn/keep-alive → connection reused for next request
  - GET /conn/close → new connection opened for next request
  - GET /conn/default → HTTP/1.1 default keep-alive
  - Two sequential GET /hello → same connection reused
  - GET /conn/upgrade-101 → connection marked non-reusable
  - Multiple concurrent requests → respect MaxConnectionsPerHost
  - Connection idle timeout → evicted, new connection for next request
- [ ] All tests green: `dotnet test src/TurboHttp.IntegrationTests/ --filter "ConnectionReuseTests"`

## Task Dependency Graph

```
              ┌──→ TASK-004-001
Feature 002 ──┼──→ TASK-004-002
              └──→ TASK-004-003
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-004-001 | ~80k | Feature 002 | yes (with 002, 003) | — |
| TASK-004-002 | ~60k | Feature 002 | yes (with 001, 003) | — |
| TASK-004-003 | ~60k | Feature 002 | yes (with 001, 002) | — |

**Total estimated tokens:** ~200k

## Functional Requirements

- FR-1: Redirect following must respect RFC 9110 §15.4 method rewriting rules (301/302/303 → GET, 307/308 → preserve)
- FR-2: POST body must be preserved on 307/308 redirects
- FR-3: Redirect loop must be detected within configurable max hops (default 50)
- FR-4: Authorization header must be stripped on cross-origin redirects
- FR-5: HTTPS→HTTP redirect must be blocked by default
- FR-6: Retry must only apply to idempotent methods (GET, HEAD, PUT, DELETE) unless explicitly configured
- FR-7: Retry-After header must be respected (both seconds and HTTP-date formats)
- FR-8: Connection: close must prevent connection reuse
- FR-9: Idle connections must be evicted after configurable timeout

## Non-Goals

- No cookie persistence across redirects (→ Feature 005)
- No HTTPS→HTTP redirect allowance configuration testing
- No connection pool sizing optimization
- No HTTP/2 GOAWAY handling (→ Feature 006)

## Technical Considerations

- Redirect tests need `.WithRedirect()` on the client builder
- Retry tests need `.WithRetry()` with a `RetryPolicy`
- `/retry/succeed-after/{n}` uses `ConcurrentDictionary` server-side — use `?key={unique}` query param for deterministic counting
- Connection reuse is hard to verify directly — may need to check response timing or server-side connection tracking
- Idle timeout tests require `Task.Delay` — keep timeouts short (1-2 seconds)

## Success Metrics

- All 56 tests green
- Redirect loop detection triggers within 100ms
- Retry-After delay respected within ±500ms tolerance
- No flaky connection reuse tests after 10 repeated runs

## Open Questions

_None — all questions resolved._
