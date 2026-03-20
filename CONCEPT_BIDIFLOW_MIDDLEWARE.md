# Concept: BidiFlow Middleware Pipeline

## Executive Summary

Replace the current split-stage pattern (separate request-only and response-only `FlowShape` stages
scattered across pre-processing and post-processing islands) with a **composable `BidiFlow` middleware
chain** that lets both built-in and user-supplied handlers see the request on the way out and the
response on the way back — exactly like .NET's `DelegatingHandler`, but with Akka.Streams
backpressure, async boundaries, and `.Atop()` composition.

---

## Motivation

### Problem 1: Logically Coupled Stages Live Far Apart

Today `CookieInjectionStage` (request-side, pre-processing island) and `CookieStorageStage`
(response-side, post-processing island) share a `CookieJar` but live in completely different parts
of the graph. The same applies to `CacheLookupStage` (request-side FanOut) and `CacheStorageStage`
(response-side), which share an `HttpCacheStore`. And `DecompressionStage` only has a response side
today but logically pairs with `Accept-Encoding` injection on the request side.

```
CookieInjectionStage                                     CookieStorageStage
   (pre-processing)          ... 5 stages apart ...       (post-processing)
         │                                                       │
         └──────────── shared CookieJar reference ───────────────┘

CacheLookupStage                                         CacheStorageStage
   (pre-processing)          ... 6 stages apart ...       (post-processing)
         │                                                       │
         └──────────── shared HttpCacheStore reference ──────────┘
```

This makes the code harder to reason about and prevents users from building similar cross-cutting
concerns.

### Problem 2: No User-Extensible Middleware

Users cannot plug custom request/response handlers into the pipeline. Common patterns like
authentication token injection, request/response logging, metrics collection, or custom header
management all require forking the engine graph.

### Problem 3: .NET Developers Expect DelegatingHandler

The `HttpClient` ecosystem trains .NET developers to think in terms of `DelegatingHandler` chains.
TurboHttp should offer an analogous concept built on Akka.Streams primitives, giving users a
familiar mental model with streaming superpowers (backpressure, async, bounded buffers).

---

## Design

### Core Idea

A **middleware handler** is a `BidiFlow` with four ports:

```
              ┌─────────────────────────────┐
              │        Middleware            │
  Inlet1  ──→│  Request transformation      │──→  Outlet1
  (request)   │                             │   (request, modified)
              │                             │
  Outlet2 ←──│  Response transformation     │←──  Inlet2
  (response)  │                             │   (response, from engine)
              └─────────────────────────────┘
```

Type signature:

```csharp
BidiFlow<HttpRequestMessage, HttpRequestMessage,
         HttpResponseMessage, HttpResponseMessage, NotUsed>
```

Multiple handlers compose via `.Atop()`:

```csharp
var pipeline = outerHandler.Atop(innerHandler);
```

`.Atop()` nests handlers like Russian dolls — the outermost handler sees the request **first** and
the response **last**:

```
Request ──→ [Outer.Req] ──→ [Inner.Req] ──→ Engine ──→ [Inner.Res] ──→ [Outer.Res] ──→ Response
```

This matches `DelegatingHandler` semantics exactly.

### Short-Circuit Pattern

Some handlers (most notably caching) can **short-circuit**: they produce a response on the
outbound response port (`Outlet2`) without forwarding the request to the engine via `Outlet1`.
This is implemented as a custom `GraphStage<BidiShape>` where the stage logic tracks whether
a request was a hit or miss and routes accordingly.

```
                    ┌──────────────────────┐
    Request ──→ In1 │   Cache BidiStage    │ Out1 ──→  (miss: forward to engine)
                    │                      │
                    │   HIT? ──→ short-circuit directly to Out2
                    │                      │
   Response ←─ Out2 │                      │ In2  ←──  (miss: engine response comes back)
                    └──────────────────────┘
```

This eliminates the need for FanOut shapes and separate bypass wiring in the graph — the
short-circuit is fully encapsulated inside the BidiFlow.

### Type Alias

To avoid repeating the full generic signature everywhere:

```csharp
namespace TurboHttp.Streams;

/// <summary>
/// A bidirectional HTTP message handler that can transform requests on the way out
/// and responses on the way back. Compose multiple handlers via <c>.Atop()</c>.
/// <para>
/// This is the Akka.Streams equivalent of <see cref="System.Net.Http.DelegatingHandler"/>.
/// </para>
/// </summary>
public static class HttpBidiHandler
{
    /// <summary>The full BidiFlow type for an HTTP middleware handler.</summary>
    public delegate BidiFlow<HttpRequestMessage, HttpRequestMessage,
                             HttpResponseMessage, HttpResponseMessage, NotUsed> Factory();
}
```

> **Note:** C# does not support type aliases for generic types (until `using` aliases in C# 12
> global scope). The static class serves as a namespace for factory methods and documentation.

---

## Stage Classification

### Stages That Become BidiFlow Middleware

These stages today exist as separate `FlowShape` or `FanOutShape` stages on the request and
response sides. They logically pair into request + response halves that share state.

| Current Stages | Side | Shared State | BidiFlow Name | Complexity |
|---|---|---|---|---|
| `CookieInjectionStage` + `CookieStorageStage` | Req + Res | `CookieJar` | `CookieBidiHandler` | Stateless (`BidiFlow.FromFlows`) |
| `DecompressionStage` (+ new `Accept-Encoding` injection) | Res (+ new Req) | none | `DecompressionBidiHandler` | Stateless (`BidiFlow.FromFlows`) |
| `CacheLookupStage` + `CacheStorageStage` | Req (FanOut) + Res | `HttpCacheStore` | `CacheBidiStage` | Stateful (`GraphStage<BidiShape>` with short-circuit) |

#### Why Cache Works as BidiFlow

The current `CacheLookupStage` is a `FanOutShape<Req, Req, Res>` — it has two outlet types because
cache hits produce a `HttpResponseMessage` that bypasses the engine entirely. This led to extra
graph wiring: a separate `CacheHitIn` inlet on `PostProcessShape`, a `CacheMerge` node, and
dedicated wiring between `CacheLookup.Out1` and `PostProcess.CacheHitIn`.

**Key insight:** The FanOut is an implementation choice, not an inherent constraint. A BidiFlow
can achieve the same result via **short-circuiting** inside its `GraphStageLogic`:

- **Cache hit:** The request-side `OnPush` finds a fresh entry → pushes the cached response
  directly on `Outlet2` (response out) → does NOT push on `Outlet1` (request out) → the engine
  never sees the request.
- **Cache miss:** The request-side `OnPush` pushes the (possibly conditional) request on
  `Outlet1` → the engine processes it → the response arrives on `Inlet2` → the response-side
  `OnPush` stores the response if cacheable and pushes it on `Outlet2`.
- **304 Not Modified:** The response-side `OnPush` merges the 304 with the cached entry and
  pushes the resulting 200 on `Outlet2`.

**What this eliminates in `Engine.cs`:**

| Removed Component | Reason |
|---|---|
| `CacheLookup.Out1` (hit outlet) wiring | Short-circuit happens inside BidiFlow |
| `PostProcessShape.CacheHitIn` (second inlet) | No more separate cache hit path |
| `CacheMerge` node in post-processing | Cache hits exit through `CacheBidiStage.Outlet2` |
| `CacheStorageStage` as separate stage | Merged into `CacheBidiStage` response side |

**Why cache hits safely skip Retry and Redirect:**

| Stage | Triggers on | Cached responses | Safe to skip? |
|---|---|---|---|
| `RetryStage` | 408, 503 | Always 2xx (only 2xx are stored by `CacheStorageStage`) | Yes |
| `RedirectStage` | 301, 302, 303, 307, 308 | Always 2xx | Yes |

Cache hits that short-circuit still pass through all **outer** middleware handlers on the response
path (e.g. Cookie storage, user logging). They only skip the **inner** handlers and the engine.

### Stages That CANNOT Become BidiFlow

These stages have feedback loops back into the pipeline or emit internal transport signals. They
require graph-level wiring with `MergePreferred` and cannot be expressed as a bidirectional
pass-through.

| Stage | Shape | Why Not BidiFlow |
|---|---|---|
| `RetryStage` | `FanOut<Res, Res, Req>` | Feedback loop: emits retry requests back into the pipeline via `MergePreferred`. The retry request re-enters the pre-processing island — this cross-island feedback cannot be expressed inside a BidiFlow. |
| `RedirectStage` | `FanOut<Res, Res, Req>` | Feedback loop: emits redirect requests back into the pipeline via `MergePreferred`. Same cross-island constraint as RetryStage. |
| `ConnectionReuseStage` | `FanOut<Res, Res, IOutputItem>` | Internal transport signal to `ConnectionStage` — not a request/response concern. |
| `ExtractOptionsStage` | `FanOut<Req, Req, IOutputItem>` | Internal connect signal for `ConnectionStage` — not a request/response concern. |

### Stages That Stay Internal (Unchanged)

Protocol-internal stages inside the engine `BidiFlow`:

| Stage | Location |
|---|---|
| `Http10EncoderStage`, `Http11EncoderStage`, `Http20EncoderStage` | Inside `Http*Engine.CreateFlow()` |
| `Http10DecoderStage`, `Http11DecoderStage`, `Http20DecoderStage` | Inside `Http*Engine.CreateFlow()` |
| `Http1XCorrelationStage`, `Http20CorrelationStage` | Inside `Http*Engine.CreateFlow()` |
| `Http20ConnectionStage` (custom 5-port) | Inside `Http20Engine.CreateFlow()` |
| `StreamIdAllocatorStage`, `Request2FrameStage`, `PrependPrefaceStage` | Inside `Http20Engine.CreateFlow()` |
| `RequestEnricherStage` | Always first, applies `BaseAddress` / `DefaultVersion` / `DefaultHeaders` |

---

## Pipeline Architecture

### Current Pipeline (Before)

```
 ┌──────────────────────── PRE-PROCESSING (island 1) ─────────────────────────┐
 │                                                                             │
 │  Enricher → RedirectMerge → CookieInject → RetryMerge → CacheLookup       │
 │                  ↑ pref                      ↑ pref       │miss    │hit    │
 └──────────────────│───────────────────────────│────────────│────────│────────┘
                    │                           │            ↓        │
 ┌──────────────────│───────────────────────────│── ENGINE (island 2, async) ──┐
 │                  │                           │                              │
 │                  │              EngineCore → Decompression                   │
 │                  │                                                          │
 └──────────────────│───────────────────────────│──────────────┬───────────────┘
                    │                           │              ↓
 ┌──────────────────│───────────────────────────│── POST-PROC (island 3, async)┐
 │                  │                           │                              │
 │   CookieStorage → CacheStorage → Retry ─────│─→ CacheMerge → Redirect      │
 │                                  │final │retry    ↑hit        │final        │
 │                                  │      └────┘    │           │             │
 │                                  │                │           │             │
 └──────────────────│───────────────│────────────────│───────────│─────────────┘
                    │               │                │           ↓
                    └── redirect ───┘                │        Response
                        feedback                    │
                                                    └─ from CacheLookup.Out1
```

**9 custom stages + complex graph wiring** including separate cache-hit bypass path,
`CacheMerge` node, and 2-inlet `PostProcessShape`.

### New Pipeline (After)

```
 ┌───────────────── PRE-PROCESSING (island 1) ──────────────────────────────┐
 │                                                                           │
 │  Enricher → RedirectMerge ──→ mw.Inlet1                                  │
 │                  ↑ pref                                                   │
 │                           ┌── MIDDLEWARE BidiFlow ──────────────────┐     │
 │                           │                                         │     │
 │                           │  Cookie.Req → Cache.Req → Decomp.Req   │     │
 │                           │      → User.Req                        │     │
 │                           │                                         │     │
 │                           │  Cache HIT? ──→ short-circuit to Out2  │     │
 │                           │                                         │     │
 │                           └─────────────────────────────────────────┘     │
 │                                                                           │
 │              mw.Outlet1 → RetryMerge ──→ Engine                           │
 │                             ↑ pref                                        │
 └─────────────────────────────│─────────────────────────────────────────────┘
                               │     ═══ async boundary ═══
 ┌─────────────────────────────│── ENGINE (island 2) ───────────────────────┐
 │                             │                                            │
 │                             │         EngineCore                          │
 │                             │                                            │
 └─────────────────────────────│─────────────────┬──────────────────────────┘
                               │                 │
                               │     ═══ async boundary ═══
                               │                 ↓
 ┌─────────────────────────────│── POST-PROC (island 3) ───────────────────┐
 │                             │                                            │
 │                             │   ┌── MIDDLEWARE BidiFlow ──────────────┐  │
 │                             │   │                                     │  │
 │                             │   │  User.Res → Decomp.Res             │  │
 │                             │   │      → Cache.Res → Cookie.Res      │  │
 │                             │   │                                     │  │
 │                             │   │  304? ──→ merge with cached entry   │  │
 │                             │   │  2xx? ──→ store in cache            │  │
 │                             │   │                                     │  │
 │                             │   └────────────────────────────────────┘  │
 │                             │                  │                         │
 │                             │    Retry ←── mw.Outlet2                   │
 │                             │    │final  │retry                         │
 │                             │    │       └──┘                           │
 │                             │    ↓                                      │
 │                             └── Redirect                                │
 │                                  │final                                 │
 └──────────────────────────────────│──────────────────────────────────────┘
                                    ↓
                                 Response
```

