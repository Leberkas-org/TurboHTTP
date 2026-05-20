# Server StateMachine Options Threading

**Date:** 2026-05-20
**Status:** Approved
**Branch:** feature/use-config

## Goal

Thread `TurboServerOptions` through Server Engines → Stages → StateMachines following the same pattern the client side uses with `TurboClientOptions`. Wire all declared-but-unused public options properties. Correct defaults to RFC/Kestrel alignment.

## Architecture

### Current Flow (before)

```
ProtocolRouter.ResolveEngine(version, serverOptions)
  → Engine(individual params: 0–8 depending on protocol)
    → Stage(same individual params)
      → StateMachine(same individual params + hardcoded encoder/decoder opts)
```

### New Flow (after)

```
ProtocolRouter.ResolveEngine(version, serverOptions)   ← unchanged structurally
  → Engine(TurboServerOptions)                         ← simplified: one param
    → Stage(TurboServerOptions)                        ← simplified: one param
      → SM(TurboServerOptions, ops)                    ← builds encoder/decoder opts internally
```

### What changes

- Engine constructors: individual params → `TurboServerOptions`
- Stage constructors: individual params → `TurboServerOptions`
- SM constructors: individual params → `TurboServerOptions` + `IServerStageOperations`
- SMs build `SharedHttpOptions`, encoder/decoder options internally (client pattern)
- All unwired options get wired

### What stays the same

- `IServerProtocolEngine` interface and `CreateFlow()` contract
- `ProtocolRouter.ResolveEngine()` signature and selection logic
- `ListenerActor` / `ConnectionActor` structure
- `HttpConnectionServerStageLogic<TSM>` — generic over SM, unchanged
- `IServerStateMachine` interface
- `IServerStageOperations` interface
- `ServerConnectionShape`
- All encoder/decoder options record types (they just get properly populated)
- All encoder/decoder implementations

## Options Precedence

Protocol-specific options win over top-level `TurboServerOptions` properties. Top-level values serve as fallback defaults when protocol-specific options are nullable.

Example: `Http2ServerOptions.KeepAliveTimeout` (130s) overrides `TurboServerOptions.KeepAliveTimeout` for H2 connections. For HTTP/1.1, `Http1ServerOptions.KeepAliveTimeout` is nullable — when null, falls back to `TurboServerOptions.KeepAliveTimeout`.

## Default Values — RFC-first, Kestrel-fallback

### Top-level TurboServerOptions

| Property | Current | Proposed | Source |
|---|---|---|---|
| `MaxConcurrentConnections` | 0 | null (unlimited) | Kestrel |
| `KeepAliveTimeout` | 130s | 130s | Kestrel |
| `RequestHeadersTimeout` | 30s | 30s | Kestrel |
| `BodyBufferThreshold` | 4MB | 4 * 1024 * 1024 | Own concept (not same as Kestrel MaxResponseBufferSize) |

### SharedHttpOptions (built per-SM)

| Property | Default | Source |
|---|---|---|
| `MaxHeaderBytes` | 32 * 1024 | Kestrel MaxRequestHeadersTotalSize |
| `MaxHeaderCount` | 100 | Kestrel |
| `HeaderLineMaxLength` | 8 * 1024 | Reasonable |
| `RequestLineMaxLength` | 8 * 1024 | Kestrel MaxRequestLineSize |
| `StreamingThreshold` | 64 * 1024 | Own concept |
| `MaxBufferedBodySize` | 4 * 1024 * 1024 | Own concept |
| `AllowObsFold` | false | — |

### Http1ServerOptions

