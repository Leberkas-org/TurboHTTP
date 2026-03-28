---
title: Protocol Layer Architecture
description: >-
  Encoder/decoder patterns, HPACK/QPACK internals, RFC subfolder structure, and
  wire-format handling for HTTP/1.x, HTTP/2, and HTTP/3
tags:
  - architecture
  - protocol
  - encoders
  - decoders
  - hpack
  - qpack
---
# Protocol Layer Architecture

## Purpose

The Protocol layer (`src/TurboHttp/Protocol/`) implements wire-format encoding and decoding for all supported HTTP versions. Each RFC gets its own subfolder containing encoders, decoders, and version-specific business logic. Shared compression codecs (HPACK, QPACK, Huffman) live in dedicated RFC subfolders and are consumed by multiple protocol versions.

This layer sits **below** the Streams layer (which orchestrates stage graphs) and **above** the Transport layer (which moves raw bytes). Protocol types convert between `HttpRequestMessage`/`HttpResponseMessage` and the `IOutputItem`/`IInputItem` message protocol used by the pipeline.

> **Extends, does not repeat**: For how protocol flows are composed into the pipeline, see [[Architecture/15-STREAMS_LAYER|Streams Layer]]. For the three-layer decoder pattern, see [[Architecture/06-DECODER_PIPELINE_ARCHITECTURE|Decoder Pipeline Architecture]].

---

## Key Files

| Component | Path | Role |
|-----------|------|------|
| HTTP/1.1 Encoder | `Protocol/RFC9112/Http11Encoder.cs` | Serialises requests to HTTP/1.1 wire format |
| HTTP/1.1 Decoder | `Protocol/RFC9112/Http11Decoder.cs` | Parses HTTP/1.1 responses from byte stream |
| HTTP/1.0 Encoder | `Protocol/RFC9112/Http10Encoder.cs` | HTTP/1.0 request serialisation (no chunked) |
| HTTP/1.0 Decoder | `Protocol/RFC9112/Http10Decoder.cs` | HTTP/1.0 response parsing (Content-Length only) |
| HTTP/2 Encoder | `Protocol/RFC9113/Http2Encoder.cs` | Frames requests into HTTP/2 binary format |
| HTTP/2 Decoder | `Protocol/RFC9113/Http2Decoder.cs` | Parses HTTP/2 frames into response events |
| HTTP/2 Settings | `Protocol/RFC9113/Http2Settings.cs` | SETTINGS frame parameter handling |
| HTTP/2 Flow Control | `Protocol/RFC9113/Http2FlowControl.cs` | Window update tracking per stream/connection |
| HTTP/3 Encoder | `Protocol/RFC9114/Http3Encoder.cs` | QUIC-based HTTP/3 frame encoding |
| HTTP/3 Decoder | `Protocol/RFC9114/Http3Decoder.cs` | HTTP/3 frame parsing from QUIC streams |
| HPACK Encoder | `Protocol/RFC7541/HpackEncoder.cs` | HTTP/2 header compression (RFC 7541) |
| HPACK Decoder | `Protocol/RFC7541/HpackDecoder.cs` | HTTP/2 header decompression |
| QPACK Encoder | `Protocol/RFC9204/QpackEncoder.cs` | HTTP/3 header compression (RFC 9204) |
| QPACK Decoder | `Protocol/RFC9204/QpackDecoder.cs` | HTTP/3 header decompression |
| Huffman Codec | `Protocol/RFC7541/HuffmanCodec.cs` | Shared Huffman encoding/decoding for HPACK/QPACK |
| Dynamic Table | `Protocol/RFC7541/DynamicTable.cs` | HPACK dynamic header table |
| Static Table | `Protocol/RFC7541/StaticTable.cs` | HPACK static header table (61 entries) |
| Decode Result | `Protocol/Shared/HttpDecodeResult.cs` | Discriminated union for decoder output states |

---

## Data Flow

```text
┌─────────────────────────────────────────────────────────┐
│                    Streams Layer                        │
│  (GraphStages: EncoderStage / DecoderStage wrappers)    │
└────────────┬────────────────────────────┬───────────────┘
             │ HttpRequestMessage         │ HttpResponseMessage
             ▼                            ▲
┌────────────────────────┐  ┌─────────────────────────────┐
│     Protocol Encoder   │  │      Protocol Decoder       │
│                        │  │                             │
│  1. Serialise headers  │  │  1. Parse frame/line        │
│     (HPACK/QPACK/text) │  │  2. Decompress headers      │
│  2. Frame body         │  │     (HPACK/QPACK/text)      │
│  3. Emit IOutputItem   │  │  3. Assemble response       │
│     (DataItem bytes)   │  │  4. Emit HttpResponseMessage│
└────────────┬───────────┘  └─────────────┬───────────────┘
             │ IOutputItem                │ IInputItem
             ▼                            ▲
┌─────────────────────────────────────────────────────────┐
│                   Transport Layer                       │
│           (ConnectionStage → TCP/QUIC)                  │
└─────────────────────────────────────────────────────────┘
```

