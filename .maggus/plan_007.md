# Plan: Middleware System & `IHttpClientFactory`-Style API

## Introduction

TurboHttp currently has a single `AddTurboHttpClientFactory(services, configure)` with no named/typed client support and no user-middleware extensibility. Built-in features (Cookie, Cache, Retry, Redirect) are wired directly into `TurboClientOptions` as nullable policies and are unconditionally instantiated inside `Engine.BuildExtendedPipeline` — even when the features are never used. `Engine.cs` itself is a large monolithic class with mixed responsibilities, no access modifier discipline, and test seams leaking into production code.

The goal is to port the well-known `IHttpClientFactory` pattern from `Microsoft.Extensions.Http` to TurboHttp: `services.AddTurboHttpClient("name", configure)` returns an `ITurboHttpClientBuilder` on which built-ins (`.WithCookies()`, `.WithRetry()`, etc.) and custom middleware (`.AddMiddleware<T>()`) are registered — with no Akka knowledge required from the user. The Akka graph continues to be materialized exactly once at `CreateClient()` time. As a final step, `Engine.cs` is fully refactored to reflect this new architecture cleanly.

## Goals

- `services.AddTurboHttpClient(name, options => ...)` → `ITurboHttpClientBuilder` (mirrors `AddHttpClient`)
- Named and typed clients (`factory.CreateClient("payments")`, `AddTurboHttpClient<IGitHubClient>()`)
- `TurboMiddleware` base class with `ProcessRequestAsync` / `ProcessResponseAsync` — no Akka knowledge needed
- `.AddMiddleware<T>()` resolves middleware via DI; constructor injection of dependencies is supported
- Built-in features cleanly extracted from `TurboClientOptions` → only activatable via builder
- Engine wires features **only** when activated via `PipelineDescriptor` — no hidden allocations
- `TurboClientOptions.RedirectPolicy/RetryPolicy/CachePolicy` marked `[Obsolete]` but backward-compatible
- `AddTurboHttpClientFactory` marked `[Obsolete]` but backward-compatible
- Isolated stage tests (`CookieInjectionStageTests`, `RetryStageTests`, etc.) left untouched
- Pipeline integration tests (`01_StageOrderingTests`, `11_EnginePipelineWiringTests`) rewritten to new API
- `Engine.cs` fully refactored: split responsibilities, proper access modifiers, clean public API

## User Stories

---

### TASK-001: `TurboMiddleware` Base Class
**Description:** As a library user, I want a simple base class for custom middleware so that I can transform requests and responses without any Akka knowledge.

**Acceptance Criteria:**
- [x] `public abstract class TurboMiddleware` in `src/TurboHttp/Middleware/TurboMiddleware.cs`
- [x] `public virtual ValueTask<HttpRequestMessage> ProcessRequestAsync(HttpRequestMessage request, CancellationToken ct)` — default returns request unchanged
- [x] `public virtual ValueTask<HttpResponseMessage> ProcessResponseAsync(HttpRequestMessage original, HttpResponseMessage response, CancellationToken ct)` — default returns response unchanged
- [x] No Akka, no DI imports in this file
- [x] Namespace: `TurboHttp.Middleware`
- [x] Typecheck/lint passes

---

### TASK-002: `ITurboHttpClientBuilder` Interface
**Description:** As a library author, I want an interface analogous to `IHttpClientBuilder` so that all feature and middleware extension methods share a common target type.

**Acceptance Criteria:**
- [ ] `public interface ITurboHttpClientBuilder` in `src/TurboHttp/Middleware/ITurboHttpClientBuilder.cs`
- [ ] Property `string Name { get; }`
- [ ] Property `IServiceCollection Services { get; }`
- [ ] No Akka imports — only `Microsoft.Extensions.DependencyInjection`
- [ ] Namespace: `TurboHttp.Middleware`
- [ ] Typecheck/lint passes

---

### TASK-003: `TurboClientDescriptor` Mutable Class
**Description:** As a developer, I want an internal mutable class that captures everything registered via `ITurboHttpClientBuilder` so that the factory can read it at `CreateClient()` time.

