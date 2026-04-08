---
title: Diagnostics Integration Architecture
description: >-
  Three-pillar observability: DiagnosticListener events, ETW EventSource, and
  OpenTelemetry-compatible Metrics for TurboHTTP
tags:
  - architecture
  - diagnostics
  - observability
  - telemetry
  - metrics
---
# Diagnostics Integration Architecture

## Purpose

TurboHTTP provides a three-pillar observability model that integrates with standard .NET diagnostic infrastructure. All telemetry is opt-in — zero overhead when no listeners are attached. The three pillars are:

1. **`DiagnosticListener`** — Rich structured events for distributed tracing and APM tools
2. **`EventSource` (ETW)** — Lightweight keyword-filtered events for production logging and PerfView
3. **`System.Diagnostics.Metrics`** — OpenTelemetry-compatible counters, histograms, and gauges

> **Extends, does not repeat**: For how tracing integrates with the pipeline, see [[Architecture/Layers/15-STREAMS_LAYER|Streams Layer]] (TracingBidiStage is the outermost BidiFlow). For deadlock watchdog diagnostics in DEBUG builds, see [[Architecture/Layers/15-STREAMS_LAYER|Streams Layer]].

---

## Key Files

| Component | Path | Role |
|-----------|------|------|
| DiagnosticListener | `Diagnostics/TurboHttpDiagnosticListener.cs` | Structured event source for APM/tracing integration |
| EventSource (ETW) | `Diagnostics/TurboHttpEventSource.cs` | ETW events with keyword filtering for production logging |
| Metrics | `Diagnostics/TurboHttpMetrics.cs` | OTel-compatible counters, histograms, gauges |
| TracingBidiStage | `Streams/Stages/Features/TracingBidiStage.cs` | Pipeline stage that creates `Activity` spans per request |
| DeadlockWatchdogStage | `Streams/Stages/Routing/DeadlockWatchdogStage.cs` | DEBUG-only stage emitting stall diagnostics |

---

## Data Flow

```text
┌──────────────────────────────────────────────────────────────┐
│                     TurboHTTP Pipeline                       │
│                                                              │
│  TracingBidiStage ◄──── Creates Activity per request         │
│       │                                                      │
│       ▼                                                      │
│  Feature BidiStages ──► Emit events at key decision points   │
│       │                                                      │
│       ▼                                                      │
│  Protocol Core ────────► Emit events on connect/disconnect   │
│       │                                                      │
│       ▼                                                      │
│  Transport Layer ──────► Emit events on socket open/close    │
└──────┬──────────┬──────────┬─────────────────────────────────┘
       │          │          │
       ▼          ▼          ▼
┌──────────┐ ┌──────────┐ ┌──────────────┐
│Diagnostic│ │  ETW     │ │   Metrics    │
│ Listener │ │EventSrc  │ │  (OTel)      │
│          │ │          │ │              │
│ APM/DT   │ │ PerfView │ │ Prometheus   │
│ Zipkin   │ │ dotnet-  │ │ Grafana      │
│ Jaeger   │ │ trace    │ │ Azure Mon.   │
└──────────┘ └──────────┘ └──────────────┘
```

---

## Pillar 1: DiagnosticListener

`TurboHttpDiagnosticListener` is a static class exposing a single `DiagnosticListener` named `"TurboHTTP"`.

### Events

| Event Name | Payload | Emitted By |
|------------|---------|------------|
| `TurboHTTP.Request.Start` | `HttpRequestMessage` | TracingBidiStage (request direction) |
| `TurboHTTP.Request.Stop` | `HttpResponseMessage` | TracingBidiStage (response direction) |
| `TurboHTTP.Request.Failed` | `Exception` | TracingBidiStage (on upstream failure) |
| `TurboHTTP.Connection.Opened` | `RequestEndpoint` | ConnectionStage (on connect) |
| `TurboHTTP.Connection.Closed` | `RequestEndpoint, CloseKind` | ConnectionStage (on disconnect) |
| `TurboHTTP.DeadlockStall` | `StageName, Duration` | DeadlockWatchdogStage (DEBUG only) |

