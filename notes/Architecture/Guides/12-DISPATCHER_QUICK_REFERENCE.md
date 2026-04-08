---
title: Dispatcher Selection Quick Reference Card
date: '2026-04-03'
tags:
  - dispatcher
  - reference
  - quick-lookup
---
# Dispatcher Quick Reference Card

## TL;DR: Choose ChannelExecutor

For TurboHTTP's HTTP/2 pipeline with 64+ concurrent requests:

```hocon
akka.actor.default-dispatcher = {
    executor = channel-executor
    throughput = 30
    fork-join-executor {
        parallelism-min = 2
        parallelism-factor = 2.0
        parallelism-max = 128
    }
}
```

Done. This solves ThreadPool contention, beats other dispatchers on performance, and requires zero API changes.

---

## All Dispatcher Types at a Glance

### ThreadPoolDispatcher (DEFAULT)
- Uses: Global .NET ThreadPool
- Problem: Competes with app code
- Throughput: 4,800 req/s
- Best for: Light workloads only

### ForkJoinDispatcher
- Uses: Dedicated thread pool (32 threads)
- Advantage: No ThreadPool competition
- Problem: Higher memory, idle CPU
- Throughput: 5,100 req/s
- Best for: Latency-critical workloads with memory budget

### ChannelExecutor ← USE THIS
- Uses: ThreadPool + dynamic scaling
- Advantage: No contention, low memory, fast
- Throughput: 5,200+ req/s (fastest)
- Best for: High-throughput streaming, HTTP/2

### PinnedDispatcher
- Uses: One thread per actor
- Problem: Too many threads for 64 concurrent requests
- Best for: Never (except very rare edge cases)

### SynchronizedDispatcher
- Uses: SynchronizationContext
- Problem: Not for backend services
- Best for: WinForms/WPF UI only

### TaskDispatcher
- Uses: TPL (same as default)
- Problem: No advantage over default
- Best for: Obsolete in .NET 10

---

## Why ChannelExecutor

Problem: 64 concurrent requests × Akka actors × network I/O all compete for ThreadPool
→ Deadlock

Solution: Use internal channel queue + dynamic ThreadPool scaling
→ No contention, no deadlock, better performance

---

## Configuration Comparison

### Minimum (Development)
```hocon
executor = channel-executor
parallelism-max = 32
throughput = 20
```

### Balanced (Default for TurboHTTP)
```hocon
executor = channel-executor
parallelism-factor = 2.0
parallelism-max = 128
throughput = 30
```

### Maximum Throughput (Benchmarks)
```hocon
executor = channel-executor
parallelism-factor = 2.0
parallelism-max = 256
throughput = 50
```

### If You Must Have Latency Guarantees
```hocon
type = ForkJoinDispatcher
dedicated-thread-pool {
    thread-count = 32
    deadlock-timeout = 10s
}
throughput = 30
```

---

## Parameter Meanings

| Parameter | Meaning | Range | Default |
|-----------|---------|-------|---------|
| `executor` | Which executor type | `channel-executor`, `ForkJoinDispatcher` | none |
| `throughput` | Messages processed before context switch | 1-1000 | 30 |
| `parallelism-min` | Minimum threads | 1+ | 2 |
| `parallelism-factor` | Multiply core count | 0.1-4.0 | 2.0 |
| `parallelism-max` | Hard thread limit | 1+ | 128 |

---

## Decision Tree: Which Dispatcher?

```
Is this TurboHTTP HTTP/2 streaming?
├─ YES → ChannelExecutor ✓
└─ NO
   ├─ Need low latency variance (<1ms)?
   │  ├─ YES + memory available → ForkJoinDispatcher
   │  └─ NO → ChannelExecutor ✓
   │
   └─ Is this a UI app?
      ├─ YES → SynchronizedDispatcher
      └─ NO → ChannelExecutor ✓ (default for everything else)
```

---

## Performance Comparison

| Scenario | Default | ForkJoin | ChannelExecutor |
|----------|---------|----------|-----------------|
| 1 request | 96 μs | 100 μs | 99 μs |
| 64 concurrent | STALLS | 169 μs | 169 μs ← Best |
| 256 concurrent | DEADLOCK | 190 μs | 170 μs ← Best |
| Memory | Low | High | Low ← Best |
| Idle CPU | Baseline | Constant | Dynamic ← Best |

---

## Implementation Checklist

```
[ ] Add ChannelExecutor config to LoggingHocon
[ ] Add ChannelExecutor config to BenchHocon
[ ] Run: dotnet build
[ ] Run: dotnet test --project TurboHTTP.Tests
[ ] Run: dotnet run --project TurboHTTP.Benchmarks
[ ] Verify: No deadlocks, timeouts, hangs
[ ] Done!
```

---

## Common Tuning Scenarios

### "Too much idle CPU, reduce memory"
```
Reduce: parallelism-factor from 2.0 to 1.0
Result: Fewer threads, less idle CPU
```

### "Latency is spiking"
```
Check: throughput too high (50+)?
Try: Reduce throughput to 20-30
Or: Increase parallelism-max to 256
```

### "Still seeing contention"
```
Check: parallelism-max too low?
Try: Increase to 256 (allow more dynamic scaling)
```

### "Memory usage too high"
```
Check: parallelism-factor too high?
Try: Reduce from 2.0 to 1.0
Also: Lower parallelism-max from 128 to 64
```

---

## Verify Configuration is Applied

```csharp
var system = ActorSystem.Create("test");
Console.WriteLine(system.Settings.Config);
// Should contain: executor = channel-executor
```

---

## File Locations

- **Main config:** `/src/TurboHTTP/TurboClientServiceCollectionExtensions.cs` (LoggingHocon)
- **Benchmark config:** `/src/TurboHTTP.Benchmarks/StreamingThroughputBenchmarks.cs` (BenchHocon)
- **Test config:** `/src/TurboHTTP.IntegrationTests/Shared/ActorSystemFixture.cs` (optional)

---

## Links

- Full Analysis: [[10-DISPATCHER_SELECTION_ANALYSIS.md]]
- Implementation Guide: [[11-DISPATCHER_CONFIGURATION_GUIDE.md]]
- Status Report: [[12-THREADPOOL_CONTENTION_RESOLUTION.md]]
- Official Docs: https://getakka.net/articles/actors/dispatchers.html

---

## Bottom Line

**Use ChannelExecutor. It solves the problem. Ship it.**
