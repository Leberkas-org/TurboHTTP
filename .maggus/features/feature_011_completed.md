# Feature 011: OpenTelemetry Metrics

## Introduction

Add metrics instrumentation to TurboHttp using `System.Diagnostics.Metrics.Meter`. This enables consumers to monitor request throughput, latency, cache efficiency, retry rates, and connection pool health via any .NET metrics provider (Prometheus, Application Insights, OTLP, etc.).

### Architecture Context

- **Components involved:** New `TurboHttpMetrics` static class, BidiStages (Cache, Redirect, Retry), Pooling layer (HostPool, ConnectionState)
- **No external dependency** — uses built-in `System.Diagnostics.Metrics` (ships with .NET 8+)
- **Follows .NET conventions:** Meter name = assembly name, instrument names follow OTel HTTP semantic conventions

## Mandatory Testing Policy

> **Tests are required and must never be skipped, disabled, or marked as `[Skip]`.** Every acceptance criterion that specifies "All tests pass" is a hard gate — the feature is not complete until every test passes. No `[Fact(Skip = "...")]`, no `#if` guards, no `Assert.Skip()`. If a test fails, fix the code or the test — never suppress it.

## Goals

- Expose request counters, latency histograms, and connection pool gauges
- Follow OpenTelemetry HTTP semantic conventions for metric names
- Zero overhead when no listener is attached (Meter short-circuits)
- Enable consumers to subscribe via `AddMeter("TurboHttp")` in OTel SDK

## Tasks

### TASK-011-001: Create TurboHttpMetrics Infrastructure
**Description:** As a library author, I want a central metrics class that defines all instruments.

**Token Estimate:** ~30k tokens
**Predecessors:** none
**Successors:** TASK-011-002
**Parallel:** yes — can run alongside Feature 010

**Acceptance Criteria:**
- [x] New file `src/TurboHttp/Diagnostics/TurboHttpMetrics.cs`
- [x] Static `Meter` named `"TurboHttp"` with assembly version
- [x] Instruments defined:
  - `Counter<long> http.client.request.count` — total requests sent (tags: method, status_code, server.address)
  - `Histogram<double> http.client.request.duration` — request duration in seconds (tags: method, status_code)
  - `Counter<long> http.client.cache.hit` — cache hits
  - `Counter<long> http.client.cache.miss` — cache misses
  - `Counter<long> http.client.retry.count` — retry attempts (tags: method, server.address)
  - `Counter<long> http.client.redirect.count` — redirect hops (tags: status_code)
  - `Histogram<double> http.client.connection.duration` — connection lifetime in seconds
  - `UpDownCounter<long> http.client.connection.active` — currently active connections
  - `UpDownCounter<long> http.client.connection.idle` — currently idle connections
- [x] Build succeeds with 0 errors

### TASK-011-002: Instrument Stages and Pooling Layer
**Description:** As an operator, I want to see metrics for request throughput, cache efficiency, and connection pool health.

**Token Estimate:** ~45k tokens
**Predecessors:** TASK-011-001
**Successors:** TASK-011-003
**Parallel:** no
**Model:** opus

**Acceptance Criteria:**
- [x] `CacheBidiStage` records cache.hit / cache.miss on each request
- [x] `RetryBidiStage` records retry.count on each retry attempt
- [x] `RedirectBidiStage` records redirect.count on each redirect hop
- [x] Request pipeline entry/exit records request.count and request.duration
- [x] `HostPool` or `ConnectionActorBase` records connection.active / connection.idle changes
- [x] `ConnectionActorBase` records connection.duration on connection close
- [x] All existing tests pass — metrics are no-op when no listener attached
- [x] Build succeeds with 0 errors

### TASK-011-003: Metrics Unit Tests
**Description:** As a library author, I want tests that verify metrics are emitted correctly.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-011-002
**Successors:** none
**Parallel:** no

**Acceptance Criteria:**
- [x] Test file in `src/TurboHttp.Tests/`
- [x] Uses `MeterListener` to capture emitted metrics
- [x] Verifies request.count incremented on each request
- [x] Verifies cache.hit/miss counters
- [x] Verifies retry.count with correct attempt tags
- [x] Verifies connection.active incremented/decremented
- [x] All tests pass

## Task Dependency Graph

```
TASK-011-001 ──→ TASK-011-002 ──→ TASK-011-003
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-011-001 | ~30k | none | yes (with Feature 010) | — |
| TASK-011-002 | ~45k | 001 | no | opus |
| TASK-011-003 | ~30k | 002 | no | — |

**Total estimated tokens:** ~105k

## Functional Requirements

- FR-1: `Meter("TurboHttp")` must be the single source for all metrics
- FR-2: All metric instrument names must follow OTel HTTP semantic conventions
- FR-3: Tags must include `http.request.method`, `http.response.status_code`, `server.address` where applicable
- FR-4: UpDownCounters for connection pool must accurately reflect current state
- FR-5: When no `MeterListener` is subscribed, zero allocation overhead

## Non-Goals

- No automatic OTel SDK registration (consumer's responsibility via `AddMeter("TurboHttp")`)
- No custom metric views or historgram bucket configuration
- No per-host breakdown metrics (too high cardinality by default)

## Technical Considerations

- `UpDownCounter` for connection pool requires increment on connect, decrement on disconnect — must be called from actor message handlers (not from stage callbacks)
- `Histogram.Record()` for request duration requires measuring elapsed time — use `Stopwatch` or `Activity.Duration`
- Tags with high cardinality (e.g., full URL) must be avoided — use `server.address` not `url.full`

## Success Metrics

- `MeterListener` tests capture correct metric values
- Connection pool counters stay consistent (no leaks)
- Zero test regressions when metrics are not subscribed
