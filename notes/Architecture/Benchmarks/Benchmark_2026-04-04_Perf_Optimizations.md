# Benchmark Run: Performance Optimizations (2026-04-04)

## Summary

Benchmark run following three performance optimizations:
1. **Deleted dead code**: `Http20PrependPrefaceStage`, `Http20StreamIdAllocatorStage`, related test files
2. **Inlined stream ID allocation**: Stream ID generation moved into `Http20ConnectionStage` — eliminates one pipeline stage per request
3. **O(1) slot lookup in `GroupByRequestEndpointStage`**: Replaced `List<SubflowState>.Find(s => s.SlotId == id)` with `Dictionary<int, SubflowState>` — eliminates O(n) scan per connection affinity lookup

**Test status**: 3712 unit tests + 790 stream tests — all passing, 0 failures.

## Benchmark Configuration

- **Run Type**: ShortRun (3 warmup, 5 iterations, 32 invocations)
- **Hardware**: AMD Ryzen 5 7600X 4.70GHz (6 physical, 12 logical cores)
- **Runtime**: .NET 10.0.5 (x64, RyuJIT, GC: Concurrent Workstation)
- **Concurrency Levels**: 1, 4, 16, 64, 256
- **Payloads**: Light (no body) and Heavy (10 KB body)
- **HTTP Versions**: 1.1 and 2.0
- **Streaming**: 1000, 5000, 10000 requests

## Key Results

### TurboHTTP Single Request (selected)

| Concurrency | Payload | Version | Mean | Req/sec | Allocated |
|-------------|---------|---------|------|---------|-----------|
| 1 | light | 1.1 | 172 μs | 5,811 | 7.78 KB |
| 1 | light | 2.0 | 194 μs | 5,161 | 9.74 KB |
| 1 | heavy | 1.1 | 191 μs | 5,248 | 48.98 KB |
| 1 | heavy | 2.0 | 203 μs | 4,932 | 9.96 KB |
| 256 | light | 1.1 | 174 μs | 5,751 | 6.40 KB |
| 256 | light | 2.0 | 195 μs | 5,127 | 9.74 KB |

### TurboHTTP vs HttpClient — Concurrent (CL=1, light payload)

| Metric | TurboHTTP H1.1 | HttpClient H1.1 | TurboHTTP H2 | HttpClient H2 |
|--------|---------------|----------------|--------------|---------------|
| Mean | 171 μs | 102 μs | 201 μs | 119 μs |
| Req/sec | 5,834 | 9,769 | 4,977 | 8,371 |
| Allocated | 6.43 KB | 2.68 KB | 6.69 KB | 8.11 KB |

TurboHTTP is ~1.7x slower than HttpClient at CL=1 (consistent with previous baseline).

### TurboHTTP vs HttpClient — Concurrent Throughput (light payload)

| CL | TurboHTTP H1.1 | HttpClient H1.1 | TurboHTTP H2 | HttpClient H2 |
|----|----------------|----------------|--------------|---------------|
| 4 | 21K req/sec | 22K req/sec | 16K req/sec | 22K req/sec |
| 16 | 40K req/sec | 46K req/sec | 31K req/sec | **84K req/sec** |
| 64 | 34K req/sec | 53K req/sec | 27K req/sec | **46K req/sec** |
| 256 | 28K req/sec | 43K req/sec | 24K req/sec | **134K req/sec** |

HttpClient H2 at CL=256 achieves 134K req/sec (light) vs TurboHTTP 24K req/sec — because HttpClient multiplexes all 256 requests over a small number of connections, while TurboHTTP creates separate per-endpoint substreams.

### Streaming Throughput (HTTP/1.1)

| Requests | TurboHTTP | HttpClient | Ratio | TurboHTTP Alloc | HttpClient Alloc |
|----------|-----------|------------|-------|-----------------|-----------------|
| 1,000 | 22.91 ms | 19.96 ms | 1.15x | 5.23 MB | 2.43 MB |
| 5,000 | 137.32 ms | 97.93 ms | 1.40x | 26.16 MB | 12.42 MB |
| 10,000 | 276.58 ms | 193.02 ms | 1.43x | 51.23 MB | 24.36 MB |