### Guard Pattern

All event emission is guarded by `IsEnabled()` checks to avoid payload allocation when no subscriber is attached:

```csharp
if (Source.IsEnabled("TurboHTTP.Request.Start"))
{
    Source.Write("TurboHTTP.Request.Start", new { Request = request });
}
```

This ensures **zero allocation overhead** when diagnostics are not subscribed.

### Subscribing

```csharp
DiagnosticListener.AllListeners.Subscribe(listener =>
{
    if (listener.Name == "TurboHTTP")
    {
        listener.Subscribe(kvp =>
        {
            // Handle events by kvp.Key
        });
    }
});
```

### Activity Integration

`TracingBidiStage` creates a root `Activity` named `"TurboHTTP.Request"` for each request passing through the pipeline. The activity:
- Starts on request entry (outermost BidiStage, request direction)
- Tags with `http.method`, `http.url`, `http.version`
- Stops on response exit (outermost BidiStage, response direction)
- Sets `ActivityStatusCode.Error` on failure

This integrates with `System.Diagnostics.ActivitySource` for W3C Trace Context propagation.

---

## Pillar 2: EventSource (ETW)

`TurboHttpEventSource` is an ETW `EventSource` singleton (`TurboHttpEventSource.Log`) providing keyword-filtered events for production environments.

### Keyword Groups

| Keyword | Value | Events | Use Case |
|---------|-------|--------|----------|
| Connection | 0x01 | ConnectionOpened (1), ConnectionClosed (2) | Connection lifecycle monitoring |
| Request | 0x02 | RequestStart (3), RequestStop (4), RequestFailed (5) | Request-level tracing |
| Protocol | 0x04 | ProtocolNegotiated (6), ProtocolError (7), SettingsReceived (8) | Protocol debugging |
| Cache | 0x08 | CacheHit (9), CacheMiss (10) | Cache effectiveness analysis |
| Retry | 0x10 | RetryAttempt (11), RedirectFollowed (12) | Retry/redirect monitoring |

### Event Levels

- **Informational**: Normal lifecycle events (connect, request start/stop, cache hit)
- **Warning**: Retry attempts, redirects, protocol negotiation fallbacks
- **Error**: Request failures, protocol errors, connection failures

### Usage with dotnet-trace

```bash
dotnet-trace collect --providers TurboHTTP:0x1F:4
#                                 name    keywords level(Informational)
```

### Usage with PerfView

```text
PerfView /providers=TurboHTTP:0x1F:4 collect
```

---

## Pillar 3: Metrics (OpenTelemetry-Compatible)

`TurboHttpMetrics` exposes a static `Meter` named `"TurboHTTP"` with instruments following OpenTelemetry semantic conventions.

### Instruments

| Instrument | Type | Unit | Description |
|------------|------|------|-------------|
| `turbohttp.request.count` | Counter | `{request}` | Total requests sent |
| `turbohttp.request.duration` | Histogram | `ms` | Request round-trip duration |
| `turbohttp.cache.hit` | Counter | `{hit}` | Cache hit count |
| `turbohttp.cache.miss` | Counter | `{miss}` | Cache miss count |
| `turbohttp.retry.count` | Counter | `{retry}` | Retry attempt count |
| `turbohttp.redirect.count` | Counter | `{redirect}` | Redirect follow count |
| `turbohttp.connection.duration` | Histogram | `ms` | Connection lifetime duration |
| `turbohttp.connection.active` | UpDownCounter | `{connection}` | Currently active connections |
| `turbohttp.connection.idle` | UpDownCounter | `{connection}` | Currently idle connections |

### Tags/Dimensions

Metrics are tagged with:
- `http.method` — GET, POST, etc.
- `http.status_code` — Response status code
- `http.version` — 1.0, 1.1, 2, 3
- `server.address` — Target host

