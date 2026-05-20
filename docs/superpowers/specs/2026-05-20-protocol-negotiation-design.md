# Dynamic Protocol Negotiation Design

## Problem

Protocol routing is currently static per listener. `ListenerActor.ResolveEngineForListener()` picks a single `IServerProtocolEngine` based on `TcpListenerOptions.ApplicationProtocols[0]` — the first configured ALPN, not the actually negotiated one. Users who want HTTP/1.1 and HTTP/2 on the same port get whichever protocol was listed first in config.

Additionally:
- `TcpListenerStage` does not extract `SslStream.NegotiatedApplicationProtocol` into `SecurityInfo` after the TLS handshake
- Cleartext HTTP/2 (h2c) via connection preface detection is not supported
- HTTP/1.1 → HTTP/2 upgrade (`Upgrade: h2c`) is not supported

## Goals

1. Per-connection protocol selection based on ALPN negotiation result (TLS) or connection preface sniffing (cleartext)
2. Support cleartext HTTP/2 via prior knowledge (RFC 9113 §3.3 — connection preface detection)
3. Support HTTP/1.1 → HTTP/2 upgrade via `Upgrade: h2c` (RFC 9113 §3.2) with pipeline rematerialization semantics inside the stage
4. Zero performance overhead for the TLS/ALPN case (production default)
5. No changes to `HttpConnectionServerStageLogic`, `ConnectionActor`, or existing protocol StateMachines (except H1.1 for upgrade)

## Non-Goals

- HTTP/3 negotiation (QUIC is always HTTP/3 by definition — no negotiation needed)
- Alt-Svc based protocol advertisement (separate feature)
- WebSocket upgrade (different mechanism, separate feature)

## Architecture

### Core Insight

The existing architecture already separates concerns cleanly:

- `HttpConnectionServerStageLogic<TSM>` — generic, protocol-agnostic stage orchestrator
- `IServerStateMachine` — protocol-specific contract (H1.0, H1.1, H2, H3 each implement this)
- `Http*ServerConnectionStage` — trivial boilerplate GraphStage wrappers (differ only in SM type)

Protocol negotiation fits naturally as a new `IServerStateMachine` implementation that handles detection and delegates to the appropriate real SM.

### New Components

#### 1. `ProtocolNegotiatingStateMachine` (implements `IServerStateMachine`)

Location: `Protocol/ProtocolNegotiatingStateMachine.cs`

A state machine with three phases:

```
[WaitingForConnect] → TransportConnected with ALPN    → [Running(selected SM)]
[WaitingForConnect] → TransportConnected without ALPN → [Sniffing]
[Sniffing]          → First TransportData bytes       → [Running(selected SM)]
[Running(H1.1)]     → h2c Upgrade detected            → [Running(H2)]
```

**WaitingForConnect:**
- Receives `TransportConnected` via `DecodeClientData()`
- Checks `ConnectionInfo.Security?.ApplicationProtocol`
- ALPN `h2` → create `Http2ServerStateMachine`, transition to Running, forward message
- ALPN `http/1.1` or default → create `Http11ServerStateMachine`, transition to Running, forward message
- No security info (cleartext) → buffer `TransportConnected`, transition to Sniffing

**HTTP/1.0 note:** The negotiator never creates `Http10ServerStateMachine` directly. HTTP/1.0 uses the same wire format as HTTP/1.1 — the version is only distinguishable in the request line, not at connection level. `Http11ServerStateMachine` already handles HTTP/1.0 requests correctly (detects the version and sets `ShouldComplete` to close after the response).

**Sniffing:**
- Receives `TransportData` via `DecodeClientData()`
- Buffers the message
- Checks first 4 bytes against `"PRI "` (ASCII `0x50 0x52 0x49 0x20`)
- Match → create `Http2ServerStateMachine`, transition to Running, replay all buffered messages
- No match → create `Http11ServerStateMachine`, transition to Running, replay all buffered messages
- Insufficient data (< 4 bytes) → stay in Sniffing, accumulate

**Running:**
- All `IServerStateMachine` method calls delegate directly to `_inner`
- For h2c Upgrade: see `IProtocolSwitchCapable` below

**IServerStateMachine delegation in Running state:**

```csharp
bool CanAcceptResponse => _inner!.CanAcceptResponse;
bool ShouldComplete => _inner!.ShouldComplete;
void PreStart() => _inner!.PreStart();
void OnResponse(HttpResponseMessage response) => _inner!.OnResponse(response);
void DecodeClientData(ITransportInbound data) => _inner!.DecodeClientData(data);
void OnDownstreamFinished() => _inner!.OnDownstreamFinished();
void OnTimerFired(string name) => _inner!.OnTimerFired(name);
void OnBodyMessage(object msg) => _inner!.OnBodyMessage(msg);
void Cleanup() => _inner!.Cleanup();
```

Steady-state overhead: one interface virtual dispatch per call (~1ns).

#### 2. `IProtocolSwitchCapable` (new interface)

Location: `Protocol/IProtocolSwitchCapable.cs`

