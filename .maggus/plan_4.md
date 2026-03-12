# Plan: Actor-Based Connection Pool — Replacing ConnectionPoolStage

## Introduction

Replace the current `ConnectionPoolStage` (GraphStage with sub-graph materialisation, see plan_3) with an Actor-based connection pool architecture. The new design uses a three-actor hierarchy — `PoolRouterActor` → `HostPoolActor` → `ConnectionActor` — with a thin `ConnectionPoolStage` GraphStage acting purely as a bridge between the Akka.Streams world and the actor system.

### Why Actors Replace the Stage-Internal Approach

The plan_3 design materialised `ConnectionStage` sub-graphs inside GraphStageLogic. While functionally correct, this created complexity:
- Sub-graph lifecycle management inside a GraphStage is fragile (materialiser scoping, async callbacks)
- Connection state (idle, active, pending) was tracked inside a single-threaded GraphStageLogic — no natural supervision
- Error recovery required manual `WatchTermination` + async callback chains
- Testing required full stream materialisation for every scenario

The Actor-based approach gives us:
- **Natural supervision**: Akka's actor hierarchy handles failures, restarts, and lifecycle
- **Independent testability**: Each actor is testable with `TestKit` + `TestProbe` — no materialiser needed
- **Simpler concurrency**: Actor mailbox serialisation replaces async callback coordination
- **Watch/Terminated**: Built-in death watch replaces manual `WatchTermination`
- **Scheduling**: `ScheduleTellRepeatedlyCancelable` replaces `TimerMessages` in GraphStageLogic

### Architecture Comparison

```
plan_3 (Stage-based):                    plan_5 (Actor-based):
┌─────────────────────────┐              ┌──────────────────────────────────┐
│ ConnectionPoolStage     │              │ ConnectionPoolStage (thin)       │
│ ┌─────────────────────┐ │              │   ↕ StageActor ↔ PoolRouterActor │
│ │ HostPool            │ │              └──────────────────────────────────┘
│ │ ┌─────────────────┐ │ │                         │
│ │ │ ConnectionSlot  │ │ │              ┌───────────┴───────────┐
│ │ │ Source.Queue    │ │ │              │ PoolRouterActor       │
│ │ │ ConnectionStage │ │ │              │ ┌───────────────────┐ │
│ │ │ Sink.ForEach    │ │ │              │ │ HostPoolActor (A) │ │
│ │ └─────────────────┘ │ │              │ │ ┌───────────────┐ │ │
│ │ ┌─────────────────┐ │ │              │ │ │ConnectionActor│ │ │
│ │ │ ConnectionSlot  │ │ │              │ │ │ConnectionActor│ │ │
│ │ └─────────────────┘ │ │              │ │ └───────────────┘ │ │
│ └─────────────────────┘ │              │ ├───────────────────┤ │
│ ┌─────────────────────┐ │              │ │ HostPoolActor (B) │ │
│ │ HostPool            │ │              │ │ ...               │ │
│ └─────────────────────┘ │              │ └───────────────────┘ │
└─────────────────────────┘              └───────────────────────┘
```

## Goals

- Replace plan_3's sub-graph materialisation with an actor hierarchy
- Each actor independently testable with Akka.TestKit (no materialiser required)
- Fix known bugs in the POC code (HandleReconnect inverted logic)
- One file per actor/stage — clean separation of concerns
- Preserve all plan_3 functional requirements: dynamic scaling, idle eviction, health monitoring, reconnect, load balancing
- Thin GraphStage bridge for Akka.Streams integration
- Full test coverage with TestKit and StreamTestBase

## User Stories

---

### TASK-001: Extract and Test ConnectionState

**Description:** As a developer, I want `ConnectionState` as a standalone, testable type so that connection state transitions (active/idle/pending) are verified independently.

