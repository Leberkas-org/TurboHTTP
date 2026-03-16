# Architecture Decision: Engine.cs Transport Layer

**Date:** 2026-03-16
**Status:** PROPOSED — awaiting user decision
**Context:** TASK-DEC-001 (Plan 5 Gap Analysis)

---

## Background

The audit (TASK-AUD-001 through AUD-005) revealed that the current architecture **already integrates the Actor Pool** (PoolRouterActor → HostPoolActor → ConnectionActor) via `ConnectionStage`. The original plan framed Option A as "ConnectionStage direct (no actors)" — but that is not the status quo. The status quo IS the actor pool.

This document reframes the three options based on what the audit actually found.

### Current Architecture (as discovered by audit)

```
Engine.cs
  └─ GroupBy(HostKey) per protocol lane
       └─ BuildConnectionFlowPublic
            ├─ TEngine().CreateFlow() (BidiFlow: encode/decode)
            └─ ConnectionStage(poolRouter)
                 ├─ on-start: Tell(GetGlobalRefs) → receives GlobalRefs(RequestQueue, ResponseSource)
                 ├─ ConnectItem → Tell(EnsureHost) to PoolRouterActor
                 └─ DataItem → OfferAsync to global RequestQueue
                      └─ PoolRouterActor routes by HostKey
                           └─ HostPoolActor routes to ConnectionActor
                                └─ ConnectionActor → ClientManager → TCP
```

### Known Gaps (from audit)

| Gap | Severity | Source |
|-----|----------|--------|
| `ConnectionReuseStage` not wired (HTTP/1.x keep-alive decisions never made) | High | AUD-001, AUD-003 |
| `ConnectionFailed` never sent by ConnectionActor → HostPoolActor.HandleFailure is dead code | High | AUD-004 |
| No backoff on reconnect (immediate retry loop on server down) | Medium | AUD-004 |
| In-flight requests silently lost on TCP drop | Medium | AUD-004 |
| Stale queue entry during reconnect window | Medium | AUD-004 |
| `MaxReconnectAttempts` / `ReconnectInterval` are dead config | Low | AUD-004 |
| `PerHostConnectionLimiter` not integrated | Low | AUD-005 |
| No end-to-end integration tests | High | AUD-005 |

---

## Option A: Evolve Current Architecture (Fix Gaps Incrementally)

**Summary:** Keep the existing PoolRouterActor → HostPoolActor → ConnectionActor hierarchy and ConnectionStage. Fix the known gaps one by one.

### What changes

1. **Wire `ConnectionReuseStage`** into `BuildConnectionFlowPublic` (after BidiFlow decode, before response output). Feed decisions back to HostPoolActor via `MarkConnectionNoReuse` message.
2. **Fix `ConnectionActor.Reconnect()`**: Send `ConnectionFailed` to parent before reconnecting. Add exponential backoff using `PoolConfig.ReconnectInterval` as base.
3. **Fix stale queue entry**: Remove `_connectionQueues[conn.Actor]` entry in `HostPoolActor` when receiving `ConnectionFailed`, re-queue pending items.
4. **Wire `PerHostConnectionLimiter`**: Gate `SpawnConnection()` in HostPoolActor against `MaxConnectionsPerHost`.
5. **Add integration tests**: Exercise `SendAsync()` against Kestrel fixtures.

### Evaluation

| Dimension | Score | Notes |
|-----------|-------|-------|
| **Effort** | **Low** (S-M per fix) | Each gap is an isolated, testable change. No structural refactoring. |
| **Connection Reuse (HTTP/1.x)** | ✅ After fix #1 | `ConnectionReuseStage` + `MarkConnectionNoReuse` + HostPoolActor idle eviction |
| **Error Tolerance** | ✅ After fixes #2-4 | Backoff, failure notification, queue cleanup, in-flight retry |
| **HTTP/2 Multiplex** | ✅ Already works | `SelectConnectionWithQueue` returns first active H2 connection; stream-level mux inside Http20Engine |
| **Risk** | **Low** | All changes are additive; existing tests remain valid |

### Pros

- **Lowest effort** — incremental fixes, no structural changes
- **All existing tests remain green** — no breaking changes
- **Actor supervision is already built** — HostPoolActor watches ConnectionActors, idle eviction timer runs
- **MergeHub isolation already works** — one connection drop doesn't affect others (proven by audit)
- **Production-proven pattern** — the actor hierarchy already handles the happy path correctly

### Cons

- **Actor messages in the hot data path** — every DataItem flows: ConnectionStage → OfferAsync → PoolRouterActor mailbox → Tell → HostPoolActor mailbox → Tell → ConnectionActor mailbox → TCP write. This is ~3 actor hops per request.
- **PoolRouterActor is a single-threaded bottleneck** — all DataItems from all hosts pass through one actor's mailbox for routing. Under high concurrency, this serializes request dispatch.
- **Mixed paradigms** — Akka.Streams for the pipeline + Actor messages for the transport bridge adds conceptual complexity.