**Key changes:**

1. **`CookieInjectionStage` + `CookieStorageStage`** → unified `CookieBidiHandler` (stateless,
   `BidiFlow.FromFlows`)
2. **`DecompressionStage`** → `DecompressionBidiHandler` with new request-side `Accept-Encoding`
   injection (stateless, `BidiFlow.FromFlows`)
3. **`CacheLookupStage` + `CacheStorageStage`** → unified `CacheBidiStage` (stateful,
   `GraphStage<BidiShape>` with short-circuit on cache hit). Eliminates `CacheHitIn`,
   `CacheMerge`, and the entire cache-hit bypass wiring.
4. User-supplied handlers slot into the same chain via `.Atop()`
5. **`PostProcessShape` simplified:** 1 inlet (was 2), no `CacheMerge` — just `Retry → Redirect`

---

## Middleware Ordering

The `.Atop()` composition determines which handler sees the request first and the response last.
The built-in handler order is:

```
Cookie.Atop(Cache).Atop(Decomp).Atop(User[0]).Atop(User[1])...
```

This produces:

```
Request path (left to right = first to last):

  Cookie.Req → Cache.Req → Decomp.Req → User[0].Req → User[1].Req → Engine

Response path (left to right = first to last):

  Engine → User[1].Res → User[0].Res → Decomp.Res → Cache.Res → Cookie.Res
```

**Why this order:**

| Position | Handler | Rationale |
|---|---|---|
| 1 (outermost) | Cookie | Must inject cookies before cache lookup (cookies may be Vary key, INV-2). Must store Set-Cookie after decompression and cache storage (INV-3). |
| 2 | Cache | Must see cookies in request. On response side, must see decompressed bodies to store them correctly (INV-6). Cache hits short-circuit here — they skip Decompression and User handlers on response, which is correct because cached bodies are already decompressed. |
| 3 | Decompression | Request: injects `Accept-Encoding`. Response: decompresses body before cache stores it (INV-6). |
| 4+ (innermost) | User handlers | Closest to the engine. See the fully-prepared request and the raw engine response (before decompression). |

**Cache hit data flow:**

```
Request arrives at Cookie.Req → cookies injected
  → arrives at Cache.Req → CACHE HIT
    → response pushed directly to Cache response-out (Outlet2)
      → arrives at Cookie.Res → Set-Cookie stored
        → arrives at Retry → 2xx, no retry needed → pass through
          → arrives at Redirect → 2xx, no redirect needed → pass through
            → Response delivered to client

(Decomp.Req, User.Req, Engine, User.Res, Decomp.Res are all SKIPPED)
```

This is correct because:
- Cached bodies are **already decompressed** (stored after decompression on the original request)
- User handlers don't need to see cache hits (they'll see the original request/response pair)
- Retry/Redirect only act on 4xx/5xx and 3xx respectively — cached 2xx passes through

---

## Invariant Preservation

The current pipeline enforces specific ordering invariants (documented in `Engine.cs`). The new
wiring must preserve all of them.

| ID | Invariant | Current | New | Status |
|---|---|---|---|---|
| INV-1 | `ConnectionReuseStage` runs inside substream | Inside `BuildConnectionFlowPublic` | Unchanged | Preserved |
| INV-2 | Cookie before cache lookup (cookies may be Vary key) | `CookieInject` → `CacheLookup` | Cookie is outermost, Cache is next: `Cookie.Req` → `Cache.Req` | Preserved |
| INV-3 | Cookie storage before cache storage | `CookieStorage` → `CacheStorage` | Cache.Res → Cookie.Res (response flows outward: inner first, outer last). Cache stores on response-side, Cookie stores after cache. | Preserved |
| INV-4 | Cache storage before RetryStage | `CacheStorage` → `Retry` | `Cache.Res` (inside middleware) → `mw.Outlet2` → `Retry` | Preserved |
| INV-5 | Retry before Redirect | `Retry` → `Redirect` | Unchanged | Preserved |
| INV-6 | Decompression before cache storage | `Decomp` → `CookieStorage` → `CacheStorage` | Response flows inward-to-outward: `Decomp.Res` → `Cache.Res` (cache stores decompressed body) | Preserved |
| INV-7 | Redirect feedback gets fresh cookies | `RedirectMerge` → `CookieInject` | `RedirectMerge` → `mw.Inlet1` → `Cookie.Req` (outermost, runs first) | Preserved |
| INV-8 | Retry feedback reuses same cookies | `CookieInject` → `RetryMerge` | `mw.Outlet1` → `RetryMerge` (retry enters after middleware, skips cookie re-injection) | Preserved |
| INV-9 | Redirect feedback skips re-enrichment | `RedirectMerge` after `Enricher` | Unchanged | Preserved |
| INV-10 | Cache hits bypass engine entirely | `CacheLookup.Hit` → `PostProcess.CacheHitIn` via dedicated wiring | `CacheBidiStage` short-circuits internally — hit never reaches `Outlet1` (engine) | Preserved |

---

## Graph Wiring (Engine.cs)

### `BuildExtendedPipeline` — Main Graph