| Property | Current | Proposed | Source | New? |
|---|---|---|---|---|
| `MaxRequestLineLength` | 8192 | 8 * 1024 | Kestrel | No |
| `MaxRequestTargetLength` | 8192 | 8 * 1024 | — | No |
| `MaxPipelinedRequests` | 16 | 16 | — | No |
| `MaxChunkExtensionLength` | 4096 | 4 * 1024 | — | No |
| `BodyReadTimeout` | 30s | 30s | — | No |
| `MaxRequestBodySize` | hardcoded 10MB | 30_000_000 | Kestrel | **Yes** |
| `MaxHeaderListSize` | not exposed | 32 * 1024 | Kestrel | **Yes** |
| `KeepAliveTimeout` | hardcoded 120s | null (→ top-level 130s) | Kestrel | **Yes** (nullable) |
| `RequestHeadersTimeout` | hardcoded 30s | null (→ top-level 30s) | Kestrel | **Yes** (nullable) |

### Http2ServerOptions

| Property | Current | Proposed | Source | Change? |
|---|---|---|---|---|
| `MaxConcurrentStreams` | 100 | 100 | Kestrel (RFC recommends ≥100) | No |
| `InitialConnectionWindowSize` | 65,535 | 1 * 1024 * 1024 | Kestrel | **Fix** |
| `InitialStreamWindowSize` | 65,535 | 768 * 1024 | Kestrel | **Fix** |
| `MaxFrameSize` | 16,384 | 16 * 1024 | RFC 9113 §6.5.2 + Kestrel | No |
| `MaxHeaderListSize` | 8,192 | 32 * 1024 | Kestrel | **Fix** |
| `MaxRequestBodySize` | 30 * 1024 * 1024 | 30_000_000 | Kestrel | **Fix** |
| `MaxResponseBufferSize` | 1 * 1024 * 1024 | 64 * 1024 | Kestrel | **Fix** |
| `HeaderTableSize` | hardcoded 64KB | 4 * 1024 | RFC 7541 §4.2 + Kestrel | **New + Fix** |
| `KeepAliveTimeout` | 130s | 130s | Kestrel | No |
| `RequestHeadersTimeout` | 30s | 30s | Kestrel | No |
| `MinRequestBodyDataRate` | 240 | 240 | Kestrel | No |
| `MinRequestBodyDataRateGracePeriod` | 5s | 5s | Kestrel | No |

### Http3ServerOptions

| Property | Current | Proposed | Source | Change? |
|---|---|---|---|---|
| `MaxConcurrentStreams` | 100 | 100 | QUIC transport parameter | No |
| `MaxHeaderListSize` | 8,192 | 32 * 1024 | Kestrel | **Fix** |
| `MaxRequestBodySize` | 30 * 1024 * 1024 | 30_000_000 | Kestrel | **Fix** |
| `QpackMaxTableCapacity` | hardcoded 4KB | 0 | RFC 9204 (0 = no dynamic table) | **New + Fix** |
| `EnableWebTransport` | false | false | — | No |
| `KeepAliveTimeout` | 130s | 130s | Kestrel | No |
| `RequestHeadersTimeout` | 30s | 30s | Kestrel | No |
| `MinRequestBodyDataRate` | 240 | 240 | Kestrel | No |
| `MinRequestBodyDataRateGracePeriod` | 5s | 5s | Kestrel | No |

## Options Mapping per Protocol

### SharedHttpOptions Construction (in each SM)

| SharedHttpOptions field | H1 source | H2 source | H3 source |
|---|---|---|---|
| `MaxBufferedBodySize` | `options.BodyBufferThreshold` | `options.BodyBufferThreshold` | `options.BodyBufferThreshold` |
| `MaxStreamedBodySize` | `options.Http1.MaxRequestBodySize` | `options.Http2.MaxRequestBodySize` | `options.Http3.MaxRequestBodySize` |
| `MaxHeaderBytes` | `options.Http1.MaxHeaderListSize` | `options.Http2.MaxHeaderListSize` | `options.Http3.MaxHeaderListSize` |
| `MaxHeaderCount` | default 100 | default 100 | default 100 |
| `HeaderLineMaxLength` | `options.Http1.MaxRequestLineLength` | default 8KB | default 8KB |
| `RequestLineMaxLength` | `options.Http1.MaxRequestLineLength` | default 8KB | default 8KB |
| `AllowObsFold` | default false | default false | default false |
| `BufferPool` | `MemoryPool<byte>.Shared` | `MemoryPool<byte>.Shared` | `MemoryPool<byte>.Shared` |
| `StreamingThreshold` | default 64KB | default 64KB | default 64KB |

