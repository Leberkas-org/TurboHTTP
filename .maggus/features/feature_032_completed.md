<!-- maggus-id: 6a860ec8-b1d0-4d94-aa76-e3d7d6e2ae9d -->

# Feature 032: Multi-Connection per Host & Lazy Engine Materialization

## Introduction

Two architectural improvements to TurboHttp's stream pipeline:

1. **Multi-connection per host** — `GroupByRequestKeyStage` currently creates exactly one substream per `RequestEndpoint`. When HTTP/2's `MaxConcurrentStreams` limit is reached, the pipeline backpressures instead of opening a second connection. This feature extends `GroupByRequestKeyStage` to maintain N slots per key, routing to a new slot (= new connection) when existing slots are backpressured.

2. **Lazy engine materialization** — All 4 protocol engines (HTTP/1.0, 1.1, 2.0, 3.0) currently materialize at startup via `Partition(4)` in `ProtocolCoreGraphBuilder`, even if only one version is used. Wrapping each branch in `Flow.LazyFlow` ensures unused engines never allocate resources.

### Architecture Context

- **Components involved:**
  - `GroupByRequestKeyStage` (`src/TurboHttp/Streams/Stages/Internal/`) — core routing stage
  - `HostKeyGroupByExtensions` + `HostKeyMergeBack` — extensions that thread parameters to the stage
  - `ProtocolCoreGraphBuilder` (`src/TurboHttp/Streams/`) — builds the 4-branch protocol graph
  - `ConnectionPolicy` (`src/TurboHttp/Protocol/RFC9112/`) — user-facing connection config
- **No new message types, no shared state between stages** — routing signal is Akka Streams native backpressure observed via `SubflowState.Offering`.
- **Deadlock-safe** — no Akka graph cycles introduced; the multi-slot logic is purely local to `GroupByRequestKeyStage`.

## Goals

- When an HTTP/2 (or HTTP/3) connection is saturated (MaxConcurrentStreams backpressure), open additional parallel connections instead of stalling.
- Allow users to configure max parallel connections per host per protocol via `ConnectionPolicy`.
- Allow users to configure per-slot queue size via `ConnectionPolicy`.
- HTTP/1.x remains single-connection per host (unchanged behavior).
- HTTP/2 and HTTP/3 default to 2 parallel connections (opt-in via sensible defaults).
- Unused protocol engines never materialize, saving resources.

## Tasks

### TASK-032-001: Extend ConnectionPolicy with multi-connection config

**Description:** As a library user, I want to configure how many parallel connections TurboHttp opens per host, so that I can tune HTTP/2 and HTTP/3 connection concurrency.

**Token Estimate:** ~15k tokens
**Predecessors:** none
**Successors:** TASK-032-002, TASK-032-003
**Parallel:** yes — can run alongside TASK-032-004

**Acceptance Criteria:**
- [x] `ConnectionPolicy` gains `MaxHttp2ConnectionsPerHost` (default `2`)
- [x] `ConnectionPolicy` gains `MaxHttp3ConnectionsPerHost` (default `2`)
- [x] `ConnectionPolicy` gains `SlotQueueSize` (default `1`) — per-slot Source.Queue buffer size; must not exceed `1` to ensure `Offering=true` propagates after at most one queued item
- [x] HTTP/1.0 and HTTP/1.1 are hard-coded to 1 connection per host (not configurable via these properties)
- [x] XML doc comments explain each property
- [x] Existing `ConnectionPolicy` tests still pass
- [x] Build succeeds with zero errors

---

### TASK-032-002: Extend GroupByRequestKeyStage with multi-slot SubflowGroup

**Description:** As a developer, I want `GroupByRequestKeyStage` to maintain multiple substreams (slots) per host key, so that backpressure on one slot triggers routing to another slot or opening a new connection.

**Token Estimate:** ~120k tokens
**Predecessors:** TASK-032-001
**Successors:** TASK-032-005
**Parallel:** no