```csharp
private static Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> BuildExtendedPipeline(
    IActorRef poolRouter,
    TurboClientOptions options,
    Func<TurboRequestOptions> requestOptionsFactory,
    Func<Flow<IOutputItem, IInputItem, NotUsed>>? http10Factory = null,
    Func<Flow<IOutputItem, IInputItem, NotUsed>>? http11Factory = null,
    Func<Flow<IOutputItem, IInputItem, NotUsed>>? http20Factory = null)
{
    var cookieJar = new CookieJar();
    var cacheStore = new HttpCacheStore(options.CachePolicy);
    var middleware = BuildMiddlewareChain(cookieJar, cacheStore, options);

    return Flow.FromGraph(GraphDsl.Create(builder =>
    {
        // ---- PRE-PROCESSING ----

        var enricher = builder.Add(new RequestEnricherStage(requestOptionsFactory));
        var redirectMerge = builder.Add(new MergePreferred<HttpRequestMessage>(1));
        var retryMerge = builder.Add(new MergePreferred<HttpRequestMessage>(1));

        // ---- MIDDLEWARE BidiFlow (4 ports wired independently) ----
        //
        //  Contains: Cookie → Cache → Decompression → User handlers
        //  Cache hit short-circuits inside the BidiFlow (no external bypass wiring needed).
        //

        var mw = builder.Add(middleware);

        // ---- ENGINE (async boundary) ----

        var engine = builder.Add(
            Flow.FromGraph(
                    BuildEngineCoreGraph(poolRouter, options,
                        http10Factory, http11Factory, http20Factory))
                .WithAttributes(Attributes.CreateAsyncBoundary()));

        // ---- POST-PROCESSING (async boundary) ----
        //
        //  Simplified: just Retry → Redirect (no CacheStorage, CookieStorage, CacheMerge).
        //

        var postProcess = builder.Add(
            BuildPostProcessGraph(options).Async());

        // ===== REQUEST PATH =====
        //
        //  Enricher → RedirectMerge ──→ mw.Inlet1
        //                                   │
        //      [Cookie.Req → Cache.Req → Decomp.Req → User.Req]
        //          (cache hit? → short-circuit to mw.Outlet2)
        //                                   │
        //                             mw.Outlet1 ──→ RetryMerge → Engine
        //

        builder.From(enricher.Outlet).To(redirectMerge.In(0));
        builder.From(redirectMerge.Out).To(mw.Inlet1);
        builder.From(mw.Outlet1).To(retryMerge.In(0));
        builder.From(retryMerge.Out).To(engine.Inlet);

        // ===== RESPONSE PATH =====
        //
        //  Engine ──→ mw.Inlet2
        //                │
        //   [User.Res → Decomp.Res → Cache.Res → Cookie.Res]
        //       (304? → merge with cached entry)
        //       (2xx? → store in cache)
        //                │
        //          mw.Outlet2 ──→ PostProcess (Retry → Redirect)
        //
        //  Cache hits also arrive here via short-circuit from mw request side.
        //

        builder.From(engine.Outlet).To(mw.Inlet2);
        builder.From(mw.Outlet2).To(postProcess.ResponseIn);

        // ===== FEEDBACK LOOPS =====
        //
        //  RetryFeedback    → RetryMerge.Preferred    (enters AFTER middleware — INV-8)
        //  RedirectFeedback → RedirectMerge.Preferred  (enters BEFORE middleware — INV-7)
        //

        builder.From(postProcess.RetryFeedbackOut)
            .Via(Flow.Create<HttpRequestMessage>().Buffer(4, OverflowStrategy.Backpressure))
            .To(retryMerge.Preferred);

        builder.From(postProcess.RedirectFeedbackOut)
            .Via(Flow.Create<HttpRequestMessage>().Buffer(4, OverflowStrategy.Backpressure))
            .To(redirectMerge.Preferred);

        return new FlowShape<HttpRequestMessage, HttpResponseMessage>(
            enricher.Inlet,
            postProcess.ResponseOut);
    }));
}
```

### `BuildMiddlewareChain` — Compose Built-in + User Handlers

```csharp
/// <summary>
/// Composes the middleware BidiFlow chain: built-in handlers (cookie, cache, decompression)
/// followed by user-supplied handlers from <see cref="TurboClientOptions.MessageHandlers"/>.
/// <para>
/// Atop order (outermost first):
///   Request:  Cookie.Req → Cache.Req → Decomp.Req → User[0].Req → User[1].Req → Engine
///   Response: Engine → User[1].Res → User[0].Res → Decomp.Res → Cache.Res → Cookie.Res
/// </para>
/// </summary>
private static BidiFlow<HttpRequestMessage, HttpRequestMessage,
                         HttpResponseMessage, HttpResponseMessage, NotUsed>
    BuildMiddlewareChain(CookieJar cookieJar, HttpCacheStore cacheStore, TurboClientOptions options)
{
    var chain = BidiFlow.FromFlows(
        Flow.Identity<HttpRequestMessage>(),
        Flow.Identity<HttpResponseMessage>());

    // Cookie is outermost: sees request first (inject cookies before cache lookup),
    // sees response last (store Set-Cookie after cache storage).
    chain = TurboHttpHandlers.Cookie(cookieJar).Atop(chain);

    // Cache: check store on request side (short-circuit on hit),
    // store/revalidate on response side.
    chain = chain.Atop(TurboHttpHandlers.Cache(cacheStore, options.CachePolicy));

    // Decompression: inject Accept-Encoding on request,
    // decompress body on response (before cache stores it).
    chain = chain.Atop(TurboHttpHandlers.Decompression());

    // User-supplied handlers (innermost — closest to the engine).
    foreach (var handler in options.MessageHandlers)
    {
        chain = chain.Atop(handler);
    }

    return chain;
}
```

### `BuildPostProcessGraph` — Simplified

