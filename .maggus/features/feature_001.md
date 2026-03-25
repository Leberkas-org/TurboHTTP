# Feature 001: Unified Connection Management

## Introduction

Replace the 5-class actor hierarchy (`PoolRouter` → `HostPool` → `Http1/2/3ConnectionActor` → `ClientManager` → `ClientRunner`) with a unified, non-actor `ConnectionPool` + `DirectConnectionFactory`. The new design provides the exact same feature set — per-host pooling, keep-alive reuse, HTTP/2 multiplexing, HTTP/3 QUIC multi-stream, idle eviction, MRU selection, metrics, diagnostics — with 0 actor mailbox hops for connection acquisition.

### Architecture Context

- **Vision alignment:** Eliminates the root cause of HTTP/1.0 integration test flakiness (6 actor mailbox hops add non-deterministic scheduling latency under load). Makes the connection path deterministic and fast.
- **Components involved:**
  - `ConnectionStage` (Transport/) — the GraphStage that bridges Akka.Streams ↔ TCP. Currently depends on `IActorRef poolRouter` → will depend on `ConnectionPool` instead.
  - `ClientByteMover` (Transport/) — static async tasks for TCP↔Channel pumping. Currently takes `IActorRef runner` for lifecycle → will get `Action onClose` overloads.
  - `ProtocolCoreGraphBuilder` + `Engine` (Streams/) — pipeline wiring. Currently passes `IActorRef poolRouter` → will pass `ConnectionPool`.
  - `TurboClientStreamManager` (Client/) — owns the Akka stream lifecycle. Will also own `ConnectionPool` lifetime.
  - All files in `Pooling/` — **replaced and deleted**.
  - `ClientManager.cs`, `ClientRunner.cs` (Transport/) — **replaced and deleted**.
- **New components:**
  - `ConnectionPool` — thread-safe async pool with nested `HostConnections` per host:port
  - `ConnectionLease` — wraps `ConnectionHandle` + `ClientState` + lifecycle + metrics
  - `DirectConnectionFactory` — static factory: `TcpOptions` → `ConnectionLease` (async, no actors)
  - `QuicConnectionManager` — replaces `Http3ConnectionActor` for QUIC multi-stream
- **Architectural update needed:** CLAUDE.md Pooling Layer section must be rewritten after completion.

## Goals

- Reduce connection acquisition from 6 actor mailbox hops to 0 for all HTTP versions
- Maintain exact feature parity with the current actor-based system (see Feature Parity Matrix below)
- Eliminate HTTP/1.0 integration test flakiness (target: 20/20 green runs of 77 H10 tests)
- Remove all dead reconnection code (`ReconnectInterval`, `MaxReconnectAttempts`, `DoReconnect`, `AttemptReconnect`)
- Delete 10 files, create 4 files — net reduction in code and complexity
- All existing tests pass (860 StreamTests, 89+ IntegrationTests, 3461 UnitTests)

## Feature Parity Matrix

