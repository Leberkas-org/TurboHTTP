# Plan 5b: Protocol-Aware Connection Pool — HTTP/2 MaxConcurrentStreams & HTTP/1.x Multi-Connection

## Introduction

`HostPoolActor` currently maintains a single `_activeHandle` per host and hands it out to every
requester immediately. This ignores two critical correctness constraints:

1. **HTTP/2** — The server signals `SETTINGS_MAX_CONCURRENT_STREAMS` (RFC 9113 §6.5.2). Exceeding
   it causes RST_STREAM or GOAWAY. The pool must track in-flight streams per connection and either
   apply backpressure or open a second connection when at capacity.
2. **HTTP/1.x** — Multiple concurrent requests to the same host each need their own connection slot
   (HTTP/1.0: exactly 1; HTTP/1.1: pipelined but bounded by depth).

Both protocols are already isolated by `HostKey` (which includes `Version`), so HTTP/1.x and HTTP/2
land in separate `HostPoolActor` instances and never interfere.

**Primary allowed files:** `ConnectionStage.cs`, `PoolRouterActor.cs`, `HostPoolActor.cs`,
`ConnectionActor.cs`

**Protocol engine files (in scope):** `Http20Engine.cs`, `Http11Engine.cs`, `Http10Engine.cs`

**Protocol stage files (in scope):** `Http20ConnectionStage.cs`, `Http20EncoderStage.cs`,
`Http20DecoderStage.cs`, `Http11EncoderStage.cs`, `Http11DecoderStage.cs`,
`Http1XCorrelationStage.cs`, `Http10EncoderStage.cs`, `Http10DecoderStage.cs`

**Supporting files** (IO domain, in scope): `ConnectionHandle.cs`, `ConnectionState.cs`

**New types:** `MaxConcurrentStreamsItem.cs`, `StreamAcquireItem.cs` (IO.Stages domain)

---

## Architecture Context

The Akka.Streams graph per protocol version (from `Engine.BuildConnectionFlowPublic`):

```
ExtractOptionsStage ──► Concat ──► MergePreferred.In(0) ──► ConnectionStage ──► Decoders
                            ▲            ▲
                     BidiFlow.Outlet1    │  MergePreferred.Preferred
                                         │
                              ConnectionReuseStage.Out1
```

Two guarantees that the plan exploits:

- **`MergePreferred` ordering**: feedback items (`ConnectionReuseItem`, signals) arrive at
  `ConnectionStage.inlet` *before* the next request's `DataItem`s.
- **`Http20ConnectionStage` sees all frames**: it processes both inbound server frames and outbound
  client frames, making it the ideal place to observe stream lifecycle events.

---

## Key Design Decision: Stateless ConnectionStage

**Principle:** `ConnectionStage` is a **pure byte pipe** with zero protocol knowledge.

- It holds a `ConnectionHandle` for I/O (write bytes, read bytes) — this is configuration, not protocol state.
- It does **NOT** count streams, check HTTP versions, or track `MaxConcurrentStreams`.
- All `IControlItem`s received from the engine are forwarded to the actor hierarchy untouched.
- Backpressure for HTTP/2 is managed entirely in `Http20ConnectionStage` (which already sees
  HEADERS/END_STREAM/RST_STREAM and is the natural owner of stream lifecycle).
- Connection re-acquisition is channel-driven: when the inbound channel completes (server closed
  TCP), `ConnectionStage` clears `_handle` and sends a new `EnsureHost`. This is protocol-agnostic.

**Why:** HTTP/2 connections are long-lived and shared across streams. If `ConnectionStage` managed
stream counts, it would need protocol-specific branching (HTTP/1.x: re-acquire handle after each
response; HTTP/2: keep handle, count streams). Moving backpressure into `Http20ConnectionStage`
eliminates this complexity and keeps each layer focused:

```
Http20ConnectionStage  →  protocol semantics (stream count, backpressure, flow control)
ConnectionStage        →  I/O (write bytes, read bytes, forward signals)
HostPoolActor          →  lifecycle (spawn, limit, evict, reconnect, route handles)
```

---

## Fanout Design

### BidiFlow Shape Boundary Constraint

A `BidiShape<In1, Out1, In2, Out2>` has exactly 4 external ports. A signal outlet added to
`Http20ConnectionStage` cannot escape `Engine.BuildConnectionFlowPublic` when the stage is nested
inside `IHttpProtocolEngine.CreateFlow()` — the engine's `BidiFlow` only exposes `Outlet1`
(the `IOutputItem` stream). Neither `Engine.cs` nor `IHttpProtocolEngine` can be changed.

### Solution: Merge Signal Inside the Engine BidiFlow

**The key insight**: if `Http20EncoderStage` outputs `IOutputItem` directly (instead of
`(IMemoryOwner<byte>, int)`), and `Http20DecoderStage` accepts `IInputItem` directly, then the
internal `flowOut`/`flowIn` adapters in `Http20Engine.CreateFlow()` are eliminated. The signal
from `Http20ConnectionStage.OutletSignal` is then merged into the `IOutputItem` stream *inside*
the engine's own `GraphDsl.Create()` block using `MergePreferred<IOutputItem>`.

Since `IControlItem : IOutputItem`, control items flow legally through the `IOutputItem` stream
that is `BidiFlow.Outlet1`. They reach `ConnectionStage.HandlePush` through the same path as
`DataItem` — the existing feedback merge in `Engine.BuildConnectionFlowPublic` is not changed.

```
Inside Http20Engine.CreateFlow():

Http20EncoderStage (IOutputItem) ──► MergePreferred<IOutputItem>.In(0) ──► BidiFlow.Outlet1
Http20ConnectionStage.OutletSignal ──► MergePreferred<IOutputItem>.Preferred
```

The `BidiFlow`'s external signature remains:
`BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage>` — unchanged.
No changes to `Engine.cs` or `IHttpProtocolEngine`.

### HTTP/2 Signal Outlet (`Http20ConnectionStage`)

`Http20ConnectionStage` changes from `BidiShape<H2, H2, H2, H2>` to a custom
`GraphStage<Http20ConnectionShape>` with **5 ports**:

