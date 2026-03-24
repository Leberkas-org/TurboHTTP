# Feature 026: Unified Connection Management

## Introduction

Replace the entire actor-based connection hierarchy (PoolRouter, HostPool, Http1/2/3ConnectionActor, ClientManager, ClientRunner) AND merge Http3ConnectionStage into ConnectionStage — creating a single unified transport stage + async connection pool with 0 actor mailbox hops for all HTTP versions.

### Architecture Context

- **Vision alignment:** Eliminates HTTP/1.0 integration test flakiness (6 actor hops → 0) and simplifies the transport layer from 2 stages + 7 actor classes to 1 stage + 3 plain classes.
- **Components involved:**
  - `ConnectionStage` (Transport/) — unified transport stage for ALL HTTP versions (1.0, 1.1, 2, 3/QUIC)
  - `ConnectionPool` (Transport/) — replaces PoolRouter + HostPool + ConnectionState + PerHostConnectionLimiter
  - `ConnectionLease` (Transport/) — replaces ConnectionState, wraps handle + lifecycle + metrics
  - `DirectConnectionFactory` (Transport/) — replaces Http1/2/3ConnectionActor + ClientManager + ClientRunner
  - `QuicConnectionManager` (Transport/) — replaces Http3ConnectionActor's QUIC multi-stream logic
  - `ClientByteMover` (Transport/) — add callback overloads (no actor dependency)
- **Deleted components:** PoolRouter, HostPool, ConnectionState, ConnectionActorBase, Http1ConnectionActor, Http2ConnectionActor, Http3ConnectionActor, Http3ConnectionStage, ClientManager, ClientRunner, PerHostConnectionLimiter (11 files)
- **Architectural update needed:** CLAUDE.md Pooling Layer + Transport Layer sections must be rewritten.

## Goals

- Single transport stage (`ConnectionStage`) for HTTP/1.0, 1.1, 2.0, and 3.0/QUIC
- 0 actor mailbox hops for connection acquisition (all versions)
- Exact feature parity: per-host pooling, keep-alive, multiplexing, QUIC multi-stream, idle eviction, MRU selection, metrics, diagnostics
- Delete 11 files, create 4 files — significant complexity reduction
- Remove all dead reconnection code (ReconnectInterval, MaxReconnectAttempts, DoReconnect, AttemptReconnect)
- H10 integration tests: 20/20 consecutive green runs
- All existing tests pass

## Tasks

### TASK-026-001: Add callback-based ByteMover overloads
**Description:** As a developer, I want ClientByteMover methods that accept `Action onClose` instead of `IActorRef runner` so the direct connection path can manage byte-mover lifecycle without actors.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-026-003
**Parallel:** yes — can run alongside TASK-026-002

**Acceptance Criteria:**
- [ ] `MoveStreamToPipe(ClientState, Action, ILoggingAdapter?, CancellationToken)` overload added
- [ ] `MovePipeToChannel(ClientState, Action, ILoggingAdapter?, CancellationToken)` overload added
- [ ] `MoveChannelToStream(ClientState, Action, ILoggingAdapter?, CancellationToken)` overload added
- [ ] Each replaces `runner.Tell(DoClose.Instance)` with `onClose()` call
- [ ] Clean close path (0 bytes read): no `onClose()` — just return and let pipe complete naturally (fix from current session)
- [ ] Existing IActorRef overloads unchanged
- [ ] `dotnet build src/TurboHttp.sln` — 0 errors, 0 warnings
- [ ] `dotnet test src/TurboHttp.StreamTests -v q` — all pass

### TASK-026-002: Create ConnectionLease
**Description:** As a developer, I want a `ConnectionLease` class that wraps `ConnectionHandle` + `ClientState` + lifecycle so connections have a single owner responsible for cleanup, metrics, and stream tracking.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** TASK-026-003, TASK-026-004, TASK-026-005
**Parallel:** yes — can run alongside TASK-026-001

**Acceptance Criteria:**
- [ ] `ConnectionLease` sealed class at `src/TurboHttp/Transport/ConnectionLease.cs`
- [ ] Properties: `Handle`, `Key`, `IsAlive`, `Reusable`, `LastActivity`, `ActiveStreams`, `MaxConcurrentStreams`, `HasAvailableSlot`
- [ ] Methods: `MarkBusy()`, `MarkIdle()`, `MarkNoReuse()`, `UpdateMaxConcurrentStreams(int)`
- [ ] `IAsyncDisposable`: cancels CTS → disposes ClientState → emits `ConnectionDuration` metric + `EventSource.ConnectionClosed` + `DiagnosticListener.OnConnectionClosed`
- [ ] MaxConcurrentStreams defaults: 1 (HTTP/1.0), 6 (HTTP/1.1), 100 (HTTP/2+)
- [ ] `ConnectionHandle.CreateDirect()` factory added using `ActorRefs.Nobody`
- [ ] Unit tests in `src/TurboHttp.StreamTests/Transport/ConnectionLeaseTests.cs`
- [ ] Build passes