### Header Compression Flow (HTTP/2)

```text
Request Headers ──► HpackEncoder ──► HEADERS frame bytes
                        │
                    DynamicTable
                    (shared state)
                        │
Response HEADERS ──► HpackDecoder ──► Decoded Headers
```

### Header Compression Flow (HTTP/3)

```text
Request Headers ──► QpackEncoder ──► HEADERS + Encoder Stream
                        │                    │
                    DynamicTable         QPACK instructions
                        │                    │
                        ▼                    ▼
Response HEADERS ◄── QpackDecoder ◄── Decoder Stream feedback
```

---

## Encoder/Decoder Pattern

All protocol versions follow a consistent pattern:

### Encoder Contract

1. **Input**: `HttpRequestMessage` from the Streams layer
2. **Header serialisation**: Version-specific format (text lines for HTTP/1.x, HPACK-compressed HEADERS frames for HTTP/2, QPACK-compressed for HTTP/3)
3. **Body framing**: Identity/chunked (HTTP/1.x), DATA frames with flow control (HTTP/2), DATA frames on QUIC streams (HTTP/3)
4. **Output**: `IOutputItem` (typically `DataItem` wrapping `IMemoryOwner<byte>`) to Transport

### Decoder Contract

1. **Input**: `IInputItem` (raw bytes from Transport)
2. **Frame/line parsing**: Extract protocol units (HTTP/1.x lines, HTTP/2 frames, HTTP/3 frames)
3. **Header decompression**: Reverse of encoder header compression
4. **Response assembly**: Build `HttpResponseMessage` with headers and body stream
5. **Output**: `HttpResponseMessage` to Streams layer

### Three-Layer Decoder Architecture

HTTP/2 and HTTP/3 decoders use a three-layer pipeline (detailed in [[Architecture/06-DECODER_PIPELINE_ARCHITECTURE|Decoder Pipeline Architecture]]):

```text
ConnectionStage (connection-level frames: SETTINGS, GOAWAY, PING)
    └── StreamStage (per-stream demux and state machine)
        └── DecoderStage (frame → HttpResponseMessage assembly)
```

### `HttpDecodeResult` Discriminated Union

Decoders return `HttpDecodeResult` to signal parsing state:

- **`NeedMoreData`** — Incomplete frame/message; request more bytes
- **`HeadersComplete`** — Headers fully parsed; body may follow
- **`Complete`** — Full response assembled
- **`Error`** — Protocol violation detected

---

## HPACK Internals (RFC 7541)

HPACK compresses HTTP/2 headers using a combination of:

1. **Static Table** — 61 pre-defined header name/value pairs (e.g., `:method: GET`, `:status: 200`)
2. **Dynamic Table** — FIFO table of recently-seen headers, bounded by `SETTINGS_HEADER_TABLE_SIZE`
3. **Huffman Coding** — Optional per-octet Huffman encoding using the RFC 7541 code table

### Encoding Decisions

The encoder chooses per-header:
- **Indexed** (1 byte reference) — header exists in static or dynamic table
- **Literal with indexing** — header added to dynamic table for future reference
- **Literal without indexing** — transient headers (e.g., `:path`) not worth caching
- **Literal never indexed** — sensitive headers (e.g., `Authorization`) excluded from compression

### Dynamic Table Eviction

When a new entry exceeds `SETTINGS_HEADER_TABLE_SIZE`, oldest entries are evicted FIFO. The table size can be updated mid-connection via SETTINGS frames, triggering immediate eviction.

---

## QPACK Internals (RFC 9204)

QPACK adapts HPACK for HTTP/3's unordered QUIC streams:

1. **Static Table** — Extended to 99 entries (superset of HPACK's 61)
2. **Dynamic Table** — Same concept but with **out-of-order insertion acknowledgment**
3. **Encoder Stream** — Unidirectional QUIC stream carrying table update instructions
4. **Decoder Stream** — Unidirectional QUIC stream carrying insertion acknowledgments

### Key Difference from HPACK

HPACK relies on TCP ordering — encoder and decoder see frames in the same order, so dynamic table state is always synchronised. QPACK cannot assume ordering, so it uses:

- **Required Insert Count** — Each HEADERS block declares how many dynamic table inserts it depends on
- **Blocked streams** — Decoder may block a stream until the required inserts arrive on the encoder stream
- **Section Acknowledgment** — Decoder tells encoder which HEADERS blocks it has processed, allowing encoder to evict safely

---

## RFC Subfolder Structure

```text
src/TurboHttp/Protocol/
├── RFC7541/          # HPACK (HTTP/2 header compression)
│   ├── HpackEncoder.cs
│   ├── HpackDecoder.cs
│   ├── DynamicTable.cs
│   ├── StaticTable.cs
│   └── HuffmanCodec.cs
├── RFC9110/          # HTTP Semantics (shared across versions)
│   └── (status codes, method handling, content negotiation)
├── RFC9112/          # HTTP/1.1 Message Syntax and Routing
│   ├── Http11Encoder.cs
│   ├── Http11Decoder.cs
│   ├── Http10Encoder.cs
│   └── Http10Decoder.cs
├── RFC9113/          # HTTP/2
│   ├── Http2Encoder.cs
│   ├── Http2Decoder.cs
│   ├── Http2Settings.cs
│   └── Http2FlowControl.cs
├── RFC9114/          # HTTP/3
│   ├── Http3Encoder.cs
│   ├── Http3Decoder.cs
│   └── (QUIC stream management)
├── RFC9204/          # QPACK (HTTP/3 header compression)
│   ├── QpackEncoder.cs
│   ├── QpackDecoder.cs
│   └── (encoder/decoder stream handling)
└── Shared/           # Cross-version types
    └── HttpDecodeResult.cs
```

### Naming Convention

- Encoders: `Http{version}Encoder.cs` — one per wire-format version
- Decoders: `Http{version}Decoder.cs` — paired with encoder
- RFC subfolder name matches the RFC number (e.g., `RFC9113/` for HTTP/2)

---

## Design Decisions

1. **RFC-per-folder organisation** — Each RFC gets its own namespace and folder, making compliance tracking straightforward. Cross-cutting concerns (Huffman, shared types) live in the lowest-numbered RFC that defines them.

2. **Stateless encoders, stateful decoders** — Encoders are largely stateless (HPACK/QPACK state is injected). Decoders maintain parsing state machines because responses can arrive incrementally across multiple `IInputItem` deliveries.

3. **`IMemoryOwner<byte>` for zero-copy** — Encoded output uses pooled memory (`ArrayPool<byte>`) wrapped in `IMemoryOwner<byte>` to minimise allocations on the hot path. The Transport layer returns buffers to the pool after writing to the socket.

4. **Shared Huffman codec** — `HuffmanCodec` is used by both HPACK and QPACK since they share the same Huffman table (RFC 7541 Appendix B). This avoids code duplication and ensures consistent encoding.

5. **Separate QPACK streams** — HTTP/3 QPACK uses dedicated unidirectional QUIC streams for encoder/decoder communication. These are modelled as separate GraphStages (`QpackEncoderStreamStage`, `QpackDecoderStreamStage`) in the Streams layer, keeping protocol logic in the Protocol layer and stream orchestration in Streams.

---

## Known Limitations

- **No server push** — HTTP/2 server push (PUSH_PROMISE) is parsed but not acted upon; frames are discarded. This matches industry trend (Chrome disabled server push in 2022).
- **QPACK blocked streams limit** — Currently hardcoded; not configurable via `SETTINGS_QPACK_BLOCKED_STREAMS`. Sufficient for typical client usage but may need tuning for high-concurrency scenarios.
- **HTTP/1.0 no chunked transfer** — By RFC, HTTP/1.0 does not support chunked encoding. The encoder uses Content-Length only, which requires the full body to be buffered before sending.
- **Dynamic table size negotiation** — HPACK/QPACK dynamic table sizes respect server SETTINGS but the client does not proactively reduce table size to save memory on idle connections.

---

## Integration Points

| Boundary | Direction | Contract |
|----------|-----------|----------|
| Streams → Protocol | Inbound | `HttpRequestMessage` via `IHttpProtocolEngine.CreateFlow()` BidiFlow |
| Protocol → Transport | Outbound | `IOutputItem` (`DataItem`, `ConnectItem`) via BidiFlow outlet |
| Transport → Protocol | Inbound | `IInputItem` (`DataItem` with raw bytes) via BidiFlow inlet |
| Protocol → Streams | Outbound | `HttpResponseMessage` via BidiFlow outlet |
| HPACK ↔ HTTP/2 Encoder/Decoder | Internal | `HpackEncoder`/`HpackDecoder` injected into HTTP/2 codec |
| QPACK ↔ HTTP/3 Encoder/Decoder | Internal | `QpackEncoder`/`QpackDecoder` + dedicated stream stages |
| Huffman ↔ HPACK/QPACK | Internal | `HuffmanCodec` shared static utility |

---

## See Also

- [[Architecture/06-DECODER_PIPELINE_ARCHITECTURE|Decoder Pipeline Architecture]] — Three-layer decoder pattern in detail
- [[Architecture/15-STREAMS_LAYER|Streams Layer]] — GraphStage wrappers that host protocol encoders/decoders
- [[Architecture/14-TRANSPORT_LAYER|Transport Layer]] — Raw byte transport below the protocol layer
- [[Architecture/02-STAGE_PATTERNS|GraphStage Patterns]] — Port naming and stage lifecycle conventions
- [[Architecture/11-STAGE_COMPLETION_AUDIT|Stage Completion Audit]] — Completion propagation bugs found in protocol stages