**Acceptance Criteria:**
- [x] `ConnectionState.cs` created in `src/TurboHttp/IO/`
- [x] `internal sealed class` with properties: `Actor` (IActorRef), `Active` (bool), `Idle` (bool), `PendingRequests` (int), `LastActivity` (DateTime)
- [x] Default state: `Active=true`, `Idle=true`, `PendingRequests=0`, `LastActivity=DateTime.UtcNow`
- [x] Methods: `MarkBusy()` sets `Idle=false`, increments `PendingRequests`, updates `LastActivity`
- [x] Methods: `MarkIdle()` decrements `PendingRequests`, sets `Idle=true` when `PendingRequests==0`, updates `LastActivity`
- [x] Methods: `MarkDead()` sets `Active=false`
- [x] Test file `src/TurboHttp.Tests/IO/ConnectionStateTests.cs` with tests for all transitions
- [x] Build passes

---

### TASK-002: Extract PoolRouterActor into Own File

**Description:** As a developer, I want the `PoolRouterActor` in its own file with only its message records, so that routing responsibility is clearly isolated.

**Acceptance Criteria:**
- [x] `PoolRouterActor.cs` in `src/TurboHttp/IO/` contains only `PoolRouterActor` and its records: `RegisterHost`, `SendRequest`, `Response`, `ConnectionIdle`, `ConnectionFailed`
- [x] Uses `Context.ResolveChildActor<HostPoolActor>()` for child creation (Servus.Akka pattern)
- [x] Only required `using` statements
- [x] Build passes

---

### TASK-003: TestKit Tests for PoolRouterActor — Host Registration

**Description:** As a developer, I want tests for host registration so that I can verify hosts are correctly created and duplicates are ignored.

**Acceptance Criteria:**
- [x] Test file `src/TurboHttp.Tests/IO/PoolRouterActorTests.cs` created
- [x] Test class extends `TestKit` (Akka.TestKit.Xunit2)
- [x] Test: `RegisterHost` creates a child actor for the given PoolKey
- [x] Test: Duplicate `RegisterHost` with same PoolKey is silently ignored (no second child)
- [x] Test: Different PoolKeys create different child actors
- [x] All tests green

---

### TASK-004: TestKit Tests for PoolRouterActor — Request Routing

**Description:** As a developer, I want tests for request forwarding so that I can verify requests reach the correct host pool.

**Acceptance Criteria:**
- [x] Test: `SendRequest` with registered PoolKey is forwarded to the correct `HostPoolActor`
- [x] Test: `SendRequest` with unknown PoolKey replies with `Status.Failure(InvalidOperationException("Unknown host"))`
- [x] Test: Multiple hosts — request goes to the correct host based on PoolKey
- [x] Test: `Sender` is preserved through `Forward` (original sender receives the failure)
- [x] All tests green

---

### TASK-005: Extract HostPoolActor into Own File

**Description:** As a developer, I want the `HostPoolActor` in its own file with its internal state management.

**Acceptance Criteria:**
- [x] `HostPoolActor.cs` in `src/TurboHttp/IO/` created
- [x] Contains actor with message records: `Incoming`, `ConnectionIdle`, `ConnectionFailed`, `ConnectionResponse`, `IdleCheck`, `Reconnect`
- [x] References `ConnectionState`, `PoolConfig`, `ConnectionActor`
- [x] Constructor takes `TcpOptions`, `PoolConfig`, `IActorRef streamPublisher`
- [x] `PreStart` schedules `IdleCheck` timer via `ScheduleTellRepeatedlyCancelable`
- [x] Build passes

---

### TASK-006: TestKit Tests for HostPoolActor — Connection Spawning & Dynamic Scaling

**Description:** As a developer, I want tests for dynamic connection creation so that pool scaling up to `MaxConnectionsPerHost` is verified.

**Equivalent to plan_3 TASK-003 (Multi-Connection per Host — Dynamic Scaling)**

**Acceptance Criteria:**
- [x] Test file `src/TurboHttp.Tests/IO/HostPoolActorTests.cs` created
- [x] Test: First `Incoming` request spawns a new `ConnectionActor` child
- [x] Test: Second request while first connection is busy spawns a second `ConnectionActor`
- [x] Test: N parallel requests with `MaxConnectionsPerHost=N` → N connections spawned
- [x] Test: Request when `MaxConnectionsPerHost` is reached → request queued in `_pending`
- [x] Test: Each spawned `ConnectionActor` is `Context.Watch`'ed
- [x] All tests green