---

## Option B: Strip Actor Pool — Direct TCP in ConnectionStage

**Summary:** Remove the 3-level actor hierarchy (PoolRouterActor, HostPoolActor, ConnectionActor). Have `ConnectionStage` manage TCP connections directly per GroupBy substream.

### What changes

1. **Delete** PoolRouterActor, HostPoolActor, ConnectionActor.
2. **Rewrite ConnectionStage** to create `ClientManager.CreateTcpRunner` directly. Each GroupBy substream gets its own ConnectionStage instance which owns exactly one TCP connection.
3. **Connection reuse** handled by `ConnectionReuseStage` wired after the decoder — when the response says `Connection: close`, the stage signals ConnectionStage to reconnect.
4. **Connection pooling** for HTTP/1.x: use `GroupBy(HostKey)` with `maxSubstreams` as the pool size limit. Each substream IS a connection.
5. **HTTP/2 multiplex**: works naturally — one substream per host, Http20Engine handles stream-level multiplexing internally.

### Evaluation

| Dimension | Score | Notes |
|-----------|-------|-------|
| **Effort** | **High** (L) | Major rewrite of ConnectionStage + delete 3 actors + rewrite all integration test infrastructure |
| **Connection Reuse (HTTP/1.x)** | ⚠️ Limited | One TCP connection per GroupBy substream. No pool — just "reconnect on close". No idle spare connections. |
| **Error Tolerance** | ⚠️ Worse | No supervision tree. ConnectionStage crash = substream dead. No automatic reconnect across substreams. Must implement reconnect logic inside the GraphStage (complex). |
| **HTTP/2 Multiplex** | ✅ Same | GroupBy gives one substream per host; Http20Engine handles stream mux |
| **Risk** | **High** | Throws away proven actor infrastructure. Must rewrite reconnect, idle eviction, failure isolation from scratch inside GraphStage logic. |

### Pros

- **Simpler mental model** — pure Akka.Streams, no actor messages in the data path
- **Lower latency per request** — no actor mailbox hops (direct stream backpressure)
- **Fewer moving parts** — no PoolRouterActor, HostPoolActor, ConnectionActor, MergeHub wiring

### Cons

- **Loses connection pooling** — `GroupBy` creates one substream per unique HostKey, not N connections per host. Can't scale to N concurrent connections to the same host.
- **Loses supervision** — actors provide `DeathWatch`, `Terminated` messages, scheduled reconnect. GraphStage has none of this — must re-implement in `PreStart`/`PostStop`/`OnTimer`.
- **Loses idle eviction** — no timer-based eviction of unused connections (actors have `Scheduler.ScheduleTellRepeatedlyCancelable`; GraphStages need `ScheduleRepeatedly` which is less ergonomic).
- **High risk** — deletes ~500 lines of proven code and test infrastructure (11 CA tests, 3 HA tests, 2 PR tests, 2 CS tests, 1 ETE test).
- **Reconnect in GraphStage is hard** — GraphStage logic runs on the stream materializer thread; cannot easily do async TCP connect without `GetAsyncCallback` complexity.

---

## Option C: Hybrid — Actor Pool for Lifecycle, Pure Streams for Data

**Summary:** Keep the actor hierarchy for connection lifecycle management (spawn, supervise, reconnect, idle eviction) but remove actors from the hot data path. Data flows through Akka.Streams directly — ConnectionStage talks to TCP via Channels, not via actor messages.

### What changes

1. **ConnectionStage** receives TCP `Channel<byte>` handles directly (not via PoolRouterActor → DataItem → actor mailbox chain).
2. **HostPoolActor** manages connection lifecycle only: spawn, watch, reconnect with backoff, idle eviction, per-host limits. It does NOT route DataItems.
3. **ConnectionActor** owns the TCP socket and exposes `ChannelWriter<byte>` / `ChannelReader<byte>` directly to the stream graph via a `TaskCompletionSource` handshake.
4. **Remove MergeHub/Source.Queue chains** in PoolRouterActor and HostPoolActor. Replace with direct Channel wiring.
5. **Wire `ConnectionReuseStage`** in `BuildConnectionFlowPublic`.
6. **Wire `PerHostConnectionLimiter`** in HostPoolActor.

### Evaluation

| Dimension | Score | Notes |
|-----------|-------|-------|
| **Effort** | **Medium-High** (M-L) | Restructure ConnectionStage + HostPoolActor data path. Keep actor lifecycle code. |
| **Connection Reuse (HTTP/1.x)** | ✅ After wiring | Same as Option A — `ConnectionReuseStage` + actor lifecycle |
| **Error Tolerance** | ✅ Best | Actor supervision for lifecycle + no data-path actor hops to lose messages |
| **HTTP/2 Multiplex** | ✅ Same | Unchanged |
| **Risk** | **Medium** | Structural change to data flow, but lifecycle code stays. Must redesign ConnectionStage ↔ ConnectionActor protocol. |

