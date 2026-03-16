# Plan 5a: Hybrid Migration — Actor Pool for Lifecycle, Pure Streams for Data (Option C)

**Date:** 2026-03-16
**Based on:** ARCHITECTURE_DECISION.md Option C
**Supersedes:** Plan 8 (Option A) — this plan replaces the incremental approach with a structural migration

---

## Introduction

Migrate the TurboHttp transport layer from "actors route every byte" to "actors manage lifecycle only, data flows through Channels directly". The current hot path sends every `DataItem` through 3 actor mailboxes (PoolRouterActor → HostPoolActor → ConnectionActor). After migration, `ConnectionStage` writes/reads `Channel<byte>` handles directly — zero actor hops in the data path.

**Why:** Actor mailbox serialization is a theoretical bottleneck under high concurrency. More importantly, the mixed paradigm (Akka.Streams for pipeline + actor messages for transport) adds conceptual complexity and makes backpressure harder to reason about.

**Key constraint:** All lifecycle management (spawn, supervise, reconnect with backoff, idle eviction, per-host limits) stays in the actor hierarchy. We only remove actors from the data path.

---

## Goals

- Remove `DataItem` routing from actor mailboxes (zero actor hops in hot path)
- Keep actor supervision tree for connection lifecycle
- Introduce a direct `Channel<byte>` handshake between `ConnectionStage` and `ConnectionActor`
- Remove `MergeHub` / `Source.Queue` chains used for data routing
- All existing lifecycle tests remain green throughout migration
- Each task is independently buildable and testable

---

## Current vs Target Architecture

### Current (3 actor hops per request)
```
ConnectionStage → OfferAsync(globalQueue)
  → PoolRouterActor mailbox → Tell(item) → HostPoolActor mailbox
    → OfferAsync(connQueue) → Sink.ForEach → ConnectionActor mailbox
      → _outbound.WriteAsync() → TCP

TCP → PumpInbound() → _responseQueue.OfferAsync()
  → HostPoolActor MergeHub → PoolRouterActor MergeHub
    → ConnectionStage response subscription
```

### Target (zero actor hops for data)
```
ConnectionStage → direct ChannelWriter<byte> → TCP

TCP → direct ChannelReader<byte> → ConnectionStage
```

### What stays in actors
```
PoolRouterActor:  EnsureHost(key) → create/reuse HostPoolActor
HostPoolActor:    SpawnConnection, Watch, Reconnect, IdleEviction, PerHostLimits
ConnectionActor:  Own TCP socket, expose Channels, handle connect/disconnect
```

---

## Task Dependency Graph

```
TASK-5A-001 (Define ConnectionHandle record)
    ↓
TASK-5A-002 (ConnectionActor exposes ConnectionHandle)
    ↓
TASK-5A-003 (HostPoolActor returns ConnectionHandle on EnsureHost)
    ↓
TASK-5A-004 (ConnectionStage consumes ConnectionHandle directly)
    ↓
TASK-5A-005 (Remove data-routing from PoolRouterActor)
    ↓
TASK-5A-006 (Remove data-routing from HostPoolActor)
    ↓
TASK-5A-007 (Remove MergeHub + Source.Queue plumbing)
    ↓
TASK-5A-008 (Wire ConnectionReuseStage feedback to HostPoolActor)
    ↓
TASK-5A-009 (Wire PerHostConnectionLimiter in HostPoolActor)
    ↓
TASK-5A-010 (Fix ConnectionActor reconnect — backoff + ConnectionFailed)
    ↓
TASK-5A-011 (Stale queue cleanup in HostPoolActor)
    ↓
TASK-5A-012 (Integration tests — E2E SendAsync against Kestrel)
    ↓
TASK-5A-013 (Update CLAUDE.md)
```

---

## User Stories

---

### TASK-5A-001: Define `ConnectionHandle` Record

