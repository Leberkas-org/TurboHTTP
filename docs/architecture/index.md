# Architecture Overview

TurboHttp is a high-performance HTTP client library for .NET built on Akka.Streams. It implements HTTP/1.0, HTTP/1.1, and HTTP/2 with full RFC compliance across four composable layers.

<LikeC4Diagram viewId="index" :height="520" />

## System Boundaries

The diagram above shows the three actors in the system:

- **User** — any .NET application that creates and sends `HttpRequestMessage` objects
- **TurboHttp** — the library itself: four layers (Client, Streams, Protocol, I/O) encapsulated behind `ITurboHttpClient`
- **TCP Network** — the remote server; TurboHttp manages all TCP socket lifecycle internally

## Four-Layer Architecture

| Layer | Location | Responsibility |
|-------|----------|----------------|
| **Client** | `TurboHttp/Client/` | Public API — `ITurboHttpClient`, `SendAsync`, channel-based request/response |
| **Streams** | `TurboHttp/Streams/` | Akka.Streams `GraphStage` pipeline — version routing, encode/decode, correlation |
| **Protocol** | `TurboHttp/Protocol/` | Pure RFC logic — encoders, decoders, HPACK, cookies, caching, redirects, retries |
| **I/O** | `TurboHttp/IO/` | Hybrid: actors for lifecycle management, `System.Threading.Channels` for data |

Data flows **top-to-bottom** through the layers. Lifecycle management (connection pooling, reconnect, idle eviction) flows **bottom-up** via the actor hierarchy.

## Key Design Decisions

### Backpressure End-to-End

Every stage in the pipeline is a backpressure-aware Akka.Streams `GraphStage`. Demand propagates from the response consumer all the way back to the TCP reader — no unbounded buffers, no silent drops.

### Zero-Copy Data Path

The I/O layer uses `System.Threading.Channels` and `System.IO.Pipelines` to move bytes from TCP to the stream pipeline without copying through actor mailboxes. `ClientByteMover` runs three static async tasks per connection (TCP→Pipe, Pipe→InboundChannel, OutboundChannel→TCP), keeping the hot path allocation-free.

### RFC Compliance First

Every protocol decision in TurboHttp maps to a specific RFC section, with a corresponding test. The test suite covers RFC 1945 (HTTP/1.0), RFC 9112 (HTTP/1.1), RFC 9113 (HTTP/2), RFC 7541 (HPACK), RFC 9110 (HTTP Semantics), RFC 9111 (Caching), and RFC 6265 (Cookies).

## Explore Further

- [**Layers**](./layers) — Container view and per-layer details
- [**Engines**](./engines) — Per-protocol engine internals
- [**Pipeline Flow**](./pipeline) — Full pipeline with feedback loops
- [**Scenarios**](./scenarios) — End-to-end request walkthroughs
