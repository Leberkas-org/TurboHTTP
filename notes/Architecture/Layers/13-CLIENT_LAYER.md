---
title: Client Layer
description: >-
  Public API surface, factory pattern, DI integration, and request lifecycle for
  TurboHttp client layer
tags:
  - architecture
  - client
  - api
  - dependency-injection
---
# Client Layer

The Client Layer is TurboHttp's public API surface — the entry point for consumers who want to send HTTP requests. It follows the `HttpClientFactory` pattern from `Microsoft.Extensions.Http`, providing named/typed client instances with DI-friendly configuration.

> **Scope**: This note covers the client-facing types only. For the internal pipeline that executes requests, see [[Architecture/15-STREAMS_LAYER|Streams Layer]].

## Purpose

- Provide a familiar, `HttpClient`-compatible API for sending HTTP requests
- Support named and typed clients via `ITurboHttpClientFactory`
- Integrate with `Microsoft.Extensions.DependencyInjection` via `ITurboHttpClientBuilder`
- Allow per-client configuration of policies (redirect, retry, cache, cookies, compression)

## Key Files

| File | Purpose |
|------|---------|
| `src/TurboHttp/ITurboHttpClientFactory.cs` | Factory interface — creates named `ITurboHttpClient` instances |
| `src/TurboHttp/ITurboHttpClientBuilder.cs` | Builder interface — configures a named client's `IServiceCollection` |
| `src/TurboHttp/TurboClientOptions.cs` | Per-client configuration: timeouts, TLS, certificates, max frame size |
| `src/TurboHttp/TurboRequestOptions.cs` | Per-request defaults: base address, headers, version, timeout |
| `src/TurboHttp/TurboHandler.cs` | User middleware — injected into the BidiFlow pipeline |
| `src/TurboHttp/Streams/PipelineDescriptor.cs` | Aggregates all policies into a single record for pipeline construction |

## Data Flow

```text
Application Code
       │
       ▼
ITurboHttpClientFactory.CreateClient("name")
       │
       ▼
ITurboHttpClient.SendAsync(HttpRequestMessage)
       │
       ▼
Engine.CreateFlow(pool, options, descriptor)
       │
       ▼
┌──────────────────────────────────────────┐
│  Feature BidiFlow Chain (outermost→in):  │
│  Tracing → Handlers → Redirect → Cookie  │
│  → Retry → Expect100 → Cache → Content   │
│  Encoding → Protocol Engine Core         │
└──────────────────────────────────────────┘
       │
       ▼
HttpResponseMessage returned to caller
```

## Design Decisions

### Factory Pattern over Direct Instantiation

TurboHttp uses `ITurboHttpClientFactory` rather than exposing constructors directly. This enables:
- **Named clients** with different configurations (e.g., "github-api" vs "internal-service")
- **Lifetime management** — the factory controls `ConnectionPool` sharing across clients
- **DI integration** — `ITurboHttpClientBuilder` plugs into `IServiceCollection` for clean startup code

### PipelineDescriptor as Policy Aggregator

Rather than passing 8+ policy parameters individually through the pipeline construction chain, `PipelineDescriptor` collects all optional policies into a single immutable record:

```csharp
internal sealed record PipelineDescriptor(
    RedirectPolicy? RedirectPolicy,
    RetryPolicy? RetryPolicy,
    Expect100Policy? Expect100Policy,
    RequestCompressionPolicy? RequestCompressionPolicy,
    CookieJar? CookieJar,
    CacheStore? CacheStore,
    CachePolicy? CachePolicy,
    IReadOnlyList<TurboHandler> Handlers,
    bool AutomaticDecompression = true);
```

Null policies are simply skipped — no BidiStage is inserted for unused features.

### TurboHandler as BidiFlow Middleware

User-provided `TurboHandler` instances are wrapped in `HandlerBidiStage` and stacked via `Atop` in the feature BidiFlow chain. Handlers[0] is outermost (sees initial request first, final response last). This gives middleware the same request/response interception pattern as `DelegatingHandler` in `HttpClient` but implemented as Akka.Streams BidiFlows.

## Known Limitations

- **No `HttpClient` drop-in replacement** — `ITurboHttpClient` is a separate interface, not a subclass of `HttpClient`
- **No automatic `HttpMessageHandler` compatibility** — existing `DelegatingHandler` chains cannot be reused directly; they must be ported to `TurboHandler`
- **Client/Handlers/Hosting directories** referenced in CLAUDE.md do not exist as separate folders yet — the types live at the project root and in `Streams/`

## Integration Points

| Component | Interaction |
|-----------|-------------|
| [[Architecture/15-STREAMS_LAYER|Streams Layer]] | `Engine.CreateFlow()` builds the Akka.Streams pipeline from `PipelineDescriptor` |
| [[Architecture/14-TRANSPORT_LAYER|Transport Layer]] | `ConnectionPool` is shared across clients created by the same factory |
| [[Architecture/17-DIAGNOSTICS_INTEGRATION|Diagnostics]] | `TracingBidiStage` wraps outermost layer for `Activity`-based tracing |
| `Microsoft.Extensions.DependencyInjection` | `ITurboHttpClientBuilder.Services` enables DI registration |

## See Also

- [[Architecture/01-LAYERED_ARCHITECTURE|Layered Architecture]] — Where the Client Layer fits in the overall stack
- [[Architecture/15-STREAMS_LAYER|Streams Layer]] — Pipeline construction details
- [[Architecture/09-CLAUDE_PREFERENCES|Claude Preferences]] — Workflow and response conventions