**Description:** As a developer, I want a `ConnectionHandle` value type that bundles the Channel read/write handles for a single TCP connection, so that `ConnectionStage` can get direct access to TCP I/O without actor messages.

**Dependencies:** None
**Size:** S

**Acceptance Criteria:**
- [x] New record `ConnectionHandle` created in `src/TurboHttp/IO/` with:
  - `ChannelWriter<(IMemoryOwner<byte>, int)> OutboundWriter` — write request bytes to TCP
  - `ChannelReader<(IMemoryOwner<byte>, int)> InboundReader` — read response bytes from TCP
  - `HostKey Key` — routing identity
  - `IActorRef ConnectionActor` — for lifecycle messages only (not data)
- [x] Unit test: `ConnectionHandle` can be constructed and properties read back
- [x] `dotnet build ./src/TurboHttp.sln` — 0 errors
- [x] `dotnet test ./src/TurboHttp.sln` — all tests pass

**Key Files:**
- `src/TurboHttp/IO/ConnectionHandle.cs` — new file

---

### TASK-5A-002: `ConnectionActor` Exposes `ConnectionHandle` via Reply

**Description:** As a developer, I want `ConnectionActor` to reply with a `ConnectionHandle` when TCP connects, so that the stream layer can get direct Channel access without routing data through the actor mailbox.

**Dependencies:** TASK-5A-001
**Size:** M

**Design:**
- `ConnectionActor` already receives `ClientConnected(RemoteEndPoint, InboundReader, OutboundWriter)` from `ClientRunner`
- Currently it stores these internally and creates a `_responseQueue`
- New behavior: on `ClientConnected`, create `ConnectionHandle` from the `ClientState` channels and reply to parent via `ConnectionReady(ConnectionHandle)` message
- Keep `PumpInbound()` removal for TASK-5A-007 — for now, both paths coexist

**Acceptance Criteria:**
- [x] New message `ConnectionReady(ConnectionHandle)` defined
- [x] `ConnectionActor.HandleConnected()` sends `ConnectionReady(handle)` to parent after receiving `ClientConnected`
- [x] `ConnectionHandle` contains the actual `ClientState` channel reader/writer (not copies)
- [x] Existing `RegisterConnectionRefs` message still sent (dual-path coexistence)
- [x] Existing `ConnectionActor` tests remain green
- [x] New unit test: verify `ConnectionReady` message is sent to parent on connect
- [x] New unit test: verify `ConnectionHandle` channels are functional (write → read roundtrip)
- [x] `dotnet build ./src/TurboHttp.sln` — 0 errors
- [x] `dotnet test ./src/TurboHttp.sln` — all tests pass

**Key Files:**
- `src/TurboHttp/IO/ConnectionActor.cs` — `HandleConnected()`
- `src/TurboHttp/IO/ConnectionHandle.cs`

---

### TASK-5A-003: `HostPoolActor` Forwards `ConnectionHandle` on `EnsureHost`

**Description:** As a developer, I want `HostPoolActor` to forward the `ConnectionHandle` to the requester when a connection is ready, so that `ConnectionStage` can receive the direct Channel handles.

**Dependencies:** TASK-5A-002
**Size:** M

**Design:**
- `PoolRouterActor.EnsureHost` is currently fire-and-forget (Tell)
- Change to request-reply: `EnsureHost` → `HostPoolActor` → on `ConnectionReady` → reply `ConnectionHandle` to original requester
- `HostPoolActor` stashes the requester `IActorRef` (from `ConnectionStage` via `StageActorRef`) until the connection is ready
- If a connection already exists and is active, reply immediately with existing handle

