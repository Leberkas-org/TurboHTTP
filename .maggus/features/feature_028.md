<!-- maggus-id: 20260326-163730-feature-028 -->

# Feature 028: Phase 3 — Performance Optimizations (Allocation & CPU Reduction)

## Introduction

Optimize two critical performance bottlenecks:
1. **Streaming Request Encoding** — Reduce allocations for large request bodies (HTTP/1.0, HTTP/1.1)
2. **SIMD CRLF Detection** — Faster line parsing in HTTP/1.1 decoder using SIMD instructions

These optimizations target 10-20% latency improvement and 30% memory reduction for large payloads.

### Architecture Context
- Components: `Http10Encoder`, `Http11Encoder`, `Http11DecoderPipeline`, `Http2RequestEncoder`
- New patterns: SIMD-optimized utilities (Vector<T>, SSE2/AVX2 intrinsics)
- Leverages existing `Span<T>` patterns

## Goals
1. Implement streaming request encoding (non-buffered headers)
2. Add SIMD CRLF detection (>20% faster line parsing)
3. Reduce GC pressure (fewer allocations, no buffer pooling)
4. Measure before/after with benchmarks (P99 latency improvement)

## Tasks

### TASK-028-001: Implement Streaming Http10Encoder
**Token Estimate:** ~35k | **Predecessors:** none | **Successors:** TASK-028-004 | **Parallel:** yes (with 002, 003)

**Acceptance Criteria:**
- [ ] Refactor `Http10Encoder.Encode()` to stream headers into `IBufferWriter<byte>` instead of buffering
- [ ] Encode request-line, then headers one-by-one without intermediate buffering
- [ ] Validate no performance regression (benchmark < 50µs for typical request)
- [ ] All existing tests pass

---

### TASK-028-002: Implement Streaming Http11Encoder
**Token Estimate:** ~40k | **Predecessors:** none | **Successors:** TASK-028-004 | **Parallel:** yes (with 001, 003)

**Acceptance Criteria:**
- [ ] Refactor `Http11Encoder.Encode()` to stream headers + body
- [ ] Handle chunked Transfer-Encoding streaming (if body is IAsyncEnumerable)
- [ ] Validate no performance regression (benchmark < 100µs for 50-header request)
- [ ] All existing tests pass

---

### TASK-028-003: Add SIMD CRLF Detection Utility
**Token Estimate:** ~45k | **Predecessors:** none | **Successors:** TASK-028-005 | **Parallel:** yes (with 001, 002)

**Acceptance Criteria:**
- [ ] Create `src/TurboHttp/Utilities/SimdCrlfFinder.cs` with optimized CRLF detection
- [ ] Use `Vector<T>` or low-level SIMD intrinsics (System.Runtime.Intrinsics)
- [ ] Benchmark vs. string.IndexOf: target >20% improvement
- [ ] Fallback to non-SIMD path on platforms without SIMD support
- [ ] Validate correctness with comprehensive unit tests

---

### TASK-028-004: Integrate Streaming Encoders into Pipeline
**Token Estimate:** ~25k | **Predecessors:** TASK-028-001, TASK-028-002 | **Successors:** TASK-028-006 | **Parallel:** no

**Acceptance Criteria:**
- [ ] Update `Http10EncoderStage` and `Http11EncoderStage` to use streaming encoders
- [ ] Verify graph construction and backpressure work correctly
- [ ] End-to-end test: send request, verify bytes match old encoder output
- [ ] All stage tests pass

---

### TASK-028-005: Integrate SIMD CRLF Detection into Http11DecoderPipeline
**Token Estimate:** ~30k | **Predecessors:** TASK-028-003 | **Successors:** TASK-028-006 | **Parallel:** no

**Acceptance Criteria:**
- [ ] Update `Http11DecoderPipeline` to use `SimdCrlfFinder` for line parsing
- [ ] Drop in replacement: same API, faster implementation
- [ ] All decoder tests pass
- [ ] Benchmark validates >20% improvement on typical responses

---

### TASK-028-006: Performance Benchmarks (Before/After)
**Token Estimate:** ~40k | **Predecessors:** TASK-028-004, TASK-028-005 | **Successors:** none | **Parallel:** no

**Acceptance Criteria:**
- [ ] Create benchmarks in `src/TurboHttp.Benchmarks/Performance/`:
  - [ ] `Http10EncoderStreamingBenchmark` (measure allocation + throughput)
  - [ ] `Http11EncoderStreamingBenchmark` (50-header request)
  - [ ] `Http11DecoderCrlfBenchmark` (typical response with 20 headers)
  - [ ] `SimdCrlfBenchmark` (direct SIMD vs. string.IndexOf)
- [ ] Compare baseline (old) vs. optimized (new)
- [ ] Target: ≥10% latency improvement, ≥30% allocation reduction
- [ ] Report in `docs/PERFORMANCE_RESULTS.md`
- [ ] Run dry: `dotnet run --configuration Release --project src/TurboHttp.Benchmarks -- --filter "*Streaming*"` — results stable

---

## Task Dependency Graph
```
TASK-028-001 ──→ TASK-028-004 ──→ TASK-028-006
TASK-028-002 ──→↗
TASK-028-003 ──→ TASK-028-005 ──→↗
```

### Summary Table

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-028-001 | ~35k | none | yes (w/ 002, 003) | — |
| TASK-028-002 | ~40k | none | yes (w/ 001, 003) | — |
| TASK-028-003 | ~45k | none | yes (w/ 001, 002) | opus |
| TASK-028-004 | ~25k | 001, 002 | no | — |
| TASK-028-005 | ~30k | 003 | no | — |
| TASK-028-006 | ~40k | 004, 005 | no | — |

**Total:** ~215k tokens (~5 days solo)

## Functional Requirements

1. **FR-1:** Streaming encoders SHALL encode headers without intermediate buffering
2. **FR-2:** SIMD CRLF detection SHALL be >20% faster than string.IndexOf
3. **FR-3:** All encoder/decoder output SHALL match previous implementation byte-for-byte
4. **FR-4:** Benchmarks SHALL show measurable improvements (latency, allocations)

## Non-Goals
- No changes to public API
- No HTTP/2 optimizations (separate phase)
- No changes to header compression (HPACK/QPACK separate)

## Success Metrics
1. Streaming encoders reduce allocations by ≥30%
2. SIMD CRLF detection improves latency by ≥20%
3. P99 latency improved by ≥10% overall
4. All benchmarks pass with stable results
5. Zero regressions in existing tests