---

### TASK‑007: HostPoolActor Tests — Connection Selection & HTTP Version Awareness

**Description:**
As a developer, I want comprehensive tests for the `HostPoolActor` connection selection logic so that:

1. **idle, reusable connections** are preferred,
2. **HTTP/2 stream IDs** are correctly assigned for multiplexed streams,
3. **HTTP/1.0/1.1 connection reuse behavior** respects RFC rules,
4. connection selection behaves correctly across active, idle, and dead connections.

This ensures that for HTTP/1.x the pool prefers idle and reusable connections, and for HTTP/2 it selects the same connection but with correct Stream ID allocation (multiplexing), consistent with RFC‑compliant connection reuse.

This replaces (and significantly expands) plan_3 TASK‑004 (Load Balancing Across Connections) to include HTTP version details.

---

**Acceptance Criteria:**

**General Connection Selection Logic (HTTP/1.x & HTTP/2)**

* [ ] When multiple connections exist and at least one is idle *and* reusable, the idle connection is preferred for scheduling a new request (idle reuse over busy).
* [ ] If a connection is marked **non‑active** (dead), it is never selected.
* [ ] If all active connections are busy:

  * For HTTP/1.x: returns `null` so the pool knows to spawn or queue.
  * For HTTP/2: selects an active connection to multiplex new stream IDs (since HTTP/2 supports parallel streams).
* [ ] Connections with pending requests beyond `MaxRequestsPerConnection` **must not** be selected for new requests in HTTP/1.x.
* [ ] Connection selection logic must not reorder connections (no starvation).

**HTTP/1.0 & HTTP/1.1 Connection Semantics**

* [ ] Idle connections with `Connection: keep‑alive` are preferred for reuse.
* [ ] Connections with `Connection: close` or server signal for close are not considered reusable/idle.
* [ ] For HTTP/1.0 (unless explicitly keep‑alive), existing connections are not reused by default.
* [ ] HostPoolActor must generate a *new connection* when no suitable reusable connection exists (for HTTP/1.0 default “close”).

**HTTP/2 (Multiplexing & Stream IDs)**

* [ ] A single HTTP/2 connection must be reused for all requests to the same host (multiplexed).
* [ ] HostPoolActor must track and assign **unique stream IDs** within an existing HTTP/2 connection for each new request.
* [ ] Stream ID allocation must follow RFC 9113 rules (increment by 2 for client‑initiated streams, odd IDs by default).
* [ ] If the selected HTTP/2 connection has exhausted its concurrency window (`MAX_CONCURRENT_STREAMS`), selection returns `null` so the pool may queue or wait until a slot frees.

**Test Assertions (Explicit Behavioral Checks):**
Idle + Active Selection

* [ ] Idle, active connection selected first for HTTP/1.x.
* [ ] Multiple idle connections → selects the first idle (RoundRobin configuration documented as future enhancement).
* [ ] Busy connections with pending requests > 0 not selected if idle exists.

Non‑Idle/Dead Skipping

* [ ] Dead (non‑active) connections never selected.
* [ ] Connections flagged as explicitly “no‑reuse” (close) are not selected even if idle.

HTTP/1.x Full Path

* [ ] No idle connection → returns `null` (pool will spawn or queue).
* [ ] Active idle connection with keep‑alive selected for repeat scheduling.
* [ ] For HTTP/1.0 without `Connection: keep‑alive`, returns `null` (no reuse).

HTTP/2 Multiplexing

* [ ] A single HTTP/2 connection always selected for multiplexing.
* [ ] Test: stream IDs start at 1 and increment by 2 for each new request.
* [ ] Test: When HTTP/2 stream window is exhausted (`MAX_CONCURRENT_STREAMS = 1`), selection returns null/blocked.
* [ ] Test: After some streams complete, connection becomes eligible again for new streams (stream freeing).

