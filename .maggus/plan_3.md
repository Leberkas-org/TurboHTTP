# Plan: Dynamic Connection Pool — ConnectionPoolStage

## Introduction

Build a dynamic connection pool layer for TurboHttp that manages per-host connection lifecycles using Akka.Streams. The pool orchestrates multiple `ConnectionStage` instances per host, scales dynamically when needed (e.g. when HTTP/2 stream limits are reached or HTTP/1.x needs parallel requests), and provides idle eviction, health monitoring, auto-retry on connection loss, load balancing, and per-host backpressure.

The key architectural insight: rather than bypassing `ConnectionStage`, the pool **wraps and orchestrates** multiple `ConnectionStage` instances — each one materialised as an independent Akka.Streams sub-graph with its own TCP connection lifecycle.

### Design Decisions (from clarifying questions)

| Question | Answer |
|----------|--------|
| Connection creation | **Explicit** — ConnectItem must arrive before DataItems |
| Dynamism granularity | **Maximal** — full lifecycle + load balancing across connections |
| Integration with ConnectionStage | **Orchestration** — pool manages multiple ConnectionStage instances |
| Scope | **Full** — multi-connection, idle eviction, health, retry, backpressure |
| HTTP/2 stream tracking | **Delegated** — H2-Connection-Stage manages itself |

## Goals

- Dynamic per-host connection management instead of static partition slots
- Explicit connection setup via `ConnectItem` — connections created only after ConnectItem arrives
- Multiple connections per host when needed (HTTP/2 stream limits, HTTP/1.x parallelism)
- Load balancing across active connections of a host (round-robin or least-loaded)
- Idle connection eviction after configurable timeout
- Connection health monitoring with automatic retry on loss
- Per-host backpressure — a slow host must not block other hosts
- Clean integration into the existing Engine pipeline
- HTTP/2 stream tracking stays in `Http20ConnectionStage` — pool only queries status

## User Stories

### TASK-001: ConnectionPoolStage — Custom GraphStage Foundation

**Description:** As a developer, I want a `ConnectionPoolStage` custom `GraphStage<FlowShape<RoutedTransportItem, RoutedDataItem>>` that serves as the central entry point for all connection pool operations, orchestrating `ConnectionStage` instances internally.

**Acceptance Criteria:**
- [x] `ConnectionPoolStage` inherits from `GraphStage<FlowShape<RoutedTransportItem, RoutedDataItem>>`
- [x] Inner `Logic` class with correct inlet/outlet handling (Pull/Push)
- [x] Constructor takes `Func<ConnectionStage> connectionStageFactory` and `PoolConfig`
- [x] Stage uses `StageActor` for async actor communication (same pattern as `ConnectionStage`)
- [x] ConnectItem on inlet → registered in internal state (host known, no connection yet)
- [x] DataItem on inlet without prior ConnectItem for that PoolKey → stage failure with descriptive error
- [x] Empty pass-through (only ConnectItems, no DataItems) completes without error
- [x] Unit tests for foundation (connect registers host, unknown key is rejected)
- [x] Typecheck/build passes

### TASK-002: Per-Host Connection Lifecycle — First Connection via ConnectionStage

**Description:** As a developer, I want the pool to materialise a `ConnectionStage` instance for a host on the first DataItem after a ConnectItem, and route data through it.

**Acceptance Criteria:**
- [x] After ConnectItem + first DataItem, a new `ConnectionStage` is materialised as a sub-graph
- [x] Sub-graph materialisation uses `SubFusingActorMaterializer` from the stage's `Materializer` (available via `GraphStageLogic`)
- [x] The materialised `ConnectionStage` flow is driven by `Source.Queue` (inlet) and `Sink.ForEach` (outlet) — pool pushes items into it and reads results
- [x] DataItem is forwarded to the ConnectionStage's materialised source
- [x] Response data from ConnectionStage is emitted as `RoutedDataItem` with correct PoolKey
- [x] Internal state tracks: `Dictionary<string, HostPool>` with list of active connections
- [x] `HostPool` contains: `TcpOptions` (from ConnectItem), `List<ConnectionSlot>`, connection counter
- [x] `ConnectionSlot` wraps: materialised ConnectionStage (Source.Queue + completion), active/idle status, pending request count
- [x] Single host, single connection, request-response works end-to-end
- [x] Unit tests: single-host single-connection roundtrip with fake ConnectionStage
- [x] Typecheck/build passes

