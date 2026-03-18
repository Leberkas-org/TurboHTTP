# Diagram Validation Checklist

This document maps every element in the TurboHttp architecture diagrams to its source file and line number,
verifying that diagrams accurately represent the codebase as of 2026-03-18 (branch `poc2`).

---

## 1. Stages — Each stage in `src/TurboHttp/Streams/Stages/` appears in at least one diagram

| Stage Class | Source File | Diagram(s) |
|-------------|------------|-------------|
| `CacheLookupStage` | `Streams/Stages/CacheLookupStage.cs` | c4-components.md, engine-stream-graph.md |
| `CacheStorageStage` | `Streams/Stages/CacheStorageStage.cs` | c4-components.md, engine-stream-graph.md |
| `ConnectionReuseStage` | `Streams/Stages/ConnectionReuseStage.cs` | c4-components.md, connection-flow-graph.md, protocol-engine-graphs.md |
| `CookieInjectionStage` | `Streams/Stages/CookieInjectionStage.cs` | c4-components.md, engine-stream-graph.md |
| `CookieStorageStage` | `Streams/Stages/CookieStorageStage.cs` | c4-components.md, engine-stream-graph.md |
| `DecompressionStage` | `Streams/Stages/DecompressionStage.cs` | c4-components.md, engine-stream-graph.md |
| `ExtractOptionsStage` | `Streams/Stages/ExtractOptionsStage.cs` | c4-components.md, connection-flow-graph.md, protocol-engine-graphs.md |
| `Http10DecoderStage` | `Streams/Stages/Http10DecoderStage.cs` | c4-components.md, protocol-engine-graphs.md |
| `Http10EncoderStage` | `Streams/Stages/Http10EncoderStage.cs` | c4-components.md, protocol-engine-graphs.md |
| `Http11DecoderStage` | `Streams/Stages/Http11DecoderStage.cs` | c4-components.md, protocol-engine-graphs.md |
| `Http11EncoderStage` | `Streams/Stages/Http11EncoderStage.cs` | c4-components.md, protocol-engine-graphs.md |
| `Http1XCorrelationStage` | `Streams/Stages/Http1XCorrelationStage.cs:54` | c4-components.md, protocol-engine-graphs.md |
| `Http20ConnectionStage` | `Streams/Stages/Http20ConnectionStage.cs` | c4-components.md, protocol-engine-graphs.md |
| `Http20CorrelationStage` | `Streams/Stages/Http20CorrelationStage.cs:8` | c4-components.md (noted as not yet wired) |
| `Http20DecoderStage` | `Streams/Stages/Http20DecoderStage.cs` | c4-components.md, protocol-engine-graphs.md |
| `Http20EncoderStage` | `Streams/Stages/Http20EncoderStage.cs` | c4-components.md, protocol-engine-graphs.md |
| `Http20StreamStage` | `Streams/Stages/Http20StreamStage.cs` | c4-components.md, protocol-engine-graphs.md |
| `PrependPrefaceStage` | `Streams/Stages/PrependPrefaceStage.cs:14` | c4-components.md (noted as not yet wired) |
| `RedirectStage` | `Streams/Stages/RedirectStage.cs` | c4-components.md, engine-stream-graph.md |
| `Request2FrameStage` | `Streams/Stages/Request2FrameStage.cs` | c4-components.md, protocol-engine-graphs.md |
| `RequestEnricherStage` | `Streams/Stages/RequestEnricherStage.cs` | c4-components.md, engine-stream-graph.md |
| `RetryStage` | `Streams/Stages/RetryStage.cs` | c4-components.md, engine-stream-graph.md |
| `StreamIdAllocatorStage` | `Streams/Stages/StreamIdAllocatorStage.cs` | c4-components.md, protocol-engine-graphs.md |
| `ConnectionStage` | `IO/Stages/ConnectionStage.cs` | c4-components.md, connection-flow-graph.md, io-actor-hierarchy.md |

**Result: 24/24 stages appear in at least one diagram.**

---

## 2. Actors — Each actor in `src/TurboHttp/IO/` appears in the actor hierarchy diagram