**Integration Verification:**

* [ ] Integration test: Under mixed HTTP/1.x load and connection reuse settings, ensure reused connections are preferred and new connections are spawned only when required.
* [ ] Integration test: Under HTTP/2 load with concurrency >1, verify parallel requests succeed on the same connection with correct stream IDs.
* [ ] Integration test: When an HTTP/2 connection is closed (GOAWAY), the next request triggers a reconnection with a new HTTP/2 connection and reset stream ID sequence.

---

### TASK-008: TestKit Tests for HostPoolActor — Idle Handling & Queue Draining

**Description:** As a developer, I want tests for idle handling and pending queue draining so that freed connections immediately serve waiting requests.

**Acceptance Criteria:**
- [x] Test: `ConnectionIdle` decrements `PendingRequests` on the correct `ConnectionState`
- [x] Test: `ConnectionIdle` sets `Idle=true` when `PendingRequests==0`
- [x] Test: `ConnectionIdle` does NOT set `Idle=true` when `PendingRequests > 0` after decrement
- [x] Test: `ConnectionIdle` updates `LastActivity`
- [x] Test: After `ConnectionIdle`, pending requests are drained to freed connection
- [x] Test: `DrainPending` stops when no idle connection is available
- [x] Test: Multiple pending requests are drained sequentially
- [x] Test: `ConnectionIdle` for unknown connection is silently ignored
- [x] All tests green

---

### TASK-009: TestKit Tests for HostPoolActor — Connection Failure & Reconnect

**Description:** As a developer, I want tests for failure handling and reconnection so that dead connections are replaced.

**Equivalent to plan_3 TASK-005 (Connection Health Monitoring and Auto-Reconnect)**

**Acceptance Criteria:**
- [x] Test: `ConnectionFailed` marks `ConnectionState.Active=false`
- [x] Test: `ConnectionFailed` schedules a `Reconnect` message after `PoolConfig.ReconnectInterval`
- [x] Test: `ConnectionFailed` for unknown connection is silently ignored
- [x] Test: `Reconnect` spawns a new `ConnectionActor` to replace the dead one
- [x] Test: Reconnect for already-removed connection is ignored
- [x] All tests green

---

### TASK-010: Fix HandleReconnect Bug

**Description:** As a developer, I want the `HandleReconnect` logic fixed. The current code has inverted logic: it spawns a new connection when the old one is NOT found, and does nothing when it IS found.

**Bug Analysis:**
```csharp
// CURRENT (buggy):
private void HandleReconnect(Reconnect msg)
{
    var conn = Find(msg.Connection);
    if (conn != null)    // ← connection still exists → do nothing (wrong!)
        return;
    SpawnConnection();   // ← connection gone → spawn blind (wrong!)
}
```

**Correct behavior:**
1. If connection found → it was marked `Active=false` by `HandleFailure` → remove it, spawn replacement
2. If connection not found → already removed (e.g. by eviction) → do nothing

**Acceptance Criteria:**
- [x] Bug documented with before/after code
- [x] Fix: `if (conn == null) return;` — ignore reconnect for already-removed connections
- [x] Fix: Remove dead connection from `_connections`, then `SpawnConnection()`
- [x] Test: `HandleFailure` → wait `ReconnectInterval` → dead connection removed, new one spawned
- [x] Test: Connection removed before reconnect timer fires → reconnect is no-op
- [x] Test: New connection after reconnect is `Active=true`, `Idle=true`
- [x] Build + all tests green

---

### TASK-011: TestKit Tests for HostPoolActor — Idle Connection Eviction

**Description:** As a developer, I want tests for idle eviction so that unused connections are automatically cleaned up.

**Equivalent to plan_3 TASK-006 (Idle Connection Eviction)**

