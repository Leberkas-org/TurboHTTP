# TurboHTTP IServer Pipeline Redesign

## Summary

Strip TurboHTTP down to a pure transport+protocol layer. Remove all custom routing, context types, and middleware abstractions. ASP.NET's `IHttpApplication<TContext>` becomes the sole request-handling contract. The Akka Streams pipeline carries `IFeatureCollection` directly — no wrapper types.

## Goals

- TurboHTTP is a drop-in `IServer` replacement for Kestrel
- ASP.NET Middleware, Controllers, Minimal APIs run natively on TurboHTTP
- No custom routing, no custom context, no custom pipeline delegate
- `IFeatureCollection` is the only data contract between protocol layer and application layer
- Parallel request dispatch with sequence ordering (H2/H3 multiplexing)

## Non-Goals

- Custom TurboHTTP routing or middleware system
- TurboHTTP-specific handler APIs
- Standalone server mode (without ASP.NET hosting)

---

## Architecture

### Stream Element: `IFeatureCollection`

The Akka Streams pipeline element changes from `RequestContext` to `IFeatureCollection`.

**Before:**
```
Protocol Decoder → RequestContext → RoutingStage → TurboHttpContext → Handler
```

**After:**
```
Protocol Decoder → IFeatureCollection → ApplicationBridgeStage<TContext> → IFeatureCollection → Response Encoder
```

No wrapper, no intermediate context type. The feature collection IS the request.

### ApplicationBridgeStage\<TContext\>

Generic Akka `GraphStage<FlowShape<IFeatureCollection, IFeatureCollection>>` that bridges Akka Streams to ASP.NET's `IHttpApplication<TContext>`.

```csharp
internal sealed class ApplicationBridgeStage<TContext>
    : GraphStage<FlowShape<IFeatureCollection, IFeatureCollection>>
    where TContext : notnull
{
    private readonly IHttpApplication<TContext> _application;
    private readonly int _parallelism;
    private readonly TimeSpan _handlerTimeout;
    private readonly TimeSpan _handlerGracePeriod;
}
```

**Key properties:**
- Holds `IHttpApplication<TContext>` directly — no type erasure, no adapter, no `Func<object, Task>`
- The generic parameter is captured in `TurboServer.StartAsync<TContext>` and flows into the stage
- Stage instance is created once, shared across connection materializations (safe because `IHttpApplication` calls are per-request)

**Parallel dispatch with sequence ordering:**
- Maintains `_inFlight` counter, pulls up to `_parallelism` concurrent requests
- `SortedDictionary<int, IFeatureCollection>` reorders completed requests for sequential emission
- Each request gets a sequence number on arrival, emitted in order regardless of completion order

**Timeout management:**
- Per-request `CancellationTokenSource` linked to the request's `IHttpRequestLifetimeFeature`
- `CancelAfter(_handlerTimeout)` on the CTS
- Grace period via `Task.Delay(_handlerTimeout + _handlerGracePeriod)` → `HandlerTimedOut` message
- If timeout fires and no response headers sent: 503

**Request lifecycle in the stage:**

```
OnPush(features):
  seq = _sequence++
  appContext = _application.CreateContext(features)
  task = _application.ProcessRequestAsync(appContext)
  
  if task.IsCompletedSuccessfully:
    _application.DisposeContext(appContext, null)
    CompleteResponseBody(features)
    Emit(seq, features)
  
  elif task.IsFaulted:
    Set 500 on IHttpResponseFeature
    _application.DisposeContext(appContext, task.Exception)
    CompleteResponseBody(features)
    Emit(seq, features)
  
  else (async):
    Start timeout CTS
    PipeTo(stageActor) for completion/failure/timeout signals
    TryPullNext() for parallel dispatch
```

**Estimated size:** ~200-250 lines (down from 387).

### FeatureCollectionFactory (renamed from ServerContextFactory)

Pools `TurboFeatureCollection` instances with thread-static stack (max 32).

```csharp
internal static class FeatureCollectionFactory
{
    public static IFeatureCollection Create(
        IHttpRequestFeature requestFeature,
        IHttpResponseFeature responseFeature,
        IHttpResponseBodyFeature bodyFeature,
        IHttpConnectionFeature connectionFeature,
        IHttpRequestLifetimeFeature lifetimeFeature,
        IHttpRequestIdentifierFeature identifierFeature,
        ...);
    
    public static void Return(IFeatureCollection features);
}
```

- `Create()`: pops from pool or allocates, sets all features
- `Return()`: clears all features, disposes CTS from lifetime feature, pushes to pool
- CTS lifecycle: created by the protocol decoder, set as `IHttpRequestLifetimeFeature`, disposed on return

