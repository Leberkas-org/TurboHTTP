# Plan: BidiFlow Pipeline Architecture with Conditional Stage Inclusion

## Introduction

The current pipeline in `Engine.BuildExtendedPipeline()` has two problems:

1. **Split stages**: Each feature has separate request-side and response-side stages
   (e.g., `CookieInjectionStage` + `CookieStorageStage`) wired together with external
   `MergePreferred` feedback loops, `Buffer(4)`, and a custom `PostProcessShape`.

2. **Always-on stages**: All stages are added to the graph even when their policy is null.
   Null-policy stages act as pass-throughs but their structural elements (MergePreferred,
   FanOut shapes, Merge nodes) still exist — unnecessary scheduling overhead and complexity.

This plan solves both problems by converting each feature into a single **BidiFlow** per
feature and composing the pipeline via `BidiFlow.Atop()`. Conditional inclusion is trivial:
omit a BidiFlow from the Atop chain when its policy is null.

**Pipeline becomes:**
```csharp
enricherFlow
    .Via(middlewareBidi)
    .Via(redirectBidi.Atop(cookieBidi).Atop(retryBidi).Atop(cacheBidi).Atop(decompressionBidi)
         .Join(engineFlow))
```

**With conditional inclusion:**
```csharp
// Start with identity BidiFlow, conditionally layer features
var features = BidiFlow.Identity<HttpRequestMessage, HttpResponseMessage>();

if (descriptor.RedirectPolicy is not null)
    features = new RedirectBidiStage(descriptor.RedirectPolicy).Atop(features);
if (descriptor.CookieJar is not null)
    features = new CookieBidiStage(descriptor.CookieJar).Atop(features);
if (descriptor.RetryPolicy is not null)
    features = new RetryBidiStage(descriptor.RetryPolicy).Atop(features);
if (descriptor.CacheStore is not null)
    features = new CacheBidiStage(descriptor.CacheStore, descriptor.CachePolicy).Atop(features);
if (descriptor.AutomaticDecompression)
    features = new DecompressionBidiStage().Atop(features);

var pipeline = enricherFlow.Via(features.Join(engineFlow));
```

---

## Goals

- Convert each feature (Cookies, Cache, Retry, Redirect, Decompression) into one BidiFlow
- Handle retry/redirect feedback loops internally within each BidiStage (no external MergePreferred)
- Handle cache hit short-circuit internally within CacheBidiStage
- Compose pipeline via `BidiFlow.Atop()` stacking
- Conditional inclusion: only include BidiFlows for non-null policies
- Minimal graph for `PipelineDescriptor.Empty`: Enricher → Engine → Output (no feature stages)
- Add `AutomaticDecompression` flag to `PipelineDescriptor` for decompression control
- Preserve stage ordering invariants (INV-1 through INV-10) when features are active
- All existing tests must remain green (or be adapted)
- Remove `BuildPostProcessGraph`, `PostProcessShape`, all old split stages

---

## User Stories

### TASK-001: Add `AutomaticDecompression` to PipelineDescriptor
**Description:** As a developer, I want an `AutomaticDecompression` flag in `PipelineDescriptor`
so that decompression can be conditionally included in the pipeline.

**Acceptance Criteria:**
- [x] `PipelineDescriptor` has new property `bool AutomaticDecompression` (default `true`)
- [x] `PipelineDescriptor.Empty` has `AutomaticDecompression: true` (backward compatibility)
- [x] Builder API has `.WithDecompression(bool enabled = true)` extension method
- [x] Typecheck/lint passes
- [x] Unit tests are written and successful

### TASK-002: Create CookieBidiStage
**Description:** As a developer, I want a single `CookieBidiStage` that replaces both
`CookieInjectionStage` and `CookieStorageStage`, handling cookie injection on the request
path and Set-Cookie storage on the response path.

**Acceptance Criteria:**
- [ ] `CookieBidiStage` is a `GraphStage<BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>>`
- [ ] Request direction (In1→Out1): injects cookies from `CookieJar` into request headers
- [ ] Response direction (In2→Out2): extracts Set-Cookie headers and stores in `CookieJar`
- [ ] Pass-through when `CookieJar` is null (no-op in both directions)
- [ ] Port names: `"Cookie.In.Request"`, `"Cookie.Out.Request"`, `"Cookie.In.Response"`, `"Cookie.Out.Response"`
- [ ] Typecheck/lint passes
- [ ] Unit tests are written and successful

