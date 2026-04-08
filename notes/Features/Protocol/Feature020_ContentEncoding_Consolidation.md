---
title: "Feature 020: ContentEncoding Architecture Consolidation"
description: "Consolidated scattered decompression logic from protocol decoders into a single ContentEncodingBidiStage at the stream layer"
tags: [features, history, architecture, refactoring, decompression, content-encoding, bidi-stage]
status: completed
---

# Feature 020: ContentEncoding Architecture Consolidation

## Summary

| Field | Value |
|-------|-------|
| **Status** | ✅ Completed |
| **Category** | Architecture Refactoring |
| **Scope** | 5 steps |

## Description

Consolidated all HTTP response body decompression logic — which had accumulated in three separate protocol decoders and the original `DecompressionBidiStage` — into a single `ContentEncodingBidiStage` at the Streams layer. This gave decompression a single, well-tested, stream-native home.

| # | Change |
|---|--------|
| 1 | Removed decompression from `Http10Decoder` and `Http11Decoder` — they now pass `Content-Encoding` headers through unchanged |
| 2 | Removed decompression from `Http20StreamStage` |
| 3 | Removed decompression from `Http30StreamStage` |
| 4 | Renamed all references from `DecompressionBidiStage` → `ContentEncodingBidiStage`; updated pipeline wiring in `ProtocolCoreGraphBuilder` |
| 5 | End-to-end verification — all compression integration tests pass after consolidation |

**Before**: Decompression scattered across Http10Decoder, Http11Decoder, Http20StreamStage, Http30StreamStage, and DecompressionBidiStage.  
**After**: Single `ContentEncodingBidiStage` handles all encodings (gzip, deflate, brotli, identity, unknown pass-through).

## Key Source Files

| File | Role |
|------|------|
| `src/TurboHTTP/Streams/Stages/Features/ContentEncodingBidiStage.cs` | Consolidated decompression stage |
| `src/TurboHTTP/Streams/Routing/ProtocolCoreGraphBuilder.cs` | Pipeline wiring updated |
| `src/TurboHTTP/Protocol/RFC9110/Http10Decoder.cs` | Decompression removed |
| `src/TurboHTTP/Protocol/RFC9113/Http20StreamStage.cs` | Decompression removed |

## See Also

- [[Features/Protocol/Feature003_Decompression_Stage\|Feature 003]] — original `DecompressionStage` (superseded by this)
- [[Architecture/Layers/15-STREAMS_LAYER\|Streams Layer]] — stage layer responsibilities
- [[Architecture/Layers/16-PROTOCOL_LAYER\|Protocol Layer]] — what remains in protocol decoders after this refactor
