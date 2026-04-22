# Transport Options Self-Build Removal Design

## Problem

The transport layer (`TcpTransportStateMachine`, `QuicTransportStateMachine`) currently builds `TcpOptions` / `QuicOptions` itself at connection time via `OptionsFactory.Build(endpoint, clientOptions)`. This happens in two places per transport SM:

1. `HandleConnectItem`: `connect.Options ?? OptionsFactory.Build(connect.Key, _clientOptions)` — fallback when ConnectItem arrives with null Options
2. `AutoConnect(endpoint)`: triggered when a `NetworkBuffer` arrives without a prior `ConnectItem`; builds options from scratch

This violates the single-responsibility principle: the transport should be a pure connector, not an options builder.

## Goal

`OptionsFactory.Build` is called **only once**, in `ProtocolCoreBuilder.CreateFlowForEndpoint` — the single place where both `endpoint` and `clientOptions` are known. Pre-built options flow down to every component that needs them. Transport SMs never call `OptionsFactory`.

## Design

### 1. Engine Options — add `ConnectionOptions`

Each engine options record gains an optional `TcpOptions? ConnectionOptions` field:

```csharp
internal record Http1EngineOptions(
    int MaxPipelineDepth,
    int MaxConnectionsPerServer,
    int MaxReconnectAttempts,
    long MaxBatchWeight,
    int MaxResponseHeadersLength,
    int MaxResponseDrainSize,
    TimeSpan ResponseDrainTimeout,
    TcpOptions? ConnectionOptions = null);   // NEW

internal record Http2EngineOptions(
    ...existing fields...,
    TcpOptions? ConnectionOptions = null);   // NEW

internal sealed record Http3EngineOptions(
    ...existing fields...,
    TcpOptions? ConnectionOptions = null);   // NEW
```

Defaulting to `null` preserves backward compatibility with tests that construct `EngineOptions` directly via `ToEngineOptions()` without providing a real endpoint.

### 2. ProtocolCoreBuilder — single options build site

```csharp
Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> CreateFlowForEndpoint(RequestEndpoint endpoint)
{
    var connectionOptions = OptionsFactory.Build(endpoint, clientOptions);
    var version = endpoint.Version;
    IHttpProtocolEngine engine = version switch
    {
        { Major: 1, Minor: 0 } => new Http10Engine(http1Options with { ConnectionOptions = connectionOptions }),
        { Major: 1, Minor: 1 } => new Http11Engine(http1Options with { ConnectionOptions = connectionOptions }),
        { Major: 2, Minor: 0 } => new Http20Engine(http2Options with { ConnectionOptions = connectionOptions }),
        { Major: 3, Minor: 0 } => new Http30Engine(http3Options with { ConnectionOptions = connectionOptions }),
        _ => throw new ArgumentOutOfRangeException(...)
    };
    return engine.CreateFlow().Join(transports.Get(version));
}
```

### 3. H1 protocol SMs — emit ConnectItem on first encode

`Http10/StateMachine.cs` and `Http11/StateMachine.cs` already detect first-request via `Endpoint == default`. They now emit `ConnectItem` before the first `StreamAcquireItem`:

```csharp
if (Endpoint == default && endpoint != default)
{
    Endpoint = endpoint;
    _ops.OnOutbound(new ConnectItem { Key = endpoint, Options = _connectionOptions }); // NEW
}
```

The SM constructor accepts `TcpOptions? connectionOptions = null` stored as `_connectionOptions`.

All reconnect `ConnectItem` emissions are updated to include `Options = _connectionOptions`.

### 4. H1 stage — thread ConnectionOptions to SM

`Http10ConnectionStage` and `Http11ConnectionStage` accept `TcpOptions?` and pass it to the SM constructor. `Http10Engine` and `Http11Engine` pass `_options.ConnectionOptions` to the stage.

### 5. H2/H3 protocol SMs — populate Options in all ConnectItem emissions

H2 SM has 3 ConnectItem emissions (initial + 2 reconnect paths):
```csharp
new ConnectItem { Key = Endpoint, Options = _options.ConnectionOptions }
new ConnectItem { Key = Endpoint, IsReconnect = true, Options = _options.ConnectionOptions }
```

H3 SM has 1 initial ConnectItem emission — same pattern.

### 6. Transport SMs — remove OptionsFactory, replace AutoConnect with contract violation

```csharp
// HandleConnectItem — before:
var options = connect.Options ?? OptionsFactory.Build(connect.Key, _clientOptions);

// HandleConnectItem — after:
var options = connect.Options!;   // always non-null in production (null only in isolated tests)

// AutoConnect — converted to a guard assertion:
private void AutoConnect(RequestEndpoint endpoint)
{
    throw new InvalidOperationException(
        $"Received network output for {endpoint} without a preceding ConnectItem. " +
        "Protocol stages must emit ConnectItem before any output items.");
}
```

`_clientOptions` field and constructor parameter are removed from both `TcpTransportStateMachine` and `QuicTransportStateMachine`.

`TcpConnectionStage` and `TcpTransportFactory` stop passing `TurboClientOptions` to the SM.

## Test Impact

| Test file | Change |
|-----------|--------|
| `TcpTransportStateMachineLifecycleSpec` | Remove `TurboClientOptions` from `CreateStateMachine`; update `AutoConnect_with_different_endpoint_should_trigger_acquire` — send `ConnectItem` first, verify connect-timeout timer fires |
| `QuicTransportStateMachineLifecycleSpec` | Remove `TurboClientOptions` from `CreateStateMachine` |
| `TcpTransportStateMachineSpec`, `ErrorSpec`, `DataFlowSpec` | Remove `TurboClientOptions` from SM construction |
| `Http10StateMachineSpec` | Expect `ConnectItem` as first outbound item in first-encode tests |
| `Http11StateMachineSpec` | Same |
| `Http10ConnectionStageSpec` | Consume `ConnectItem` before `StreamAcquireItem` |
| `Http11ConnectionStageSpec` | Same |
| `Http10ConnectionStageReconnectSpec` | Reconnect `ConnectItem` now carries `Options = null` (test default) |
| `Http11ConnectionStageReconnectSpec` | Same |

## Invariants After This Change

- `OptionsFactory.Build` is called in exactly one place: `ProtocolCoreBuilder.CreateFlowForEndpoint`.
- `ConnectItem.Options` is always non-null in production flows.
- `ConnectItem.Options` may be null in isolated unit tests that construct SMs directly.
- Transport SMs are pure connectors: they accept pre-built options and never compute them.
- `AutoConnect` no longer silently creates connections — it throws to enforce the protocol contract.
