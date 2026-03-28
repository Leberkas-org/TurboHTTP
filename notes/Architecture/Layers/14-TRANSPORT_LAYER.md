---
title: Transport Layer
description: >-
  Actor-free connection pool, Channels-based I/O, TCP/TLS/QUIC transport, and
  backpressure model
tags:
  - architecture
  - transport
  - connection-pool
  - channels
  - tcp
  - quic
---
# Transport Layer

The Transport Layer manages physical network connections — TCP sockets, TLS streams, and QUIC endpoints. It is **actor-free by design**: connection lifecycle is managed through `System.Threading.Channels` and `System.IO.Pipelines` instead of Akka actors, reducing overhead and simplifying the concurrency model.

> **Scope**: This note covers connection management and byte-level I/O. For protocol framing (HTTP/1.x, HTTP/2, HTTP/3), see [[Architecture/Layers/16-PROTOCOL_LAYER|Protocol Layer]].

## Purpose

- Establish and manage per-host TCP/TLS/QUIC connections
- Provide version-aware connection pooling (HTTP/1.0 no-reuse, HTTP/1.1 keep-alive, HTTP/2 multiplexing)
- Bridge Akka.Streams `GraphStage` I/O with raw network streams via `Channel<T>`
- Handle backpressure between the pipeline and the network

## Key Files

| File | Purpose |
|------|---------|
| `src/TurboHttp/Transport/ConnectionPool.cs` | Thread-safe per-host pool: `AcquireAsync`/`Release` API |
| `src/TurboHttp/Transport/ConnectionLease.cs` | Single connection lease with busy/idle state, stream count, lifetime tracking |
| `src/TurboHttp/Transport/ConnectionStage.cs` | Unified `GraphStage` bridging pipeline ↔ transport; delegates to `ITransportHandler` |
| `src/TurboHttp/Transport/ITransportHandler.cs` | Strategy interface: `TcpTransportHandler` (TCP) or `QuicTransportHandler` (QUIC) |
| `src/TurboHttp/Transport/TcpTransportHandler.cs` | TCP/TLS single-stream handler |
| `src/TurboHttp/Transport/QuicTransportHandler.cs` | QUIC multi-stream handler |
| `src/TurboHttp/Transport/QuicConnectionManager.cs` | QUIC connection + stream lifecycle management |
| `src/TurboHttp/Transport/ClientState.cs` | Per-connection state: inbound/outbound `Channel<T>`, `Pipe`, stream direction |
| `src/TurboHttp/Transport/ClientByteMover.cs` | Async read/write pump between `Stream` and `Channel<T>` |
| `src/TurboHttp/Transport/DirectConnectionFactory.cs` | Establishes new TCP/TLS/QUIC connections |
| `src/TurboHttp/Transport/TcpOptionsFactory.cs` | Builds `TcpOptions`/`TlsOptions`/`QuicOptions` from URI + client config |
| `src/TurboHttp/Pooling/ConnectionHandle.cs` | Bundles Channel read/write handles for direct TCP I/O |
| `src/TurboHttp/Internal/Messages.cs` | Pipeline message types: `DataItem`, `ConnectItem`, `CloseSignalItem`, etc. |

## Data Flow

```text
Pipeline (IOutputItem)                          Network
       │                                           │
       ▼                                           │
┌─────────────────┐                                │
│ ConnectionStage │ ──── ITransportHandler ─────── │
│  (GraphStage)   │     ┌─────────────────────┐    │
│                 │     │ TcpTransportHandler  │   │
│  Dispatches:    │     │   or                 │   │
│  ConnectItem    │     │ QuicTransportHandler │   │
│  DataItem       │     └────────┬────────────┘    │
│  ControlItem    │              │                 │
└────────┬────────┘              ▼                 │
         │              ┌──────────────────┐       │
         │              │   ClientState    │       │
         │              │ ┌──────────────┐ │       │
         │              │ │OutboundWriter│─┼──►  Stream.WriteAsync()
         │              │ └──────────────┘ │       │
         │              │ ┌──────────────┐ │       │
         ◄──────────────┼─│InboundReader │◄┼────  Stream.ReadAsync()
         │              │ └──────────────┘ │       │
      (IInputItem)      │ ┌──────────────┐ │       │
                        │ │   Pipe       │ │  (reassembly buffer)
                        │ └──────────────┘ │       │
                        └──────────────────┘
```

