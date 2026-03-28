---
title: "Feature 016: TracingBidiStage Consolidation"
description: "Consolidated EventSource and DiagnosticListener from Diagnostics/ into a single TracingBidiStage, simplified HandlerBidiStage to pure pass-through"
tags: [features, history, tracing, refactoring, bidi-stage, architecture]
status: completed
---

# Feature 016: TracingBidiStage Consolidation

## Summary

| Field | Value |
|-------|-------|
| **Status** | ✅ Completed |
| **Category** | Architecture Refactoring |
| **Scope** | 2 tasks (TASK-016-001 through TASK-016-002) |
| **Maggus Plan** | Not available |

## Description

Consolidated the diagnostics infrastructure introduced in [[Features/Feature012_Diagnostic_EventSource\|Feature 012]] into a dedicated pipeline stage, and simplified the handler bridge.

- **TASK-016-001**: Moved `TurboHttpEventSource` and `TurboHttpDiagnosticListener` from the `Diagnostics/` folder into `TracingBidiStage` — a `GraphStage<BidiShape>` that wraps the request/response flow and emits diagnostic events as data passes through. This aligned diagnostics with the stream-native architecture rather than side-effecting from external hooks.

- **TASK-016-002**: Simplified `HandlerBidiStage` to a pure pass-through wrapper around `DelegatingHandler` — removed logic that had accumulated in the stage and pushed it into the handler chain where it belongs. The stage became a thin adapter between Akka.Streams and the `HttpMessageHandler` model.

## Key Source Files

| File | Role |
|------|------|
| `src/TurboHttp/Streams/Stages/Features/TracingBidiStage.cs` | Consolidated diagnostics stage |
| `src/TurboHttp/Streams/Stages/Features/HandlerBidiStage.cs` | Simplified handler bridge |

## See Also

- [[Features/Feature012_Diagnostic_EventSource\|Feature 012]] — original diagnostics implementation
- [[Architecture/15-STREAMS_LAYER\|Streams Layer]] — stage composition and BidiFlow pipeline
- [[Architecture/17-DIAGNOSTICS_INTEGRATION\|Diagnostics Integration]] — observability stack