```
           ┌─────────────────────────────┐
InletRaw   │  ══► (process server frames)│
           │       ├─► OutletStream      │  (frames to decoder stages)
           │       └─► OutletSignal ─────┼──► MaxConcurrentStreamsItem (SETTINGS)
           │                             │
InletRequest ════► (process app frames)  │
           │       ├─► OutletRaw         │  (frames to encoder → IOutputItem stream)
           │       └─► OutletSignal ─────┼──► StreamAcquireItem (per HEADERS frame)
           └─────────────────────────────┘
```

| Item | Trigger | Carries |
|------|---------|---------|
| `MaxConcurrentStreamsItem` | Inbound SETTINGS with `MAX_CONCURRENT_STREAMS` | `int MaxStreams` |
| `StreamAcquireItem` | Outbound `HeadersFrame` on `_inletRequest` | _(no payload)_ |

### HTTP/2 Backpressure: Owned by `Http20ConnectionStage`

`Http20ConnectionStage` is the natural owner of HTTP/2 stream-level backpressure because it
already processes both inbound server frames and outbound client frames. The backpressure
mechanism:

1. **Track active streams**: increment on outbound `HeadersFrame`, decrement on inbound
   `END_STREAM` flag or `RstStreamFrame`.
2. **Gate `_inletRequest`**: when `_activeStreams >= _maxConcurrentStreams`, stop pulling
   `_inletRequest`. Backpressure propagates upstream through `Request2FrameStage` →
   `StreamIdAllocatorStage` → `BidiFlow.Inlet1` → `ExtractOptionsStage` → source.
3. **Resume on stream close**: when `_activeStreams` drops below `_maxConcurrentStreams`, resume
   pulling `_inletRequest`.
4. **Update limit on SETTINGS**: when server sends `MAX_CONCURRENT_STREAMS`, update
   `_maxConcurrentStreams` and re-evaluate pull state.

This keeps all HTTP/2 semantics in the HTTP/2-specific stage. `ConnectionStage` sees only bytes
and forwarded signals.

### HTTP/1.1 Signal Outlet (`Http1XCorrelationStage`)

`Http1XCorrelationStage` is the natural HTTP/1.x fanout point — it sees every inbound request
and owns the `_pending` queue of in-flight requests. Adding a 4th port `OutletSignal:
Outlet<IControlItem>` that emits `StreamAcquireItem` on each request push mirrors the HTTP/2
pattern. The same `MergePreferred<IOutputItem>` inside `Http11Engine.CreateFlow()` routes the
signal to `ConnectionStage`.

HTTP/1.1 uses only `StreamAcquireItem` — there is no `MaxConcurrentStreamsItem` because the
pipeline depth (`6`) is a static client-side default.

### HTTP/1.0: No Fanout Needed

HTTP/1.0 is single-request-per-connection. The existing `ConnectionReuseItem` path
(re-acquire handle after each response) already enforces the limit. `Http10Engine`,
`Http10EncoderStage`, and `Http10DecoderStage` still receive native I/O type changes (Phase 0)
for consistency, but no signal outlet is added.

### Why `HeadersFrame` = stream start

`StreamIdAllocatorStage` (upstream) emits exactly one `HeadersFrame` per new client stream before
any `DataFrame`s. Emitting `StreamAcquireItem` on each `HeadersFrame` from `_inletRequest` is
a 1:1 mapping to "new HTTP/2 stream started".

---

## Goals

- **HTTP/1.0**: one request per connection, always fresh handle after response.
- **HTTP/1.1**: pipeline depth bounded by `ConnectionState` default (6); MRU connection reuse;
  new connection spawned when all slots full and within `PerHostConnectionLimiter`.
- **HTTP/2**: stream count bounded by server-negotiated `MAX_CONCURRENT_STREAMS`;
  `Http20ConnectionStage` enforces via backpressure (stop pulling `_inletRequest`); signals
  the value and each stream start to `HostPoolActor` for connection spawning decisions.
  New connection spawned when at capacity and within the limiter.
- **Stateless ConnectionStage**: zero protocol knowledge. Holds handle for I/O, forwards
  `IControlItem`s to actor hierarchy. Re-acquires handle on channel completion (protocol-agnostic).
- Signals reach `ConnectionStage` through `BidiFlow.Outlet1` (merged inside each engine) — no
  changes to `Engine.BuildConnectionFlowPublic` or `IHttpProtocolEngine`.
- Pending requesters queued in `HostPoolActor` and served FIFO when a slot frees or a new
  connection becomes ready.

---

## Phase 0: Native I/O Types (Prerequisites)

Before the signal merge inside engine BidiFlows can work, encoder and decoder stages must speak
`IOutputItem`/`IInputItem` natively. This eliminates the `flowOut`/`flowIn` adapter lambdas that
currently exist inside each engine's `GraphDsl.Create()` block, making room for the
`MergePreferred` wiring without restructuring the engine graph.

---

### TASK-9-E01: `Http20EncoderStage` — output `IOutputItem` natively
**File:** `src/TurboHttp/Streams/Stages/Http20EncoderStage.cs`

**Description:** Change the stage's output type from `(IMemoryOwner<byte>, int)` to `IOutputItem`
so that `Http20Engine` can eliminate its `flowOut` adapter.

**Acceptance Criteria:**
- [x] Stage changes from `FlowShape<Http2Frame, (IMemoryOwner<byte>, int)>` to
      `FlowShape<Http2Frame, IOutputItem>`
- [x] Each serialised frame is emitted as `new DataItem(HostKey.Default, memory, length)`
- [x] No cast at the call site is required
- [x] Existing stream tests for `Http20EncoderStage` pass unchanged (output type updated in tests)
- [x] Build succeeds with zero errors

---

### TASK-9-E02: `Http20DecoderStage` — accept `IInputItem` natively
**File:** `src/TurboHttp/Streams/Stages/Http20DecoderStage.cs`

**Description:** Change the stage's input type from `(IMemoryOwner<byte>, int)` to `IInputItem`
so that `Http20Engine` can eliminate its `flowIn` adapter.

