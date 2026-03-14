# Plan 4b: Migrate ConnectionPoolStage into Actor Hierarchy via StreamRefs

## Introduction

Plan 4 built a working actor hierarchy (`PoolRouterActor → HostPoolActor → ConnectionActor`) with
all connection-pool logic inside actors. However, the `ConnectionPoolStage` still bridges actors
to streams using actor message passing (`SendRequest` / `Response` round-trip), and `Engine.cs`
bypasses the pool entirely — it uses `ConnectionStage` directly with per-`clientManager` TCP.

Plan 4b completes the migration by:

1. Having each `ConnectionActor` create its own `SinkRef<IDataItem>` + `SourceRef<IDataItem>` and
   push them UP through the actor hierarchy.
2. `HostPoolActor` wires all `ConnectionActor` SourceRefs into a `MergeHub`, exposes one merged
   `SourceRef<IDataItem>` to `PoolRouterActor`.
3. `PoolRouterActor` wires all `HostPoolActor` SourceRefs into its own `MergeHub`, exposes ONE
   `SinkRef<ITransportItem>` (fan-out by HostKey) and ONE `SourceRef<IDataItem>` (fan-in from all
   hosts) to the outside world.
4. `ConnectionStage` becomes a **pure stream bridge** — no TCP logic, no actor system knowledge
   beyond obtaining the two refs from `PoolRouterActor`.
5. `ConnectionPoolStage` and its support types (`RoutedTransportItem`, `RoutedDataItem`) are
   deleted — they are superseded by this architecture.

---

## Goals

- `ConnectionActor` is the sole owner of TCP-to-stream wiring.
- Response data flows purely via Akka.Streams from connection → host → router → engine.
- Request data flows: engine inlet → PoolRouterActor SinkRef → actor routing → connection send queue → TCP.
- `ConnectionStage` shape stays `FlowShape<ITransportItem, IDataItem>` but contains zero TCP code.
- `ConnectionPoolStage` and `RoutedTransportItem`/`RoutedDataItem` are removed from the codebase.
- All existing integration tests continue to pass (zero regressions).

---

## Architecture

```
                 Engine (unchanged shape)
                        │
              ┌─────────▼─────────┐
              │   ConnectionStage  │  FlowShape<ITransportItem, IDataItem>
              │  (pure bridge)     │  — asks PoolRouterActor for refs
              └──┬────────────┬───┘
  ITransportItem │            │ IDataItem
                 ▼            ▲
        ┌────────────────────────────┐
        │      PoolRouterActor       │
        │  SinkRef<ITransportItem>   │ ← receives items, routes by HostKey via actor Tell
        │  SourceRef<IDataItem>      │ ← MergeHub fed by all HostPoolActor SourceRefs
        └──┬─────────────────────┬──┘
           │ DataItem (Tell)     │ RegisterHostStreamRefs(SourceRef)
           ▼                     ▲
        ┌────────────────────────────┐
        │      HostPoolActor         │
        │  per-connection request    │ ← receives DataItem, offers to connection queue
        │  queues (ISourceQueueWith  │
        │  Complete → SinkRef)       │
        │  MergeHub SourceRef        │ ← merged from all ConnectionActor SourceRefs
        └──┬─────────────────────┬──┘
           │ queue.OfferAsync    │ RegisterStreamRefs(SinkRef, SourceRef)
           ▼                     ▲
        ┌────────────────────────────┐
        │      ConnectionActor       │
        │  SinkRef<IDataItem>        │ ← Sink.ForEachAsync → TCP outbound channel
        │  SourceRef<IDataItem>      │ ← Source.Queue fed from TCP inbound channel
        └────────────────────────────┘
```

### Request path (top → bottom)
1. `ConnectionStage` pushes `ITransportItem` elements into `PoolRouterActor`'s `SinkRef`.
2. `PoolRouterActor`'s internal sink reads each element, extracts `HostKey` from `ITransportItem.Key`,
   and routes via actor `Tell` to the corresponding `HostPoolActor`.
