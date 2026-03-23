# Feature 025: ExtractOptionsStage State Machine Analysis

## 1. Current State Machine

### State Fields (`ExtractOptionsStage.Logic`)

| Field | Type | Initial | Purpose |
|-------|------|---------|---------|
| `_initialSent` | `bool` | `false` | Guards one-shot `ConnectItem` emission |
| `_pending` | `HttpRequestMessage?` | `null` | Buffers the first request while `_outSignal` is pushed |

### State Transitions

```
┌─────────────┐
│  AWAITING   │  _initialSent = false
│  FIRST REQ  │  _pending = null
└──────┬──────┘
       │ onPush(_in): first HttpRequestMessage arrives
       │
       ▼
┌─────────────────────────────────────────────────────────────┐
│  EMIT CONNECT                                               │
│  1. Build TcpOptions from request URI + clientOptions       │
│  2. _pending = request                                      │
│  3. _initialSent = true                                     │
│  4. Push(_outSignal, ConnectItem(options))     ← line 46    │
│  5. Complete(_outSignal)                       ← line 47    │
│  6. If _outRequest has demand: Push + clear _pending        │
└──────┬──────────────────────────────────────────────────────┘
       │
       ▼
┌─────────────┐
│  PASSTHROUGH │  _initialSent = true, _outSignal COMPLETED
│  (terminal)  │  All subsequent requests: Push(_outRequest, request)
└─────────────┘
```

### Handler Dispatch Table

| Handler | Condition | Action |
|---------|-----------|--------|
| `_in.onPush` | `!_initialSent` | Build `ConnectItem`, push to `_outSignal`, complete outlet, buffer request |
| `_in.onPush` | `_initialSent` | Push request directly to `_outRequest` |
| `_outSignal.onPull` | `!_initialSent && !HasBeenPulled(_in)` | Pull `_in` (prime the pump) |
| `_outSignal.onDownstreamFinish` | always | no-op (ignore) |
| `_outRequest.onPull` | `_pending is not null` | Push buffered request, clear `_pending` |
| `_outRequest.onPull` | `_pending is null && !HasBeenPulled(_in)` | Pull `_in` |
| `_outRequest.onDownstreamFinish` | always | `CompleteStage()` |
| `_in.onUpstreamFinish` | always | `CompleteStage()` |
| `_in.onUpstreamFailure` | always | Log warning, absorb failure |

### Critical Line: `Complete(_outSignal)` at Line 47

```csharp
// ExtractOptionsStage.cs:46-47
Push(stage._outSignal, new ConnectItem(options) { Key = RequestEndpoint.FromRequest(request) });
Complete(stage._outSignal);
```

This permanently closes the signal outlet after the first `ConnectItem`. No further `ConnectItem` can ever be emitted. This is correct for HTTP/1.1 (persistent connections) and HTTP/2 (multiplexed), but **breaks HTTP/1.0** where each request-response cycle closes the connection.

## 2. Request Flow by Protocol Version

### HTTP/1.1: Single ConnectItem Per Substream (correct)

```
Req1 → ExtractOptions → ConnectItem → ConnectionStage (opens TCP)
                       → Req1 → Encoder → TCP → Decoder → Response1
                                                         → ConnectionReuseItem(KeepAlive)
Req2 → ExtractOptions → Req2 → Encoder → TCP → Decoder → Response2
                                                         → ConnectionReuseItem(KeepAlive)
...same TCP connection reused...
```

- `ConnectionReuseEvaluator` returns `KeepAlive` (HTTP/1.1 default, RFC 9112 §9.3)
- Single `ConnectItem` is sufficient; connection persists
- `Complete(_outSignal)` is harmless — outlet is never needed again

### HTTP/2: Single ConnectItem Per Substream (correct)

```
Req1 → ExtractOptions → ConnectItem → ConnectionStage (opens TCP + TLS + ALPN)
                       → Req1 → H2Encoder → TCP → H2Decoder → Response1
                                                              → ConnectionReuseItem(KeepAlive)
Req2 → ExtractOptions → Req2 → H2Encoder → TCP → H2Decoder → Response2
                                                              → ConnectionReuseItem(KeepAlive)
...streams multiplexed on same connection...
```