### TASK-026-003: Create DirectConnectionFactory
**Description:** As a developer, I want a static factory that establishes TCP/TLS connections, creates channels, spawns ByteMover tasks, and returns a `ConnectionLease` — all in a single async call with no actors.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-026-001, TASK-026-002
**Successors:** TASK-026-004
**Parallel:** no
**Model:** opus

**Acceptance Criteria:**
- [ ] `DirectConnectionFactory` at `src/TurboHttp/Transport/DirectConnectionFactory.cs`
- [ ] `EstablishAsync(TcpOptions, RequestEndpoint, CancellationToken)` → `Task<ConnectionLease>`
- [ ] Selects IClientProvider (TcpClientProvider / TlsClientProvider) based on TcpOptions type
- [ ] Creates ClientState with channels + Pipe
- [ ] Spawns 3 ByteMover tasks using callback overloads
- [ ] Emits `ConnectionActive` metric + `EventSource.ConnectionOpened` + `DiagnosticListener.OnConnectionOpened`
- [ ] Tests: establish happy path, cancellation, connection refused, dispose cleanup
- [ ] All tests pass

### TASK-026-004: Create ConnectionPool + HostConnections
**Description:** As a developer, I want a thread-safe `ConnectionPool` with per-host `HostConnections` that handles acquire (new/reused), release (pool/dispose), idle eviction, per-host limits, and MRU selection — replacing PoolRouter + HostPool + PerHostConnectionLimiter + ConnectionState.

**Token Estimate:** ~100k tokens
**Predecessors:** TASK-026-002, TASK-026-003
**Successors:** TASK-026-006
**Parallel:** yes — can run alongside TASK-026-005
**Model:** opus

**Acceptance Criteria:**
- [ ] `ConnectionPool` at `src/TurboHttp/Transport/ConnectionPool.cs`
- [ ] `AcquireAsync(TcpOptions, RequestEndpoint, CancellationToken)` → `Task<ConnectionLease>`
- [ ] `Release(ConnectionLease, bool canReuse)` — returns to idle pool or disposes
- [ ] `IAsyncDisposable` — disposes all hosts
- [ ] Nested `HostConnections`:
  - `_leases: List<ConnectionLease>`, `_idle: ConcurrentQueue`, `_limiter: SemaphoreSlim` (6 for HTTP/1.x), `_evictionTimer: Timer`
  - MRU selection via `SelectMru()` (most recent `LastActivity` with `HasAvailableSlot`)
- [ ] Version-aware acquire: HTTP/1.0 always new, HTTP/1.1 try idle first, HTTP/2 find slot or new
- [ ] Version-aware release: HTTP/1.0 always dispose, HTTP/1.1 idle queue or dispose, HTTP/2 decrement streams
- [ ] Idle eviction: older than `IdleTimeout`, keep at least 1 per host
- [ ] Metrics: `ConnectionActive` +/-, `ConnectionIdle` +/- on transitions
- [ ] Tests: 9 test cases covering all acquire/release/evict/limit/MRU scenarios
- [ ] All tests pass

### TASK-026-005: Create QuicConnectionManager
**Description:** As a developer, I want a `QuicConnectionManager` that replaces Http3ConnectionActor for QUIC multi-stream management — shared provider, typed stream opening, inbound acceptance loop — without actors.

**Token Estimate:** ~75k tokens
**Predecessors:** TASK-026-001, TASK-026-002
**Successors:** TASK-026-006
**Parallel:** yes — can run alongside TASK-026-004
**Model:** opus

**Acceptance Criteria:**
- [ ] `QuicConnectionManager` at `src/TurboHttp/Transport/QuicConnectionManager.cs`
- [ ] Shared `QuicClientProvider` per host
- [ ] `OpenStreamAsync(OutputStreamType, CancellationToken)` → `ConnectionLease` (Request, Control, QpackEncoder)
- [ ] `_activeStreams: List<ConnectionLease>`, `_spawnLock: SemaphoreSlim(1)` for sequential spawn
- [ ] `StartInboundAcceptLoop(Action<ConnectionLease, InputStreamType> subscriber)` — accepts server-initiated streams
- [ ] Buffered inbound notifications + subscriber pattern
- [ ] `IAsyncDisposable`: cancel inbound loop, dispose all streams, dispose shared provider
- [ ] Tests mirroring `12_ConnectionActorQuicTests.cs`
- [ ] All tests pass

