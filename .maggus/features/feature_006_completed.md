# Feature 006: Integration Tests — Connection Management (RFC 9112 §9)

## Introduction

Add end-to-end integration tests for HTTP/1.1 connection management. Validates keep-alive, connection close, and connection reuse behavior over real TCP connections.

### Architecture Context

- **Components involved:** ConnectionReuseStage (Streams/Features), ConnectionReuseEvaluator (Protocol/RFC9112), KestrelFixture
- **Existing infrastructure:** 4 connection routes in Routes.RegisterConnectionReuseRoutes()
- **HTTP/1.x only** — HTTP/2 uses multiplexing, not connection-level keep-alive

## Mandatory Testing Policy

> **Tests are required and must never be skipped, disabled, or marked as `[Skip]`.** Every acceptance criterion that specifies "All tests pass" is a hard gate — the feature is not complete until every test passes. No `[Fact(Skip = "...")]`, no `#if` guards, no `Assert.Skip()`. If a test fails, fix the code or the test — never suppress it.

## Goals

- Verify Connection: Keep-Alive enables connection reuse
- Verify Connection: close terminates the connection
- Verify default HTTP/1.1 keep-alive behavior (no Connection header)
- Verify 101 Upgrade terminates normal HTTP exchange

## Tasks

### TASK-006-001: Connection Management Integration Tests
**Description:** As a library consumer, I want connections to be reused when appropriate so that latency is minimized.

**Token Estimate:** ~35k tokens
**Predecessors:** none
**Successors:** none
**Parallel:** yes — independent feature

**Acceptance Criteria:**
- [x] Test file `ConnectionIntegrationTests.cs` in `src/TurboHttp.IntegrationTests/`
- [x] Collection: `"Http1Integration"`, uses `KestrelFixture`
- [x] Tests cover:
  - `GET /conn/keep-alive` — two sequential requests succeed on same client
  - `GET /conn/close` — Connection: close header present in response
  - `GET /conn/default` — HTTP/1.1 default keep-alive (no Connection header)
  - `GET /conn/upgrade-101` — 101 response, connection not reusable for further HTTP
  - Sequential requests test: send two requests, second succeeds (proves reuse)
- [x] All tests pass with `dotnet test --filter "FullyQualifiedName~ConnectionIntegrationTests"`

## Task Dependency Graph

```
TASK-006-001 (standalone)
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-006-001 | ~35k | none | yes | — |

**Total estimated tokens:** ~35k

## Functional Requirements

- FR-1: Requests to /conn/keep-alive must allow subsequent requests on the same connection
- FR-2: Requests to /conn/close must result in connection teardown
- FR-3: Default HTTP/1.1 behavior (no Connection header) must keep the connection alive
- FR-4: 101 Switching Protocols must not be followed by HTTP requests on the same connection

## Non-Goals

- No connection pool sizing tests
- No idle timeout eviction tests
- No pipelining tests (HTTP/1.1 pipelining is rarely used in practice)

## Success Metrics

- All 5+ connection management tests pass consistently
