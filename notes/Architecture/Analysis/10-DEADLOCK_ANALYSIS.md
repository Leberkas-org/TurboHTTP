---
title: Deadlock Analysis Catalog
created: '2026-03-26'
tags:
  - architecture
  - deadlock
  - catalog
status: all-fixed
---
# Deadlock Analysis Catalog

Complete catalog of all known deadlock patterns in TurboHttp, organized by layer. Each entry includes root cause, affected files, fix status, and test coverage.

> **DL-009** and **DL-010** are **Fixed** — resolved by Feature 030 (IConnectionScope + linear topology rewrite).

---

## Async-Boundary Diagram

The core pipeline topology where most deadlocks occur:

```
ChannelSource
    │
    ▼
KillSwitch
    │
    ▼
┌─────────────────────────┐
│  RetryBidi  │  CacheBidi │   ◄── Feature BidiStages (feedback re-injection)
└─────────────────────────┘
    │
    ▼
GroupByHostKey ─────────────── [Source.Queue Boundary] ◄── fusion island break
    │
    ▼
Substream (per host-key)
    │
    ▼
ExtractOptions → Encoder → ConnectionStage → Decoder → ConnectionReuse
    │
    ▼
MergeSubstreams
    │
    ▼
Response outlet
```

**Key boundary**: `Source.Queue` inside `GroupByHostKeyStage` creates an async boundary between the feature BidiStage stack and per-host substreams. Callbacks from substream completion (`WatchTask`) run on different execution contexts than `onUpstreamFinish` in the BidiStages above.

---

## Deadlock Catalog

### Summary Table

| ID | Name | Category | Status |
|--------|----------------------------------------------|------------------------|-----------------|
| DL-001 | GroupByHostKey Two-Phase Completion | Akka.Streams Internal | Fixed |
| DL-002 | OfferAsync Timeout Race | Akka.Streams Internal | Fixed |
| DL-003 | Unknown Encoding Pass-Through | Akka.Streams Internal | Fixed |
| DL-004 | ConnectionStage Generation Guard | Akka.Streams Internal | Fixed |
| DL-005 | ConnectionReuse Signal Ordering | Akka.Streams Internal | Fixed |
| DL-006 | ExtractOptions Reconnection Window | HTTP/1.0 Pipeline | Fixed |
| DL-007 | MergeSubstreams Zombie Prevention | Akka.Streams Internal | Fixed |
| DL-008 | Feedback Buffer Backpressure | Akka.Streams Internal | Fixed |
| DL-009 | RetryBidi _inFlightCount Race | HTTP/1.0 Reconnect | Fixed |
| DL-010 | CacheBidi ReadAsByteArrayAsync Blocking | HTTP/1.0 Reconnect | Fixed |
| DL-011 | Materializer Buffer Sizing | Akka.Streams Internal | Fixed |
| DL-012 | ConnectionPool Semaphore Starvation | Transport Layer | Design Pattern |
| DL-013 | ClientState Channel Direction | Transport Layer | Design Pattern |

---

### DL-001: GroupByHostKey Two-Phase Completion

- **Category**: Akka.Streams Internal — Completion Race
- **Status**: Fixed
- **Root Cause**: `CompleteStage()` called immediately after substream queue completion, but downstream BidiStages (RetryBidiStage, CacheBidiStage) still hold re-injection requests. Outlet becomes dead before retry/cache can push back, causing silent hang.
- **Affected Files**: `Streams/Stages/Routing/GroupByHostKeyStage.cs`
- **Fix**: Implemented two-phase completion with `TryCompleteStage()` that defers stage completion until all substream `WatchTask`s report `IsDead == true`. Callbacks on WatchTask completion trigger final `CompleteStage()`.
- **Test IDs**: FBUF-001 through FBUF-006, Deadlock-H10-001, Reinjection-H10-001

### DL-002: OfferAsync Timeout Race

- **Category**: Akka.Streams Internal — Ask Pattern Timeout
- **Status**: Fixed
- **Root Cause**: `Source.Queue.OfferAsync()` internally uses Ask pattern with 5-second timeout. Between `IsDead` check and `OfferAsync` call, queue actor dies — waits for full timeout instead of detecting immediately.
- **Affected Files**: `Streams/Stages/Routing/GroupByHostKeyStage.cs` (line ~325-334)
- **Fix**: Race `offerTask` against `state.WatchTask` using `Task.WhenAny()`. When queue dies, WatchTask completes first, giving sub-millisecond detection instead of 5-second timeout.
- **Test IDs**: DLAK-002, Reinjection-H10-001, Reinjection-H10-002

### DL-003: Unknown Encoding Pass-Through

