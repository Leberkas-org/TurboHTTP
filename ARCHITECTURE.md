# Architecture

## Overview

TurboHTTP is a high-performance **.NET 10 HTTP client library** built on **Akka.Streams**. It implements HTTP/1.0, HTTP/1.1, HTTP/2, and HTTP/3 (QUIC) as a reactive streaming pipeline with full RFC compliance. Connection pooling, redirect following, retry, caching, cookie management, and content encoding are composable BidiFlow stages stacked around a version-demultiplexed protocol core.

---

## Layer Map

```
┌─────────────────────────────────────────────────┐
│ Client Layer (ITurboHttpClient)                 │
│ - Channel-based API + SendAsync() convenience   │
│ - ITurboHttpClientFactory (named/typed clients) │
│ - DI via AddTurboHttpClient()                   │
├─────────────────────────────────────────────────┤
│ Streams Layer (Akka.Streams GraphStages)        │
│ ┌─────────────────────────────────────────────┐ │
│ │ Feature BidiFlow Chain (Island 1)           │ │
│ │ Tracing → Handlers → Redirect → Cookie      │ │
│ │ → Retry → Expect100                         │ │
│ │ → Cache → ContentEncoding                   │ │
│ ├─────────────────────────────────────────────┤ │
│ │ Protocol Engine Core (Island 2)             │ │
│ │ RequestEnricher → GroupByRequestEndpoint    │ │
│ │ → EndpointDispatchStage → [H10|H11|H20|H30] │ │
│ │ engines ↔ ITransportFactory                 │ │
│ │ → MergeSubstreams                           │ │
│ └─────────────────────────────────────────────┘ │
├─────────────────────────────────────────────────┤
│ Protocol Layer (RFC-subfolder encoders/decoders)│
│ - RFC9112 HTTP/1.x, RFC9113 HTTP/2              │
│ - RFC9114 HTTP/3, RFC7541 HPACK, RFC9204 QPACK  │
│ - Business logic: cookies, cache, redirect...   │
├─────────────────────────────────────────────────┤
│ Transport Layer (actor-free connection pool)    │
│ - ConnectionPool → HostConnections              │
│ - ConnectionLease, ClientByteMover              │
│ - System.Threading.Channels + IO.Pipelines      │
│ - TCP/TLS + QUIC                                │
│ - ITransportFactory: TCP, QUIC plug-in point    │
└─────────────────────────────────────────────────┘
```

**Key invariant**: each layer depends only on the layer directly below it. No upward references.

---

## Client Layer

| Component | Responsibility |
|-----------|---------------|
| `ITurboHttpClient` | Public API: `ChannelWriter<HttpRequestMessage>` + `ChannelReader<HttpResponseMessage>` + `SendAsync()` |
| `ITurboHttpClientFactory` | Creates named/typed client instances; owns `ConnectionPool` lifetime |
| `ITurboHttpClientBuilder` | Fluent DI configuration surface (extends `IServiceCollection`) |
| `TurboClientOptions` | Per-client config: nested `Http1Options`, `Http2Options`, `Http3Options`; timeouts, TLS certificates |
| `TurboRequestOptions` | Per-request defaults: base address, version, headers |
| `PipelineDescriptor` | Aggregates all optional policies into a single record for pipeline construction |
| `TurboHandler` | User middleware (bridges delegating-handler pattern into the BidiFlow chain) |

**Factory pattern** — `ITurboHttpClientFactory` rather than direct construction, enabling named clients with separate configurations and shared `ConnectionPool` instances.

**PipelineDescriptor** — null policies are skipped; no BidiStage is inserted for unused features.

### Stream Lifecycle Actors

A thin two-actor supervisor hierarchy manages Akka stream lifecycle (not in the data path):

```
ClientStreamOwnerActor (supervisor)
└── ClientStreamInstanceActor (materializes the pipeline)
```

- **Owner** tracks pending work from feature stages (redirects, retries in flight), retries with exponential backoff (100ms → 500ms → 2s, max 3 attempts), and enforces 5s graceful shutdown.
- **Instance** owns the materialised pipeline; reports completion/failure to Owner.
- **`IPendingWorkTracker`** — lock-free counter; BidiStages increment before re-injecting a request, decrement after the round-trip completes.

---

## Streams Layer

### Feature BidiFlow Chain