3. `HostPoolActor` selects (or spawns) a `ConnectionActor`, and offers the `DataItem` to that
   connection's per-request `ISourceQueueWithComplete<IDataItem>`.
4. The queue's `Source<IDataItem>` is already running into the `ConnectionActor`'s `SinkRef.Sink`,
   so the item flows into the `ConnectionActor`, which writes it to the TCP outbound channel.

> **Note:** `ConnectItem` — the item that carries `TcpOptions` to establish the connection — is
> handled at HostPoolActor level. When a new `HostPoolActor` receives the first `ITransportItem`
> it checks if it carries `TcpOptions` (via the existing `ConnectItem` type) to initialise its
> `TcpOptions` before spawning connections.

### Response path (bottom → top)
1. `ConnectionActor` TCP inbound channel → `Source.Queue<IDataItem>` → `SourceRef<IDataItem>`.
2. `HostPoolActor` subscribes each `ConnectionActor` SourceRef to its `MergeHub` sink.
3. `HostPoolActor` materializes its `MergeHub.Source` as a `SourceRef<IDataItem>` and tells
   `PoolRouterActor` via `RegisterHostStreamRefs`.
4. `PoolRouterActor` subscribes each host SourceRef to its own `MergeHub` sink.
5. `ConnectionStage` subscribes to `PoolRouterActor`'s final `SourceRef<IDataItem>` and pushes
   items to its outlet.

---

## User Stories

### TASK-4B-001: Define the new actor message protocol
**Description:** As an architect, I want clear, typed message records that represent the new
stream-ref handshake between actors so that all actors can be refactored independently and
consistently.

**Acceptance Criteria:**
- [x] New message type `ConnectionActor.StreamRefsReady(ISinkRef<IDataItem> Sink, ISourceRef<IDataItem> Source)` defined on `ConnectionActor`.
- [x] New message type `HostPoolActor.RegisterConnectionRefs(IActorRef Connection, ISinkRef<IDataItem> Sink, ISourceRef<IDataItem> Source)` defined on `HostPoolActor`.
- [x] New message type `HostPoolActor.HostStreamRefsReady(HostKey Key, ISourceRef<IDataItem> Source)` defined on `HostPoolActor`.
- [x] New message type `PoolRouterActor.GetPoolRefs` and response `PoolRouterActor.PoolRefs(ISinkRef<ITransportItem> Sink, ISourceRef<IDataItem> Source)` defined on `PoolRouterActor`.
- [x] Old messages `SendRequest`, `Response`, `ConnectionIdle`, `ConnectionFailed` on `PoolRouterActor` removed.
- [x] Old messages `ConnectionResponse` on `HostPoolActor` removed.
- [x] `ConnectionActor.GetStreamRefs` and `ConnectionActor.StreamRefsResponse` removed.
- [x] Build compiles (CS errors only in implementing classes, not in type definitions).

---

### TASK-4B-002: Refactor `ConnectionActor` — push StreamRefs on connect
**Description:** As a developer, I want `ConnectionActor` to create its own `SinkRef<IDataItem>`
and `SourceRef<IDataItem>` immediately after the TCP connection is established, and push them to
its parent (`HostPoolActor`) so that the parent can wire them into the merge topology.

**Acceptance Criteria:**
- [x] `ConnectionActor` no longer handles `GetStreamRefs` request messages.
- [x] On receiving `ClientRunner.ClientConnected`:
  - Creates `Source.Queue<IDataItem>(1024, OverflowStrategy.Backpressure)` → stores `ISourceQueueWithComplete<IDataItem>` as `_responseQueue`.
  - Materializes the queue's Source as a `SourceRef<IDataItem>`.
  - Creates `Sink.ForEachAsync<IDataItem>(1, item => _outbound.WriteAsync((item.Memory, item.Length)))`.
  - Materializes the Sink as a `SinkRef<IDataItem>`.
  - Tells `Context.Parent` with `new HostPoolActor.RegisterConnectionRefs(Self, sinkRef, sourceRef)`.
