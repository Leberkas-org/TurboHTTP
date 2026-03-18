# Plan 12: Akka.Streams Pipeline Performance Optimization

## Introduction

Optimize TurboHttp's Akka.Streams pipeline for balanced throughput, latency, and resource efficiency using Akka.NET stream primitives — async boundaries, `Batch`/`BatchWeighted`, buffer tuning, and fusing control. The scope starts minimal (Akka.Streams composition-level only), focusing on shared pipeline stages across all protocol versions while preserving all existing stage interfaces and shapes (no breaking changes).

**Critical finding:** None of the 26 custom `GraphStage` implementations currently use `InitialAttributes` or read `inheritedAttributes` in `CreateLogic`. This means any `.WithAttributes()` calls at the composition level have no effect on stage-internal behavior. This must be fixed first before any buffer tuning can take effect.

## Goals

- Fix the Attributes plumbing gap: stages must declare `InitialAttributes` and read `inheritedAttributes` where relevant
- Reduce per-request overhead in the shared Engine pipeline (cookie, cache, redirect, retry, decompression stages)
- Improve throughput by introducing async boundaries at CPU-intensive stage clusters
- Lower memory pressure through buffer tuning and materializer configuration
- Establish a hybrid benchmark baseline to measure improvements
- Keep all existing `GraphStage` shapes and public APIs unchanged

## User Stories

### TASK-12-001: Smoke Benchmark Harness

**Description:** As a developer, I want a lightweight benchmark harness that measures request throughput and p99 latency through the Engine pipeline so that I have a baseline before optimizing.

**Acceptance Criteria:**
- [x] BenchmarkDotNet project or benchmarks exist at `src/TurboHttp.Benchmarks/`
- [x] Benchmark exercises the full Engine graph (encode -> decode -> correlate) for HTTP/1.1 and HTTP/2
- [x] Measures: ops/sec, p50/p99 latency, allocations (bytes/op)
- [x] Benchmark runs against a loopback `Source`/`Sink` (no real TCP) to isolate stream overhead
- [x] Results captured as baseline in `.maggus/runs/benchmark_baseline_plan6.md`
- [x] Unit tests are written and successful

### TASK-12-002: Fix InitialAttributes in All GraphStages

**Description:** As a developer, I want all custom `GraphStage` implementations to properly declare `InitialAttributes` and read `inheritedAttributes` where relevant, so that composition-level `.WithAttributes()` calls actually take effect.