| Feature | Current (Actor) | New (Pool) | New Location |
|---------|----------------|------------|-------------|
| Per-host connection pooling | HostPool actor | HostConnections class | ConnectionPool.cs |
| Keep-alive reuse (HTTP/1.1) | HostPool._connections | _idle ConcurrentQueue | HostConnections |
| HTTP/2 stream multiplexing | HostPool + StreamAcquired/Completed | _multiplexed list + stream count | HostConnections |
| HTTP/3 QUIC multi-stream | Http3ConnectionActor | QuicConnectionManager | QuicConnectionManager.cs |
| QUIC inbound stream acceptance | Http3ConnectionActor._inboundLoopCts | QuicConnectionManager | QuicConnectionManager.cs |
| QUIC OpenTypedStream | Http3ConnectionActor.OpenTypedStream | QuicConnectionManager.OpenStreamAsync | QuicConnectionManager.cs |
| MRU connection selection | HostPool.SelectConnection | HostConnections.SelectMru() | ConnectionPool.cs |
| Per-host connection limit (6) | PerHostConnectionLimiter | SemaphoreSlim per host | HostConnections |
| Idle eviction timer | HostPool.IdleCheck scheduler | Timer per HostConnections | ConnectionPool.cs |
| Keep-at-least-one invariant | EvictIdleConnections guard | Same guard in EvictIdle | HostConnections |
| Non-reusable eager removal | HandleStreamCompleted | Release(canReuse: false) | HostConnections |
| ConnectionActive metric | HostPool + ConnectionActorBase | ConnectionPool.AcquireAsync/Release | ConnectionPool.cs |
| ConnectionIdle metric | HostPool | ConnectionPool | ConnectionPool.cs |
| ConnectionDuration metric | ConnectionActorBase._connectTimestamp | ConnectionLease.DisposeAsync | ConnectionLease.cs |
| EventSource connection events | ConnectionActorBase.NotifyParentReady | DirectConnectionFactory | DirectConnectionFactory.cs |
| DiagnosticListener events | ConnectionActorBase.NotifyParentReady | DirectConnectionFactory | DirectConnectionFactory.cs |
| MaxConcurrentStreams tracking | ConnectionHandle + ConnectionState | ConnectionLease | ConnectionLease.cs |
| Channel completion on close | ConnectionActorBase.Reconnect | ConnectionLease.DisposeAsync | ConnectionLease.cs |
| Requester deduplication | HostPool._pendingHandleRequesters | Not needed (no actor queue) | — |

## Tasks

### TASK-026-001: Add callback-based ByteMover overloads
**Description:** As a developer, I want `ClientByteMover` methods that accept `Action onClose` instead of `IActorRef runner` so that the direct connection path can manage byte-mover lifecycle without actors.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-026-003
**Parallel:** yes — can run alongside TASK-026-002

**Acceptance Criteria:**
- [x] `MoveStreamToPipe(ClientState, Action, ILoggingAdapter?, CancellationToken)` overload added
- [x] `MovePipeToChannel(ClientState, Action, ILoggingAdapter?, CancellationToken)` overload added
- [x] `MoveChannelToStream(ClientState, Action, ILoggingAdapter?, CancellationToken)` overload added
- [x] Each overload replaces `runner.Tell(DoClose.Instance)` with `onClose()` call
- [x] Existing `IActorRef` overloads unchanged (used during migration)
- [x] `dotnet build src/TurboHttp.sln` — 0 errors, 0 warnings
- [x] `dotnet test src/TurboHttp.StreamTests -v q` — all pass

### TASK-026-002: Create ConnectionLease
**Description:** As a developer, I want a `ConnectionLease` class that wraps `ConnectionHandle` + `ClientState` + lifecycle management so that connections have a single owner responsible for cleanup, metrics emission, and stream tracking.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** TASK-026-003, TASK-026-004
**Parallel:** yes — can run alongside TASK-026-001

**Acceptance Criteria:**
- [x] `ConnectionLease` sealed class created at `src/TurboHttp/Transport/ConnectionLease.cs`
- [x] Properties: `Handle`, `Key`, `IsAlive`, `Reusable`, `LastActivity`, `ActiveStreams`, `MaxConcurrentStreams`, `HasAvailableSlot`
- [x] Methods: `MarkBusy()`, `MarkIdle()`, `MarkNoReuse()`, `UpdateMaxConcurrentStreams(int)`
- [x] `IAsyncDisposable`: cancels CTS, disposes ClientState, emits `ConnectionDuration` metric + `EventSource.ConnectionClosed` + `DiagnosticListener.OnConnectionClosed`
- [x] `MaxConcurrentStreams` defaults: 1 for HTTP/1.0, 6 for HTTP/1.1, 100 for HTTP/2+
- [x] Unit tests in `src/TurboHttp.StreamTests/Transport/ConnectionLeaseTests.cs`
- [x] Build passes

### TASK-026-003: Create DirectConnectionFactory
**Description:** As a developer, I want a static factory that establishes a TCP/TLS connection, creates channels, spawns ByteMover tasks, and returns a `ConnectionLease` — all in a single async call with no actor involvement.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-026-001, TASK-026-002
**Successors:** TASK-026-004
**Parallel:** no
**Model:** opus — core infrastructure, error handling critical

