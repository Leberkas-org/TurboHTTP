---
title: "Feature 003: Decompression Stage"
description: "Initial HTTP response body decompression stage (RFC 9110 §8.4) — superseded by Feature 020"
tags: [features, history, streams, decompression, rfc9110]
status: completed
---

# Feature 003: Decompression Stage

## Summary

| Field | Value |
|-------|-------|
| **Status** | ✅ Completed (superseded by [[Features/Protocol/Feature020_ContentEncoding_Consolidation\|Feature 020]]) |
| **Category** | Pipeline Stage |
| **Scope** | Single commit |

## Description

Introduced `DecompressionStage`, an Akka.Streams `GraphStage<FlowShape<HttpResponseMessage, HttpResponseMessage>>` that decompresses HTTP response bodies per RFC 9110 §8.4. The stage delegated to the existing `ContentEncodingDecoder` for gzip, x-gzip, deflate, and brotli (br) encodings. After decompression, it removed the `Content-Encoding` header and updated `Content-Length`. Responses with no `Content-Encoding` or `identity` encoding passed through unchanged.

10 unit tests covered all supported encodings, header management, and multi-response scenarios.

> **Note**: This stage was later renamed to `DecompressionBidiStage` and ultimately replaced by `ContentEncodingBidiStage` in [[Features/Protocol/Feature020_ContentEncoding_Consolidation\|Feature 020]], which consolidated all content-encoding logic into a single BidiFlow stage.

## Key Source Files

| File | Role |
|------|------|
| `src/TurboHttp/Streams/Stages/DecompressionStage.cs` | Stage implementation (later removed) |
| `src/TurboHttp.StreamTests/Streams/DecompressionStageTests.cs` | Unit tests |

## See Also

- [[Features/Protocol/Feature020_ContentEncoding_Consolidation\|Feature 020]] — supersedes this stage
- [[Architecture/Layers/15-STREAMS_LAYER\|Streams Layer]] — stage categories and composition