Composed via `Atop` (innermost → outermost):

| Stage | RFC | Effect |
|-------|-----|--------|
| `TracingBidiStage` | — | Root `Activity` per request |
| `HandlerBidiStage` | — | Wraps user `TurboHandler` middleware |
| `RedirectBidiStage` | RFC 9110 §15.4 | Follows 301/302/303/307/308; internal feedback loop |
| `CookieBidiStage` | RFC 6265 §5.3–5.4 | Injects/extracts cookies via `CookieJar` |
| `RetryBidiStage` | RFC 9110 §9.2 | Retries idempotent requests; internal feedback loop |
| `ExpectContinueBidiStage` | RFC 9110 §10.1.1 | Manages `Expect: 100-continue` handshake |
| `CacheBidiStage` | RFC 9111 | Short-circuits on hit; stores responses |
| `ContentEncodingBidiStage` | RFC 9110 §8.4 | Compresses requests / decompresses responses |

Request flows top-to-bottom; response flows bottom-to-top. Only BidiFlows for non-null policies in `PipelineDescriptor` are included.

### Protocol Engine Core

The protocol engine core routes requests by endpoint and version, then wires them through version-specific connection flows:

```
RequestEnricherStage
    → GroupByRequestEndpoint(endpoint)   [per-endpoint substream]
        → per-endpoint substream: EndpointDispatchStage [lazy flow initialization per endpoint]
            → [H10|H11|H20|H30] engines ↔ ConnectionStage ↔ ConnectionReuseStage
    → MergeSubstreams
```

- `RequestEndpoint` key = `(Scheme, Host, Port, Version)` — case-insensitive.
- `maxSubstreams`: Single shared ceiling per endpoint, configurable via `TurboClientOptions.MaxEndpointSubstreams`. Default is 256.
- An async boundary separates the feature chain from the engine core (protocol work runs on its own dispatcher).

### Per-Version Engine Assembly

| Version | Encode path | Decode path |
|---------|------------|------------|
| HTTP/1.0 | `Http10ConnectionStage` (unified encode + decode + correlation) | — |
| HTTP/1.1 | `Http11ConnectionStage` (unified encode + decode + correlation) | — |
| HTTP/2 | `Http20EncoderStage` + `PrependPrefaceStage` + `Request2FrameStage` | `Http20DecoderStage` + `ConnectionStage` + `StreamStage` + `CorrelationStage` + `StreamIdAllocatorStage` |
| HTTP/3 | `Http30EncoderStage` + control/QPACK preface stages + `Request2FrameStage` | `Http30DecoderStage` + `ConnectionStage` + `StreamStage` + `CorrelationStage` + `StreamDemuxStage` + QPACK stream stages |

### Builders (Streams Layer Orchestration)

| Builder | Responsibility |
|---------|-----------------|
| `Engine` | Thin orchestrator; wires `ProtocolCoreBuilder` and `FeaturePipelineBuilder` to create the final client-facing flow |
| `ProtocolCoreBuilder` | Owns endpoint grouping (`GroupByRequestEndpoint`), version dispatch via `EndpointDispatchStage`, version-specific engine instantiation, and transport selection via `TransportRegistry` (zero Transport-layer imports) |
| `FeaturePipelineBuilder` | Owns BidiFlow feature stack composition (ContentEncoding, Cache, Expect100, Retry, Cookie, Redirect, Handlers, Tracing) |

### Stage Naming Convention

| Shape | Inlet | Outlet | Example |
|-------|-------|--------|---------|
| FlowShape (1 in, 1 out) | `StageName.In` | `StageName.Out` | `"Http11Encoder.In"` |
| FanOutShape | `StageName.In` | `StageName.Out.Role` | `"Redirect.Out.Final"` |
| FanInShape | `StageName.In.Role` | `StageName.Out` | `"Http20Correlation.In.Request"` |

PascalCase, no protocol prefix, no `Stage` suffix, semantically named roles, globally unique.

---

## Protocol Layer

Organized by RFC number. Each RFC folder owns its encoders, decoders, and business logic.

