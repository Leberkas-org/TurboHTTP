# PROGRESS_7.md — Engine Wiring Audit

**Date:** 2026-03-15
**Task:** TASK-AUD-001 — Read and Document the Full Engine Wiring

---

## Engine.cs — Full Wiring Map

`Engine.cs` has two main paths: **Extended Pipeline** (production+test) and **Engine Core** (protocol demux).

---

### Path 1: `BuildExtendedPipeline` (always used)

This wraps every request/response with business logic stages:

**Request chain (top → engine):**

| Stage | Class | Notes |
|-------|-------|-------|
| RequestEnricherStage | `Streams/Stages/RequestEnricherStage.cs` | Applies BaseAddress, DefaultHeaders, DefaultVersion |
| MergePreferred (redirect feedback) | Akka built-in | Accepts re-queued redirect requests |
| CookieInjectionStage | `Streams/Stages/CookieInjectionStage.cs` | Injects cookies from shared CookieJar |
| MergePreferred (retry feedback) | Akka built-in | Accepts re-queued retry requests |
| CacheLookupStage | `Streams/Stages/CacheLookupStage.cs` | Out0=cache miss→engine, Out1=cache hit→merge |

**Engine core** (see below)

**Response chain (engine → output):**

| Stage | Class | Notes |
|-------|-------|-------|
| DecompressionStage | `Streams/Stages/DecompressionStage.cs` | gzip/deflate/brotli |
| CookieStorageStage | `Streams/Stages/CookieStorageStage.cs` | Persists Set-Cookie into CookieJar |
| CacheStorageStage | `Streams/Stages/CacheStorageStage.cs` | Stores cacheable responses |
| RetryStage | `Streams/Stages/RetryStage.cs` | Out0=final, Out1=retry→retryMerge |
| Merge (cache hit + response) | Akka built-in | Merges cached and live responses |
| RedirectStage | `Streams/Stages/RedirectStage.cs` | Out0=final, Out1=redirect→redirectMerge |

---

### Path 2: `BuildEngineCoreGraph` — Test Mode (http10Factory != null)

Partitions into 3 protocol lanes (HTTP/1.0, HTTP/1.1, HTTP/2):

| Lane | Protocol Engine | Builder |
|------|----------------|---------|
| Port 0 | Http10Engine | `BuildProtocolFlow<Http10Engine>(16, ActorRefs.Nobody, factory)` |
| Port 1 | Http11Engine | `BuildProtocolFlow<Http11Engine>(16, ActorRefs.Nobody, factory)` |
| Port 2 | Http20Engine | `BuildProtocolFlow<Http20Engine>(16, ActorRefs.Nobody, factory)` |

Each `BuildProtocolFlow` in test mode: `new TEngine().CreateFlow().Join(transportFactory())`
→ **No ConnectionStage used. Transport comes from the test factory.**

---

### Path 3: `BuildEngineCoreGraph` — Production Mode (http10Factory == null)

Partitions into 4 protocol lanes:

| Lane | Protocol Engine | maxSubstreams | Transport |
|------|----------------|---------------|-----------|
| Port 0 | Http10Engine | 256 | `ConnectionStage(poolRouter)` |
| Port 1 | Http11Engine | 256 | `ConnectionStage(poolRouter)` |
| Port 2 | Http20Engine | 64 | `ConnectionStage(poolRouter)` |
| Port 3 | Http30Engine | 32 | `ConnectionStage(poolRouter)` |

---

### Path 4: `BuildProtocolFlow` — Production substream wiring

Each protocol lane creates per-host substreams using `GroupBy(HostKey.FromRequest)`.

Within each substream (`BuildConnectionFlowPublic`):

| Component | What it does |
|-----------|-------------|
| `TEngine().CreateFlow()` (BidiFlow) | Protocol encoding/decoding |
| `ConnectionStage(poolRouter)` | TCP transport, contacts `PoolRouterActor` for socket refs |
| `Broadcast<HttpRequestMessage>(2)` | Fans out first request to (a) BidiFlow and (b) connectOnce |
| `connectOnce` (Take(1).Select ConnectItem) | Extracts URI on first request → creates `ConnectItem` |
| `Concat<IOutputItem>(2)` | Sends ConnectItem first, then BidiFlow data |
| `Buffer(1, Backpressure)` | Decouples Broadcast from BidiFlow (deadlock prevention) |

---

## Stage Inventory — Wired vs. Unused

