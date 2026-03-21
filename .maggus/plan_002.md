# Plan: Rename TurboMiddleware → TurboHandler + Sync API

## Introduction

`TurboMiddleware` is the abstract base class for user-defined request/response transformations in the Akka.Streams pipeline. Its current API uses `ValueTask<T>` with `CancellationToken` — an async pattern that adds complexity to both the user-facing API and the internal `MiddlewareBidiStage` (which requires `GetAsyncCallback`, `ContinueWith`, and async-in-flight tracking with deferred completion).

Renaming to `TurboHandler` aligns with .NET's `HttpMessageHandler` / `DelegatingHandler` naming convention. Converting to sync eliminates the async machinery in `MiddlewareBidiStage`, reducing it from ~165 lines to ~60 lines.

## Goals

- Rename `TurboMiddleware` → `TurboHandler` (HttpClient-style naming)
- Convert `ProcessRequestAsync` / `ProcessResponseAsync` to sync `ProcessRequest` / `ProcessResponse`
- Simplify `MiddlewareBidiStage` → `HandlerBidiStage` by removing all async callback machinery
- Rename builder API: `AddMiddleware<T>()` → `AddHandler<T>()`
- Rename all internal references (`MiddlewareTypes` → `HandlerTypes`, `MiddlewareFactories` → `HandlerFactories`, `Middlewares` → `Handlers`)
- Remove async test cases that no longer apply
- All remaining tests pass after migration

## Naming Map

| Before | After |
|--------|-------|
| `TurboMiddleware` | `TurboHandler` |
| `ProcessRequestAsync(req, ct)` → `ValueTask<HttpRequestMessage>` | `ProcessRequest(req)` → `HttpRequestMessage` |
| `ProcessResponseAsync(orig, resp, ct)` → `ValueTask<HttpResponseMessage>` | `ProcessResponse(orig, resp)` → `HttpResponseMessage` |
| `MiddlewareBidiStage` | `HandlerBidiStage` |
| `AddMiddleware<T>()` | `AddHandler<T>()` |
| `UseRequest(Func<…, ValueTask<>>)` | `UseRequest(Func<HttpRequestMessage, HttpRequestMessage>)` |
| `UseResponse(Func<…, ValueTask<>>)` | `UseResponse(Func<…, HttpResponseMessage>)` |
| `DelegateRequestMiddleware` | `DelegateRequestHandler` |
| `DelegateResponseMiddleware` | `DelegateResponseHandler` |
| `MiddlewareTypes` / `MiddlewareFactories` | `HandlerTypes` / `HandlerFactories` |
| `Middlewares` (PipelineDescriptor) | `Handlers` |
| `20_MiddlewareBidiStageTests.cs` | `20_HandlerBidiStageTests.cs` |
| `TurboHttpClientBuilderMiddlewareTests.cs` | `TurboHttpClientBuilderHandlerTests.cs` |

## User Stories

### TASK-001: Rename TurboMiddleware → TurboHandler + Sync API
**Description:** As a developer, I want the user-facing handler base class renamed and converted to sync so that the API is simpler and follows HttpClient conventions.

**Acceptance Criteria:**
- [x] `src/TurboHttp/Middleware/TurboMiddleware.cs`: class renamed to `TurboHandler`
- [x] `ProcessRequestAsync(HttpRequestMessage, CancellationToken)` → `ProcessRequest(HttpRequestMessage)` returning `HttpRequestMessage`
- [x] `ProcessResponseAsync(HttpRequestMessage, HttpResponseMessage, CancellationToken)` → `ProcessResponse(HttpRequestMessage, HttpResponseMessage)` returning `HttpResponseMessage`
- [x] Default implementations return the input unchanged (same semantics as before)
- [x] Remove `using System.Threading` and `using System.Threading.Tasks`
- [x] Typecheck passes (`dotnet build`)

### TASK-002: Simplify MiddlewareBidiStage → HandlerBidiStage (Sync)
**Description:** As a developer, I want the BidiStage simplified to call sync handler methods directly, removing all async callback machinery.

**Acceptance Criteria:**
- [x] `src/TurboHttp/Streams/Stages/MiddlewareBidiStage.cs`: class renamed to `HandlerBidiStage`
- [x] Constructor parameter: `TurboHandler handler` instead of `TurboMiddleware middleware`
- [x] Remove fields: `_onRequestProcessed`, `_onResponseProcessed`, `_requestAsyncInFlight`, `_responseAsyncInFlight`, `_requestUpstreamFinished`, `_responseUpstreamFinished`
- [x] Remove `PreStart()` override (no more `GetAsyncCallback`)
- [x] Request `onPush`: `Push(_outRequest, _handler.ProcessRequest(Grab(_inRequest)))`
- [x] Response `onPush`: `var resp = Grab(_inResponse); Push(_outResponse, _handler.ProcessResponse(resp.RequestMessage!, resp))`
- [x] `onUpstreamFinish` directly calls `Complete(outlet)` (no deferred completion needed)
- [x] `onUpstreamFailure` log message updated: `"HandlerBidiStage: …"`
- [x] Port name prefix uses handler type name (unchanged logic, just `_handler` field name)
- [x] Remove `using System.Threading.Tasks`
- [x] Typecheck passes