```
Protocol/
├── RFC7541/   HPACK (HTTP/2 header compression)
├── RFC9110/   HTTP Semantics — shared across versions
├── RFC9112/   HTTP/1.0 + HTTP/1.1 encoders/decoders
├── RFC9113/   HTTP/2 frames, settings, flow control
├── RFC9114/   HTTP/3 frames, QUIC stream management
├── RFC9204/   QPACK (HTTP/3 header compression)
└── Shared/    HttpDecodeResult discriminated union
```

### Encoder Contract

1. Accept `HttpRequestMessage` from Streams layer.
2. Serialize headers (text/HPACK/QPACK) + frame body.
3. Emit `IOutputItem` (`DataItem` wrapping `IMemoryOwner<byte>`) to Transport.

### Decoder Contract

1. Accept `IInputItem` (raw bytes from Transport).
2. Parse frames/lines; decompress headers.
3. Assemble and emit `HttpResponseMessage` to Streams layer.
4. Return `HttpDecodeResult` (`NeedMoreData` | `HeadersComplete` | `Complete` | `Error`).

### Three-Layer Decoder (HTTP/2 and HTTP/3)

```
ConnectionStage  — connection-level frames (SETTINGS, PING, GOAWAY, WINDOW_UPDATE)
    └── StreamStage  — per-stream demux and state machine
        └── DecoderStage  — frame → HttpResponseMessage assembly
```

### HPACK (RFC 7541)

- 61-entry static table + bounded FIFO dynamic table (`SETTINGS_HEADER_TABLE_SIZE`).
- Per-header encoding: Indexed | Literal with indexing | Literal without indexing | Never indexed (sensitive headers: `Authorization`, `Cookie`).
- Shared `HuffmanCodec` (RFC 7541 Appendix B table).

### QPACK (RFC 9204)

- 99-entry static table; dynamic table with out-of-order acknowledgment.
- Unidirectional QUIC Encoder Stream (table updates) and Decoder Stream (acknowledgments).
- Required Insert Count per HEADERS block; blocked streams supported.
- Shares `HuffmanCodec` with HPACK.

### Business Logic Components

| Component | RFC | Responsibility |
|-----------|-----|---------------|
| `RedirectHandler` | RFC 9110 §15.4 | Redirect following with correct method rewriting |
| `RetryEvaluator` | RFC 9110 §9.2 | Idempotency-based retry decisions |
| `ConnectionReuseEvaluator` | RFC 9112 §9 | Keep-alive vs. close decisions |
| `CookieJar` | RFC 6265 | Domain/path matching, Secure/HttpOnly/SameSite |
| `ContentEncodingDecoder` | RFC 9110 §8.4 | gzip/deflate/brotli decompression |
| `HttpCacheStore` | RFC 9111 | Thread-safe in-memory LRU cache |
| `CacheFreshnessEvaluator` | RFC 9111 | Freshness lifetime calculation |
| `CacheValidationRequestBuilder` | RFC 9111 | Conditional request construction |

---

## Transport Layer

Actor-free by design — zero actor mailbox hops in the data path.

### Connection Pool

```
ConnectionPool
└── HostConnections (per RequestEndpoint)
    ├── _idle: ConcurrentQueue<ConnectionLease>
    ├── _limiter: SemaphoreSlim
    └── _evictionTimer (at least one connection kept per host)
```

| Version | Acquire | Release | Limit |
|---------|---------|---------|-------|
| HTTP/1.0 | Always new | Always dispose | None |
| HTTP/1.1 | Idle queue → wait semaphore → establish | Reusable → idle queue; else dispose + release | 6/host |
| HTTP/2+ | MRU with available stream slots → establish | Decrement streams; dispose at 0 | Unlimited |

### Channels-Based I/O

```
Pipeline (IOutputItem) → OutboundChannel → Stream.WriteAsync() → Network
Network → Stream.ReadAsync() → Pipe → InboundChannel → Pipeline (IInputItem)
```

`ClientByteMover` runs two async loops per connection (read pump + write pump). On read completion it sets `CloseKind` (clean TLS `close_notify` vs. abrupt TCP RST) so decoders can apply RFC 9112 §9.8 rules.

### Transport Factory Plugin

`ITransportFactory` — formal contract for transport stage creation:

```csharp
internal interface ITransportFactory
{
    Flow<IOutputItem, IInputItem, NotUsed> Create();
}
```

