<!-- maggus-id: 231a4107-a84a-481f-b77e-6afdf3c3012f -->

# Feature 035: Safe Feedback Loop + HTTP/1.0 Keep-Alive Connection Reuse

## Introduction

TurboHttp's current connection lifecycle uses `IConnectionScope` with 3 implementations (`SingleRequestConnectionScope`, `PersistentConnectionScope`, `DeferredConnectionScope`) to bridge the reuse decision from `ConnectionReuseFlowStage` back to `ConnectionStage` via method calls. While this eliminated the deadlock-prone feedback loop from the original architecture (Feature 030), it introduces a side-channel that is invisible to the Akka.Streams graph — reuse decisions flow through method calls instead of graph edges, breaking the "everything is a stream" principle.

Additionally, HTTP/1.0 suffers from severe TCP connection churn: every request creates a new TCP socket because the encoder strips `Connection: Keep-Alive` and the pool always disposes H10 connections. This causes flaky integration tests (5+ minute runs, shutdown timeouts) and would be a production performance problem.

This feature replaces `IConnectionScope` with a **safe graph-level feedback loop** using two new custom stages, and adds **HTTP/1.0 Keep-Alive** support at the encoder and pool level.

### Architecture Context

- **Components replaced:** `IConnectionScope` (interface + 3 implementations), `ConnectionReuseFlowStage`, `TransportSignal`
- **Components added:** `ConnectionReuseFeedbackMergeStage` (cycle-aware merge), `ConnectionReuseFanOutStage` (response + feedback split)
- **Components modified:** `Http10Encoder` (Keep-Alive injection), `ConnectionPool.HostConnections` (H10 idle queue), `ConnectionStage` (takes pool directly), `ProtocolCoreGraphBuilder.BuildConnectionFlow()`
- **Components kept as-is:** `ExtractOptionsStage` (simplified, first ConnectItem only), `ConnectionReuseEvaluator` (already handles H10 Keep-Alive), all Feature BidiStages
- **Key safety invariant:** The feedback loop stays entirely within one fused actor (SubFusingMaterializer guarantee). No async boundaries cross the cycle. The `ConnectionReuseFeedbackMergeStage` handles cycle-aware completion and prevents backpressure deadlocks.

### Why the Old Feedback Loop Failed (3 Root Causes)

1. **Async boundary in cycle** — `Source.Queue` inside `GroupByHostKeyStage` split the cycle across two actors, causing thread races (DL-001, DL-002)
2. **No cycle-aware completion** — `MergePreferred` completed immediately on upstream finish, even with feedback items still in flight (DL-005, DL-008)
3. **Circular backpressure** — Response outlet blocked feedback, feedback blocked encoder, encoder blocked new requests, no new responses could drain the outlet (DL-008, DL-009)

### How This Design Avoids All Three

1. **No async boundary** — All stages in the per-host substream are fused into a single actor by `SubFusingMaterializer`. The feedback loop never crosses an actor boundary.
2. **Cycle-aware completion** — `ConnectionReuseFeedbackMergeStage` tracks in-flight items and only completes when upstream is done AND feedback queue is empty AND in-flight count is zero.
3. **No circular backpressure** — The feedback inlet is always eagerly pulled with an internal queue. Feedback never backpressures against the response outlet.

## Goals

- Replace `IConnectionScope` (interface + 3 implementations) with direct `ConnectionPool` usage in `ConnectionStage`
- Implement a safe graph-level feedback loop for connection reuse decisions
- Add HTTP/1.0 `Connection: Keep-Alive` support (encoder injection + pool idle queue)
- Maintain the linear completion guarantee (no `Broadcast(eagerCancel)` hack)
- Reduce abstraction layers: eliminate 5 files (`IConnectionScope`, `SingleRequestConnectionScope`, `PersistentConnectionScope`, `DeferredConnectionScope`, `TransportSignal`)
- Keep all existing Feature BidiStages (Retry, Cache, Redirect, Cookie, Compression) unchanged
- No public API changes

## Tasks

### TASK-035-001: ConnectionReuseFeedbackMergeStage

**Description:** As a developer, I want a custom merge stage that safely handles a graph-level feedback cycle by prioritizing feedback items, tracking in-flight counts, and only completing when the cycle is fully drained.

