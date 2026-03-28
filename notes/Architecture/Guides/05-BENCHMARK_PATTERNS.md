---
title: Benchmark Patterns & Infrastructure
description: >-
  BenchmarkDotNet conventions, port assignments, Windows TCP TIME_WAIT
  workarounds, thread safety rules for concurrent benchmarks
tags:
  - benchmarks
  - performance
  - infrastructure
  - tcp
aliases:
  - Benchmark Patterns
  - BDN Patterns
---
# Benchmark Patterns & Infrastructure

**Last Updated**: 2026-03-26

## BenchmarkDotNet Conventions

Standard attributes for TurboHttp benchmarks:
```csharp
[MemoryDiagnoser]
[Config(typeof(MicroBenchmarkConfig))]
[SimpleJob(warmupCount: 3, targetCount: 5)]
```

**Dry-run command:**
```bash
dotnet run --configuration Release --project src/TurboHttp.Benchmarks/... -- --filter "*ClassName*" --job dry
```

**Key**: BDN runs each benchmark×job in a separate child process; each child calls `GlobalSetup → benchmark → GlobalCleanup`.

---

## Port Assignments

| Benchmark File | Port |
|----------------|------|
| CoreRequestBenchmarks | 5006 |
| CoreMemoryBenchmarks | 5007 |
| CoreConnectionBenchmarks | 5008 |
| Http11EfficiencyBenchmarks | 5009 |
| ConcurrencyScalingBenchmarks | dynamic (port 0) |
| BurstTrafficBenchmarks | dynamic (port 0) |
| FailureRecoveryBenchmarks | dynamic (port 0) |

---

## Windows TCP TIME_WAIT & Ephemeral Port Exhaustion

- Windows has ~16,384 ephemeral ports (49152–65535)
- TIME_WAIT lasts 120s by default; each closed connection blocks `(src_ip:src_port, dst_ip:dst_port)`
- BDN pilot phase doubles `invocationCount` until iteration ≥ 500ms → can generate thousands of connections
- **Formula**: `total_connections = (pilot_invocations + warmupCount × invocationCount + targetCount × invocationCount) × conns_per_invocation`
- For a 300µs operation: `invocationCount ≈ 2048`, giving ~20,000 total connections → **exhausts 16,384 limit**

### Solutions

1. **Pre-established connection pool**: `GlobalSetup` creates N keep-alive connections; benchmarks reuse them — zero new connections per pilot invocation
2. **Dynamic port**: `web.UseUrls("http://127.0.0.1:0")` then discover via:
   ```csharp
   _server.Services.GetRequiredService<IServer>()
       .Features.Get<IServerAddressesFeature>()!
   ```
   Requires: `Microsoft.AspNetCore.Hosting.Server`, `Microsoft.AspNetCore.Hosting.Server.Features`, `Microsoft.Extensions.DependencyInjection`, `System.Linq`
3. **invocationCount cap**: `[SimpleJob(warmupCount:3, targetCount:5, invocationCount:16)]` — bypasses pilot, caps total connections

---

## Thread Safety in Concurrent Benchmarks

**Rule**: Never use class-level `_encBuf`/`_readBuf` fields in methods called concurrently.

**Why**: BDN may run benchmark methods in parallel across threads.

**Fix**: Use local buffers per call:
```csharp
var encBuf = new byte[512];
var readBuf = new byte[2048];
```
