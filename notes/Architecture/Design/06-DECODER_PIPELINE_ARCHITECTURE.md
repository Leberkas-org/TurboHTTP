---
title: Decoder Pipeline Architecture
description: >-
  Three-layer decoder architecture for HTTP/1.0, HTTP/1.1, and HTTP/2 —
  Pipeline, EventAggregator, CompletionDecoder pattern
tags:
  - architecture
  - decoder
  - protocol
  - pipeline
aliases:
  - Decoder Pipeline
  - Three-Layer Decoder
---
# Decoder Pipeline Architecture

**Last Updated**: 2026-03-26
**Status**: ✅ Complete (HTTP/1.0, HTTP/1.1, HTTP/2 all implemented)

## Three-Layer Pattern

Each protocol version follows the same three-layer architecture:

```
1. Pipeline        — Orchestrates frame/field parsing
2. Event Aggregator — Converts event stream → HttpResponseMessage
3. Completion Decoder — Convenience wrapper (Pipeline + Aggregator)
```

### Usage Patterns

| Pattern | Use When | API |
|---------|----------|-----|
| **Event Streaming** | Real-time body streaming, multiplexing | Use Pipeline directly |
| **Complete Response** | Simple request/response | `CompletionDecoder.Process() → HttpResponseMessage?` |

### Memory Patterns

- **Zero-Copy**: Body data is slices of input `ReadOnlyMemory<byte>`, not buffered
- **ArrayPool**: Headers buffered during parsing, released after complete response

## Implementations

### HTTP/1.1
- `Http11DecoderPipeline` + `Http11EventAggregator` + `Http11CompletionDecoder`

### HTTP/1.0
- `Http10DecoderPipeline` + `Http10EventAggregator` + `Http10CompletionDecoder`
- Extra: `MarkEof()` for EOF-based body boundaries (HTTP/1.0 has no Content-Length guarantee)

### HTTP/2
- `Http2DecoderPipeline` + `Http2EventAggregator` + `Http2CompletionDecoder`
- Extra: `Reset()` for connection reuse (HTTP/2 multiplexes on one connection)