```csharp
/// <summary>
/// Post-processing sub-graph: just Retry → Redirect.
/// <para>
/// All cross-cutting concerns (cookie, cache, decompression) have moved into the
/// middleware BidiFlow. This island only evaluates retry and redirect decisions.
/// </para>
/// </summary>
private static IGraph<PostProcessShape, NotUsed> BuildPostProcessGraph(
    TurboClientOptions options)
{
    return GraphDsl.Create(builder =>
    {
        var retry = builder.Add(new RetryStage(options.RetryPolicy));
        var redirect = builder.Add(new RedirectStage(options.RedirectPolicy));

        builder.From(retry.Out0).To(redirect.In);

        return new PostProcessShape(
            retry.In,           // response input (from middleware)
            redirect.Out0,      // final response output
            retry.Out1,         // retry feedback
            redirect.Out1);     // redirect feedback
    });
}

/// <summary>
/// Simplified shape: 1 inlet (response), 3 outlets (final, retry feedback, redirect feedback).
/// The second inlet (CacheHitIn) and CacheMerge are no longer needed — cache hits short-circuit
/// inside the middleware BidiFlow.
/// </summary>
private sealed class PostProcessShape : Shape
{
    public Inlet<HttpResponseMessage> ResponseIn { get; }
    public Outlet<HttpResponseMessage> ResponseOut { get; }
    public Outlet<HttpRequestMessage> RetryFeedbackOut { get; }
    public Outlet<HttpRequestMessage> RedirectFeedbackOut { get; }

    public PostProcessShape(
        Inlet<HttpResponseMessage> responseIn,
        Outlet<HttpResponseMessage> responseOut,
        Outlet<HttpRequestMessage> retryFeedbackOut,
        Outlet<HttpRequestMessage> redirectFeedbackOut)
    {
        ResponseIn = responseIn;
        ResponseOut = responseOut;
        RetryFeedbackOut = retryFeedbackOut;
        RedirectFeedbackOut = redirectFeedbackOut;
    }

    public override ImmutableArray<Inlet> Inlets => [ResponseIn];
    public override ImmutableArray<Outlet> Outlets => [ResponseOut, RetryFeedbackOut, RedirectFeedbackOut];

    public override Shape DeepCopy() => new PostProcessShape(
        (Inlet<HttpResponseMessage>)ResponseIn.CarbonCopy(),
        (Outlet<HttpResponseMessage>)ResponseOut.CarbonCopy(),
        (Outlet<HttpRequestMessage>)RetryFeedbackOut.CarbonCopy(),
        (Outlet<HttpRequestMessage>)RedirectFeedbackOut.CarbonCopy());

    public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
        => new PostProcessShape(
            (Inlet<HttpResponseMessage>)inlets[0],
            (Outlet<HttpResponseMessage>)outlets[0],
            (Outlet<HttpRequestMessage>)outlets[1],
            (Outlet<HttpRequestMessage>)outlets[2]);
}
```

---

## Built-in Handlers

### `TurboHttpHandlers.Cookie`

Replaces `CookieInjectionStage` + `CookieStorageStage`. Stateless — built from two independent
flows via `BidiFlow.FromFlows`:

```csharp
public static class TurboHttpHandlers
{
    /// <summary>
    /// RFC 6265 — Injects cookies into outgoing requests and stores Set-Cookie
    /// headers from incoming responses. Both sides share the same <see cref="CookieJar"/>.
    /// </summary>
    public static BidiFlow<HttpRequestMessage, HttpRequestMessage,
                           HttpResponseMessage, HttpResponseMessage, NotUsed>
        Cookie(CookieJar jar)
    {
        var requestFlow = Flow.Create<HttpRequestMessage>()
            .Select(req =>
            {
                if (req.RequestUri is not null)
                {
                    jar.AddCookiesToRequest(req.RequestUri, ref req);
                }
                return req;
            });

        var responseFlow = Flow.Create<HttpResponseMessage>()
            .Select(res =>
            {
                if (res.RequestMessage?.RequestUri is not null)
                {
                    jar.ProcessResponse(res.RequestMessage.RequestUri, res);
                }
                return res;
            });

        return BidiFlow.FromFlows(requestFlow, responseFlow);
    }
}
```

### `TurboHttpHandlers.Decompression`

Replaces `DecompressionStage` and adds the currently missing request-side `Accept-Encoding`
injection. Stateless:

```csharp
/// <summary>
/// RFC 9110 §8.4 — Injects Accept-Encoding on outgoing requests and decompresses
/// Content-Encoding (gzip, deflate, br) on incoming responses.
/// </summary>
public static BidiFlow<HttpRequestMessage, HttpRequestMessage,
                       HttpResponseMessage, HttpResponseMessage, NotUsed>
    Decompression()
{
    var requestFlow = Flow.Create<HttpRequestMessage>()
        .Select(req =>
        {
            if (!req.Headers.Contains("Accept-Encoding"))
            {
                req.Headers.TryAddWithoutValidation(
                    "Accept-Encoding", "gzip, deflate, br");
            }
            return req;
        });

    var responseFlow = Flow.Create<HttpResponseMessage>()
        .Select(ContentEncodingDecoder.DecompressResponse);

    return BidiFlow.FromFlows(requestFlow, responseFlow);
}
```

### `TurboHttpHandlers.Cache` — Stateful BidiStage

Replaces `CacheLookupStage` + `CacheStorageStage`. This is the most complex built-in handler
because it requires **shared mutable state** between the request and response sides and implements
the **short-circuit pattern** for cache hits.

```csharp
/// <summary>
/// RFC 9111 — Cache lookup on request side (with short-circuit on hit),
/// cache storage + 304 revalidation on response side.
/// </summary>
public static BidiFlow<HttpRequestMessage, HttpRequestMessage,
                       HttpResponseMessage, HttpResponseMessage, NotUsed>
    Cache(HttpCacheStore store, CachePolicy? policy = null)
{
    return BidiFlow.FromGraph(new CacheBidiStage(store, policy ?? CachePolicy.Default));
}
```

#### `CacheBidiStage` Implementation