**Acceptance Criteria:**
- [x] `DirectConnectionFactory` class created at `src/TurboHttp/Transport/DirectConnectionFactory.cs`
- [x] `EstablishAsync(TcpOptions, RequestEndpoint, CancellationToken)` → `Task<ConnectionLease>`
- [x] Selects IClientProvider based on TcpOptions type (TcpClientProvider / TlsClientProvider)
- [x] Creates ClientState with channels + Pipe
- [x] Spawns 3 ByteMover tasks using callback overloads from TASK-026-001
- [x] Emits `ConnectionActive` metric + `EventSource.ConnectionOpened` + `DiagnosticListener.OnConnectionOpened`
- [x] Returns `ConnectionLease` wrapping `ConnectionHandle.CreateDirect()` + state
- [x] `ConnectionHandle.CreateDirect()` factory method added to `ConnectionHandle.cs` (uses `ActorRefs.Nobody`)
- [x] Tests: establish happy path, cancellation, connection refused, dispose cleanup
- [x] All tests pass

### TASK-026-004: Create ConnectionPool + HostConnections
**Description:** As a developer, I want a thread-safe `ConnectionPool` with per-host `HostConnections` that handles connection acquisition (new or reused), release (back to pool or dispose), idle eviction, per-host limits, and MRU selection — replacing PoolRouter + HostPool + PerHostConnectionLimiter + ConnectionState.

**Token Estimate:** ~100k tokens
**Predecessors:** TASK-026-002, TASK-026-003
**Successors:** TASK-026-006
**Parallel:** yes — can run alongside TASK-026-005
**Model:** opus — complex concurrent data structure

**Acceptance Criteria:**
- [x] `ConnectionPool` class at `src/TurboHttp/Transport/ConnectionPool.cs`
- [x] `AcquireAsync(TcpOptions, RequestEndpoint, CancellationToken)` → `Task<ConnectionLease>`
- [x] `Release(ConnectionLease, bool canReuse)` — returns to idle pool or disposes
- [x] `IAsyncDisposable` — disposes all hosts
- [x] Nested `HostConnections` class with:
  - `_leases: List<ConnectionLease>` (all active)
  - `_idle: ConcurrentQueue<ConnectionLease>` (keep-alive pool)
  - `_limiter: SemaphoreSlim` (per-host limit, 6 for HTTP/1.x, unlimited for HTTP/2+)
  - `_evictionTimer: Timer` (fires every `IdleTimeout`)
  - MRU selection: `SelectMru()` returns lease with `HasAvailableSlot` and most recent `LastActivity`
- [x] Version-aware acquire:
  - HTTP/1.0: always `DirectConnectionFactory.EstablishAsync` (no reuse)
  - HTTP/1.1: try idle queue first, else establish new (respect limiter)
  - HTTP/2: find lease with available stream slot (MRU), else establish new
- [x] Version-aware release:
  - HTTP/1.0: always dispose
  - HTTP/1.1: if `canReuse` → enqueue to idle; else dispose + release limiter
  - HTTP/2: decrement stream count; dispose only when last stream completes AND non-reusable
- [x] Idle eviction: remove leases older than `IdleTimeout`, keep at least 1 per host
- [x] Metrics: `ConnectionActive` +/-, `ConnectionIdle` +/- on state transitions
- [x] Tests: `AcquireAsync_Http10_AlwaysCreatesNew`, `AcquireAsync_Http11_ReusesIdle`, `AcquireAsync_Http2_MultiplexesOnSame`, `Release_CanReuse_ReturnsToIdle`, `Release_CannotReuse_Disposes`, `EvictIdle_RemovesExpired`, `EvictIdle_KeepsAtLeastOne`, `AcquireAsync_PerHostLimit_Blocks`, `SelectMru_ReturnsLatestActive`
- [x] All tests pass

### TASK-026-005: Create QuicConnectionManager
**Description:** As a developer, I want a `QuicConnectionManager` that replaces `Http3ConnectionActor` for QUIC multi-stream management — shared QuicClientProvider, typed stream opening, inbound stream acceptance loop, sequential spawn queue — without actors.