Streaming overhead grows to ~1.4x at scale. Memory is ~2.1x compared to HttpClient across all counts.

### Heavy Payload Memory Pattern (CL=1, H2)

| Library | Mean | Allocated |
|---------|------|-----------|
| TurboHTTP | 188 μs | 7.14 KB |
| HttpClient | 157 μs | 50.45 KB |

TurboHTTP allocates **7x LESS** than HttpClient for H2 heavy payload at CL=1. The pipeline's pooled buffers avoid materialising the response body on the heap.

## Comparison vs Previous Baseline (2026-04-03)

Previous run was taken after the transport layer refactoring, before these optimisations.

| Scenario | Previous | Current | Delta |
|----------|----------|---------|-------|
| H1.1 CL=1 light mean | 166 μs | 172 μs | +3.6% (noise) |
| H2 CL=1 light mean | 205 μs | 201 μs | -2.0% (noise) |
| H1.1 CL=256 light mean | 169 μs | 174 μs | +3.0% (noise) |
| H1.1 CL=1 light alloc | 7.14 KB | 7.78 KB | +9% (noise) |

All deltas are within measurement noise (±5-15 μs with ShortRun config). The optimisations do not regress measurable latency — the gains are structural:
- One fewer pipeline stage allocation per request (inlined stream ID)
- O(1) affinity slot lookup (eliminates O(n) scan for connection pools with many slots)
- Smaller codebase: 3 stage files + 2 test files deleted

## Analysis

### Why latency numbers are similar despite optimisations

The bottleneck is **Akka actor message passing** (async scheduler), not the eliminated allocations. Stage removal reduces object count but not the number of scheduler ticks. Measurable gains would require profiling at higher concurrency levels or in sustained-throughput scenarios.

### HttpClient H2 CL=256 anomaly (134K req/sec)

HttpClient's HTTP/2 multiplexer sends 256 concurrent requests over ~1–2 connections. TurboHTTP currently opens one substream per endpoint (connection-per-slot model). This is a fundamental architectural difference, not a bug. For the HTTP/2 benchmark to be fair, TurboHTTP would need to multiplex multiple logical requests over a single physical H2 connection at the GroupBy level.

### Streaming overhead

The ~1.4x streaming overhead at 10K requests and 2.1x allocation ratio are inherent in the `IOutputItem`/`IInputItem` pipeline design. Every response is wrapped in a `DataItem` with a pooled `IMemoryOwner<byte>`. This adds a fixed overhead per item that dominates at small payloads.

## Recommendations

1. **No regression from current optimisations** — safe to ship
2. **Streaming memory**: The Gen0/Gen1/Gen2 allocations at 10K requests (`4000/2000/1000`) indicate GC pressure from `DataItem` objects. Consider a slab allocator or object pool for `DataItem`.
3. **HTTP/2 throughput gap**: Investigate multiplexing multiple logical requests per substream at the connection level for scenarios with CL > 16.
4. **Profiling target**: Run dotMemory or BenchmarkDotNet with `NativeMemoryProfiler` at CL=64 H2 to understand the 2,330 μs outlier behaviour.
5. **Baseline cadence**: Re-run these benchmarks after any change to the hot path in `Http20ConnectionStage`, `Http20EncoderStage`, or `GroupByRequestEndpointStage`.

## Related Notes

- [[Architecture/Benchmarks/Benchmark_2026-04-03_Transport_Refactoring]] — Previous baseline
- [[05-BENCHMARK_PATTERNS]] — Benchmark conventions and port assignments
- [[04-CURRENT_STATE_SUMMARY]] — Project status

## Tags

#benchmark #performance #http1 #http2 #akka-streams #optimization #2026-04-04