### TASK-003: Create DecompressionBidiStage
**Description:** As a developer, I want a `DecompressionBidiStage` that decompresses
response bodies while passing requests through unchanged.

**Acceptance Criteria:**
- [ ] `DecompressionBidiStage` is a `GraphStage<BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>>`
- [ ] Request direction (In1→Out1): pass-through (forward unchanged)
- [ ] Response direction (In2→Out2): decompresses gzip/deflate/brotli, removes Content-Encoding header
- [ ] Reuses existing `ContentEncodingDecoder` logic from current `DecompressionStage`
- [ ] Port names: `"Decompression.In.Request"`, `"Decompression.Out.Request"`, `"Decompression.In.Response"`, `"Decompression.Out.Response"`
- [ ] Typecheck/lint passes
- [ ] Unit tests are written and successful

### TASK-004: Create CacheBidiStage
**Description:** As a developer, I want a `CacheBidiStage` that handles cache lookup on
the request path and cache storage on the response path, with internal short-circuit for
cache hits.

**Acceptance Criteria:**
- [ ] `CacheBidiStage` is a `GraphStage<BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>>`
- [ ] Request direction (In1): cache lookup
  - Cache miss: forward request on Out1 (to engine)
  - Cache hit: push cached response directly on Out2 (short-circuit, Out1 never gets the request)
  - Must-revalidate: build conditional request (If-None-Match/If-Modified-Since), forward on Out1
- [ ] Response direction (In2→Out2): store cacheable 2xx responses, handle 304 merge, invalidate on unsafe methods
- [ ] Internal demand management: buffer hit response if Out2 has no demand yet
- [ ] Pass-through when `HttpCacheStore` is null
- [ ] Reuses `CacheFreshnessEvaluator`, `CacheValidationRequestBuilder`, `HttpCacheStore` from Protocol layer
- [ ] Port names: `"Cache.In.Request"`, `"Cache.Out.Request"`, `"Cache.In.Response"`, `"Cache.Out.Response"`
- [ ] Typecheck/lint passes
- [ ] Unit tests are written and successful

### TASK-005: Create RetryBidiStage
**Description:** As a developer, I want a `RetryBidiStage` that evaluates retry internally
on the response path, re-injecting retry requests on the request output without any external
feedback loop.

**Acceptance Criteria:**
- [ ] `RetryBidiStage` is a `GraphStage<BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>>`
- [ ] Request direction (In1→Out1): forward request, buffer for potential retry
- [ ] Response direction (In2): evaluate retry via `RetryEvaluator`
  - Not retryable: push final response on Out2
  - Retryable (immediate): dispose response, push retry request on Out1 (priority over new requests from In1)
  - Retryable (Retry-After): schedule timer, push retry request on Out1 when timer fires
- [ ] Internal state machine: IDLE → AWAITING_RESPONSE → (RETRYING | TIMER_WAITING | IDLE)
- [ ] Attempt count tracked via `HttpRequestMessage.Options`
- [ ] Respects `MaxPendingRetries` and `RetryPolicy.MaxAttempts`
- [ ] Pass-through when `RetryPolicy` is null (forward In1→Out1, In2→Out2 directly)
- [ ] Reuses `RetryEvaluator` from Protocol layer
- [ ] Port names: `"Retry.In.Request"`, `"Retry.Out.Request"`, `"Retry.In.Response"`, `"Retry.Out.Response"`
- [ ] Typecheck/lint passes
- [ ] Unit tests are written and successful

### TASK-006: Create RedirectBidiStage
**Description:** As a developer, I want a `RedirectBidiStage` that evaluates redirects
internally on the response path, re-injecting redirect requests on the request output
without any external feedback loop.

**Acceptance Criteria:**
- [ ] `RedirectBidiStage` is a `GraphStage<BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>>`
- [ ] Request direction (In1→Out1): forward request unchanged
- [ ] Response direction (In2): evaluate redirect via `RedirectHandler`
  - Not redirect: push final response on Out2
  - Redirect: build new request via `RedirectHandler.BuildRedirectRequest()`, push on Out1 (internally loops)
  - Max redirects or loop detected: push response on Out2 as final
