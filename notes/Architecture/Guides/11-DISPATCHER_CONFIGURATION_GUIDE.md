---
title: Dispatcher Configuration Implementation Guide
date: '2026-04-03'
status: ready-to-implement
tags:
  - implementation
  - configuration
  - akka-streams
  - threading
related:
  - Architecture/Design/10-DISPATCHER_SELECTION_ANALYSIS.md
---
# Dispatcher Configuration Implementation Guide

## Quick Reference: Akka.NET Dispatchers for TurboHTTP

### The Problem
TurboHTTP's HTTP/2 multiplexing with 64+ concurrent requests causes .NET ThreadPool contention, leading to deadlocks in BenchmarkDotNet processes. The default dispatcher routes all actor work through the shared global ThreadPool, which also handles application async/await continuations.

### The Solution
**Use ChannelExecutor dispatcher** (available in Akka.NET 1.5.x).

ChannelExecutor:
- Runs on the .NET ThreadPool but dynamically scales it
- Reduces idle thread count while maintaining performance
- Proven faster than ForkJoinDispatcher in benchmarks
- Eliminates ThreadPool starvation issues
- Works well in cloud/containerized environments

---

## Configuration Options

### Option 1: Global Default (Recommended for TurboHTTP)

Apply ChannelExecutor as the system-wide default dispatcher:

```hocon
akka {
    actor.default-dispatcher = {
        executor = channel-executor
        throughput = 30
        fork-join-executor {
            parallelism-min = 2
            parallelism-factor = 2.0
            parallelism-max = 128
        }
    }
}
```

**Parameters:**
- `executor = channel-executor` — Use ChannelExecutor instead of ThreadPool
- `throughput = 30` — Process 30 messages per actor before yielding (lower = more responsive, higher = better throughput)
- `parallelism-min = 2` — Minimum thread pool threads (keep low to reduce startup overhead)
- `parallelism-factor = 2.0` — Multiply logical core count (e.g., 8 cores × 2.0 = 16 threads)
- `parallelism-max = 128` — Hard limit on threads (cap at expected max concurrent load)

---

### Option 2: Production ASP.NET Core

For applications running in ASP.NET Core with ThreadPool already in use:

```hocon
akka {
    actor {
        default-dispatcher = {
            executor = channel-executor
            throughput = 20  # More responsive due to app code also needing ThreadPool
            fork-join-executor {
                parallelism-min = 2
                parallelism-factor = 1.0  # Exactly 1x core count
                parallelism-max = 64
            }
        }
    }
}
```

Reasoning: Conservative parallelism settings since ASP.NET Core also needs ThreadPool threads.

---

### Option 3: High-Throughput Streaming (Benchmarks/Load Tests)

For maximum throughput in controlled benchmarking environments:

```hocon
akka {
    actor {
        default-dispatcher = {
            executor = channel-executor
            throughput = 50  # Higher throughput prioritized over latency
            fork-join-executor {
                parallelism-min = 1
                parallelism-factor = 2.0
                parallelism-max = 256
            }
        }
    }
}
```

---

### Option 4: ForkJoinDispatcher (If Maximum Predictability Needed)

If you need guaranteed latency instead of dynamic scaling:

```hocon
akka {
    actor {
        default-dispatcher = {
            type = ForkJoinDispatcher
            throughput = 30
            dedicated-thread-pool {
                thread-count = 32
                deadlock-timeout = 10s
                threadtype = background
            }
        }
    }
}
```

**Trade-offs:**
- ✓ Eliminates ThreadPool variance entirely
- ✓ Predictable latency
- ✗ Higher memory usage (dedicated threads always running)
- ✗ Worse in cloud/containerized environments

---

## Implementation Steps for TurboHTTP

### Step 1: Update TurboClientServiceCollectionExtensions.cs

```csharp
// File: /src/TurboHTTP/TurboClientServiceCollectionExtensions.cs

private static readonly Config LoggingHocon = ConfigurationFactory.ParseString(
    """
    akka.loggers = ["Akka.Hosting.Logging.LoggerFactoryLogger, Akka.Hosting"]
    akka.actor.default-dispatcher = {
        executor = channel-executor
        throughput = 30
        fork-join-executor {
            parallelism-min = 2
            parallelism-factor = 2.0
            parallelism-max = 128
        }
    }
    """);
```

### Step 2: Update Benchmark Configuration

```csharp
// File: /src/TurboHTTP.Benchmarks/StreamingThroughputBenchmarks.cs

private static readonly Config BenchHocon = ConfigurationFactory.ParseString(
    """
    akka.actor.default-dispatcher = {
        executor = channel-executor
        throughput = 30
        fork-join-executor {
            parallelism-min = 2
            parallelism-factor = 2.0
            parallelism-max = 128
        }
    }
    """);
```

### Step 3: Run Validation Tests

```bash
# Run benchmarks to confirm no deadlocks/hangs
dotnet run --configuration Release --project src/TurboHTTP.Benchmarks/TurboHTTP.Benchmarks.csproj

# Run integration tests
dotnet test --project src/TurboHTTP.IntegrationTests/TurboHTTP.IntegrationTests.csproj

# Run stream tests
dotnet test --project src/TurboHTTP.StreamTests/TurboHTTP.StreamTests.csproj
```

