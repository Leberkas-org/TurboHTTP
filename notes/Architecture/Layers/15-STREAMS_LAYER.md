---
title: Streams Layer
description: >-
  Akka.Streams pipeline architecture — stage categories, BidiFlow stacking,
  version demux, and data-flow diagrams
tags:
  - architecture
  - streams
  - akka
  - stages
  - pipeline
---
# Streams Layer

The Streams Layer is TurboHttp's core — it composes Akka.Streams `GraphStage` and `BidiFlow` components into a reactive pipeline that transforms `HttpRequestMessage` into `HttpResponseMessage`. Every HTTP feature (redirect, retry, caching, compression, cookies) is a composable BidiFlow stage.

> **Scope**: This note covers pipeline composition and stage organization. For individual encoder/decoder internals, see [[Architecture/16-PROTOCOL_LAYER|Protocol Layer]]. For stage patterns and naming, see [[Architecture/02-STAGE_PATTERNS|GraphStage Patterns]].

## Purpose

- Compose HTTP features as stackable BidiFlow stages
- Route requests to version-specific protocol engines (HTTP/1.0, 1.1, 2, 3)
- Demultiplex per-host connections via `GroupByHostKey` / `MergeSubstreams`
- Provide the request/response correlation between outbound and inbound data

## Key Files

| File | Purpose |
|------|---------|
| `src/TurboHttp/Streams/Engine.cs` | Top-level pipeline builder — stacks feature BidiFlows via `Atop` |
| `src/TurboHttp/Streams/ProtocolCoreGraphBuilder.cs` | Version-demux graph: Partition → 4 protocol flows → Merge |
| `src/TurboHttp/Streams/PipelineDescriptor.cs` | Aggregates optional policies for conditional BidiFlow insertion |
| `src/TurboHttp/Streams/IProtocolEngine.cs` | Interface for per-version BidiFlow factories |
| `src/TurboHttp/Streams/Http10Engine.cs` | HTTP/1.0 BidiFlow assembly |
| `src/TurboHttp/Streams/Http11Engine.cs` | HTTP/1.1 BidiFlow assembly |
| `src/TurboHttp/Streams/Http20Engine.cs` | HTTP/2 BidiFlow assembly |
| `src/TurboHttp/Streams/Http30Engine.cs` | HTTP/3 BidiFlow assembly |

## Full Pipeline Data Flow

```text
HttpRequestMessage
       │
       ▼
┌─────────────────────────────────────────────────────────────┐
│                    Feature BidiFlow Chain                   │
│  (outermost → innermost, composed via Atop)                 │
│                                                             │
│  ┌──────────────┐                                           │
│  │   Tracing    │  Creates root "TurboHttp.Request" Activity│
│  └──────┬───────┘                                           │
│  ┌──────┴───────┐                                           │
│  │  Handler[0]  │  User middleware (outermost)              │
│  │  Handler[N]  │  User middleware (innermost)              │
│  └──────┬───────┘                                           │
│  ┌──────┴───────┐                                           │
│  │   Redirect   │  RFC 9110 §15.4 — internal feedback loop  │
│  └──────┬───────┘                                           │
│  ┌──────┴───────┐                                           │
│  │    Cookie    │  RFC 6265 §5.3–§5.4 — jar inject/extract  │
│  └──────┬───────┘                                           │
│  ┌──────┴───────┐                                           │
│  │    Retry     │  RFC 9110 §9.2 — internal feedback loop   │
│  └──────┬───────┘                                           │
│  ┌──────┴───────┐                                           │
│  │ Expect 100   │  RFC 9110 §10.1.1 — Expect: 100-continue  │
│  └──────┬───────┘                                           │
│  ┌──────┴───────┐                                           │
│  │    Cache     │  RFC 9111 — short-circuit on cache hit    │
│  └──────┬───────┘                                           │
│  ┌──────┴───────┐                                           │
│  │  Content     │  RFC 9110 §8.4 — compress req / decomp res│
│  │  Encoding    │                                           │
│  └──────┬───────┘                                           │
└─────────┼───────────────────────────────────────────────────┘
          │
          ▼
┌─────────────────────────────────────────────────────────────┐
│              Protocol Engine Core (Island 2)                │
│                                                             │
│  ┌────────────────────────┐                                 │
│  │ RequestEnricherStage   │  Applies defaults, base address │
│  └────────┬───────────────┘                                 │
│           ▼                                                 │
│  ┌──── Partition (by Version) ────┐                         │
│  │  Out0: 1.0  Out1: 1.1  Out2: 2.0  Out3: 3.0              │
│  └──┬────────┬────────┬────────┬──┘                         │
│     ▼        ▼        ▼        ▼                            │
│  ┌──────┐┌──────┐┌──────┐┌──────┐                           │
│  │H10   ││H11   ││H20   ││H30   │  Per-version subflow      │
│  │Engine││Engine││Engine││Engine│                           │
│  └──┬───┘└──┬───┘└──┬───┘└──┬───┘                           │
│     └───┬────┘───┬────┘───┬────┘                            │
│         ▼                                                   │
│  ┌── Merge<HttpResponseMessage>(4) ──┐                      │
│  └───────────────────────────────────┘                      │
└─────────────────────────────────────────────────────────────┘
          │
          ▼
HttpResponseMessage
```