**Acceptance Criteria:**
- [ ] `internal sealed class TurboClientDescriptor` in `src/TurboHttp/Middleware/TurboClientDescriptor.cs` (mutable class, not record — `IConfigureNamedOptions` mutates it in-place)
- [ ] Fields: `RedirectPolicy? RedirectPolicy`, `RetryPolicy? RetryPolicy`, `bool EnableCookies`, `CookieJar? CustomCookieJar`, `CachePolicy? CachePolicy`, `List<Type> MiddlewareTypes` (empty by default)
- [ ] Namespace: `TurboHttp.Middleware`
- [ ] Typecheck/lint passes

---

### TASK-004: `PipelineDescriptor` Immutable Record
**Description:** As a developer, I want an immutable record passed from the factory to the engine so that the engine no longer instantiates CookieJar/CacheStore itself.

**Acceptance Criteria:**
- [ ] `internal sealed record PipelineDescriptor(RedirectPolicy?, RetryPolicy?, CookieJar?, HttpCacheStore?, IReadOnlyList<TurboMiddleware>)` in `src/TurboHttp/Streams/PipelineDescriptor.cs`
- [ ] `static readonly PipelineDescriptor Empty` — all null, empty list; used by backward-compat code paths
- [ ] Namespace: `TurboHttp.Streams`
- [ ] Typecheck/lint passes

---

### TASK-005: `MiddlewareRequestStage` Akka Stage
**Description:** As a developer, I want a FlowShape stage that calls `TurboMiddleware.ProcessRequestAsync` per element so that request transformation happens inside the Akka graph without blocking.

**Acceptance Criteria:**
- [ ] `internal sealed class MiddlewareRequestStage : GraphStage<FlowShape<HttpRequestMessage, HttpRequestMessage>>` in `src/TurboHttp/Streams/Stages/MiddlewareRequestStage.cs`
- [ ] Constructor: `MiddlewareRequestStage(TurboMiddleware middleware)`
- [ ] Port names: `"MiddlewareRequest.In"` / `"MiddlewareRequest.Out"`
- [ ] Async invocation via `GetAsyncCallback<HttpRequestMessage>` in StageLogic — no `.Result`, no `Task.Run`
- [ ] `onPush`: starts async task; in callback: `Push(_out, result)`
- [ ] `onPull`: `Pull(_in)`
- [ ] Typecheck/lint passes

---

### TASK-006: `MiddlewareResponseStage` Akka Stage
**Description:** As a developer, I want a FlowShape stage that calls `TurboMiddleware.ProcessResponseAsync` per element so that response transformation happens inside the Akka graph.

**Acceptance Criteria:**
- [ ] `internal sealed class MiddlewareResponseStage : GraphStage<FlowShape<HttpResponseMessage, HttpResponseMessage>>` in `src/TurboHttp/Streams/Stages/MiddlewareResponseStage.cs`
- [ ] Constructor: `MiddlewareResponseStage(TurboMiddleware middleware)`
- [ ] Port names: `"MiddlewareResponse.In"` / `"MiddlewareResponse.Out"`
- [ ] `original` read from `response.RequestMessage!` — no second inlet needed
- [ ] Async invocation via `GetAsyncCallback<HttpResponseMessage>` — same pattern as TASK-005
- [ ] Typecheck/lint passes

---

### TASK-007: `BuildExtendedPipeline` — Accept `PipelineDescriptor`
**Description:** As a developer, I want `BuildExtendedPipeline` to accept a `PipelineDescriptor` and use its CookieJar/CacheStore instead of always creating new instances so that features are only active when configured.

**Acceptance Criteria:**
- [ ] `BuildExtendedPipeline` receives `PipelineDescriptor descriptor` as an additional parameter
- [ ] `var cookieJar = new CookieJar()` and `var cacheStore = new HttpCacheStore(...)` removed
- [ ] Stage instantiation uses `descriptor.CookieJar` / `descriptor.CacheStore` (may be null)
- [ ] `CreateFlow(poolRouter, options, factory)` — the existing public method — derives a `PipelineDescriptor` internally for backward-compat:
  ```csharp
  new PipelineDescriptor(options.RedirectPolicy, options.RetryPolicy,
      new CookieJar(), new HttpCacheStore(options.CachePolicy), [])
  ```
- [ ] All existing tests continue to pass
- [ ] Typecheck/lint passes

---

### TASK-008: `BuildExtendedPipeline` — Wire Request Middleware Stages
**Description:** As a developer, I want request middleware stages wired after `RequestEnricherStage` and before `redirectMerge.In(0)` so that initial requests are transformed before entering the feedback loop.

