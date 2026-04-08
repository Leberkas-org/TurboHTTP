---
title: Layered Architecture
description: 7-layer design with strict separation of concerns from client API to TCP/QUIC transport
tags: [architecture, design, layers, akka, streams]
aliases: [ArchitectureOverview, LayerDesign, SystemArchitecture]
---

# TurboHTTP Layered Architecture

## Overview

TurboHTTP implements a **strict layered architecture** with data flowing from user API down through handlers, streams, encoders/decoders, and finally to the transport layer (TCP/QUIC).

```
┌─────────────────────────────────────────────────┐
│ Client Layer (ITurboHttpClient)                 │
│ - DI-friendly factory pattern                   │
│ - Channel-based API (ChannelWriter/Reader)      │
├─────────────────────────────────────────────────┤
│ Handlers Layer (TurboHandler)                   │
│ - Delegating handler bridge to Akka pipeline    │
├─────────────────────────────────────────────────┤
│ Hosting Layer (DI Registration)                 │
│ - AddTurboHttpClient() extension                │
├─────────────────────────────────────────────────┤
│ Streams Layer (Akka.Streams GraphStages)        │
│ ┌─────────────────────────────────────────────┐ │
│ │ Four Protocol Engines (1.0, 1.1, 2.0, 3.0)  │ │
│ │ ┌─────────────────────────────────────────┐ │ │
│ │ │ Encoding/ - Serialize requests          │ │ │
│ │ │ Decoding/ - Parse wire format           │ │ │
│ │ │ Features/ - Cross-cutting (cache,       │ │ │
│ │ │            redirect, retry, cookies)    │ │ │
│ │ │ Routing/  - Request multiplexing        │ │ │
│ │ └─────────────────────────────────────────┘ │ │
├─────────────────────────────────────────────────┤
│ Protocol Layer (Encoders/Decoders)              │
│ - RFC subfolders (RFC9112, RFC9113, RFC9114)    │
│ - HPACK/QPACK compression                       │
│ - Business logic: redirects, retries, cookies   │
├─────────────────────────────────────────────────┤
│ Transport Layer (Actor-free connection pool)    │
│ - ConnectionPool → HostConnections              │
│ - DirectConnectionFactory → ConnectionLease     │
│ - ClientByteMover (async data pump)             │
│ - TCP / QUIC channels                           │
└─────────────────────────────────────────────────┘
```

## Layer Responsibilities

### Client Layer (`TurboHTTP/Client/`)
- **ITurboHttpClient**: Channel-based API
  - `ChannelWriter<HttpRequestMessage>` — requests
  - `ChannelReader<HttpResponseMessage>` — responses
  - `SendAsync()` convenience method
  - `BaseAddress`, `DefaultRequestVersion`, `DefaultRequestHeaders`
- **ITurboHttpClientFactory**: DI-friendly named/typed client registration
- **TurboHttpClientFactoryExtensions**: Extension methods for factory setup
- **TurboClientOptions**: Per-client config (timeouts, redirects, retries)
- **TurboClientStreamManager**: Akka stream lifecycle management

### Handlers Layer (`TurboHTTP/Handlers/`)
- **TurboHandler**: Delegating handler that bridges `HttpMessageHandler` → Akka stream pipeline
- **TurboHttpClientBuilder**: Fluent API for composing handler pipeline
- **TurboClientDescriptor**: Configuration snapshot for a client instance

### Hosting Layer (`TurboHTTP/Hosting/`)
- **TurboClientServiceCollectionExtensions**: DI registration
- Integrates with `IServiceCollection` (Microsoft.Extensions.DependencyInjection)
- Supports named and typed client registration

### Streams Layer (`TurboHTTP/Streams/`)

Four separate **protocol engines** route requests by HTTP version:

#### Encoding/ — Request Serialization
- `Http10EncoderStage`, `Http11EncoderStage`, `Http20EncoderStage`, `Http30EncoderStage`
- `Request2FrameStage` (HTTP/2), `Http30Request2FrameStage` (HTTP/3)
- `PrependPrefaceStage` — HTTP/2 connection preface ("PRI * HTTP/2.0\r\n...")
- `QpackEncoderStreamStage` — QPACK encoder stream (HTTP/3 only)