**Token Estimate:** ~80k tokens
**Predecessors:** none
**Successors:** TASK-035-006
**Parallel:** yes — can run alongside TASK-035-002, TASK-035-003, TASK-035-004, TASK-035-005
**Model:** opus — cycle-aware completion logic is subtle and critical

#### Design

```csharp
/// <summary>
/// Cycle-aware merge stage for connection reuse feedback loop.
/// Prioritizes feedback (reconnect) items over new requests.
/// Tracks in-flight count to prevent premature completion.
/// Feedback inlet is always eagerly pulled — never backpressures the sender.
/// </summary>
internal sealed class ConnectionReuseFeedbackMergeStage
    : GraphStage<FanInShape<HttpRequestMessage, HttpRequestMessage, HttpRequestMessage>>
{
    // InPrimary  — new requests from upstream (GroupByHostKey substream)
    // InFeedback — reconnect signals from ConnectionReuseFanOutStage
    // Out        — merged output to ExtractOptionsStage/Encoder
}
```

**Stage logic rules:**
- `InFeedback` handler: enqueue to internal `Queue<HttpRequestMessage>`, immediately `Pull(InFeedback)` again (never backpressure)
- `InPrimary` handler: increment `_inFlight`, buffer or push
- `Out` handler (onPull): serve from feedback queue first (priority), then pull `InPrimary`
- `InPrimary` onUpstreamFinish: set `_upstreamDone = true`, call `TryComplete()` — do NOT `CompleteStage()` immediately
- `TryComplete()`: only `CompleteStage()` when `_upstreamDone && _inFlight == 0 && _feedbackQueue.Count == 0`
- `DecrementInFlight()`: called externally by `ConnectionReuseFanOutStage` when a response exits the cycle (regardless of reuse decision). Decrements `_inFlight`, calls `TryComplete()`.

**In-flight counting contract:**
- Incremented: when a request enters via `InPrimary` (new request enters the cycle)
- NOT incremented: when a request enters via `InFeedback` (recycled request, already counted)
- Decremented: when `ConnectionReuseFanOutStage` pushes a response downstream (request has left the cycle)

**Acceptance Criteria:**
- [ ] Stage compiles as `GraphStage<FanInShape<HttpRequestMessage, HttpRequestMessage, HttpRequestMessage>>`
- [ ] Port names: `ConnectionReuseFeedbackMerge.In.Request`, `ConnectionReuseFeedbackMerge.In.Feedback`, `ConnectionReuseFeedbackMerge.Out`
- [ ] Feedback items are served before primary items when both are available
- [ ] `InFeedback` is always pulled after grab — internal queue, no backpressure
- [ ] `_inFlight` incremented only on `InPrimary` push, decremented only via `DecrementInFlight()`
- [ ] Stage does NOT complete when upstream finishes if `_inFlight > 0` or feedback queue non-empty
- [ ] Stage completes when `_upstreamDone && _inFlight == 0 && _feedbackQueue.Count == 0`
- [ ] `PreStart()` pulls both inlets
- [ ] Unit tests: priority ordering, completion with empty feedback, completion with pending feedback, in-flight tracking, upstream finish with items in cycle
- [ ] Stream tests in `src/TurboHttp.StreamTests/Streams/`
- [ ] Build succeeds with zero warnings

---

### TASK-035-002: ConnectionReuseFanOutStage

**Description:** As a developer, I want a fan-out stage that evaluates connection reuse after each response, always pushes the response downstream, and conditionally sends a reconnect signal back to the feedback merge when the connection cannot be reused.

**Token Estimate:** ~60k tokens
**Predecessors:** none
**Successors:** TASK-035-006
**Parallel:** yes — can run alongside TASK-035-001, TASK-035-003, TASK-035-004, TASK-035-005

#### Design

```csharp
/// <summary>
/// Fan-out stage: evaluates connection reuse via ConnectionReuseEvaluator,
/// pushes response to Out.Response (always), and pushes the original request
/// back to Out.Feedback when reconnection is needed (canReuse == false).
/// Also decrements the in-flight counter on FeedbackMergeStage.
/// </summary>
internal sealed class ConnectionReuseFanOutStage
    : GraphStage<FanOutShape<HttpResponseMessage, HttpResponseMessage, HttpRequestMessage>>
{
    // In           — decoded HttpResponseMessage from engine
    // OutResponse  — always: response to downstream (Feature BidiStages)
    // OutFeedback  — only when canReuse == false: original request back into cycle
}
```