### TASK-026-006: Unify ConnectionStage (merge Http3ConnectionStage)
**Description:** As a developer, I want a single `ConnectionStage` that handles ALL HTTP versions — TCP single-stream (H1.0/1.1/2) AND QUIC multi-stream (H3) — using `ConnectionPool` for acquisition and `QuicConnectionManager` for QUIC streams. This eliminates `Http3ConnectionStage` entirely.

**Token Estimate:** ~120k tokens
**Predecessors:** TASK-026-004, TASK-026-005
**Successors:** TASK-026-007
**Parallel:** no
**Model:** opus

**Acceptance Criteria:**
- [ ] `ConnectionStage` constructor: `ConnectionPool pool` (no `IActorRef`)
- [ ] Removed: `_stageActor`, `GetStageActor()`, `OnMessage()` — no actor bridge
- [ ] Version routing in HandlePush(ConnectItem):
  ```
  if (version.Major >= 3) → AcquireQuicConnection(connect)
  else                    → AcquireConnection(connect)
  ```
- [ ] QUIC multi-stream support merged from Http3ConnectionStage:
  - `_requestHandle`, `_controlHandle`, `_encoderHandle` fields (QUIC only)
  - `_inboundHandles` list for server-initiated streams
  - `_pendingControlItems`, `_pendingEncoderItems` buffers
  - `Http3TaggedItem` routing via `RouteToStream(tagged)`
  - Multiple inbound pumps (one per QUIC stream)
- [ ] TCP single-stream path (HTTP/1.x, HTTP/2): `_pool.AcquireAsync()` → `_onLeaseAcquired`
- [ ] HandlePush(ConnectionReuseItem): `_pool.Release(lease, canReuse)` — no actor Tell
- [ ] HandlePush(StreamAcquireItem): `_currentLease?.MarkBusy()` — direct
- [ ] HandlePush(MaxConcurrentStreamsItem): `_currentLease?.UpdateMaxConcurrentStreams(n)` — direct
- [ ] `_onInboundComplete`: disposes lease, clears handles
- [ ] PostStop: disposes all leases/handles
- [ ] `ProtocolCoreGraphBuilder.Build()` accepts `ConnectionPool` instead of `IActorRef`
- [ ] `Engine.BuildExtendedPipeline()` creates/receives `ConnectionPool`
- [ ] `TurboClientStreamManager` owns `ConnectionPool` lifetime
- [ ] `Http3ConnectionStage.cs` deleted
- [ ] `dotnet build src/TurboHttp.sln` — 0 errors
- [ ] `dotnet test src/TurboHttp.sln -v q` — ALL tests pass

### TASK-026-007: Delete old classes + dead properties
**Description:** As a developer, I want to remove all replaced actor classes, dead reconnection properties, and stale test code for a clean codebase.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-026-006
**Successors:** TASK-026-008
**Parallel:** no

**Acceptance Criteria:**
- [ ] Deleted (11 files): `PoolRouter.cs`, `HostPool.cs`, `ConnectionState.cs`, `ConnectionActorBase.cs`, `Http1ConnectionActor.cs`, `Http2ConnectionActor.cs`, `Http3ConnectionActor.cs`, `Http3ConnectionStage.cs`, `ClientManager.cs`, `ClientRunner.cs`, `PerHostConnectionLimiter.cs`
- [ ] Removed from `TurboClientOptions`: `ReconnectInterval`, `MaxReconnectAttempts`
- [ ] Removed from `TcpOptions`: `ReconnectInterval`, `MaxReconnectAttempts`
- [ ] Removed from `TcpOptionsFactory`: reconnect mappings (3 locations)
- [ ] Removed `DoClose` record if no remaining references
- [ ] Tests rewritten: `01_ConnectionActorTests` → `ConnectionPoolTests`, `12_ConnectionActorQuicTests` → `QuicConnectionManagerTests`
- [ ] `IOActorTestBase.cs` removed/simplified
- [ ] `01_QuicOptionsTests.cs` updated (no reconnect assertions)
- [ ] `grep -r "PoolRouter\|HostPool\|ConnectionActorBase\|Http1ConnectionActor\|ClientManager\b\|ClientRunner\b\|ReconnectInterval\|MaxReconnectAttempts\|Http3ConnectionStage"` in `src/` returns 0 hits
- [ ] `dotnet build src/TurboHttp.sln` — 0 errors, 0 warnings