**Acceptance Criteria:**
- [x] `EnsureHost` message carries the requester `IActorRef` (or uses `Sender`)
- [x] `HostPoolActor` tracks pending requesters waiting for a `ConnectionHandle`
- [x] On `ConnectionReady(handle)` from `ConnectionActor`, `HostPoolActor` replies to pending requester with `ConnectionHandle`
- [x] If active connection exists, `HostPoolActor` replies immediately (no wait)
- [x] Existing fire-and-forget `EnsureHost` path still works (backward compat during migration)
- [x] New unit test: verify `ConnectionHandle` is returned to requester after TCP connect
- [x] New unit test: verify immediate reply when active connection exists
- [x] New unit test: verify multiple requesters are all served
- [x] `dotnet build ./src/TurboHttp.sln` — 0 errors
- [x] `dotnet test ./src/TurboHttp.sln` — all tests pass

**Key Files:**
- `src/TurboHttp/IO/HostPoolActor.cs` — `HandleEnsureHost`, `HandleConnectionReady`
- `src/TurboHttp/IO/PoolRouterActor.cs` — forward requester ref

---

### TASK-5A-004: `ConnectionStage` Consumes `ConnectionHandle` Directly

**Description:** As a developer, I want `ConnectionStage` to read/write TCP bytes via `ConnectionHandle` channels directly, bypassing the actor mailbox data path.

**Dependencies:** TASK-5A-003
**Size:** L

**Design:**
- `ConnectionStage.PreStart` currently asks `PoolRouterActor` for `GlobalRefs` (request queue + response source)
- New behavior: on `ConnectItem`, ask `PoolRouterActor.EnsureHost` and await `ConnectionHandle` reply (via `GetAsyncCallback`)
- Use `ConnectionHandle.OutboundWriter` for writing `DataItem` bytes (instead of `_globalRequestQueue.OfferAsync`)
- Use `ConnectionHandle.InboundReader` for reading response bytes (instead of MergeHub subscription)
- `GetAsyncCallback` bridges the async Channel read into the GraphStage event loop

**Acceptance Criteria:**
- [x] `ConnectionStage` no longer asks for `GlobalRefs` on `PreStart`
- [x] On `ConnectItem`: sends `EnsureHost`, awaits `ConnectionHandle` via `GetAsyncCallback`
- [x] On `DataItem` (outbound): writes directly to `ConnectionHandle.OutboundWriter`
- [x] Inbound: reads from `ConnectionHandle.InboundReader` via async pump with `GetAsyncCallback`
- [x] Old `GlobalRefs` / `_globalRequestQueue` / response subscription code removed
- [x] Existing `ConnectionStage` tests updated for new protocol
- [x] New stream test: verify end-to-end byte flow through `ConnectionStage` with stubbed channels
- [x] `dotnet build ./src/TurboHttp.sln` — 0 errors
- [x] `dotnet test ./src/TurboHttp.sln` — all tests pass

**Key Files:**
- `src/TurboHttp/IO/Stages/ConnectionStage.cs` — full rewrite of data flow
- `src/TurboHttp.StreamTests/` — updated ConnectionStage tests

---

### TASK-5A-005: Remove Data-Routing from `PoolRouterActor`

**Description:** As a developer, I want to remove the `DataItem` routing and `MergeHub` response aggregation from `PoolRouterActor`, since data now flows directly through channels.

**Dependencies:** TASK-5A-004
**Size:** S

**Acceptance Criteria:**
- [x] `_globalRequestQueue` (Source.Queue) removed from `PoolRouterActor`
- [x] `HandleDataItem()` method removed
- [x] `_globalResponseMerge` (MergeHub) removed
- [x] `GlobalRefs` message and handler removed
- [x] `RegisterHostResponseSource` handler simplified or removed
- [x] `PoolRouterActor` only handles: `EnsureHost` (lifecycle), `GetStatus` (diagnostics)
- [x] Existing lifecycle tests (`EnsureHost` creates `HostPoolActor`) remain green
- [x] `dotnet build ./src/TurboHttp.sln` — 0 errors
- [x] `dotnet test ./src/TurboHttp.sln` — all tests pass

**Key Files:**
- `src/TurboHttp/IO/PoolRouterActor.cs`

---