**Acceptance Criteria:**
- [x] Stage changes from `FlowShape<(IMemoryOwner<byte>, int), Http2Frame>` to
      `FlowShape<IInputItem, Http2Frame>`
- [x] Internally casts to `DataItem` and extracts `(Memory, Length)` before feeding the existing parser
- [x] Non-`DataItem` items are silently dropped (same as the current `Where(x => x is DataItem)`)
- [x] Existing stream tests for `Http20DecoderStage` pass unchanged (input type updated in tests)
- [x] Build succeeds with zero errors

---

### TASK-9-E03: `Http20Engine` — remove adapters; merge `OutletSignal` via `MergePreferred`
**File:** `src/TurboHttp/Streams/Http20Engine.cs`

**Description:** Remove the `flowOut`/`flowIn` adapter lambdas and wire `Http20ConnectionStage.OutletSignal`
into the `IOutputItem` output stream using `MergePreferred<IOutputItem>`.

**Acceptance Criteria:**
- [x] `flowOut` lambda deleted; encoder is wired directly to the BidiFlow's outlet
- [x] `flowIn` lambda deleted; decoder is wired directly from the BidiFlow's inlet (done in TASK-9-E02)
- [x] Add `var signalMerge = b.Add(new MergePreferred<IOutputItem>(1))` (1 non-preferred + 1 preferred)
- [x] Wire:
  ```
  frameEncoder.Outlet → signalMerge.In(0)        // DataItem (normal data)
  connection.OutletSignal → signalMerge.Preferred  // IControlItem (signals, higher priority)
  signalMerge.Out → BidiFlow.Outlet1 (IOutputItem)
  ```
- [x] BidiFlow type remains `BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage>`
- [x] No changes to `Engine.cs` or `IHttpProtocolEngine.CreateFlow()` signatures
- [x] Engine round-trip test: inbound SETTINGS `MAX_CONCURRENT_STREAMS = 50` produces
  `MaxConcurrentStreamsItem(50)` on the `IOutputItem` outlet

---

### TASK-9-E04: `Http11EncoderStage` — output `IOutputItem` natively
**File:** `src/TurboHttp/Streams/Stages/Http11EncoderStage.cs`

**Description:** Mirror TASK-9-E01 for HTTP/1.1.

**Acceptance Criteria:**
- [x] Stage changes from `FlowShape<HttpRequestMessage, (IMemoryOwner<byte>, int)>` to
      `FlowShape<HttpRequestMessage, IOutputItem>`
- [x] Each serialised request is emitted as `new DataItem(HostKey.Default, memory, length)`
- [x] Existing stream tests pass (output type updated)
- [x] Build succeeds

---

### TASK-9-E05: `Http11DecoderStage` — accept `IInputItem` natively
**File:** `src/TurboHttp/Streams/Stages/Http11DecoderStage.cs`

**Description:** Mirror TASK-9-E02 for HTTP/1.1.

**Acceptance Criteria:**
- [x] Stage changes from `FlowShape<(IMemoryOwner<byte>, int), HttpResponseMessage>` to
      `FlowShape<IInputItem, HttpResponseMessage>`
- [x] Internally casts to `DataItem` and extracts `(Memory, Length)` before the existing parser
- [x] Non-`DataItem` items dropped
- [x] Existing stream tests pass (input type updated)
- [x] Build succeeds

---

### TASK-9-E06: `Http1XCorrelationStage` — add `OutletSignal`; emit `StreamAcquireItem`
**File:** `src/TurboHttp/Streams/Stages/Http1XCorrelationStage.cs`

**Description:** Add a 4th port `OutletSignal: Outlet<IControlItem>` to the stage. Emit
`StreamAcquireItem` each time a new request enters the pipeline so `HostPoolActor` can
increment its in-flight counter and enforce the pipeline-depth limit.

**Acceptance Criteria:**
- [x] Stage shape expands from `FanInShape<HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>`
      to a custom shape adding `OutletSignal: Outlet<IControlItem>`
- [x] On each push to `_requestIn`: `Emit(OutletSignal, new StreamAcquireItem())`
- [x] `OnPull` handler for `OutletSignal` is a no-op (demand-driven by `Emit`)
- [x] Existing `_pending`/`_waiting` correlation logic is unchanged
- [x] Unit test: one request pushed → `OutletSignal` emits one `StreamAcquireItem`
- [x] Unit test: two requests pushed → two `StreamAcquireItem`s emitted
- [x] Existing correlation tests pass unchanged

---

### TASK-9-E07: `Http11Engine` — remove adapters; merge `OutletSignal` via `MergePreferred`
**File:** `src/TurboHttp/Streams/Http11Engine.cs`

**Description:** Mirror TASK-9-E03 for HTTP/1.1, using `Http1XCorrelationStage.OutletSignal`
as the signal source.

**Acceptance Criteria:**
- [x] `flowOut` adapter lambda deleted; encoder wired directly to BidiFlow outlet
- [x] `flowIn` adapter lambda deleted; decoder wired directly from BidiFlow inlet
- [x] Add `var signalMerge = b.Add(new MergePreferred<IOutputItem>(1))`
- [x] Wire:
  ```
  encoder.Outlet     → signalMerge.In(0)          // DataItem
  correlation.OutletSignal → signalMerge.Preferred // StreamAcquireItem (higher priority)
  signalMerge.Out    → BidiFlow.Outlet1
  ```
- [x] No changes to `Engine.cs` or `IHttpProtocolEngine`
- [x] Build succeeds; existing Http11Engine round-trip tests pass

---

### TASK-9-E08: `Http10EncoderStage` — output `IOutputItem` natively
**File:** `src/TurboHttp/Streams/Stages/Http10EncoderStage.cs`

**Description:** Mirror TASK-9-E01 for HTTP/1.0 (consistency; no signal merge needed).

**Acceptance Criteria:**
- [x] Stage changes from `FlowShape<HttpRequestMessage, (IMemoryOwner<byte>, int)>` to
      `FlowShape<HttpRequestMessage, IOutputItem>`
- [x] Emits `new DataItem(HostKey.Default, memory, length)`
- [x] Existing tests pass; build succeeds

---