### Integration with OTel Collector

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddMeter("TurboHTTP");  // Subscribe to all TurboHTTP instruments
    });
```

### Integration with Prometheus

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddMeter("TurboHTTP");
        metrics.AddPrometheusExporter();
    });
```

---

## Design Decisions

1. **Three independent pillars** — Each diagnostic channel serves a different audience: DiagnosticListener for APM tools, EventSource for ops/production logging, Metrics for dashboards. They can be enabled independently with no cross-dependencies.

2. **Zero overhead when unsubscribed** — All three pillars use guard checks (`IsEnabled()`, keyword filtering, `Meter` listener registration) to avoid allocations when no consumer is attached. This is critical for a library that sits on the hot path of every HTTP request.

3. **TracingBidiStage as outermost layer** — Placing tracing at the outermost position in the BidiFlow chain ensures that the `Activity` span captures the full request lifecycle including retries, redirects, and cache lookups — not just the protocol-level round-trip.

4. **Static singletons** — `TurboHttpEventSource.Log` and `TurboHttpDiagnosticListener.Source` are static singletons. This matches .NET conventions and avoids per-client diagnostic overhead. Metrics use a static `Meter` for the same reason.

5. **DEBUG-only watchdog** — `DeadlockWatchdogStage` is conditionally compiled (`#if DEBUG`) to avoid production overhead. It emits `TurboHTTP.DeadlockStall` events when a stage stalls beyond a configurable threshold, aiding development-time deadlock detection.

---

## Known Limitations

- **No per-client Activity source** — All clients share one `ActivitySource`. If multiple `ITurboHttpClient` instances are used, traces are differentiated only by tags, not by source. This matches `HttpClient`'s behaviour.
- **No custom baggage propagation** — W3C Trace Context headers are propagated, but custom baggage items are not automatically injected into outgoing requests. Applications must add baggage manually via handlers.
- **EventSource event IDs are sequential** — Adding new events requires appending to the end of the class to maintain stable event IDs. Inserting events mid-sequence would break existing ETW consumers.
- **Histogram bucket boundaries** — Request duration and connection duration histograms use default OTel bucket boundaries. Applications with specific SLA requirements may need to configure custom boundaries via the OTel SDK.

---

## Integration Points

| Boundary | Direction | Contract |
|----------|-----------|----------|
| TracingBidiStage → DiagnosticListener | Outbound | `Request.Start/Stop/Failed` events |
| ConnectionStage → DiagnosticListener | Outbound | `Connection.Opened/Closed` events |
| DeadlockWatchdogStage → DiagnosticListener | Outbound | `DeadlockStall` events (DEBUG) |
| Feature BidiStages → EventSource | Outbound | Cache/Retry/Redirect keyword events |
| ConnectionStage → EventSource | Outbound | Connection keyword events |
| Protocol stages → EventSource | Outbound | Protocol keyword events |
| All stages → Metrics | Outbound | Counter/histogram recordings |
| External APM → DiagnosticListener | Inbound | `AllListeners.Subscribe()` |
| External OTel → Metrics | Inbound | `AddMeter("TurboHTTP")` |
| External ETW → EventSource | Inbound | Provider name + keyword mask |

---

## See Also

- [[Architecture/Layers/15-STREAMS_LAYER|Streams Layer]] — TracingBidiStage placement and DeadlockWatchdogStage
- [[Architecture/Layers/13-CLIENT_LAYER|Client Layer]] — Where diagnostic configuration is set up
- [[Architecture/Layers/14-TRANSPORT_LAYER|Transport Layer]] — Connection events originate here
- [[Architecture/Design/01-LAYERED_ARCHITECTURE|Layered Architecture]] — Overall system context
- [[Architecture/Design/02-STAGE_PATTERNS|GraphStage Patterns]] — Stage lifecycle and port conventions
