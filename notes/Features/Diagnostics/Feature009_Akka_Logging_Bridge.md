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
| **Scope** | 3 steps |

## Description

Integrated `Akka.Logger.Extensions.Logging` to bridge Akka.NET's internal actor system logging to the standard `Microsoft.Extensions.Logging` pipeline. This allowed Akka debug/info/error messages to appear in the same log output as ASP.NET Core and TurboHTTP application logs.

- Added `Akka.Logger.Extensions.Logging` NuGet package
- Configured the logging bridge in the hosting layer (`TurboHttpServiceCollectionExtensions`)
- Added integration tests verifying Akka log messages flow through the bridge

## Key Source Files

| File | Role |
|------|------|
| `src/TurboHTTP/Hosting/TurboHttpServiceCollectionExtensions.cs` | DI configuration for logging bridge |
| `src/TurboHTTP.IntegrationTests/Diagnostics/AkkaLoggingBridgeTests.cs` | Bridge integration tests |

## See Also

- [[Features/Diagnostics/Feature010_Tracing_Infrastructure\|Feature 010]] — OTel tracing (built on top of logging)
- [[Architecture/Guides/17-DIAGNOSTICS_INTEGRATION\|Diagnostics Integration]] — full observability stack