**Acceptance Criteria:**
- [x] New nested class `SubflowGroup` holds `List<SubflowState>` slots for one key
- [x] `SubflowState` gains `HasCapacity` property: `!IsDead && !Offering`
- [x] `_subflows` changes from `Dictionary<RequestEndpoint, SubflowState>` to `Dictionary<RequestEndpoint, SubflowGroup>`
- [x] New stage constructor parameter `maxSubstreamsPerKey` (int, default `1`)
- [x] `HandlePush` routing logic:
  - Find first slot with `HasCapacity = true` → route there
  - If none found: clean dead slots, check per-key and total limits → create new slot OR route to least-loaded slot
- [x] Dead slot handling: pending items transferred to another alive slot in the same group, or new slot created (replaces `ReplaceSubstream`)
- [x] `TryFinish`, `TryCompleteStage` iterate `group.Slots` instead of single `SubflowState`
- [x] `_onOfferComplete` correctly identifies the state by slot reference within the group
- [x] `maxSubstreamsPerKey = 1` behaves identically to the previous implementation (backward compat)
- [x] All existing `GroupByRequestKeyStage` tests pass unchanged
- [x] Build succeeds with zero errors

---

### TASK-032-003: Thread maxSubstreamsPerKey and SlotQueueSize through extensions

**Description:** As a developer, I want `HostKeyGroupByExtensions` and `HostKeyMergeBack` to forward the new parameters to `GroupByRequestKeyStage`, so that callers can configure multi-slot routing.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-032-001, TASK-032-002
**Successors:** TASK-032-004
**Parallel:** no

**Acceptance Criteria:**
- [x] `GroupByRequestKey<T, TMat>` extension gains `maxSubstreamsPerKey = 1` parameter
- [x] `GroupByRequestKey<T, TMat>` extension gains `slotQueueSize = 64` parameter
- [x] `HostKeyMergeBack<T, TMat>` stores and passes both parameters to `GroupByRequestKeyStage`
- [x] Existing call sites compile without changes (defaults preserve old behavior)
- [x] Build succeeds with zero errors

---

### TASK-032-004: Thread config through ProtocolCoreGraphBuilder + lazy wrapping

**Description:** As a developer, I want `ProtocolCoreGraphBuilder` to pass per-protocol multi-slot config to `GroupByRequestKey` and wrap all 4 engines in `Flow.LazyFlow`, so that unused engines never materialize and connection concurrency is configurable.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-032-001, TASK-032-003
**Successors:** TASK-032-005
**Parallel:** no

**Acceptance Criteria:**
- [x] `BuildProtocolFlow<TEngine>` gains `maxSubstreamsPerKey` parameter
- [x] `Build()` reads `ConnectionPolicy` from `clientOptions` and passes per-protocol values:
  - HTTP/1.0: `maxSubstreamsPerKey = 1`
  - HTTP/1.1: `maxSubstreamsPerKey = policy.MaxConnectionsPerHost`
  - HTTP/2.0: `maxSubstreamsPerKey = policy.MaxHttp2ConnectionsPerHost`
  - HTTP/3.0: `maxSubstreamsPerKey = policy.MaxHttp3ConnectionsPerHost`
- [x] `slotQueueSize` forwarded from `policy.SlotQueueSize`
- [x] All 4 protocol branches wrapped in `Flow.LazyFlow(() => Task.FromResult(...)).MapMaterializedValue(_ => NotUsed.Instance)`
- [x] `highThroughputBuffer` attributes preserved on lazy-wrapped flows
- [x] Existing integration tests pass (engines still function when used)
- [x] Build succeeds with zero errors

---

### TASK-032-005: Tests for multi-slot routing and lazy materialization

**Description:** As a developer, I want tests covering multi-slot routing and lazy materialization, so that regressions are caught and the feature behavior is documented.

**Token Estimate:** ~80k tokens
**Predecessors:** TASK-032-002, TASK-032-004
**Successors:** none
**Parallel:** no
**Model:** sonnet