**Acceptance Criteria:**
- [ ] For each `TurboMiddleware` in `descriptor.Middlewares`: `builder.Add(new MiddlewareRequestStage(mw))`
- [ ] Stages chained **after** `enricher.Outlet` and **before** `redirectMerge.In(0)`
- [ ] Order: FIFO — first registered middleware runs first
- [ ] When `descriptor.Middlewares` is empty → no stage added; `requestTip` goes directly from enricher to redirectMerge
- [ ] Redirect feedback (`redirectMerge.Preferred`) correctly bypasses the request middleware stages (redirect requests are not re-transformed)
- [ ] Typecheck/lint passes

---

### TASK-009: `BuildPostProcessGraph` — Wire Response Middleware Stages
**Description:** As a developer, I want response middleware stages wired after `RedirectStage.Out0` so that only final responses (after redirect and retry) reach user middleware.

**Acceptance Criteria:**
- [ ] `BuildPostProcessGraph` receives `IReadOnlyList<TurboMiddleware> middlewares` parameter
- [ ] For each middleware: `builder.Add(new MiddlewareResponseStage(mw))`
- [ ] Stages chained **after** `redirect.Out0` (final responses only)
- [ ] When empty → last stage outlet is used directly as `ResponseOut` in `PostProcessShape`
- [ ] `PostProcessShape.ResponseOut` points to the last response stage outlet
- [ ] Typecheck/lint passes

---

### TASK-010: `TurboClientStreamManager` — `PipelineDescriptor` Overload
**Description:** As a developer, I want `TurboClientStreamManager` to accept a `PipelineDescriptor` so that the factory can pass the fully configured descriptor without going through `TurboClientOptions`.

**Acceptance Criteria:**
- [ ] New internal constructor: `TurboClientStreamManager(TurboClientOptions, Func<TurboRequestOptions>, ActorSystem, PipelineDescriptor)`
- [ ] Calls `engine.CreateFlow(poolRouter, options, factory, descriptor)` internally
- [ ] Existing `public` constructor without `PipelineDescriptor` remains and delegates to the new one with `PipelineDescriptor.Empty`
- [ ] Typecheck/lint passes

---

### TASK-011: `TurboHttpClientBuilder` — Concrete Implementation
**Description:** As a developer, I want a concrete implementation of `ITurboHttpClientBuilder` so that `AddTurboHttpClient` has something to return.

**Acceptance Criteria:**
- [ ] `internal sealed class TurboHttpClientBuilder(string name, IServiceCollection services) : ITurboHttpClientBuilder` in `src/TurboHttp/Middleware/TurboHttpClientBuilder.cs`
- [ ] Implements `Name` and `Services` — no other logic
- [ ] Namespace: `TurboHttp.Middleware`
- [ ] Typecheck/lint passes

---

### TASK-012: Extension Methods — Built-in Features
**Description:** As a library user, I want fluent extension methods for built-in features so that I can enable cookies, cache, retry, and redirect with a single call on `ITurboHttpClientBuilder`.

**Acceptance Criteria:**
- [ ] `public static class TurboHttpClientBuilderExtensions` in `src/TurboHttp/Middleware/TurboHttpClientBuilderExtensions.cs`
- [ ] `.WithCookies(CookieJar? jar = null)` — calls `services.Configure<TurboClientDescriptor>(name, d => { d.EnableCookies = true; d.CustomCookieJar = jar; })`
- [ ] `.WithCache(CachePolicy policy)` — sets `d.CachePolicy = policy`
- [ ] `.WithRetry(RetryPolicy policy)` — sets `d.RetryPolicy = policy`
- [ ] `.WithRedirect(RedirectPolicy? policy = null)` — sets `d.RedirectPolicy = policy ?? new RedirectPolicy()`
- [ ] All methods return `ITurboHttpClientBuilder`
- [ ] Typecheck/lint passes

---

### TASK-013: Extension Methods — User Middleware
**Description:** As a library user, I want `.AddMiddleware<T>()` and inline delegates `.UseRequest()`/`.UseResponse()` so that I can add custom middleware in different ways.