- [ ] Internal state machine: IDLE → AWAITING_RESPONSE → (REDIRECTING | IDLE)
- [ ] `RedirectHandler` per request chain stored in `HttpRequestMessage.Options`
- [ ] Pass-through when `RedirectPolicy` is null
- [ ] Reuses `RedirectHandler` from Protocol layer
- [ ] Port names: `"Redirect.In.Request"`, `"Redirect.Out.Request"`, `"Redirect.In.Response"`, `"Redirect.Out.Response"`
- [ ] Typecheck/lint passes
- [ ] Unit tests are written and successful

### TASK-007: Refactor Engine to BidiFlow Atop chain with conditional inclusion
**Description:** As a developer, I want `Engine.BuildExtendedPipeline` to compose feature
BidiFlows via conditional `Atop` stacking, replacing all manual wiring, MergePreferred
feedback loops, and PostProcessShape.

**Acceptance Criteria:**
- [ ] `BuildExtendedPipeline` uses conditional `BidiFlow.Atop()` to compose feature layers
- [ ] Only BidiFlows for non-null policies are included in the Atop chain
- [ ] `PipelineDescriptor.Empty` produces minimal graph: Enricher → Engine → Output
- [ ] `BuildPostProcessGraph` and `PostProcessShape` are removed
- [ ] No more `MergePreferred` for redirect/retry feedback
- [ ] No more external `Buffer(4)` for feedback loops
- [ ] No more `Merge(cacheHit)` for cache hit merging
- [ ] `RequestEnricherStage` remains as a Flow prepended before the BidiFlow chain
- [ ] `MiddlewareRequestStage` / `MiddlewareResponseStage` composed as a MiddlewareBidiFlow or remain as Flows
- [ ] Engine async boundary preserved on the protocol engine
- [ ] Typecheck/lint passes
- [ ] Unit tests are written and successful

### TASK-008: Adapt and extend tests
**Description:** As a developer, I want all existing tests updated and new tests added
to verify the BidiFlow architecture and conditional inclusion.

**Acceptance Criteria:**
- [ ] `01_StageOrderingTests.cs` — adapted or rewritten for BidiFlow composition
- [ ] `11_EnginePipelineDescriptorTests.cs` — adapted to verify conditional BidiFlow inclusion
- [ ] Cookie stage tests — merged/adapted for `CookieBidiStage`
- [ ] Cache stage tests — adapted for `CacheBidiStage`
- [ ] `03_RetryStageTests.cs` — adapted for `RetryBidiStage`
- [ ] `02_RedirectStageTests.cs` — adapted for `RedirectBidiStage`
- [ ] New tests: internal feedback (retry loop, redirect loop, cache short-circuit)
- [ ] New tests: conditional composition (empty chain, single feature, all features)
- [ ] New test: `PipelineDescriptor.Empty` — 200 OK flows through minimal graph
- [ ] New test: `AutomaticDecompression = false` — gzip response is not decompressed
- [ ] New test: each feature in isolation (only Retry, only Redirect, only Cookies, only Cache)
- [ ] `dotnet test src/TurboHttp.sln` — all tests green

### TASK-009: Remove old stages and cleanup
**Description:** As a developer, I want the old separate stages removed after the BidiFlow
architecture is fully in place.

