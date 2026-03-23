# Feature 012: DiagnosticListener & EventSource

## Introduction

Add low-level diagnostic events to TurboHttp using `DiagnosticListener` and `EventSource`. This enables advanced debugging with `dotnet-trace`, PerfView, and ETW without requiring OpenTelemetry SDK integration. Follows the pattern established by `System.Net.Http` in .NET itself.

### Architecture Context

- **Components involved:** New `TurboHttpDiagnosticListener` and `TurboHttpEventSource` classes, Transport layer (ConnectionStage, ClientByteMover), Decoding stages, Encoding stages
- **No external dependency** — uses built-in `System.Diagnostics.DiagnosticSource` and `System.Diagnostics.Tracing`
- **Pattern reference:** `System.Net.Http.HttpHandlerDiagnosticListener` and `System.Net.Http.HttpTelemetry` in .NET runtime

## Mandatory Testing Policy

> **Tests are required and must never be skipped, disabled, or marked as `[Skip]`.** Every acceptance criterion that specifies "All tests pass" is a hard gate — the feature is not complete until every test passes. No `[Fact(Skip = "...")]`, no `#if` guards, no `Assert.Skip()`. If a test fails, fix the code or the test — never suppress it.

## Goals

- Emit diagnostic events for connection lifecycle (opened/closed)
- Emit diagnostic events for request lifecycle (start/stop)
- Emit diagnostic events for protocol-level operations (frames sent/received, headers decoded)
- Enable `dotnet-trace` and ETW collection without code changes
- Zero overhead when no listener is attached

## Tasks

### TASK-012-001: Create TurboHttpEventSource
**Description:** As an operator, I want ETW/EventPipe events so that I can use `dotnet-trace` to diagnose issues in production.

**Token Estimate:** ~35k tokens
**Predecessors:** none
**Successors:** TASK-012-003
**Parallel:** yes — can run alongside TASK-012-002

**Acceptance Criteria:**
- [x] New file `src/TurboHttp/Diagnostics/TurboHttpEventSource.cs`
- [x] Singleton `EventSource` named `"TurboHttp"` (or `"System.Net.TurboHttp"` for consistency with .NET)
- [x] Events defined (with EventId, Level, Keywords):
  - `ConnectionOpened(string host, int port, string protocol)` — Informational
  - `ConnectionClosed(string host, int port, double durationMs)` — Informational
  - `RequestStart(string method, string uri)` — Informational
  - `RequestStop(int statusCode, double durationMs)` — Informational
  - `RequestFailed(string exceptionType, string message)` — Warning
  - `HeadersDecoded(int headerCount, int compressedSize, int decompressedSize)` — Verbose
  - `FrameSent(string frameType, int streamId, int length)` — Verbose
  - `FrameReceived(string frameType, int streamId, int length)` — Verbose
  - `CacheHit(string uri)` — Informational
  - `CacheMiss(string uri)` — Informational
  - `RetryAttempt(string method, string uri, int attempt)` — Warning
  - `RedirectFollowed(string uri, int statusCode, string location)` — Informational
- [x] Keywords enum for filtering: `Connection`, `Request`, `Protocol`, `Cache`
- [x] Build succeeds with 0 errors

### TASK-012-002: Create TurboHttpDiagnosticListener
**Description:** As a library integrator, I want DiagnosticListener events so that I can subscribe to fine-grained events programmatically.

**Token Estimate:** ~30k tokens
**Predecessors:** none
**Successors:** TASK-012-003
**Parallel:** yes — can run alongside TASK-012-001

**Acceptance Criteria:**
- [x] New file `src/TurboHttp/Diagnostics/TurboHttpDiagnosticListener.cs`
- [x] Static `DiagnosticListener` named `"TurboHttp"`
- [x] Events (fired via `Write()` when `IsEnabled()` returns true):
  - `"TurboHttp.Request.Start"` — payload: HttpRequestMessage
  - `"TurboHttp.Request.Stop"` — payload: HttpResponseMessage, duration
  - `"TurboHttp.Request.Failed"` — payload: Exception
  - `"TurboHttp.Connection.Opened"` — payload: host, port, protocol
  - `"TurboHttp.Connection.Closed"` — payload: host, port, duration
- [x] `IsEnabled()` guard on every `Write()` call for zero-overhead when unsubscribed
- [x] Build succeeds with 0 errors

### TASK-012-003: Wire Diagnostics into Stages and Transport
**Description:** As a library author, I want diagnostic events emitted from the actual code paths.

**Token Estimate:** ~45k tokens
**Predecessors:** TASK-012-001, TASK-012-002
**Successors:** TASK-012-004
**Parallel:** no
**Model:** opus

**Acceptance Criteria:**
- [x] `ConnectionActorBase` / `ClientByteMover` emits ConnectionOpened/Closed events
- [x] Pipeline entry (HandlerBidiStage or TurboHandler) emits RequestStart/Stop/Failed events
- [x] HTTP/2 encoding/decoding stages emit FrameSent/FrameReceived at Verbose level
- [x] Cache, Retry, Redirect stages emit their respective events
- [x] All events use `IsEnabled()` guards
- [x] All existing tests pass (no behavioral change)
- [x] Build succeeds with 0 errors

### TASK-012-004: Diagnostics Unit Tests
**Description:** As a library author, I want tests verifying diagnostic events are emitted.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-012-003
**Successors:** none
**Parallel:** no

**Acceptance Criteria:**
- [x] Test file in `src/TurboHttp.Tests/`
- [x] Uses `DiagnosticListener.AllListeners.Subscribe()` to capture events
- [x] Uses `EventListener` to capture EventSource events
- [x] Verifies RequestStart/Stop events with correct payload
- [x] Verifies ConnectionOpened/Closed events
- [x] Verifies zero events when no listener is subscribed (IsEnabled guard)
- [x] All tests pass

## Task Dependency Graph

```
TASK-012-001 ──→ TASK-012-003 ──→ TASK-012-004
TASK-012-002 ──┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-012-001 | ~35k | none | yes (with 002) | — |
| TASK-012-002 | ~30k | none | yes (with 001) | — |
| TASK-012-003 | ~45k | 001, 002 | no | opus |
| TASK-012-004 | ~30k | 003 | no | — |

**Total estimated tokens:** ~140k

## Functional Requirements

- FR-1: EventSource must be collectible via `dotnet-trace --providers TurboHttp`
- FR-2: DiagnosticListener must fire events only when `IsEnabled()` is true
- FR-3: All events must carry enough context to correlate with a specific request
- FR-4: Verbose-level events (frames, headers) must not fire at Informational level
- FR-5: Keywords must allow filtering to specific event categories

## Non-Goals

- No custom ETW manifest (auto-generated from EventSource attributes)
- No DiagnosticListener integration with HttpClient's existing listener
- No EventCounter instruments (covered by Meter in Feature 011)

## Technical Considerations

- `EventSource` methods must be `[NonEvent]` wrappers calling `[Event]` methods for proper ETW metadata
- `DiagnosticListener.Write()` allocates anonymous objects for payload — use `IsEnabled()` guard to avoid allocation when unsubscribed
- Frame-level events (FrameSent/Received) can be very high volume — Verbose keyword filtering is essential
- EventSource singleton must be thread-safe (guaranteed by `EventSource` base class)

## Success Metrics

- `dotnet-trace --providers TurboHttp` captures connection and request events
- DiagnosticListener subscriber receives all expected events
- Zero allocation overhead when no subscriber is attached