```csharp
/// <summary>
/// A stateful BidiFlow stage that implements RFC 9111 caching with short-circuit:
/// <list type="bullet">
///   <item><b>Cache hit (Fresh/Stale):</b> Pushes cached response directly on Outlet2
///         without forwarding the request to Outlet1. The engine never sees the request.</item>
///   <item><b>Cache miss:</b> Forwards request on Outlet1, receives response on Inlet2,
///         stores cacheable 2xx responses, forwards all responses on Outlet2.</item>
///   <item><b>MustRevalidate:</b> Builds conditional request (If-None-Match / If-Modified-Since),
///         forwards on Outlet1. On 304 response, merges with cached entry.</item>
/// </list>
/// </summary>
internal sealed class CacheBidiStage
    : GraphStage<BidiShape<HttpRequestMessage, HttpRequestMessage,
                           HttpResponseMessage, HttpResponseMessage>>
{
    private readonly HttpCacheStore _store;
    private readonly CachePolicy _policy;

    private readonly Inlet<HttpRequestMessage> _inRequest = new("Cache.In.Request");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("Cache.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("Cache.In.Response");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Cache.Out.Response");

    public override BidiShape<HttpRequestMessage, HttpRequestMessage,
                              HttpResponseMessage, HttpResponseMessage> Shape { get; }

    public CacheBidiStage(HttpCacheStore store, CachePolicy policy)
    {
        _store = store;
        _policy = policy;
        Shape = new BidiShape<HttpRequestMessage, HttpRequestMessage,
                              HttpResponseMessage, HttpResponseMessage>(
            _inRequest, _outRequest, _inResponse, _outResponse);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly CacheBidiStage _stage;
        private Action<(HttpResponseMessage response, byte[] body)>? _onBodyRead;

        /// <summary>
        /// Tracks whether the last request was a cache hit (short-circuited) or miss.
        /// When true, the next pull on Outlet1 (request out) should pull Inlet1 for
        /// the next request instead of waiting for a response on Inlet2.
        /// </summary>
        private bool _lastWasHit;

        /// <summary>
        /// Tracks whether Outlet2 (response out) has downstream demand. Required because
        /// both request-side (cache hit) and response-side (engine response) can push here.
        /// </summary>
        private bool _responseDemand;

        public Logic(CacheBidiStage stage) : base(stage.Shape)
        {
            _stage = stage;

            // ---- REQUEST SIDE (Inlet1 → Outlet1, or short-circuit to Outlet2) ----

            SetHandler(stage._inRequest, onPush: () =>
            {
                var request = Grab(stage._inRequest);
                var entry = _stage._store.Get(request);
                var result = CacheFreshnessEvaluator.Evaluate(
                    entry, request, DateTimeOffset.UtcNow, _stage._policy);

                switch (result.Status)
                {
                    case CacheLookupStatus.Fresh:
                    case CacheLookupStatus.Stale:
                        // SHORT-CIRCUIT: push cached response on Outlet2
                        _lastWasHit = true;
                        _responseDemand = false;
                        Push(stage._outResponse, result.Entry!.Response);
                        break;

                    case CacheLookupStatus.MustRevalidate when result.Entry is not null:
                        // Conditional request → forward to engine
                        _lastWasHit = false;
                        var conditional = CacheValidationRequestBuilder
                            .BuildConditionalRequest(request, result.Entry);
                        Push(stage._outRequest, conditional);
                        break;

                    default:
                        // Miss → forward original request to engine
                        _lastWasHit = false;
                        Push(stage._outRequest, request);
                        break;
                }
            });

            // Outlet1 demand (request out → engine wants next request)
            SetHandler(stage._outRequest, onPull: () =>
            {
                // If last request was a hit, engine never consumed from Outlet1.
                // We still need to pull the next request from upstream.
                if (!HasBeenPulled(stage._inRequest) && !IsClosed(stage._inRequest))
                {
                    Pull(stage._inRequest);
                }
            });

            // ---- RESPONSE SIDE (Inlet2 → Outlet2) ----

            SetHandler(stage._inResponse, onPush: () =>
            {
                var response = Grab(stage._inResponse);
                var request = response.RequestMessage;

                if (request is null)
                {
                    _responseDemand = false;
                    Push(stage._outResponse, response);
                    return;
                }

                // 304 Not Modified → merge with cached entry
                if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
                {
                    var cached = _stage._store.Get(request);
                    if (cached is not null)
                    {
                        var merged = CacheValidationRequestBuilder
                            .MergeNotModifiedResponse(response, cached);
                        merged.RequestMessage = request;

                        var now = DateTimeOffset.UtcNow;
                        _stage._store.Put(request, merged, cached.Body, now, now);

                        _responseDemand = false;
                        Push(stage._outResponse, merged);
                        return;
                    }
                }

                // Unsafe method → invalidate
                var method = request.Method;
                if (method == HttpMethod.Post || method == HttpMethod.Put
                    || method == HttpMethod.Delete || method == HttpMethod.Patch)
                {
                    if (request.RequestUri is not null)
                    {
                        _stage._store.Invalidate(request.RequestUri);
                    }
                }
                // 2xx → store if cacheable
                else if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 300)
                {
                    var task = response.Content.ReadAsByteArrayAsync();
                    if (task.IsCompletedSuccessfully)
                    {
                        var body = task.Result;
                        var now = DateTimeOffset.UtcNow;
                        _stage._store.Put(request, response, body, now, now);
                    }
                    else
                    {
                        // Async fallback
                        var callback = _onBodyRead!;
                        var capturedResponse = response;
                        task.ContinueWith(t =>
                        {
                            callback((capturedResponse, t.Result));
                        }, TaskContinuationOptions.ExecuteSynchronously);
                    }
                }

                _responseDemand = false;
                Push(stage._outResponse, response);
            });

            // Outlet2 demand (response out → downstream wants next response)
            SetHandler(stage._outResponse, onPull: () =>
            {
                _responseDemand = true;

                if (_lastWasHit)
                {
                    // Last was a cache hit — pull next request (no engine response expected)
                    if (!HasBeenPulled(stage._inRequest) && !IsClosed(stage._inRequest))
                    {
                        Pull(stage._inRequest);
                    }
                }
                else
                {
                    // Last was a miss — pull response from engine
                    if (!HasBeenPulled(stage._inResponse) && !IsClosed(stage._inResponse))
                    {
                        Pull(stage._inResponse);
                    }
                }
            });
        }

        public override void PreStart()
        {
            _onBodyRead = GetAsyncCallback<(HttpResponseMessage response, byte[] body)>(tuple =>
            {
                var (response, body) = tuple;
                var now = DateTimeOffset.UtcNow;
                _stage._store.Put(response.RequestMessage!, response, body, now, now);
            });
        }
    }
}
```

#### Short-Circuit Demand Management

The trickiest aspect of `CacheBidiStage` is demand management across the four ports. The stage
must handle two fundamentally different flows:

**Cache miss (normal BidiFlow behavior):**
```
Inlet1 (request)  ──push──→  Outlet1 (to engine)
Inlet2 (response) ──push──→  Outlet2 (to downstream)

Demand: Outlet1 pull → pull Inlet1
        Outlet2 pull → pull Inlet2
```