### TurboServer Changes

```csharp
public async Task StartAsync<TContext>(
    IHttpApplication<TContext> application,
    CancellationToken cancellationToken) where TContext : notnull
{
    // ActorSystem setup (unchanged)
    
    var bridgeStage = new ApplicationBridgeStage<TContext>(
        application,
        _options.MaxConcurrentRequests,
        _options.HandlerTimeout,
        _options.HandlerGracePeriod);
    
    // Resolve endpoints, create listeners with bridgeStage
    // No TurboRequestDelegate, no RouteTable
}
```

### HttpConnectionServerStageLogic Changes

Port types change:
- `Outlet<RequestContext>` → `Outlet<IFeatureCollection>`
- `Inlet<RequestContext>` → `Inlet<IFeatureCollection>`
- `IServerStageOperations.OnRequest(RequestContext)` → `OnRequest(IFeatureCollection)`

Protocol decoders create features → `FeatureCollectionFactory.Create(...)` → push `IFeatureCollection` directly.

### ServerConnectionShape Changes

The shape definition updates its port types from `RequestContext` to `IFeatureCollection`.

---

## Deletions

### Types to Delete

| Type | File | Reason |
|------|------|--------|
| `RequestContext` | `Streams/Stages/Server/RequestContext.cs` | Replaced by `IFeatureCollection` as stream element |
| `TurboHttpContext` | `Server/TurboHttpContext.cs` | ASP.NET builds its own `HttpContext` |
| `TurboHttpRequest` | `Context/TurboHttpRequest.cs` | No consumer without TurboHttpContext |
| `TurboHttpResponse` | `Context/TurboHttpResponse.cs` | No consumer without TurboHttpContext |
| `TurboConnectionInfo` | `Server/TurboConnectionInfo.cs` | ASP.NET has `IHttpConnectionFeature` |
| `RoutingStage` | `Streams/Stages/Server/RoutingStage.cs` | No custom routing |
| `RouteTable` | `Server/RouteTable.cs` | No custom routing |
| `RouteMatchResult` | `Routing/RouteMatchResult.cs` | No custom routing |
| `TurboRequestDelegate` | `Server/TurboRequestDelegate.cs` | No custom pipeline |
| `Routing/` folder | `Routing/**` | All dispatchers, binding, route types |

### Tests to Delete

All tests for deleted types:
- `ContextPoolingSpec.cs` (tests RequestContext pooling)
- Any tests for RoutingStage, RouteTable, TurboHttpContext
- Tests for TurboHttpRequest/TurboHttpResponse standalone usage

### Tests to Modify

- `ServerContextFactorySpec.cs` → rename to `FeatureCollectionFactorySpec.cs`, test IFeatureCollection pooling
- Integration tests that construct TurboHttpContext manually → use IFeatureCollection directly

---

## DI Registration Changes

`TurboServerServiceCollectionExtensions.cs`:
- `AddTurboServer()` stays (registers `IServer` → `TurboServer`)
- `AddTurboKestrel()` removes `TurboRouteTable` registration, removes any routing-related DI

---

## Data Flow (Final)

```
Network Bytes (TCP/TLS/QUIC)
  → TransportFlow (Servus.Akka)
  → ProtocolEngine (Http11/H2/H3 decoder)
  → FeatureCollectionFactory.Create(requestFeature, responseFeature, ...)
  → [IFeatureCollection] pushed to outlet
  
  → ApplicationBridgeStage<TContext>
    → _application.CreateContext(features)
    → _application.ProcessRequestAsync(appContext)
    → _application.DisposeContext(appContext, exception)
    → CompleteResponseBody(features)
  → [IFeatureCollection] emitted downstream
  
  → HttpConnectionServerStageLogic (response inlet)
    → Protocol encoder writes response bytes
    → FeatureCollectionFactory.Return(features)
  → Network Bytes
```

---

## Migration Notes

- The `ApplicationBridgeStage` file gets rewritten, not modified — the current implementation has type-erased `Func<object, Task>` that is replaced by generic `IHttpApplication<TContext>`
- `ListenerActor` and `ConnectionActor` constructor signatures change (no more `TurboRequestDelegate`/`RouteTable` params, gain `ApplicationBridgeStage<TContext>` or the graph flow)
- Protocol state machines (`Http11ServerStateMachine`, `Http2ServerSessionManager`, etc.) change their `OnRequest` callback to emit `IFeatureCollection` instead of `RequestContext`
