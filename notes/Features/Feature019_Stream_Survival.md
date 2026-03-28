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
| **Scope** | 6 tasks (TASK-019-001 through TASK-019-006) |
| **Maggus Plan** | Not available |

## Description

Hardened the Akka.Streams pipeline so that individual request failures do not tear down the entire stream. Previously, if a stage received an upstream failure signal (e.g., connection write error), it propagated via Akka's default `onUpstreamFailure` behavior, killing the whole pipeline. This caused all in-flight requests to fail whenever a single connection error occurred.

| Task | Stage Fixed |
|------|------------|
| TASK-019-001 | `ConnectionStage` — absorb outbound write failures (log + recover, not propagate) |
| TASK-019-002 | `TracingBidiStage` — absorb upstream failure on response path |
| TASK-019-003 | Correlation stages (`CorrelationHttp1XStage`, `CorrelationHttp20Stage`) — absorb upstream failures |
| TASK-019-004 | Version router — block HTTP/3 with `NotSupportedException` instead of `FailStage` |
| TASK-019-005 | `Http30ConnectionStage` — replace `FailStage` with log + absorb pattern |
| TASK-019-006 | End-to-end verification — fixed final `FailStage` in `Http3ConnectionStage` |

The fix pattern: override `onUpstreamFailure`, log the error, and call `CompleteStage()` rather than `FailStage(cause)`. This keeps downstream stages alive for subsequent requests.

## Key Source Files

| File | Role |
|------|------|
| `src/TurboHttp/Streams/Stages/Routing/ConnectionStage.cs` | Outbound write failure absorption |
| `src/TurboHttp/Streams/Stages/Features/TracingBidiStage.cs` | Response path failure absorption |
| `src/TurboHttp/Streams/Stages/Routing/CorrelationHttp1XStage.cs` | HTTP/1.x correlation absorption |
| `src/TurboHttp/Streams/Stages/Routing/CorrelationHttp20Stage.cs` | HTTP/2 correlation absorption |

## See Also

- [[Features/Feature017_ConnectionStage_Race\|Feature 017]] — related ConnectionStage fix
- [[Architecture/15-STREAMS_LAYER\|Streams Layer]] — stage error handling patterns
- [[Architecture/03-KNOWN_GAPS_AND_LIMITATIONS\|Known Gaps & Limitations]] — remaining stream lifecycle issues