- **Category**: Akka.Streams Internal — Encoding Failure
- **Status**: Fixed
- **Root Cause**: Pipeline would deadlock if server returned unknown compression encoding. `ContentEncodingBidiStage` threw unhandled `HttpDecoderException` which killed the stage without propagating completion signals downstream.
- **Affected Files**: `Streams/Stages/Features/ContentEncodingBidiStage.cs`
- **Fix**: Catch `HttpDecoderException` and pass response through unchanged when encoding is unrecognized. Unknown encodings no longer kill the pipeline.
- **Test IDs**: Deadlock-H10-003

### DL-004: ConnectionStage Stale Callback Race (Generation Guard)

- **Category**: Akka.Streams Internal — Async Callback Race
- **Status**: Fixed
- **Root Cause**: After HTTP/1.0 connection close, inbound pump drains asynchronously. Stale async callbacks from the old pump could inject `CloseSignalItem` into the new connection's decoder via `GetAsyncCallback`, corrupting state.
- **Affected Files**: `Transport/ConnectionStage.cs`
- **Fix**: Introduced `_connectionGen` (generation counter) that increments on reconnect. Stale callbacks check generation before posting — generation mismatch means callback is ignored.
- **Test IDs**: CS-RC-001

### DL-005: ConnectionReuse Signal Ordering

- **Category**: Akka.Streams Internal — Outlet Ordering
- **Status**: Fixed
- **Root Cause**: If response outlet pushed before signal outlet, the redirect/retry feedback path was not yet set. Follow-up requests skip `ConnectItem` emission, causing connection setup to stall.
- **Affected Files**: `Streams/Stages/Features/ConnectionReuseStage.cs` (line ~61-67)
- **Fix**: Always `TryPushSignal()` before `TryPushResponse()`. Signal sets `_needsReconnect` flag in ExtractOptionsStage before response pushes through the fused graph.
- **Test IDs**: DLH10-004

### DL-006: ExtractOptions Reconnection Window

- **Category**: HTTP/1.0 Pipeline
- **Status**: Fixed
- **Root Cause**: `ExtractOptionsStage` emits `ConnectItem` only once via `_initialSent` flag. After HTTP/1.0 connection close, retry/redirect recirculated requests flow through encoder without a new `ConnectItem` — `ConnectionStage` has no handle to establish a new connection.
- **Affected Files**: `Streams/Stages/Routing/ExtractOptionsStage.cs`
- **Fix**: Eliminated entirely by Feature 030 architecture rewrite. `ConnectionStage` now uses `IConnectionScope` for auto-reconnect — when a `DataItem` arrives with `_handle == null`, it acquires a new connection via `scope.AcquireAsync()` using stored options. No `ConnectItem` feedback loop needed. `ExtractOptionsStage.InReuse` inlet removed; `_needsReconnect` field removed.
- **Test IDs**: DLH10-005, Reinjection-H10-001 through Reinjection-H10-003
- **See also**: [[07-HTTP10_RECONNECTION_LIMITATION]]

### DL-007: MergeSubstreams Zombie Prevention

- **Category**: Akka.Streams Internal — Substream Completion
- **Status**: Fixed
- **Root Cause**: If upstream finishes before all active substreams complete, zombie substream actors linger after materializer shutdown. `onUpstreamFailure` did not set `_upstreamDone`, so substream callbacks waited indefinitely.
- **Affected Files**: `Streams/Stages/Routing/MergeSubstreamsStage.cs` (line ~72-94)
- **Fix**: On `onUpstreamFailure`, set `_upstreamDone = true` so substream callbacks recognize terminal state and trigger `CompleteStage()` without hanging.
- **Test IDs**: DLAK-004, SURV-006 through SURV-008

### DL-008: Feedback Buffer Backpressure

- **Category**: Akka.Streams Internal — Backpressure Stall
- **Status**: Fixed
- **Root Cause**: Feedback path (redirect/retry re-injection) blocked when downstream backpressure prevented the response outlet from draining. Requests piled up in the feedback loop with no way to make progress.
- **Affected Files**: `Streams/ProtocolCoreGraphBuilder.cs` (feedback path wiring)
- **Fix**: Buffer feedback path with configurable capacity to decouple response consumption from re-injection request generation.
- **Test IDs**: FBUF-001 through FBUF-006

### DL-009: RetryBidi _inFlightCount Race

- **Category**: HTTP/1.0 Reconnect — In-Flight Request Window
- **Status**: Fixed (Feature 030, TASK-030-001)
- **Root Cause**: Window between `_inFlightCount` decrement (response received) and retry enqueue (decision to retry). If `TryCompleteIfDone()` fires in this window, it sees zero in-flight requests and closes the outlet prematurely. The retry request has nowhere to go.
- **Affected Files**: `Streams/Stages/Features/RetryBidiStage.cs`
- **Fix**: Added `_retryTransactionActive` boolean field as atomic transaction guard. Set `true` before retry evaluation, `false` after `_inFlightCount--` and `TryPullResponse()`. `TryCompleteIfDone()` returns early if transaction is active. Same pattern applied to `OnTimer()` delayed retry path.
- **Test IDs**: DLH10-001
- **Symptom** (before fix): Pipeline hangs ~10-15 seconds after 503 response when retry is attempted on HTTP/1.0 connection.

