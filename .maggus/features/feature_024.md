<!-- maggus-id: 20250325-140000-feature-024 -->

# Feature 024: Benchmark Comparison — TurboHttp vs Standard HttpClient

## Introduction

Create a comprehensive benchmark suite comparing TurboHttp's `ITurboHttpClient` against standard .NET `HttpClient` across HTTP/1.1 and HTTP/2 protocols. The comparison measures throughput (requests/sec), latency distribution (p50/p95/p99), and memory allocations under single-request and concurrent-load scenarios. Results are published as a markdown comparison report showing performance deltas and resource efficiency trade-offs.

### Architecture Context

- **Vision alignment:** Validates TurboHttp's performance claims with quantified data against the standard .NET baseline
- **Components involved:**
  - Benchmarks layer: New BenchmarkDotNet infrastructure (`src/TurboHttp.Benchmarks/`)
  - Server: Kestrel test server (same as integration tests)
  - Clients: System.Net.Http.HttpClient and TurboHttp.Transport.ITurboHttpClient
- **New patterns:** Shared benchmark base class for parameterized concurrency levels and request payloads; markdown report generation from BenchmarkDotNet results

## Goals

- Establish quantified performance baseline comparing TurboHttp to HttpClient across both HTTP protocols
- Identify performance sweet spots and saturation points under concurrent load (1, 4, 16, 64, 256 concurrent requests)
- Validate memory efficiency (allocations/op) across payload sizes (no body, 10KB body)
- Generate human-readable markdown comparison report for documentation and marketing
- Enable regression detection for future performance changes

## Tasks

### TASK-024-001: Create Shared Benchmark Infrastructure
**Description:** As a benchmark developer, I want a reusable base class and utility helpers so that individual client benchmarks don't duplicate concurrency parametrization, metadata, and measurement setup.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-024-002, TASK-024-003, TASK-024-004
**Parallel:** yes — can run alongside other setup tasks

**Acceptance Criteria:**
- [ ] `BenchmarkBaseClass` created with:
  - `[Params(1, 4, 16, 64, 256)]` for ConcurrencyLevel
  - `[Params("light", "heavy")]` for PayloadType (no body vs 10KB)
  - `[Params(HttpVersion.Version11, HttpVersion.Version20)]` for HttpVersion
  - Public properties for derived classes
- [ ] Helper: `CreateKestrelUri(path)` returns base URI for test server
- [ ] Helper: `GeneratePayload(sizeBytes)` returns deterministic byte[] for consistency
- [ ] Helper: `WarmupRequest()` async method for pre-test priming
- [ ] Configuration: Uses EngineBenchmarkConfig (p50/p95/p100 latency columns + req/sec)
- [ ] Typecheck/lint passes
- [ ] Unit tests verify payload generation is deterministic

### TASK-024-002: Set Up Minimal Kestrel Test Server
**Description:** As a benchmark developer, I want a real Kestrel server running on localhost with two simple routes so that both HttpClient and TurboHttp benchmarks measure through the same wire protocol stack.

**Token Estimate:** ~20k tokens
**Predecessors:** TASK-024-001
**Successors:** TASK-024-003, TASK-024-004
**Parallel:** no — HTTP/1.1 and HTTP/2 require sequential server configuration
**Model:** haiku — straightforward server setup

**Acceptance Criteria:**
- [ ] Kestrel server starts in GlobalSetup on `http://127.0.0.1:0` (dynamic port)
- [ ] Port discovered via `IServer.Features.Get<IServerAddressesFeature>()`
- [ ] Routes registered:
  - `GET /benchmark/simple` — returns 200 OK with minimal body (e.g., "OK\n")
  - `POST /benchmark/payload` — accepts 10KB body, returns 200 OK with echo of size received
- [ ] HTTP/1.1 support: explicit (all requests use Version11)
- [ ] HTTP/2 support: Kestrel Alt-Svc header or ALPN upgrade path configured
- [ ] Connection keep-alive enabled (Connection: keep-alive header)
- [ ] Server stops cleanly in GlobalCleanup
- [ ] Typecheck/lint passes

### TASK-024-003: Implement HttpClient Benchmarks
**Description:** As a benchmark developer, I want to measure standard .NET HttpClient performance across payload sizes and concurrency levels so that we have a quantified baseline for comparison.

**Token Estimate:** ~60k tokens
**Predecessors:** TASK-024-001, TASK-024-002
**Successors:** TASK-024-005
**Parallel:** yes — can run alongside TASK-024-004

**Acceptance Criteria:**
- [ ] New file: `src/TurboHttp.Benchmarks/HttpClientComparativeBenchmarks.cs`
- [ ] Extends `BenchmarkBaseClass`
- [ ] Class: `HttpClientSingleRequestBenchmarks`
  - `[Benchmark]` method: Single request throughput (light payload)
  - `[Benchmark]` method: Single request throughput (heavy payload)
  - Parameterized by HttpVersion (1.1 and 2.0)