**Stage logic rules:**
- `OnPush(In)`: Grab response, evaluate via `ConnectionReuseEvaluator.Evaluate()` (H1.x) or `Http3ConnectionReuseEvaluator` (H3)
- Always: `Push(OutResponse, response)`
- If `!decision.CanReuse && response.RequestMessage is not null`: `Emit(OutFeedback, response.RequestMessage)` — `Emit()` buffers internally, avoids backpressure issues
- Always: call `_mergeStage.DecrementInFlight()` to signal that this request has exited the cycle
- `OutFeedback` demand: managed by `Emit()` internally — stage does not need to track demand manually

**Acceptance Criteria:**
- [ ] Stage compiles as `GraphStage<FanOutShape<HttpResponseMessage, HttpResponseMessage, HttpRequestMessage>>`
- [ ] Port names: `ConnectionReuseFanOut.In`, `ConnectionReuseFanOut.Out.Response`, `ConnectionReuseFanOut.Out.Feedback`
- [ ] Response is always pushed to `OutResponse` regardless of reuse decision
- [ ] Original request is pushed to `OutFeedback` only when `canReuse == false` and `RequestMessage != null`
- [ ] Uses `Emit()` for feedback outlet (internal buffering, no manual demand tracking)
- [ ] Calls `DecrementInFlight()` on merge stage for every response (both reuse and close)
- [ ] Delegates to `ConnectionReuseEvaluator.Evaluate()` for H1.x/H2, `Http3ConnectionReuseEvaluator` for H3
- [ ] Handles upstream finish gracefully (complete stage)
- [ ] Handles downstream cancel on either outlet gracefully
- [ ] Unit tests: reuse response (no feedback), close response (feedback emitted), H10 without Keep-Alive, H10 with Keep-Alive, H11 default, H11 close
- [ ] Stream tests in `src/TurboHttp.StreamTests/Streams/`
- [ ] Build succeeds with zero warnings

---

### TASK-035-003: HTTP/1.0 Keep-Alive Encoder Injection

**Description:** As a developer, I want the HTTP/1.0 encoder to inject `Connection: Keep-Alive` into outbound requests, so that servers that support persistent connections can keep the TCP socket open.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-035-006
**Parallel:** yes — can run alongside TASK-035-001, TASK-035-002, TASK-035-004, TASK-035-005

#### Change

In `Http10Encoder.EnforceHttp10Headers()`:

```csharp
// Before:
headers.Remove("Connection");
headers.Remove("Keep-Alive");

// After:
headers["Connection"] = ["Keep-Alive"];
headers.Remove("Keep-Alive");  // Remove client-supplied Keep-Alive params (server decides)
```

The `ConnectionReuseEvaluator` already handles the response side correctly (lines 87-100 in `ConnectionReuseEvaluator.cs`):
- Server responds with `Connection: Keep-Alive` → returns `KeepAlive` decision
- Server does not respond with Keep-Alive → returns `Close` decision

No changes needed to the evaluator.

**Acceptance Criteria:**
- [ ] `Http10Encoder.EnforceHttp10Headers()` sets `Connection: Keep-Alive` instead of removing it
- [ ] Client-supplied `Keep-Alive` parameters are still removed (server controls timeout/max)
- [ ] Wire format verified: request contains `Connection: Keep-Alive\r\n` header
- [ ] Existing encoder unit tests updated to expect `Connection: Keep-Alive`
- [ ] New unit test: verify Keep-Alive header is present in encoded bytes
- [ ] New unit test: verify client-supplied Keep-Alive params are stripped
- [ ] Existing stream tests (RFC1945 encoder tests) updated and pass
- [ ] Build succeeds with zero warnings

---

### TASK-035-004: ConnectionPool HTTP/1.0 Keep-Alive Support

**Description:** As a developer, I want the connection pool to support idle connection reuse for HTTP/1.0 when the server confirms Keep-Alive, reducing TCP connection churn from 1-per-request to amortized reuse.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** TASK-035-006
**Parallel:** yes — can run alongside TASK-035-001, TASK-035-002, TASK-035-003, TASK-035-005

#### Changes to `ConnectionPool.HostConnections`

