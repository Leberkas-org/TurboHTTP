# Engine Stream Graph — `BuildExtendedPipeline`

This diagram shows the complete Akka.Streams graph constructed by `Engine.BuildExtendedPipeline()` in
[`src/TurboHttp/Streams/Engine.cs`](../src/TurboHttp/Streams/Engine.cs).
It covers the full request/response data flow including feedback loops for retries and redirects,
cache fan-out/fan-in, and the four-way HTTP version router.

> **Reading guide:** Rounded boxes are `GraphStage` implementations. Diamond shapes are fan-out/fan-in
> junctions. Dashed arrows represent feedback loops. Data types are annotated on key edges.

```mermaid
flowchart TD
    %% ================================================================
    %% REQUEST CHAIN
    %% ================================================================

    IN(["⬇ HttpRequestMessage"]):::io

    enricher(["RequestEnricherStage"]):::stage

    redirectMerge{{"MergePreferred\n(redirect)"}}:::junction
    cookieInject(["CookieInjectionStage"]):::stage
    retryMerge{{"MergePreferred\n(retry)"}}:::junction
    cacheLookup(["CacheLookupStage"]):::stage

    IN -->|"HttpRequestMessage"| enricher
    enricher -->|"HttpRequestMessage"| redirectMerge
    redirectMerge -->|"out"| cookieInject
    cookieInject -->|"HttpRequestMessage"| retryMerge
    retryMerge -->|"out"| cacheLookup

    %% ================================================================
    %% ENGINE CORE  (BuildEngineCoreGraph)
    %% ================================================================

    subgraph EngineCore["Engine Core — BuildEngineCoreGraph"]
        direction TB
        partition{{"Partition\n(by HTTP version)"}}:::junction

        subgraph H10["HTTP/1.0 — GroupByHostKey"]
            http10(["Http10Engine"]):::engine
        end
        subgraph H11["HTTP/1.1 — GroupByHostKey"]
            http11(["Http11Engine"]):::engine
        end
        subgraph H20["HTTP/2 — GroupByHostKey"]
            http20(["Http20Engine"]):::engine
        end
        subgraph H30["HTTP/3 — GroupByHostKey"]
            http30(["Http30Engine"]):::engine
        end

        hub{{"Merge(4)"}}:::junction

        partition -->|"Out(0) — 1.0"| http10
        partition -->|"Out(1) — 1.1"| http11
        partition -->|"Out(2) — 2.0"| http20
        partition -->|"Out(3) — 3.0"| http30

        http10 --> hub
        http11 --> hub
        http20 --> hub
        http30 --> hub
    end

    cacheLookup -->|"Out0 — cache miss\nHttpRequestMessage"| partition
    hub -->|"HttpResponseMessage"| decomp

    %% ================================================================
    %% RESPONSE CHAIN
    %% ================================================================

    decomp(["DecompressionStage"]):::stage
    cookieStore(["CookieStorageStage"]):::stage
    cacheStore(["CacheStorageStage"]):::stage
    retry(["RetryStage"]):::stage

    cacheMerge{{"Merge(2)\n(cache)"}}:::junction
    redirect(["RedirectStage"]):::stage

    OUT(["⬇ HttpResponseMessage"]):::io

    decomp -->|"HttpResponseMessage"| cookieStore
    cookieStore -->|"HttpResponseMessage"| cacheStore
    cacheStore -->|"HttpResponseMessage"| retry

    retry -->|"Out0 — final\nHttpResponseMessage"| cacheMerge
    cacheLookup -->|"Out1 — cache hit\nHttpResponseMessage"| cacheMerge

    cacheMerge -->|"HttpResponseMessage"| redirect

    redirect -->|"Out0 — final\nHttpResponseMessage"| OUT

    %% ================================================================
    %% FEEDBACK LOOPS (dashed)
    %% ================================================================

    retryBuf(["Buffer(1)"]):::buffer
    redirectBuf(["Buffer(1)"]):::buffer

    retry -.->|"Out1 — retry\nHttpRequestMessage"| retryBuf
    retryBuf -.->|"preferred"| retryMerge

    redirect -.->|"Out1 — redirect\nHttpRequestMessage"| redirectBuf
    redirectBuf -.->|"preferred"| redirectMerge

    %% ================================================================
    %% STYLES
    %% ================================================================

    classDef stage fill:#4a90d9,stroke:#2c5f8a,color:#fff,rx:12,ry:12
    classDef engine fill:#7b68ee,stroke:#483d8b,color:#fff,rx:12,ry:12
    classDef junction fill:#f5a623,stroke:#c07d1a,color:#fff
    classDef buffer fill:#e74c3c,stroke:#c0392b,color:#fff,rx:8,ry:8
    classDef io fill:#2ecc71,stroke:#1a9e4f,color:#fff,rx:16,ry:16
```

## Legend

| Shape | Meaning |
|-------|---------|
| Rounded box (blue) | `GraphStage` — stateless or stateful stream processing stage |
| Rounded box (purple) | Protocol engine — composite sub-graph (`IHttpProtocolEngine`) |
| Diamond (orange) | Fan-out / fan-in junction (`Partition`, `Merge`, `MergePreferred`) |
| Rounded box (red) | `Buffer(1, Backpressure)` — cycle-breaking buffer on feedback loops |
| Stadium (green) | Pipeline input / output |
| Solid arrow | Normal data flow |
| Dashed arrow | Feedback loop (retry or redirect) |

## Stage Reference

| Stage | Source | Purpose |
|-------|--------|---------|
| `RequestEnricherStage` | `Streams/Stages/RequestEnricherStage.cs` | Applies `BaseAddress`, `DefaultRequestVersion`, `DefaultRequestHeaders` |
| `CookieInjectionStage` | `Streams/Stages/CookieInjectionStage.cs` | Injects matching cookies from `CookieJar` (RFC 6265) |
| `CacheLookupStage` | `Streams/Stages/CacheLookupStage.cs` | Cache hit → Out1, cache miss → Out0 (RFC 9111 §4) |
| `DecompressionStage` | `Streams/Stages/DecompressionStage.cs` | gzip/deflate/brotli decompression (RFC 9110 §8.4) |
| `CookieStorageStage` | `Streams/Stages/CookieStorageStage.cs` | Stores `Set-Cookie` headers in `CookieJar` (RFC 6265 §5.3) |
| `CacheStorageStage` | `Streams/Stages/CacheStorageStage.cs` | Stores cacheable responses, merges 304 (RFC 9111 §3/§4.3.4) |
| `RetryStage` | `Streams/Stages/RetryStage.cs` | Out0 = final, Out1 = retryable request (RFC 9110 §9.2) |
| `RedirectStage` | `Streams/Stages/RedirectStage.cs` | Out0 = final, Out1 = redirect request (RFC 9110 §15.4) |
| `Partition` | Akka.Streams built-in | Routes by `HttpRequestMessage.Version` (1.0 / 1.1 / 2.0 / 3.0) |
| `Merge(4)` | Akka.Streams built-in | Merges four protocol engine outputs |
| `MergePreferred` | Akka.Streams built-in | Preferred inlet prioritises feedback over new requests |
| `Buffer(1)` | Akka.Streams built-in | Breaks backpressure cycle on feedback loops |