### Pros

- **Best of both worlds** — actor supervision for lifecycle, stream backpressure for data
- **No actor hops in hot path** — DataItem goes directly from ConnectionStage to TCP Channel (zero mailbox overhead)
- **Eliminates PoolRouterActor bottleneck** — no single-threaded router for all hosts
- **Retains supervision** — HostPoolActor watches ConnectionActors, handles reconnect with backoff

### Cons

- **Higher effort than Option A** — must redesign the ConnectionStage ↔ actor handshake protocol
- **New protocol needed** — how does ConnectionStage get a Channel handle from ConnectionActor? (TaskCompletionSource? StageActor message? GetAsyncCallback?) This is non-trivial.
- **Testing complexity** — must design a new StubRouter pattern for stream tests
- **Premature optimisation risk** — the PoolRouterActor bottleneck is theoretical; no benchmark proves it's a problem today

---

## Comparison Matrix

| Dimension | Option A (Fix Gaps) | Option B (Strip Actors) | Option C (Hybrid) |
|-----------|:-------------------:|:-----------------------:|:-----------------:|
| **Effort** | **Low** ✅ | High ❌ | Medium-High ⚠️ |
| **HTTP/1.x Keep-Alive** | ✅ (after wiring) | ⚠️ (limited) | ✅ (after wiring) |
| **HTTP/2 Multiplex** | ✅ | ✅ | ✅ |
| **Error Tolerance** | ✅ (after fixes) | ⚠️ (must reimplement) | ✅ (best) |
| **Connection Pooling** | ✅ (N conn/host) | ❌ (1 conn/host) | ✅ (N conn/host) |
| **Idle Eviction** | ✅ (exists) | ❌ (must build) | ✅ (exists) |
| **Supervision** | ✅ (actors) | ❌ (none) | ✅ (actors) |
| **Hot-Path Latency** | ⚠️ (3 actor hops) | ✅ (direct) | ✅ (direct) |
| **Risk** | **Low** ✅ | High ❌ | Medium ⚠️ |
| **Existing Tests** | ✅ All green | ❌ Must rewrite | ⚠️ Some rewrite |
| **Code Deleted** | None | ~500 lines + 19 tests | ~100 lines |

---

## Recommendation

**Option A: Evolve Current Architecture** is the recommended choice.

### Justification

1. **The actor pool already works.** The audit proved that the full hierarchy (PoolRouterActor → HostPoolActor → ConnectionActor → TCP) is integrated and functional. The MergeHub isolation prevents cross-connection failure propagation. HTTP/2 multiplexing works at the stream layer. The gaps are well-understood and individually fixable.

2. **The gaps are small and isolated.** Each gap (backoff, ConnectionFailed, ConnectionReuseStage wiring, PerHostConnectionLimiter) is an S or M-sized task with clear acceptance criteria and no cross-cutting dependencies.

3. **The actor mailbox "bottleneck" is theoretical.** No benchmark has shown that PoolRouterActor's mailbox is a throughput limiter. Akka actor mailboxes process millions of messages per second on a single thread. The real bottleneck is TCP I/O, not message routing. If this becomes a measured problem, Option C can be pursued later — the refactoring is backward-compatible.

4. **Option B destroys proven infrastructure for marginal gain.** Removing the actor pool loses supervision, idle eviction, per-host connection pooling, and 19 passing tests. Reimplementing these in GraphStage logic would be more complex and error-prone than the current actor-based approach.

5. **Option C is premature optimisation.** It adds structural complexity (new handshake protocol, new test patterns) to solve a problem that hasn't been measured. The right time for Option C is after Option A is complete AND benchmarks show the actor mailbox is the bottleneck.

### Recommended Implementation Order (Option A)

| Priority | Task | Size | Dependencies |
|----------|------|------|--------------|
| 1 | Wire `ConnectionReuseStage` into `BuildConnectionFlowPublic` | S | None |
| 2 | Fix `ConnectionActor.Reconnect()` — send `ConnectionFailed`, add backoff | M | None |
| 3 | Fix stale queue cleanup in `HostPoolActor` on `ConnectionFailed` | S | Task 2 |
| 4 | Wire `PerHostConnectionLimiter` in `HostPoolActor.SpawnConnection()` | S | None |
| 5 | Write integration tests against Kestrel fixtures | M | Tasks 1-4 |
| 6 | Update CLAUDE.md "Current Limitations" section | S | Tasks 1-5 |

**Estimated total effort: 2-3 days of focused work.**

---

## Decision

**⏳ AWAITING USER INPUT**

Please review the three options and confirm or override the recommendation. The chosen option will be used to generate the gap list (TASK-GAP-001) and roadmap (TASK-ROAD-001).