**AcquireAsync — H10 checks idle queue:**
```csharp
if (version is { Major: 1, Minor: 0 })
{
    // Try idle queue first (server may have confirmed Keep-Alive on previous response)
    while (_idle.TryDequeue(out var idle))
    {
        if (idle is { IsAlive: true, Reusable: true })
        {
            idle.MarkBusy();
            return idle;
        }
        RemoveLease(idle);
        idle.Dispose();
    }
    // No idle available — create new (no semaphore wait for H10)
    return await EstablishAndTrack(options, ct);
}
```

**Release — H10 respects canReuse:**
```csharp
if (version is { Major: 1, Minor: 0 })
{
    if (canReuse && lease is { IsAlive: true, Reusable: true })
    {
        // Server confirmed Keep-Alive — return to idle queue
        lease.MarkIdle();
        _idle.Enqueue(lease);
        TurboHttpMetrics.ConnectionIdle.Add(1, ...);
        return;
    }
    // No Keep-Alive or dead — dispose as before
    RemoveLease(lease);
    lease.Dispose();
    TurboHttpMetrics.ConnectionActive.Add(-1, ...);
    return;
}
```

**Constructor — Semaphore limit for H10:**
```csharp
// Before: H10 had no limit (int.MaxValue for Major >= 2, 6 for H11, nothing for H10)
// After: H10 gets same 6-per-host limit as H11
var maxConnections = endpoint.Version.Major >= 2 ? int.MaxValue : 6;
```
Note: this already applies to H10 since `Major < 2` for version 1.0. But AcquireAsync for H10 currently bypasses the semaphore entirely (line 124-127). The new code must go through the semaphore when no idle connection is available.

**Acceptance Criteria:**
- [ ] `HostConnections.AcquireAsync` for H10: check idle queue before creating new connection
- [ ] `HostConnections.Release` for H10: enqueue to idle when `canReuse == true` and lease is alive
- [ ] `HostConnections.AcquireAsync` for H10: respect semaphore limit (6 per host) when no idle available
- [ ] `EvictIdle` correctly handles H10 idle connections (same as H11 — already generic)
- [ ] Metrics: `ConnectionIdle` incremented on H10 Keep-Alive return, decremented on eviction
- [ ] Fallback: when server does NOT confirm Keep-Alive, behavior is identical to current (dispose immediately)
- [ ] Unit tests: H10 acquire-new, H10 acquire-idle-reuse, H10 release-keep-alive, H10 release-close, H10 semaphore limit, H10 idle-eviction
- [ ] Build succeeds with zero warnings

---

### TASK-035-005: Remove IConnectionScope + Simplify ConnectionStage

**Description:** As a developer, I want to remove the `IConnectionScope` abstraction layer entirely and have `ConnectionStage` interact with `ConnectionPool` directly, reducing indirection and aligning with the new feedback loop architecture where reuse decisions flow through graph edges, not method calls.

**Token Estimate:** ~80k tokens
**Predecessors:** none
**Successors:** TASK-035-006
**Parallel:** yes — can run alongside TASK-035-001, TASK-035-002, TASK-035-003, TASK-035-004
**Model:** opus — removing a core abstraction requires careful impact analysis

#### Files Deleted

| File | Lines | Reason |
|------|-------|--------|
| `Transport/ConnectionScope/IConnectionScope.cs` | ~55 | Interface no longer needed |
| `Transport/ConnectionScope/SingleRequestConnectionScope.cs` | ~109 | Pool handles H10 directly |
| `Transport/ConnectionScope/PersistentConnectionScope.cs` | ~137 | Pool handles H11+ directly |
| `Transport/ConnectionScope/DeferredConnectionScope.cs` | ~??? | Lazy init moves into ConnectionStage |
| `Streams/Stages/Features/ConnectionReuseFlowStage.cs` | ~125 | Replaced by ConnectionReuseFanOutStage |

#### ConnectionStage Changes

```csharp
// Before:
internal sealed class ConnectionStage : GraphStage<FlowShape<IOutputItem, IInputItem>>
{
    public ConnectionStage(IConnectionScope scope) { ... }
}

// After:
internal sealed class ConnectionStage : GraphStage<FlowShape<IOutputItem, IInputItem>>
{
    public ConnectionStage(ConnectionPool pool) { ... }
}
```