**Cache hit (short-circuit):**
```
Inlet1 (request)  ──push──→  Outlet2 (response, directly!)
                              Outlet1 is NOT pushed

Demand: Outlet2 pull → pull Inlet1 (skip Inlet2 entirely)
        Outlet1 pull → pull Inlet1 (engine asks for work but we already served it)
```

The `_lastWasHit` flag tracks which mode the stage is in. When downstream pulls on `Outlet2`:
- If `_lastWasHit == true`: pull next request from `Inlet1` (prepare for next lookup)
- If `_lastWasHit == false`: pull response from `Inlet2` (engine is processing)

When the engine pulls on `Outlet1` after a cache hit, we pull the next request from `Inlet1`
regardless — the engine didn't get a request last time, so it's ready for the next one.

---

## User-Facing API

### `TurboClientOptions` Extension

```csharp
public record TurboClientOptions
{
    // ... existing properties ...

    /// <summary>
    /// User-supplied middleware handlers, composed via <c>.Atop()</c> in declaration order.
    /// The first handler in the list is closest to the engine (sees the request last,
    /// sees the response first). Handlers are applied after the built-in cookie, cache,
    /// and decompression handlers.
    /// <para>
    /// This is the Akka.Streams equivalent of <see cref="HttpClientHandler"/> /
    /// <see cref="DelegatingHandler"/> chains in <see cref="HttpClient"/>.
    /// </para>
    /// </summary>
    public IReadOnlyList<BidiFlow<HttpRequestMessage, HttpRequestMessage,
        HttpResponseMessage, HttpResponseMessage, NotUsed>> MessageHandlers { get; init; }
        = [];
}
```

### `TurboHttpMiddleware` Factory

Convenience methods for creating handlers without writing raw `BidiFlow` code:

```csharp
namespace TurboHttp.Streams;

/// <summary>
/// Factory for creating HTTP middleware handlers as <see cref="BidiFlow{TIn1,TOut1,TIn2,TOut2,TMat}"/>.
/// </summary>
public static class TurboHttpMiddleware
{
    /// <summary>
    /// Creates a middleware handler from separate request and response transformations.
    /// Either side can be null to pass through unchanged.
    /// </summary>
    public static BidiFlow<HttpRequestMessage, HttpRequestMessage,
                           HttpResponseMessage, HttpResponseMessage, NotUsed>
        Create(
            Flow<HttpRequestMessage, HttpRequestMessage, NotUsed>? requestHandler = null,
            Flow<HttpResponseMessage, HttpResponseMessage, NotUsed>? responseHandler = null)
    {
        return BidiFlow.FromFlows(
            requestHandler  ?? Flow.Identity<HttpRequestMessage>(),
            responseHandler ?? Flow.Identity<HttpResponseMessage>());
    }

    /// <summary>
    /// Creates a middleware handler from simple mapping functions.
    /// Either side can be null to pass through unchanged.
    /// </summary>
    public static BidiFlow<HttpRequestMessage, HttpRequestMessage,
                           HttpResponseMessage, HttpResponseMessage, NotUsed>
        Create(
            Func<HttpRequestMessage, HttpRequestMessage>? onRequest = null,
            Func<HttpResponseMessage, HttpResponseMessage>? onResponse = null)
    {
        return BidiFlow.FromFlows(
            onRequest is not null
                ? Flow.Create<HttpRequestMessage>().Select(onRequest)
                : Flow.Identity<HttpRequestMessage>(),
            onResponse is not null
                ? Flow.Create<HttpResponseMessage>().Select(onResponse)
                : Flow.Identity<HttpResponseMessage>());
    }
}
```

---

## Usage Examples

### Logging Handler

```csharp
var logging = TurboHttpMiddleware.Create(
    onRequest: req =>
    {
        logger.LogInformation("→ {Method} {Uri}", req.Method, req.RequestUri);
        return req;
    },
    onResponse: res =>
    {
        logger.LogInformation("← {StatusCode} for {Uri}",
            res.StatusCode, res.RequestMessage?.RequestUri);
        return res;
    });
```

### Bearer Token Authentication

```csharp
var auth = TurboHttpMiddleware.Create(
    onRequest: req =>
    {
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenProvider.GetToken());
        return req;
    });
// Response side: null → pass-through (no response transformation needed)
```

### Request Timing / Metrics (Stateful — Custom GraphStage)

For handlers that need shared mutable state between request and response (e.g. a stopwatch started
on request, stopped on response), users implement a custom `GraphStage<BidiShape<...>>`:

```csharp
public sealed class TimingBidiStage
    : GraphStage<BidiShape<HttpRequestMessage, HttpRequestMessage,
                           HttpResponseMessage, HttpResponseMessage>>
{
    // Inlet/Outlet definitions following naming convention:
    //   "Timing.In.Request", "Timing.Out.Request",
    //   "Timing.In.Response", "Timing.Out.Response"

    private sealed class Logic : GraphStageLogic
    {
        private readonly ConcurrentDictionary<string, long> _timestamps = new();

        // OnPush(requestInlet):  record timestamp, push to requestOutlet
        // OnPush(responseInlet): compute elapsed, record metric, push to responseOutlet
    }
}
```

### Composing Everything

```csharp
var options = new TurboClientOptions
{
    RedirectPolicy = RedirectPolicy.Default,
    RetryPolicy = RetryPolicy.Default,
    CachePolicy = CachePolicy.Default,
    MessageHandlers =
    [
        logging,    // outermost user handler
        auth,       // inner
        metrics     // innermost (closest to engine)
    ]
};

var client = factory.CreateClient("my-api");
```

Resulting pipeline order:

```
Request:  Enricher → RedirectMerge
             → Cookie.Req → Cache.Req → Decomp.Req
                → logging.Req → auth.Req → metrics.Req
                   → RetryMerge → Engine

Response: Engine
             → metrics.Res → auth.Res → logging.Res
                → Decomp.Res → Cache.Res → Cookie.Res
                   → Retry → Redirect → Client

Cache hit (short-circuit at Cache.Req):

Request:  Enricher → RedirectMerge → Cookie.Req → Cache.Req ──╮
                                                     HIT!       │
Response:                            Cookie.Res ← Cache ←──────╯
             → Retry → Redirect → Client
```

---

## Async Boundary Placement