### TASK-9-E09: `Http10DecoderStage` — accept `IInputItem` natively
**File:** `src/TurboHttp/Streams/Stages/Http10DecoderStage.cs`

**Description:** Mirror TASK-9-E02 for HTTP/1.0 (consistency; no signal merge needed).

**Acceptance Criteria:**
- [x] Stage changes from `FlowShape<(IMemoryOwner<byte>, int), HttpResponseMessage>` to
      `FlowShape<IInputItem, HttpResponseMessage>`
- [x] Non-`DataItem` items dropped
- [x] Existing tests pass; build succeeds

---

### TASK-9-E10: `Http10Engine` — remove adapters
**File:** `src/TurboHttp/Streams/Http10Engine.cs`

**Description:** Remove the `flowOut`/`flowIn` adapters now that the encoder and decoder stages
speak `IOutputItem`/`IInputItem` natively. No signal outlet is added (HTTP/1.0 uses one request
per connection; `ConnectionReuseItem` already enforces the limit).

**Acceptance Criteria:**
- [x] `flowOut` and `flowIn` adapter lambdas deleted
- [x] Encoder wired directly to BidiFlow outlet; decoder directly from BidiFlow inlet
- [x] No `MergePreferred` added (HTTP/1.0 needs no signal)
- [x] Build succeeds; existing Http10Engine round-trip tests pass

---

## Phase 1: Control Item Types

### TASK-9-001: Add `MaxConcurrentStreamsItem` and `StreamAcquireItem` control types
**Files:** `src/TurboHttp/IO/Stages/MaxConcurrentStreamsItem.cs` (new),
`src/TurboHttp/IO/Stages/StreamAcquireItem.cs` (new)

**Description:** As `ConnectionStage`, I want typed control items for the two signals that
`Http20ConnectionStage` and `Http1XCorrelationStage` will emit so I can forward them to the
actor hierarchy.

**Acceptance Criteria:**
- [x] `public record MaxConcurrentStreamsItem(int MaxStreams) : IControlItem`
- [x] `public record StreamAcquireItem : IControlItem` (no payload needed)
- [x] Both placed in namespace `TurboHttp.IO.Stages`
- [x] Build succeeds with zero errors

---

## Phase 2: ConnectionHandle and ConnectionState

### TASK-9-002: `ConnectionHandle` — add `UpdateMaxConcurrentStreams` and volatile field
**File:** `ConnectionHandle.cs`

**Description:** As `HostPoolActor`, I want to read the effective stream limit on a handle
so that `SelectConnection` can make routing decisions.

**Acceptance Criteria:**
- [x] Add `private volatile int _maxConcurrentStreams = 100`
- [x] Public getter: `public int MaxConcurrentStreams => _maxConcurrentStreams`
- [x] Public mutator: `public void UpdateMaxConcurrentStreams(int value) => Volatile.Write(ref _maxConcurrentStreams, value)`
- [x] Field is NOT part of the primary constructor and does NOT affect record equality/hash
- [x] No `#nullable enable` directive added
- [x] Unit test: concurrent writes and reads do not throw; value is eventually consistent

---

### TASK-9-003: `ConnectionState` — associate `ConnectionHandle`, version-aware `MaxConcurrentStreams`
**File:** `ConnectionState.cs`

**Description:** As `HostPoolActor`, I want each `ConnectionState` to own its `ConnectionHandle`
so I can read the live stream limit and route to the correct channel.

**Acceptance Criteria:**
- [x] Add `public ConnectionHandle? Handle { get; private set; }`
- [x] Add `public void SetHandle(ConnectionHandle handle)` — stores handle, updates `LastActivity`
- [x] `HttpVersion` becomes computed: `public Version HttpVersion => Handle?.Key.Version ?? System.Net.HttpVersion.Version11`
- [x] `MaxConcurrentStreams` property returns version-dependent default when handle is absent:
  - HTTP/1.0 (`Major == 1 && Minor == 0`): `1`
  - HTTP/1.1: `6` (pipeline depth default)
  - HTTP/2+: `Handle?.MaxConcurrentStreams ?? 100`
- [x] `public bool HasAvailableSlot => Active && Reusable && PendingRequests < MaxConcurrentStreams`
- [x] No existing public methods removed
- [x] Unit tests cover all three version branches and `HasAvailableSlot` edge cases

---

## Phase 3: Http20ConnectionStage — Signal Outlet + Backpressure

### TASK-9-004: `Http20ConnectionStage` — define custom shape with signal outlet
**File:** `Http20ConnectionStage.cs`

**Description:** Add the 5th port `OutletSignal` to the stage shape. This outlet feeds the
`MergePreferred<IOutputItem>` inside `Http20Engine.CreateFlow()` (TASK-9-E03).

**Acceptance Criteria:**
- [x] Define `public sealed class Http20ConnectionShape : Shape` with 5 ports:
  - `Inlet<Http2Frame> ServerIn`
  - `Outlet<Http2Frame> AppOut`
  - `Inlet<Http2Frame> AppIn`
  - `Outlet<Http2Frame> ServerOut`
  - `Outlet<IControlItem> OutletSignal`
- [x] `Http20ConnectionStage` changes to `GraphStage<Http20ConnectionShape>`
- [x] `Shape` property returns an `Http20ConnectionShape` instance
- [x] All existing port variables renamed to use the shape's properties for consistency
- [x] Constructor and `_initialRecvWindowSize` parameter unchanged
- [x] Build succeeds; no existing tests broken

---

### TASK-9-005: `Http20ConnectionStage` — emit `MaxConcurrentStreamsItem` on SETTINGS
**File:** `Http20ConnectionStage.cs`

**Description:** When the server sends a SETTINGS frame containing `MAX_CONCURRENT_STREAMS`,
emit a `MaxConcurrentStreamsItem` on `OutletSignal` and update the internal limit for
backpressure decisions (TASK-9-007).

**Acceptance Criteria:**
- [x] Add field `private int _maxConcurrentStreams = 100` (default per RFC 9113 §6.5.2)
- [x] `HandleSettings` reads `SettingsParameter.MaxConcurrentStreams` (key = 3):
  ```
  if key == MaxConcurrentStreams:
      _maxConcurrentStreams = (int)value
      Emit(OutletSignal, new MaxConcurrentStreamsItem((int)value))
  ```
