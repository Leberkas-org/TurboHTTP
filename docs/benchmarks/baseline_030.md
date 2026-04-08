# Benchmark Baseline — Feature 030

**Date:** 2026-04-09
**Git Commit:** `0cf08586be6703a451569b523c85d28a33ae64ac` (branch: `feature/better-graph`)
**Runtime:** .NET 10.0, Release configuration
**Total benchmarks:** 166, executed in 00:10:21 (621.65 sec)

---

## Benchmark Classes

| # | Class | Description | Benchmark Methods |
|---|-------|-------------|-------------------|
| 1 | `HttpClientSingleRequestBenchmarks` | Baseline .NET HttpClient, single sequential request | `SingleRequest_Light`, `SingleRequest_Heavy` |
| 2 | `HttpClientConcurrentBenchmarks` | Baseline .NET HttpClient, concurrent requests | `ConcurrentRequests_Light`, `ConcurrentRequests_Heavy` |
| 3 | `TurboHttpSingleRequestBenchmarks` | TurboHTTP, single sequential request | `SingleRequest_Light`, `SingleRequest_Heavy` |
| 4 | `TurboHttpConcurrentBenchmarks` | TurboHTTP, concurrent requests | `ConcurrentRequests_Light`, `ConcurrentRequests_Heavy` |
| 5 | `StreamingThroughputBenchmarks` | TurboHTTP streaming vs HttpClient concurrent | `TurboHttp_Streaming`, `HttpClient_Concurrent` |

### Parameters

- **ConcurrencyLevel** (classes 1-4): 1, 4, 16, 64, 256
- **PayloadType** (classes 1-4): light, heavy
- **HttpVersion** (classes 1-4): 1.1, 2.0
- **RequestCount** (class 5): 1000, 5000, 10000
- **HttpVersion** (class 5): 1.1

---

## Single Request Benchmarks (CL=1, representative subset)

### HTTP/1.1

| Method | PayloadType | Median (us) | Allocated |
|--------|-------------|------------:|----------:|
| HttpClient.SingleRequest_Light | light | 120.17 | 2.63 KB |
| HttpClient.SingleRequest_Heavy | light | 104.67 | 40.6 KB |
| HttpClient.SingleRequest_Light | heavy | 87.93 | 2.63 KB |
| HttpClient.SingleRequest_Heavy | heavy | 98.81 | 40.6 KB |
| TurboHttp.SingleRequest_Light | light | 171.31 | 4.89 KB |
| TurboHttp.SingleRequest_Heavy | light | 183.16 | 42.89 KB |
| TurboHttp.SingleRequest_Light | heavy | 167.64 | 4.89 KB |
| TurboHttp.SingleRequest_Heavy | heavy | 251.14 | 42.89 KB |

### HTTP/2.0

| Method | PayloadType | Median (us) | Allocated |
|--------|-------------|------------:|----------:|
| HttpClient.SingleRequest_Light | light | 116.61 | 3.30 KB |
| HttpClient.SingleRequest_Heavy | light | 147.17 | 42.63 KB |
| HttpClient.SingleRequest_Light | heavy | 110.09 | 3.30 KB |
| HttpClient.SingleRequest_Heavy | heavy | 167.29 | 42.63 KB |
| TurboHttp.SingleRequest_Light | light | 197.81 | 5.97 KB |
| TurboHttp.SingleRequest_Heavy | light | 191.71 | 16.25 KB |
| TurboHttp.SingleRequest_Light | heavy | 195.47 | 5.97 KB |
| TurboHttp.SingleRequest_Heavy | heavy | 205.51 | 16.25 KB |

---

## Concurrent Request Benchmarks (HTTP/1.1, light payload)