**Acceptance Criteria:**
- [ ] `.AddMiddleware<T>() where T : TurboMiddleware` — registers `T` as Transient in `builder.Services`, appends `typeof(T)` to `TurboClientDescriptor.MiddlewareTypes`
- [ ] `.UseRequest(Func<HttpRequestMessage, CancellationToken, ValueTask<HttpRequestMessage>> transform)` — wraps delegate in an anonymous `TurboMiddleware` subclass, calls `.AddMiddleware<>()` internally
- [ ] `.UseResponse(Func<HttpRequestMessage, HttpResponseMessage, CancellationToken, ValueTask<HttpResponseMessage>> transform)` — same pattern
- [ ] Registration order is preserved (FIFO in `MiddlewareTypes` list)
- [ ] Typecheck/lint passes

---

### TASK-014: `AddTurboHttpClient` — Named + Default Client
**Description:** As a library user, I want `services.AddTurboHttpClient(name, configure)` so that I can register named clients using the familiar `IHttpClientFactory` pattern.

**Acceptance Criteria:**
- [ ] `public static ITurboHttpClientBuilder AddTurboHttpClient(this IServiceCollection services, string name, Action<TurboClientOptions>? configure = null)` in `TurboClientServiceCollectionExtensions.cs`
- [ ] `public static ITurboHttpClientBuilder AddTurboHttpClient(this IServiceCollection services, Action<TurboClientOptions>? configure = null)` — delegates to above with `name = string.Empty`
- [ ] `configure` registered as `services.Configure<TurboClientOptions>(name, configure)` (named options)
- [ ] `ITurboHttpClientFactory` registered as a singleton — idempotent (no double-registration on multiple calls)
- [ ] ActorSystem auto-create logic unchanged
- [ ] Returns `new TurboHttpClientBuilder(name, services)`
- [ ] Typecheck/lint passes

---

### TASK-015: Typed Client Support
**Description:** As a library user, I want `services.AddTurboHttpClient<TClient>()` so that I can inject typed HTTP clients directly via constructor injection.

**Acceptance Criteria:**
- [ ] `AddTurboHttpClient<TClient>(this IServiceCollection services, Action<TurboClientOptions>? configure = null) where TClient : class` — name = `typeof(TClient).Name`
- [ ] `AddTurboHttpClient<TClient, TImpl>(...) where TClient : class where TImpl : class, TClient` — separate implementation class variant
- [ ] `TClient` registered as Transient: `services.AddTransient<TClient>(sp => (TClient)sp.GetRequiredService<ITurboHttpClientFactory>().CreateClient(typeof(TClient).Name))`
- [ ] Both methods return `ITurboHttpClientBuilder`
- [ ] Typecheck/lint passes

---

### TASK-016: Mark `AddTurboHttpClientFactory` as `[Obsolete]`
**Description:** As a developer, I want the old registration method to emit a deprecation warning so that users migrate to the new API without a breaking change.

**Acceptance Criteria:**
- [ ] `[Obsolete("Use AddTurboHttpClient(services, configure) instead. AddTurboHttpClientFactory will be removed in a future version.")]` on `AddTurboHttpClientFactory`
- [ ] Method remains functional — no breaking change
- [ ] Existing tests that use `AddTurboHttpClientFactory` compile with warning, not error
- [ ] Typecheck/lint passes

---

### TASK-017: Factory — Add `IServiceProvider` + `IOptionsMonitor<TurboClientDescriptor>`
**Description:** As a developer, I want the factory to have access to `IServiceProvider` and the named descriptor monitor so that it can read per-client feature configuration at `CreateClient()` time.

**Acceptance Criteria:**
- [ ] `TurboHttpClientFactory` receives `IServiceProvider provider` and `IOptionsMonitor<TurboClientDescriptor> descriptors` in its constructor
- [ ] `TurboClientServiceCollectionExtensions` registers the factory so both are resolved from DI
- [ ] `CreateClient(string name)` reads `descriptors.Get(name)` for the descriptor
- [ ] Typecheck/lint passes

---

### TASK-018: Factory — Build `PipelineDescriptor` from Descriptor
**Description:** As a developer, I want the factory to build a `PipelineDescriptor` from the named `TurboClientDescriptor` so that each client gets the correct CookieJar and CacheStore.

**Acceptance Criteria:**
- [ ] `CreateClient(name)` creates `CookieJar` when `descriptor.EnableCookies` (or forwards `descriptor.CustomCookieJar` if set)
- [ ] `CreateClient(name)` creates `HttpCacheStore(descriptor.CachePolicy)` when `descriptor.CachePolicy != null`, otherwise `null`
- [ ] `CreateClient(name)` reads `descriptor.RedirectPolicy` and `descriptor.RetryPolicy`
- [ ] `PipelineDescriptor` instance assembled (middlewares added in TASK-019)
- [ ] Typecheck/lint passes