## Stage Categories

### Encoding Stages (`Streams/Stages/Encoding/`)

Transform `HttpRequestMessage` into wire-format `DataItem` bytes.

| Stage | Protocol | Shape | Purpose |
|-------|----------|-------|---------|
| `Http10EncoderStage` | HTTP/1.0 | BidiFlow | Request → HTTP/1.0 text encoding |
| `Http11EncoderStage` | HTTP/1.1 | BidiFlow | Request → HTTP/1.1 text encoding |
| `Http20EncoderStage` | HTTP/2 | BidiFlow | Request → HPACK-compressed headers + DATA frames |
| `Http20PrependPrefaceStage` | HTTP/2 | Flow | Prepends connection preface (`PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n`) |
| `Http20Request2FrameStage` | HTTP/2 | Flow | Serializes `Http2Frame` structs to raw bytes |
| `Http30EncoderStage` | HTTP/3 | BidiFlow | Request → QPACK-compressed headers + DATA frames |
| `Http30ControlStreamPrefaceStage` | HTTP/3 | Flow | Prepends control stream type + SETTINGS frame |
| `Http30QpackEncoderPrefaceStage` | HTTP/3 | Flow | Prepends QPACK encoder stream type byte |
| `Http30Request2FrameStage` | HTTP/3 | Flow | Serializes `Http3Frame` structs to raw bytes |
| `QpackEncoderStreamStage` | HTTP/3 | Flow | Processes QPACK encoder instructions |

### Decoding Stages (`Streams/Stages/Decoding/`)

Transform inbound `DataItem` bytes into `HttpResponseMessage`.

| Stage | Protocol | Shape | Purpose |
|-------|----------|-------|---------|
| `Http10DecoderStage` | HTTP/1.0 | BidiFlow | HTTP/1.0 text → response headers + body |
| `Http11DecoderStage` | HTTP/1.1 | BidiFlow | HTTP/1.1 text → response (chunked, content-length, close-delimited) |
| `Http20DecoderStage` | HTTP/2 | BidiFlow | Raw bytes → `Http2Frame` → response headers + DATA |
| `Http20StreamStage` | HTTP/2 | BidiFlow | Per-stream frame routing and reassembly |
| `Http20ConnectionStage` | HTTP/2 | Custom | Connection-level frame handling (SETTINGS, PING, GOAWAY, WINDOW_UPDATE) |
| `Http30DecoderStage` | HTTP/3 | BidiFlow | Raw bytes → `Http3Frame` → response headers + DATA |
| `Http30StreamStage` | HTTP/3 | BidiFlow | Per-stream frame routing |
| `Http30ConnectionStage` | HTTP/3 | Custom | Connection-level frame handling for QUIC |
| `QpackDecoderStreamStage` | HTTP/3 | Flow | Processes inbound QPACK decoder instructions |
| `QpackDecoderFeedbackStage` | HTTP/3 | Sink | Acknowledges QPACK table updates |

### Feature Stages (`Streams/Stages/Features/`)

Cross-cutting HTTP features implemented as BidiFlows.

| Stage | RFC | Shape | Purpose |
|-------|-----|-------|---------|
| `TracingBidiStage` | — | BidiFlow | Root `Activity` lifecycle per request |
| `HandlerBidiStage` | — | BidiFlow | Wraps user `TurboHandler` middleware |
| `RedirectBidiStage` | RFC 9110 §15.4 | BidiFlow | Follows redirects with internal feedback loop |
| `CookieBidiStage` | RFC 6265 §5.3–§5.4 | BidiFlow | Injects/extracts cookies via `CookieJar` |
| `RetryBidiStage` | RFC 9110 §9.2 | BidiFlow | Retries idempotent requests with internal feedback loop |
| `ExpectContinueBidiStage` | RFC 9110 §10.1.1 | BidiFlow | Manages `Expect: 100-continue` handshake |
| `CacheBidiStage` | RFC 9111 | BidiFlow | Short-circuits on cache hit; stores responses |
| `ContentEncodingBidiStage` | RFC 9110 §8.4 | BidiFlow | Request compression + response decompression |
| `ConnectionReuseStage` | RFC 9112 §9 | Flow | Evaluates keep-alive/close per response |
| `DeadlockWatchdogStage` | — | Flow | DEBUG-only: detects pipeline stalls |

### Routing Stages (`Streams/Stages/Routing/`)

Control request/response routing within the pipeline.