| Actor Class | Source File | Diagram |
|-------------|------------|---------|
| `PoolRouterActor` | `IO/PoolRouterActor.cs` | io-actor-hierarchy.md (supervision tree + message flow) |
| `HostPoolActor` | `IO/HostPoolActor.cs` | io-actor-hierarchy.md (supervision tree + message flow) |
| `ConnectionActor` | `IO/ConnectionActor.cs` | io-actor-hierarchy.md (supervision tree + message flow) |
| `ClientManager` | `IO/ClientManager.cs` | io-actor-hierarchy.md (supervision tree) |
| `ClientRunner` | `IO/ClientRunner.cs` | io-actor-hierarchy.md (supervision tree + message flow) |

**Non-actor I/O types in diagrams:**

| Type | Source File | Diagram |
|------|------------|---------|
| `ConnectionHandle` | `IO/ConnectionHandle.cs` | io-actor-hierarchy.md (ConnectionHandle table), c4-components.md |
| `ClientByteMover` | `IO/ClientByteMover.cs` | io-actor-hierarchy.md (3 async pump tasks diagram) |
| `ClientState` | `IO/ClientState.cs` | io-actor-hierarchy.md (ClientState structure) |
| `ConnectionState` | `IO/ConnectionState.cs` | c4-components.md |
| `IClientProvider` | `IO/IClientProvider.cs` | io-actor-hierarchy.md (TCP/TLS socket) |

**Result: 5/5 actors appear in the actor hierarchy diagram. All key I/O types represented.**

---

## 3. Engine Graph Wiring — `Engine.BuildExtendedPipeline` (engine-stream-graph.md)

Source: `Streams/Engine.cs` lines 69–165

| Pipeline Step | Code Line(s) | Diagram Element | Match |
|---------------|-------------|-----------------|-------|
| `RequestEnricherStage` | 84–85 | `enricher` node | Yes |
| `MergePreferred` (redirect) | 88–90 | `redirectMerge` junction | Yes |
| `CookieInjectionStage` | 93–95 | `cookieInject` node | Yes |
| `MergePreferred` (retry) | 98–100 | `retryMerge` junction | Yes |
| `CacheLookupStage` (Out0: miss, Out1: hit) | 103–107 | `cacheLookup` node with two outputs | Yes |
| `BuildEngineCoreGraph` (Partition → engines → Merge) | 114–115 | `EngineCore` subgraph | Yes |
| `DecompressionStage` | 120 | `decomp` node | Yes |
| `CookieStorageStage` | 123 | `cookieStore` node | Yes |
| `CacheStorageStage` | 126 | `cacheStore` node | Yes |
| `RetryStage` (Out0: final, Out1: retry) | 129–140 | `retry` node + feedback loop | Yes |
| `Buffer(1)` retry feedback | 139 | `retryBuf` node | Yes |
| `Merge(2)` cache (In0: normal, In1: cache hit) | 145–148 | `cacheMerge` junction | Yes |
| `RedirectStage` (Out0: final, Out1: redirect) | 151–158 | `redirect` node + feedback loop | Yes |
| `Buffer(1)` redirect feedback | 155 | `redirectBuf` node | Yes |

**Result: All pipeline steps match.**

### Engine Core — `BuildEngineCoreGraph` (lines 167–196)

| Element | Code Line(s) | Diagram Element | Match |
|---------|-------------|-----------------|-------|
| `Partition(4)` by HTTP version | 281–293 | `partition` junction | Yes |
| `Http10Engine` (Out 0) | 178 | `http10` in `H10` subgraph | Yes |
| `Http11Engine` (Out 1) | 180 | `http11` in `H11` subgraph | Yes |
| `Http20Engine` (Out 2) | 183 | `http20` in `H20` subgraph | Yes |
| `Http30Engine` (Out 3, stub) | 187 | `http30` in `H30` subgraph | Yes |
| `Merge(4)` hub | 192 | `hub` junction | Yes |
| `GroupByHostKey` per engine | 198–227 | Subgraph labels | Yes |

**Result: All engine core elements match.**

---

## 4. Connection Flow — `Engine.BuildConnectionFlowPublic` (connection-flow-graph.md + protocol-engine-graphs.md)

Source: `Streams/Engine.cs` lines 229–279

