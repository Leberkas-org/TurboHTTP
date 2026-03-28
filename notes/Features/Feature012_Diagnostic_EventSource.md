---
title: "Feature 012: DiagnosticListener and ETW EventSource Diagnostics"
description: "Low-level ETW EventSource and DiagnosticListener infrastructure for production diagnostics and tooling integration"
tags: [features, history, diagnostics, etw, eventsource, diagnosticlistener]
status: completed
---

# Feature 012: DiagnosticListener and ETW EventSource Diagnostics

## Summary

| Field | Value |
|-------|-------|
| **Status** | ✅ Completed (partially consolidated into TracingBidiStage in [[Features/Feature016_TracingBidi_Consolidation\|Feature 016]]) |
| **Category** | Observability / Diagnostics |
| **Scope** | 4 tasks (TASK-012-001 through TASK-012-004) |
| **Maggus Plan** | Not available |

## Description

Added low-level observability infrastructure using both ETW `EventSource` and the `DiagnosticListener` pattern — the same approach used by `HttpClient` and ASP.NET Core.

- **TASK-012-001**: Added `TurboHttpEventSource` (`[EventSource(Name = "TurboHttp")]`) for ETW/EventPipe diagnostics consumable by `dotnet-trace`, PerfView, and Application Insights
- **TASK-012-002**: Added `TurboHttpDiagnosticListener` for programmatic in-process event subscription (same pattern as `System.Net.Http` DiagnosticListener)
- **TASK-012-003**: Wired both into pipeline stages and the transport layer
- **TASK-012-004**: Added unit tests verifying EventSource event payloads and DiagnosticListener subscription/unsubscription

## Key Source Files

| File | Role |
|------|------|
| `src/TurboHttp/Diagnostics/TurboHttpEventSource.cs` | ETW EventSource (later moved to TracingBidiStage) |
| `src/TurboHttp/Diagnostics/TurboHttpDiagnosticListener.cs` | DiagnosticListener (later moved to TracingBidiStage) |
| `src/TurboHttp.Tests/Diagnostics/DiagnosticsUnitTests.cs` | Diagnostic infrastructure tests |

## See Also

- [[Features/Feature016_TracingBidi_Consolidation\|Feature 016]] — consolidated EventSource + DiagnosticListener into `TracingBidiStage`
- [[Architecture/17-DIAGNOSTICS_INTEGRATION\|Diagnostics Integration]] — full observability stack