- [x] `HandleSend(DataItem)` actor message handler is removed (requests now enter via the SinkRef stream).
- [x] Inbound TCP data is pumped into `_responseQueue` via `Source.Queue.OfferAsync` in `PumpInbound`.
- [x] On disconnect / `PostStop`: `_responseQueue?.Complete()` closes the SourceRef stream.
- [x] Existing reconnect logic (`Reconnect()`) clears `_responseQueue` and re-registers new refs after reconnecting.
- [x] Unit test: `ConnectionActor` tells parent `RegisterConnectionRefs` after `ClientConnected`. (CA-016)
- [x] Unit test: TCP bytes arrive → `IDataItem` emitted on the registered SourceRef. (CA-017)

**Build notes:** Restored `PoolRouterActor.SendRequest`, `PoolRouterActor.Response`, `HostPoolActor.ConnectionResponse` as stubs to fix cascading compile errors in existing `ConnectionPoolStageTests` and `HostPoolActorTests`. These stubs are clearly marked `// TODO TASK-4B-003/4B-004` and will be permanently removed when those tasks replace the actor-message routing.

---

### TASK-4B-003: Refactor `HostPoolActor` — MergeHub and connection queues
**Description:** As a developer, I want `HostPoolActor` to dynamically merge response streams from
all `ConnectionActor` children and to send requests to connections via per-connection stream queues
so that the response path is fully stream-based.

**Acceptance Criteria:**
- [x] `HostPoolActor` materializes a `MergeHub.Source<IDataItem>` on creation (or lazily on first connection), storing the `Sink<IDataItem>` (hub entry point) and `Source<IDataItem>` (merged output).
- [x] On receiving `RegisterConnectionRefs(connection, sinkRef, sourceRef)`:
  - Creates an `ISourceQueueWithComplete<IDataItem>` (buffer 128) as the per-connection request queue.
  - Runs `queueSource.RunWith(sinkRef.Sink, materializer)` — persistent stream into ConnectionActor.
  - Runs `sourceRef.Source.RunWith(mergeHubSink, materializer)` — ConnectionActor responses into MergeHub.
  - Stores `connection → queue` mapping.
- [x] When routing a request (`ITransportItem` received via actor message from `PoolRouterActor`):
  - Selects (or spawns) a `ConnectionActor` using existing `SelectConnection` logic.
  - Calls `queue.OfferAsync(dataItem)` on the selected connection's queue.
- [x] Once the MergeHub is ready, materializes `mergeHubSource.RunWith(StreamRefs.SourceRef(), materializer)` and tells `Context.Parent` with `HostStreamRefsReady(hostKey, sourceRef)`.
- [x] Removes `_replyToMap`, `PendingReplyTo`, `HandleResponse`, `ConnectionResponse` — response correlation is stream-based from here.
- [x] `ConnectionState` class retains `Active`, `Idle`, `Reusable`, HTTP version, and stream capacity tracking (no change to selection logic).
- [x] On connection death (`HandleFailure`): removes connection's queue from map, closes that queue.
- [x] Unit test: two `ConnectionActor` SourceRefs registered → both responses appear on the merged output SourceRef.
- [x] Unit test: request routing selects idle connection's queue.

---

### TASK-4B-004: Refactor `PoolRouterActor` — expose SinkRef + SourceRef
**Description:** As a developer, I want `PoolRouterActor` to expose one `SinkRef<ITransportItem>`
(routes by HostKey) and one `SourceRef<IDataItem>` (merged from all hosts) so that `ConnectionStage`
has a single clean pair of stream endpoints to wire into.

**Acceptance Criteria:**
- [x] `PoolRouterActor` materializes a `MergeHub.Source<IDataItem>` on creation.
- [x] `PoolRouterActor` materializes a `SinkRef<ITransportItem>` backed by `Sink.ForEach<ITransportItem>` that:
  - On `ConnectItem`: looks up or creates a `HostPoolActor` child (keyed by `item.Key`), and `Forward`s the item.
  - On `DataItem`: looks up the `HostPoolActor` by `item.Key` and `Tell`s the item.