---

### TASK-019: Factory — Resolve Middleware Instances from DI
**Description:** As a developer, I want the factory to resolve all registered middleware types from DI and include them in the `PipelineDescriptor` so that each middleware instance gets its injected dependencies.

**Acceptance Criteria:**
- [ ] `CreateClient(name)` iterates `descriptor.MiddlewareTypes` and calls `provider.GetRequiredService(type)`
- [ ] Resolved instances passed as `IReadOnlyList<TurboMiddleware>` to `PipelineDescriptor`
- [ ] When `MiddlewareTypes` is empty → empty list, no DI calls made
- [ ] Completed `PipelineDescriptor` passed to `TurboClientStreamManager` (new constructor from TASK-010)
- [ ] Typecheck/lint passes

---

### TASK-020: Tests — `MiddlewareRequestStage`
**Description:** As a developer, I want stream tests for `MiddlewareRequestStage` so that I can verify async request transformation works inside an Akka graph.

**Acceptance Criteria:**
- [ ] New file `src/TurboHttp.StreamTests/Streams/MiddlewareRequestStageTests.cs`
- [ ] Test: stage adds an auth header (synchronous `ValueTask` return)
- [ ] Test: stage transforms request asynchronously (real async via `Task.Delay(1)`)
- [ ] Test: chained stages — second stage sees the output of the first
- [ ] No deadlock on async callback
- [ ] Unit tests are written and successful

---

### TASK-021: Tests — `MiddlewareResponseStage`
**Description:** As a developer, I want stream tests for `MiddlewareResponseStage` so that I can verify async response transformation and access to the original request works.

**Acceptance Criteria:**
- [ ] New file `src/TurboHttp.StreamTests/Streams/MiddlewareResponseStageTests.cs`
- [ ] Test: stage adds a custom header to the response
- [ ] Test: stage reads `original.RequestUri` via `response.RequestMessage`
- [ ] Test: chained stages — pipeline order is correct
- [ ] Unit tests are written and successful

---

### TASK-022: Tests — Builder Feature Registrations
**Description:** As a developer, I want unit tests for each `.With*()` method so that I can verify they correctly populate the named `TurboClientDescriptor`.

**Acceptance Criteria:**
- [ ] New file `src/TurboHttp.Tests/Hosting/TurboHttpClientBuilderFeatureTests.cs`
- [ ] Test: `.WithCookies()` → `descriptor.EnableCookies == true`
- [ ] Test: `.WithCookies(jar)` → `descriptor.CustomCookieJar == jar`
- [ ] Test: `.WithCache(policy)` → `descriptor.CachePolicy == policy`
- [ ] Test: `.WithRetry(policy)` → `descriptor.RetryPolicy == policy`
- [ ] Test: `.WithRedirect()` → `descriptor.RedirectPolicy != null` (default policy)
- [ ] Test: `.WithRedirect(policy)` → `descriptor.RedirectPolicy == policy`
- [ ] Unit tests are written and successful

---

### TASK-023: Tests — Middleware Registration and DI Resolution
**Description:** As a developer, I want unit tests for `.AddMiddleware<T>()` and the factory's DI resolution so that I can verify middleware is registered and resolved correctly.

**Acceptance Criteria:**
- [ ] New file `src/TurboHttp.Tests/Hosting/TurboHttpClientBuilderMiddlewareTests.cs`
- [ ] Test: `.AddMiddleware<TestMiddleware>()` → `descriptor.MiddlewareTypes` contains `typeof(TestMiddleware)`
- [ ] Test: `TestMiddleware` is registered as Transient in `Services`
- [ ] Test: `.UseRequest(transform)` → one anonymous middleware in `MiddlewareTypes`
- [ ] Test: FIFO order preserved across multiple `.AddMiddleware<>()` calls
- [ ] Test: factory resolves middleware correctly from a real `IServiceProvider`
- [ ] Unit tests are written and successful

---

### TASK-024: Tests — Named Client Isolation
**Description:** As a developer, I want integration-level tests verifying that two named clients with different configurations receive independent pipelines so that cookie jars and middleware instances are never shared.