- **`TcpTransportFactory`** — encapsulates `IActorRef connectionManager` + `TurboClientOptions`; registered for HTTP/1.0, 1.1, 2.0
- **`QuicTransportFactory`** — parameterless; registered for HTTP/3
- **`TransportRegistry`** — `Dictionary<Version, ITransportFactory>` + `Get(Version)` lookup; used in production and for test injection

Custom transports (Unix domain sockets, named pipes, etc.) are implemented by creating a new `ITransportFactory` and registering it before calling `Engine.CreateFlow()`.

### ConnectionStage Strategy Pattern

`ConnectionStage` delegates to an `ITransportHandler`:

- **`TcpTransportHandler`** — single bidirectional stream (HTTP/1.x, HTTP/2)
- **`QuicTransportHandler`** — multiple uni/bidirectional QUIC streams (HTTP/3)

Connection lifecycle is managed by `IConnectionScope`:

- **`SingleRequestConnectionScope`** — always new connection (HTTP/1.0)
- **`PersistentConnectionScope`** — reuse when keep-alive, close on `Connection: close` (HTTP/1.1+)
- **`DeferredConnectionScope`** — defers scope creation until first `ConnectItem` arrives

### Buffer Sizing

`ClientState` scales pause thresholds with `MaxFrameSize`:

- ≤128 KB → 512 KB pause threshold
- ≤1 MB → 2 MB pause threshold
- >1 MB → 2× MaxFrameSize

---

## Diagnostics

| Mechanism | API | Purpose |
|-----------|-----|---------|
| `TracingBidiStage` | `ActivitySource("TurboHTTP")` | W3C trace context, root `Activity` per request |
| `TurboHttpDiagnosticListener` | `DiagnosticListener` | Publish/subscribe event bus (compatible with `HttpClient` tooling) |
| `TurboHttpEventSource` | ETW `EventSource` | High-performance structured logging (zero alloc on hot path) |
| `TurboHttpMetrics` | OTel `Meter` | `ConnectionActive`, `ConnectionIdle`, `ConnectionDuration` gauges |
| `DeadlockWatchdogStage` | DEBUG only | Fires `OnDeadlockStall` if no element flows within 10s |

---

## Testing Structure

| Project | Contents |
|---------|----------|
| `TurboHTTP.Tests` | Unit tests organized by RFC namespace (`RFC9112`, `RFC9113`, …) |
| `TurboHTTP.StreamTests` | Akka.Streams `GraphStage` behavior via `StreamTestBase` |
| `TurboHTTP.IntegrationTests` | End-to-end with Kestrel fixtures |
| `TurboHTTP.Benchmarks` | BenchmarkDotNet suite (25+ benchmarks) |

All `[Fact]`/`[Theory]` tests carry `DisplayName("RFC-section-cat-nnn: description")` and explicit timeouts (`Timeout = 5000` or `CancellationToken`). Max 500 lines per test file.

---

## Extension Points

| Extension point | How |
|----------------|-----|
| Custom middleware | Implement `TurboHandler`; add via `TurboHttpClientBuilder` |
| Custom BidiFlow stages | Extend `GraphStage<BidiShape<...>>`; wire into `FeaturePipelineBuilder.Build()` |
| Custom encoders/decoders | Replace Protocol-layer implementations (maintain RFC wire compatibility) |
| Custom transport | Implement `ITransportFactory`; register via `TransportRegistry` (production + test injection) |
| Transport registry override | Inject a `TransportRegistry` into `Engine.CreateFlow()` with alternate or test `ITransportFactory` instances |
| DI registration | `AddTurboHttpClient()` + `ITurboHttpClientBuilder.Services` |

---

## Implementation Status

| Area | Score | Notes |
|------|-------|-------|
| HTTP/1.0 | 85/100 | Stable |
| HTTP/1.1 | 92/100 | Stable |
| HTTP/2 | 87/100 | Stable |
| HTTP/3 | 75/100 | Frame parsing + QUIC transport fully wired via `ITransportFactory` |
| HPACK | 90/100 | Stable |
| QPACK | 40/100 | Decoder only; encoder missing |
| Cookies | 80/100 | Stable |
| Caching | 78/100 | Stable |
| Redirects/Retries | 82/100 | Stable |

**Open gaps**: DoS protection (header size/count limits), redirect loop detection, HTTPS→HTTP downgrade blocking, QPACK encoder, QUIC transport, trailer header parsing.