| CL | HttpClient Light Median (us) | TurboHTTP Light Median (us) | HttpClient Heavy Median (us) | TurboHTTP Heavy Median (us) |
|---:|-----------------------------:|----------------------------:|-----------------------------:|----------------------------:|
| 1 | 84.28 | 165.24 | 110.07 | 184.45 |
| 4 | 137.34 | 205.52 | 200.63 | 225.37 |
| 16 | 348.43 | 321.25 | 587.38 | 377.48 |
| 64 | 1,135.34 | 1,021.27 | 1,950.21 | 1,289.86 |
| 256 | 5,474.34 | 4,587.23 | 8,170.27 | 7,153.95 |

> TurboHTTP catches up at CL=16 and overtakes HttpClient at CL=64+ for HTTP/1.1.

## Concurrent Request Benchmarks (HTTP/2.0, light payload)

| CL | HttpClient Light Median (us) | TurboHTTP Light Median (us) | HttpClient Heavy Median (us) | TurboHTTP Heavy Median (us) |
|---:|-----------------------------:|----------------------------:|-----------------------------:|----------------------------:|
| 1 | 117.67 | 232.03 | 177.76 | 203.98 |
| 4 | 145.42 | 227.46 | 195.97 | 233.62 |
| 16 | 172.31 | 2,311.59 | 651.52 | 1,755.42 |
| 64 | 1,027.94 | 2,889.48 | 2,123.52 | 3,051.51 |
| 256 | 2,674.07 | 7,967.87 | 4,056.71 | 9,613.63 |

> TurboHTTP H2 has significant latency overhead at higher concurrency — a known bottleneck area for Feature 030.

---

## Concurrent Allocation Summary (HTTP/1.1, CL=256)

| Method | Payload | Allocated |
|--------|---------|----------:|
| HttpClient.ConcurrentRequests_Light | light | 657.94 KB |
| TurboHttp.ConcurrentRequests_Light | light | 1,187.44 KB |
| HttpClient.ConcurrentRequests_Heavy | light | 10,465.29 KB |
| TurboHttp.ConcurrentRequests_Heavy | light | 10,792.88 KB |
| HttpClient.ConcurrentRequests_Light | heavy | 664.12 KB |
| TurboHttp.ConcurrentRequests_Light | heavy | 1,206.11 KB |
| HttpClient.ConcurrentRequests_Heavy | heavy | 10,463.02 KB |
| TurboHttp.ConcurrentRequests_Heavy | heavy | 10,849.50 KB |

---

## Streaming Throughput Benchmarks (HTTP/1.1)

| RequestCount | TurboHTTP Streaming Median (us) | TurboHTTP Alloc | HttpClient Concurrent Median (us) | HttpClient Alloc | Ratio (TurboHTTP/HttpClient) | Alloc Ratio |
|-------------:|--------------------------------:|----------------:|----------------------------------:|-----------------:|-----------------------------:|------------:|
| 1,000 | 18,424.75 | 3,795.08 KB | 17,189.85 | 2,492.51 KB | 1.07 | 1.52 |
| 5,000 | 94,689.50 | 19,277.89 KB | 90,771.85 | 13,126.29 KB | 1.14 | 1.47 |
| 10,000 | 238,897.65 | 37,675.20 KB | 186,208.80 | 24,947.67 KB | 1.32 | 1.51 |

> TurboHTTP streaming is ~7-32% slower than HttpClient concurrent and allocates ~50% more memory.
> This is a key target for Feature 030 optimizations.

---

## Key Observations (Pre-optimization)

1. **Single request latency:** TurboHTTP is ~1.5-2x slower than HttpClient for single requests (expected — Akka.Streams pipeline overhead).
2. **HTTP/1.1 concurrent scaling:** TurboHTTP scales better than HttpClient at CL >= 16, overtaking it by CL=64.
3. **HTTP/2.0 concurrent:** TurboHTTP has a major bottleneck — 3-4x slower than HttpClient at CL >= 16. This is the primary target for optimizations.
4. **Memory:** TurboHTTP allocates ~1.5-1.8x more for light requests, roughly equal for heavy requests at high concurrency.
5. **Streaming throughput:** TurboHTTP streaming is 7-32% slower with 47-52% more allocations — pipeline overhead is the target.