**Acceptance Criteria:**
- [ ] New file `src/TurboHttp.Tests/Hosting/NamedClientIsolationTests.cs`
- [ ] Test: `factory.CreateClient("a")` and `factory.CreateClient("b")` return two distinct instances
- [ ] Test: cookies from client "a" do not appear in client "b"'s `CookieJar` (separate instances)
- [ ] Test: client "a" with `.WithCookies()`, client "b" without — both function correctly
- [ ] Test: `AddTurboHttpClientFactory` (old API) produces a compile-time warning; runtime still works
- [ ] Unit tests are written and successful

---

### TASK-025: Update Docs
**Description:** As a documentation reader, I want `docs/architecture/middleware-design.md` to reflect the final implementation so that the architecture is accurately documented.

**Acceptance Criteria:**
- [ ] Types match implementation: `TurboClientDescriptor`, `PipelineDescriptor`, exact method signatures
- [ ] `[Obsolete]` notes for both `AddTurboHttpClientFactory` and the policies on `TurboClientOptions`
- [ ] Example code is syntactically valid and matches real signatures
- [ ] Pipeline diagram shows middleware stage insertion points correctly

---

### TASK-026: Mark `TurboClientOptions` Policies as `[Obsolete]`
**Description:** As a developer, I want `RedirectPolicy`, `RetryPolicy`, and `CachePolicy` on `TurboClientOptions` marked obsolete so that users are guided to the builder API while existing code still compiles.

**Acceptance Criteria:**
- [ ] `TurboClientOptions.RedirectPolicy` gets `[Obsolete("Use .WithRedirect() on ITurboHttpClientBuilder instead.")]`
- [ ] `TurboClientOptions.RetryPolicy` gets `[Obsolete("Use .WithRetry() on ITurboHttpClientBuilder instead.")]`
- [ ] `TurboClientOptions.CachePolicy` gets `[Obsolete("Use .WithCache() on ITurboHttpClientBuilder instead.")]`
- [ ] Properties remain functional — no breaking change
- [ ] Backward-compat path in `Engine.CreateFlow(poolRouter, options, factory)` reads these properties via `#pragma warning disable/restore` without compile errors
- [ ] Typecheck/lint passes

---

### TASK-027: Rewrite `01_StageOrderingTests.cs` for Builder API
**Description:** As a developer, I want the stage ordering tests to activate features via `PipelineDescriptor` instead of `TurboClientOptions` policies so that the tests remain valid after always-on instantiation is removed.

**File:** `src/TurboHttp.StreamTests/Streams/01_StageOrderingTests.cs`

**Background:** These tests verify RFC ordering invariants (INV-1 through INV-10) — e.g. that cookie injection runs before cache lookup. The invariants themselves are unchanged; only the test setup needs updating.

**Acceptance Criteria:**
- [ ] All tests that activate features via `TurboClientOptions` policies are rewritten to activate them via `PipelineDescriptor` directly (StreamTests call the engine internally)
- [ ] No test implicitly assumes cookie/cache/retry/redirect are active — each test explicitly enables only the features it exercises
- [ ] All existing stage-ordering assertions are preserved unchanged
- [ ] Unit tests are written and successful

---

### TASK-028: Replace `11_EnginePipelineWiringTests.cs`
**Description:** As a developer, I want the always-on pipeline wiring tests removed and replaced with tests verifying that features are only wired when explicitly passed via `PipelineDescriptor`.

**Delete:** `src/TurboHttp.StreamTests/Streams/11_EnginePipelineWiringTests.cs`

**New file:** `src/TurboHttp.StreamTests/Streams/11_EnginePipelineDescriptorTests.cs`

**Background:** The old file tests that features are active based on `TurboClientOptions` flags. After the refactor, features come exclusively via `PipelineDescriptor` — the old file is obsolete.

**Acceptance Criteria:**
- [ ] `11_EnginePipelineWiringTests.cs` is deleted
- [ ] New `11_EnginePipelineDescriptorTests.cs` contains:
  - Test: engine with empty `PipelineDescriptor` → no cookie stage active (request passes through unchanged)
  - Test: engine with `PipelineDescriptor { CookieJar = new CookieJar() }` → cookies injected correctly
  - Test: engine with empty `PipelineDescriptor` → no retry stage active (503 passed through directly)
  - Test: engine with `PipelineDescriptor { RetryPolicy = new RetryPolicy() }` → 503 is retried correctly
  - Test: engine with empty `PipelineDescriptor` → no redirect stage active (301 passed through directly)
  - Test: engine with `PipelineDescriptor { RedirectPolicy = new RedirectPolicy() }` → 301 redirected correctly