| Element | Code Line(s) | Diagram Element | Match |
|---------|-------------|-----------------|-------|
| `ExtractOptionsStage` | 244 | `extract` node | Yes |
| BidiFlow (protocol engine) | 237–238 | `BidiFlow` subgraph | Yes |
| `Concat(2)` | 247 | `concat` junction | Yes |
| `MergePreferred` transport | 254 | `transportMerge` junction | Yes |
| Transport flow (`ConnectionStage`) | 264 | `connStage` / `transport` node | Yes |
| `ConnectionReuseStage` | 250 | `connReuse` node | Yes |
| Signal feedback (Select + Buffer(1)) | 271–274 | `selectBuf` / `signalCast` node | Yes |
| `ConnectItem` (Out1 → Concat In0) | 257 | `extract Out1 → concat` edge | Yes |
| BidiFlow Outlet1 → Concat In1 | 261 | `bidiOut1 → concat` edge | Yes |

**Result: All connection flow elements match.**

---

## 5. Protocol Engine Diagrams (protocol-engine-graphs.md)

### HTTP/1.0 Engine — `Http10Engine.CreateFlow()` (lines 12–40)

| Element | Code Line(s) | Diagram Element | Match |
|---------|-------------|-----------------|-------|
| `Broadcast(2)` | 16 | `bcast` junction | Yes |
| `Http10EncoderStage` | 17 | `encoder` node | Yes |
| `Http10DecoderStage` | 18 | `decoder` node | Yes |
| `Http1XCorrelationStage` | 19 | `correlation` node | Yes |
| `Sink.Ignore` (signal discard) | 33 | `signalSink` node | Yes |
| Wiring: bcast Out(0) → encoder | 25 | Solid arrow | Yes |
| Wiring: bcast Out(1) → correlation | 26 | Solid arrow | Yes |
| Wiring: decoder → correlation | 30 | Solid arrow | Yes |

**Result: HTTP/1.0 engine diagram matches.**

### HTTP/1.1 Engine — `Http11Engine.CreateFlow()` (lines 12–43)

| Element | Code Line(s) | Diagram Element | Match |
|---------|-------------|-----------------|-------|
| `Broadcast(2)` | 16 | `bcast` junction | Yes |
| `Http11EncoderStage` | 17 | `encoder` node | Yes |
| `Http11DecoderStage` | 18 | `decoder` node | Yes |
| `Http1XCorrelationStage` | 19 | `correlation` node | Yes |
| `Flow.Select` (IControlItem → IOutputItem) | 35 | `signalCast` node | Yes |
| `MergePreferred(1)` | 20 | `signalMerge` junction | Yes |
| Wiring: encoder → signalMerge In(0) | 37 | Solid arrow | Yes |
| Wiring: correlation OutletSignal → signalCast → signalMerge Preferred | 35–37 | Solid arrow | Yes |

**Result: HTTP/1.1 engine diagram matches.**

### HTTP/2 Engine — `Http20Engine.CreateFlow()` (lines 24–59)

| Element | Code Line(s) | Diagram Element | Match |
|---------|-------------|-----------------|-------|
| `StreamIdAllocatorStage` | 28 | `streamAlloc` node | Yes |
| `Request2FrameStage` | 29 | `req2frame` node | Yes |
| `Http20ConnectionStage` | 30 | `connection` node | Yes |
| `Http20EncoderStage` | 31 | `frameEncoder` node | Yes |
| `Http20DecoderStage` | 32 | `frameDecoder` node | Yes |
| `Http20StreamStage` | 33 | `streamDecoder` node | Yes |
| `Flow.Select` (IControlItem → IOutputItem) | 45 | `signalCast` node | Yes |
| `MergePreferred(1)` | 34 | `signalMerge` junction | Yes |
| Wiring: streamAlloc → req2frame → connection AppIn | 39–40 | Solid arrows | Yes |
| Wiring: connection ServerOut → frameEncoder | 41 | Solid arrow | Yes |
| Wiring: frameDecoder → connection ServerIn | 42 | Solid arrow | Yes |
| Wiring: connection AppOut → streamDecoder | 43 | Solid arrow | Yes |

**Result: HTTP/2 engine diagram matches.**

---

## 6. I/O Actor Hierarchy (io-actor-hierarchy.md)

### Supervision Tree

| Element | Source File | Diagram Element | Match |
|---------|------------|-----------------|-------|
| `PoolRouterActor` | `IO/PoolRouterActor.cs` | `PR` node at root | Yes |
| `HostPoolActor` (per host) | `IO/HostPoolActor.cs` | `HPA1`, `HPA2` nodes | Yes |
| `ConnectionActor` (per connection) | `IO/ConnectionActor.cs` | `CA1`, `CA2`, `CA3` nodes | Yes |
| `ClientManager` (factory) | `IO/ClientManager.cs` | `CM` node | Yes |
| `ClientRunner` (per TCP) | `IO/ClientRunner.cs` | `CR1`, `CR2`, `CR3` nodes | Yes |
| `ClientByteMover` (3 tasks) | `IO/ClientByteMover.cs` | `CBM1` non-actor node | Yes |
| `ClientState` (I/O primitives) | `IO/ClientState.cs` | `CS1` non-actor node | Yes |
| `IClientProvider` (socket factory) | `IO/IClientProvider.cs` | `ICP1` non-actor node | Yes |