**Acceptance Criteria:**
- [x] Test: `IdleCheck` timer is started in `PreStart` with `PoolConfig.IdleCheckInterval`
- [x] Test: Idle connection past `IdleTimeout` receives `PoisonPill` and is removed
- [x] Test: Idle connection within `IdleTimeout` is preserved
- [x] Test: Active (non-idle) connection is never evicted regardless of `LastActivity`
- [x] Test: Last remaining connection per host is preserved (`_connections.Count > 1` check)
- [x] Test: Multiple idle connections — only expired ones are evicted
- [x] All tests green

---

### TASK-012: Extract ConnectionActor into Own File

**Description:** As a developer, I want the `ConnectionActor` in its own file so that TCP connection lifecycle management is isolated.

**Acceptance Criteria:**
- [x] `ConnectionActor.cs` in `src/TurboHttp/IO/` created
- [x] Contains actor with records: `GetStreamRefs`, `StreamRefsResponse`
- [x] References `ClientManager`, `ClientRunner`, `DataItem`, `IDataItem`, `DoClose`
- [x] `PreStart` calls `Connect()` which sends `CreateTcpRunner` to ClientManager
- [x] Build passes

---

### TASK-013: TestKit Tests for ConnectionActor — TCP Lifecycle

**Description:** As a developer, I want tests for the TCP connection lifecycle so that connect/disconnect/reconnect behavior is verified.

**Acceptance Criteria:**
- [x] Test file `src/TurboHttp.Tests/IO/ConnectionActorTests.cs` created
- [x] Test: `PreStart` sends `ClientManager.CreateTcpRunner` with correct `TcpOptions` and `Self`
- [x] Test: `ClientConnected` stores inbound/outbound channels and runner ref
- [x] Test: `ClientConnected` starts PumpInbound task
- [x] Test: `ClientDisconnected` triggers reconnect (sends `CreateTcpRunner` again)
- [x] Test: `Terminated` of runner triggers reconnect
- [x] Test: Reconnect clears old channel references before creating new ones
- [x] All tests green

---

### TASK-014: TestKit Tests for ConnectionActor — Data Send

**Description:** As a developer, I want tests for sending data through the connection so that outbound channel writes are verified.

**Acceptance Criteria:**
- [x] Test: `DataItem` is written to `_outbound` via `TryWrite`
- [x] Test: `DataItem` when `_outbound` is null → `Memory.Dispose()` called (no crash)
- [x] Test: `DataItem` when `TryWrite` returns false → `Memory.Dispose()` called
- [x] Test: After successful `ClientConnected`, DataItem flows through outbound channel
- [x] All tests green

---

### TASK-015: TestKit Tests for ConnectionActor — Cleanup

**Description:** As a developer, I want tests for clean resource disposal on actor stop.

**Acceptance Criteria:**
- [x] Test: `PostStop` cancels `CancellationTokenSource`
- [x] Test: `PostStop` sends `DoClose` to runner
- [x] Test: `PostStop` completes `_responseQueue`
- [x] Test: `PostStop` with null runner does not throw
- [x] Test: `PostStop` with null `_responseQueue` does not throw
- [x] All tests green

---

### TASK-016: Extract ConnectionPoolStage — Rename & Own File

**Description:** As a developer, I want the `ConnectionPoolStageTest` GraphStage renamed to `ConnectionPoolStage` and placed in its own file, as it is the production bridge between streams and actors.

**Acceptance Criteria:**
- [x] `ConnectionPoolStage.cs` in `src/TurboHttp/IO/Stages/` created
- [x] Class renamed from `ConnectionPoolStageTest` to `ConnectionPoolStage`
- [x] `GraphStage<FlowShape<RoutedTransportItem, RoutedDataItem>>` shape preserved
- [x] Constructor takes `IActorRef router` (the `PoolRouterActor`)
- [x] All references updated
- [x] Build passes

---

### TASK-017: StreamTests for ConnectionPoolStage — Request Flow

**Description:** As a developer, I want stream tests for the GraphStage inlet so that request routing through StageActor is verified.

