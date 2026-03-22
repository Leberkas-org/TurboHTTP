# Feature 002: Client Transport Hardening — QUIC Multiplexing, Async Disposal & Error Propagation

## Introduction

Analysis of the TurboHttp client transport layer revealed three bugs that will surface as blockers or flaky failures once integration tests exercise real TCP/QUIC connections. This feature fixes all three:

1. **QuicClientProvider** is fundamentally single-stream — it opens one bidirectional QUIC stream per connection, defeating HTTP/3 multiplexing. The entire pooling layer assumes one-stream-per-ConnectionActor, which wastes QUIC connections.
2. **QuicClientProvider.Close()** calls `DisposeAsync()` fire-and-forget (`_ = connection.DisposeAsync()`), leaking QUIC connection-close frames and causing flaky test teardown.
3. **ConnectionStage** swallows `WriteAsync` failures on the outbound channel — when the connection drops, errors are silently ignored and the stage keeps pulling, causing downstream hangs instead of clean failure propagation.

### Architecture Context

- **Components involved:**
  - `QuicClientProvider` — QUIC transport, stream opening
  - `ConnectionActor` — connection lifecycle (currently: one stream per actor)
  - `HostPool` — connection selection, stream capacity tracking
  - `ConnectionHandle` — channel bundle for zero-actor-hop data flow
  - `ConnectionStage` — GraphStage bridging channels into Akka event loop
  - `ClientByteMover` / `ClientRunner` / `ClientState` — async pump tasks (no changes needed)
  - `IClientProvider` — transport interface (needs extension for QUIC)
  - `PerHostConnectionLimiter` — per-host connection limits (needs version awareness)
- **Patterns preserved:** Actor hierarchy for lifecycle, channels for data, zero-actor-hop data path
- **New pattern introduced:** Connection vs. stream lifecycle separation for QUIC

## Goals

- HTTP/3 requests can multiplex over a single QUIC connection (multiple bidirectional streams)
- QUIC connections are properly closed with awaited `DisposeAsync()`
- ConnectionStage propagates outbound write failures cleanly via `FailStage()`
- All existing HTTP/1.x and HTTP/2 behavior remains unchanged (no regressions)
- Pooling layer is version-aware: HTTP/3 reuses one connection per host, opens new streams on demand

## Tasks

### TASK-007-001: Make QuicClientProvider Reentrant for Multi-Stream Support

**Description:** As a client making multiple HTTP/3 requests, I want each request to get its own bidirectional QUIC stream on the same connection so that requests are multiplexed per RFC 9114.

**Token Estimate:** ~50k tokens
**Predecessors:** none
**Successors:** TASK-007-003, TASK-007-004
**Parallel:** yes — can run alongside TASK-007-002
**Model:** opus

**Current behavior:**
- `GetStreamAsync()` calls `QuicConnection.ConnectAsync()` AND `OpenOutboundStreamAsync()` in one shot
- Second call overwrites `_connection` field with a new QUIC connection
- Result: one stream per connection, no multiplexing

**Required changes in `src/TurboHttp/Transport/QuicClientProvider.cs`:**
- Split `GetStreamAsync()` into two phases:
  - `ConnectAsync()` — establishes the QUIC connection once (lazy, thread-safe via `SemaphoreSlim` or lock)
  - `OpenStreamAsync()` — opens a new bidirectional stream on the existing connection (called per request)
- Keep `IClientProvider.GetStreamAsync()` as the public API but make it reentrant:
  - First call: connect + open stream
  - Subsequent calls: reuse connection, open new stream
- Add `IsConnected` property to check if QUIC connection is alive
- Handle `QuicException` when connection is closed — trigger reconnect via ConnectionActor

**Required changes in `src/TurboHttp/Transport/IClientProvider.cs`:**
- Add optional `bool SupportsMultipleStreams { get; }` property (default: `false` for TCP/TLS, `true` for QUIC)
- This lets ConnectionActor/HostPool query whether to request new streams vs. new connections