```
┌─ Island 1 (fused) ──────────────────────────────────────────────────┐
│  Enricher → RedirectMerge → [mw.Req] → RetryMerge                  │
└──────────────────────────────────────────────│───────────────────────┘
                                      ═══ async boundary ═══
┌─ Island 2 (fused) ──────────────────────────│───────────────────────┐
│                                       EngineCore                     │
└──────────────────────────────────────────────│───────────────────────┘
                                      ═══ async boundary ═══
┌─ Island 3 (fused) ──────────────────────────│───────────────────────┐
│              [mw.Res] → Retry → Redirect                             │
└──────────────────────────────────────────────────────────────────────┘
```

> **Decision:** The middleware response-half runs in island 3 (post-processing). This keeps
> decompression and cache storage fused with retry/redirect, preserving the current threading model.

---

## Files Changed

### Deleted

| File | Reason |
|---|---|
| `Streams/Stages/CookieInjectionStage.cs` | Replaced by `TurboHttpHandlers.Cookie()` |
| `Streams/Stages/CookieStorageStage.cs` | Replaced by `TurboHttpHandlers.Cookie()` |
| `Streams/Stages/DecompressionStage.cs` | Replaced by `TurboHttpHandlers.Decompression()` |
| `Streams/Stages/CacheLookupStage.cs` | Replaced by `CacheBidiStage` inside `TurboHttpHandlers.Cache()` |
| `Streams/Stages/CacheStorageStage.cs` | Merged into `CacheBidiStage` response side |

### New

| File | Purpose |
|---|---|
| `Streams/TurboHttpHandlers.cs` | Built-in BidiFlow handlers (Cookie, Cache, Decompression) |
| `Streams/TurboHttpMiddleware.cs` | Factory methods for creating user handlers |
| `Streams/Stages/CacheBidiStage.cs` | Stateful cache BidiFlow with short-circuit (internal) |

### Modified

| File | Change |
|---|---|
| `Streams/Engine.cs` | New `BuildMiddlewareChain`, updated `BuildExtendedPipeline` wiring, simplified `BuildPostProcessGraph` (1 inlet, no CacheMerge), simplified `PostProcessShape` |
| `Client/TurboClientOptions.cs` | Add `MessageHandlers` property |

### Test Impact

| Test File | Change |
|---|---|
| `RFC6265/CookieInjectionStageTests` | Rewrite to test `TurboHttpHandlers.Cookie()` BidiFlow |
| `RFC6265/CookieStorageStageTests` | Merge into cookie BidiFlow tests |
| `RFC9110/DecompressionStageTests` | Rewrite to test `TurboHttpHandlers.Decompression()` BidiFlow |
| `RFC9111/CacheLookupStageTests` | Rewrite to test `CacheBidiStage` (hit, miss, revalidation, short-circuit) |
| `RFC9111/CacheStorageStageTests` | Merge into `CacheBidiStage` tests (304 merge, 2xx storage, invalidation) |
| `Streams/StageOrderingTests` | Update stage count and ordering expectations |
| `Streams/EnginePipelineWiringTests` | Update wiring assertions (no CacheMerge, no CacheHitIn) |
| New: `Streams/MiddlewareCompositionTests` | Test `.Atop()` composition, user handlers, ordering |
| New: `Streams/MiddlewareInvariantTests` | Verify INV-2 through INV-10 with new wiring |
| New: `Streams/CacheShortCircuitTests` | Verify cache hit demand management, no engine interaction |

---

## Migration Path

### Phase 1: Infrastructure (non-breaking)

1. Create `TurboHttpHandlers.cs` with `Cookie()` and `Decompression()` as BidiFlow factories
2. Create `CacheBidiStage.cs` with the stateful cache handler
3. Add `TurboHttpHandlers.Cache()` factory method
4. Create `TurboHttpMiddleware.cs` with convenience `Create()` methods
5. Add `MessageHandlers` to `TurboClientOptions` (default empty — no behavior change)
6. Add `BuildMiddlewareChain()` to `Engine.cs`

### Phase 2: Rewire Engine.cs

1. Update `BuildExtendedPipeline` to use the middleware BidiFlow (4-port wiring)
2. Simplify `BuildPostProcessGraph` (remove CacheStorageStage, CookieStorageStage, CacheMerge)
3. Simplify `PostProcessShape` (1 inlet instead of 2)
4. Remove CacheLookup from pre-processing island
5. Verify all invariants hold (INV-1 through INV-10)

### Phase 3: Cleanup

1. Mark `CookieInjectionStage`, `CookieStorageStage`, `DecompressionStage`, `CacheLookupStage`,
   `CacheStorageStage` as `[Obsolete]`
2. Migrate all tests to use BidiFlow-based handlers
3. Delete obsolete stage files

### Phase 4: Documentation + Examples

1. Document the middleware API in the docs site
2. Add usage examples (logging, auth, metrics, custom GraphStage)
3. Update architecture diagrams (pipeline before/after)

---

## Open Questions

1. **Async handlers:** Should `TurboHttpMiddleware.Create()` accept `SelectAsync` / `Flow.SelectAsync`
   for handlers that need async operations (e.g. fetching a token from a vault)?

2. **Error propagation:** If a user handler throws in `Select()`, the stream fails. Should we wrap
   user handlers in a `Recover` stage, or document that handlers must not throw?

3. **Ordering documentation:** `.Atop()` ordering is counter-intuitive for developers used to
   `DelegatingHandler` (where the last added handler runs first). Should we reverse the list in
   `BuildMiddlewareChain` to match the .NET mental model?

4. **Per-request context:** Should we provide a typed context object (like `HttpMessageInvoker`) that
   flows through the BidiFlow, carrying cancellation tokens, tracing IDs, etc.?

5. **Stateful handlers:** `BidiFlow.FromFlows()` creates stateless handlers. For stateful handlers
   (timing, circuit breaker), users must implement `GraphStage<BidiShape>`. Should we provide a
   base class `HttpBidiStage` with pre-wired inlet/outlet naming?

6. **Cache hit visibility:** With the short-circuit pattern, user-supplied middleware handlers
   (innermost) do NOT see cache hits — the hit is produced at the Cache layer before reaching
   User handlers. This matches the behavior of most HTTP client middleware (e.g. `HttpClient`
   caching handlers), but should we offer an option to let user handlers observe cache hits too?
