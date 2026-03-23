# Feature 010: OpenTelemetry Tracing

## Introduction

Add distributed tracing to TurboHttp using `System.Diagnostics.ActivitySource` and `Activity` (the .NET implementation of OpenTelemetry spans). This enables consumers to trace HTTP request lifecycles through their observability stack (Jaeger, Zipkin, Application Insights, Grafana Tempo, etc.).

### Architecture Context

- **Components involved:** New `TurboHttpInstrumentation` static class, BidiStages (Redirect, Retry, Cache), ConnectionStage (Transport), Engine (Streams)
- **No external dependency** — uses built-in `System.Diagnostics.DiagnosticSource` (ships with .NET)
- **Follows .NET conventions:** ActivitySource name = assembly name, span naming follows OpenTelemetry HTTP semantic conventions

## Mandatory Testing Policy

> **Tests are required and must never be skipped, disabled, or marked as `[Skip]`.** Every acceptance criterion that specifies "All tests pass" is a hard gate — the feature is not complete until every test passes. No `[Fact(Skip = "...")]`, no `#if` guards, no `Assert.Skip()`. If a test fails, fix the code or the test — never suppress it.

## Goals

- Trace full request lifecycle with parent-child span hierarchy
- Follow OpenTelemetry HTTP semantic conventions for span names and attributes
- Zero overhead when no listener is attached (ActivitySource short-circuits)
- Enable consumers to subscribe via `AddSource("TurboHttp")` in OTel SDK

## Tasks

### TASK-010-001: Create TurboHttpInstrumentation Infrastructure
**Description:** As a library author, I want a central instrumentation class that holds the ActivitySource and standardized span creation methods.

**Token Estimate:** ~35k tokens
**Predecessors:** none
**Successors:** TASK-010-002
**Parallel:** no

**Acceptance Criteria:**
- [x] New file `src/TurboHttp/Diagnostics/TurboHttpInstrumentation.cs`
- [x] Static `ActivitySource` named `"TurboHttp"` with assembly version
- [x] Helper methods for creating Activities:
  - `StartRequest(HttpRequestMessage)` → Activity with `http.request.method`, `url.full`, `server.address`, `server.port`
  - `StartConnect(Uri)` → Activity "TurboHttp.Connect"
  - `StartRedirect(Uri, int statusCode)` → Activity "TurboHttp.Redirect"
  - `StartRetry(int attemptNumber)` → Activity "TurboHttp.Retry"
  - `StartCacheLookup(Uri)` → Activity "TurboHttp.CacheLookup"
- [x] `SetResponse(Activity, HttpResponseMessage)` enrichment: `http.response.status_code`
- [x] `SetError(Activity, Exception)` enrichment: `otel.status_code = ERROR`, `exception.type`, `exception.message`
- [x] All span/tag names follow [OTel HTTP semantic conventions](https://opentelemetry.io/docs/specs/semconv/http/)
- [x] Build succeeds with 0 errors

### TASK-010-002: Instrument Request Lifecycle in Stages
**Description:** As an operator, I want to see spans for each stage of a request so that I can diagnose latency.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-010-001
**Successors:** TASK-010-003
**Parallel:** no
**Model:** opus

**Acceptance Criteria:**
- [x] `RedirectBidiStage` starts a "TurboHttp.Redirect" child activity on each redirect hop
- [x] `RetryBidiStage` starts a "TurboHttp.Retry" child activity on each retry attempt
- [x] `CacheBidiStage` starts a "TurboHttp.CacheLookup" activity, tags `cache.hit` = true/false
- [x] Root activity "TurboHttp.Request" created at pipeline entry point (HandlerBidiStage or TurboHandler)
- [x] Response status code set on root activity completion
- [x] Error status set on exception
- [x] All existing tests pass — tracing is no-op when no listener attached
- [x] Build succeeds with 0 errors

### TASK-010-003: Tracing Unit Tests
**Description:** As a library author, I want tests that verify the tracing instrumentation emits correct spans.

**Token Estimate:** ~35k tokens
**Predecessors:** TASK-010-002
**Successors:** none
**Parallel:** no

**Acceptance Criteria:**
- [ ] Test file in `src/TurboHttp.Tests/` or `src/TurboHttp.StreamTests/`
- [ ] Uses `ActivityListener` to capture emitted activities
- [ ] Verifies root span name, tags, and status for successful request
- [ ] Verifies redirect child spans with correct hop count
- [ ] Verifies retry child spans with attempt number
- [ ] Verifies cache lookup span with hit/miss tag
- [ ] Verifies error span attributes on exception
- [ ] All tests pass

## Task Dependency Graph

```
TASK-010-001 ──→ TASK-010-002 ──→ TASK-010-003
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-010-001 | ~35k | none | no | — |
| TASK-010-002 | ~50k | 001 | no | opus |
| TASK-010-003 | ~35k | 002 | no | — |

**Total estimated tokens:** ~120k

## Functional Requirements

- FR-1: `ActivitySource("TurboHttp")` must be the single source for all spans
- FR-2: Root span "TurboHttp.Request" must wrap the entire request lifecycle
- FR-3: Child spans for Redirect, Retry, CacheLookup must be nested under root
- FR-4: All tags must follow OpenTelemetry HTTP semantic conventions
- FR-5: When no `ActivityListener` is subscribed, zero allocation overhead (ActivitySource returns null)
- FR-6: `http.response.status_code` must be set on every completed request

## Non-Goals

- No automatic OTel SDK registration (consumer's responsibility via `AddSource("TurboHttp")`)
- No span for Encode/Decode (too low-level, adds noise)
- No baggage propagation
- No W3C trace context header injection (consumers can add this via DelegatingHandler)

## Technical Considerations

- `Activity.Current` propagation in Akka.Streams stages requires care — stages run on thread pool, but `Activity` flows via `AsyncLocal`
- BidiStages process elements in `OnPush` callbacks — Activity must be started and stopped within the same callback, or stored in stage state
- GraphStages are not async — use `Activity.Start()` / `Activity.Stop()` (synchronous API)
- Consider using `Activity.SetTag()` for enrichment (not `AddTag()` which doesn't overwrite)

## Success Metrics

- `ActivityListener` tests capture correct span hierarchy
- Zero test regressions when tracing is not subscribed