**Acceptance Criteria:**
- [ ] `CookieInjectionStage.cs` removed
- [ ] `CookieStorageStage.cs` removed
- [ ] `CacheLookupStage.cs` removed
- [ ] `CacheStorageStage.cs` removed
- [ ] `RetryStage.cs` removed
- [ ] `RedirectStage.cs` removed
- [ ] `DecompressionStage.cs` removed
- [ ] `PostProcessShape` class removed
- [ ] `BuildPostProcessGraph` method removed
- [ ] No dead code or unused references remaining
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` — zero errors, zero warnings
- [ ] `dotnet test src/TurboHttp.sln` — all tests green
- [ ] Stage port validator — no naming violations

---

## Functional Requirements

- FR-1: Each feature (Cookies, Cache, Retry, Redirect, Decompression) is exactly one BidiFlow
- FR-2: BidiFlows compose via `Atop()` — stacking order determines request/response ordering
- FR-3: Retry and redirect feedback loops are internal to their BidiStages (no external MergePreferred)
- FR-4: Cache hit short-circuit is internal to CacheBidiStage (response pushed on Out2 directly from request handler)
- FR-5: Conditional inclusion: omitting a BidiFlow from the Atop chain disables that feature entirely (no graph nodes)
- FR-6: When `PipelineDescriptor.AutomaticDecompression` is false, `DecompressionBidiStage` is not in the graph
- FR-7: `PipelineDescriptor.Empty` produces a minimal graph with zero feature stages
- FR-8: `RequestEnricherStage` remains a standalone Flow (not a BidiFlow — no response-side logic)
- FR-9: `MiddlewareRequestStage` / `MiddlewareResponseStage` are always in the graph when middlewares are registered
- FR-10: Protocol engine retains its async boundary
- FR-11: Stage ordering invariants INV-1 through INV-10 are preserved when all features are active
- FR-12: Each BidiStage is a pass-through when its policy/config is null (defense in depth, though conditional inclusion should prevent this)

---

## Non-Goals

- No changes to protocol engine stages (Http10Engine, Http11Engine, Http20Engine)
- No changes to ConnectionReuseStage (stays as FanOut inside the engine)
- No changes to the builder API (WithCookies, WithRetry, etc.) except new `WithDecompression`
- No changes to the Protocol layer (CookieJar, RedirectHandler, RetryEvaluator, etc.)

---

## Technical Considerations

### BidiFlow Atop Stacking Order

The Atop chain (outermost to innermost) determines execution order:

```
Request direction:  Outermost.In1 → ... → Innermost.Out1 → Engine
Response direction: Engine → Innermost.In2 → ... → Outermost.Out2
```

Required stacking (outermost → innermost):

```
Redirect → Cookie → Retry → Cache → Decompression → Engine
```

This produces:
- **Request**: Redirect(pass) → Cookie(inject) → Retry(pass+buffer) → Cache(lookup) → Decomp(pass) → Engine
- **Response**: Engine → Decomp(decompress) → Cache(store) → Retry(evaluate) → Cookie(store) → Redirect(evaluate)

### Invariant Verification

| Invariant | BidiFlow Behavior | Status |
|-----------|-------------------|--------|
| INV-2: CookieInjection before CacheLookup | Cookie is outer, Cache is inner → request hits Cookie first | ✓ |
| INV-3: CookieStorage before CacheStorage | Response direction: Cache stores before Cookie stores | ⚠️ Reversed |
| INV-5: Retry before Redirect | Response direction: Retry evaluates before Redirect | ✓ |
| INV-6: Decompression before CacheStorage | Decomp is innermost → decompresses before Cache stores | ✓ |
| INV-7: Redirect gets fresh cookies | Redirect is outermost → redirect request flows through Cookie | ✓ |
| INV-8: Retry preserves original cookies | Retry is inner to Cookie → retry request bypasses Cookie | ✓ |
| INV-9: Redirect skips re-enrichment | Redirect is inner to Enricher → redirect stays inside BidiFlow chain | ✓ |
| INV-10: Cache hits bypass retry | Cache hit pushed on Out2 → flows through Retry (200 OK passes through) | ✓ |

**INV-3 resolution**: Cookie storage is a side-effect (Set-Cookie extraction into CookieJar).
The response passes through unchanged. Whether cookies are stored before or after cache
storage has no functional impact — the CookieJar will have the cookies for subsequent
requests either way. Update invariant documentation to reflect BidiFlow architecture.

### Internal Feedback State Machine (Retry/Redirect)

```
States: IDLE → AWAITING_RESPONSE → RETRYING/REDIRECTING → AWAITING_RESPONSE → ...

IDLE:
  In1.onPush: buffer request, push on Out1 → AWAITING_RESPONSE
  Out1.onPull: propagate demand to In1

AWAITING_RESPONSE:
  In2.onPush: evaluate response
    - Final: push on Out2 → IDLE
    - Retry/Redirect: buffer new request → RETRYING
  Out2.onPull: (no-op, waiting for response)

RETRYING/REDIRECTING:
  Out1.onPull: push retry/redirect request → AWAITING_RESPONSE
  (In1 is not pulled in this state — new requests wait)
