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
| **Scope** | 5 tasks (TASK-020-001 through TASK-020-005) |
| **Maggus Plan** | Not available |

## Description

Consolidated all HTTP response body decompression logic — which had accumulated in three separate protocol decoders and the original `DecompressionBidiStage` — into a single `ContentEncodingBidiStage` at the Streams layer. This gave decompression a single, well-tested, stream-native home.

| Task | Change |
|------|--------|
| TASK-020-001 | Removed decompression from `Http10Decoder` and `Http11Decoder` — they now pass `Content-Encoding` headers through unchanged |
| TASK-020-002 | Removed decompression from `Http20StreamStage` |
| TASK-020-003 | Removed decompression from `Http30StreamStage` |
| TASK-020-004 | Renamed all references from `DecompressionBidiStage` → `ContentEncodingBidiStage`; updated pipeline wiring in `ProtocolCoreGraphBuilder` |
| TASK-020-005 | End-to-end verification — all compression integration tests pass after consolidation |

**Before**: Decompression scattered across Http10Decoder, Http11Decoder, Http20StreamStage, Http30StreamStage, and DecompressionBidiStage.  
**After**: Single `ContentEncodingBidiStage` handles all encodings (gzip, deflate, brotli, identity, unknown pass-through).

## Key Source Files

| File | Role |
|------|------|
| `src/TurboHttp/Streams/Stages/Features/ContentEncodingBidiStage.cs` | Consolidated decompression stage |
| `src/TurboHttp/Streams/Routing/ProtocolCoreGraphBuilder.cs` | Pipeline wiring updated |
| `src/TurboHttp/Protocol/RFC9110/Http10Decoder.cs` | Decompression removed |
| `src/TurboHttp/Protocol/RFC9113/Http20StreamStage.cs` | Decompression removed |

## See Also

- [[Features/Feature003_Decompression_Stage\|Feature 003]] — original `DecompressionStage` (superseded by this)
- [[Architecture/15-STREAMS_LAYER\|Streams Layer]] — stage layer responsibilities
- [[Architecture/16-PROTOCOL_LAYER\|Protocol Layer]] — what remains in protocol decoders after this refactor