#### Decoding/ — Response Parsing
- `Http10DecoderStage`, `Http11DecoderStage`, `Http20DecoderStage`, `Http30DecoderStage`
- `Http20ConnectionStage`, `Http30ConnectionStage` — connection-level frames (SETTINGS, PING, GOAWAY)
- `Http20StreamStage`, `Http30StreamStage` — stream-level assembly into `HttpResponseMessage`
- `QpackDecoderStreamStage` — QPACK decoder stream (HTTP/3 only)

#### Features/ — Cross-Cutting BidiStages
- **Redirect** (`RedirectBidiStage`) — RFC 9110 §15.4 redirect following
- **Retry** (`RetryBidiStage`) — RFC 9110 §9.2 idempotent retry
- **Cookies** (`CookieBidiStage`) — RFC 6265 cookie injection/storage
- **Cache** (`CacheBidiStage`) — RFC 9111 cache lookup/storage
- **Decompression** (`DecompressionBidiStage`) — gzip/deflate/brotli response body decompression
- **Request Compression** (`RequestCompressionBidiStage`) — request body compression
- **Expect-Continue** (`ExpectContinueBidiStage`) — 100-continue protocol
- **Connection Reuse** (`ConnectionReuseStage`) — keep-alive/close decisions
- **Handler Bridge** (`HandlerBidiStage`) — delegating handler integration

#### Routing/ — Request Multiplexing & Correlation
- `RequestEnricherStage` — applies BaseAddress, DefaultRequestVersion, DefaultRequestHeaders
- `ExtractOptionsStage` — separates transport options from request
- `Http1XCorrelationStage` — FIFO request-response matching (HTTP/1.x)
- `Http20CorrelationStage` — stream-ID-based matching (HTTP/2)
- `StreamIdAllocatorStage` — allocates client stream IDs (1, 3, 5, …)
- `GroupByHostKeyStage` / `HostKeyMergeBack` — per-host sub-stream routing

### Protocol Layer (`TurboHTTP/Protocol/`)

**Encoders** — Serialize `HttpRequestMessage` → bytes:
- Use `ref Span<byte>` and `ref Memory<byte>` for zero-allocation patterns
- Methods: `Encode()`, `EncodeHeaders()`, etc.

**Decoders** — Stateful, handle partial frames across TCP boundaries:
- Maintain `_remainder` for incomplete messages
- `TryDecode()` for normal parsing, `TryDecodeEof()` for connection close
- `Reset()` to clear state between connections

**HPACK (RFC 7541)** — Header compression for HTTP/2:
- `HpackEncoder`/`HpackDecoder` maintain synchronized dynamic tables
- `HpackDynamicTable` — FIFO with 32-byte per-entry overhead
- `HuffmanCodec` — static Huffman encoding/decoding
- Sensitive headers (Authorization, Cookie) use NeverIndex automatically

**QPACK (RFC 9204)** — Header compression for HTTP/3:
- `QpackDecoder`/`QpackDecoderInstructionWriter`
- Streamed decoder, supports blocking references to encoder updates

**HTTP/2 Frames** (`Http2Frame.cs`) — 9-byte headers + variable-length payloads:
- `DataFrame`, `HeadersFrame`, `ContinuationFrame`, `RstStreamFrame`, `SettingsFrame`, `PingFrame`, `GoAwayFrame`, `WindowUpdateFrame`, `PushPromiseFrame`
- `SerializedSize` for buffer pre-allocation, `WriteTo(ref Span<byte>)` for serialization

**HTTP/3 Frames** (`RFC9114/Http3Frame.cs`) — Variable-length headers using QUIC integers:
- `Http3FrameEncoder`/`Http3FrameDecoder`
- `Http3RequestEncoder`/`Http3ResponseDecoder`
- Stream types: Control, Request (unidirectional), Bidirectional