- `ConnectionReuseEvaluator` always returns `KeepAlive` for HTTP/2 (RFC 9113 §5.1)
- Stream close ≠ connection close
- `Complete(_outSignal)` is harmless

### HTTP/1.0: Needs Multiple ConnectItems (BROKEN)

```
Req1 → ExtractOptions → ConnectItem → ConnectionStage (opens TCP)
                       → Req1 → Encoder → TCP → Decoder → Response1 (301 redirect)
                                                         → ConnectionReuseItem(Close)
                                                         → ConnectionStage closes TCP

RedirectBidiStage re-emits Req1' (redirected URL)

Req1' → ExtractOptions → ??? _outSignal is COMPLETED → CANNOT emit ConnectItem
                        → Req1' → Encoder → ??? No TCP connection → DEADLOCK
```

- `ConnectionReuseEvaluator` returns `Close` (HTTP/1.0 default, RFC 1945)
- `ConnectionStage` closes the TCP connection
- Redirect/retry needs a new connection but `_outSignal` is permanently closed
- **Result**: pipeline deadlocks waiting for a connection that can never be established

## 3. Pipeline Wiring Context (ProtocolCoreGraphBuilder)

From `BuildConnectionFlow<TEngine>()` (lines 97-134):

```
                    ┌─────────────────────┐
    HttpRequest ───→│  ExtractOptionsStage │
                    │  .In                 │
                    └──┬──────────────┬────┘
                  Out0 │           Out1│
          (requests)   │    (ConnectItem)│
                       ▼               ▼
                 ┌──────────┐    ┌──────────┐
                 │  BidiFlow │    │  Concat   │ In(0) ← ConnectItem
                 │ .Inlet1  │    │  .In(1)   │ In(1) ← BidiFlow.Outlet1
                 │          │    └─────┬─────┘
                 │ .Outlet1 ├─────────→│
                 │          │          ▼
                 │          │    ┌──────────────┐
                 │          │    │ MergePreferred│ In(0) ← Concat.Out
                 │          │    │              │ Preferred ← connReuse.Out1 (feedback)
                 │ .Inlet2  │←───┤ .Out         │
                 │          │    └──────┬───────┘
                 │          │           ▼
                 │ .Outlet2 │    ┌──────────────┐
                 │          │    │ConnectionStage│ (Transport)
                 └────┬─────┘    └──────────────┘
                      │
                      ▼
                 ┌──────────────────┐
                 │ConnectionReuseStg│
                 │  .In             │
                 └──┬───────────┬───┘
               Out0 │        Out1│
         (response) │   (signal) │
                    ▼            ▼
              HttpResponse    ConnectionReuseItem
                               → Buffer(1)
                               → transportMerge.Preferred
                               → ConnectionStage
```

### Current Feedback Path (lines 127-131)

`ConnectionReuseItem` flows from `connReuse.Out1` → buffer → `MergePreferred.Preferred` → `ConnectionStage`. The `ConnectionStage` processes it by telling the actor `MarkConnectionNoReuse()` if decision is `Close`, then pulls next element. **But no signal reaches `ExtractOptionsStage`.**

### Missing Feedback (Feature 025 Gap)

`ExtractOptionsStage` has no inlet for reuse feedback. It cannot know that the connection was closed and a new `ConnectItem` is needed. The `ConnectionStage` line 147 comment confirms this was planned:

```csharp
// Accept next element (e.g. a new ConnectItem for reconnection).
```

## 4. ConnectionReuseStage Feedback Loop

### ConnectionReuseStage Shape

```
FanOutShape<HttpResponseMessage, HttpResponseMessage, IControlItem>
  _in:          HttpResponseMessage (from decoder)
  _outResponse: HttpResponseMessage (to application)
  _outSignal:   IControlItem        (ConnectionReuseItem to feedback)
```

### ConnectionReuseItem Record

```csharp
public record ConnectionReuseItem(RequestEndpoint Key, ConnectionReuseDecision Decision) : IControlItem;
```

### ConnectionReuseDecision

```csharp
public sealed record ConnectionReuseDecision
{
    public bool CanReuse { get; private init; }
    public string Reason { get; private init; }
    public TimeSpan? KeepAliveTimeout { get; private init; }
    public int? MaxRequests { get; private init; }

    public static ConnectionReuseDecision KeepAlive(string reason, ...) => ...;
    public static ConnectionReuseDecision Close(string reason) => ...;
}
```