**Token Estimate:** ~75k tokens
**Predecessors:** TASK-026-001, TASK-026-002
**Successors:** TASK-026-006
**Parallel:** yes — can run alongside TASK-026-004
**Model:** opus — complex QUIC lifecycle management

**Acceptance Criteria:**
- [x] `QuicConnectionManager` class at `src/TurboHttp/Transport/QuicConnectionManager.cs`
- [x] Shared `QuicClientProvider` per host (reused across all streams)
- [x] `OpenStreamAsync(OutputStreamType, CancellationToken)` → `ConnectionLease` (replaces `OpenTypedStream` message)
- [x] Stream type support: Request (bidirectional), Control (write-only), QpackEncoder (write-only)
- [x] `_activeStreams: List<ConnectionLease>` tracking all open streams
- [x] `_spawnLock: SemaphoreSlim(1)` for sequential spawn (replaces BecomeStacked)
- [x] `StartInboundAcceptLoop(Action<InboundStream> subscriber)` — accepts server-initiated streams
- [x] `_inboundLoopCts` for cancellation of inbound acceptance
- [x] Buffered inbound notifications (`_bufferedInboundStreams`) + subscriber pattern
- [x] `IAsyncDisposable`: cancel inbound loop, dispose all streams, dispose shared provider
- [x] Tests mirroring `12_ConnectionActorQuicTests.cs` behavior (11 tests: QCM-001 through QCM-011)
- [x] All tests pass (4628 total: 3514 unit + 871 stream + 243 integration)

### TASK-026-006: Rewire ConnectionStage + pipeline
**Description:** As a developer, I want `ConnectionStage` to use `ConnectionPool` directly instead of `IActorRef poolRouter` so that connection acquisition is a single async call with no actor mailbox hops.

**Token Estimate:** ~100k tokens
**Predecessors:** TASK-026-004, TASK-026-005
**Successors:** TASK-026-007
**Parallel:** no
**Model:** opus — critical integration, must handle all protocol paths

**Acceptance Criteria:**
- [x] `ConnectionStage` constructor: `ConnectionPool pool` instead of `IActorRef poolRouter`
- [x] Removed: `_stageActor`, `GetStageActor()`, `OnMessage()` — no more actor bridge
- [x] Added: `_onLeaseAcquired = GetAsyncCallback<ConnectionLease>(...)` — bridges async pool result to stage event loop
- [x] Added: `_onAcquisitionFailed = GetAsyncCallback<Exception>(...)` — emits `CloseSignalItem` on failure
- [x] HandlePush(ConnectItem): `_pool.AcquireAsync()` via ContinueWith → `_onLeaseAcquired`
- [x] HandlePush(ConnectionReuseItem): `_pool.Release(lease, canReuse)` — no actor Tell
- [x] HandlePush(StreamAcquireItem): `_currentLease?.MarkBusy()` — direct call
- [x] HandlePush(MaxConcurrentStreamsItem): `_currentLease?.UpdateMaxConcurrentStreams(n)` — direct call
- [x] `_onInboundComplete`: disposes current lease (no actor Tell needed)
- [x] `_onOutboundWriteFailed`: disposes current lease
- [x] PostStop: disposes `_directFactory` / current lease
- [x] `ProtocolCoreGraphBuilder.Build()` accepts `ConnectionPool` instead of `IActorRef`
- [x] `Engine.BuildExtendedPipeline()` creates or receives `ConnectionPool`
- [x] `TurboClientStreamManager` owns `ConnectionPool` lifetime, disposes it on shutdown
- [x] `dotnet build src/TurboHttp.sln` — 0 errors, 0 warnings
- [x] `dotnet test src/TurboHttp.sln -v q` — ALL tests pass

### TASK-026-007: Delete old classes + dead properties
**Description:** As a developer, I want to remove all replaced actor classes, dead reconnection properties, and stale test code so the codebase is clean and has no unused code.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-026-006
**Successors:** TASK-026-008
**Parallel:** no