- [x] `PoolRouterActor` materializes its `MergeHub.Source<IDataItem>` as a `SourceRef<IDataItem>`.
- [x] On receiving `HostStreamRefsReady(key, sourceRef)`:
  - Subscribes `sourceRef.Source.RunWith(mergeHubSink, materializer)`.
- [x] Handles `GetPoolRefs` message: replies with `PoolRefs(sinkRef, sourceRef)`.
- [x] Old `RegisterHost`, `SendRequest`, `Response` messages removed.
- [x] Unit test: `GetPoolRefs` returns valid SinkRef + SourceRef after actor start.
- [x] Unit test: item pushed into SinkRef with `HostKey("http","localhost",8080)` is forwarded to the correct HostPoolActor.

---

### TASK-4B-005: Refactor `ConnectionStage` — pure stream bridge
**Description:** As a developer, I want `ConnectionStage` to be a pure Akka.Streams flow that
wraps exactly one `SinkRef<ITransportItem>` and one `SourceRef<IDataItem>` obtained from
`PoolRouterActor` so that all TCP management is fully encapsulated inside the actor hierarchy.

**Acceptance Criteria:**
- [x] `ConnectionStage` constructor accepts `IActorRef poolRouter` (instead of `IActorRef clientManager`).
- [x] On `PreStart`, `ConnectionStage` asks `PoolRouterActor` for `PoolRefs` using `GetAsyncCallback` + actor ask pattern (timeout: 10 s).
- [x] Once `PoolRefs` received:
  - Requests flow: `inlet` elements → `Source.ActorRef` / callback → `sinkRef.Sink` (via materialised sub-graph).
  - Responses flow: `sourceRef.Source` → push to `outlet` (via `GetAsyncCallback`).
- [x] `ConnectionStage` contains NO references to `ClientManager`, `ClientRunner`, `TcpOptions`, `Channel`, or TCP-related types.
- [x] Shape stays `FlowShape<ITransportItem, IDataItem>` — Engine.cs wiring is unchanged.
- [x] Backpressure is respected: outlet pulls drive demand; inlet pulls happen when the sink is ready.
- [x] `PostStop` completes any pending sub-streams.
- [x] Unit test (stream test): items pushed into inlet appear on PoolRouter's SinkRef; items pushed into PoolRouter's SourceRef appear at outlet.

---

### TASK-4B-006: Remove `ConnectionPoolStage` and related types
**Description:** As a developer, I want `ConnectionPoolStage`, `RoutedTransportItem`, and
`RoutedDataItem` removed since they are fully superseded by the new architecture.

**Acceptance Criteria:**
- [x] `src/TurboHttp/IO/Stages/ConnectionPoolStage.cs` deleted.
- [x] `src/TurboHttp/IO/Stages/ConnectionPoolTypes.cs` deleted (contains `RoutedTransportItem` + `RoutedDataItem`).
- [x] `src/TurboHttp.Tests/IO/ConnectionPoolIntegrationTests.cs` deleted or replaced with new actor-hierarchy tests.
- [x] `src/TurboHttp.StreamTests/IO/ConnectionPoolStageTests.cs` deleted or replaced.
- [x] No remaining references to `ConnectionPoolStage`, `RoutedTransportItem`, or `RoutedDataItem` anywhere in the solution.
- [x] Build succeeds with zero errors.

---

### TASK-4B-007: Update `Engine.cs` — wire `PoolRouterActor` into `ConnectionStage`
**Description:** As a developer, I want `Engine.cs` to create and supervise a `PoolRouterActor`
and pass it to the refactored `ConnectionStage` so that the production path uses the full
actor-pool stream topology.