**Result: All actors and key I/O types appear in the hierarchy diagram.**

### Message Flow Sequence Diagram

| Message | Source | Diagram Element | Match |
|---------|--------|-----------------|-------|
| `EnsureHost` | `ConnectionStage` → `PoolRouterActor` | Sequence arrow CS → PR | Yes |
| Forward to `HostPoolActor` | `PoolRouterActor` | Sequence arrow PR → HPA | Yes |
| `SelectConnection()` | `HostPoolActor` internal | Self-arrow HPA → HPA | Yes |
| Spawn `ConnectionActor` | `HostPoolActor` | Sequence arrow HPA → CA | Yes |
| `CreateRunnerWithChannels` | `ConnectionActor` → `ClientManager` | Sequence arrow CA → CR | Yes |
| `ClientConnected` | `ClientRunner` → `ConnectionActor` | Sequence arrow CR → CA | Yes |
| `ConnectionReady(Handle)` | `ConnectionActor` → `HostPoolActor` | Sequence arrow CA → HPA | Yes |
| `ConnectionHandle` reply | `HostPoolActor` → `ConnectionStage` | Sequence arrow HPA → CS | Yes |
| Zero-hop data transfer | Channel-based | Dashed arrows CS ↔ CBM | Yes |

**Result: Message flow matches actor implementation.**

### Channel Data Path Diagram

