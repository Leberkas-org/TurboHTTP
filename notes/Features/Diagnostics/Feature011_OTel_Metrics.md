---
title: "Feature 011: OpenTelemetry Metrics (TurboHttpMetrics)"
description: "OpenTelemetry Meter-based metrics for request counts, latency, and connection pool utilisation"
tags: [features, history, metrics, opentelemetry, diagnostics, instrumentation]
status: completed
---

# Feature 011: OpenTelemetry Metrics (TurboHttpMetrics)

## Summary

| Field | Value |
|-------|-------|
| **Status** | ✅ Completed |
| **Category** | Observability / Diagnostics |
| **Scope** | 3 steps |

## Description

Added `System.Diagnostics.Metrics`-based metrics infrastructure using `TurboHttpMetrics`. Metrics were instrumented at the stage and pooling layers and exposed for consumption by OTel collectors and `dotnet-counters`.

- Added `TurboHttpMetrics` with `Meter` registration, counters, histograms for request count, latency, bytes sent/received
- Instrumented pipeline stages and the connection pooling layer with metric recording calls
- Added unit tests using `MeterListener` to verify metric names, units, and values under load

Metrics exposed under meter name `TurboHttp` with instruments following .NET OTel naming conventions (`turbohttp.request.count`, `turbohttp.request.duration`, etc.).

## Key Source Files

| File | Role |
|------|------|
| `src/TurboHttp/Diagnostics/TurboHttpMetrics.cs` | Meter and instrument definitions |
| `src/TurboHttp.Tests/Diagnostics/TurboHttpMetricsTests.cs` | MeterListener-based unit tests |

## See Also

- [[Features/Diagnostics/Feature010_Tracing_Infrastructure\|Feature 010]] — companion distributed tracing
- [[Features/Diagnostics/Feature012_Diagnostic_EventSource\|Feature 012]] — lower-level ETW/EventSource
- [[Architecture/Guides/17-DIAGNOSTICS_INTEGRATION\|Diagnostics Integration]] — full observability stack