### Protocol-Specific Decisions (ConnectionReuseEvaluator)

| Condition | Decision | Protocol |
|-----------|----------|----------|
| HTTP/2 | Always `KeepAlive` | RFC 9113 §5.1 |
| Protocol error (decoder exception) | `Close` | All |
| Body not fully consumed | `Close` | HTTP/1.x |
| 101 Switching Protocols | `Close` | HTTP/1.x |
| `Connection: close` header | `Close` | HTTP/1.x |
| HTTP/1.0 without `Connection: Keep-Alive` | `Close` | RFC 1945 |
| HTTP/1.1 (default) | `KeepAlive` | RFC 9112 §9.3 |
| HTTP/1.0 with `Connection: Keep-Alive` | `KeepAlive` | RFC 9112 §9.3 |

## 5. Desired State Machine with Reuse Feedback Inlet

### Proposed Shape Change

```
Current:  FanOutShape<HttpRequestMessage, HttpRequestMessage, IOutputItem>
Proposed: Custom shape with 3 ports:
  _in:       Inlet<HttpRequestMessage>     (requests from application / redirect)
  _inReuse:  Inlet<IControlItem>           (ConnectionReuseItem from feedback loop)
  _outRequest: Outlet<HttpRequestMessage>  (requests to protocol engine)
  _outSignal:  Outlet<IOutputItem>         (ConnectItem to transport concat)
```

### Proposed State Fields

```csharp
private bool _connectItemSent;                    // At least one ConnectItem emitted
private bool _needsReconnect;                     // Last reuse decision was Close
private HttpRequestMessage? _pending;             // Buffered request awaiting ConnectItem push
```

### Proposed State Machine

```
┌──────────────┐
│   AWAITING   │  _connectItemSent = false
│   FIRST REQ  │  _needsReconnect = false
└──────┬───────┘
       │ onPush(_in): first request
       ▼
┌─────────────────────────────────────────────┐
│  EMIT FIRST CONNECT                         │
│  1. Build TcpOptions, push ConnectItem      │
│  2. _connectItemSent = true                 │
│  3. Buffer request in _pending              │
│  4. When _outRequest pulls: push _pending   │
│  NOTE: Do NOT Complete(_outSignal)          │
└──────┬──────────────────────────────────────┘
       │
       ▼
┌────────────────────────────────────────────────────────────┐
│  CONNECTED                                                  │
│  _connectItemSent = true, _needsReconnect = false           │
│                                                             │
│  onPush(_in):                                               │
│    → Push request to _outRequest (passthrough)              │
│                                                             │
│  onPush(_inReuse):                                          │
│    → Grab ConnectionReuseItem                               │
│    → If decision.CanReuse == false:                         │
│        _needsReconnect = true                               │
│    → Pull(_inReuse)  (ready for next signal)                │
│                                                             │
└──────┬──────────────────────────────────────────────────────┘
       │ _needsReconnect becomes true (Close decision received)
       ▼
┌────────────────────────────────────────────────────────────┐
│  AWAITING RECONNECT                                         │
│  _connectItemSent = true, _needsReconnect = true            │
│                                                             │
│  onPush(_in):                                               │
│    → Build TcpOptions from new request                      │
│    → Push new ConnectItem to _outSignal                     │
│    → _needsReconnect = false                                │
│    → Buffer request in _pending                             │
│    → When _outRequest pulls: push _pending                  │
│                                                             │
│  onPush(_inReuse):                                          │
│    → Same as CONNECTED (update _needsReconnect)             │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### Pseudocode

```csharp
private sealed class Logic : GraphStageLogic
{
    private bool _connectItemSent;
    private bool _needsReconnect;
    private HttpRequestMessage? _pending;