| Element | Source File | Diagram Element | Match |
|---------|------------|-----------------|-------|
| `ConnectionStage` bridge | `IO/Stages/ConnectionStage.cs` | `CSTAGE` node | Yes |
| `StageActor` (ConnectionStage's ref) | `IO/Stages/ConnectionStage.cs` | `SA` node | Yes |
| Outbound `Channel<(IMemoryOwner, int)>` | `IO/ClientState.cs` | `OCH` node | Yes |
| Inbound `Channel<(IMemoryOwner, int)>` | `IO/ClientState.cs` | `ICH` node | Yes |
| `System.IO.Pipelines.Pipe` | `IO/ClientState.cs` | `PIPE` node | Yes |
| `MoveStreamToPipe` (Task 1) | `IO/ClientByteMover.cs` | `T1` node | Yes |
| `MovePipeToChannel` (Task 2) | `IO/ClientByteMover.cs` | `T2` node | Yes |
| `MoveChannelToStream` (Task 3) | `IO/ClientByteMover.cs` | `T3` node | Yes |
| TCP/TLS Socket | `IO/IClientProvider.cs` | `SOCK` node | Yes |

**Result: Channel data path matches implementation.**

---

## 7. C4 Component Diagram (c4-components.md)

### Client Layer

| Component | Source File | Match |
|-----------|------------|-------|
| `ITurboHttpClient` | `Client/ITurboHttpClient.cs` | Yes |
| `TurboClientStreamManager` | `Client/TurboClientStreamManager.cs:17` | Yes |
| `TurboClientOptions` | `Client/TurboClientOptions.cs:11` | Yes |

### Streams Layer

| Component | Source File | Match |
|-----------|------------|-------|
| `Engine` | `Streams/Engine.cs` | Yes |
| `Http10Engine` | `Streams/Http10Engine.cs` | Yes |
| `Http11Engine` | `Streams/Http11Engine.cs` | Yes |
| `Http20Engine` | `Streams/Http20Engine.cs` | Yes |
| All 24 stages | See Section 1 above | Yes |

### Protocol Layer

| Component | Source File | Match |
|-----------|------------|-------|
| `Http10Encoder` | `Protocol/RFC1945/Http10Encoder.cs` | Yes |
| `Http11Encoder` | `Protocol/RFC9112/Http11Encoder.cs` | Yes |
| `Http2RequestEncoder` | `Protocol/RFC9113/Http2RequestEncoder.cs` | Yes |
| `Http10Decoder` | `Protocol/RFC1945/Http10Decoder.cs` | Yes |
| `Http11Decoder` | `Protocol/RFC9112/Http11Decoder.cs` | Yes |
| `Http2FrameDecoder` | `Protocol/RFC9113/Http2FrameDecoder.cs` | Yes |
| `HpackEncoder` | `Protocol/RFC7541/HpackEncoder.cs` | Yes |
| `HpackDecoder` | `Protocol/RFC7541/HpackDecoder.cs` | Yes |
| `HpackDynamicTable` | `Protocol/RFC7541/HpackDynamicTable.cs` | Yes |
| `HuffmanCodec` | `Protocol/RFC7541/HuffmanCodec.cs` | Yes |
| `RedirectHandler` | `Protocol/RFC9110/RedirectHandler.cs` | Yes |
| `RetryEvaluator` | `Protocol/RFC9110/RetryEvaluator.cs` | Yes |
| `CookieJar` | `Protocol/RFC6265/CookieJar.cs` | Yes |
| `ConnectionReuseEvaluator` | `Protocol/RFC9112/ConnectionReuseEvaluator.cs` | Yes |
| `ContentEncodingDecoder` | `Protocol/RFC9110/ContentEncodingDecoder.cs` | Yes |
| `HttpCacheStore` | `Protocol/RFC9111/HttpCacheStore.cs` | Yes |
| `CacheFreshnessEvaluator` | `Protocol/RFC9111/CacheFreshnessEvaluator.cs` | Yes |
| `CacheValidationRequestBuilder` | `Protocol/RFC9111/CacheValidationRequestBuilder.cs` | Yes |
| `CacheControlParser` | `Protocol/RFC9111/CacheControlParser.cs` | Yes |

### I/O Layer

| Component | Source File | Match |
|-----------|------------|-------|
| `PoolRouterActor` | `IO/PoolRouterActor.cs` | Yes |
| `HostPoolActor` | `IO/HostPoolActor.cs` | Yes |
| `ConnectionActor` | `IO/ConnectionActor.cs` | Yes |
| `ConnectionStage` | `IO/Stages/ConnectionStage.cs` | Yes |
| `ConnectionHandle` | `IO/ConnectionHandle.cs` | Yes |
| `ClientByteMover` | `IO/ClientByteMover.cs` | Yes |
| `ClientState` | `IO/ClientState.cs` | Yes |
| `ConnectionState` | `IO/ConnectionState.cs` | Yes |
| `PerHostConnectionLimiter` | `Protocol/RFC9112/PerHostConnectionLimiter.cs` | Yes |

**Result: All C4 components map to real source files.**

---

## 8. Aspirational/Invented Components — None

| Stage/Component | Status | Notes |
|----------------|--------|-------|
| `Http30Engine` | Stub | Exists in `Streams/Http30Engine.cs` — routes in `Partition(4)` but throws `NotSupportedException`. Correctly shown in engine-stream-graph.md as a routing slot. |
| `PrependPrefaceStage` | Implemented, not wired | Exists in `Streams/Stages/PrependPrefaceStage.cs:14` — stage is complete but not connected in any engine graph. C4 diagram notes this. |
| `Http20CorrelationStage` | Implemented, not wired | Exists in `Streams/Stages/Http20CorrelationStage.cs:8` — stage is complete but not connected in any engine graph. C4 diagram notes this. |

No invented or aspirational components appear in any diagram. All diagram elements correspond to real source files.

---

## 9. Corrections Applied (TASK-7-006)

| Issue | Diagram | Fix |
|-------|---------|-----|
| Wrong class name `CorrelationHttp1XStage` | c4-components.md | Renamed to `Http1XCorrelationStage` (matches `Http1XCorrelationStage.cs:54`) |
| Wrong class name `CorrelationHttp20Stage` | c4-components.md | Renamed to `Http20CorrelationStage` (matches `Http20CorrelationStage.cs:8`) |
| Missing relationship `Http10Engine → Http1XCorrelationStage` | c4-components.md | Added `Rel` — Http10Engine uses Http1XCorrelationStage in its `CreateFlow()` |
| Incorrect `Rel(Http20Engine, PrependPrefaceStage)` | c4-components.md | Removed — PrependPrefaceStage is not wired into Http20Engine |
| Incorrect `Rel(Http20Engine, CorrelationHttp20Stage)` | c4-components.md | Removed — Http20CorrelationStage is not wired into Http20Engine |
