<!-- maggus-id: 20260326-163800-feature-029 -->

# Feature 029: Phase 4 — Features & Operations (Observability & Control)

## Introduction

Add operational capabilities required for production deployment:
1. **Request/Response Logging** — Structured logging for diagnostics and auditing
2. **OpenTelemetry Metrics/Tracing** — Distributed tracing and performance metrics
3. **Timeout Policies** — Per-operation and per-connection timeout control

These features enable production monitoring, debugging, and operational visibility.

### Architecture Context
- Integrates with Microsoft.Extensions.Logging
- OpenTelemetry SDK for tracing/metrics
- Hooks into `TurboHttpClient`, `ConnectionPool`, `Akka.Streams` stages

## Goals
1. Implement structured logging (request start, response received, errors)
2. Add OpenTelemetry Activity/Meter for distributed tracing
3. Implement timeout policies (request, connection, idle)
4. Zero impact when logging/tracing disabled

## Tasks

### TASK-029-001: Structured Logging Framework
**Token Estimate:** ~50k | **Predecessors:** none | **Successors:** TASK-029-004 | **Parallel:** yes (with 002, 003)

**Acceptance Criteria:**
- [ ] Create logging abstraction using `ILogger<TurboHttpClient>`
- [ ] Log events:
  - [ ] RequestStarted (method, uri, headers count)
  - [ ] ResponseReceived (status, headers count, body size)
  - [ ] RequestFailed (error, status if applicable)
  - [ ] ConnectionAcquired, ConnectionReleased (pool stats)
  - [ ] RedirectFollowed (uri, count)
  - [ ] Retry attempted (reason, attempt number)
- [ ] Use `LogLevel.Information` for normal flow, `LogLevel.Debug` for detailed state
- [ ] Ensure sensitive data (passwords, tokens) are NOT logged
- [ ] All log messages are structured (use log scopes, properties)
- [ ] Performance: <100µs logging overhead per request

---

### TASK-029-002: OpenTelemetry Tracing Integration
**Token Estimate:** ~45k | **Predecessors:** none | **Successors:** TASK-029-004 | **Parallel:** yes (with 001, 003)

**Acceptance Criteria:**
- [ ] Create `TurboHttpActivitySource` with standard HTTP span attributes (RFC 3986)
- [ ] Instrument request/response lifecycle:
  - [ ] Activity.Start: "http.client_request" (method, url, headers)
  - [ ] Activity.SetTag: response status, body size, duration
  - [ ] Activity.Stop on success or exception
- [ ] Support distributed tracing headers (W3C Trace Context)
- [ ] Baggage propagation (correlate related spans)
- [ ] Zero overhead when tracing disabled
- [ ] Unit tests verify Activity creation and tags

---

### TASK-029-003: Timeout Policies (Request, Connection, Idle)
**Token Estimate:** ~40k | **Predecessors:** none | **Successors:** TASK-029-005 | **Parallel:** yes (with 001, 002)

**Acceptance Criteria:**
- [ ] Add `TimeoutPolicy` class:
  - [ ] `RequestTimeout` (default 30s) — abort if request not complete
  - [ ] `ConnectionTimeout` (default 10s) — abort if connection not established
  - [ ] `IdleTimeout` (default 60s) — close idle keep-alive connections
- [ ] Implement in `TurboHttpClientBuilder`:
  - [ ] `.WithRequestTimeout(TimeSpan)`
  - [ ] `.WithConnectionTimeout(TimeSpan)`
  - [ ] `.WithIdleTimeout(TimeSpan)`
- [ ] Enforce via `CancellationToken` with timeout
- [ ] Log timeout events with context
- [ ] Test scenarios: timeout < actual duration, timeout > actual duration

---

### TASK-029-004: Logging Integration into Stages
**Token Estimate:** ~30k | **Predecessors:** TASK-029-001, TASK-029-002 | **Successors:** TASK-029-006 | **Parallel:** no

**Acceptance Criteria:**
- [ ] Inject logging into key stages:
  - [ ] `Http*EncoderStage`: log encoding details (size, header count)
  - [ ] `Http*DecoderStage`: log decoding details (status, body size)
  - [ ] `RedirectBidiStage`: log redirect events
  - [ ] `RetryBidiStage`: log retry attempts
  - [ ] `ConnectionStage`: log pool acquisition/release
- [ ] All logging is optional (no logs if ILogger is null)
- [ ] Stage tests still pass, no behavioral changes

---

### TASK-029-005: Timeout Enforcement in Pipeline & Connection Pool
**Token Estimate:** ~35k | **Predecessors:** TASK-029-003 | **Successors:** TASK-029-006 | **Parallel:** no

**Acceptance Criteria:**
- [ ] Modify `ConnectionPool.AcquireAsync()` to respect `ConnectionTimeout`
- [ ] Modify request processing to enforce `RequestTimeout`
- [ ] Modify keep-alive connection cleanup to enforce `IdleTimeout`
- [ ] Propagate `CancellationToken` through entire async call chain
- [ ] Test scenarios: abort during connection, during response streaming, during idle wait

---

### TASK-029-006: Integration Tests & Documentation
**Token Estimate:** ~35k | **Predecessors:** TASK-029-004, TASK-029-005 | **Successors:** none | **Parallel:** no

**Acceptance Criteria:**
- [ ] Integration tests:
  - [ ] Verify logging output contains expected events
  - [ ] Verify OpenTelemetry activities are created and exported
  - [ ] Verify timeout aborts request and logs timeout event
  - [ ] Verify slow server triggers RequestTimeout
- [ ] Documentation:
  - [ ] Add `docs/LOGGING.md` with examples
  - [ ] Add `docs/TRACING.md` with OpenTelemetry setup
  - [ ] Add `docs/TIMEOUT_POLICIES.md` with configuration examples
- [ ] Update CLAUDE.md with configuration snippets
- [ ] All tests pass

---

## Task Dependency Graph
```
TASK-029-001 ──→ TASK-029-004 ──→ TASK-029-006
TASK-029-002 ──→↗
TASK-029-003 ──→ TASK-029-005 ──→↗
```

### Summary Table

| Task | Estimate | Predecessors | Parallel |
|------|----------|--------------|----------|
| TASK-029-001 | ~50k | none | yes (w/ 002, 003) |
| TASK-029-002 | ~45k | none | yes (w/ 001, 003) |
| TASK-029-003 | ~40k | none | yes (w/ 001, 002) |
| TASK-029-004 | ~30k | 001, 002 | no |
| TASK-029-005 | ~35k | 003 | no |
| TASK-029-006 | ~35k | 004, 005 | no |

**Total:** ~235k tokens (~6 days solo)

## Functional Requirements
1. **FR-1:** All request/response events SHALL be logged with context
2. **FR-2:** OpenTelemetry activities SHALL include standard HTTP attributes
3. **FR-3:** Timeout policies SHALL be configurable per-client
4. **FR-4:** Request timeout SHALL abort mid-stream and cleanup resources
5. **FR-5:** Logging/tracing disabled → zero overhead

## Non-Goals
- No custom metrics dashboard (configuration only)
- No application-level retry logic (separate from retry policy)
- No compliance audit logging (application responsibility)

## Success Metrics
1. Logging shows complete request lifecycle
2. OpenTelemetry traces work with popular backends (Jaeger, DataDog, etc.)
3. Timeouts abort gracefully with proper cleanup
4. <50µs overhead when disabled
5. ≥20 integration tests passing

