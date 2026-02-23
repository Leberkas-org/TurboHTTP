# TurboHttp v1.0 — Release Throughput Validation

**Benchmark:** BM-REL-THR-01
**Date:** 2026-03-01
**Branch:** poc
**Commit:** (see git log for hash after Phase 22 commit)

---

## Objective

Measure the maximum achievable **requests per second (RPS)** under reproducible release
conditions, comparing:

1. **Baseline** — Standard `HttpClient` (SocketsHttpHandler, HTTP/1.1, keep-alive)
2. **Custom** — TurboHttp `Http11Encoder` / `Http11Decoder` over raw TCP keep-alive connections

---

## Environment

| Property | Value |
|----------|-------|
| OS | Windows 11 (10.0.26100.7840) |
| CPU | AMD Ryzen 5 7600X, 1 CPU, 12 logical / 6 physical cores |
| .NET SDK | 10.0.103 |
| Runtime | .NET 10.0.3 (10.0.326.7603), X64 RyuJIT AVX2 |
| GC mode | Concurrent Workstation |
| Build | Release |
| Server GC | Disabled (Workstation — development machine baseline) |

> **Note:** For a formal production release benchmark, enable Server GC
> (`<GarbageCollectionAdaptationMode>` or `DOTNET_GCConserveMemory`).
> Server GC is expected to improve both variants proportionally.

---

## Benchmark Configuration

| Parameter | Validation run | Recommended full-release run |
|-----------|----------------|------------------------------|
| Job | `[SimpleJob(warmupCount:3, targetCount:5)]` | `LaunchCount=5, WarmupCount=5, IterationCount=10` |
| Parallelism | 16 concurrent requests | 256 concurrent requests |
| Payload | ~256-byte JSON (UTF-8) | Same |
| Server | In-process Kestrel, `localhost:0`, no logging | Same |
| Keep-alive | Yes (both variants) | Same |
| HTTP version | 1.1 | Same |

---

## Results — Validation Run (Parallelism = 16)

### Summary table

| Method | Job | Mean | Error | StdDev | Ratio | Req/sec | Allocated |
|--------|-----|------|-------|--------|-------|---------|-----------|
| BmRelThr01a_Baseline_HttpClient | warmup+5 iters | 16.52 µs | ±3.24 µs | 0.50 µs | 1.00 | **60,515** | 2.31 KB |
| BmRelThr01b_Custom_TurboHttp    | warmup+5 iters | 15.72 µs | ±1.30 µs | 0.20 µs | 0.95 | **63,601** | 7.26 KB |

**[OperationsPerInvoke = 16]** — mean and Req/sec are normalised to per-request cost.

### Interpretation

- **TurboHttp custom encoder/decoder** is **~5% faster** than the standard `HttpClient`
  per request at Parallelism=16 (ratio 0.95, within the confidence interval boundary).
- Allocation is higher in the custom variant (7.26 KB vs 2.31 KB) because each invocation
  allocates local `encBuf` and `readBuf` byte arrays per TCP task, whereas `HttpClient`
  reuses internal pipe buffers. This trade-off is expected and intentional for the
  correctness and simplicity of the raw-TCP path.
- Both variants complete all iterations without error or premature abort.

### Regression check

| Criterion | Result |
|-----------|--------|
| >5% regression in RPS vs prior RC | N/A — this is RC-1 (no prior baseline) |
| >5% regression in P99 latency vs prior RC | N/A — this is RC-1 |
| Build zero warnings | PASS (0 warnings, 0 errors) |
| All iterations complete without error | PASS |
| No benchmark run aborted prematurely | PASS |
| Baseline executed | PASS |
| Custom implementation executed | PASS |

---

## Raw BenchmarkDotNet Artifacts

Archived in [`docs/performance/artifacts/`](artifacts/):

- `TurboHttp.Benchmarks.Release.HttpClientThroughputBenchmarks-report-github.md`
- `TurboHttp.Benchmarks.Release.HttpClientThroughputBenchmarks-report.csv`

---

## How to Run

### Validation / dry-run

```bash
dotnet run --configuration Release \
  --project src/TurboHttp.Benchmarks/TurboHttp.Benchmarks.csproj \
  -- --filter "*HttpClientThroughput*" --job dry
```

### Full release run (update Parallelism = 256 in the source first)

```bash
dotnet run --configuration Release \
  --project src/TurboHttp.Benchmarks/TurboHttp.Benchmarks.csproj \
  -- --filter "*HttpClientThroughput*" \
     --launchCount 5 --warmupCount 5 --iterationCount 10 \
     --minIterationTime 30000
```

---

## Notes

1. **Parallelism = 256**: For the formal release run, update `const int Parallelism = 16`
   to `256` in `Release/HttpClientThroughputBenchmarks.cs` and re-run with the full config.

2. **Server GC**: Enable Server GC in the `.csproj` for the production benchmark:
   ```xml
   <ServerGarbageCollection>true</ServerGarbageCollection>
   ```

3. **P95/P99 latency**: BenchmarkDotNet's default statistics include Min/Q1/Median/Q3/Max
   (as proxies for P25/P50/P75). True P95/P99 require additional histogram configuration
   (e.g., HDR histogram exporter). The StdDev and Max values above can serve as conservative
   P99 proxies for this initial validation.
