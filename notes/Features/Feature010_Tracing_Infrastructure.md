---
title: "Feature 010: Tracing Infrastructure (TurboHttpInstrumentation)"
description: "OpenTelemetry ActivitySource distributed tracing wired into request lifecycle stages"
tags: [features, history, tracing, opentelemetry, diagnostics, instrumentation]
status: completed
---

# Feature 010: Tracing Infrastructure (TurboHttpInstrumentation)

## Summary

| Field | Value |
|-------|-------|
| **Status** | ✅ Completed |
| **Category** | Observability / Diagnostics |
| **Scope** | 3 tasks (TASK-010-001 through TASK-010-003) |
| **Maggus Plan** | Not available |

## Description

Added distributed tracing infrastructure using OpenTelemetry `ActivitySource`. The `TurboHttpInstrumentation` class became the central tracing entry point, emitting spans for request lifecycle events.

- **TASK-010-001**: Added `TurboHttpInstrumentation` class with `ActivitySource` registration and span creation helpers
- **TASK-010-002**: Instrumented request lifecycle in pipeline stages — request start/end, encoding, decoding, connection acquisition
- **TASK-010-003**: Added unit tests verifying span creation, propagation, and correct parent/child relationships using `ActivityListener`

Traces exposed via `TurboHttpInstrumentation.ActivitySourceName` for consumption by OTel collectors (Zipkin, OTLP, etc.).

## Key Source Files

| File | Role |
|------|------|
| `src/TurboHttp/Diagnostics/TurboHttpInstrumentation.cs` | ActivitySource and span helpers |
| `src/TurboHttp.Tests/Diagnostics/TurboHttpInstrumentationTests.cs` | Tracing unit tests |

## See Also

- [[Features/Feature011_OTel_Metrics\|Feature 011]] — companion metrics infrastructure
- [[Features/Feature012_Diagnostic_EventSource\|Feature 012]] — lower-level ETW/DiagnosticListener
- [[Architecture/17-DIAGNOSTICS_INTEGRATION\|Diagnostics Integration]] — full observability stack