- [ ] Class: `HttpClientConcurrentBenchmarks`
  - `[Benchmark]` method: N concurrent requests (light payload)
  - `[Benchmark]` method: N concurrent requests (heavy payload)
  - Parameterized by ConcurrencyLevel and HttpVersion
  - Uses `Task.WhenAll()` to wait for completion
- [ ] GlobalSetup: Creates `HttpClient` with AllowAutoRedirect=false, configured for protocol version
- [ ] GlobalCleanup: Disposes HttpClient
- [ ] All benchmarks include `[MemoryDiagnoser]` to capture allocations
- [ ] Warmup: 3 iterations, Target: 5 iterations, Invocations: 32 (per user spec)
- [ ] Typecheck/lint passes

### TASK-024-004: Implement TurboHttp Benchmarks
**Description:** As a benchmark developer, I want to measure TurboHttp's `ITurboHttpClient` performance across the same scenarios as HttpClient so that we can compare apples-to-apples throughput, latency, and memory efficiency.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-024-001, TASK-024-002
**Successors:** TASK-024-005
**Parallel:** yes — can run alongside TASK-024-003

**Acceptance Criteria:**
- [ ] New file: `src/TurboHttp.Benchmarks/TurboHttpComparativeBenchmarks.cs`
- [ ] Extends `BenchmarkBaseClass`
- [ ] Class: `TurboHttpSingleRequestBenchmarks`
  - `[Benchmark]` method: Single request via channel write/read (light payload)
  - `[Benchmark]` method: Single request via channel write/read (heavy payload)
  - Parameterized by HttpVersion (1.1 and 2.0)
- [ ] Class: `TurboHttpConcurrentBenchmarks`
  - `[Benchmark]` method: N concurrent requests (light payload)
  - `[Benchmark]` method: N concurrent requests (heavy payload)
  - Parameterized by ConcurrencyLevel and HttpVersion
  - Uses `Task.WhenAll()` on N tasks, each writing request and reading response
- [ ] GlobalSetup: Creates `ITurboHttpClient` via `ClientHelper.CreateClient()` with version override
- [ ] GlobalCleanup: Calls `DisposeAsync()` on client
- [ ] All benchmarks include `[MemoryDiagnoser]` to capture allocations
- [ ] Warmup: 3 iterations, Target: 5 iterations, Invocations: 32 (per user spec)
- [ ] Typecheck/lint passes

### TASK-024-005: Create Markdown Comparison Report Generator
**Description:** As a benchmark developer, I want to transform BenchmarkDotNet results into a human-readable markdown report showing TurboHttp vs HttpClient performance deltas so that stakeholders can quickly understand relative performance.

**Token Estimate:** ~35k tokens
**Predecessors:** TASK-024-003, TASK-024-004
**Successors:** TASK-024-006
**Parallel:** no — requires both sets of benchmark results

**Acceptance Criteria:**
- [ ] New utility class: `BenchmarkComparisonReport` in `src/TurboHttp.Benchmarks/`
- [ ] Method: `GenerateReport(httpClientResults, turboHttpResults) → markdown string`
- [ ] Report structure:
  - Header: Feature version, test date, server configuration
  - Summary table: Throughput comparison (Req/sec) for single + concurrent scenarios
  - Latency table: p50/p95/p99 comparison for each scenario
  - Memory table: Allocations/op comparison for each scenario
  - Notes: CPU/GC pressure observations, any variance anomalies
- [ ] Comparison format: Side-by-side columns (HttpClient | TurboHttp | Delta%)
  - Green highlight (✓) if TurboHttp ≥ HttpClient by >5%
  - Neutral (–) if within ±5%
  - Red highlight (✗) if TurboHttp <HttpClient by >5%
- [ ] Output: Markdown written to `benchmarks/comparison_report_<timestamp>.md`
- [ ] Typecheck/lint passes
- [ ] Unit tests verify table formatting and delta calculation

### TASK-024-006: Verification Gate — Run & Validate Benchmarks
**Description:** As a benchmark developer, I want to execute the full benchmark suite with dry-run validation, verify server connectivity, and generate a sample comparison report so that all infrastructure is working before committing.

**Token Estimate:** ~15k tokens
**Predecessors:** TASK-024-003, TASK-024-004, TASK-024-005
**Successors:** none
**Parallel:** no — requires everything ready