    // --- _in handler (requests) ---
    SetHandler(_in, onPush: () =>
    {
        var request = Grab(_in);

        if (!_connectItemSent || _needsReconnect)
        {
            // First request OR reconnect needed: emit ConnectItem
            var options = TcpOptionsFactory.Build(request.RequestUri!, _clientOptions, request.Version);
            _pending = request;
            _connectItemSent = true;
            _needsReconnect = false;
            Push(_outSignal, new ConnectItem(options) { Key = RequestEndpoint.FromRequest(request) });

            // Serve pending demand on _outRequest if available
            if (IsAvailable(_outRequest))
            {
                Push(_outRequest, _pending);
                _pending = null;
            }
        }
        else
        {
            // Connection is alive: passthrough
            Push(_outRequest, request);
        }
    });

    // --- _inReuse handler (feedback from ConnectionReuseStage) ---
    SetHandler(_inReuse, onPush: () =>
    {
        var item = Grab(_inReuse);
        if (item is ConnectionReuseItem reuse && !reuse.Decision.CanReuse)
        {
            _needsReconnect = true;
        }
        Pull(_inReuse);  // Always ready for next signal
    });

    // --- _outSignal handler (ConnectItem demand) ---
    SetHandler(_outSignal, onPull: () =>
    {
        if (!_connectItemSent && !HasBeenPulled(_in))
        {
            Pull(_in);
        }
    }, onDownstreamFinish: _ => { });

    // --- _outRequest handler (request demand) ---
    SetHandler(_outRequest, onPull: () =>
    {
        if (_pending is not null)
        {
            Push(_outRequest, _pending);
            _pending = null;
        }
        else if (!HasBeenPulled(_in))
        {
            Pull(_in);
        }
    }, onDownstreamFinish: _ => CompleteStage());

    // --- PreStart: prime the feedback inlet ---
    public override void PreStart() => Pull(_inReuse);
}
```

### Proposed Wiring Change (ProtocolCoreGraphBuilder)

```csharp
// Current (line 127-131): signal goes ONLY to ConnectionStage
b.From(connReuse.Out1)
    .Via(Flow.Create<IControlItem>().Select(IOutputItem (x) => x)
        .Buffer(1, OverflowStrategy.Backpressure))
    .To(transportMerge.Preferred);

// Proposed: signal goes to BOTH ExtractOptionsStage AND ConnectionStage
// Option A: Broadcast the signal
var reuseBroadcast = b.Add(new Broadcast<IControlItem>(2));
b.From(connReuse.Out1).To(reuseBroadcast.In);
b.From(reuseBroadcast.Out(0)).To(extract.InReuse);   // feedback for reconnect
b.From(reuseBroadcast.Out(1))
    .Via(Flow.Create<IControlItem>().Select(IOutputItem (x) => x)
        .Buffer(1, OverflowStrategy.Backpressure))
    .To(transportMerge.Preferred);                    // signal to ConnectionStage