---

## Full Raw Data

The complete BDN output is saved in `docs/benchmarks/baseline_030_raw.txt`.

### All 166 benchmark parameter combinations

<details>
<summary>Click to expand full results table</summary>

| Type | Method | CL | PayloadType | HttpVersion | RequestCount | Median (us) | Allocated |
|------|--------|----|-------------|-------------|-------------:|------------:|----------:|
| HttpClientConcurrent | ConcurrentRequests_Light | 1 | heavy | 1.1 | - | 85.75 | 2.63 KB |
| HttpClientSingle | SingleRequest_Light | 1 | heavy | 1.1 | - | 87.93 | 2.63 KB |
| TurboHttpConcurrent | ConcurrentRequests_Light | 1 | heavy | 1.1 | - | 169.22 | 8.90 KB |
| TurboHttpSingle | SingleRequest_Light | 1 | heavy | 1.1 | - | 167.64 | 4.89 KB |
| HttpClientConcurrent | ConcurrentRequests_Heavy | 1 | heavy | 1.1 | - | 111.94 | 40.60 KB |
| HttpClientSingle | SingleRequest_Heavy | 1 | heavy | 1.1 | - | 98.81 | 40.60 KB |
| TurboHttpConcurrent | ConcurrentRequests_Heavy | 1 | heavy | 1.1 | - | 174.00 | 42.89 KB |
| TurboHttpSingle | SingleRequest_Heavy | 1 | heavy | 1.1 | - | 251.14 | 42.89 KB |
| HttpClientConcurrent | ConcurrentRequests_Light | 1 | heavy | 2.0 | - | 124.28 | 3.30 KB |
| HttpClientSingle | SingleRequest_Light | 1 | heavy | 2.0 | - | 110.09 | 3.30 KB |
| TurboHttpConcurrent | ConcurrentRequests_Light | 1 | heavy | 2.0 | - | 195.47 | 5.97 KB |
| TurboHttpSingle | SingleRequest_Light | 1 | heavy | 2.0 | - | 214.93 | 5.97 KB |
| HttpClientConcurrent | ConcurrentRequests_Heavy | 1 | heavy | 2.0 | - | 152.49 | 42.63 KB |
| HttpClientSingle | SingleRequest_Heavy | 1 | heavy | 2.0 | - | 167.29 | 42.63 KB |
| TurboHttpConcurrent | ConcurrentRequests_Heavy | 1 | heavy | 2.0 | - | 189.59 | 16.25 KB |
| TurboHttpSingle | SingleRequest_Heavy | 1 | heavy | 2.0 | - | 205.51 | 16.25 KB |
| HttpClientConcurrent | ConcurrentRequests_Light | 1 | light | 1.1 | - | 84.28 | 2.63 KB |
| HttpClientSingle | SingleRequest_Light | 1 | light | 1.1 | - | 120.17 | 2.63 KB |
| TurboHttpConcurrent | ConcurrentRequests_Light | 1 | light | 1.1 | - | 165.24 | 6.92 KB |
| TurboHttpSingle | SingleRequest_Light | 1 | light | 1.1 | - | 171.31 | 4.89 KB |
| HttpClientConcurrent | ConcurrentRequests_Heavy | 1 | light | 1.1 | - | 110.07 | 40.60 KB |
| HttpClientSingle | SingleRequest_Heavy | 1 | light | 1.1 | - | 104.67 | 40.60 KB |
| TurboHttpConcurrent | ConcurrentRequests_Heavy | 1 | light | 1.1 | - | 184.45 | 44.93 KB |
| TurboHttpSingle | SingleRequest_Heavy | 1 | light | 1.1 | - | 183.16 | 42.89 KB |
| HttpClientConcurrent | ConcurrentRequests_Light | 1 | light | 2.0 | - | 117.67 | 3.43 KB |
| HttpClientSingle | SingleRequest_Light | 1 | light | 2.0 | - | 116.61 | 3.30 KB |
| TurboHttpConcurrent | ConcurrentRequests_Light | 1 | light | 2.0 | - | 232.03 | 5.97 KB |
| TurboHttpSingle | SingleRequest_Light | 1 | light | 2.0 | - | 197.81 | 5.97 KB |
| HttpClientConcurrent | ConcurrentRequests_Heavy | 1 | light | 2.0 | - | 177.76 | 42.65 KB |
| HttpClientSingle | SingleRequest_Heavy | 1 | light | 2.0 | - | 147.17 | 42.63 KB |
| TurboHttpConcurrent | ConcurrentRequests_Heavy | 1 | light | 2.0 | - | 203.98 | 16.25 KB |
| TurboHttpSingle | SingleRequest_Heavy | 1 | light | 2.0 | - | 191.71 | 16.25 KB |
| HttpClientConcurrent | ConcurrentRequests_Light | 4 | heavy | 1.1 | - | 125.93 | 10.41 KB |
| TurboHttpConcurrent | ConcurrentRequests_Light | 4 | heavy | 1.1 | - | 203.79 | 18.05 KB |
| HttpClientConcurrent | ConcurrentRequests_Heavy | 4 | heavy | 1.1 | - | 182.85 | 162.46 KB |
| TurboHttpConcurrent | ConcurrentRequests_Heavy | 4 | heavy | 1.1 | - | 221.49 | 153.56 KB |
| HttpClientConcurrent | ConcurrentRequests_Light | 4 | heavy | 2.0 | - | 149.70 | 14.90 KB |
| TurboHttpConcurrent | ConcurrentRequests_Light | 4 | heavy | 2.0 | - | 235.53 | 21.26 KB |
| HttpClientConcurrent | ConcurrentRequests_Heavy | 4 | heavy | 2.0 | - | 209.57 | 171.86 KB |
| TurboHttpConcurrent | ConcurrentRequests_Heavy | 4 | heavy | 2.0 | - | 269.03 | 62.38 KB |
| HttpClientConcurrent | ConcurrentRequests_Light | 4 | light | 1.1 | - | 137.34 | 10.41 KB |
| TurboHttpConcurrent | ConcurrentRequests_Light | 4 | light | 1.1 | - | 205.52 | 18.07 KB |
| HttpClientConcurrent | ConcurrentRequests_Heavy | 4 | light | 1.1 | - | 200.63 | 162.49 KB |
| TurboHttpConcurrent | ConcurrentRequests_Heavy | 4 | light | 1.1 | - | 225.37 | 153.06 KB |
| HttpClientConcurrent | ConcurrentRequests_Light | 4 | light | 2.0 | - | 145.42 | 13.07 KB |
| TurboHttpConcurrent | ConcurrentRequests_Light | 4 | light | 2.0 | - | 227.46 | 21.31 KB |
| HttpClientConcurrent | ConcurrentRequests_Heavy | 4 | light | 2.0 | - | 195.97 | 171.83 KB |
| TurboHttpConcurrent | ConcurrentRequests_Heavy | 4 | light | 2.0 | - | 233.62 | 62.61 KB |
| HttpClientConcurrent | ConcurrentRequests_Light | 16 | heavy | 1.1 | - | 316.06 | 41.26 KB |
| TurboHttpConcurrent | ConcurrentRequests_Light | 16 | heavy | 1.1 | - | 340.06 | 71.14 KB |
| HttpClientConcurrent | ConcurrentRequests_Heavy | 16 | heavy | 1.1 | - | 537.10 | 651.91 KB |
| TurboHttpConcurrent | ConcurrentRequests_Heavy | 16 | heavy | 1.1 | - | 382.91 | 623.33 KB |
| HttpClientConcurrent | ConcurrentRequests_Light | 16 | heavy | 2.0 | - | 188.28 | 52.41 KB |
| TurboHttpConcurrent | ConcurrentRequests_Light | 16 | heavy | 2.0 | - | 1,138.47 | 83.00 KB |
| HttpClientConcurrent | ConcurrentRequests_Heavy | 16 | heavy | 2.0 | - | 730.06 | 693.44 KB |
| TurboHttpConcurrent | ConcurrentRequests_Heavy | 16 | heavy | 2.0 | - | 1,468.55 | 255.50 KB |
| HttpClientConcurrent | ConcurrentRequests_Light | 16 | light | 1.1 | - | 348.43 | 41.26 KB |
| TurboHttpConcurrent | ConcurrentRequests_Light | 16 | light | 1.1 | - | 321.25 | 71.15 KB |
| HttpClientConcurrent | ConcurrentRequests_Heavy | 16 | light | 1.1 | - | 587.38 | 651.58 KB |
| TurboHttpConcurrent | ConcurrentRequests_Heavy | 16 | light | 1.1 | - | 377.48 | 630.34 KB |
| HttpClientConcurrent | ConcurrentRequests_Light | 16 | light | 2.0 | - | 172.31 | 51.93 KB |
| TurboHttpConcurrent | ConcurrentRequests_Light | 16 | light | 2.0 | - | 2,311.59 | 87.17 KB |
| HttpClientConcurrent | ConcurrentRequests_Heavy | 16 | light | 2.0 | - | 651.52 | 693.18 KB |
| TurboHttpConcurrent | ConcurrentRequests_Heavy | 16 | light | 2.0 | - | 1,755.42 | 248.58 KB |
| HttpClientConcurrent | ConcurrentRequests_Light | 64 | heavy | 1.1 | - | 1,125.35 | 164.71 KB |
| TurboHttpConcurrent | ConcurrentRequests_Light | 64 | heavy | 1.1 | - | 1,029.17 | 287.64 KB |
| HttpClientConcurrent | ConcurrentRequests_Heavy | 64 | heavy | 1.1 | - | 1,939.10 | 2,609.68 KB |
| TurboHttpConcurrent | ConcurrentRequests_Heavy | 64 | heavy | 1.1 | - | 1,348.77 | 2,663.61 KB |
| HttpClientConcurrent | ConcurrentRequests_Light | 64 | heavy | 2.0 | - | 984.99 | 207.57 KB |
| TurboHttpConcurrent | ConcurrentRequests_Light | 64 | heavy | 2.0 | - | 2,335.86 | 339.88 KB |
| HttpClientConcurrent | ConcurrentRequests_Heavy | 64 | heavy | 2.0 | - | 1,384.37 | 2,775.18 KB |
| TurboHttpConcurrent | ConcurrentRequests_Heavy | 64 | heavy | 2.0 | - | 2,871.44 | 1,041.11 KB |
| HttpClientConcurrent | ConcurrentRequests_Light | 64 | light | 1.1 | - | 1,135.34 | 164.71 KB |
| TurboHttpConcurrent | ConcurrentRequests_Light | 64 | light | 1.1 | - | 1,021.27 | 287.08 KB |
| HttpClientConcurrent | ConcurrentRequests_Heavy | 64 | light | 1.1 | - | 1,950.21 | 2,606.74 KB |
| TurboHttpConcurrent | ConcurrentRequests_Heavy | 64 | light | 1.1 | - | 1,289.86 | 2,624.68 KB |
| HttpClientConcurrent | ConcurrentRequests_Light | 64 | light | 2.0 | - | 1,027.94 | 207.29 KB |
| TurboHttpConcurrent | ConcurrentRequests_Light | 64 | light | 2.0 | - | 2,889.48 | 336.69 KB |
| HttpClientConcurrent | ConcurrentRequests_Heavy | 64 | light | 2.0 | - | 2,123.52 | 2,775.42 KB |
| TurboHttpConcurrent | ConcurrentRequests_Heavy | 64 | light | 2.0 | - | 3,051.51 | 1,055.39 KB |
| HttpClientConcurrent | ConcurrentRequests_Light | 256 | heavy | 1.1 | - | 4,901.45 | 664.12 KB |
| TurboHttpConcurrent | ConcurrentRequests_Light | 256 | heavy | 1.1 | - | 4,454.77 | 1,206.11 KB |
| HttpClientConcurrent | ConcurrentRequests_Heavy | 256 | heavy | 1.1 | - | 7,252.15 | 10,463.02 KB |
| TurboHttpConcurrent | ConcurrentRequests_Heavy | 256 | heavy | 1.1 | - | 7,465.15 | 10,849.50 KB |
| HttpClientConcurrent | ConcurrentRequests_Light | 256 | heavy | 2.0 | - | 2,684.75 | 828.55 KB |
| TurboHttpConcurrent | ConcurrentRequests_Light | 256 | heavy | 2.0 | - | 7,840.33 | 1,451.05 KB |
| HttpClientConcurrent | ConcurrentRequests_Heavy | 256 | heavy | 2.0 | - | 4,077.11 | 11,141.39 KB |
| TurboHttpConcurrent | ConcurrentRequests_Heavy | 256 | heavy | 2.0 | - | 9,264.24 | 4,128.70 KB |
| HttpClientConcurrent | ConcurrentRequests_Light | 256 | light | 1.1 | - | 5,474.34 | 657.94 KB |
| TurboHttpConcurrent | ConcurrentRequests_Light | 256 | light | 1.1 | - | 4,587.23 | 1,187.44 KB |
| HttpClientConcurrent | ConcurrentRequests_Heavy | 256 | light | 1.1 | - | 8,170.27 | 10,465.29 KB |
| TurboHttpConcurrent | ConcurrentRequests_Heavy | 256 | light | 1.1 | - | 7,153.95 | 10,792.88 KB |
| HttpClientConcurrent | ConcurrentRequests_Light | 256 | light | 2.0 | - | 2,674.07 | 828.67 KB |
| TurboHttpConcurrent | ConcurrentRequests_Light | 256 | light | 2.0 | - | 7,967.87 | 1,437.49 KB |
| HttpClientConcurrent | ConcurrentRequests_Heavy | 256 | light | 2.0 | - | 4,056.71 | 11,144.24 KB |
| TurboHttpConcurrent | ConcurrentRequests_Heavy | 256 | light | 2.0 | - | 9,613.63 | 4,217.59 KB |
| Streaming | TurboHttp_Streaming | - | - | 1.1 | 1000 | 18,424.75 | 3,795.08 KB |
| Streaming | HttpClient_Concurrent | - | - | 1.1 | 1000 | 17,189.85 | 2,492.51 KB |
| Streaming | TurboHttp_Streaming | - | - | 1.1 | 5000 | 94,689.50 | 19,277.89 KB |
| Streaming | HttpClient_Concurrent | - | - | 1.1 | 5000 | 90,771.85 | 13,126.29 KB |
| Streaming | TurboHttp_Streaming | - | - | 1.1 | 10000 | 238,897.65 | 37,675.20 KB |
| Streaming | HttpClient_Concurrent | - | - | 1.1 | 10000 | 186,208.80 | 24,947.67 KB |

</details>

---

## Notes

- The `BenchmarkComparisonReport.GenerateReport()` threw an `ArgumentException` (duplicate key) after all 166 benchmarks completed. This is a bug in the report generator, not the benchmarks themselves.
- BDN warnings indicate some iterations are below 100ms minimum recommended time for low-CL single request tests.
- PayloadType parameter does not affect benchmark behavior for Light methods (only Heavy methods use a body), but it is part of the parameter matrix — results for different PayloadType values on Light methods are equivalent noise.