- [x] After updating `_maxConcurrentStreams`, re-evaluate pull state:
  if `_activeStreams < _maxConcurrentStreams` and `_inletRequest` not pulled → `Pull(_inletRequest)`
  *(Note: `_activeStreams` does not exist yet — pull re-evaluation is a no-op until TASK-9-007 adds stream counting and pull gating)*
- [x] Existing `InitialWindowSize` handling unchanged
- [x] SETTINGS ACK (`isAck: true`) frames are still ignored
- [x] `OutletSignal` handler registered (`onPull: () => { }` — demand-driven by `Emit`)
- [x] Unit test: SETTINGS frame with `MAX_CONCURRENT_STREAMS = 50` → `OutletSignal` emits `MaxConcurrentStreamsItem(50)`
- [x] Unit test: SETTINGS ACK → no emission on `OutletSignal`

---

### TASK-9-006: `Http20ConnectionStage` — emit `StreamAcquireItem` on outbound HEADERS
**File:** `Http20ConnectionStage.cs`

**Description:** When the app layer sends a `HeadersFrame` (new client stream), emit a
`StreamAcquireItem` on `OutletSignal` and increment the active stream count.

**Acceptance Criteria:**
- [x] In `_inletRequest` handler, before pushing to `_outletRaw`:
  ```
  if (frame is HeadersFrame):
      _activeStreams++
      Emit(OutletSignal, new StreamAcquireItem())
  ```
- [x] Non-`HeadersFrame` types are unaffected
- [x] Unit test: `HeadersFrame` on `InletRequest` → `OutletSignal` emits `StreamAcquireItem`
- [x] Unit test: `DataFrame` on `InletRequest` → no emission on `OutletSignal`

---

### TASK-9-007: `Http20ConnectionStage` — stream-level backpressure
**File:** `Http20ConnectionStage.cs`

**Description:** As the HTTP/2 protocol layer, I want to gate `_inletRequest` when the number
of active streams reaches `_maxConcurrentStreams`, so that backpressure propagates upstream
without any protocol knowledge in `ConnectionStage`.

**Acceptance Criteria:**
- [x] Add field `private int _activeStreams = 0` to Logic
- [x] Add field `private int _maxConcurrentStreams = 100` to Logic (or reuse from TASK-9-005)
- [x] Increment `_activeStreams` on outbound `HeadersFrame` (already done in TASK-9-006)
- [x] Decrement `_activeStreams` on inbound frame with `END_STREAM` flag:
  ```
  if (frame is DataFrame { EndStream: true } or HeadersFrame { EndStream: true }):
      _activeStreams = Math.Max(0, _activeStreams - 1)
  if (frame is RstStreamFrame):
      _activeStreams = Math.Max(0, _activeStreams - 1)
  ```
- [x] Remove stream from `_streamWindows` dictionary on END_STREAM/RST_STREAM (cleanup)
- [x] **Pull gating** — change `_outletRaw` handler:
  ```
  SetHandler(_outletRaw, onPull: () =>
  {
      if (_activeStreams < _maxConcurrentStreams && !HasBeenPulled(_inletRequest))
          Pull(_inletRequest);
      // else: backpressure — don't pull until a stream closes
  });
  ```
- [x] On stream close (decrement): if `_activeStreams < _maxConcurrentStreams` and
  `_outletRaw` has demand and `_inletRequest` not pulled → `Pull(_inletRequest)`
- [x] On `MaxConcurrentStreamsItem` update (TASK-9-005): re-evaluate pull in same manner
- [x] Unit test (limit = 3): 3 `HeadersFrame`s → no pull after 3rd
- [x] Unit test (limit = 3): 3 `HeadersFrame`s + 1 `END_STREAM` → pull resumes
- [x] Unit test: `RstStreamFrame` → `_activeStreams` decrements, pull resumes
- [x] Unit test: SETTINGS `MAX_CONCURRENT_STREAMS = 2` mid-session → new limit enforced immediately

---

## Phase 4: HostPoolActor Rewrite

### TASK-9-008: `HostPoolActor` — add `StreamCompleted` and `StreamAcquired` messages
**File:** `HostPoolActor.cs`

**Description:** As the connection pool, I want per-stream lifecycle signals so I can
track pending counts and serve queued requesters.

**Acceptance Criteria:**
- [x] Add `public sealed record StreamCompleted(IActorRef Connection)`
- [x] Add `public sealed record StreamAcquired(IActorRef Connection)`
- [x] Add `public sealed record UpdateMaxConcurrentStreams(IActorRef Connection, int MaxStreams)`
- [x] `Receive<StreamCompleted>(HandleStreamCompleted)` registered in constructor
- [x] `Receive<StreamAcquired>(HandleStreamAcquired)` registered in constructor
- [x] `Receive<UpdateMaxConcurrentStreams>(HandleUpdateMaxConcurrentStreams)` registered in constructor
- [x] Build succeeds

---

### TASK-9-009: `HostPoolActor` — implement `SelectConnection` with MRU ordering
**File:** `HostPoolActor.cs`

**Description:** Select the most-recently-used connection with a free stream slot to minimise
idle connections while respecting per-connection limits.

**Acceptance Criteria:**
- [x] Private method `SelectConnection(): ConnectionState?`
- [x] Returns the connection from `_connections` where `conn.HasAvailableSlot == true`,
  ordered descending by `conn.LastActivity` (MRU first)
- [x] Returns `null` when no eligible connection exists
- [x] Unit tests cover:
  - All connections at capacity → returns `null`
  - Multiple eligible connections → returns most recently active
  - Dead / non-reusable connections skipped

---

### TASK-9-010: `HostPoolActor` — rewrite `HandleConnectionReady`
**File:** `HostPoolActor.cs`

**Description:** Store the received handle in the owning `ConnectionState` and immediately serve
queued requesters.

**Acceptance Criteria:**
- [x] Remove the `private ConnectionHandle? _activeHandle` field entirely
- [x] `HandleConnectionReady`:
  1. `Find(msg.Handle.ConnectionActor)?.SetHandle(msg.Handle)`
  2. Call `ServePendingRequesters()`