### TASK-003: Multi-Connection per Host — Dynamic Scaling

**Description:** As a developer, I want the pool to automatically create additional ConnectionStage instances for a host when existing connections are saturated.

**Acceptance Criteria:**
- [ ] `PoolConfig.MaxConnectionsPerHost` limits the maximum number of ConnectionStage instances per host
- [ ] When all existing connections for a host are "busy", a new ConnectionStage is materialised (up to the limit)
- [ ] "Busy" definition for HTTP/1.x: connection has a pending request without a response
- [ ] HTTP/2 busy status is managed by H2-Connection-Stage itself (pool does not track stream count)
- [ ] When max reached and all busy → DataItem is queued internally (backpressure)
- [ ] New connection uses the same `TcpOptions` from the original ConnectItem
- [ ] Each new ConnectionStage is a fresh materialisation with independent state
- [ ] Unit tests: 3 parallel requests → 3 ConnectionStage instances (HTTP/1.x, MaxConnections=3)
- [ ] Unit tests: 4 requests with MaxConnections=2 → 2 connections, 2 queued
- [ ] Typecheck/build passes

### TASK-004: Load Balancing Across Connections

**Description:** As a developer, I want incoming DataItems to be intelligently distributed across available ConnectionStage instances for a host.

**Acceptance Criteria:**
- [ ] Load balancing strategy is configurable in `PoolConfig` (enum: `RoundRobin`, `LeastLoaded`)
- [ ] `RoundRobin`: cycles through all active connections of a host
- [ ] `LeastLoaded`: selects connection with fewest pending requests
- [ ] Default strategy: `LeastLoaded`
- [ ] Idle connections are preferred (don't create a new connection when one is idle)
- [ ] Unit tests: multiple requests are distributed evenly across connections
- [ ] Typecheck/build passes

### TASK-005: Connection Health Monitoring and Auto-Reconnect

**Description:** As a developer, I want the pool to detect dead connections and automatically replace them with fresh ConnectionStage instances.

**Acceptance Criteria:**
- [ ] Materialised ConnectionStage completion/failure is detected (via `WatchTermination` on the materialised graph)
- [ ] Dead ConnectionSlot is marked as `Dead` and removed from the active list
- [ ] Pending requests on a dead connection are re-routed to another ConnectionSlot or queued for retry
- [ ] If no other connection is available → a new ConnectionStage is materialised (retry)
- [ ] `PoolConfig.MaxReconnectAttempts` limits retry attempts per host
- [ ] `PoolConfig.ReconnectInterval` defines wait time between retries (via `ScheduleOnce` timer)
- [ ] After MaxReconnectAttempts → stage fails with `ConnectionPoolException`
- [ ] Unit tests: connection dies → retry → new request works
- [ ] Unit tests: connection dies → max retries reached → exception
- [ ] Typecheck/build passes

### TASK-006: Idle Connection Eviction

**Description:** As a developer, I want unused ConnectionStage instances to be automatically shut down after a timeout to free resources.

**Acceptance Criteria:**
- [ ] `PoolConfig.IdleTimeout` (default 5 minutes) defines maximum idle time
- [ ] Periodic timer (via `ScheduleRepeatedly` in GraphStageLogic) checks for idle connections
- [ ] Connection without traffic for > IdleTimeout is gracefully shut down (complete the materialised source)
- [ ] At least one connection per host is preserved (no complete eviction while host is registered)
- [ ] Activity timestamp is updated on every request and response
- [ ] `ConnectionReuseEvaluator` results feed into eviction decisions (Keep-Alive timeout overrides IdleTimeout)
- [ ] Unit tests: connection idle > timeout → shut down
- [ ] Unit tests: connection with traffic → preserved
- [ ] Unit tests: last connection per host → preserved despite idle
- [ ] Typecheck/build passes

### TASK-007: Per-Host Backpressure

**Description:** As a developer, I want a slow host to not block other hosts — backpressure should be isolated per host.

**Acceptance Criteria:**
- [ ] Each HostPool has its own internal queue with configurable size (`PoolConfig.PerHostQueueSize`)
- [ ] When all connections for a host are busy and the queue is full → incoming DataItems for that host are buffered
- [ ] Other hosts can continue to send requests
- [ ] Queue overflow strategy configurable: `DropOldest`, `DropNewest`, `Fail` (default: `Fail`)
- [ ] When a connection becomes free → queue is drained
- [ ] Global inlet pull continues as long as at least one host can accept items
- [ ] Unit tests: Host-A slow, Host-B fast → Host-B not blocked
- [ ] Typecheck/build passes

**Note:** Per-host backpressure in a single-inlet GraphStage requires internal buffering. The inlet can only be globally pulled/not-pulled. Solution: internal per-host queues with a configurable global buffer limit. The inlet is pulled as long as total buffered items < global limit.

### TASK-008: PerHostConnectionLimiter Integration

**Description:** As a developer, I want the existing `PerHostConnectionLimiter` integrated into the pool for connection limit enforcement.

**Acceptance Criteria:**
- [ ] `PerHostConnectionLimiter` is used inside the pool instead of custom counting
- [ ] `TryAcquire()` is called before materialising a new ConnectionStage
- [ ] `Release()` is called when a ConnectionStage is shut down
- [ ] If `TryAcquire()` returns false → no new connection, request is queued
- [ ] `PerHostConnectionLimiter` max and `PoolConfig.MaxConnectionsPerHost` are kept consistent
- [ ] Unit tests: limiter blocks connection → request waits
- [ ] Typecheck/build passes

### TASK-009: ConnectionReuseEvaluator Integration

**Description:** As a developer, I want connection reuse decisions (Keep-Alive, Close) to flow back into the pool.

**Acceptance Criteria:**
- [ ] After each response, `ConnectionReuseEvaluator.Evaluate()` is called
- [ ] `ConnectionReuseDecision.CanReuse == false` → ConnectionStage is shut down after response
- [ ] `ConnectionReuseDecision.KeepAliveTimeout` → overrides `PoolConfig.IdleTimeout` for that connection slot
- [ ] `ConnectionReuseDecision.MaxRequests` → ConnectionStage is shut down after N requests
- [ ] Request counter per ConnectionSlot
- [ ] Unit tests: "Connection: close" response → ConnectionStage shut down
- [ ] Unit tests: "Keep-Alive: timeout=10" → idle timeout set to 10s
- [ ] Typecheck/build passes

### TASK-010: Engine Pipeline Integration

**Description:** As a developer, I want the new ConnectionPoolStage integrated into the Engine pipeline, replacing the current direct ConnectionStage usage.

**Acceptance Criteria:**
- [ ] `Engine.BuildConnectionFlow<TEngine>()` uses `ConnectionPoolStage` instead of direct `ConnectionStage`
- [ ] `BuildProtocolFlow<TEngine>()` `Balance(N)` is replaced by the pool (pool manages parallelism)
- [ ] `Engine.CreateFlow()` test overload accepts a ConnectionStage factory for the pool
- [ ] HTTP/1.0 and HTTP/1.1: pool with MaxConnectionsPerHost=4 (same as current Balance(4))
- [ ] HTTP/2: pool with MaxConnectionsPerHost=1 (multiplexing via streams)
- [ ] Existing `EngineVersionRoutingTests` remain green
- [ ] Existing `StreamTests` remain green
- [ ] New integration test: full Engine → Pool → ConnectionStage → Response
- [ ] Typecheck/build passes

### TASK-011: PoolConfig Extension and Validation

**Description:** As a developer, I want a complete `PoolConfig` with all required parameters and validation.

**Acceptance Criteria:**
- [ ] `PoolConfig` extended with:
  - `MaxConnectionsPerHost` (default 10)
  - `IdleTimeout` (default 5 min)
  - `ConnectionTimeout` (default 30s)
  - `MaxReconnectAttempts` (default 3)
  - `ReconnectInterval` (default 5s)
  - `PerHostQueueSize` (default 100)
  - `LoadBalancingStrategy` (enum, default LeastLoaded)
  - `QueueOverflowStrategy` (enum, default Fail)
- [ ] Validation: no negative values, MaxConnections >= 1, Timeouts > 0
- [ ] Invalid config → `ArgumentException` in constructor
- [ ] Unit tests for all defaults and validation
- [ ] Typecheck/build passes

### TASK-012: Cleanup and Migration

**Description:** As a developer, I want the old static partition-based pool removed and replaced by the new dynamic pool.

**Acceptance Criteria:**
- [ ] Old `ConnectionPool` static class code (partition-based) is removed
- [ ] `ConnectionPoolTests` are migrated to new `ConnectionPoolStage`
- [ ] All existing tests remain green
- [ ] No dead code in `ConnectionDemuxStage.cs`
- [ ] File renamed to `ConnectionPoolStage.cs` if appropriate
- [ ] Typecheck/build passes

## Functional Requirements

- **FR-1:** The pool must manage a separate connection lifecycle per PoolKey (host:port)
- **FR-2:** A ConnectItem must arrive before the first DataItem for a PoolKey; otherwise the DataItem is rejected
- **FR-3:** The pool must dynamically create new ConnectionStage instances when existing ones are saturated (up to MaxConnectionsPerHost)
- **FR-4:** The pool must close connections after IdleTimeout automatically (at least one per host is preserved)
- **FR-5:** The pool must detect dead connections and automatically re-route or retry requests
- **FR-6:** The pool must isolate backpressure per host — a slow host must not block other hosts
- **FR-7:** The pool must load-balance across active connections of a host
- **FR-8:** HTTP/2 stream tracking is delegated to H2-Connection-Stage — the pool treats H2 connections as black boxes
- **FR-9:** `ConnectionReuseEvaluator` results must feed into connection lifecycle decisions
- **FR-10:** The pool must interact with the existing `ClientManager`/`ClientRunner` actor system through `ConnectionStage`

## Non-Goals

- **No** HTTP/2 stream-level routing in the pool — stays in `Http20ConnectionStage`
- **No** DNS resolution caching or DNS-based load balancing
- **No** connection pre-warming (connections created only on demand)
- **No** cross-host connection sharing (each host has its own pool)
- **No** persistence of pool state across process restarts
- **No** circuit breaker pattern (may come as a separate stage later)
- **No** changes to `ClientManager`/`ClientRunner` — pool only orchestrates them via `ConnectionStage`

## Technical Considerations

### Why ConnectionStage Orchestration (not bypass)

The `ConnectionStage` already handles:
- Actor communication with `ClientManager` (connection creation via `CreateTcpRunner`)
- `ClientRunner.ClientConnected` / `ClientDisconnected` message handling
- Pending write/read queues during connection establishment
- Graceful shutdown (`DoClose` to runner, channel completion)
- Auto-reconnect on disconnect

Replicating all of this in the pool stage would be duplication. Instead, the pool **materialises multiple ConnectionStage flows** as independent sub-graphs, each with its own TCP connection.

### Sub-Graph Materialisation Pattern

Each `ConnectionSlot` materialises a `ConnectionStage` flow independently:

```csharp
// Inside ConnectionPoolStage.Logic
var connectionFlow = Flow.FromGraph(connectionStageFactory());
// Type: Flow<ITransportItem, (IMemoryOwner<byte>, int), NotUsed>

// Materialise with Source.Queue as inlet, Sink callback as outlet
var (queue, completion) = Source.Queue<ITransportItem>(bufferSize, OverflowStrategy.Backpressure)
    .Via(connectionFlow)
    .ToMaterialized(
        Sink.ForEach<(IMemoryOwner<byte>, int)>(tuple => responseCallback(poolKey, tuple)),
        Keep.Both)
    .Run(subFusingMaterializer);

// Store in ConnectionSlot
slot.Queue = queue;           // push ITransportItem into this
slot.Completion = completion;  // watch for failure/completion
```

This gives each slot:
- Its own `ConnectionStage` instance with independent TCP state
- A `ISourceQueueWithComplete<ITransportItem>` to push items
- A `Task` completion to detect connection death

### Architecture Diagram

```
                    ConnectionPoolStage (single GraphStage)
                    ┌──────────────────────────────────────────────┐
                    │                                              │
  RoutedTransportItem → [Inlet Handler]                           │
                    │       │                                      │
                    │       ├─ ConnectItem → register HostPool     │
                    │       │                                      │
                    │       └─ DataItem → find/create slot         │
                    │              │                                │
                    │    ┌─────────┼──────────────┐                │
                    │    │HostPool │HostPool      │ HostPool       │
                    │    │ host-a  │ host-b       │ host-c         │
                    │    │         │              │                │
                    │    │ ┌─────────────────┐    │                │
                    │    │ │ ConnectionSlot  │    │                │
                    │    │ │ Source.Queue ──→│    │                │
                    │    │ │ ConnectionStage │    │ ...            │
                    │    │ │ ──→ Sink.ForEach│    │                │
                    │    │ └─────────────────┘    │                │
                    │    │ ┌─────────────────┐    │                │
                    │    │ │ ConnectionSlot  │    │                │
                    │    │ │ (scaled up)     │    │                │
                    │    │ └─────────────────┘    │                │
                    │    └───────────┬────────────┘                │
                    │                │                              │
                    │    [Async callbacks from Sink.ForEach]        │
                    │                │                              │
                    │                ↓                              │
                    │    RoutedDataItem → [Outlet Push]             │
                    └──────────────────────────────────────────────┘
```

### Why Not GroupBy + Via

| Aspect | GroupBy + Via | Custom GraphStage + ConnectionStage |
|--------|-------------|-------------------------------------|
| Per-host state | Shared (bug) | Isolated (each ConnectionStage is independent) |
| Dynamic connections | Not possible | Full control via sub-graph materialisation |
| Connection scaling | No | Yes, materialise additional ConnectionStages |
| Idle eviction | Not possible | Via TimerMessages |
| Health monitoring | Not possible | Via WatchTermination on sub-graphs |
| Load balancing | No | Yes, configurable |
| ConnectionStage reuse | N/A | Yes, leverages existing TCP lifecycle code |

### Concurrency Model

- **GraphStageLogic** is single-threaded (Akka guarantee) — no locking needed for internal state
- **Async callbacks** (`GetAsyncCallback<T>`) for sub-graph responses (Sink.ForEach → pool outlet)
- **TimerMessages** for idle eviction scheduling
- **Sub-graph materialisation** happens on the stage's `Materializer` — each sub-graph runs independently

### Existing Classes Reused

| Class | Usage in Pool |
|-------|--------------|
| `ConnectionStage` | **Wrapped as sub-graph** — each ConnectionSlot materialises one instance |
| `ClientManager` | Used indirectly through ConnectionStage |
| `ClientRunner` | Used indirectly through ConnectionStage |
| `PerHostConnectionLimiter` | Integrated for connection limit enforcement |
| `ConnectionReuseEvaluator` | Integrated for Keep-Alive/Close decisions |
| `PoolConnectionManager` | Integrated for activity tracking |
| `PoolConfig` | Extended with new parameters |

### Integration with Engine

```
Engine.BuildConnectionFlow<TEngine>()
  ├── current: ExtractOptions → [ConnectItem + BidiFlow] → ConnectionStage (single)
  └── new:     ExtractOptions → [ConnectItem + BidiFlow] → ConnectionPoolStage
                                                              ├── ConnectionStage 1 (materialised sub-graph)
                                                              ├── ConnectionStage 2 (materialised sub-graph)
                                                              └── ConnectionStage N (materialised sub-graph)

Engine.BuildProtocolFlow<TEngine>()
  ├── current: Balance(4) → 4x BuildConnectionFlow → Merge(4)
  └── new:     1x BuildConnectionFlow with pool (pool manages parallelism internally)
```

## Success Metrics

- All existing stream tests and integration tests remain green
- ConnectionPool tests cover: single-host, multi-host, multi-connection, eviction, retry, backpressure
- Performance: no measurable overhead vs direct ConnectionStage for single-connection scenarios
- Dynamic scaling verifiable: N parallel requests → N ConnectionStage instances (up to limit)
- Idle eviction verifiable: ConnectionStage instance shut down after timeout

## Open Questions

1. **Timer granularity:** How often should the idle eviction timer fire? Every second? Every 30 seconds? Configurable? Configurable!
2. **Graceful shutdown:** How should the pool behave on stage shutdown? Drain all pending requests or abort immediately? drain all pending requests
3. **Error propagation:** If a host is permanently unreachable (MaxReconnect exhausted) — should only that host fail or the entire stage? only host has to fail
4. **HTTP/2 connection signaling:** How does the pool learn that an H2 ConnectionStage has reached its stream limit, given that tracking lives inside the H2 stage? Do we need a callback/signal interface? 
5. **Pool key semantics:** Is `host:port` sufficient as pool key, or must the scheme (http/https) also be included? (Relevant for HTTP→HTTPS redirects). Must be `schema:host:port:httpversion`