```

### Cache Short-Circuit State Machine

```
States: IDLE → FORWARDED_TO_ENGINE | HIT_BUFFERED

IDLE:
  In1.onPush: lookup cache
    - Miss: push on Out1 → FORWARDED_TO_ENGINE
    - Hit: if Out2 has demand → push response on Out2 → IDLE
           else buffer response → HIT_BUFFERED

FORWARDED_TO_ENGINE:
  In2.onPush: store in cache, push on Out2 → IDLE

HIT_BUFFERED:
  Out2.onPull: push buffered response → IDLE
```

### Graph Comparison

**Before (PipelineDescriptor.Empty — all null):**
```
Enricher → MergePreferred(redirect) → CookieInjection(passthrough) → MergePreferred(retry)
  → CacheLookup(passthrough) → Engine+Decomp → CookieStorage(passthrough)
  → CacheStorage(passthrough) → Retry(passthrough) → Merge(cacheHit) → Redirect(passthrough) → Output
  + 2x feedback loops with Buffer(4)
  = 12 stages, 2 feedback loops
```

**After (PipelineDescriptor.Empty — all null):**
```
Enricher → Engine → Output
  = 2 stages, 0 feedback loops, 0 BidiFlows
```

**After (only RedirectPolicy set):**
```
Enricher → RedirectBidi(Engine) → Output
  = 1 BidiFlow wrapping Engine, feedback handled internally
```

**After (all features active):**
```
Enricher → RedirectBidi(CookieBidi(RetryBidi(CacheBidi(DecompressionBidi(Engine))))) → Output
  = 5 BidiFlows layered via Atop, all feedback/short-circuit internal
```

### Key Files

| New File | Purpose |
|----------|---------|
| `src/TurboHttp/Streams/Stages/CookieBidiStage.cs` | Cookie injection + storage BidiFlow |
| `src/TurboHttp/Streams/Stages/DecompressionBidiStage.cs` | Response decompression BidiFlow |
| `src/TurboHttp/Streams/Stages/CacheBidiStage.cs` | Cache lookup + storage BidiFlow |
| `src/TurboHttp/Streams/Stages/RetryBidiStage.cs` | Retry with internal feedback BidiFlow |
| `src/TurboHttp/Streams/Stages/RedirectBidiStage.cs` | Redirect with internal feedback BidiFlow |

| Modified File | Change |
|---------------|--------|
| `src/TurboHttp/Streams/Engine.cs` | Replace manual wiring with conditional BidiFlow.Atop chain |
| `src/TurboHttp/Streams/PipelineDescriptor.cs` | Add `AutomaticDecompression` flag |
| `src/TurboHttp/Middleware/TurboHttpClientBuilderExtensions.cs` | Add `WithDecompression()` |

| Removed Files | Reason |
|---------------|--------|
| `src/TurboHttp/Streams/Stages/CookieInjectionStage.cs` | Replaced by CookieBidiStage |
| `src/TurboHttp/Streams/Stages/CookieStorageStage.cs` | Replaced by CookieBidiStage |
| `src/TurboHttp/Streams/Stages/CacheLookupStage.cs` | Replaced by CacheBidiStage |
| `src/TurboHttp/Streams/Stages/CacheStorageStage.cs` | Replaced by CacheBidiStage |
| `src/TurboHttp/Streams/Stages/RetryStage.cs` | Replaced by RetryBidiStage |
| `src/TurboHttp/Streams/Stages/RedirectStage.cs` | Replaced by RedirectBidiStage |
| `src/TurboHttp/Streams/Stages/DecompressionStage.cs` | Replaced by DecompressionBidiStage |

---

## Success Metrics

- `PipelineDescriptor.Empty` produces a graph with zero feature stages
- Each feature can be activated in isolation without pulling in other features
- Pipeline graph has no MergePreferred, no feedback buffers, no PostProcessShape
- Each feature is one file, one BidiFlow, one unit test suite
- All ~1800 existing tests remain green (after adaptation)
- No performance regression when all features are active

---

## Open Questions

- Should `MiddlewareRequestStage` + `MiddlewareResponseStage` also become a `MiddlewareBidiStage`?
  (They have async processing and N instances — one per registered middleware)
- Should `AutomaticDecompression` be controllable per-request (via `TurboRequestOptions`)
  or is client-level sufficient?