**Business Logic**:
- `RedirectHandler` — RFC 9110 §15.4 redirect following with correct method rewriting
- `RetryEvaluator` — RFC 9110 §9.2 idempotency-based retry
- `ConnectionReuseEvaluator` — RFC 9112 §9 keep-alive/close decision
- `CookieJar` — RFC 6265 domain/path matching, Secure/HttpOnly/SameSite
- `ContentEncodingDecoder` — gzip/deflate/brotli decompression
- `HttpCacheStore` — RFC 9111 thread-safe in-memory LRU cache
- `CacheFreshnessEvaluator` — RFC 9111 freshness lifetime calculation
- `CacheValidationRequestBuilder` — RFC 9111 conditional request building

### Transport Layer (`TurboHTTP/Transport/`)

**Actor-free connection pool** — zero mailbox hops:
- `ConnectionPool` — thread-safe async pool; owns nested `HostConnections` per host:port
- `HostConnections` — per-host limits, idle queue, MRU selection
- `DirectConnectionFactory` — establishes TCP/QUIC connections
- `QuicConnectionManager` — QUIC multi-stream management
- `ConnectionLease` — wraps `ConnectionHandle` + lifecycle
- `ClientByteMover` — async task pump: TCP ↔ Channels
- `ClientState` — holds TCP stream, Pipes, channel readers/writers

**Data Path** — `System.Threading.Channels`:
- `ConnectionStage` acquires `ConnectionLease` from `ConnectionPool`
- `ClientByteMover` spawns as background async tasks per connection
- TCP/QUIC data flows through `System.IO.Pipelines.Pipe`

## Actor-Based Stream Lifecycle (`TurboHTTP/Client/`)

The Akka stream pipeline is supervised by a two-actor hierarchy:

```
ClientStreamOwnerActor (supervisor)
└── ClientStreamInstanceActor (materializes the Akka.Streams pipeline)
```

### ClientStreamOwnerActor
- **Supervises** the stream instance actor
- **Tracks pending work** from feature BidiStages (redirect/retry re-injections)
- **Retries** with exponential backoff: 100ms → 500ms → 2s (max 3 attempts)
- **Graceful shutdown**: 5s timeout, waits for pending work to drain

### ClientStreamInstanceActor
- **Owns and materializes** the Akka.Streams pipeline (`ChannelSource → Engine → Sink`)
- **Reports** completion/failure to Owner actor
- **Cleans up** resources in `PostStop`

### Supporting Types
- **IPendingWorkTracker / PendingWorkTracker** — thread-safe lock-free counter; feature BidiStages increment before re-injection, decrement after round-trip; Owner checks before allowing stream completion
- **IClientStreamOwner** — public interface for advanced users; provides `InitializeStreamAsync` and `ActorRef` access
- **StreamInitializationOptions** — record with `TurboClientOptions`, `RequestOptionsFactory`, optional `SupervisorStrategy`
- **StreamInitializationResult** — union type: `Success(IActorRef)` or `Failed(Exception)`

### Actor Protocol Messages (`ActorProtocol.cs`)
- **ClientStreamOwner.Message**: `Create`, `Created`, `Failed`, `PendingWorkSignal`, `RequestStreamIdle`, `Shutdown`
- **ClientStreamInstance.Message**: `Initialize`, `Initialized`, `Failed`, `PendingWorkChanged`, `RequestShutdown`

## Key Invariants

1. **No actor mailbox in data path** — TCP→Channels→Pipe→Channels→TCP with zero actor hops
2. **Layered dependencies** — each layer only depends on layers below it
3. **RFC alignment** — Protocol layer is the RFC authority; Streams/Handlers layer delegates to it
4. **Memory efficiency** — `Span<T>`, `Memory<T>`, `IMemoryOwner<T>` throughout
5. **Cancellation** — `CancellationToken` flows through all async call chains

## Extension Points

1. **Custom handlers** — extend `HttpMessageHandler` and add to `TurboHttpClientBuilder`
2. **Custom stages** — extend `GraphStage<>` and wire into `ProtocolCoreGraphBuilder`
3. **Custom encoders/decoders** — replace encoder/decoder implementations (but maintain RFC compliance)
4. **DI configuration** — `AddTurboHttpClient()` extensibility for custom registrations