**ConnectionStage.Logic changes:**
- `_pool` field instead of `_scope`
- `_lease` field (nullable) — current active `ConnectionLease`
- `_options` field (nullable) — stored from first `ConnectItem`
- `_endpoint` field (nullable) — stored from first `ConnectItem`
- On `ConnectItem`: store options + endpoint, acquire from pool, start transport
- On `DataItem` with `_handle == null`: auto-reconnect via `_pool.AcquireAsync(_options, _endpoint)`
- On upstream finish / downstream cancel: `_pool.Release(_lease, canReuse: false)` + dispose
- `RegisterTransportCallback` / `TransportSignal` — removed entirely (no callback needed, reuse flows through graph)

**TcpTransportHandler changes:**
- Constructor takes `ConnectionPool` instead of `IConnectionScope`
- `OnTransportReturned` callback removed — no longer needed
- Cleanup (stop pump, clear handle, gen++) happens when `ConnectionReuseFanOutStage` sends the request back through the feedback loop and `ConnectionStage` acquires a new connection

**Key insight:** In the feedback loop model, `ConnectionStage` does NOT need to know the reuse decision. The decision manifests as:
- **CanReuse = true** → no feedback, next request reuses existing handle (handle is still alive)
- **CanReuse = false** → feedback sends request back, ConnectionStage sees `_handle == null` on next DataItem, auto-reconnects

The reuse decision is **encoded in the graph topology**, not in a method call.

**Acceptance Criteria:**
- [ ] `IConnectionScope.cs` deleted
- [ ] `SingleRequestConnectionScope.cs` deleted
- [ ] `PersistentConnectionScope.cs` deleted
- [ ] `DeferredConnectionScope.cs` deleted
- [ ] `ConnectionReuseFlowStage.cs` deleted
- [ ] `TransportSignal` class deleted (if separate file)
- [ ] `ConnectionStage` constructor takes `ConnectionPool` directly
- [ ] `ConnectionStage` stores `_lease`, `_options`, `_endpoint` as private fields
- [ ] Auto-reconnect: `DataItem` with `_handle == null` acquires via `_pool.AcquireAsync()`
- [ ] `TcpTransportHandler` constructor takes `ConnectionPool` instead of `IConnectionScope`
- [ ] No `RegisterTransportCallback` or `OnTransportReturned` anywhere
- [ ] `_connectionGen` preserved for stale pump detection
- [ ] All references to `IConnectionScope` removed from codebase (grep verification)
- [ ] Existing ConnectionStage unit/stream tests updated and pass
- [ ] Build succeeds with zero warnings

---

### TASK-035-006: BuildConnectionFlow Feedback Loop Topology

**Description:** As a developer, I want to rewrite `BuildConnectionFlow()` to use the safe feedback loop topology with `ConnectionReuseFeedbackMergeStage` + `ConnectionReuseFanOutStage`, replacing the current linear topology with callback side-channel.

**Token Estimate:** ~100k tokens
**Predecessors:** TASK-035-001, TASK-035-002, TASK-035-003, TASK-035-004, TASK-035-005
**Successors:** TASK-035-007
**Parallel:** no — requires all new stages and pool changes
**Model:** opus — graph wiring with cycle, completion semantics critical

#### Topology

```
┌─── Per-host substream (ONE fused actor, no async boundary) ──────────────────┐
│                                                                               │
│  FeedbackMerge ──→ ExtractOptions ──→ Engine.encode ──→ Merge ──→ ConnStage  │
│    ↑ InRequest        (simplified)                   ↑ Preferred             │
│    │ InFeedback                           OutSignal ─┘                       │
│    │                                                         │               │
│    │                                                         ▼               │
│    │                                                   Engine.decode         │
│    │                                                         │               │
│    │                                                         ▼               │
│    │                                              ConnectionReuseFanOut      │
│    │                                               ╱               ╲         │
│    │                                        OutFeedback      OutResponse     │
│    │                                          │                    │         │
│    └──────────────────────────────────────────┘                    ▼         │
│                                                                  Out        │
│  In (new requests from GroupByHostKey)                                       │
└───────────────────────────────────────────────────────────────────────────────┘
```

#### Implementation