```csharp
internal interface IProtocolSwitchCapable
{
    void RequestProtocolSwitch(
        Func<IServerStageOperations, IServerStateMachine> newSmFactory);
}
```

Opt-in interface. Only implemented by the wrapped `IServerStageOperations` that `ProtocolNegotiatingStateMachine` provides to its inner SM.

#### 3. `UpgradeAwareOps` (private nested class in `ProtocolNegotiatingStateMachine`)

Wraps the real `IServerStageOperations` and implements both `IServerStageOperations` and `IProtocolSwitchCapable`:

- All `IServerStageOperations` methods delegate to the real ops
- `RequestProtocolSwitch()` calls back to `ProtocolNegotiatingStateMachine.HandleUpgrade()`

This wrapper is passed to inner SMs instead of the real ops. When an inner SM (H1.1) detects `Upgrade: h2c`:

1. It checks `_ops is IProtocolSwitchCapable` — true only when running under the negotiator
2. It encodes and emits the `101 Switching Protocols` response via `_ops.OnOutbound()`
3. It calls `switchable.RequestProtocolSwitch(ops => new Http2ServerStateMachine(options, ops))`

When used directly (existing `Http11ServerConnectionStage`), the check fails and the upgrade header is ignored. Zero impact on existing code paths.

#### 4. `ProtocolNegotiatorConnectionStage` (GraphStage wrapper)

Location: `Streams/Stages/Server/ProtocolNegotiatorConnectionStage.cs`

Identical boilerplate to the four existing connection stages:

```csharp
internal sealed class ProtocolNegotiatorConnectionStage : GraphStage<ServerConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inNetwork = new("NegotiatorConnection.In.Network");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("NegotiatorConnection.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("NegotiatorConnection.In.Response");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("NegotiatorConnection.Out.Network");
    private readonly TurboServerOptions _options;

    public ProtocolNegotiatorConnectionStage(TurboServerOptions options) => _options = options;

    public override ServerConnectionShape Shape => new(_inNetwork, _outRequest, _inResponse, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new HttpConnectionServerStageLogic<ProtocolNegotiatingStateMachine>(this,
            ops => new ProtocolNegotiatingStateMachine(_options, ops));
}
```

#### 5. `NegotiatingServerEngine` (implements `IServerProtocolEngine`)

Location: `Streams/NegotiatingServerEngine.cs`

Identical pattern to the four existing engines, wrapping `ProtocolNegotiatorConnectionStage`.

### Modified Components

#### 1. `TcpListenerStage` (Servus.Akka) — ALPN extraction fix

After `sslStream.AuthenticateAsServerAsync()`, extract the negotiated ALPN:

```csharp
var security = new SecurityInfo(
    sslStream.SslProtocol,
    sslStream.NegotiatedApplicationProtocol);

var connectionInfo = new ConnectionInfo(
    localEndPoint, remoteEndPoint,
    TransportProtocol.Tls,
    security);
```

This aligns TCP behavior with QUIC (`QuicListenerStage` already does this correctly).

#### 2. `Http11ServerStateMachine` — h2c Upgrade detection

In `DecodeClientData()`, after parsing a complete request:

- Check for `Upgrade: h2c` header + `HTTP2-Settings` header + `Connection: Upgrade, HTTP2-Settings`
- If present and `_ops is IProtocolSwitchCapable switchable`:
  - Encode `HTTP/1.1 101 Switching Protocols` with `Connection: Upgrade` and `Upgrade: h2c` headers
  - Emit via `_ops.OnOutbound()`
  - Call `switchable.RequestProtocolSwitch(ops => new Http2ServerStateMachine(_options, ops))`
- If `_ops` is not `IProtocolSwitchCapable`: ignore the Upgrade header, process request normally

#### 3. `ProtocolRouter` — new entry point

```csharp
// Existing overloads remain unchanged for backward compatibility (direct version/ALPN-based selection)
internal static IServerProtocolEngine ResolveEngine(Version version, TurboServerOptions options) { ... }
internal static IServerProtocolEngine ResolveEngine(SslApplicationProtocol protocol, TurboServerOptions options) { ... }

// New: negotiating engine for dynamic per-connection protocol selection
internal static IServerProtocolEngine ResolveNegotiating(TurboServerOptions options)
    => new NegotiatingServerEngine(options);
```

Existing overloads are preserved for use cases that need direct protocol selection (e.g., tests, explicit version pinning). The core server path uses `ResolveNegotiating()` exclusively for TCP.

#### 4. `ListenerActor.ResolveEngineForListener()`

```csharp
private IServerProtocolEngine ResolveEngineForListener()
{
    if (_listenerOptions is QuicListenerOptions)
    {
        return ProtocolRouter.ResolveEngine(new Version(3, 0), _serverOptions);
    }

    return ProtocolRouter.ResolveNegotiating(_serverOptions);
}
```

All TCP connections (TLS and cleartext) use the negotiating engine. QUIC remains static (always HTTP/3).

### Unchanged Components