**Acceptance Criteria:**
- [ ] `Engine.cs` creates a `PoolRouterActor` (or receives one via constructor/DI) before building the graph.
- [ ] `BuildConnectionFlow<TEngine>` passes `poolRouterActor` to `new ConnectionStage(poolRouterActor)` instead of `clientManager`.
- [ ] The `PoolConfig` used to construct `PoolRouterActor` children is configurable (default stays).
- [ ] `ClientManager` is no longer injected into `ConnectionStage` (it is only used inside `ConnectionActor`).
- [ ] Existing `BuildConnectionFlow` topology (Broadcast → ConnectItem inject → Concat → ConnectionStage) remains structurally unchanged where it does not conflict with the new design.
- [ ] Build succeeds, zero CS warnings introduced.

---

### TASK-4B-008: Write actor-hierarchy StreamRef integration tests
**Description:** As a developer, I want targeted tests for the new StreamRef-based actor topology
so that the handshake, routing, and merge semantics are verified independently of integration tests.

**Acceptance Criteria:**
- [ ] Test: `ConnectionActor` tells parent `RegisterConnectionRefs` after receiving `ClientConnected`.
- [ ] Test: item offered to `ConnectionActor` SinkRef appears in the mocked TCP outbound channel.
- [ ] Test: inbound TCP bytes appear as `IDataItem` on `ConnectionActor` SourceRef.
- [ ] Test: `HostPoolActor` with two connections — both connection SourceRefs are merged; items from both appear on host SourceRef.
- [ ] Test: `PoolRouterActor` routes by `HostKey` — item with key `("http","host-a",80)` goes to correct host actor.
- [ ] Test: end-to-end — item pushed to `PoolRouterActor` SinkRef traverses the full hierarchy and arrives at `ConnectionActor`'s TCP outbound channel.
- [ ] Tests use `TestKit` + `TestProbe` for actor interactions; `StreamTestBase` for stream assertions.
- [ ] All new tests pass; build is green.

---

### TASK-4B-009: Validation gate — full regression run
**Description:** As a developer, I want to confirm that no existing test breaks after the
refactoring so that Plan 4b can be considered complete.