**Acceptance Criteria:**
- [x] Test file `src/TurboHttp.StreamTests/IO/ConnectionPoolStageTests.cs` created
- [x] Test class extends `StreamTestBase`
- [x] Test: `RoutedTransportItem` with `DataItem` is sent as `PoolRouterActor.SendRequest` to the router
- [x] Test: StageActor ref is used as `ReplyTo` in `SendRequest`
- [x] Test: After push, inlet pulls immediately (demand signaling)
- [x] Test: Non-DataItem transport items are handled gracefully (no crash)
- [x] All tests green

---

### TASK-018: StreamTests for ConnectionPoolStage — Response Flow

**Description:** As a developer, I want stream tests for the GraphStage outlet so that responses are correctly pushed as `RoutedDataItem`.

**Acceptance Criteria:**
- [x] Test: `PoolRouterActor.Response` received via StageActor → converted to `RoutedDataItem` → pushed to outlet
- [x] Test: Response buffered when outlet has no demand (no pull yet)
- [x] Test: Multiple responses pushed in order when demand arrives
- [x] Test: Response with correct PoolKey correlation (response PoolKey matches request PoolKey)
- [x] All tests green

---

### TASK-019: Fix Response Correlation — ReplyTo-Based Routing

**Description:** As a developer, I want `HandleResponse` in `HostPoolActor` to route responses back to the original requester instead of a fixed `_streamPublisher`, so that request-response correlation is maintained.

**Current problem:** `HandleResponse` sends all responses to `_streamPublisher` — there is no correlation between a request's `ReplyTo` and the response.

**Acceptance Criteria:**
- [x] `HostPoolActor` tracks `ReplyTo` per pending request (e.g. in `ConnectionState` or a separate correlation dictionary)
- [x] `HandleResponse` routes the response to the correct `ReplyTo` actor
- [x] `_streamPublisher` constructor parameter removed or repurposed
- [x] Test: Response for request A goes to ReplyTo A
- [x] Test: Response for request B goes to ReplyTo B (not A)
- [x] Test: Multiple concurrent requests to different ReplyTos are correctly routed
- [x] Build + all tests green

---

### TASK-020: Integration Test — Full Actor Pipeline

**Description:** As a developer, I want an integration test that exercises the complete actor hierarchy with the GraphStage bridge.

**Acceptance Criteria:**
- [x] Test: `RegisterHost` → `SendRequest` via stream inlet → `ConnectionActor` spawned → response flows back through stream outlet
- [x] Test: Multiple PoolKeys (hosts) work in parallel
- [x] Test: Pool under load — `MaxConnectionsPerHost` reached → requests queued → connection freed → requests drained
- [x] Test: Connection failure → reconnect → subsequent request succeeds
- [x] All tests green

---

### TASK-021: Delete Original Monolith File

**Description:** As a developer, I want the original `PoolRouterActor.cs` monolith file removed since all components have been extracted.

**Acceptance Criteria:**
- [ ] `src/TurboHttp/IO/PoolRouterActor.cs` deleted
- [ ] All references point to the new individual files
- [ ] No dead imports or orphaned `using` statements
- [ ] Build passes
- [ ] All tests green

---

### TASK-022: Validation Gate

**Description:** As a developer, I want to verify zero regressions after the full refactoring.