### HTTP/1.0 Encoder/Decoder Options

**Http10ServerDecoderOptions:**
- `Shared` ← built from TurboServerOptions (see table above)

**Http10ServerEncoderOptions:**
- `Shared` ← built from TurboServerOptions
- `WriteDateHeader` = true (unchanged)

**Direct SM usage:**
- `maxRequestBodySize` ← `options.Http1.MaxRequestBodySize`
- `requestHeadersTimeout` ← `options.RequestHeadersTimeout` (top-level fallback; no HTTP/1.0 keep-alive)

### HTTP/1.1 Encoder/Decoder Options

**Http11ServerDecoderOptions:**
- `Shared` ← built from TurboServerOptions
- `MaxPipelinedRequests` ← `options.Http1.MaxPipelinedRequests`

**Http11ServerEncoderOptions:**
- `Shared` ← built from TurboServerOptions
- `WriteDateHeader` = true (unchanged)
- `KeepAliveTimeout` ← `options.Http1.KeepAliveTimeout ?? options.KeepAliveTimeout`
- `RequestHeadersTimeout` ← `options.Http1.RequestHeadersTimeout ?? options.RequestHeadersTimeout`

**Direct SM usage:**
- `maxRequestBodySize` ← `options.Http1.MaxRequestBodySize`
- `bodyReadTimeout` ← `options.Http1.BodyReadTimeout`

### HTTP/2 Encoder/Decoder Options

**Http2ServerDecoderOptions:**
- `Shared` ← built from TurboServerOptions
- `MaxConcurrentStreams` ← `options.Http2.MaxConcurrentStreams`
- `MaxFieldSectionSize` ← `options.Http2.MaxHeaderListSize`

**Http2ServerEncoderOptions:**
- `Shared` ← built from TurboServerOptions
- `WriteDateHeader` = true (unchanged)
- `HeaderTableSize` ← `options.Http2.HeaderTableSize`
- `MaxFrameSize` ← `options.Http2.MaxFrameSize`

**Direct SM / SessionManager usage:**
- `initialConnectionWindowSize` ← `options.Http2.InitialConnectionWindowSize`
- `initialStreamWindowSize` ← `options.Http2.InitialStreamWindowSize`
- `maxRequestBodySize` ← `options.Http2.MaxRequestBodySize`
- `keepAliveTimeout` ← `options.Http2.KeepAliveTimeout`
- `requestHeadersTimeout` ← `options.Http2.RequestHeadersTimeout`
- `minBodyDataRate` ← `options.Http2.MinRequestBodyDataRate`
- `bodyRateGracePeriod` ← `options.Http2.MinRequestBodyDataRateGracePeriod`
- `maxResponseBufferSize` ← `options.Http2.MaxResponseBufferSize`

### HTTP/3 Encoder/Decoder Options

**Http3ServerDecoderOptions:**
- `Shared` ← built from TurboServerOptions
- `MaxConcurrentStreams` ← `options.Http3.MaxConcurrentStreams`
- `MaxFieldSectionSize` ← `options.Http3.MaxHeaderListSize`

**Http3ServerEncoderOptions:**
- `Shared` ← built from TurboServerOptions
- `WriteDateHeader` = true (unchanged)
- `QpackMaxTableCapacity` ← `options.Http3.QpackMaxTableCapacity`

**Direct SM / SessionManager usage:**
- `maxRequestBodySize` ← `options.Http3.MaxRequestBodySize`
- `keepAliveTimeout` ← `options.Http3.KeepAliveTimeout`
- `requestHeadersTimeout` ← `options.Http3.RequestHeadersTimeout`
- `minBodyDataRate` ← `options.Http3.MinRequestBodyDataRate`
- `bodyRateGracePeriod` ← `options.Http3.MinRequestBodyDataRateGracePeriod`