```csharp
private static IGraph<FlowShape<HttpRequestMessage, HttpResponseMessage>, NotUsed>
    BuildConnectionFlow<TEngine>(ConnectionPool pool, TurboClientOptions clientOptions)
    where TEngine : IHttpProtocolEngine, new()
{
    return GraphDsl.Create(b =>
    {
        // Stages
        var feedbackMerge = b.Add(new ConnectionReuseFeedbackMergeStage());
        var extract = b.Add(new ExtractOptionsStage(clientOptions));
        var bidi = b.Add(new TEngine().CreateFlow());
        var transportMerge = b.Add(new MergePreferred<IOutputItem>(1));
        var transport = b.Add(new ConnectionStage(pool));
        var reuseFanOut = b.Add(new ConnectionReuseFanOutStage(feedbackMerge));

        // Request path: merge → extract → encode
        b.From(feedbackMerge.Out).To(extract.In);
        b.From(extract.Out0).To(bidi.Inlet1);          // request → encode
        b.From(extract.Out1).To(transportMerge.Preferred); // ConnectItem (first only)

        // Transport path: encoded → merge → connection → decode
        b.From(bidi.Outlet1).To(transportMerge.In(0));
        b.From(transportMerge.Out).To(transport.Inlet);
        b.From(transport.Outlet).To(bidi.Inlet2);

        // Response + feedback: decode → fan-out → response + feedback loop
        b.From(bidi.Outlet2).To(reuseFanOut.In);
        b.From(reuseFanOut.OutFeedback).To(feedbackMerge.InFeedback);  // THE LOOP

        return new FlowShape<HttpRequestMessage, HttpResponseMessage>(
            feedbackMerge.InPrimary, reuseFanOut.OutResponse);
    });
}
```

#### Completion Flow (Safe Cycle)

1. Request source finishes → `FeedbackMerge.InPrimary` upstream finish → `_upstreamDone = true`
2. `FeedbackMerge.TryComplete()`: checks `_inFlight == 0 && _feedbackQueue.Count == 0`
3. If items still in cycle: waits (does NOT complete)
4. Last response reaches `ConnectionReuseFanOutStage` → `DecrementInFlight()` → `_inFlight = 0`
5. If no feedback (Keep-Alive): `TryComplete()` fires → `CompleteStage()`
6. If feedback (reconnect): feedback enqueued → served → re-enters cycle → eventually exits
7. When `_inFlight == 0 && _feedbackQueue.Count == 0 && _upstreamDone`: stage completes
8. Linear completion propagates: FeedbackMerge → ExtractOptions → Encode → Transport → Decode → FanOut → done

No `Broadcast(eagerCancel)`. No zombie actors. Cycle drains naturally.

#### BuildProtocolFlow Changes

```csharp
// Before: scope created per-substream
var scope = new DeferredConnectionScope(version, pool);
return src.Via(Flow.FromGraph(BuildConnectionFlow<TEngine>(scope, options)));

// After: pool passed directly
return src.Via(Flow.FromGraph(BuildConnectionFlow<TEngine>(pool, options)));
```

**Acceptance Criteria:**
- [ ] `BuildConnectionFlow()` rewritten with feedback loop topology
- [ ] `ConnectionReuseFeedbackMergeStage` wired as cycle entry point
- [ ] `ConnectionReuseFanOutStage.OutFeedback` connected to `FeedbackMerge.InFeedback` (the cycle)
- [ ] `ExtractOptionsStage` kept (simplified, first ConnectItem only)
- [ ] One `MergePreferred<IOutputItem>(1)` for ConnectItem priority
- [ ] `ConnectionStage` takes `ConnectionPool` directly
- [ ] No `IConnectionScope` creation anywhere in `BuildProtocolFlow`
- [ ] No `DeferredConnectionScope` instantiation
- [ ] No `TransportSignal` creation
- [ ] No `Broadcast(eagerCancel)` or `Buffer(1)` on feedback path
- [ ] Test mode path updated (when `transportFactory` is provided)
- [ ] DEBUG watchdog paths updated
- [ ] All existing unit tests updated and pass
- [ ] All existing stream tests updated and pass
- [ ] All H10 integration tests pass
- [ ] All H11 integration tests pass
- [ ] Build succeeds with zero warnings

---

### TASK-035-007: Full Integration Verification + Cleanup

**Description:** As a developer, I want to verify the entire test suite passes with the new feedback loop architecture, confirm HTTP/1.0 Keep-Alive works end-to-end, clean up obsolete code, and update documentation.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-035-006
**Successors:** none
**Parallel:** no — final verification