**Acceptance Criteria:**
- [x] New test file `src/TurboHttp.StreamTests/Streams/NN_GroupByRequestKeyMultiSlotTests.cs`
- [x] Test: `maxSubstreamsPerKey=1` — single slot per key, same behavior as before
- [x] Test: `maxSubstreamsPerKey=2` — first slot backpressured (slow downstream) → second slot created for same key
- [x] Test: per-key limit reached + all slots backpressured → routes to least-loaded slot (Pending.Count)
- [x] Test: dead slot cleanup — slot terminates with pending items → items transferred to alive slot or new slot
- [x] Test: total `maxSubstreams` enforced across all keys (one key cannot consume all slots)
- [x] Test: independent fan-out — key A and key B each get separate `SubflowGroup` instances
- [x] Test: lazy materialization — only the used protocol branch materializes (at least verify for HTTP/2 path)
- [x] File follows project test conventions: `public sealed class`, namespace `TurboHttp.StreamTests.Streams`, `[Fact(Timeout = 5000)]`, file-prefix `NN_`
- [x] Max 500 lines per test file — split if needed
- [x] All new tests pass
- [x] All existing tests still pass (`dotnet test ./src/TurboHttp.sln`)

---

## Task Dependency Graph

```
TASK-032-001 ──→ TASK-032-002 ──→ TASK-032-003 ──→ TASK-032-004 ──→ TASK-032-005
                                                                   ↑
                                               TASK-032-002 ───────┘
```

Simplified linear:
```
032-001 → 032-002 → 032-003 → 032-004 ──→ 032-005
                                       ↑
                        032-002 ───────┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-032-001 | ~15k | none | yes (with 032-004 if decoupled) | — |
| TASK-032-002 | ~120k | 032-001 | no | — |
| TASK-032-003 | ~25k | 032-001, 032-002 | no | — |
| TASK-032-004 | ~50k | 032-001, 032-003 | no | — |
| TASK-032-005 | ~80k | 032-002, 032-004 | no | sonnet |

**Total estimated tokens:** ~290k

## Functional Requirements

- FR-1: `GroupByRequestKeyStage` must support N slots per `RequestEndpoint` key, controlled by `maxSubstreamsPerKey`
- FR-2: Slot selection must use `HasCapacity = !IsDead && !Offering` — a slot busy with a pending `OfferAsync` is not eligible for new items
- FR-3: When no slot has capacity and per-key limit not reached and total limit not reached, a new slot (= new substream downstream = new connection) must be created
- FR-4: When all slots are at capacity and all limits are reached, the item must queue on the slot with the fewest pending items (`FindLeastLoaded`)
- FR-5: When a slot dies with pending items, those items must be transferred to another alive slot in the same group, or a new slot must be created if none exists
- FR-6: `maxSubstreamsPerKey = 1` must produce identical behavior to the current implementation
- FR-7: HTTP/1.0 must always use exactly 1 slot per key (hardcoded, regardless of `ConnectionPolicy`)
- FR-8: HTTP/1.1 must use `policy.MaxConnectionsPerHost` slots per key
- FR-9: HTTP/2 must default to 2 slots per key (`policy.MaxHttp2ConnectionsPerHost = 2`)
- FR-10: HTTP/3 must default to 2 slots per key (`policy.MaxHttp3ConnectionsPerHost = 2`)
- FR-11: Per-slot `Source.Queue` buffer size must come from `policy.SlotQueueSize` (default `1`, max `1`); values greater than `1` defeat the `Offering`-based capacity signal and must be rejected (throw `ArgumentOutOfRangeException` in `ConnectionPolicy` setter)
- FR-12: All 4 protocol engine branches in `ProtocolCoreGraphBuilder` must be wrapped in `Flow.LazyFlow` — unused engines must not materialize
- FR-13: `Flow.LazyFlow` wrapping must preserve `highThroughputBuffer` attributes on each branch

## Non-Goals

- No changes to `TcpTransportHandler`, `QuicTransportHandler`, `ExtractOptionsStage`, or `ConnectItem` — the connection-per-slot mapping is implicit through the substream lifecycle
- No shared tracker object between `GroupByRequestKeyStage` and transport stages
- No new `IControlItem` message types
- No HTTP/1.1 pipelining — HTTP/1.1 multi-slot means multiple separate connections, not pipelining
- No per-request slot affinity (sticky routing) — requests always go to the first available slot

## Technical Considerations

### Design Decision: Pure backpressure routing (not feedback/MergeHub)

Feedback-based designs were considered and rejected:

- **ISlotCapacity (volatile fields)** — distributes shared mutable state across stage actor boundaries, violating Akka's actor isolation model.
- **FanIn + MergeHub** — `MaxConcurrentStreamsItem` feedback provides the server's limit but not current utilization. To know utilization, stream counting is required (increment on `StreamAcquireItem`, decrement on response), which reintroduces cross-actor state tracking via signal taps + response taps inside `BuildConnectionFlow`. Both approaches also require changing `GroupByRequestKeyStage`'s outlet type to a tuple carrying a `Sink` or callback interface, and modifying `HostKeyMergeBack`.

**Pure backpressure** uses Akka's own flow control as the capacity signal:

```
Http20ConnectionStage at MaxConcurrentStreams
  → stops pulling from inlet
    → Source.Queue fills
      → OfferAsync pending → Offering = true → HasCapacity = false
        → next request → new slot or least-loaded slot