- `HttpConnectionServerStageLogic<TSM>` — no changes
- `ConnectionActor` — no changes
- `Http10ServerStateMachine` — no changes
- `Http2ServerStateMachine` — no changes
- `Http3ServerStateMachine` — no changes
- `Http10/11/20/30 ServerConnectionStage` — no changes (remain usable for direct protocol selection)
- `Http10/11/20/30 ServerEngine` — no changes (remain usable for explicit version routing)

## Data Flow

### Case 1: TLS with ALPN (production default — zero overhead)

```
TransportConnected(Security.ApplicationProtocol = "h2")
  → ProtocolNegotiatingStateMachine [WaitingForConnect]
  → ALPN recognized → create Http2ServerStateMachine
  → State → Running
  → Forward TransportConnected to _inner
  → All subsequent calls: direct delegation
```

Buffering: 0 bytes. Detection cost: 1 property check.

### Case 2: Cleartext h2c with prior knowledge

```
TransportConnected(Security = null)
  → [WaitingForConnect] → buffer, State → Sniffing
TransportData("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n...")
  → [Sniffing] → first 4 bytes = "PRI " → create Http2ServerStateMachine
  → State → Running
  → Replay: TransportConnected + TransportData to _inner
```

Buffering: 1 TransportConnected + 1 TransportData. Detection cost: 4-byte comparison.

### Case 3: Cleartext HTTP/1.1

```
TransportConnected(Security = null)
  → [WaitingForConnect] → buffer, State → Sniffing
TransportData("GET / HTTP/1.1\r\n...")
  → [Sniffing] → first 4 bytes ≠ "PRI " → create Http11ServerStateMachine
  → State → Running
  → Replay: TransportConnected + TransportData to _inner
```

Detection cost: 4-byte comparison. Decision after first TransportData — no additional buffering.

### Case 4: h2c Upgrade (rare)

```
[Cleartext → Sniffing → "GET " → Http11ServerStateMachine → Running]
  → H1.1 SM parses request with "Upgrade: h2c"
  → _ops is IProtocolSwitchCapable → true
  → H1.1 SM emits 101 response via _ops.OnOutbound()
  → H1.1 SM calls switchable.RequestProtocolSwitch(Http2SM factory)
  → ProtocolNegotiatingStateMachine.HandleUpgrade():
      _inner.Cleanup()
      _inner = new Http2ServerStateMachine(options, wrappedOps)
      _inner.PreStart()
  → Running(Http2ServerStateMachine)
  → Client sends HTTP/2 connection preface
  → H2 SM processes normally
```

## Performance Summary

| Case | Detection Cost | Buffering | Steady-State Overhead |
|------|---------------|-----------|-----------------------|
| TLS + ALPN | 1 property check | 0 | 1 virtual dispatch/call |
| h2c Preface | 4-byte compare | ~1 TransportData | 1 virtual dispatch/call |
| Cleartext H1.1 | 4-byte compare | ~1 TransportData | 1 virtual dispatch/call |
| h2c Upgrade | 0 (H1.1 SM detects) | 0 (SM swap) | 1 virtual dispatch/call |

The steady-state overhead of 1 virtual dispatch per `IServerStateMachine` method call is ~1ns — negligible compared to actual protocol processing (microseconds).

## Testing Strategy

### Unit Tests (no Akka infrastructure)

**`ProtocolNegotiatingStateMachineSpec`** (folder: `Protocol/`):
- ALPN `h2` → creates `Http2ServerStateMachine`
- ALPN `http/1.1` → creates `Http11ServerStateMachine`
- ALPN default/unknown → creates `Http11ServerStateMachine` (fallback)
- Cleartext + preface `"PRI "` → creates `Http2ServerStateMachine`, replays buffered messages
- Cleartext + preface `"GET "` → creates `Http11ServerStateMachine`, replays buffered messages
- Cleartext + preface `"POST"` → creates `Http11ServerStateMachine`
- Insufficient data (< 4 bytes) → stays in Sniffing until more data
- h2c Upgrade → H1.1 → 101 emitted → swap to H2 SM
- Cleanup() in each state disposes buffered data

**`Http11UpgradeH2cSpec`** (folder: `Protocol/Syntax/Http11/Server/`):
- Upgrade header present + IProtocolSwitchCapable → 101 + switch
- Upgrade header present + not IProtocolSwitchCapable → normal request processing
- Malformed HTTP2-Settings → reject upgrade, process normally
- Missing Connection header → reject upgrade

### Stage Tests (Akka TestKit)

**`ProtocolNegotiatorConnectionStageSpec`** (folder: `Stages/`):
- TLS ALPN h2 → H2 framing works end-to-end
- TLS ALPN http/1.1 → H1.1 framing works end-to-end
- Cleartext H2 preface → H2 framing works
- Cleartext H1.1 → H1.1 framing works

### Integration Tests

**`H2cUpgradeSpec`** (folder: `IntegrationTests/`):
- Full TCP connection → H1.1 request with Upgrade: h2c → 101 → H2 framing → response on stream 1

### Existing Tests

All existing SM tests (`Http10/11/20/30 ServerStateMachineSpec`) remain unchanged and continue to pass — the SMs themselves are not modified (except H1.1 for upgrade detection).