- [x] `_pendingHandleRequesters` remains `List<IActorRef>`
- [x] Zero references to `_activeHandle` remain in the file

---

### TASK-9-011: `HostPoolActor` — rewrite `HandleEnsureHost`
**File:** `HostPoolActor.cs`

**Description:** Return a handle immediately when a slot is available, or queue the requester
and attempt to spawn a new connection.

**Acceptance Criteria:**
- [x] Logic:
  ```
  conn = SelectConnection()
  if conn != null:
      conn.MarkBusy()
      Sender.Tell(conn.Handle!)
      return
  _pendingHandleRequesters.Add(Sender)
  SpawnConnection()   // noop if _limiter refuses
  ```
- [x] Requester is always queued before `SpawnConnection` is called (no race)
- [x] Unit tests:
  - Slot available → handle returned immediately, `MarkBusy` called
  - All slots full + under limiter → requester queued, new connection spawned
  - All slots full + at limiter limit → requester queued only, no spawn

---

### TASK-9-012: `HostPoolActor` — add handlers for `StreamCompleted`, `StreamAcquired`, `UpdateMaxConcurrentStreams`
**File:** `HostPoolActor.cs`

**Description:** When stream lifecycle signals arrive (forwarded by ConnectionStage →
ConnectionActor), update the corresponding ConnectionState and drain the pending queue.

**Acceptance Criteria:**
- [x] `HandleStreamCompleted(StreamCompleted msg)`:
  1. `Find(msg.Connection)?.MarkIdle()`
  2. Call `ServePendingRequesters()`
- [x] `HandleStreamAcquired(StreamAcquired msg)`:
  1. `Find(msg.Connection)?.MarkBusy()`
- [x] `HandleUpdateMaxConcurrentStreams(UpdateMaxConcurrentStreams msg)`:
  1. `var conn = Find(msg.Connection)`
  2. `conn?.Handle?.UpdateMaxConcurrentStreams(msg.MaxStreams)`
  3. Call `ServePendingRequesters()` (limit may have increased)
- [x] `ServePendingRequesters()`:
  ```
  while _pendingHandleRequesters.Count > 0:
      conn = SelectConnection()
      if conn == null: break
      requester = _pendingHandleRequesters[0]
      _pendingHandleRequesters.RemoveAt(0)
      conn.MarkBusy()
      requester.Tell(conn.Handle!)
  ```
- [x] Unit tests:
  - `StreamCompleted` → slot freed → queued requester served
  - `StreamCompleted` → no eligible connection → queue unchanged
  - Multiple queued requesters drained in FIFO order
  - `UpdateMaxConcurrentStreams` with increased limit → queued requesters served

---

### TASK-9-013: `HostPoolActor` — fix `EvictIdleConnections` and `HandleReconnect`
**File:** `HostPoolActor.cs`

**Description:** Remove all references to the deleted `_activeHandle` field; call
`ServePendingRequesters` after eviction to unblock any waiting requesters.

**Acceptance Criteria:**
- [x] Remove block: `if (_activeHandle?.ConnectionActor.Equals(conn.Actor) == true) _activeHandle = null;`
- [x] Replace end-of-method spawn logic with `ServePendingRequesters()`
- [x] `HandleReconnect`: remove `previousVersion` capture and `newConn.HttpVersion = previousVersion`
  assignment (version is now derived from the handle once it arrives)
- [x] Eviction still respects `IdleTimeout` and `_connections.Count > 1` minimum
- [x] `_limiter.Release` still called per evicted connection

---

## Phase 5: ConnectionActor Forwarding

### TASK-9-014: `ConnectionActor` — forward stream lifecycle messages
**File:** `ConnectionActor.cs`

**Description:** Forward stream lifecycle messages from `ConnectionStage` up to `HostPoolActor`,
mirroring the existing `MarkConnectionNoReuse` forwarding pattern.

**Acceptance Criteria:**
- [x] In constructor:
  ```csharp
  Receive<HostPoolActor.StreamCompleted>(msg => Context.Parent.Tell(msg));
  Receive<HostPoolActor.StreamAcquired>(msg => Context.Parent.Tell(msg));
  Receive<HostPoolActor.UpdateMaxConcurrentStreams>(msg => Context.Parent.Tell(msg));
  ```
- [x] No other changes
- [x] Unit test: each message type sent to `ConnectionActor` → parent receives it

---

## Phase 6: ConnectionStage — Stateless Signal Forwarding

### TASK-9-015: `ConnectionStage` — forward all `IControlItem` to actor hierarchy
**File:** `ConnectionStage.cs` (Logic class)

**Description:** `ConnectionStage` becomes a pure byte pipe that forwards all protocol signals
to the actor hierarchy without storing any protocol state. No version checks, no stream counting,
no MaxConcurrentStreams tracking.

**Acceptance Criteria:**
- [x] **No new fields** — no `_activeH2Streams`, no `_lastOptions`, no version tracking
- [x] On `MaxConcurrentStreamsItem`:
  ```csharp
  _handle?.ConnectionActor.Tell(
      new HostPoolActor.UpdateMaxConcurrentStreams(_handle.ConnectionActor, item.MaxStreams));
  Pull(_stage._inlet);  // signal consumed, ready for next
  ```
- [x] On `StreamAcquireItem`:
  ```csharp
  _handle?.ConnectionActor.Tell(
      new HostPoolActor.StreamAcquired(_handle.ConnectionActor));
  Pull(_stage._inlet);  // signal consumed, ready for next
  ```
- [x] On `ConnectionReuseItem`:
  ```csharp
  if (!reuseItem.Decision.CanReuse)
      _handle?.ConnectionActor.Tell(new HostPoolActor.MarkConnectionNoReuse(_handle.ConnectionActor));
  _handle?.ConnectionActor.Tell(
      new HostPoolActor.StreamCompleted(_handle.ConnectionActor));
  Pull(_stage._inlet);  // signal consumed, ready for next
  ```