```

With `SlotQueueSize=1`: after exactly 1 item is buffered in the Source.Queue, the next `OfferAsync` waits → `Offering=true` → `HasCapacity=false` → the routing stage immediately opens a new slot (connection). This means the capacity signal propagates after at most 1 queued item, not 64. No shared state, no cross-actor reads, no shape changes to existing stages.

### Why SlotQueueSize must not exceed 1 — pipeline analysis

The stages between `Source.Queue` and `Http20ConnectionStage.InApp` are (in order):

```
Source.Queue → ExtractOptionsStage → Http20StreamIdAllocatorStage → Broadcast(2) → Http20Request2FrameStage → Http20ConnectionStage.InApp
```

All four intermediate stages are **fused** (no async boundary) and hold **no internal buffer**. When `Http20ConnectionStage` stops pulling because `MaxConcurrentStreams` is reached, backpressure propagates synchronously through all of them back to the `Source.Queue`. No request is silently buffered inside any intermediate stage.

This means `Source.Queue` is the **only place** where a request can accumulate per slot. With `SlotQueueSize=1`, at most 1 request is stuck per slot before `Offering=true` fires and the routing stage reacts. A higher `SlotQueueSize` would cause N requests to pile up on a saturated connection invisibly, defeating the purpose of multi-slot routing.

The sole exception is `ExtractOptionsStage` on connection establishment: a single request may be held there while the `ConnectItem` is being forwarded downstream. This is bounded to exactly 1 request and only occurs on the first request per slot.

### Implementation notes

- `SubflowState.Offering = true` already means `OfferAsync` is in flight and the slot cannot accept more work — this is the capacity signal, no new tracking needed
- `SubflowGroup.RemoveDead()` should be called lazily (during `HandlePush` when routing fails, not on every push) to avoid O(n) cleanup on every element
- `_onOfferComplete` callback currently stores `originState` for stale-check — this pattern still works when iterating `group.Slots.Find(s => s == originState)`
- `Flow.LazyFlow` requires `MapMaterializedValue(_ => NotUsed.Instance)` to compile with `IGraph<FlowShape<...>, NotUsed>`
- Test file prefix `NN_` — check existing `.StreamTests` files for next available number

## Success Metrics

- HTTP/2 client with `MaxHttp2ConnectionsPerHost=2` and a server that limits `MaxConcurrentStreams=1` opens 2 separate TCP connections and uses both
- Unused protocol engines do not appear in Akka stage materialization logs
- All 2111+ existing tests pass after the change
- Zero compile-time warnings introduced

## Open Questions

*(none)*