### TASK-5A-006: Remove Data-Routing from `HostPoolActor`

**Description:** As a developer, I want to remove the per-connection `Source.Queue` data routing from `HostPoolActor`, since data now flows directly through channels.

**Dependencies:** TASK-5A-004
**Size:** M

**Acceptance Criteria:**
- [ ] `_connectionQueues` dictionary removed
- [ ] `HandleDataItem()` method removed (no more DataItem routing)
- [ ] `_pending` queue removed (ConnectionStage handles backpressure via channels now)
- [ ] `SelectConnectionWithQueue()` removed
- [ ] `RegisterConnectionRefs` handler simplified — no longer creates per-connection queues or MergeHub wiring
- [ ] Per-host `MergeHub` for response aggregation removed
- [ ] `HostPoolActor` only handles: `SpawnConnection`, `ConnectionReady`, `ConnectionFailed`, `IdleCheck`, `Reconnect`, `MarkConnectionNoReuse`
- [ ] Existing lifecycle tests (spawn, watch, reconnect, idle eviction) remain green
- [ ] `dotnet build ./src/TurboHttp.sln` — 0 errors
- [ ] `dotnet test ./src/TurboHttp.sln` — all tests pass

**Key Files:**
- `src/TurboHttp/IO/HostPoolActor.cs`

---

### TASK-5A-007: Remove `PumpInbound` and `_responseQueue` from `ConnectionActor`

**Description:** As a developer, I want to remove the `PumpInbound()` background task and `_responseQueue` from `ConnectionActor`, since `ConnectionStage` now reads directly from the inbound channel.

**Dependencies:** TASK-5A-004, TASK-5A-006
**Size:** S

**Acceptance Criteria:**
- [ ] `PumpInbound()` async task removed
- [ ] `_responseQueue` (`ISourceQueueWithComplete<DataItem>`) removed
- [ ] `RegisterConnectionRefs` message removed (replaced by `ConnectionReady`)
- [ ] `ConnectionActor` only handles: TCP lifecycle (`ClientConnected`, `ClientDisconnected`, `Terminated`), `DoClose`
- [ ] `ConnectionActor` still sends `ConnectionReady(ConnectionHandle)` to parent on connect
- [ ] On reconnect, old `ConnectionHandle` channels are completed/disposed, new handle sent
- [ ] Existing lifecycle tests remain green
- [ ] `dotnet build ./src/TurboHttp.sln` — 0 errors
- [ ] `dotnet test ./src/TurboHttp.sln` — all tests pass

**Key Files:**
- `src/TurboHttp/IO/ConnectionActor.cs`

---

### TASK-5A-008: Wire `ConnectionReuseStage` Feedback to `HostPoolActor`

**Description:** As a developer, I want `ConnectionReuseStage` decisions to reach `HostPoolActor` so that "Connection: close" responses cause the actor to mark the connection as non-reusable.

**Dependencies:** TASK-5A-004 (ConnectionStage has actor ref from ConnectionHandle)
**Size:** S

**Design:**
- `ConnectionReuseStage.Out1` emits `IControlItem` (specifically `ConnectionReuseItem`)
- Currently wired to `Sink.Ignore` in `Engine.BuildConnectionFlowPublic`
- Replace `Sink.Ignore` with a `Sink.ForEach` that sends `MarkConnectionNoReuse` to the `ConnectionHandle.ConnectionActor` ref (which forwards to `HostPoolActor`)
- Or: `ConnectionStage` can subscribe to the signal outlet and send the message directly

**Acceptance Criteria:**
- [ ] `ConnectionReuseItem` with `CanReuse = false` triggers `MarkConnectionNoReuse` message to `HostPoolActor`
- [ ] `HostPoolActor.HandleMarkNoReuse()` sets `ConnectionState.Reusable = false`
- [ ] Next idle eviction cycle closes non-reusable connections
- [ ] New unit test: verify `MarkConnectionNoReuse` message reaches `HostPoolActor`
- [ ] New stream test: verify "Connection: close" response → connection marked non-reusable
- [ ] `dotnet build ./src/TurboHttp.sln` — 0 errors
- [ ] `dotnet test ./src/TurboHttp.sln` — all tests pass