## New Properties on Public Options

### Http1ServerOptions (4 new properties)

```csharp
public long MaxRequestBodySize { get; set; } = 30_000_000;
public int MaxHeaderListSize { get; set; } = 32 * 1024;
public TimeSpan? KeepAliveTimeout { get; set; }        // null → top-level fallback
public TimeSpan? RequestHeadersTimeout { get; set; }   // null → top-level fallback
```

### Http2ServerOptions (1 new property)

```csharp
public int HeaderTableSize { get; set; } = 4 * 1024;   // RFC 7541 §4.2
```

### Http3ServerOptions (1 new property)

```csharp
public int QpackMaxTableCapacity { get; set; } = 0;     // RFC 9204
```

## Files to Modify

| Layer | File | Change |
|---|---|---|
| **Public Options** | `Server/Http1ServerOptions.cs` | Add 4 new properties |
| | `Server/Http2ServerOptions.cs` | Add `HeaderTableSize`; fix 5 defaults |
| | `Server/Http3ServerOptions.cs` | Add `QpackMaxTableCapacity`; fix 2 defaults |
| **Engines** | `Streams/Http10ServerEngine.cs` | Constructor → `TurboServerOptions` |
| | `Streams/Http11ServerEngine.cs` | Constructor → `TurboServerOptions` |
| | `Streams/Http20ServerEngine.cs` | 8 params → `TurboServerOptions` |
| | `Streams/Http30ServerEngine.cs` | 5 params → `TurboServerOptions` |
| **Stages** | `Streams/Stages/Server/Http10ServerConnectionStage.cs` | Constructor → `TurboServerOptions` |
| | `Streams/Stages/Server/Http11ServerConnectionStage.cs` | encoder/decoder opts → `TurboServerOptions` |
| | `Streams/Stages/Server/Http20ServerConnectionStage.cs` | 8 params → `TurboServerOptions` |
| | `Streams/Stages/Server/Http30ServerConnectionStage.cs` | 5 params → `TurboServerOptions` |
| **StateMachines** | `Protocol/Syntax/Http10/Server/Http10ServerStateMachine.cs` | `(ops, maxBody)` → `(TurboServerOptions, ops)` |
| | `Protocol/Syntax/Http11/Server/Http11ServerStateMachine.cs` | `(ops, enc?, dec?)` → `(TurboServerOptions, ops)` |
| | `Protocol/Syntax/Http2/Server/Http2ServerStateMachine.cs` | `(ops, 11 params)` → `(TurboServerOptions, ops)` |
| | `Protocol/Syntax/Http3/Server/Http3ServerStateMachine.cs` | `(ops, 5 params)` → `(TurboServerOptions, ops)` |
| **ProtocolRouter** | `Streams/ProtocolRouter.cs` | Simplify engine constructor calls |
| **Tests** | All server SM test files | Use `TurboServerOptions` instead of individual params |

## Files Unchanged

- `IServerProtocolEngine` — interface stays
- `IServerStateMachine` — interface stays
- `IServerStageOperations` — interface stays
- `ServerConnectionShape` — shape stays
- `HttpConnectionServerStageLogic<TSM>` — generic logic stays
- All `Http*Server{Encoder,Decoder}Options` record types — unchanged
- All `Http*Server{Encoder,Decoder}` implementations — unchanged

## Test Impact

Tests instantiate StateMachines directly. After refactor they use `TurboServerOptions`:

```csharp
// Before
var sm = new Http2ServerStateMachine(ops, maxConcurrentStreams: 50);

// After
var options = new TurboServerOptions();
options.Http2.MaxConcurrentStreams = 50;
var sm = new Http2ServerStateMachine(options, ops);
```

Tests that use only defaults (`new Http2ServerStateMachine(ops)`) become `new Http2ServerStateMachine(new TurboServerOptions(), ops)`.

No tests instantiate Engines or ConnectionStages directly — Engine/Stage changes are test-invisible.