**Acceptance Criteria:**
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` — 0 errors
- [ ] `dotnet test src/TurboHttp.sln` — all tests green
- [ ] No new `[Obsolete]` warnings introduced
- [ ] Every file has correct namespace matching its folder
- [ ] `PoolRouterActor` — minimum 5 tests
- [ ] `HostPoolActor` — minimum 20 tests (core complexity lives here)
- [ ] `ConnectionActor` — minimum 8 tests
- [ ] `ConnectionPoolStage` — minimum 6 stream tests
- [ ] Integration test — minimum 4 scenarios
- [ ] Total new tests: ≥ 43

---

## Functional Requirements

- **FR-1:** `PoolRouterActor` must route requests to the correct `HostPoolActor` by PoolKey
- **FR-2:** `PoolRouterActor` must reject requests for unregistered hosts with `Status.Failure`
- **FR-3:** `HostPoolActor` must dynamically spawn `ConnectionActor` children up to `MaxConnectionsPerHost`
- **FR-4:** `HostPoolActor` must queue requests when all connections are busy and max is reached
- **FR-5:** `HostPoolActor` must drain pending queue when a connection becomes idle
- **FR-6:** `HostPoolActor` must evict idle connections after `IdleTimeout` (preserving at least one)
- **FR-7:** `HostPoolActor` must schedule reconnect after `ReconnectInterval` on connection failure
- **FR-8:** `ConnectionActor` must manage TCP lifecycle via `ClientManager` / `ClientRunner`
- **FR-9:** `ConnectionActor` must pump inbound data from TCP channel to response handlers
- **FR-10:** `ConnectionActor` must dispose `IMemoryOwner<byte>` when send fails
- **FR-11:** `ConnectionPoolStage` must bridge between Akka.Streams inlet/outlet and `PoolRouterActor` messages
- **FR-12:** Response correlation must be maintained — each response reaches the correct requester

## Non-Goals

- No changes to `PoolConfig`, `ConnectionPoolTypes.cs`, or `LoadBalancingStrategy` enum
- No TLS-specific logic in this plan
- No changes to `ClientManager`, `ClientRunner`, or `ClientByteMover`
- No HTTP protocol logic (encoders/decoders) — I/O layer only
- No per-host backpressure isolation (plan_3 TASK-007) — deferred to future plan
- No `PerHostConnectionLimiter` integration (plan_3 TASK-008) — deferred
- No `ConnectionReuseEvaluator` integration (plan_3 TASK-009) — deferred
- No Engine pipeline integration (plan_3 TASK-010) — separate plan
- No `PoolConfig` validation or extension (plan_3 TASK-011) — deferred

## Technical Considerations

### Testing Strategy

| Component | Test Framework | Base Class | Mock Strategy |
|-----------|---------------|------------|---------------|
| `ConnectionState` | xUnit | — | Pure unit test, no actors |
| `PoolRouterActor` | Akka.TestKit.Xunit2 | `TestKit` | `TestProbe` for child actors |
| `HostPoolActor` | Akka.TestKit.Xunit2 | `TestKit` | `TestProbe` for ConnectionActors |
| `ConnectionActor` | Akka.TestKit.Xunit2 | `TestKit` | `TestProbe` for ClientManager |
| `ConnectionPoolStage` | Akka.TestKit.Xunit2 | `StreamTestBase` | `TestProbe` for PoolRouterActor |

### Actor Hierarchy & Supervision

```
/user
  └── PoolRouterActor
        ├── HostPoolActor "host-a:443"
        │     ├── ConnectionActor (child 1)
        │     └── ConnectionActor (child 2)
        └── HostPoolActor "host-b:80"
              └── ConnectionActor (child 1)
```

- `PoolRouterActor` supervises `HostPoolActor` children (default strategy: restart)
- `HostPoolActor` supervises `ConnectionActor` children + uses `Context.Watch` for death detection
- `ConnectionActor` watches its `ClientRunner` ref for `Terminated`

### Timing in Tests

- Idle eviction and reconnect tests involve scheduled messages
- Use `TestKit.Within(TimeSpan, Action)` for timing assertions
- Use small intervals in test `PoolConfig` (e.g. `IdleTimeout = 100ms`, `ReconnectInterval = 50ms`)
- Avoid `Thread.Sleep` — use `ExpectMsg<T>(TimeSpan)` with generous timeouts

### Memory Management

- Tests creating `DataItem` must use `MemoryPool<byte>.Shared.Rent()` for `IMemoryOwner<byte>`
- Assert `Memory.Dispose()` is called on failure paths (use a tracking wrapper)
- `ConnectionActor.PostStop` must complete/cancel all resources

### File Layout After Completion

```
src/TurboHttp/IO/
  ├── ConnectionState.cs          (TASK-001)
  ├── PoolRouterActor.cs          (TASK-002, messages only)
  ├── HostPoolActor.cs            (TASK-005)
  ├── ConnectionActor.cs          (TASK-012)
  └── Stages/
      └── ConnectionPoolStage.cs  (TASK-016, renamed from ConnectionPoolStageTest)