**Acceptance Criteria:**
- [x] First `GetStreamAsync()` call establishes QUIC connection and opens stream
- [x] Subsequent calls reuse the connection and open new streams
- [x] Connection establishment is thread-safe (concurrent calls don't create multiple connections)
- [x] `QuicException` on dead connection is caught and thrown as reconnectable error
- [x] `SupportsMultipleStreams` returns `true` for `QuicClientProvider`, `false` for TCP/TLS
- [x] Existing `TcpClientProvider` and `TlsClientProvider` are unchanged
- [x] Unit tests verify multi-stream behavior

---

### TASK-007-002: Fix Async Disposal in QuicClientProvider

**Description:** As a test runner tearing down fixtures, I want QUIC connections to close cleanly so that ports are released and servers see proper connection-close frames.

**Token Estimate:** ~20k tokens
**Predecessors:** none
**Successors:** TASK-007-003
**Parallel:** yes — can run alongside TASK-007-001

**Current behavior:**
- `Close()` and `CloseConnection()` both call `_ = connection.DisposeAsync()` — discarding the ValueTask
- Connection-close frame may never be sent; server sees abrupt disconnect
- In tests: port may still be bound when next test starts

**Required changes in `src/TurboHttp/Transport/QuicClientProvider.cs`:**
- Make `IClientProvider` extend `IAsyncDisposable` (add `ValueTask DisposeAsync()`)
- Keep `Close()` for backward compat but have it call `DisposeAsync().AsTask().GetAwaiter().GetResult()` as last resort — or mark `[Obsolete]`
- `DisposeAsync()`: await `_connection.DisposeAsync()`, catch `ObjectDisposedException`
- `CloseConnection()` helper: same pattern, await the disposal
- Update `ConnectionActor` to call `DisposeAsync()` on the provider during `PostStop()` — use `PipeTo` pattern for async in actor context

**Required changes in `src/TurboHttp/Transport/IClientProvider.cs`:**
- Add `: IAsyncDisposable` to interface
- Default implementation in `TcpClientProvider`/`TlsClientProvider`: close socket/stream synchronously (wrap in ValueTask)

**Acceptance Criteria:**
- [x] `IClientProvider` extends `IAsyncDisposable`
- [x] `QuicClientProvider.DisposeAsync()` awaits `QuicConnection.DisposeAsync()`
- [x] `TcpClientProvider` and `TlsClientProvider` implement `DisposeAsync()` (close socket)
- [x] No fire-and-forget `_ = DisposeAsync()` calls remain in codebase
- [x] `ConnectionActor.PostStop()` disposes provider asynchronously
- [x] Unit test: verify disposal completes before actor terminates

---

### TASK-007-003: Version-Aware Connection Pooling in HostPool & ConnectionActor

**Description:** As the pooling layer, I want to distinguish between "new connection needed" (TCP/TLS) and "new stream on existing connection needed" (QUIC) so that HTTP/3 multiplexes correctly.

**Token Estimate:** ~75k tokens
**Predecessors:** TASK-007-001, TASK-007-002
**Successors:** TASK-007-005
**Parallel:** no
**Model:** opus

**Current behavior:**
- `ConnectionActor` calls `GetStreamAsync()` once in `PreStart()`, creates one channel pair, one ConnectionHandle
- When all stream slots are used, `HostPool` spawns a **new ConnectionActor** = new QUIC connection
- `PerHostConnectionLimiter` enforces 6 connections per host (HTTP/1.x rule), applied to all protocols

**Required changes in `src/TurboHttp/Pooling/ConnectionActor.cs`:**
- Check `IClientProvider.SupportsMultipleStreams`:
  - `false` (TCP/TLS): current behavior — one stream, one channel pair
  - `true` (QUIC): on `GetStreamAsync()`, open a new stream on the existing connection; create **new channel pair and ClientRunner per stream**
- Add message `OpenNewStream` that HostPool can send to request another stream on the same connection
- Response: `StreamReady(ConnectionHandle)` with stream-specific channels
- Track active stream count internally

**Required changes in `src/TurboHttp/Pooling/HostPool.cs`:**
- `SelectConnection()`: for HTTP/3, prefer sending `OpenNewStream` to existing ConnectionActor over spawning new one
- Only spawn new ConnectionActor when existing connection's stream limit is reached
- `PerHostConnectionLimiter`: skip for HTTP/3 (one QUIC connection per host is optimal)

**Required changes in `src/TurboHttp/Pooling/ConnectionState.cs`:**
- Add `bool SupportsMultipleStreams` property
- `HasAvailableSlot` logic remains the same (PendingRequests < MaxConcurrentStreams)

**Acceptance Criteria:**
- [ ] HTTP/1.x and HTTP/2 pooling behavior is unchanged (regression test)
- [ ] HTTP/3: first request spawns ConnectionActor + QUIC connection + stream
- [ ] HTTP/3: subsequent requests reuse ConnectionActor, open new QUIC stream
- [ ] HTTP/3: `PerHostConnectionLimiter` does not limit QUIC connections
- [ ] HTTP/3: when stream limit is reached, new ConnectionActor is spawned as fallback
- [ ] HostPool unit tests for version-aware stream selection
- [ ] ConnectionActor unit tests for `OpenNewStream` message handling

---

### TASK-007-004: Fix ConnectionStage Outbound Write Error Propagation

**Description:** As a stage processing outbound data, I want write failures to propagate cleanly so that connection-abort tests fail fast instead of hanging.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-007-001
**Successors:** TASK-007-005
**Parallel:** yes — can run alongside TASK-007-003

**Current behavior in `src/TurboHttp/Transport/ConnectionStage.cs:209-214`:**
```csharp
_ = handle.OutboundWriter
    .WriteAsync(...)
    .AsTask()
    .ContinueWith(_ => _onOutboundWriteDone!(), TaskContinuationOptions.ExecuteSynchronously);
```
- `ContinueWith` fires on **any** completion (success, fault, cancel) — no error check
- If channel is closed (connection dead), `WriteAsync` throws but exception is swallowed
- Stage pulls next element, which also fails — infinite silent failure loop

**Required changes in `src/TurboHttp/Transport/ConnectionStage.cs`:**
- Split ContinueWith into two branches:
  - `OnlyOnRanToCompletion`: call `_onOutboundWriteDone!()` (pull next element)
  - `OnlyOnFaulted`: call new `_onOutboundWriteFailed` callback that invokes `FailStage(exception)`
- Add `_onOutboundWriteFailed` async callback in `PreStart()`:
  ```csharp
  _onOutboundWriteFailed = GetAsyncCallback<Exception>(ex =>
  {
      Log.Warning("ConnectionStage: Outbound write failed — {0}", ex.Message);
      FailStage(ex);
  });
  ```
- Handle `OnlyOnCanceled` case: treat as clean shutdown (don't pull, don't fail)

**Acceptance Criteria:**
- [ ] Successful writes still trigger pull of next element
- [ ] Failed writes (channel closed) call `FailStage()` with the exception
- [ ] Cancelled writes (stage shutdown) do not trigger pull or fail
- [ ] No silent error swallowing remains in outbound write path
- [ ] Unit test: ConnectionStage with closed channel fails cleanly
- [ ] Existing stream tests pass (no regressions from changed write handling)

---

### TASK-007-005: Integration Verification & Regression Gate

**Description:** As a developer, I want to verify that all three fixes work together and no existing tests break.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-007-003, TASK-007-004
**Successors:** none
**Parallel:** no

**Acceptance Criteria:**
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors
- [ ] `dotnet test src/TurboHttp.Tests/TurboHttp.Tests.csproj` — all tests pass
- [ ] `dotnet test src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj` — all tests pass
- [ ] No new compiler warnings related to changed files
- [ ] Grep codebase for remaining `_ = *.DisposeAsync()` patterns — zero hits
- [ ] Grep codebase for `ContinueWith` without explicit `TaskContinuationOptions.OnlyOn*` — zero hits in ConnectionStage

## Task Dependency Graph

```
TASK-007-001 ──→ TASK-007-003 ──→ TASK-007-005
                                ↗
TASK-007-002 ──→ TASK-007-003

TASK-007-001 ──→ TASK-007-004 ──→ TASK-007-005
```

| Task | Title | Estimate | Predecessors | Parallel | Model |
|------|-------|----------|--------------|----------|-------|
| TASK-007-001 | QuicClientProvider multi-stream | ~50k | none | yes (with 002) | opus |
| TASK-007-002 | Async disposal fix | ~20k | none | yes (with 001) | — |
| TASK-007-003 | Version-aware pooling | ~75k | 001, 002 | no | opus |
| TASK-007-004 | ConnectionStage error propagation | ~30k | 001 | yes (with 003) | — |
| TASK-007-005 | Verification gate | ~25k | 003, 004 | no | — |

**Total estimated tokens:** ~200k

## Functional Requirements

- FR-1: `QuicClientProvider.GetStreamAsync()` must be callable multiple times, returning a new bidirectional QUIC stream each time on the same connection
- FR-2: QUIC connection establishment must be lazy (first call only) and thread-safe
- FR-3: `IClientProvider` must expose `SupportsMultipleStreams` so pooling can branch on protocol
- FR-4: `IClientProvider` must extend `IAsyncDisposable`; all implementations must properly await disposal
- FR-5: `HostPool` must prefer opening new streams on existing QUIC connections over spawning new ConnectionActors
- FR-6: `PerHostConnectionLimiter` must not count HTTP/3 connections against the per-host limit
- FR-7: `ConnectionStage` must call `FailStage()` when an outbound `WriteAsync` fails
- FR-8: All changes must be backward-compatible with HTTP/1.x and HTTP/2 behavior

## Non-Goals

- No QPACK unidirectional stream support (encoder/decoder instruction streams) — separate feature
- No GOAWAY state propagation to ConnectionReuseStage — separate feature
- No server certificate threading for cross-origin coalescing — separate feature
- No changes to `ClientByteMover`, `ClientRunner`, or `ClientState` (data pump is already protocol-agnostic)
- No integration test classes (those are Features 002-006)

## Technical Considerations

- **Thread safety in QuicClientProvider**: `GetStreamAsync()` will be called concurrently by multiple ConnectionActors (or by HostPool requesting streams). Use `SemaphoreSlim(1,1)` for connection establishment, but allow concurrent `OpenOutboundStreamAsync()` calls (QUIC handles this internally).
- **Actor async patterns**: `ConnectionActor.PostStop()` is synchronous. For async disposal, use `PipeTo(Self)` pattern or fire the dispose and accept best-effort cleanup (QUIC connections have idle timeouts as fallback).
- **Backward compatibility**: `IClientProvider.SupportsMultipleStreams` should have a default interface implementation returning `false` so existing TCP/TLS providers don't need changes.
- **Stream limit**: `QuicOptions.MaxBidirectionalStreams` (default: 100) serves as the stream capacity for HTTP/3, equivalent to HTTP/2's `SETTINGS_MAX_CONCURRENT_STREAMS`.

## Success Metrics

- HTTP/3 requests multiplex over a single QUIC connection (verified by stream count)
- Zero `_ = *.DisposeAsync()` fire-and-forget patterns in transport code
- ConnectionStage fails cleanly on write errors (no hangs in error scenarios)
- All 1800+ existing unit and stream tests continue to pass

## Open Questions

*None — all questions resolved during analysis.*