| Stage | Purpose |
|-------|---------|
| `RequestEnricherStage` | Applies `TurboRequestOptions` defaults to outgoing requests |
| `ExtractOptionsStage` | Splits first request into `ConnectItem` signal + request stream; handles reconnect feedback |
| `GroupByHostKeyStage` | Groups requests by `RequestEndpoint` into per-host substreams |
| `MergeSubstreamsStage` | Merges per-host response substreams back into a single stream |
| `HostKeyGroupByExtensions` | Extension methods for fluent `GroupByHostKey` syntax |
| `HostKeyMergeBack` | Merge-back helper for host-grouped substreams |
| `Http1XCorrelationStage` | Correlates HTTP/1.x request/response pairs (one-at-a-time) |
| `Http20CorrelationStage` | Correlates HTTP/2 requests/responses by stream ID |
| `Http20StreamIdAllocatorStage` | Assigns odd stream IDs to HTTP/2 client requests |
| `Http30CorrelationStage` | Correlates HTTP/3 requests/responses by QUIC stream |
| `Http30StreamDemuxStage` | Routes tagged output to correct QUIC stream type |

## BidiFlow Stacking Pattern

Feature stages are composed via `Atop` — each BidiFlow wraps the next, forming a bidirectional pipeline:

```text
Request direction:  Handler[0] → Handler[N] → Redirect → Cookie → Retry → Expect100 → Cache → ContentEncoding → Engine
Response direction: Engine → ContentEncoding → Cache → Expect100 → Retry → Cookie → Redirect → Handler[N] → Handler[0]
```

Only BidiFlows for non-null policies are included. The stacking is built from innermost to outermost in `Engine.BuildExtendedPipeline()`.

## Per-Version Engine Assembly

Each `IHttpProtocolEngine` implementation assembles a `BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed>`:

```text
HTTP/1.0: Http10EncoderStage ↔ Http10DecoderStage + Http1XCorrelationStage
HTTP/1.1: Http11EncoderStage ↔ Http11DecoderStage + Http1XCorrelationStage
HTTP/2:   Http20EncoderStage + PrependPreface + Request2Frame
          ↔ Http20DecoderStage + ConnectionStage + StreamStage + CorrelationStage + StreamIdAllocator
HTTP/3:   Http30EncoderStage + ControlStreamPreface + QpackEncoderPreface + Request2Frame
          ↔ Http30DecoderStage + ConnectionStage + StreamStage + CorrelationStage + StreamDemux
          + QpackDecoderStream + QpackDecoderFeedback + QpackEncoderStream
```

## Per-Host Substreaming

`ProtocolCoreGraphBuilder` wraps each version's engine with `GroupByHostKey` → connection flow → `MergeSubstreams`. This materializes a **fresh pipeline copy per unique (host, port, scheme)** so connections are never mixed across hosts:

```text
Partition(version)
    │
    ▼
GroupByHostKey(RequestEndpoint.FromRequest, maxSubstreams)
    │
    ├── Substream host-A ──► Engine BidiFlow ◄──► ConnectionStage ──► TCP
    ├── Substream host-B ──► Engine BidiFlow ◄──► ConnectionStage ──► TCP
    └── ...
    │
MergeSubstreams
    │
    ▼
Merge(4 versions)
```

## Design Decisions

### BidiFlow over DelegatingHandler

Using Akka.Streams BidiFlows for features (redirect, retry, cache) instead of .NET's `DelegatingHandler` chain provides:
- **Backpressure-aware** — features naturally participate in stream flow control
- **Bidirectional** — a single stage intercepts both request and response paths
- **Composable** — `Atop` stacking is associative and order-independent for non-interacting features

### Async Boundary at Engine Core

`ProtocolCoreGraphBuilder.Build()` wraps the engine flow in `Attributes.CreateAsyncBoundary()`, ensuring the protocol engine runs on its own dispatcher. This prevents slow encode/decode work from blocking the feature BidiFlow chain.

### DEBUG-Only DeadlockWatchdogStage

In debug builds, `DeadlockWatchdogStage<T>` is inserted at three pipeline points to detect backpressure stalls. It fires `TurboHttpDiagnosticListener.OnDeadlockStall` if no element flows within `WarningThreshold` (default 10s). Removed in release builds to avoid overhead.

## Known Limitations

- **No dynamic pipeline reconfiguration** — policies are fixed at pipeline materialization time
- **GroupByHostKey maxSubstreams** — HTTP/1.x allows 256, HTTP/2/3 allows 64; exceeding these requires substream eviction
- **Single async boundary** — all protocol versions share one boundary; under extreme load, version contention is possible

## Integration Points

| Component | Interaction |
|-----------|-------------|
| [[Architecture/13-CLIENT_LAYER|Client Layer]] | `Engine.CreateFlow()` is the main entry point |
| [[Architecture/14-TRANSPORT_LAYER|Transport Layer]] | `ConnectionStage` wired inside each per-host substream |
| [[Architecture/16-PROTOCOL_LAYER|Protocol Layer]] | Encoder/decoder stages use `Protocol/` classes for wire format |
| [[Architecture/17-DIAGNOSTICS_INTEGRATION|Diagnostics]] | `TracingBidiStage` + `DeadlockWatchdogStage` emit diagnostic events |

## See Also

- [[Architecture/02-STAGE_PATTERNS|GraphStage Patterns]] — Port naming and stage lifecycle conventions
- [[Architecture/06-DECODER_PIPELINE_ARCHITECTURE|Decoder Pipeline Architecture]] — Three-layer decoder pattern
- [[Architecture/11-STAGE_COMPLETION_AUDIT|Stage Completion Audit]] — Completion propagation bug fixes