### DL-010: CacheBidi ReadAsByteArrayAsync Blocking

- **Category**: HTTP/1.0 Reconnect — Async Body Read
- **Status**: Fixed (Feature 030, TASK-030-003)
- **Root Cause**: `ReadAsByteArrayAsync()` holds the stage actor scope while the async body read runs on the thread pool. While the stage is blocked, `GroupByHostKeyStage` sees the substream queue as idle and calls `CompleteStage()` prematurely. The cache stage then waits for demand that never comes (Out1 is already cancelled).
- **Affected Files**: `Streams/Stages/Features/CacheBidiStage.cs`
- **Fix**: Added `_pendingAsyncRead` flag as backpressure guard. All `TryPull*` methods check `if (_pendingAsyncRead) return;` to prevent inlet pulls during async body reads. After async callback fires and `_pendingAsyncRead = false`, pulling resumes. GroupByHostKeyStage liveness guard (TASK-030-002) also defers completion while substreams are alive but idle.
- **Test IDs**: DLH10-002
- **Symptom** (before fix): Pipeline hangs ~10-15 seconds when CacheBidiStage attempts to cache response body from HTTP/1.0 connection.

### DL-011: Materializer Buffer Sizing

- **Category**: Akka.Streams Internal — Buffer Configuration
- **Status**: Fixed
- **Root Cause**: Default materializer buffer (16/16) insufficient for pipelined feedback loops. Responses arrive faster than they are consumed when redirect/retry chains are active, causing the entire pipeline to stall under backpressure.
- **Affected Files**: `Streams/TurboClientStreamManager.cs` (materializer configuration)
- **Fix**: Applied custom `ActorMaterializerSettings` with tuned `InputBuffer` sizing to prevent tight-loop backpressure stalls in feedback paths.
- **Test IDs**: MBUF-001 through MBUF-006

### DL-012: ConnectionPool Semaphore Starvation

- **Category**: Transport Layer — Semaphore Management
- **Status**: Design Pattern (preventive)
- **Root Cause**: If connection lease disposal fails to release semaphore on abrupt close, other `AcquireAsync` callers wait indefinitely on `SemaphoreSlim.WaitAsync()`. All connection slots become permanently consumed.
- **Affected Files**: `Transport/ConnectionPool.cs` (HostConnections, line ~92-112)
- **Fix**: `ConnectionLease.Dispose()` always calls `Release()` on semaphore via `finally` block, even on exception. `isAbruptClose` flag ensures cleanup path runs regardless of how the connection terminated.
- **Test IDs**: DLTP-001, DLTP-002

### DL-013: ClientState Channel Direction

- **Category**: Transport Layer — Channel State Machine
- **Status**: Design Pattern (preventive)
- **Root Cause**: If read pump exits but write pump tries to read from a pre-completed channel (or vice versa), the write pump hangs indefinitely waiting for data that will never arrive.
- **Affected Files**: `Transport/ClientState.cs` (line ~41-70, 89-102)
- **Fix**: `ClientState` constructor accepts `StreamDirection` enum (`ReadOnly`, `WriteOnly`, `Bidirectional`). Pre-completes unused channels so unused pumps exit immediately without blocking.
- **Test IDs**: DLTP-005

---

## Categories

### Akka.Streams Internal (DL-001 through DL-005, DL-007, DL-008, DL-011)
Completion races, async callback timing, buffer sizing, and outlet ordering within the Akka.Streams fusion framework. Most are caused by the `Source.Queue` async boundary inside `GroupByHostKeyStage`.

### HTTP/1.0 Reconnect (DL-006, DL-009, DL-010)
Deadlocks specific to HTTP/1.0 connection-close semantics where the TCP connection must be re-established for retry/redirect/cache operations. The connection close propagates through stages faster than the feature BidiStages can react.

### Transport Layer (DL-012, DL-013)
Preventive design patterns in the connection pool and byte mover to avoid semaphore starvation and channel state machine deadlocks.

---

## Status Legend

| Status | Meaning |
|-----------------|---------|
| **Fixed** | Root cause identified and fix implemented with regression tests |
| **Known Limitation** | Understood behavior with documented workaround, not yet fully resolved |
| **Active Bug** | Confirmed bug, tests written as Skip, fix pending in separate task |
| **Design Pattern** | Preventive pattern built into the architecture to avoid the deadlock class |