- [ ] Unit tests are written and successful

---

### TASK-029: Full Engine Refactoring
**Description:** As a developer, I want `Engine.cs` fully refactored so that its responsibilities are separated into cohesive units, access modifiers are correct, test seams do not leak into production code, and the public API exclusively uses `PipelineDescriptor`.

**Background:** `Engine.cs` currently mixes three distinct concerns in one 400-line file: pre-processing graph construction (island 1), protocol core graph construction (island 2), and post-processing graph construction (island 3). The class is `public` without need, has an `internal static BuildConnectionFlowPublic` that exists only for tests, and the `PostProcessShape` nested class adds clutter. After TASK-007–009 the class already accepts `PipelineDescriptor`, but the structure and naming remain ad-hoc.

**Files affected:**
- `src/TurboHttp/Streams/Engine.cs` — split and cleaned
- `src/TurboHttp/Streams/PreProcessingGraphBuilder.cs` — new (island 1)
- `src/TurboHttp/Streams/PostProcessingGraphBuilder.cs` — new (island 2 post + shapes)
- `src/TurboHttp/Streams/ProtocolCoreGraphBuilder.cs` — new (island 2 core)
- `src/TurboHttp/Streams/PostProcessShape.cs` — new (extracted shape type)
- Any stream tests calling `Engine.BuildConnectionFlowPublic` directly — updated

**Acceptance Criteria:**

**Access modifiers:**
- [ ] `Engine` changed to `internal sealed class` — it is never part of the public API
- [ ] `BuildConnectionFlowPublic` removed — tests that call it are refactored to use the transport factory injection overload of `Engine.CreateFlow` (the `internal` overload that accepts `http10Factory`, `http11Factory`, etc.)

**Responsibility split — three new `internal static` builder classes:**
- [ ] `PreProcessingGraphBuilder.Build(PipelineDescriptor, Func<TurboRequestOptions>)` — builds island 1: `RequestEnricherStage` → user request middleware → redirect merge → `CookieInjectionStage` → retry merge → `CacheLookupStage`
- [ ] `ProtocolCoreGraphBuilder.Build(IActorRef, TurboClientOptions, transport factories...)` — builds island 2: `Partition` → per-version `BuildProtocolFlow` → `Merge` → `DecompressionStage`
- [ ] `PostProcessingGraphBuilder.Build(PipelineDescriptor)` — builds island 3: `CookieStorageStage` → `CacheStorageStage` → `RetryStage` → cache hit merge → `RedirectStage` → user response middleware

**Shape extraction:**
- [ ] `PostProcessShape` moved to its own file `src/TurboHttp/Streams/PostProcessShape.cs`

**`Engine` class after refactor:**
- [ ] `Engine` is a thin orchestrator only — calls the three builders and wires their outputs together (async boundaries, feedback loops)
- [ ] Public API: `CreateFlow(IActorRef, TurboClientOptions, Func<TurboRequestOptions>)` remains for backward-compat (builds `PipelineDescriptor` internally from options)
- [ ] Internal API: `CreateFlow(IActorRef, TurboClientOptions, Func<TurboRequestOptions>, PipelineDescriptor)` — used by `TurboClientStreamManager`
- [ ] Test API: `CreateFlow(http10Factory, http11Factory, http20Factory, http30Factory, TurboClientOptions?)` — existing internal overload retained

**Tests:**
- [ ] All stream tests that previously called `Engine.BuildConnectionFlowPublic` are updated to use the transport factory injection overload instead
- [ ] All existing stream tests pass without modification to their assertions
- [ ] Build green, no new warnings introduced

---

## Functional Requirements

- FR-1: `services.AddTurboHttpClient(name, configure)` must return `ITurboHttpClientBuilder`
- FR-2: Each named client gets its own isolated Akka pipeline — own `CookieJar`, `HttpCacheStore`, own middleware instances
- FR-3: `ProcessRequestAsync` is called on initial requests only, NOT on redirect requests
- FR-4: `ProcessResponseAsync` is called on final responses only — after redirect and retry resolution
- FR-5: `AddMiddleware<T>()` resolves `T` via DI — constructor injection of dependencies supported
- FR-6: `TurboClientOptions.RedirectPolicy/RetryPolicy/CachePolicy` remain backward-compatible but marked `[Obsolete]`
- FR-7: `TurboClientOptions` remains the home for transport configuration (timeouts, TLS, reconnect intervals)
- FR-8: An engine with an empty `PipelineDescriptor` wires no cookie/cache/retry/redirect stages — no hidden overhead
- FR-9: `Engine` internals are not part of the public API surface