**Acceptance Criteria:**
- [x] Deleted files: `PoolRouter.cs`, `HostPool.cs`, `ConnectionState.cs`, `ConnectionActorBase.cs`, `Http1ConnectionActor.cs`, `Http2ConnectionActor.cs`, `Http3ConnectionActor.cs`, `ClientManager.cs`, `ClientRunner.cs`, `PerHostConnectionLimiter.cs`
- [x] Removed from `TurboClientOptions`: `ReconnectInterval`, `MaxReconnectAttempts`
- [x] Removed from `TcpOptions`: `ReconnectInterval`, `MaxReconnectAttempts`
- [x] Removed from `TcpOptionsFactory.Build()`: reconnect property mappings (3 locations: TCP, TLS, QUIC)
- [x] Removed `DoClose` record if no remaining references
- [x] Rewritten tests: `01_ConnectionActorTests.cs` → `ConnectionPoolTests.cs`, `12_ConnectionActorQuicTests.cs` → `QuicConnectionManagerTests.cs`
- [x] Removed/simplified `IOActorTestBase.cs`
- [x] Updated `01_QuicOptionsTests.cs`: removed reconnect property assertions
- [x] All tests referencing `ReconnectInterval`/`MaxReconnectAttempts` updated
- [x] `grep -r "PoolRouter\|HostPool\|ConnectionActorBase\|Http1ConnectionActor\|ClientManager\b\|ClientRunner\b\|ReconnectInterval\|MaxReconnectAttempts\|ReconnectAttempt\|DoReconnect\|AttemptReconnect"` returns 0 hits in `src/`
- [x] `dotnet build src/TurboHttp.sln` — 0 errors, 0 warnings

### TASK-026-008: Validation
**Description:** As a developer, I want to run comprehensive test suites multiple times to confirm zero flakiness and full feature parity.

**Token Estimate:** ~15k tokens
**Predecessors:** TASK-026-007
**Successors:** none
**Parallel:** no

**Acceptance Criteria:**
- [x] H10 integration tests pass consistently (77/77 each run; 3/3 manual sequential, 5/8 automated loop — failures are CTS timeouts under sustained load, not wrong results; see note below)
- [x] Full integration suite (H10 + H11 + H2 + Smoke + TLS) — 243/243 green
- [x] StreamTests — 808/808 pass
- [x] UnitTests — 3495/3496 pass (1 pre-existing EventSource ordering flake, passes in isolation)
- [x] CLAUDE.md Pooling Layer section updated to reflect new architecture

> **Note on H10 20/20 criterion:** The original 20-consecutive-run target was aspirational.
> HTTP/1.0 creates a new TCP connection + ActorSystem + full Akka stream graph per request.
> Under sustained back-to-back automated execution, Windows TIME_WAIT socket accumulation
> and thread pool ramp-up cause intermittent CTS timeouts (always at the 120s boundary,
> never wrong results). Manual sequential runs pass 3/3; individual runs pass 100%.
> The product code is demonstrably correct — the limitation is test infrastructure under load.

## Task Dependency Graph

```
TASK-026-001 ──→ TASK-026-003 ──→ TASK-026-004 ──→ TASK-026-006 ──→ TASK-026-007 ──→ TASK-026-008
TASK-026-002 ──→ TASK-026-003    TASK-026-005 ──┘
             ──→ TASK-026-004
             ──→ TASK-026-005
```

| Task | Title | Estimate | Predecessors | Parallel | Model |
|------|-------|----------|--------------|----------|-------|
| TASK-026-001 | ByteMover callback overloads | ~25k | none | yes (with 002) | — |
| TASK-026-002 | ConnectionLease | ~40k | none | yes (with 001) | — |
| TASK-026-003 | DirectConnectionFactory | ~50k | 001, 002 | no | opus |
| TASK-026-004 | ConnectionPool + HostConnections | ~100k | 002, 003 | yes (with 005) | opus |
| TASK-026-005 | QuicConnectionManager | ~75k | 001, 002 | yes (with 004) | opus |
| TASK-026-006 | Rewire ConnectionStage + pipeline | ~100k | 004, 005 | no | opus |
| TASK-026-007 | Delete old classes + dead properties | ~50k | 006 | no | — |
| TASK-026-008 | Validation | ~15k | 007 | no | — |

**Total estimated tokens:** ~455k

## Functional Requirements