### Message Types

Items flow between the pipeline and transport via marker interfaces:

| Interface | Direction | Examples |
|-----------|-----------|---------|
| `IOutputItem` | Pipeline → Network | `DataItem`, `ConnectItem`, `ConnectionReuseItem` |
| `IInputItem` | Network → Pipeline | `DataItem`, `CloseSignalItem` |
| `IControlItem` | Pipeline → Network (non-data) | `ConnectItem`, `MaxConcurrentStreamsItem`, `StreamAcquireItem` |

## Connection Pool Design

### Version-Aware Strategy

```text
┌──────────────────────────────────────────────────┐
│                ConnectionPool                    │
│  ConcurrentDictionary<RequestEndpoint, HostConns>│
│                                                  │
│  HTTP/1.0: Always new (no reuse)                 │
│  HTTP/1.1: Idle queue, 6 per host (RFC 9112 §9.4)│
│  HTTP/2+:  MRU multiplexing, unlimited slots     │
└──────────────────────────────────────────────────┘
```

| Version | Acquire Strategy | Release Strategy | Limit |
|---------|-----------------|------------------|-------|
| HTTP/1.0 | Always `EstablishAndTrack` | Always dispose | None |
| HTTP/1.1 | Try idle queue → wait semaphore → establish | Reusable → idle queue; else dispose + release semaphore | 6/host |
| HTTP/2+ | MRU with available stream slots → establish | Decrement streams; dispose when 0 streams + non-reusable | Unlimited |

### Idle Eviction

A `Timer` runs at `_idleTimeout` intervals calling `EvictIdle()`. Stale connections are disposed but **at least one connection per host is always kept** to avoid cold-start latency.

### RequestEndpoint as Pool Key

Connections are grouped by `(Scheme, Host, Port, Version)` — a `readonly record struct` with case-insensitive host/scheme comparison. This ensures HTTP/1.1 and HTTP/2 connections to the same host are pooled separately.

## Channels-Based I/O (Actor-Free)

### Why Not Actors?

Traditional Akka.NET patterns use actors for connection management. TurboHttp deliberately avoids this:

1. **Lower overhead** — `Channel<T>` has zero allocation for unbounded writes vs actor mailbox message wrapping
2. **Simpler debugging** — no actor hierarchy to trace; standard async/await stack traces
3. **Direct backpressure** — `Channel.Writer.WaitToWriteAsync` maps naturally to TCP flow control
4. **Compatibility** — `System.IO.Pipelines.Pipe` provides zero-copy buffer management aligned with .NET runtime optimizations

### ClientState: The Connection Bundle

Each connection is represented by a `ClientState` containing:
- **Inbound Channel** — network reads → pipeline consumption
- **Outbound Channel** — pipeline writes → network sends
- **Pipe** — `System.IO.Pipelines.Pipe` for reassembling partial reads into protocol frames
- **Stream Direction** — `Bidirectional`, `ReadOnly`, or `WriteOnly` (QUIC unidirectional streams)

Buffer sizing scales with `MaxFrameSize`:
- ≤128KB → 512KB pause threshold
- ≤1MB → 2MB pause threshold
- &gt;1MB → 2× max frame size

### ClientByteMover: The Async Pump

`ClientByteMover` runs two async loops per connection:
1. **Read pump**: `Stream.ReadAsync()` → `Pipe.Writer` → `InboundChannel.Writer`
2. **Write pump**: `OutboundChannel.Reader` → `Stream.WriteAsync()`

On read completion, it sets `ClientState.CloseKind` to distinguish clean TLS `close_notify` from abrupt TCP RST — this signal propagates as `CloseSignalItem` so decoders know whether partial responses are valid (RFC 9112 §9.8).

## ConnectionStage: The Bridge

`ConnectionStage` is a `GraphStage<FlowShape<IOutputItem, IInputItem>>` that sits between the protocol engine and the network. It takes an `IConnectionScope` for connection lifecycle management:

1. Receives the first `ConnectItem` and lazily creates an `ITransportHandler` (TCP or QUIC based on options type)
2. Routes `DataItem` writes to the outbound channel
3. **Auto-reconnect**: when `DataItem` arrives with `_handle == null` (HTTP/1.0, or HTTP/1.1 after Connection: close), acquires a new connection via `scope.AcquireAsync()` using stored options
4. Pumps inbound channel reads as `IInputItem` downstream
5. Handles max-concurrent-streams updates and stream acquire requests
6. Manages connect timeouts via `TimerGraphStageLogic`
7. **Transport callback**: `TcpTransportHandler` registers `OnTransportReturned` via `scope.RegisterTransportCallback()` — called by `ConnectionReuseFlowStage` after response evaluation

### Handler Strategy Pattern

```text
ConnectionStage delegates to:
  ├── TcpTransportHandler  (HTTP/1.x, HTTP/2)
  │     └── Single bidirectional Stream
  └── QuicTransportHandler (HTTP/3)
        └── Multiple uni/bidirectional QUIC streams
              ├── Control stream (SETTINGS, GOAWAY)
              ├── QPACK encoder stream
              └── Request streams (per-request)
```

## IConnectionScope: Protocol-Aware Connection Lifecycle

`IConnectionScope` abstracts protocol-specific connection lifecycle (acquire, use, return) so the pipeline doesn't need protocol-aware branches:

```text
┌─ Per-host substream (GroupByHostKey) ──────────────────────────┐
│                                                                │
│   IConnectionScope (shared within fused substream actor)       │
│     AcquireAsync() ←── ConnectionStage (first data / reconnect)│
│     ReturnAsync()  ←── ConnectionReuseFlowStage (on response)  │
│     RegisterTransportCallback(Action<bool>) ──→ TcpTransportHandler │
│                                                                │
│   SingleRequestConnectionScope (HTTP/1.0):                     │
│     Always new connection, always close                        │
│   PersistentConnectionScope (HTTP/1.1+):                       │
│     Reuse if keep-alive, close on Connection: close            │
└────────────────────────────────────────────────────────────────┘
```

**Key files:**
| File | Purpose |
|------|---------|
| `src/TurboHttp/Transport/ConnectionScope/IConnectionScope.cs` | Interface: Acquire, Return, CanReuse, Cleanup, transport callback |
| `src/TurboHttp/Transport/ConnectionScope/SingleRequestConnectionScope.cs` | HTTP/1.0: always new connection |
| `src/TurboHttp/Transport/ConnectionScope/PersistentConnectionScope.cs` | HTTP/1.1+: reuse when keep-alive |
| `src/TurboHttp/Transport/ConnectionScope/DeferredConnectionScope.cs` | Factory: defers scope creation until first request provides TcpOptions |

**Signal flow:** `ConnectionReuseFlowStage` calls `scope.ReturnAsync(canReuse)` → scope invokes registered callback → `TcpTransportHandler.OnTransportReturned(canReuse)` does cleanup (stop pump, clear handle, increment gen). All synchronous within the fused actor — no graph edges needed.

## Known Limitations

- **No connection prewarming** — connections are established on first request, not proactively
- **No DNS refresh** — `RequestEndpoint` caches the resolved host; DNS TTL changes require new connections
- **QUIC multi-stream complexity** — `QuicConnectionManager` handles stream multiplexing but the `Http3TaggedItem`/`Http3InputTaggedItem` routing adds indirection

## Integration Points

| Component | Interaction |
|-----------|-------------|
| [[Architecture/Layers/15-STREAMS_LAYER|Streams Layer]] | `ConnectionStage` is wired into `ProtocolCoreGraphBuilder` per-version substreams |
| [[Architecture/Layers/13-CLIENT_LAYER|Client Layer]] | `ConnectionPool` is created by client factory, shared across named clients |
| [[Architecture/Layers/16-PROTOCOL_LAYER|Protocol Layer]] | Encoders produce `DataItem` (outbound); decoders consume `DataItem` (inbound) |
| [[Architecture/Guides/17-DIAGNOSTICS_INTEGRATION|Diagnostics]] | `TurboHttpMetrics.ConnectionActive/Idle/Duration` track pool state |

## See Also

- [[Architecture/Design/01-LAYERED_ARCHITECTURE|Layered Architecture]] — Layer positioning
- [[Architecture/Analysis/07-HTTP10_RECONNECTION_LIMITATION|HTTP/1.0 Reconnection Limitation]] — ExtractOptionsStage single-emit constraint
- [[Architecture/Analysis/11-STAGE_COMPLETION_AUDIT|Stage Completion Audit]] — ConnectionStage completion handling