**Acceptance Criteria:**
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` — zero errors, zero warnings.
- [ ] `dotnet test src/TurboHttp.Tests/TurboHttp.Tests.csproj` — all tests pass.
- [ ] `dotnet test src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj` — all tests pass.
- [ ] `dotnet test src/TurboHttp.IntegrationTests/TurboHttp.IntegrationTests.csproj` — all tests pass.
- [ ] No new CS warnings (slopwatch clean).
- [ ] Final test counts reported and recorded.

---

## Functional Requirements

- **FR-1**: `ConnectionActor` MUST proactively push its `SinkRef<IDataItem>` and `SourceRef<IDataItem>` to its parent actor (`HostPoolActor`) immediately after TCP connect. No polling, no request/reply.
- **FR-2**: `HostPoolActor` MUST use a `MergeHub.Source<IDataItem>` to aggregate response streams from all its `ConnectionActor` children into a single `SourceRef<IDataItem>`.
- **FR-3**: `HostPoolActor` MUST maintain a per-connection `ISourceQueueWithComplete<IDataItem>` to feed requests into each `ConnectionActor`'s `SinkRef` as a persistent stream.
- **FR-4**: `PoolRouterActor` MUST expose a single `SinkRef<ITransportItem>` that routes items by `ITransportItem.Key` (HostKey) to the correct `HostPoolActor` via actor message.
- **FR-5**: `PoolRouterActor` MUST expose a single `SourceRef<IDataItem>` backed by a `MergeHub` that aggregates responses from all `HostPoolActor` children.
- **FR-6**: `ConnectionStage` MUST contain zero references to TCP infrastructure (`ClientManager`, `ClientRunner`, `Channel`). All TCP management lives in `ConnectionActor`.
- **FR-7**: `ConnectionStage` shape MUST remain `FlowShape<ITransportItem, IDataItem>` so `Engine.cs` wiring requires minimal change.
- **FR-8**: `ConnectionPoolStage`, `RoutedTransportItem`, and `RoutedDataItem` MUST be deleted.
- **FR-9**: Existing connection pool behaviour (idle eviction, max connections per host, HTTP/2 stream multiplexing) MUST be preserved in `HostPoolActor`.

---

## Non-Goals

- HTTP/1.x response pipelining across connections (i.e., interleaving bytes from two TCP connections into one decoder stream) — this plan does NOT change how `CorrelationHttp1XStage` works above `ConnectionStage`.
- TLS / ALPN negotiation inside the actor hierarchy — stays as-is.
- HTTP/3 / QUIC — out of scope.
- Connection pool configuration UI or dynamic reconfiguration.
- Performance benchmarks — covered by separate benchmark plan.

---

## Technical Considerations

### StreamRef Lifecycle
- A `SourceRef<T>` can only be subscribed to once. Each `ConnectionActor` reconnect MUST produce a
  NEW SourceRef (new `Source.Queue`, new materialisation). `HostPoolActor` must close the old
  stream subscription and open a new one via the freshly-registered SourceRef.
- A `SinkRef<T>` similarly represents a one-time materialisation. On reconnect, `HostPoolActor`
  must close the old per-connection queue and start a fresh one for the new SinkRef.

### MergeHub Dynamics
- `MergeHub.Source<T>` is created once per actor. Its `Sink<T>` is the "entry point" for adding
  producers. Each new producer (ConnectionActor, HostPoolActor) adds itself by running
  `producer.RunWith(mergeHubSink, mat)`.
- The `MergeHub.Source<T>` must be materialised (and the resulting `SourceRef` sent up the hierarchy)
  BEFORE any producer tries to subscribe. Sequence: actor preStart → materialise hub → send SourceRef
  up → later, receive child refs → subscribe them to hub.

### Backpressure on the Request Side
- Per-connection queues (`Source.Queue`) provide backpressure signalling. If `OfferAsync` returns
  `QueueOfferResult.Dropped`, the HostPoolActor must either buffer the request or signal back-pressure
  to the upstream sender. For now, log a warning and drop (same semantics as the current pool).

### ConnectItem Handling
- `ConnectItem` carries `TcpOptions` and has `HostKey.Default` (no routing key). `PoolRouterActor`
  must handle `ConnectItem` specially: use the `Options.Host` / `Options.Port` to derive the routing
  key when the item's `Key` is `HostKey.Default`.

### Ordering Guarantees (HTTP/1.x)
- The merged response stream from `HostPoolActor` is UNORDERED across connections. `Engine.cs`
  currently feeds one request at a time per `ConnectionStage` instance (the Balance stage
  distributes across N `connectionCount` instances). Each instance is backed by ONE pool router.
  Within one connection, TCP guarantees order. This plan does not change that invariant.

---

## Success Metrics

- `ConnectionPoolStage.cs` deleted — confirmed by `git status`.
- `ConnectionStage.cs` contains no `ClientManager`, `ClientRunner`, or `Channel` references.
- All 2803 existing tests remain green (no regressions).
- At least 15 new tests cover the StreamRef handshake and routing.
- Build produces zero warnings.

---

## Open Questions

- **Q1**: Should `PoolRouterActor` be created inside `Engine.cs` or injected from outside (e.g., via `TurboClientOptions`)? Injecting from outside allows sharing one pool across multiple `Engine` instances. Recommendation: inject, with a sensible default factory.
- **Q2**: On reconnect, `ConnectionActor` re-registers new StreamRefs with `HostPoolActor`. Should the old per-connection queue drain first (graceful) or be cancelled immediately? Recommendation: cancel immediately and log a warning — simplicity over grace.
- **Q3**: `MergeHub` completion semantics — if ALL connections of a HostPoolActor die, does the MergeHub complete (and the SourceRef close)? Should HostPoolActor keep the hub alive regardless? Recommendation: keep hub alive for the lifetime of the actor; individual connection streams complete without closing the hub.