### TASK-026-008: Validation + documentation
**Description:** As a developer, I want to run comprehensive test suites multiple times and update documentation to confirm zero flakiness and document the new architecture.

**Token Estimate:** ~20k tokens
**Predecessors:** TASK-026-007
**Successors:** none
**Parallel:** no

**Acceptance Criteria:**
- [ ] H10 integration tests 20/20 consecutive green (77/77 each)
- [ ] Full integration suite (H10 + H11 + H2 + Smoke + TLS) — 100% green
- [ ] StreamTests — all pass
- [ ] UnitTests — all pass (except 1 pre-existing HPACK fuzz)
- [ ] CLAUDE.md Pooling Layer section rewritten:
  ```
  Pooling Layer → Transport Layer (ConnectionPool)
    ConnectionPool → HostConnections → DirectConnectionFactory / QuicConnectionManager
    ConnectionLease — per-connection lifecycle + metrics
    ConnectionStage — single unified stage for all HTTP versions
  ```

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
| TASK-026-006 | Unify ConnectionStage | ~120k | 004, 005 | no | opus |
| TASK-026-007 | Delete old classes + properties | ~50k | 006 | no | — |
| TASK-026-008 | Validation + docs | ~20k | 007 | no | — |

**Total estimated tokens:** ~480k

## Functional Requirements

- FR-1: `ConnectionPool.AcquireAsync()` returns a `ConnectionLease` with working channels for HTTP/1.0, 1.1, 2.0, and 3.0
- FR-2: HTTP/1.0 connections never reused — always fresh TCP
- FR-3: HTTP/1.1 keep-alive connections returned to idle pool on `Release(canReuse: true)`
- FR-4: HTTP/2 connections multiplex streams — `AcquireAsync` returns existing connection with available slots
- FR-5: HTTP/3 QUIC: `QuicConnectionManager.OpenStreamAsync()` opens typed streams (Request, Control, QpackEncoder)
- FR-6: HTTP/3 inbound acceptance loop accepts server-initiated streams and notifies subscribers
- FR-7: Per-host limit of 6 for HTTP/1.x via SemaphoreSlim
- FR-8: Idle eviction after `IdleTimeout`, keep-at-least-one invariant
- FR-9: MRU selection prefers most recently active connection
- FR-10: `ConnectionActive`, `ConnectionIdle`, `ConnectionDuration` metrics with server.address + server.port tags
- FR-11: `TurboHttpEventSource` ConnectionOpened/ConnectionClosed events
- FR-12: `TurboHttpDiagnosticListener` OnConnectionOpened/OnConnectionClosed events
- FR-13: Single `ConnectionStage` handles ALL HTTP versions — no separate Http3ConnectionStage
- FR-14: `ConnectionStage` routes `Http3TaggedItem` to correct QUIC stream (Request/Control/QpackEncoder)
- FR-15: All channel writers completed on ConnectionLease disposal
- FR-16: Zero actor references in ConnectionStage (no StageActor, no IActorRef)

## Non-Goals

- No changes to protocol-level stages (Http10/11/20/30 Encoder/Decoder/Correlation/ConnectionStages in Decoding/)
- No changes to feature BidiStages (Redirect, Retry, Cookie, Cache)
- No connection pre-warming or predictive establishment
- No HTTP/3 0-RTT or connection migration

## Technical Considerations

- **Thread safety:** ConnectionPool + HostConnections must be fully thread-safe (ConcurrentDictionary, SemaphoreSlim, lock for lease lists)
- **Disposal ordering:** CTS cancel first → ClientState dispose → IClientProvider dispose
- **QUIC stream isolation:** QuicConnectionManager uses `SemaphoreSlim(1)` to serialize stream spawns (replaces BecomeStacked)
- **Async callbacks:** ConnectionStage uses `GetAsyncCallback<T>` to bridge pool results. `ContinueWith` with `ExecuteSynchronously` for minimal latency
- **Http3TaggedItem routing:** The unified ConnectionStage checks `item is Http3TaggedItem tagged` BEFORE other type checks, routes to Request/Control/QpackEncoder handle
- **Architecture update:** CLAUDE.md Pooling Layer and Transport Layer sections must be rewritten

## Success Metrics

- H10 integration tests: 20/20 consecutive green runs (currently ~6/10)
- Connection acquisition latency: < 5ms localhost (currently ~10-50ms with actors)
- Zero orphaned connections or reconnection loops
- Net file deletion: -11 files created, +4 files = -7 net
- Single transport stage for all versions (was 2: ConnectionStage + Http3ConnectionStage)

## Open Questions

*None — all design decisions resolved during the planning session.*