**Acceptance Criteria:**
- [ ] All H10 integration tests pass (0 failures, 3 consecutive runs)
- [ ] All H11 integration tests pass (0 failures)
- [ ] All H2 integration tests pass (0 failures)
- [ ] All unit tests pass (TurboHttp.Tests)
- [ ] All stream tests pass (TurboHttp.StreamTests)
- [ ] H10 integration test run time reduced (fewer shutdown timeouts due to Keep-Alive reuse)
- [ ] New integration test: H10 Keep-Alive — verify connection reuse when server supports it
- [ ] New integration test: H10 without Keep-Alive — verify fallback to connection-per-request
- [ ] Stress test: 100 sequential H10 requests to Keep-Alive-capable server — verify TCP socket reuse via metrics
- [ ] `ConnectionScope/` directory deleted (all 4 files)
- [ ] `ConnectionReuseFlowStage.cs` deleted
- [ ] `TransportSignal` removed
- [ ] Grep verification: zero references to `IConnectionScope` in codebase
- [ ] Grep verification: zero references to `TransportSignal` in codebase
- [ ] Port naming validation passes (stage-port-validator agent)
- [ ] DisplayName validation passes (displayname-validator agent)
- [ ] Update Obsidian vault: `Architecture/Layers/14-TRANSPORT_LAYER` — pool Keep-Alive, no more scopes
- [ ] Update Obsidian vault: `Architecture/Layers/15-STREAMS_LAYER` — feedback loop topology diagram
- [ ] Update Obsidian vault: `Architecture/Design/02-STAGE_PATTERNS` — FeedbackMerge pattern documented
- [ ] No new compiler warnings
- [ ] Performance: H10 benchmark shows improvement (fewer TCP handshakes with Keep-Alive)

## Task Dependency Graph

