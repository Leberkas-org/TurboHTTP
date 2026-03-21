# Plan: Conditional Stage Inclusion in Pipeline Graph

## Introduction

`Engine.BuildExtendedPipeline()` **always** adds all middleware stages (CookieInjection, CacheLookup,
Retry, Redirect, CookieStorage, CacheStorage, Decompression) to the Akka.Streams graph, even when
their policies are null. Null-policy stages act as pass-throughs but still exist as graph nodes with
their structural elements (MergePreferred feedback loops, Buffer(4), FanOut shapes, Merge nodes).
This adds unnecessary scheduling overhead and graph complexity.

**Goal**: Only include stages and their structural elements in the graph when the corresponding
feature is actually enabled in `PipelineDescriptor`.

---

## Goals

- Only add stages to the graph when their feature is enabled (non-null policy/jar/store)
- Only create MergePreferred + Buffer(4) feedback loops when Retry/Redirect is enabled
- Only create CacheLookup FanOut + CacheMerge when Cache is enabled
- Make DecompressionStage conditional via a new `AutomaticDecompression` flag
- Minimal graph for `PipelineDescriptor.Empty`: Enricher -> Engine -> Output (2 stages, 0 feedback loops)
- All existing tests must remain green
- Stage ordering invariants (INV-1 through INV-10) preserved when features are active

---

## User Stories

### TASK-001: Add `AutomaticDecompression` to PipelineDescriptor
**Description:** As a developer, I want an `AutomaticDecompression` flag in `PipelineDescriptor`
so that the DecompressionStage can be conditionally included in the graph.

**Acceptance Criteria:**
- [ ] `PipelineDescriptor` has new property `bool AutomaticDecompression` (default `true`)
- [ ] `PipelineDescriptor.Empty` has `AutomaticDecompression: true` (backward compatibility)
- [ ] Builder API has `.WithDecompression(bool enabled = true)` extension method
- [ ] Typecheck/lint passes
- [ ] Unit tests are written and successful

### TASK-002: Conditional pre-processing wiring
**Description:** As a developer, I want pre-processing stages (CookieInjection, CacheLookup,
MergePreferred for Redirect/Retry) to only be in the graph when their feature is enabled,
so that the graph stays minimal when features are disabled.

**Acceptance Criteria:**
- [ ] `MergePreferred(redirect)` only added when `RedirectPolicy != null`
- [ ] `CookieInjectionStage` only added when `CookieJar != null`
- [ ] `MergePreferred(retry)` only added when `RetryPolicy != null`
- [ ] `CacheLookupStage` only added when `CacheStore != null` — otherwise `requestTip` flows directly to engine
- [ ] Nullable local variables for `redirectMerge`, `retryMerge`, `cacheHitOut`
- [ ] With `PipelineDescriptor.Empty`: request goes directly from Enricher to Engine
- [ ] Typecheck/lint passes
- [ ] Unit tests are written and successful

### TASK-003: Inline and conditional post-processing wiring
**Description:** As a developer, I want post-processing stages (CookieStorage, CacheStorage,
RetryStage, RedirectStage, Decompression) to be wired inline in `BuildExtendedPipeline`
conditionally, instead of always being added via `BuildPostProcessGraph`.

**Acceptance Criteria:**
- [ ] `BuildPostProcessGraph` method and `PostProcessShape` class are removed
- [ ] Post-processing stages are wired inline in `BuildExtendedPipeline`
- [ ] `DecompressionStage` only added when `AutomaticDecompression == true`
- [ ] `CookieStorageStage` only added when `CookieJar != null`
- [ ] `CacheStorageStage` only added when `CacheStore != null`
- [ ] `RetryStage` only added when `RetryPolicy != null` — including feedback loop to `retryMerge`
- [ ] `Merge(cacheHit)` only added when `CacheStore != null`
- [ ] `RedirectStage` only added when `RedirectPolicy != null` — including feedback loop to `redirectMerge`
- [ ] `MiddlewareResponseStage(s)` still added for all registered middlewares
- [ ] `responseTip` outlet variable chains through only the active stages
- [ ] Typecheck/lint passes
- [ ] Unit tests are written and successful

### TASK-004: Update and extend tests
**Description:** As a developer, I want tests that verify the graph is actually minimal when
features are disabled and correct when features are enabled.

**Acceptance Criteria:**
- [ ] All existing tests in `01_StageOrderingTests.cs` are green
- [ ] All existing tests in `11_EnginePipelineDescriptorTests.cs` are green
- [ ] New test: `PipelineDescriptor.Empty` — 200 OK flows through minimal graph
- [ ] New test: Only Retry enabled — 503 is retried, no Redirect/Cookie/Cache in graph
- [ ] New test: Only Redirect enabled — 301 is followed, no Retry/Cookie/Cache in graph
- [ ] New test: Only Cookies enabled — Cookie header is injected, no Retry/Redirect/Cache
- [ ] New test: Only Cache enabled — cache hit is returned directly
- [ ] New test: All features enabled — full pipeline behavior (regression guard)
- [ ] New test: `AutomaticDecompression = false` — gzip response is not decompressed
- [ ] Typecheck/lint passes