**Acceptance Criteria:**
- [ ] Run `dotnet run --configuration Release --project src/TurboHttp.Benchmarks -- --filter "*SingleRequest*" --job dry` and confirm success
- [ ] Run `dotnet run --configuration Release --project src/TurboHttp.Benchmarks -- --filter "*Concurrent*" --job dry --filter ConcurrencyLevel=4 --filter PayloadType=light` and confirm success
- [ ] Verify Kestrel server starts on dynamic port and routes respond
- [ ] Generate sample comparison report with dry-run results
- [ ] Check report: Ensure no NaN/Inf values, all deltas calculated, markdown renders cleanly
- [ ] Verify build passes: `dotnet build --configuration Release src/TurboHttp.sln`
- [ ] Document: Add a `BENCHMARK_COMPARISON.md` to project root explaining:
  - How to run benchmarks: `dotnet run --configuration Release --project src/TurboHttp.Benchmarks/`
  - How to filter scenarios: `--filter "*HttpClient*"`, `--filter "*TurboHttp*"`
  - Interpretation of results (throughput, latency, memory)
  - Known caveats (loopback vs real network, warmup strategy, etc.)
- [ ] All acceptance criteria from TASK-024-003, 004, 005 verified
- [ ] Typecheck/lint passes

## Task Dependency Graph

```
TASK-024-001 ──→ TASK-024-002 ──→ TASK-024-003 ──┐
                                                  ├──→ TASK-024-005 ──→ TASK-024-006
                  TASK-024-002 ──→ TASK-024-004 ──┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-024-001 | ~25k | none | yes | — |
| TASK-024-002 | ~20k | 001 | no | haiku |
| TASK-024-003 | ~60k | 001, 002 | yes (with 004) | — |
| TASK-024-004 | ~50k | 001, 002 | yes (with 003) | — |
| TASK-024-005 | ~35k | 003, 004 | no | — |
| TASK-024-006 | ~15k | 003, 004, 005 | no | — |

**Total estimated tokens:** ~205k

## Functional Requirements

- FR-1: Benchmark suite must test both HTTP/1.1 and HTTP/2 via Version property on HttpRequestMessage
- FR-2: Concurrency levels must cover 1, 4, 16, 64, 256 concurrent requests
- FR-3: Payload variants must include light (no body, ~20 bytes response) and heavy (10KB body)
- FR-4: Throughput must be measured in requests/sec via custom RequestsPerSecondColumn
- FR-5: Latency must include p50, p95, p99 percentiles via StatisticColumn
- FR-6: Memory must be measured in bytes/operation via MemoryDiagnoser
- FR-7: Kestrel server must be real (not loopback stages), discoverable via dynamic port on 127.0.0.1
- FR-8: Comparison report must be markdown format with side-by-side performance deltas and visual indicators
- FR-9: All benchmarks must use conservative warmup (3 warmup, 5 target, 32 invocations) to reduce flakiness
- FR-10: Build must pass with zero warnings when benchmarks are included

## Non-Goals

- No HTTP/1.0 or HTTP/3 (QUIC) — scope is 1.1 and 2.0 only
- No feature-specific comparisons (cookies, compression, redirects are disabled)
- No comparison with other HTTP clients (e.g., RestSharp, Flurl) — baseline is HttpClient only
- No TLS/HTTPS benchmarks — scope is localhost HTTP only
- No real network comparison (e.g., remote server) — all tests run on loopback
- No async vs sync comparison — focus is on throughput/latency/memory under standard async patterns
- No benchmark automation in CI/CD — dry-run only, full runs are manual

## Technical Considerations

- **Kestrel dynamic port discovery:** Must use `IServer.Features.Get<IServerAddressesFeature>()` pattern (already used in integration tests)
- **Keep-alive reuse:** Both clients must reuse connections across benchmark iterations; verify via connection count monitoring
- **Garbage collection:** BenchmarkDotNet controls GC by default; no manual control needed
- **Warmup overhead:** First 3 iterations discarded, only last 5 counted — helps stabilize variance
- **Payload determinism:** Payloads must be identical across runs so light/heavy comparisons are fair
- **BenchmarkDotNet configuration:** Reuse existing `EngineBenchmarkConfig` (MemoryDiagnoser + RequestsPerSecondColumn + p50/p95/p100)
- **No modifications to ARCHITECTURE.md:** This feature extends benchmarking infrastructure only; no architectural changes needed

## Success Metrics

- All benchmarks run to completion without errors or timeouts
- Comparison report generates with valid markdown formatting
- Throughput deltas clearly show which client is faster in each scenario
- Memory efficiency visible (allocations/op comparison)
- Latency distribution shows performance consistency (p50 vs p99 spread)
- Report is suitable for documentation/marketing use without manual edits

## Open Questions

None — all implementation details clarified by user responses.

---

## Summary for Execution

**Objective:** Build a performance comparison framework proving TurboHttp competitive with (or faster than) standard HttpClient across HTTP/1.1 and HTTP/2.

**Key Deliverables:**
1. Reusable benchmark base class with concurrency/payload parametrization
2. Real Kestrel test server with two simple routes
3. HttpClient benchmark suite (single + concurrent, light + heavy, v1.1 + v2.0)
4. TurboHttp benchmark suite (identical scenarios)
5. Markdown comparison report generator with performance deltas
6. Verification gate proving all infrastructure works

**Why this matters:** Stakeholders can see quantified evidence that TurboHttp delivers competitive performance despite its different (stream-based) architecture.