```

> **Note:** `ConnectionStage` still needs the `ConnectionReuseItem` to call `MarkConnectionNoReuse()` on the actor. Both consumers must receive the signal.

## 6. Test Scenarios

### Scenario 1: HTTP/1.0 Single Redirect (301)

```
1. Req(GET /old, v1.0) → ExtractOptions → ConnectItem + Req
2. Response: 301 Location: /new → ConnectionReuseItem(Close)
3. RedirectBidiStage re-emits Req(GET /new, v1.0)
4. ExtractOptions sees _needsReconnect=true → new ConnectItem + Req
5. Response: 200 OK → ConnectionReuseItem(Close)
6. Final response delivered
```

**Verify:** Two `ConnectItem` emissions, two TCP connections, correct redirect.

### Scenario 2: HTTP/1.0 Redirect Chain (3 hops)

```
1. Req(GET /a) → ConnectItem #1 → 301 → Close
2. Req(GET /b) → ConnectItem #2 → 302 → Close
3. Req(GET /c) → ConnectItem #3 → 307 → Close
4. Req(GET /d) → ConnectItem #4 → 200 OK → Close
```

**Verify:** Four `ConnectItem` emissions, four TCP connections, final response is 200.

### Scenario 3: HTTP/1.0 Retry (idempotent GET)

```
1. Req(GET /flaky) → ConnectItem #1 → 503 → Close
2. RetryBidiStage re-emits same request
3. Req(GET /flaky) → ConnectItem #2 → 200 OK → Close
```

**Verify:** Two `ConnectItem` emissions, retry succeeds.

### Scenario 4: HTTP/1.1 Normal (no reconnect)

```
1. Req(GET /a, v1.1) → ConnectItem → 200 OK → KeepAlive
2. Req(GET /b, v1.1) → passthrough → 200 OK → KeepAlive
3. Req(GET /c, v1.1) → passthrough → 200 OK → KeepAlive
```

**Verify:** Exactly one `ConnectItem`. Same TCP connection for all requests.

### Scenario 5: HTTP/1.1 with Connection: close (reconnect)

```
1. Req(GET /a, v1.1) → ConnectItem → 200 OK (Connection: close) → Close
2. Req(GET /b, v1.1) → ConnectItem #2 → 200 OK → KeepAlive
```

**Verify:** Two `ConnectItem` emissions. This is a valid edge case — HTTP/1.1 server can request close.

### Scenario 6: HTTP/2 (never reconnect)

```
1. Req(GET /a, v2.0) → ConnectItem → 200 OK → KeepAlive
2. Req(GET /b, v2.0) → passthrough → 200 OK → KeepAlive
```

**Verify:** Exactly one `ConnectItem`. HTTP/2 evaluator always returns `KeepAlive`.

### Scenario 7: HTTP/1.0 with Keep-Alive (no reconnect)

```
1. Req(GET /a, v1.0) → ConnectItem → 200 OK (Connection: Keep-Alive) → KeepAlive
2. Req(GET /b, v1.0) → passthrough → 200 OK (Connection: Keep-Alive) → KeepAlive
```

**Verify:** Exactly one `ConnectItem`. HTTP/1.0 opt-in keep-alive is respected.

### Scenario 8: Multiple redirects across different hosts

```
1. Req(GET host-a/old, v1.0) → ConnectItem(host-a) → 301 Location: host-b/new → Close
2. Req(GET host-b/new, v1.0) → ??? different substream (GroupByHostKey)
```

**Note:** Cross-host redirects create a new substream via `GroupByHostKey`. Each substream has its own `ExtractOptionsStage` instance. This scenario is already handled by the substream architecture — no special logic needed in `ExtractOptionsStage`.

## 7. Design Considerations

### Backpressure on `_inReuse`

The feedback inlet must not block the pipeline. Since `ConnectionReuseItem` is a control signal (not data), it should be consumed immediately via `Pull(_inReuse)` in `PreStart()` and after each `Grab`. No buffering needed — one signal per response, and the next request won't arrive until after the signal.

### Signal Ordering Guarantee

Within an Akka.Streams substream, processing is sequential. The response path (`ConnectionReuseStage`) emits the signal before the next request can enter `ExtractOptionsStage`. This is guaranteed because:
1. `RedirectBidiStage` only re-emits after processing the response (which includes the reuse signal)
2. The `Broadcast` of the reuse signal reaches `ExtractOptionsStage._inReuse` synchronously within the same fusing boundary

### `_outSignal` Outlet Lifecycle

Currently `_outSignal` connects to `Concat.In(0)`. Removing `Complete(_outSignal)` means `Concat` will not switch to `In(1)` until the signal outlet completes. This requires a wiring change: replace `Concat` with `MergePreferred` or similar, so that `ConnectItem` emissions and encoded data can both flow to the transport.

**This is a critical design consideration for TASK-025-002/003.** The `Concat` stage waits for `In(0)` to complete before reading `In(1)`. If `_outSignal` never completes, encoded request data from `BidiFlow.Outlet1` will never reach the transport.

**Solution options:**
1. **Replace Concat with MergePreferred** — `ConnectItem` on preferred input, encoded data on normal input
2. **Keep Concat, complete _outSignal after each ConnectItem batch** — re-open semantics not supported in Akka
3. **Use a custom Merge stage** that interleaves ConnectItems with data

Option 1 (MergePreferred) is the cleanest and aligns with the existing `transportMerge` pattern.

### HTTP/2 Protection

Even though `ConnectionReuseEvaluator` always returns `KeepAlive` for HTTP/2, a defensive check in `ExtractOptionsStage` could skip reconnection for HTTP/2 requests. However, since the evaluator is the single source of truth and has been extensively tested, this may be unnecessary complexity. The feature plan's FR-7 states HTTP/2 should receive exactly one `ConnectItem` — the evaluator already guarantees this.