| Stage | File | Used in Engine.cs? | Where |
|-------|------|--------------------|-------|
| RequestEnricherStage | Streams/Stages/RequestEnricherStage.cs | ✅ YES | ExtendedPipeline request chain |
| CookieInjectionStage | Streams/Stages/CookieInjectionStage.cs | ✅ YES | ExtendedPipeline request chain |
| CacheLookupStage | Streams/Stages/CacheLookupStage.cs | ✅ YES | ExtendedPipeline request chain |
| DecompressionStage | Streams/Stages/DecompressionStage.cs | ✅ YES | ExtendedPipeline response chain |
| CookieStorageStage | Streams/Stages/CookieStorageStage.cs | ✅ YES | ExtendedPipeline response chain |
| CacheStorageStage | Streams/Stages/CacheStorageStage.cs | ✅ YES | ExtendedPipeline response chain |
| RetryStage | Streams/Stages/RetryStage.cs | ✅ YES | ExtendedPipeline response chain |
| RedirectStage | Streams/Stages/RedirectStage.cs | ✅ YES | ExtendedPipeline response chain |
| ConnectionStage | IO/Stages/ConnectionStage.cs | ✅ YES | BuildConnectionFlowPublic (production) |
| ExtractOptionsStage | Streams/Stages/ExtractOptionsStage.cs | ❌ NO | File exists, not referenced in Engine.cs |
| PrependPrefaceStage | Streams/Stages/PrependPrefaceStage.cs | ❌ NO | Used inside Http20Engine internally |
| GroupByHostKeyStage | Streams/Stages/GroupByHostKeyStage.cs | ❌ NO | Engine uses `.GroupBy(HostKey.FromRequest)` directly |
| MergeSubstreamsStage | Streams/Stages/MergeSubstreamsStage.cs | ❌ NO | Engine uses `.MergeSubstreams()` directly |
| ConnectionReuseStage | Streams/Stages/ConnectionReuseStage.cs | ❌ NO | File exists, never wired in Engine.cs |
| Http10EncoderStage | Streams/Stages/Http10EncoderStage.cs | ✅ YES (indirect) | Used inside Http10Engine.CreateFlow() |
| Http10DecoderStage | Streams/Stages/Http10DecoderStage.cs | ✅ YES (indirect) | Used inside Http10Engine.CreateFlow() |
| Http11EncoderStage | Streams/Stages/Http11EncoderStage.cs | ✅ YES (indirect) | Used inside Http11Engine.CreateFlow() |
| Http11DecoderStage | Streams/Stages/Http11DecoderStage.cs | ✅ YES (indirect) | Used inside Http11Engine.CreateFlow() |
| Http20EncoderStage | Streams/Stages/Http20EncoderStage.cs | ✅ YES (indirect) | Used inside Http20Engine.CreateFlow() |
| Http20DecoderStage | Streams/Stages/Http20DecoderStage.cs | ✅ YES (indirect) | Used inside Http20Engine.CreateFlow() |
| Http20ConnectionStage | Streams/Stages/Http20ConnectionStage.cs | ✅ YES (indirect) | Used inside Http20Engine.CreateFlow() |
| Http20StreamStage | Streams/Stages/Http20StreamStage.cs | ✅ YES (indirect) | Used inside Http20Engine.CreateFlow() |
| StreamIdAllocatorStage | Streams/Stages/StreamIdAllocatorStage.cs | ✅ YES (indirect) | Used inside Http20Engine.CreateFlow() |
| Request2FrameStage | Streams/Stages/Request2FrameStage.cs | ✅ YES (indirect) | Used inside Http20Engine.CreateFlow() |
| Http1XCorrelationStage | Streams/Stages/Http1XCorrelationStage.cs | ✅ YES (indirect) | Used inside Http11Engine.CreateFlow() |
| Http20CorrelationStage | Streams/Stages/Http20CorrelationStage.cs | ✅ YES (indirect) | Used inside Http20Engine.CreateFlow() |

---

## Key Answers

### Does `Engine.cs` use `ConnectionPoolStage`?

**NO.** There is no `ConnectionPoolStage` class anywhere in the codebase (confirmed by grep). The term "Actor Pool" refers to `PoolRouterActor` / `HostPoolActor`, which are actors — not Akka.Streams stages. `ConnectionStage` is the Akka.Streams stage that bridges the actor pool to the stream graph.

### Is `ConnectionReuseStage` actively wired?

**NO.** `ConnectionReuseStage` exists at `Streams/Stages/ConnectionReuseStage.cs` and has tests in `TurboHttp.StreamTests/Streams/ConnectionReuseStageTests.cs`, but it is **never referenced in `Engine.cs`** or any production code path. It is dead code from an earlier design iteration.

---

## Architecture Notes

- **`PoolRouterActor`** IS used in production: passed as `poolRouter: IActorRef` to `BuildProtocolFlow`, which passes it to `ConnectionStage`. So the actor pool IS integrated — but via the `ConnectionStage` bridge, not via a stream stage called "ConnectionPoolStage".
- **`GroupByHostKeyStage`** and **`MergeSubstreamsStage`** exist as custom stages but Engine.cs uses the built-in Akka `.GroupBy()` / `.MergeSubstreams()` DSL extensions directly instead.
- **`ExtractOptionsStage`** exists but is not wired in Engine.cs. Its former role (splitting `HttpRequest(Options, Message)`) may have been superseded by the current `RequestEnricherStage` + `requestOptionsFactory` pattern.