### TASK-003: Update TurboClientDescriptor
**Description:** As a developer, I want internal descriptor field names updated to reflect handler naming.

**Acceptance Criteria:**
- [x] `src/TurboHttp/Middleware/TurboClientDescriptor.cs`: `MiddlewareTypes` → `HandlerTypes`
- [x] `MiddlewareFactories` → `HandlerFactories`
- [x] Type reference: `Func<IServiceProvider, TurboMiddleware>` → `Func<IServiceProvider, TurboHandler>`
- [x] XML doc comments updated accordingly
- [x] Typecheck passes

### TASK-004: Update PipelineDescriptor
**Description:** As a developer, I want the pipeline descriptor to use handler naming.

**Acceptance Criteria:**
- [x] `src/TurboHttp/Streams/PipelineDescriptor.cs`: `IReadOnlyList<TurboMiddleware> Middlewares` → `IReadOnlyList<TurboHandler> Handlers`
- [x] `PipelineDescriptor.Empty` updated accordingly
- [x] Typecheck passes

### TASK-005: Update TurboHttpClientBuilderExtensions
**Description:** As a developer, I want the builder API renamed and converted to sync delegates.

**Acceptance Criteria:**
- [ ] `src/TurboHttp/Middleware/TurboHttpClientBuilderExtensions.cs`: `AddMiddleware<T>()` → `AddHandler<T>()` with `where T : TurboHandler`
- [ ] `UseRequest` delegate: `Func<HttpRequestMessage, HttpRequestMessage>` (no CancellationToken, no ValueTask)
- [ ] `UseResponse` delegate: `Func<HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>` (no CancellationToken, no ValueTask)
- [ ] `DelegateRequestMiddleware` → `DelegateRequestHandler` extending `TurboHandler`, overriding `ProcessRequest`
- [ ] `DelegateResponseMiddleware` → `DelegateResponseHandler` extending `TurboHandler`, overriding `ProcessResponse`
- [ ] Internal references: `d.MiddlewareTypes` → `d.HandlerTypes`, `d.MiddlewareFactories` → `d.HandlerFactories`
- [ ] XML doc `<see cref="…"/>` references updated
- [ ] Typecheck passes

### TASK-006: Update TurboHttpClientFactory
**Description:** As a developer, I want the factory to use the new handler field names.

**Acceptance Criteria:**
- [ ] `src/TurboHttp/Client/TurboHttpClientFactory.cs`: `descriptor.MiddlewareFactories` → `descriptor.HandlerFactories`
- [ ] `IReadOnlyList<TurboMiddleware>` → `IReadOnlyList<TurboHandler>`
- [ ] `Middlewares:` → `Handlers:` in PipelineDescriptor construction
- [ ] Typecheck passes

### TASK-007: Update Engine.cs
**Description:** As a developer, I want Engine pipeline composition to reference the renamed types.

**Acceptance Criteria:**
- [ ] `src/TurboHttp/Streams/Engine.cs`: `MiddlewareBidiStage` → `HandlerBidiStage`
- [ ] `descriptor.Middlewares` → `descriptor.Handlers`
- [ ] Comments updated: "User Middlewares — MiddlewareBidiStage per TurboMiddleware" → "User Handlers — HandlerBidiStage per TurboHandler"
- [ ] All XML doc / inline comments referencing old names updated
- [ ] Typecheck passes

### TASK-008: Update Stream Tests
**Description:** As a developer, I want the BidiStage test file updated to the new sync API and renamed.

**Acceptance Criteria:**
- [ ] Rename file `20_MiddlewareBidiStageTests.cs` → `20_HandlerBidiStageTests.cs`
- [ ] Test class name: `HandlerBidiStageTests`
- [ ] Test handler classes extend `TurboHandler` instead of `TurboMiddleware`
- [ ] Override `ProcessRequest` / `ProcessResponse` (sync, no CancellationToken)
- [ ] Remove `AsyncRequestHeaderMiddleware` and `AsyncResponseHeaderMiddleware` classes
- [ ] Remove test MBIDI-002 (async request transformation) and MBIDI-004 (async response transformation)
- [ ] Update `MiddlewareBidiStage` → `HandlerBidiStage` in all remaining test code
- [ ] Update `using TurboHttp.Middleware` → keep (namespace unchanged) or adjust if needed
- [ ] All remaining tests pass (`dotnet test`)

### TASK-009: Update Hosting Tests
**Description:** As a developer, I want the builder handler tests updated to new naming.

**Acceptance Criteria:**
- [ ] Rename file `TurboHttpClientBuilderMiddlewareTests.cs` → `TurboHttpClientBuilderHandlerTests.cs`
- [ ] Test class name: `TurboHttpClientBuilderHandlerTests`
- [ ] `TurboMiddleware` → `TurboHandler` in test doubles (`TestMiddleware` → `TestHandler`, `AlphaMiddleware` → `AlphaHandler`, `BetaMiddleware` → `BetaHandler`)
- [ ] `AddMiddleware<T>()` → `AddHandler<T>()` in test calls
- [ ] `MiddlewareTypes` → `HandlerTypes`, `MiddlewareFactories` → `HandlerFactories` in assertions
- [ ] `UseRequest` lambda updated to sync signature: `(req) => req`
- [ ] DisplayName attributes updated to reflect new naming
- [ ] All tests pass