- [x] On inbound channel complete (`_onInboundComplete`):
  ```csharp
  _handle = null;
  // Connection dropped — re-acquire on demand.
  // Next ConnectItem (from ExtractOptionsStage on new substream) or
  // next DataItem (if substream still alive) triggers re-acquire.
  ```
- [x] On `DataItem` when `_handle is null`:
  send `EnsureHost` to `PoolRouter` via `_stageActor` and buffer the `DataItem`
  (or fail the stage — see design note)
- [x] Unit test: `MaxConcurrentStreamsItem(50)` received → `ConnectionActor` receives
  `UpdateMaxConcurrentStreams(_, 50)`
- [x] Unit test: `StreamAcquireItem` received → `ConnectionActor` receives `StreamAcquired`
- [x] Unit test: `ConnectionReuseItem{CanReuse=false}` → both `MarkConnectionNoReuse` and
  `StreamCompleted` sent
- [x] Unit test: `ConnectionReuseItem{CanReuse=true}` → only `StreamCompleted` sent (no MarkNoReuse)
- [x] **Zero protocol-awareness**: no `if (version == HTTP/2)` branches in the file

**Design Note — handle loss recovery:**
When the inbound channel completes (TCP drop), `_handle` becomes null. For HTTP/2, the
`Http20ConnectionStage` will also see the connection close (upstream finish) and can signal
appropriately. For HTTP/1.x, the server closes the connection after each response (HTTP/1.0) or
on `Connection: close` (HTTP/1.1). In both cases, the `_onInboundComplete` callback clears the
handle. The next request on this substream will find `_handle == null` and a `DataItem` will
trigger re-acquisition via `EnsureHost`. This is protocol-agnostic — `ConnectionStage` doesn't
need to know *why* the connection closed.

---

## Functional Requirements

- **FR-1**: `SelectConnection` must never return a connection where `PendingRequests >= MaxConcurrentStreams`.
- **FR-2**: `HandleEnsureHost` must never deliver a `null` handle — it either returns a valid handle or queues the sender.
- **FR-3**: `StreamCompleted` must decrement `PendingRequests` on the correct connection matched by `IActorRef`.
- **FR-4**: HTTP/1.0 connections must never accept more than 1 concurrent request (`MaxConcurrentStreams = 1` in `ConnectionState`).
- **FR-5**: HTTP/1.1 connections must cap at pipeline depth 6 (default) concurrent pending requests.
- **FR-6**: HTTP/2 connections must cap at `_maxConcurrentStreams` (default 100, updated from server SETTINGS). `Http20ConnectionStage` enforces via pull backpressure. `HostPoolActor` enforces via slot accounting.
- **FR-7**: When all connections are at capacity, `HostPoolActor` MUST attempt to spawn a new connection (subject to `PerHostConnectionLimiter`) before the requester waits indefinitely.
- **FR-8**: Among eligible connections, the one with the most recent `LastActivity` is preferred (MRU).
- **FR-9**: HTTP/1.x and HTTP/2 share no `HostPoolActor` instance — isolation is guaranteed by `HostKey.Version`.
- **FR-10**: `OutletSignal` on `Http20ConnectionStage` must not stall the main frame path; it uses `Emit` (buffered) so frame processing is never blocked waiting for signal demand.
- **FR-11**: Signals flow through `BidiFlow.Outlet1` — no changes to `Engine.BuildConnectionFlowPublic` or `IHttpProtocolEngine.CreateFlow()`.
- **FR-12**: `ConnectionStage` has zero protocol-specific branches. All version-dependent behavior lives in the engine stages or actor layer.

---

## Non-Goals (Out of Scope)

- Changes to `Engine.cs` (`BuildConnectionFlowPublic`, `BuildProtocolFlow`) or the `IHttpProtocolEngine` interface.
- HTTP/3 connection management.
- Priority-based stream scheduling (RFC 9218).
- `TurboClientOptions.PipelineDepth` as an explicit config property (see Open Questions).
- Global cross-host connection limits (handled by `PerHostConnectionLimiter` as-is).
- Push-Promise stream tracking (server-push is not a client concern here).
- HTTP/1.0 signal outlet — `ConnectionReuseItem` already enforces the 1-request limit.
- Pre-establishing connections before the first request (substreams are created on first request;
  connection opens naturally).

---

## Data Flow Summary

```
 ┌─────────────────────────────────────────────────────────────────────────────┐
 │  HTTP/2 engine BidiFlow (inside Http20Engine.CreateFlow)                    │
 │                                                                             │
 │  Outbound:                                                                  │
 │  HttpRequest → StreamIdAllocator → Request2Frame → Http20ConnectionStage   │
 │                                                         ├─► OutletRaw       │
 │                                              Http20EncoderStage ────────┐   │
 │                                                         └─► OutletSignal─┐  │
 │                                                                          │  │
 │                                              MergePreferred<IOutputItem> │  │
 │                                                 Preferred ◄──────────────┘  │
 │                                                 In(0)    ◄── DataItem       │
 │                                                 Out ──► BidiFlow.Outlet1    │
 │                                                          (IOutputItem)      │
 │                                                                             │
 │  Backpressure: Http20ConnectionStage gates _inletRequest when               │
 │    _activeStreams >= _maxConcurrentStreams. No protocol knowledge needed     │
 │    downstream in ConnectionStage.                                           │
 │                                                                             │
 │  Inbound: BidiFlow.Inlet2 (IInputItem) → Http20DecoderStage               │
 │           → Http20ConnectionStage → Http20StreamStage                      │
 └─────────────────────────────────────────────────────────────────────────────┘

 ┌─────────────────────────────────────────────────────────────────────────────┐
 │  Engine.BuildConnectionFlowPublic (unchanged)                               │
 │                                                                             │
 │  Request path:                                                              │
 │  HttpRequest → ExtractOptions → Concat → MergePreferred.In(0)             │
 │                                    ▲            ▲                          │
 │                         BidiFlow.Outlet1        │ Preferred                │
 │                          (DataItem +            │                          │
 │                           MaxConcurrentStreamsItem ──────────────────────┐  │
 │                           StreamAcquireItem)    │                        │  │
 │                                                 └── ConnectionReuseItem  │  │
 │                                                                          │  │
 │  ConnectionStage (stateless byte pipe):                                  │  │
 │    ConnectItem               → EnsureHost → HostPoolActor               │  │
 │    DataItem                  → write to _handle channel                  │  │
 │    StreamAcquireItem         → forward to ConnectionActor → HostPoolActor│  │
 │    MaxConcurrentStreamsItem  → forward to ConnectionActor → HostPoolActor│  │
 │    ConnectionReuseItem       → forward StreamCompleted + MarkNoReuse ────┘  │
 │                                                                             │
 │  HostPoolActor:                                                             │
 │    EnsureHost               → SelectConnection (MRU) → handle or queue    │
 │    StreamAcquired           → conn.MarkBusy                               │
 │    StreamCompleted          → conn.MarkIdle → ServePendingRequesters      │
 │    UpdateMaxConcurrentStreams → conn.Handle.UpdateMaxConcurrentStreams     │
 └─────────────────────────────────────────────────────────────────────────────┘
```