## Non-Goals

- No `DelegatingHandler` equivalent with a "call next" pattern — no per-request handler stack
- No `BidiFlow`-based middleware API
- No changes to the retry/redirect feedback loop logic itself
- No circuit breaker or request deduplication
- No persistent cookie store (remains in-memory)
- No changes to the individual protocol encoder/decoder stages (HTTP/1.x, HTTP/2, HPACK)

## Technical Considerations

- `GetAsyncCallback` in Akka.Streams is the correct way to bridge async work in `GraphStageLogic` — no `.Result`, no `Task.Run`
- `IConfigureNamedOptions<T>` allows multiple configure delegates per name — important because each extension method calls `Configure` independently
- `TurboClientDescriptor` must be a mutable class (not a record) because `IConfigureNamedOptions` mutates the instance in-place
- Middleware order: FIFO on the request side, FIFO on the response side (same order as registration)
- `IServiceProvider` in the factory constructor is needed to resolve middleware instances at `CreateClient()` time
- TASK-029 should only begin after TASK-007, TASK-008, and TASK-009 are complete — the refactoring builds on the already-parameterised engine

## Task Dependencies

```
T-01 (TurboMiddleware)
  ├── T-05 (RequestStage) → T-08 (wire into engine)
  ├── T-06 (ResponseStage) → T-09 (wire into engine)
  └── T-11 (Builder) → T-13 (middleware extensions)

T-02 (ITurboHttpClientBuilder)
  └── T-11 (Builder) → T-12, T-13, T-14, T-15

T-03 (TurboClientDescriptor)
  ├── T-12 (feature extensions)
  ├── T-13 (middleware extensions)
  └── T-17 → T-18 → T-19 (factory)

T-04 (PipelineDescriptor)
  ├── T-07 (engine accepts descriptor)
  └── T-10 (StreamManager) → T-19 (factory uses StreamManager)

T-07 ← T-04
T-08 ← T-05, T-07
T-09 ← T-06, T-07
T-10 ← T-04, T-07
T-14 ← T-11, T-02
T-15 ← T-14
T-16 ← T-14
T-17 ← T-14
T-18 ← T-17, T-04
T-19 ← T-18, T-10

T-20 ← T-05
T-21 ← T-06
T-22 ← T-12
T-23 ← T-13, T-19
T-24 ← T-19, T-14
T-26 ← T-04  (mark options obsolete; backward-compat suppress in engine)
T-27 ← T-07, T-08, T-09  (rewrite ordering tests for PipelineDescriptor)
T-28 ← T-07  (delete old wiring tests; write new descriptor tests)
T-29 ← T-07, T-08, T-09  (engine refactor requires parameterised pipeline to exist first)
T-25 ← all
```

## Success Metrics

- All 29 tasks complete, build green, all tests passing
- Two named clients with different middleware configurations run in isolation
- `AddTurboHttpClientFactory` compiles with deprecation warning — no breaking change
- A user can write `AuthMiddleware(ITokenProvider)` and wire it via `.AddMiddleware<AuthMiddleware>()` without knowing a single Akka type
- `Engine.cs` is under 150 lines; the three island builders each live in their own file with a single clear responsibility
- No `internal` members in `Engine` exist solely to satisfy test access

## Open Questions

- Should `.WithRedirect()` with no argument implicitly set sensible defaults (max 10 redirects, no HTTPS→HTTP downgrade)? Or always require an explicit `RedirectPolicy`?
- Should middleware instances be Transient per `CreateClient()` call (one instance per pipeline materialization, current plan) or Scoped per DI scope? Implication: stateful middleware would need to be scoped to work correctly.
- For TASK-029: should `PreProcessingGraphBuilder`, `ProtocolCoreGraphBuilder`, and `PostProcessingGraphBuilder` be `static` classes or instance classes? Static is simpler; instance would allow subclassing for advanced test scenarios.