src/TurboHttp.Tests/IO/
  ├── ConnectionStateTests.cs     (TASK-001)
  ├── PoolRouterActorTests.cs     (TASK-003, TASK-004)
  ├── HostPoolActorTests.cs       (TASK-006 – TASK-011)
  └── ConnectionActorTests.cs     (TASK-013 – TASK-015)

src/TurboHttp.StreamTests/IO/
  └── ConnectionPoolStageTests.cs (TASK-017, TASK-018)
```

### Migration from plan_3

| plan_3 Task | Status | plan_5 Equivalent | Notes |
|-------------|--------|-------------------|-------|
| TASK-001: GraphStage Foundation | ✅ Done | TASK-016, TASK-017 | Thin bridge instead of full logic |
| TASK-002: Per-Host First Connection | ✅ Done | TASK-006 | Actor spawns instead of sub-graph materialisation |
| TASK-003: Multi-Connection Scaling | ✅ Done | TASK-006 | `MaxConnectionsPerHost` enforced in `HostPoolActor` |
| TASK-004: Load Balancing | ✅ Done | TASK-007 | `SelectConnection()` in `HostPoolActor` |
| TASK-005: Health & Auto-Reconnect | ✅ Done | TASK-009, TASK-010 | `HandleFailure` + `HandleReconnect` + bug fix |
| TASK-006: Idle Eviction | ✅ Done | TASK-011 | `EvictIdleConnections()` with timer |
| TASK-007: Per-Host Backpressure | ☐ Open | Deferred | Separate future plan |
| TASK-008: PerHostConnectionLimiter | ☐ Open | Deferred | Separate future plan |
| TASK-009: ConnectionReuseEvaluator | ☐ Open | Deferred | Separate future plan |
| TASK-010: Engine Integration | ☐ Open | Deferred | Separate future plan |
| TASK-011: PoolConfig Extension | ☐ Open | Deferred | Current `PoolConfig` is sufficient |
| TASK-012: Cleanup & Migration | ☐ Open | TASK-021 | Delete monolith file |

## Task Dependencies

```
TASK-001 (ConnectionState)
    │
TASK-002 (PoolRouterActor extract) ──→ TASK-003 ──→ TASK-004
    │
TASK-005 (HostPoolActor extract) ──→ TASK-006 ──→ TASK-007 ──→ TASK-008
                                 ──→ TASK-009 ──→ TASK-010 ──→ TASK-011
    │
TASK-012 (ConnectionActor extract) ──→ TASK-013 ──→ TASK-014 ──→ TASK-015
    │
TASK-016 (Stage rename) ──→ TASK-017 ──→ TASK-018
    │
TASK-019 (ReplyTo correlation) ← depends on TASK-008 + TASK-004
    │
TASK-020 (Integration) ← depends on TASK-018 + TASK-019
    │
TASK-021 (Delete monolith) ← depends on TASK-020
    │
TASK-022 (Validation Gate) ← depends on TASK-021
```

## Success Metrics

- 22 tasks completed
- ≥ 43 new tests (5 PoolRouter + 20 HostPool + 8 Connection + 6 Stage + 4 Integration)
- 0 build errors, 0 new warnings
- HandleReconnect bug fixed and verified
- Each actor in its own file with single responsibility
- All existing tests remain green
- Response correlation verified end-to-end

## Open Questions

1. **StreamRefs API:** Should `ConnectionActor.HandleGetStreamRefs` be tested in this plan or deferred? It's a secondary API path (Akka.Streams remote refs).
2. **Supervision strategy:** Should `HostPoolActor` use a custom `SupervisorStrategy` for `ConnectionActor` children, or is the default (restart) sufficient?
3. **Stage completion:** What should `ConnectionPoolStage` do when the `PoolRouterActor` is terminated? Fail the stage? Complete gracefully?
4. **LoadBalancingStrategy wiring:** `SelectConnection()` currently does first-idle-wins. Should LeastLoaded/RoundRobin be wired in this plan or deferred?