### TASK-010: Full Validation
**Description:** As a developer, I want to verify zero regressions across the entire solution.

**Acceptance Criteria:**
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` — zero errors, zero warnings from renamed types
- [ ] `dotnet test src/TurboHttp.sln` — all tests pass
- [ ] No remaining references to `TurboMiddleware`, `MiddlewareBidiStage`, `AddMiddleware`, `MiddlewareTypes`, `MiddlewareFactories`, `ProcessRequestAsync`, `ProcessResponseAsync` in production or test code (verified via grep)

## Functional Requirements

- FR-1: `TurboHandler.ProcessRequest(HttpRequestMessage)` must return an `HttpRequestMessage` synchronously. Default implementation returns the input unchanged.
- FR-2: `TurboHandler.ProcessResponse(HttpRequestMessage, HttpResponseMessage)` must return an `HttpResponseMessage` synchronously. Default implementation returns the input unchanged.
- FR-3: `HandlerBidiStage` must call `ProcessRequest()` on every element pushed to the request inlet and forward the result to the request outlet.
- FR-4: `HandlerBidiStage` must call `ProcessResponse()` on every element pushed to the response inlet and forward the result to the response outlet.
- FR-5: No async machinery (`GetAsyncCallback`, `ContinueWith`, async-in-flight tracking) remains in `HandlerBidiStage`.
- FR-6: Port names must derive from the handler's runtime class name with index suffix (e.g. `RequestHeaderMiddleware0.In.Request`) — same logic as before, just using `_handler` field.
- FR-7: User handler BidiFlows must remain outermost in the `Atop` chain — outside Redirect, Cookie, Retry, Cache, Decompression.
- FR-8: FIFO ordering preserved: `Handlers[0]` processes the initial request first and the final response last.
- FR-9: `AddHandler<T>()` must register `T` as Transient and append a factory to `HandlerFactories`.
- FR-10: `UseRequest` and `UseResponse` delegate signatures are fully synchronous — no `CancellationToken`, no `ValueTask`.

## Non-Goals

- No changes to built-in BidiStages (Cookie, Redirect, Retry, Cache, Decompression)
- No changes to `ITurboHttpClientBuilder` interface
- No changes to `TurboHttpClientBuilder` class
- No namespace changes — files stay in `TurboHttp.Middleware` and `TurboHttp.Streams.Stages`
- No new features — pure rename + sync conversion

## Technical Considerations

- **`MiddlewareBidiStage` simplification**: The async path (`GetAsyncCallback`, `ContinueWith`, deferred completion) is ~100 lines of code. With sync, `onPush` becomes a single `Push(out, handler.Process*(Grab(in)))` call. Completion handlers simplify to direct `Complete(outlet)`.
- **Port naming**: The port name logic (`GetType().Name + index`) is unchanged. Only the field reference changes from `_middleware` to `_handler`.
- **Namespace stays**: The `Middleware/` folder and `TurboHttp.Middleware` namespace are kept as-is. Renaming the namespace would be a larger scope change.
- **File renames**: Git will detect the renames automatically via content similarity.

## Key Files

| File | Action |
|------|--------|
| `src/TurboHttp/Middleware/TurboMiddleware.cs` | **Rewrite** (rename class + sync API) |
| `src/TurboHttp/Streams/Stages/MiddlewareBidiStage.cs` | **Rewrite** (rename + major simplification) |
| `src/TurboHttp/Middleware/TurboClientDescriptor.cs` | **Modify** (field renames) |
| `src/TurboHttp/Streams/PipelineDescriptor.cs` | **Modify** (field rename) |
| `src/TurboHttp/Middleware/TurboHttpClientBuilderExtensions.cs` | **Modify** (method rename + sync delegates) |
| `src/TurboHttp/Client/TurboHttpClientFactory.cs` | **Modify** (reference updates) |
| `src/TurboHttp/Streams/Engine.cs` | **Modify** (reference + comment updates) |
| `src/TurboHttp.StreamTests/Streams/20_MiddlewareBidiStageTests.cs` | **Rename + Rewrite** → `20_HandlerBidiStageTests.cs` |
| `src/TurboHttp.Tests/Hosting/TurboHttpClientBuilderMiddlewareTests.cs` | **Rename + Update** → `TurboHttpClientBuilderHandlerTests.cs` |

## Success Metrics

- Zero compile errors, zero test failures
- No references to old names remain in the codebase (verified via `grep -r`)
- `HandlerBidiStage` is ~60 lines (down from ~165)
- All 7 remaining stream tests pass (MBIDI-001, 003, 005, 006, 007, 008, 009)
- All hosting handler tests pass

## Open Questions

- Should the `Middleware/` folder be renamed to `Handler/` or `Handlers/`? (Deferred — namespace change is out of scope for this plan.)
