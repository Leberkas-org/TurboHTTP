---
title: "Feature 009: Akka Logging Bridge"
description: "Bridges Akka.NET internal logging to Microsoft.Extensions.Logging via Akka.Logger.Extensions.Logging"
tags: [features, history, logging, akka, infrastructure, hosting]
status: completed
---

# Feature 009: Akka Logging Bridge

## Summary

| Field | Value |
|-------|-------|
| **Status** | ✅ Completed |
| **Category** | Infrastructure / Observability |
| **Scope** | 3 tasks (TASK-009-001 through TASK-009-003) |
| **Maggus Plan** | Not available |

## Description

Integrated `Akka.Logger.Extensions.Logging` to bridge Akka.NET's internal actor system logging to the standard `Microsoft.Extensions.Logging` pipeline. This allowed Akka debug/info/error messages to appear in the same log output as ASP.NET Core and TurboHttp application logs.

- **TASK-009-001**: Added `Akka.Logger.Extensions.Logging` NuGet package
- **TASK-009-002**: Configured the logging bridge in the hosting layer (`TurboHttpServiceCollectionExtensions`)
- **TASK-009-003**: Added integration tests verifying Akka log messages flow through the bridge

## Key Source Files

| File | Role |
|------|------|
| `src/TurboHttp/Hosting/TurboHttpServiceCollectionExtensions.cs` | DI configuration for logging bridge |
| `src/TurboHttp.IntegrationTests/Diagnostics/AkkaLoggingBridgeTests.cs` | Bridge integration tests |

## See Also

- [[Features/Feature010_Tracing_Infrastructure\|Feature 010]] — OTel tracing (built on top of logging)
- [[Architecture/17-DIAGNOSTICS_INTEGRATION\|Diagnostics Integration]] — full observability stack
