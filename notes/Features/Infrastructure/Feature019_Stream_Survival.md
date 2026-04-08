---
title: "Feature 019: Stream Survival — Error Absorption"
description: "Hardened all pipeline stages to absorb upstream failures rather than propagating them, preventing full stream teardown on individual request errors"
tags: [features, history, error-handling, akka-streams, resilience, bugfix]
status: completed
---

# Feature 019: Stream Survival — Error Absorption

## Summary

| Field | Value |
|-------|-------|
| **Status** | ✅ Completed |
| **Category** | Resilience / Bug Fix |
| **Scope** | 6 steps |

## Description

Hardened the Akka.Streams pipeline so that individual request failures do not tear down the entire stream. Previously, if a stage received an upstream failure signal (e.g., connection write error), it propagated via Akka's default `onUpstreamFailure` behavior, killing the whole pipeline. This caused all in-flight requests to fail whenever a single connection error occurred.

| # | Stage Fixed |
|---|------------|
| 1 | `ConnectionStage` — absorb outbound write failures (log + recover, not propagate) |
| 2 | `TracingBidiStage` — absorb upstream failure on response path |
| 3 | Correlation stages (`CorrelationHttp1XStage`, `CorrelationHttp20Stage`) — absorb upstream failures |
| 4 | Version router — block HTTP/3 with `NotSupportedException` instead of `FailStage` |
| 5 | `Http30ConnectionStage` — replace `FailStage` with log + absorb pattern |
| 6 | End-to-end verification — fixed final `FailStage` in `Http3ConnectionStage` |

The fix pattern: override `onUpstreamFailure`, log the error, and call `CompleteStage()` rather than `FailStage(cause)`. This keeps downstream stages alive for subsequent requests.

## Key Source Files

| File | Role |
|------|------|
| `src/TurboHTTP/Streams/Stages/Routing/ConnectionStage.cs` | Outbound write failure absorption |
| `src/TurboHTTP/Streams/Stages/Features/TracingBidiStage.cs` | Response path failure absorption |
| `src/TurboHTTP/Streams/Stages/Routing/CorrelationHttp1XStage.cs` | HTTP/1.x correlation absorption |
| `src/TurboHTTP/Streams/Stages/Routing/CorrelationHttp20Stage.cs` | HTTP/2 correlation absorption |

## See Also

- [[Features/Protocol/Feature017_ConnectionStage_Race\|Feature 017]] — related ConnectionStage fix
- [[Architecture/Layers/15-STREAMS_LAYER\|Streams Layer]] — stage error handling patterns
- [[Architecture/Status/03-KNOWN_GAPS_AND_LIMITATIONS\|Known Gaps & Limitations]] — remaining stream lifecycle issues