**Key Files:**
- `src/TurboHttp/Streams/Engine.cs` — `BuildConnectionFlowPublic` signal sink wiring
- `src/TurboHttp/IO/HostPoolActor.cs` — `HandleMarkNoReuse`

---

### TASK-5A-009: Wire `PerHostConnectionLimiter` in `HostPoolActor`

**Description:** As a developer, I want per-host connection limits enforced in `HostPoolActor.SpawnConnection()`.

**Dependencies:** None (can be done in parallel with TASK-5A-001..007)
**Size:** S

**Acceptance Criteria:**
- [ ] `PerHostConnectionLimiter` instantiated in `HostPoolActor` using `PoolConfig.MaxConnectionsPerHost`
- [ ] `SpawnConnection()` checks `TryAcquire()` before creating `ConnectionActor`
- [ ] When limit reached, connection request is queued (served when a slot frees)
- [ ] New unit test: verify spawn blocked at limit
- [ ] New unit test: verify queued request served when slot frees
- [ ] `dotnet build ./src/TurboHttp.sln` — 0 errors
- [ ] `dotnet test ./src/TurboHttp.sln` — all tests pass

**Key Files:**
- `src/TurboHttp/IO/HostPoolActor.cs` — `SpawnConnection()`
- `src/TurboHttp/Protocol/PerHostConnectionLimiter.cs`

---

### TASK-5A-010: Fix `ConnectionActor` Reconnect — Backoff + `ConnectionFailed`

**Description:** As a developer, I want `ConnectionActor` to notify its parent on TCP drop and use exponential backoff on reconnect.

**Dependencies:** None (can be done in parallel)
**Size:** M

**Acceptance Criteria:**
- [ ] `ConnectionActor.Reconnect()` sends `ConnectionFailed` to parent before attempting reconnect
- [ ] Exponential backoff using `PoolConfig.ReconnectInterval` as base
- [ ] `PoolConfig.MaxReconnectAttempts` respected — permanent failure after N tries
- [ ] On reconnect success, new `ConnectionReady(handle)` sent to parent (new channels)
- [ ] New unit test: `ConnectionFailed` sent on TCP drop
- [ ] New unit test: backoff delay increases
- [ ] New unit test: stops after max attempts
- [ ] `dotnet build ./src/TurboHttp.sln` — 0 errors
- [ ] `dotnet test ./src/TurboHttp.sln` — all tests pass

**Key Files:**
- `src/TurboHttp/IO/ConnectionActor.cs` — `Reconnect()`, `HandleDisconnected`
- `src/TurboHttp/IO/PoolConfig.cs`

---

### TASK-5A-011: Stale State Cleanup in `HostPoolActor` on `ConnectionFailed`

**Description:** As a developer, I want `HostPoolActor` to clean up stale connection state when a connection drops.

**Dependencies:** TASK-5A-010
**Size:** S

**Acceptance Criteria:**
- [ ] `HandleConnectionFailed` removes connection from `_connections` list
- [ ] Connection marked `Active = false`
- [ ] `ConnectionHandle` for failed connection is invalidated
- [ ] If `ConnectionStage` holds a stale handle, the completed channel signals it to request a new one
- [ ] New unit test: verify cleanup on `ConnectionFailed`
- [ ] `dotnet build ./src/TurboHttp.sln` — 0 errors
- [ ] `dotnet test ./src/TurboHttp.sln` — all tests pass

**Key Files:**
- `src/TurboHttp/IO/HostPoolActor.cs` — `HandleConnectionFailed`

---

### TASK-5A-012: Integration Tests — E2E `SendAsync()` Against Kestrel