---

## Recommended Implementation Order

Tasks are grouped by parallelism. Within each group, tasks have no dependencies on each other
and can be implemented in parallel. Groups must be completed in order.

```
── Group 1 (no dependencies) ────────────────────────────────────────
  TASK-9-001   (MaxConcurrentStreamsItem + StreamAcquireItem types)
  TASK-9-002   (ConnectionHandle — volatile MaxConcurrentStreams field)
  TASK-9-008   (HostPoolActor — StreamCompleted/StreamAcquired/UpdateMaxConcurrentStreams messages)

── Group 2 (needs: 001) ─────────────────────────────────────────────
  TASK-9-E01   (Http20EncoderStage → IOutputItem)
  TASK-9-E02   (Http20DecoderStage ← IInputItem)
  TASK-9-E04   (Http11EncoderStage → IOutputItem)
  TASK-9-E05   (Http11DecoderStage ← IInputItem)
  TASK-9-E08   (Http10EncoderStage → IOutputItem)
  TASK-9-E09   (Http10DecoderStage ← IInputItem)
  TASK-9-004   (Http20ConnectionStage — custom 5-port shape)
  TASK-9-E06   (Http1XCorrelationStage — OutletSignal)
  TASK-9-003   (ConnectionState — handle + version-aware limits)        [needs 002]

── Group 3 (needs: Group 2) ─────────────────────────────────────────
  TASK-9-E10   (Http10Engine — remove adapters)                        [needs E08, E09]
  TASK-9-E07   (Http11Engine — remove adapters + signal merge)         [needs E04, E05, E06]
  TASK-9-005   (Http20ConnectionStage — emit MaxConcurrentStreamsItem)  [needs 004]
  TASK-9-006   (Http20ConnectionStage — emit StreamAcquireItem)        [needs 004]
  TASK-9-014   (ConnectionActor — forward lifecycle messages)          [needs 008]
  TASK-9-009   (HostPoolActor — SelectConnection)                      [needs 003]

── Group 4 (needs: Group 3) ─────────────────────────────────────────
  TASK-9-007   (Http20ConnectionStage — stream-level backpressure)     [needs 005, 006]
  TASK-9-E03   (Http20Engine — remove adapters + signal merge)         [needs E01, E02, 004-006]
  TASK-9-010   (HostPoolActor — HandleConnectionReady rewrite)         [needs 009]
  TASK-9-011   (HostPoolActor — HandleEnsureHost rewrite)              [needs 009, 010]

── Group 5 (needs: Group 4) ─────────────────────────────────────────
  TASK-9-012   (HostPoolActor — HandleStreamCompleted + ServePendingRequesters) [needs 008, 009]
  TASK-9-013   (HostPoolActor — EvictIdleConnections + HandleReconnect)        [needs 010]
  TASK-9-015   (ConnectionStage — stateless signal forwarding)                 [needs 001, 008, 014]
```

---

## Success Metrics

- **HTTP/2**: 150 concurrent requests with server `MAX_CONCURRENT_STREAMS = 100` → at most 100
  in-flight streams at any moment; zero RST_STREAM or GOAWAY received.
- **HTTP/2 backpressure**: `Http20ConnectionStage` stops pulling `_inletRequest` at capacity;
  verified via unit test with mock shape.
- **HTTP/2 settings update**: server sends `MAX_CONCURRENT_STREAMS = 50` mid-session →
  `Http20ConnectionStage._maxConcurrentStreams` reflects 50; backpressure adjusts immediately;
  `ConnectionHandle.MaxConcurrentStreams` updated via actor forwarding.
- **HTTP/1.1**: 3 concurrent requests with pipeline depth 2 → 2 connections in
  `HostPoolActor._connections`.
- **HTTP/1.0**: each request uses a fresh connection; connections do not accumulate.
- **Stateless ConnectionStage**: zero `if (version == ...)` branches in `ConnectionStage.cs`.
  `git grep "Version" src/TurboHttp/IO/Stages/ConnectionStage.cs` returns zero results.
- **No Engine.cs changes**: `git diff Engine.cs` shows zero modifications.
- **Build**: zero errors, zero new compiler warnings on modified files.
- **Tests**: all existing stream and unit tests pass; each task's new tests pass.

---

## Open Questions

1. Should `TurboClientOptions.PipelineDepth` be added as an explicit config property, or is
   the hardcoded default of `6` in `ConnectionState` sufficient for the first iteration?
2. When `PerHostConnectionLimiter` is at max and all streams are full: should there be a
   configurable timeout that fails the requester with a `ConnectionPoolExhaustedException`
   instead of queuing indefinitely?
3. Should `StreamAcquireItem` carry the HTTP/2 stream ID for diagnostics/logging purposes?
4. Should `OutletSignal` on `Http20ConnectionStage` be typed `Outlet<IControlItem>` (broad)
   or a concrete union type to prevent accidental misuse?
5. When `ConnectionStage` detects `_handle == null` on a `DataItem` (connection dropped
   mid-substream), should it buffer the item and re-acquire, or fail the stage?
   Recommendation: buffer + re-acquire for HTTP/1.x resilience; fail for HTTP/2 (GOAWAY means
   the entire connection is dead, streams should be retried at a higher level).