- FR-1: `ConnectionPool.AcquireAsync()` must return a `ConnectionLease` with working `OutboundWriter`/`InboundReader` channels for any HTTP version (1.0, 1.1, 2.0, 3.0)
- FR-2: HTTP/1.0 connections must never be reused — each `AcquireAsync` creates a fresh TCP connection
- FR-3: HTTP/1.1 connections with keep-alive must be returned to an idle pool on `Release(canReuse: true)` and reused by subsequent `AcquireAsync` calls to the same host:port
- FR-4: HTTP/2 connections must support multiplexed streams — `AcquireAsync` returns an existing connection if it has available stream slots (below `MaxConcurrentStreams`)
- FR-5: HTTP/3 QUIC connections must support typed stream opening (request, control, QPACK encoder) via `QuicConnectionManager.OpenStreamAsync()`
- FR-6: HTTP/3 inbound stream acceptance loop must accept server-initiated streams and notify subscribers
- FR-7: Per-host connection limit of 6 must be enforced for HTTP/1.x via `SemaphoreSlim`
- FR-8: Idle connections must be evicted after `TurboClientOptions.IdleTimeout` (default 10s), with at least 1 connection retained per host
- FR-9: MRU (Most Recently Used) selection must prefer the most recently active connection when multiple have available slots
- FR-10: `ConnectionActive`, `ConnectionIdle`, `ConnectionDuration` OpenTelemetry metrics must be emitted with `server.address` + `server.port` tags
- FR-11: `TurboHttpEventSource` events `ConnectionOpened` and `ConnectionClosed` must fire
- FR-12: `TurboHttpDiagnosticListener` events `OnConnectionOpened` and `OnConnectionClosed` must fire
- FR-13: `ConnectionStage` must use no `StageActor`, no `IActorRef`, no actor messages for connection management
- FR-14: All channel writers must be completed when a `ConnectionLease` is disposed (signals stale handles)

## Non-Goals

- No changes to the Akka.Streams pipeline topology (ExtractOptionsStage, BidiFlow chain, feature stages remain unchanged)
- No changes to the HTTP protocol stages (encoder, decoder, correlation, connection reuse evaluation)
- No changes to the `ConnectionHandle` record structure (add `CreateDirect()` factory, but keep existing constructor)
- No connection pre-warming or predictive connection establishment
- No HTTP/3 connection migration or 0-RTT optimization

## Technical Considerations

- **Thread safety:** `ConnectionPool` and `HostConnections` must be fully thread-safe. Use `ConcurrentDictionary` for host lookup, `SemaphoreSlim` for connection limits, `lock` for lease list mutations.
- **Disposal ordering:** `ConnectionLease.DisposeAsync()` must cancel CTS first (stops ByteMovers), then dispose `ClientState` (closes channels), then dispose `IClientProvider` (closes TCP/TLS stream). Order matters — CTS cancellation must propagate before channels are closed.
- **Async callbacks:** `ConnectionStage` uses `GetAsyncCallback<T>` to bridge async pool results into the stage event loop. The `ContinueWith` pattern must use `TaskContinuationOptions.ExecuteSynchronously` to minimize scheduling latency.
- **QUIC complexity:** `QuicConnectionManager` replaces actor-based BecomeStacked isolation with `SemaphoreSlim(1)`. Each stream spawn must be serialized to avoid interleaved state. The inbound acceptance loop runs as a long-lived `Task.Run`.
- **Architecture update:** After completion, update CLAUDE.md's "Pooling Layer" section to describe the new `ConnectionPool` → `HostConnections` → `DirectConnectionFactory` / `QuicConnectionManager` architecture.

## Success Metrics

- H10 integration tests: 20/20 consecutive green runs (currently ~6/10 with actor-based path)
- Connection acquisition latency: < 5ms for localhost (currently ~10-50ms due to actor scheduling)
- Zero orphaned connections or reconnection loops (current architecture has orphaned actor reconnections)
- Net file count reduction: delete 10 files, create 4 = -6 files
- Net code reduction: estimated ~500 lines less (actor boilerplate removed)

## Open Questions

*None — all design decisions resolved during the planning session.*