**Description:** As a developer, I want integration tests proving the full hybrid pipeline works end-to-end.

**Dependencies:** TASK-5A-004 through TASK-5A-011
**Size:** M

**Acceptance Criteria:**
- [ ] Integration test class(es) in `src/TurboHttp.IntegrationTests/`
- [ ] Tests call `TurboHttpClient.SendAsync()` (not engine helpers)
- [ ] Coverage:
  - Basic GET/POST (HTTP/1.1)
  - Redirect chains (301, 302, 307, 308)
  - Cookie round-trips
  - Cache behavior (max-age hit, etag validation)
  - Retry on 503
  - Connection reuse (multiple requests on same TCP)
- [ ] HTTP/2 basic GET via `KestrelH2Fixture`
- [ ] `dotnet build ./src/TurboHttp.sln` — 0 errors
- [ ] `dotnet test ./src/TurboHttp.sln` — all tests pass

**Key Files:**
- `src/TurboHttp.IntegrationTests/` — new test classes
- `src/TurboHttp.IntegrationTests/Shared/KestrelFixture.cs`

---

### TASK-5A-013: Update CLAUDE.md

**Description:** As a developer, I want CLAUDE.md to accurately reflect the hybrid architecture.

**Dependencies:** All above
**Size:** S

**Acceptance Criteria:**
- [ ] "Current Limitations" updated — remove outdated claims
- [ ] Architecture diagram updated to show direct Channel path
- [ ] I/O Layer description updated (no more "3 actor hops")
- [ ] `dotnet build ./src/TurboHttp.sln` — 0 errors

**Key Files:**
- `CLAUDE.md`

---

## Implementation Phases

| Phase | Tasks | Parallelizable | Description |
|-------|-------|---------------|-------------|
| **1** | TASK-5A-001 | Single task | Define `ConnectionHandle` type |
| **2** | TASK-5A-002, TASK-5A-009, TASK-5A-010 | Yes (3 parallel) | Actor changes (additive, coexist with old path) |
| **3** | TASK-5A-003 | Sequential | HostPoolActor handle forwarding |
| **4** | TASK-5A-004 | Sequential (largest task) | ConnectionStage rewrite — switches to direct channels |
| **5** | TASK-5A-005, TASK-5A-006, TASK-5A-007 | Yes (3 parallel) | Remove dead data-routing code |
| **6** | TASK-5A-008, TASK-5A-011 | Yes (2 parallel) | Wire reuse feedback + stale cleanup |
| **7** | TASK-5A-012 | Sequential | Integration tests |
| **8** | TASK-5A-013 | Sequential | Documentation |

**Key principle:** Phases 1-3 are additive (old path coexists). Phase 4 is the cutover. Phases 5-7 clean up and harden.

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| `GetAsyncCallback` complexity in `ConnectionStage` | Prototype in TASK-5A-004 with a focused spike; fall back to `StageActorRef` + message if needed |
| Reconnect invalidates `ConnectionHandle` | Completed channel signals `ConnectionStage` to re-request handle from `HostPoolActor` |
| Dual-path coexistence during migration | Each phase builds and tests clean — old path removed only after new path is proven |
| Breaking existing 19 actor tests | Phases 1-3 are additive; tests adapted incrementally in each task |

---

## Non-Goals

- HTTP/3 support (stub engine only)
- `IHttpClientFactory` compatibility
- Observability / logging / metrics
- Benchmarking (Option C vs Option A performance comparison)
- Graceful shutdown / `IAsyncDisposable` (separate concern, can be added to any architecture)

---

## Success Criteria

- Zero actor mailbox hops in the request/response data path
- All actor lifecycle management preserved (supervision, reconnect, idle eviction)
- All existing tests green (adapted where needed)
- New integration tests prove E2E functionality
- `ConnectionStage` reads/writes `Channel<byte>` directly
- CLAUDE.md reflects the hybrid architecture