### Step 4: Performance Validation

Check that:
- No timeouts or deadlocks occur
- Throughput improves (compare before/after benchmark results)
- Memory usage is reasonable
- CPU utilization is stable

---

## Parameter Tuning Guide

### throughput

Controls how many messages an actor processes before yielding to other actors.

```
throughput = N  # Process N messages, then yield
```

**Tuning:**
- `throughput = 10-20` → More responsive (fair scheduling, higher context switches)
- `throughput = 30-50` → Balanced (default sweet spot)
- `throughput = 100+` → Higher throughput (less fair, possible starvation)

For HTTP/2: Use `30-50` for balanced latency/throughput.

### parallelism-factor

Multiplies logical core count to determine max threads in the pool.

```
parallelism-factor = 1.0  # 1x core count
parallelism-factor = 2.0  # 2x core count
```

**Tuning:**
- `1.0` → Conservative (one thread per core) — good for CPU-bound work
- `2.0` → Recommended for I/O-heavy (network requests) — one extra thread per core for I/O wait
- `4.0+` → Only if many blocking operations expected

For HTTP/2: Use `2.0` (each core can handle one network I/O wait).

### parallelism-max

Hard limit on total threads the dispatcher can spawn.

```
parallelism-max = N  # Never exceed N threads
```

**Tuning:**
- Should be `2x * logical_core_count` at minimum
- Set to expected max concurrent actors
- For 64 concurrent HTTP/2 requests: use `128-256`

---

## Dispatcher Selection Decision Tree

```
Does your application share the .NET ThreadPool?
├─ YES (ASP.NET Core, background services, etc.)
│  └─ Use: ChannelExecutor ✓ (Option 2)
│
└─ NO (Standalone/benchmarking)
   ├─ Need maximum throughput?
   │  └─ YES → Use: ChannelExecutor (Option 3) ✓
   │
   └─ Need predictable latency (< 1ms variance)?
      └─ YES → Use: ForkJoinDispatcher (Option 4)
      └─ NO → Use: ChannelExecutor (Option 1) ✓
```

---

## Performance Expectations

### Before (Default ThreadPoolDispatcher)
- ThreadPool contention under 64+ concurrent requests
- Possible deadlocks in BenchmarkDotNet
- Unpredictable latency spikes
- High context-switch overhead

### After (ChannelExecutor)
- Minimal ThreadPool contention (dynamic scaling)
- No deadlocks
- Stable latency across all concurrency levels
- Reduced idle CPU
- 5-10% throughput improvement (proven in Akka benchmarks)

---

## Monitoring the Dispatcher

### Check Active Thread Count

```csharp
// Get current thread count info
var stats = ThreadPool.GetAvailableThreads(out int completionThreads, out _);
Console.WriteLine($"Available: {stats}, Completion: {completionThreads}");
```

### Expected Behavior with ChannelExecutor

Under load:
- Thread count should increase dynamically
- Idle time should show significant reduction
- No starvation of application threads

### Verify Configuration

```csharp
// Log Akka configuration
var system = ActorSystem.Create("test");
Console.WriteLine(system.Settings.Config);  // Prints full HOCON config
```

Should show:
```
akka.actor.default-dispatcher.executor = channel-executor
```

---

## Troubleshooting

### Issue: Still seeing deadlocks

**Causes:**
- Configuration not applied (check ActorSystem creation code)
- Blocking calls within actors (violates actor model)
- Insufficient `parallelism-max` for actual concurrency

**Solution:**
- Verify config with `system.Settings.Config`
- Audit actor code for blocking operations (`.Result`, `.Wait()`)
- Increase `parallelism-max` if hitting the limit

### Issue: Memory usage increased

**Causes:**
- `parallelism-factor` too high
- `parallelism-max` exceeds available system memory

**Solution:**
- Reduce `parallelism-factor` to 1.0
- Lower `parallelism-max` if memory-constrained

### Issue: Latency worse than before

**Causes:**
- `throughput` too high (thread context switch reduced unfairly)
- ChannelExecutor dynamic scaling thrashing (scale up/down rapidly)

**Solution:**
- Lower `throughput` to 15-20
- Stabilize `parallelism-min` to prevent scale-thrashing

---

## References

- [[10-DISPATCHER_SELECTION_ANALYSIS.md]] — Full comparison of all dispatcher types
- [Official Akka.NET Docs](https://getakka.net/articles/actors/dispatchers.html)
- Akka.NET GitHub: https://github.com/akkadotnet/akka.net

---

## Checklist: Before Committing

- [ ] Configuration applied to ActorSystem bootstrap
- [ ] No compilation errors
- [ ] Benchmarks run without deadlocks/timeouts
- [ ] Integration tests pass
- [ ] Memory usage validated
- [ ] Throughput improved or maintained
- [ ] Documentation updated (CLAUDE.md)