```
TASK-035-001 (FeedbackMergeStage) ──────────┐
TASK-035-002 (FanOutStage) ─────────────────┤
TASK-035-003 (Encoder Keep-Alive) ──────────┼──→ TASK-035-006 (BuildConnectionFlow) ──→ TASK-035-007 (Verification)
TASK-035-004 (Pool H10 Keep-Alive) ─────────┤
TASK-035-005 (Remove IConnectionScope) ─────┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-035-001 | ~80k | none | yes (with 002-005) | opus |
| TASK-035-002 | ~60k | none | yes (with 001,003-005) | -- |
| TASK-035-003 | ~25k | none | yes (with 001-002,004-005) | -- |
| TASK-035-004 | ~40k | none | yes (with 001-003,005) | -- |
| TASK-035-005 | ~80k | none | yes (with 001-004) | opus |
| TASK-035-006 | ~100k | 001-005 | no | opus |
| TASK-035-007 | ~50k | 006 | no | -- |

**Total estimated tokens:** ~435k

**Critical path:** max(001, 002, 003, 004, 005) → 006 → 007
**Optimal parallel execution:** 001+002+003+004+005 in parallel → 006 → 007

## Functional Requirements

- FR-1: `ConnectionReuseFeedbackMergeStage` must prioritize feedback items over primary items when both are available
- FR-2: `ConnectionReuseFeedbackMergeStage` must NOT complete when upstream finishes if in-flight count > 0 or feedback queue is non-empty
- FR-3: `ConnectionReuseFeedbackMergeStage.InFeedback` must always be pulled after grab — internal queue, never backpressure the feedback sender
- FR-4: `ConnectionReuseFanOutStage` must always push the response to `OutResponse` regardless of reuse decision
- FR-5: `ConnectionReuseFanOutStage` must push the original `RequestMessage` to `OutFeedback` only when `canReuse == false` and `RequestMessage != null`
- FR-6: `ConnectionReuseFanOutStage` must call `DecrementInFlight()` on the merge stage for every processed response
- FR-7: `Http10Encoder` must inject `Connection: Keep-Alive` header into all HTTP/1.0 requests
- FR-8: `ConnectionPool.HostConnections` must check idle queue for HTTP/1.0 before creating a new connection
- FR-9: `ConnectionPool.HostConnections` must enqueue HTTP/1.0 connections to idle queue when `canReuse == true`
- FR-10: `ConnectionPool.HostConnections` must enforce semaphore limit (6 per host) for HTTP/1.0
- FR-11: `ConnectionStage` must take `ConnectionPool` directly — no `IConnectionScope` indirection
- FR-12: `ConnectionStage` must auto-reconnect when `DataItem` arrives with `_handle == null`
- FR-13: The feedback loop must be entirely within one fused actor (no async boundary crossing)
- FR-14: `BuildConnectionFlow()` graph must contain exactly one cycle (feedback merge ↔ fan-out)
- FR-15: No `Broadcast(eagerCancel)`, no `Buffer(1)` on feedback path, no `TransportSignal`
- FR-16: All feature BidiStages (Retry, Cache, Redirect, Cookie, Compression) must remain unchanged
- FR-17: No public API changes (`ITurboHttpClient`, `TurboClientOptions`, `ITurboHttpClientFactory`)
- FR-18: When server does NOT confirm Keep-Alive for HTTP/1.0, behavior must be identical to current (dispose connection immediately)
- FR-19: `ConnectionReuseEvaluator` must NOT be modified — it already handles H10 Keep-Alive correctly

## Non-Goals

- No changes to Feature BidiStage stack (Retry, Cache, Redirect, Cookie, Compression)
- No changes to GroupByHostKey substreaming model
- No connection prewarming or DNS refresh
- No HTTP/3 QUIC changes (QuicTransportHandler manages its own lifecycle)
- No generic/reusable FeedbackMergeStage — specific to connection reuse
- No changes to `ConnectionReuseEvaluator` (already handles H10 Keep-Alive)
- No Keep-Alive parameter negotiation (timeout/max) — server decides, pool uses standard idle timeout

## Technical Considerations

- **Fused substream guarantee:** `SubFusingMaterializer` fuses all stages per substream into one actor. The feedback loop never crosses an actor boundary — all Push/Pull/Emit calls are synchronous within the actor mailbox.
- **In-flight tracking safety:** `_inFlight` is only modified within the fused actor (increment in FeedbackMerge, decrement via callback from FanOut). No locks needed. Single-threaded execution guaranteed by Akka.
- **Emit() for feedback:** `Emit()` is an Akka.Streams built-in that internally buffers and re-emits when downstream pulls. This decouples the FanOut's push from the FeedbackMerge's pull — no manual demand tracking needed.
- **DecrementInFlight communication:** The `ConnectionReuseFanOutStage` needs a reference to `ConnectionReuseFeedbackMergeStage` (or its Logic). Since both are created in `BuildConnectionFlow()` and fused into the same actor, the FanOut can hold a reference to the merge stage and call a method on it. Within the fused actor, this is a direct method call — no message passing.
- **ConnectItem on reconnect:** When a reconnect feedback request flows through `ExtractOptionsStage` for the second time, the stage has already emitted its one ConnectItem (`_connectItemSent == true`). The request passes through without generating a new ConnectItem. `ConnectionStage` handles reconnection via auto-reconnect (`_handle == null` → `pool.AcquireAsync()`). This is correct.
- **H10 Keep-Alive server compatibility:** Most modern HTTP/1.0-speaking servers (proxies, legacy APIs, embedded devices) support `Connection: Keep-Alive`. When they don't, the evaluator returns `Close`, FanOut sends feedback, and ConnectionStage auto-reconnects — identical to current behavior but through graph edges instead of method calls.
- **Pool semaphore for H10:** Currently H10 bypasses the semaphore (line 124-127 in ConnectionPool). With Keep-Alive and idle reuse, H10 must participate in semaphore limiting to prevent connection storms. The 6-per-host limit (RFC 9112 section 9.4) applies equally.
- **_connectionGen preserved:** Still needed in `TcpTransportHandler` for stale inbound pump detection. Pool-direct ConnectionStage does not change this requirement.
- **TreatWarningsAsErrors:** Enabled globally — all code must compile with zero warnings.

## Success Metrics

- 0 HTTP/1.0 integration test failures across 3 consecutive runs
- H10 integration test run time reduced by 30%+ (fewer TCP handshakes with Keep-Alive)
- `IConnectionScope` and all 3 implementations deleted (5 files, ~400+ lines removed)
- `TransportSignal` deleted
- `ConnectionReuseFlowStage` replaced by `ConnectionReuseFanOutStage`
- Feedback loop is visible in graph topology (debuggable via Akka materializer)
- Cycle is safe: no deadlocks in 1000 sequential H10 requests stress test
- Completion is correct: no zombie actors, no shutdown timeout (< 5s drain)
- All H11 and H2 tests remain green (no regressions)

## Open Questions

None — all resolved during design discussion (IConnectionScope removal: direct pool; ExtractOptionsStage: kept simplified; Keep-Alive: full implementation; FeedbackMerge: specific to connection reuse).