**Acceptance Criteria:**
- [x] Every `GraphStage` subclass overrides `InitialAttributes` with a meaningful `Attributes.CreateName("stage-name")`
- [x] Stages that use internal buffer sizes (encoder min/max, decoder growth, GroupByHostKey queue size) read `inheritedAttributes.GetAttribute<Attributes.InputBuffer>(fallback)` in `CreateLogic` and use it to configure their buffers
- [x] Lightweight pass-through stages (CookieInjectionStage, CookieStorageStage, StreamIdAllocatorStage, RequestEnricherStage) declare `InitialAttributes` with name only (no buffer override needed — they don't buffer)
- [x] Stages affected: all 26 custom GraphStages listed below
- [x] No existing stage shapes or public APIs changed
- [x] All existing tests still pass
- [x] Unit tests verify that `inheritedAttributes` buffer config is respected (e.g., encoder uses custom buffer size when attribute is set)

**Stages requiring `InitialAttributes` + buffer-aware `CreateLogic`:**

| Stage | Buffer Behavior | Attributes Needed |
|-------|----------------|-------------------|
| Http10EncoderStage | Min 4KB / Max 256KB rent | Read InputBuffer for rent sizing |
| Http11EncoderStage | Min 4KB / Max 256KB rent | Read InputBuffer for rent sizing |
| Http20EncoderStage | SerializedSize-based | Name only |
| Http10DecoderStage | Stateful _decoder | Name only |
| Http11DecoderStage | Stateful _decoder, EmitMultiple | Name only |
| Http20DecoderStage | Dynamic doubling buffer | Read InputBuffer for initial/max cap |
| Http20StreamStage | Per-stream header+body buffers | Read InputBuffer for pre-alloc sizing |
| Http20ConnectionStage | Window tracking dict | Name only |
| GroupByHostKeyStage | Queue(16) per substream | Read InputBuffer for queue capacity |
| MergeSubstreamsStage | Queue buffer | Read InputBuffer for buffer sizing |
| Request2FrameStage | Queue of pending frames | Name only |
| Http1XCorrelationStage | Request+response queues | Name only |
| ConnectionStage | Pending reads queue | Name only |
| ExtractOptionsStage | Single buffered request | Name only |
| ConnectionReuseStage | Pending response+signal | Name only |
| DecompressionStage | Pooled read buffer | Name only |
| CacheLookupStage | None (stateless) | Name only |
| CacheStorageStage | Response + cache key | Name only |
| CookieInjectionStage | None (pass-through) | Name only |
| CookieStorageStage | Response reference | Name only |
| RedirectStage | None | Name only |
| RetryStage | Optional buffered request | Name only |
| PrependPrefaceStage | First-element flag | Name only |
| RequestEnricherStage | None (pass-through) | Name only |
| StreamIdAllocatorStage | Counter only | Name only |
| Http20CorrelationStage | Stream-ID-based dict | Name only |

### TASK-12-003: Materializer Buffer Tuning

**Description:** As a developer, I want to configure the `ActorMaterializer` with explicit input buffer sizes so that small stages don't over-allocate and large stages get adequate buffering.

**Acceptance Criteria:**
- [x] `TurboClientStreamManager` creates materializer with explicit `ActorMaterializerSettings.WithInputBuffer(initialSize: 4, maxSize: 16)` (down from Akka default 16/16)
- [x] In Engine.cs graph composition: encoder/decoder stage groups get `.WithAttributes(Attributes.CreateInputBuffer(16, 64))` to override the global default where throughput matters
- [x] Lightweight stages (cookie injection/storage, request enricher) inherit the smaller global default
- [x] Attributes actually propagate because TASK-6-002 fixed `InitialAttributes`
- [x] No existing stage shapes or public APIs changed
- [x] Existing stream tests still pass
- [x] Unit tests are written and successful

### TASK-12-004: Async Boundaries for CPU-Intensive Stage Clusters

**Description:** As a developer, I want to insert async boundaries around CPU-heavy stage groups so that encoding/decoding work can run in parallel with I/O and lightweight pipeline stages.

**Acceptance Criteria:**
- [x] In `Engine.cs`: `.WithAttributes(Attributes.CreateAsyncBoundary())` inserted to create three fused islands:
  1. **Pre-processing island:** RequestEnricherStage -> redirect merge -> CookieInjectionStage -> retry merge -> CacheLookupStage
  2. **Protocol engine island:** EngineCore (all protocol engines) + DecompressionStage
  3. **Post-processing island:** CookieStorageStage -> CacheStorageStage -> RetryStage -> CacheMerge -> RedirectStage
- [x] No `.Async()` / async boundary inside individual protocol engines (they stay fused internally for low overhead)
- [x] No existing stage shapes or public APIs changed
- [x] Existing stream tests still pass
- [x] ⚠️ BLOCKED: Benchmark shows measurable improvement on multi-core (compare TASK-6-001 baseline) — Existing loopback benchmark uses single sequential requests which don't exercise multi-core parallelism. Async boundaries add minimal overhead (~29 µs/req HTTP/1.1, ~22 µs/req HTTP/2) confirming no regression. True multi-core improvement requires a concurrent request benchmark (deferred to TASK-12-009).
- [x] Unit tests are written and successful

### TASK-12-005: GroupByHostKey Queue Size Tuning

**Description:** As a developer, I want to increase the per-host substream queue capacity and make it configurable so that bursty request patterns don't cause excessive backpressure stalls.

**Acceptance Criteria:**
- [x] `GroupByHostKeyStage` queue size changed from hardcoded `16` to a constructor parameter with default `64`
- [x] Queue size is passed from `Engine.BuildProtocolFlow` — no public API changes on `ITurboHttpClient`
- [x] The queue size reads from `inheritedAttributes` (TASK-6-002) so it can be overridden via `.WithAttributes()` at composition level
- [x] No existing stage shapes changed
- [x] Existing stream tests still pass
- [x] Unit tests are written and successful

### TASK-12-006: Batch Encoding for HTTP/2 Frame Output

**Description:** As a developer, I want to batch multiple small HTTP/2 frames into a single write operation using Akka.NET's `Flow.BatchWeighted` so that TCP write syscalls are reduced under high multiplexing load.

**Acceptance Criteria:**
- [x] A `Flow.BatchWeighted` operator is inserted in `Http20Engine` between `Http20EncoderStage` output and the `FlowSelect` DataItem wrapper
- [x] Weight function uses the `int` (byte length) from the `(IMemoryOwner<byte>, int)` tuple
- [x] Max weight: 64 KB (approximately one TCP segment); seed creates initial buffer; aggregate concatenates
- [x] Falls through immediately if only one frame available (no artificial delay — this is built-in BatchWeighted behavior)
- [x] Batched frames are concatenated into a single `IMemoryOwner<byte>` buffer before emitting downstream
- [x] No existing stage shapes or public APIs changed
- [x] Existing HTTP/2 stream tests still pass
- [x] Unit tests for batch consolidation logic
- [x] Unit tests are written and successful

### TASK-12-007: Batch Encoding for HTTP/1.1 Pipelined Requests

**Description:** As a developer, I want to apply a similar batching strategy for HTTP/1.1 pipelined request encoding so that multiple small requests are coalesced into fewer TCP writes.

**Acceptance Criteria:**
- [x] A `Flow.Batch` operator inserted in `Http11Engine` between `Http11EncoderStage` and the DataItem wrapper
- [x] Max batch count: 8 requests or 64 KB total (via `BatchWeighted`), whichever comes first
- [x] Coalesces encoded byte buffers into single contiguous `IMemoryOwner<byte>`
- [x] No batching applied to HTTP/1.0 (connection-per-request, no benefit)
- [x] No existing stage shapes or public APIs changed
- [x] Existing HTTP/1.1 stream tests still pass
- [x] Unit tests are written and successful

### TASK-12-008: Redirect/Retry Feedback Buffer Optimization

**Description:** As a developer, I want to tune the feedback loop buffers in the Engine so that redirect and retry cycles don't cause unnecessary backpressure on the main pipeline.

**Acceptance Criteria:**
- [x] Redirect feedback `Buffer(1)` increased to `Buffer(4)` to allow multiple in-flight redirects
- [x] Retry feedback `Buffer(1)` increased to `Buffer(4)` to allow multiple in-flight retries
- [x] `MergePreferred` priority inlet behavior verified: feedback items always processed before new requests
- [x] No deadlock risk introduced (cycle-breaking invariant maintained — analyze carefully)
- [x] No existing stage shapes or public APIs changed
- [x] Existing stream tests still pass
- [x] Unit tests are written and successful

### TASK-12-009: Benchmark Validation Run

**Description:** As a developer, I want to run the benchmark suite after all optimizations to measure improvements against the TASK-6-001 baseline.

**Acceptance Criteria:**
- [ ] All previous TASKs merged and building green
- [ ] Benchmark harness from TASK-6-001 re-executed with identical parameters
- [ ] Results saved to `.maggus/runs/benchmark_optimized_plan6.md`
- [ ] Comparison table produced: baseline vs. optimized (ops/sec, p99, allocations)
- [ ] Any regressions flagged and investigated
- [ ] Summary written to `.maggus/runs/benchmark_comparison_plan6.md`

## Functional Requirements

- FR-1: All optimizations must be backward-compatible — no changes to `GraphStage` shapes, inlet/outlet types, or public APIs
- FR-2: Async boundaries must be placed at the Engine composition level (`Engine.cs`), not inside individual stages
- FR-3: `BatchWeighted`/`Batch` operators must use Akka.NET built-in `Flow.BatchWeighted<T>()` / `Flow.Batch<T>()` — no custom reimplementation
- FR-4: Buffer size changes must use `Attributes.CreateInputBuffer()` or explicit `Buffer()` stages — applied via `.WithAttributes()` at composition level
- FR-5: All `GraphStage` subclasses must override `InitialAttributes` with at minimum a name attribute
- FR-6: Stages with configurable internal buffers must read buffer config from `inheritedAttributes` in `CreateLogic`
- FR-7: All benchmark results must include allocation metrics (GC gen0/gen1/gen2 counts) alongside throughput and latency
- FR-8: GroupByHostKey queue size must be configurable via both constructor parameter and inherited attributes

## Non-Goals

- No stage-internal refactoring (streaming decompression, IBufferWriter rewrites, object pooling) — deferred to a follow-up plan
- No I/O layer changes (Channel sizes, ConnectionStage pump, ClientByteMover) — out of scope
- No new `GraphStage` implementations — only composition-level operators (`Batch`, `Buffer`, `.WithAttributes()`)
- No changes to HPACK encoder/decoder internals
- No HTTP/3 work
- No changes to `ITurboHttpClient` public API
- No custom Akka dispatchers or dispatcher configuration — use default Akka.NET dispatchers only
- No custom Akka mailbox implementations

## Technical Considerations

### Akka.NET Fusing Model
- By default, all stages between async boundaries are fused into a single actor — this minimizes message passing overhead but means CPU work blocks the fusion island
- `Attributes.CreateAsyncBoundary()` (applied via `.WithAttributes()`) creates actor boundaries — each island gets its own actor and mailbox, enabling true parallelism but adding per-element async message cost
- The sweet spot is 2-3 islands: pre-processing, protocol engine, post-processing

### Attributes Plumbing in Akka.NET GraphStages
- `InitialAttributes` is a virtual property on `GraphStage<TShape>` — override it to declare stage-level defaults
- `CreateLogic(Attributes inheritedAttributes)` receives the merged attributes (stage defaults + composition-level overrides)
- To read buffer config: `inheritedAttributes.GetAttribute(new Attributes.InputBuffer(defaultInit, defaultMax))`
- Without `InitialAttributes` override, the stage has no name in logs/debug and no default attributes
- Without reading `inheritedAttributes`, any `.WithAttributes()` call at composition level is silently ignored for stage-internal behavior

### BatchWeighted Semantics (Akka.NET)
- `Flow.BatchWeighted<TOut, TAgg>(maxWeight, costFn, seed, aggregate)` accumulates elements while downstream has no demand
- Does NOT add latency when downstream is pulling fast (single-element passthrough)
- Only activates under backpressure — ideal for bursty TCP writes
- Weight function must be cheap (O(1)) — use the byte length from the tuple
- If a single element exceeds `maxWeight`, it is emitted immediately (no batching)

### Buffer Strategy
- Akka.NET default input buffer: 16 elements initial, 16 max
- Reducing globally to 4/16 saves memory across 26+ stages in the pipeline
- Encoder/decoder stages benefit from larger buffers (16/64) due to variable processing time
- GroupByHostKey queue at 16 is a bottleneck for bursty traffic — 64 is a better default

### Risks
- Over-batching HTTP/2 frames could increase latency for single-request scenarios — mitigated by immediate passthrough when no backpressure
- Too many async boundaries add per-element overhead that may exceed the parallelism benefit for small payloads — benchmark will validate
- Increasing feedback buffers from 1 to 4 requires careful deadlock analysis of the cycle-breaking invariant
- Changing `InitialAttributes` in stages could theoretically affect fusing behavior — test thoroughly

## Success Metrics

- Throughput (ops/sec) improved by >=15% on multi-core benchmark (4+ cores)
- p99 latency not regressed by more than 5% for single-request scenarios
- Memory allocations (bytes/op) reduced or stable (<=5% increase acceptable for throughput gains)
- All 2,111+ existing tests remain green
- No new test flakiness introduced by async boundaries

## Open Questions

1. Should `BatchWeighted` also be applied to the inbound (decode) path, or only outbound (encode)? Also applied on the inbound path
2. Is the 64 KB max batch weight optimal, or should it match the TCP send buffer size of the platform? 64KB is optimal for first poc
3. Should `EngineSettings` (for queue sizes, buffer sizes) be a new internal class or extend `TurboClientOptions`? new internal
4. What is the target core count for benchmarks — developer laptop (4-8 cores) or CI server? 2 cores
5. Should stages that use `EmitMultiple` (Http11DecoderStage, Http20DecoderStage) also benefit from output buffer attributes, or is the current behavior sufficient?