### TASK-005: Build validation and cleanup
**Description:** As a developer, I want to ensure the entire build is green and no regressions
have been introduced.

**Acceptance Criteria:**
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` — zero errors, zero warnings
- [ ] `dotnet test src/TurboHttp.sln` — all tests green
- [ ] Stage port validator — no naming violations
- [ ] Old `PostProcessShape` class is fully removed
- [ ] Old `BuildPostProcessGraph` method is fully removed
- [ ] XML docs on `BuildExtendedPipeline` are updated to reflect conditional wiring

---

## Functional Requirements

- FR-1: When `PipelineDescriptor.RedirectPolicy` is null, `RedirectStage`, `MergePreferred(redirect)`, and the redirect feedback buffer must not exist in the graph
- FR-2: When `PipelineDescriptor.RetryPolicy` is null, `RetryStage`, `MergePreferred(retry)`, and the retry feedback buffer must not exist in the graph
- FR-3: When `PipelineDescriptor.CookieJar` is null, `CookieInjectionStage` and `CookieStorageStage` must not exist in the graph
- FR-4: When `PipelineDescriptor.CacheStore` is null, `CacheLookupStage`, `CacheStorageStage`, and `Merge(cacheHit)` must not exist in the graph
- FR-5: When `PipelineDescriptor.AutomaticDecompression` is false, `DecompressionStage` must not exist in the graph
- FR-6: `RequestEnricherStage` is always in the graph (not conditional)
- FR-7: `MiddlewareRequestStage` and `MiddlewareResponseStage` are always in the graph when middlewares are registered
- FR-8: The Engine (BuildEngineCoreGraph) is always in the graph with its async boundary
- FR-9: Stage ordering invariants INV-1 through INV-10 are preserved when all features are enabled
- FR-10: Feedback loops continue to use `Buffer(4, OverflowStrategy.Backpressure)` and `MergePreferred`
- FR-11: Pre- and post-processing fuse into one island (no separate async boundary for post-processing)

---

## Non-Goals

- No new builder pattern or GraphBuilder class — logic stays in `Engine.cs`
- No dynamic `PostProcessShape` — shape is removed entirely, everything inline
- No changes to individual stage implementations (RetryStage, RedirectStage, etc.)
- No changes to the builder API (WithCookies, WithRetry, etc.) except new `WithDecompression`
- No changes to the engine core (BuildEngineCoreGraph, Partition, Protocol Engines)

---

## Technical Considerations

- **Akka.Streams port constraint**: All ports of a Shape must be connected. By inlining, there are no custom shapes — only the final `FlowShape<HttpRequestMessage, HttpResponseMessage>`
- **Feedback loop cycles**: MergePreferred + Buffer(4) are only needed when Retry/Redirect is active. Without these stages there are no cycles in the graph
- **Async boundary**: Only the Engine retains `.WithAttributes(Attributes.CreateAsyncBoundary())`. Pre+post-processing run in one fused island
- **Backward compatibility**: `PipelineDescriptor.Empty` has `AutomaticDecompression: true` so the default path behavior doesn't change
- **Variable scoping**: `redirectMerge`, `retryMerge`, `cacheHitOut` are declared as `MergePreferred?` / `Outlet?` and only assigned when the feature is active. Post-processing reads these variables and wires feedback only when non-null

### Graph Comparison

**Before (PipelineDescriptor.Empty):**
```
Enricher -> MergePreferred(redirect) -> CookieInjection(passthrough) -> MergePreferred(retry)
  -> CacheLookup(passthrough) -> Engine+Decomp -> CookieStorage(passthrough)
  -> CacheStorage(passthrough) -> Retry(passthrough) -> Merge(cacheHit) -> Redirect(passthrough) -> Output
  + 2x feedback loops with Buffer(4)
  = 12 stages, 2 feedback loops
```

**After (PipelineDescriptor.Empty):**
```
Enricher -> Engine -> Output
  = 2 stages, 0 feedback loops
```

**After (only RedirectPolicy set):**
```
Enricher -> MergePreferred(redirect) -> Engine -> Redirect -> Output
  + 1 feedback loop with Buffer(4)
  = 4 stages, 1 feedback loop
```

**After (all features active):**
```
Enricher -> MergePreferred(redirect) -> CookieInjection -> MergePreferred(retry)
  -> CacheLookup -> Engine+Decomp -> CookieStorage -> CacheStorage
  -> Retry -> Merge(cacheHit) -> Redirect -> Output
  + 2x feedback loops with Buffer(4)
  = 12 stages, 2 feedback loops (identical to before)
```

---

## Success Metrics

- `PipelineDescriptor.Empty` produces a graph with only 2 stages (Enricher + Engine)
- Each individual feature can be activated in isolation without pulling in other features
- All ~1800 existing tests remain green
- No performance regression when all features are fully activated

---

## Open Questions

- Should `AutomaticDecompression` also be controllable per-request (via `TurboRequestOptions`)
  or is client-level sufficient?
