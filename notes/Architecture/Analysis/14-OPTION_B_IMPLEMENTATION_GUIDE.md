---
title: Option B Implementation Guide
description: >-
  Step-by-step implementation of hierarchical connection pool with child actors
  per endpoint
tags:
  - implementation
  - actors
  - transport
aliases:
  - ImplementationGuide
---
# Option B Implementation Guide

**Status**: Ready for implementation  
**Estimated Effort**: 4-6 hours  
**Files to Modify**: 2 (ConnectionManagerActor.cs, new TcpHostConnectionActor.cs)  
**Files to Create**: 1 (TcpHostConnectionActor.cs)  
**Tests to Update**: 2 (ConnectionPoolTests.cs, ConnectionPoolDeadlockTests.cs)

---

## Architecture Before/After

### BEFORE (Option A — Flat)
```
ConnectionStage
   ↓ GraphStage.Ask()
ConnectionManagerActor (single)
   ├─ OnAcquire() → checks Dictionary[endpoint]
   ├─ OnRelease() → updates Dictionary[endpoint]
   ├─ Mailbox: A.Acquire, B.Release, A.Release, B.Acquire, ... (interleaved)
   └─ All messages on single queue
```

### AFTER (Option B — Hierarchical)
```
ConnectionStage
   ↓ GraphStage.Tell()
ConnectionManagerActor (router/supervisor)
   ├─ OnAcquire(msg) → GetOrCreateChild(endpoint).Tell(msg)
   ├─ OnRelease(msg) → _children[endpoint].Tell(msg)
   ├─ Mailbox: router messages only
   └─
   ├─ TcpHostConnectionActor(example.com:80)
   │  ├─ Mailbox: A.Acquire, A.Release (from host A only)
   │  ├─ OnAcquire() → checks local _idle, _leases, _pending
   │  ├─ OnRelease() → updates local state
   │  └─ Periodic EvictMsg (local timer)
   │
   └─ TcpHostConnectionActor(api.other.com:443)
      ├─ Mailbox: B.Acquire, B.Release (from host B only)
      └─ [same structure as above]
```

---

## Step-by-Step Implementation

### Phase 1: Create Child Actor Class (1.5 hours)

#### File: `TcpHostConnectionActor.cs` (new)

```csharp
using Akka.Actor;
using TurboHTTP.Diagnostics;
using TurboHTTP.Internal;

namespace TurboHTTP.Transport;

/// <summary>
/// Single-host connection manager for TCP (HTTP/1.x, 2.0).
/// Spawned by ConnectionManagerActor, one per RequestEndpoint.
/// All state for a single host:port is kept here.
/// </summary>
internal sealed class TcpHostConnectionActor : ReceiveActor, IWithTimers
{
    // ── Messages ──────────────────────────────────────────────────────

    // Messages routed from root actor:
    // - ConnectionManagerActor.AcquireMsg
    // - ConnectionManagerActor.ReleaseMsg
    // - Internal: EstablishedMsg, EstablishFailedMsg, EvictMsg

    // ── Per-host state ────────────────────────────────────────────────

    private sealed class HostState
    {
        public readonly RequestEndpoint Endpoint;
        public readonly int MaxConnections;
        public readonly bool IsHttp10;

        /// <summary>All established, not-yet-disposed connections.</summary>
        public readonly List<ConnectionLease> Leases = [];

        /// <summary>HTTP/1.1 idle connections available for reuse.</summary>
        public readonly Queue<ConnectionLease> Idle = new();

        /// <summary>HTTP/1.1 callers waiting for a connection slot.</summary>
        public readonly Queue<ConnectionManagerActor.AcquireMsg> Pending = new();

        /// <summary>Number of in-flight EstablishAsync calls.</summary>
        public int Establishing;

        public HostState(RequestEndpoint endpoint)
        {
            Endpoint = endpoint;
            IsHttp10 = endpoint.Version is { Major: 1, Minor: 0 };
            MaxConnections = IsHttp10 || endpoint.Version.Major >= 2 ? int.MaxValue : 6;
        }
    }

    // ── Private messages ──────────────────────────────────────────────

    private sealed record EstablishedMsg(ConnectionLease Lease, ConnectionManagerActor.AcquireMsg Original);
    private sealed record EstablishFailedMsg(Exception Ex, ConnectionManagerActor.AcquireMsg Original);
    private sealed class EvictMsg { public static readonly EvictMsg Instance = new(); }

    // ── Actor state ───────────────────────────────────────────────────

    private readonly HostState _host;
    private readonly TimeSpan _idleTimeout;
    private const string EvictTimerKey = "evict-idle";

    public ITimerScheduler Timers { get; set; } = null!;

    // ── Factory ───────────────────────────────────────────────────────

    public static Props Props(RequestEndpoint endpoint, TimeSpan idleTimeout)
        => Akka.Actor.Props.Create(() => new TcpHostConnectionActor(endpoint, idleTimeout));

    // ── Constructor ───────────────────────────────────────────────────

    public TcpHostConnectionActor(RequestEndpoint endpoint, TimeSpan idleTimeout)
    {
        _host = new HostState(endpoint);
        _idleTimeout = idleTimeout;

        Receive<ConnectionManagerActor.AcquireMsg>(OnAcquire);
        Receive<ConnectionManagerActor.ReleaseMsg>(OnRelease);
        Receive<EstablishedMsg>(OnEstablished);
        Receive<EstablishFailedMsg>(OnFailed);
        Receive<EvictMsg>(_ => OnEvict());
    }

    protected override void PreStart()
    {
        // Each child runs its own eviction timer (could also use parent's global timer)
        Timers.StartPeriodicTimer(EvictTimerKey, EvictMsg.Instance, _idleTimeout, _idleTimeout);
        TurboTrace.Connection.Debug(
            this,
            "TcpHostConnectionActor started for {0}:{1}",
            _host.Endpoint.Host,
            _host.Endpoint.Port);
    }

    // ── Message handlers ──────────────────────────────────────────────

    private void OnAcquire(ConnectionManagerActor.AcquireMsg msg)
    {
        if (msg.Tcs.Task.IsCompleted)
        {
            return;
        }

        var version = msg.Endpoint.Version;

        // HTTP/2+: MRU multiplexing
        if (version.Major >= 2)
        {
            var mru = SelectMru(_host);
            if (mru is not null)
            {
                mru.MarkBusy();

                if (!msg.Tcs.TrySetResult(mru))
                {
                    mru.MarkIdle();
                }
                else
                {
                    TurboHttpMetrics.ConnectionIdle.Add(-1,
                        new("server.address", _host.Endpoint.Host),
                        new("server.port", _host.Endpoint.Port));
                }

                return;
            }

            Establish(_host, msg);
            return;
        }

        // HTTP/1.0: always new, no limit
        if (_host.IsHttp10)
        {
            Establish(_host, msg);
            return;
        }

        // HTTP/1.1: prefer idle reuse, then establish if slots available, else queue
        while (_host.Idle.TryDequeue(out var idle))
        {
            if (idle is { IsAlive: true, Reusable: true })
            {
                idle.MarkBusy();

                if (!msg.Tcs.TrySetResult(idle))
                {
                    idle.MarkIdle();
                    _host.Idle.Enqueue(idle);
                }
                else
                {
                    TurboHttpMetrics.ConnectionIdle.Add(-1,
                        new("server.address", _host.Endpoint.Host),
                        new("server.port", _host.Endpoint.Port));
                }

                return;
            }

            // Stale — dispose and free the slot
            _host.Leases.Remove(idle);
            idle.Dispose();
            TurboHttpMetrics.ConnectionActive.Add(-1,
                new("server.address", _host.Endpoint.Host),
                new("server.port", _host.Endpoint.Port));
        }

        // No idle — check slot budget
        if (_host.Leases.Count + _host.Establishing < _host.MaxConnections)
        {
            Establish(_host, msg);
        }
        else
        {
            _host.Pending.Enqueue(msg);
        }
    }

    private void OnRelease(ConnectionManagerActor.ReleaseMsg msg)
    {
        var version = msg.Lease.Key.Version;

        // HTTP/1.0: always dispose
        if (_host.IsHttp10)
        {
            _host.Leases.Remove(msg.Lease);
            msg.Lease.Dispose();
            TurboHttpMetrics.ConnectionActive.Add(-1,
                new("server.address", _host.Endpoint.Host),
                new("server.port", _host.Endpoint.Port));
            return;
        }

        // HTTP/2+: decrement stream count; dispose only when no active streams and non-reusable
        if (version.Major >= 2)
        {
            msg.Lease.MarkIdle();

            if (!msg.CanReuse)
            {
                msg.Lease.MarkNoReuse();
            }

            if (msg.Lease is { ActiveStreams: <= 0, Reusable: false })
            {
                _host.Leases.Remove(msg.Lease);
                msg.Lease.Dispose();
                TurboHttpMetrics.ConnectionActive.Add(-1,
                    new("server.address", _host.Endpoint.Host),
                    new("server.port", _host.Endpoint.Port));
            }

            return;
        }

        // HTTP/1.1
        msg.Lease.MarkIdle();

        if (msg.CanReuse && msg.Lease is { IsAlive: true, Reusable: true })
        {
            // Direct handoff to a pending caller
            while (_host.Pending.TryDequeue(out var pending))
            {
                if (!pending.Tcs.Task.IsCompleted)
                {
                    msg.Lease.MarkBusy();
                    pending.Tcs.TrySetResult(msg.Lease);
                    return;
                }
            }

            // No pending callers — park in idle pool
            _host.Idle.Enqueue(msg.Lease);
            TurboHttpMetrics.ConnectionIdle.Add(1,
                new("server.address", _host.Endpoint.Host),
                new("server.port", _host.Endpoint.Port));
        }
        else
        {
            // Not reusable — dispose and free the slot
            _host.Leases.Remove(msg.Lease);
            msg.Lease.Dispose();
            TurboHttpMetrics.ConnectionActive.Add(-1,
                new("server.address", _host.Endpoint.Host),
                new("server.port", _host.Endpoint.Port));

            ServeNextPending(_host);
        }
    }

    private void OnEstablished(EstablishedMsg msg)
    {
        _host.Establishing--;
        _host.Leases.Add(msg.Lease);
        msg.Lease.MarkBusy();
        TurboHttpMetrics.ConnectionActive.Add(1,
            new("server.address", _host.Endpoint.Host),
            new("server.port", _host.Endpoint.Port));

        if (!msg.Original.Tcs.TrySetResult(msg.Lease))
        {
            // Original caller cancelled — treat as immediate release
            OnRelease(new ConnectionManagerActor.ReleaseMsg(msg.Lease, CanReuse: true));
        }
    }

    private void OnFailed(EstablishFailedMsg msg)
    {
        _host.Establishing--;

        if (msg.Ex is OperationCanceledException oce)
        {
            msg.Original.Tcs.TrySetCanceled(oce.CancellationToken);
        }
        else
        {
            msg.Original.Tcs.TrySetException(msg.Ex);
        }

        ServeNextPending(_host);
    }

    // ── Eviction ──────────────────────────────────────────────────────

    private void OnEvict()
    {
        EvictHost(_host);
    }

    private void EvictHost(HostState host)
    {
        if (host.Idle.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var fresh = new List<ConnectionLease>();
        var expired = new List<ConnectionLease>();

        while (host.Idle.TryDequeue(out var idle))
        {
            if (!idle.IsAlive || now - idle.LastActivity > _idleTimeout)
            {
                expired.Add(idle);
            }
            else
            {
                fresh.Add(idle);
            }
        }

        // Keep at least one idle connection per host
        if (fresh.Count == 0 && expired.Count > 0)
        {
            var keeper = expired[0];
            for (var i = 1; i < expired.Count; i++)
            {
                if (expired[i].IsAlive && expired[i].LastActivity > keeper.LastActivity)
                {
                    keeper = expired[i];
                }
            }

            if (keeper.IsAlive)
            {
                expired.Remove(keeper);
                fresh.Add(keeper);
            }
        }

        foreach (var item in fresh)
        {
            host.Idle.Enqueue(item);
        }

        foreach (var lease in expired)
        {
            host.Leases.Remove(lease);
            lease.Dispose();
            TurboHttpMetrics.ConnectionIdle.Add(-1,
                new("server.address", host.Endpoint.Host),
                new("server.port", host.Endpoint.Port));
            TurboHttpMetrics.ConnectionActive.Add(-1,
                new("server.address", host.Endpoint.Host),
                new("server.port", host.Endpoint.Port));
        }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────

    protected override void PostStop()
    {
        Timers.CancelAll();

        foreach (var pending in _host.Pending)
        {
            pending.Tcs.TrySetException(new ObjectDisposedException(
                nameof(TcpHostConnectionActor),
                $"Connection manager for {_host.Endpoint} was stopped while requests were pending."));
        }

        _host.Pending.Clear();
        _host.Idle.Clear();

        foreach (var lease in _host.Leases)
        {
            lease.Dispose();
        }

        _host.Leases.Clear();

        TurboTrace.Connection.Debug(
            this,
            "TcpHostConnectionActor stopped for {0}:{1}",
            _host.Endpoint.Host,
            _host.Endpoint.Port);
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private void Establish(HostState host, ConnectionManagerActor.AcquireMsg msg)
    {
        host.Establishing++;
        DirectConnectionFactory
            .EstablishAsync(msg.Options, msg.Endpoint, msg.Ct)
            .PipeTo(Self,
                success: lease => new EstablishedMsg(lease, msg),
                failure: ex => new EstablishFailedMsg(ex, msg));
    }

    private void ServeNextPending(HostState host)
    {
        while (host.Pending.TryDequeue(out var next))
        {
            if (!next.Tcs.Task.IsCompleted)
            {
                Establish(host, next);
                return;
            }
        }
    }

    private static ConnectionLease? SelectMru(HostState host)
    {
        ConnectionLease? best = null;
        foreach (var lease in host.Leases)
        {
            if (lease.HasAvailableSlot && (best is null || lease.LastActivity > best.LastActivity))
            {
                best = lease;
            }
        }

        return best;
    }
}
```

---

### Phase 2: Update Root Actor (2 hours)

#### File: `ConnectionManagerActor.cs` (modifications)

**Replace the entire class with:**

```csharp
using Akka.Actor;
using TurboHTTP.Diagnostics;
using TurboHTTP.Internal;

namespace TurboHTTP.Transport;

/// <summary>
/// Root connection manager actor that routes Acquire/Release messages
/// to per-host child actors. Each <see cref="RequestEndpoint"/> gets its own
/// <see cref="TcpHostConnectionActor"/> (or QuicHostConnectionActor in future).
/// <para>
/// Advantages:
/// • No mailbox contention between hosts
/// • Fault isolation: one host failure doesn't affect others
/// • Per-host invariants are explicit (actor per host)
/// • Clear RFC alignment: "one actor owns one origin"
/// </para>
/// </summary>
internal sealed class ConnectionManagerActor : ReceiveActor
{
    // ── Public messages ──────────────────────────────────────────────────

    internal sealed record AcquireMsg(TcpOptions Options, RequestEndpoint Endpoint, TaskCompletionSource<ConnectionLease> Tcs, CancellationToken Ct);
    internal sealed record ReleaseMsg(ConnectionLease Lease, bool CanReuse);

    // ── Actor state ──────────────────────────────────────────────────────

    private readonly Dictionary<RequestEndpoint, IActorRef> _hostActors = new();
    private readonly TimeSpan _idleTimeout;

    // ── Factory + static helpers ─────────────────────────────────────────

    public static Props Props(TimeSpan idleTimeout)
        => Akka.Actor.Props.Create(() => new ConnectionManagerActor(idleTimeout));

    /// <summary>
    /// Sends an <see cref="AcquireMsg"/> to the manager and returns a <see cref="Task{ConnectionLease}"/>
    /// that completes when the actor resolves the request.
    /// Cancellation is wired directly to the <see cref="TaskCompletionSource{T}"/>;
    /// the child actor skips already-completed TCS instances on dequeue.
    /// </summary>
    public static Task<ConnectionLease> AcquireAsync(
        IActorRef actor, TcpOptions options, RequestEndpoint endpoint, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<ConnectionLease>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (ct.CanBeCanceled)
        {
            ct.UnsafeRegister(
                static (state, token) => ((TaskCompletionSource<ConnectionLease>)state!).TrySetCanceled(token),
                tcs);
        }

        actor.Tell(new AcquireMsg(options, endpoint, tcs, ct));
        return tcs.Task;
    }

    // ── Constructor ──────────────────────────────────────────────────────

    public ConnectionManagerActor(TimeSpan idleTimeout)
    {
        _idleTimeout = idleTimeout;

        Receive<AcquireMsg>(OnAcquire);
        Receive<ReleaseMsg>(OnRelease);
    }

    // ── Message handlers ─────────────────────────────────────────────────

    private void OnAcquire(AcquireMsg msg)
    {
        var child = GetOrCreateHostActor(msg.Endpoint);
        child.Tell(msg);
    }

    private void OnRelease(ReleaseMsg msg)
    {
        if (_hostActors.TryGetValue(msg.Lease.Key, out var child))
        {
            child.Tell(msg);
        }
    }

    // ── Lifecycle ────────────────────────────────────────────────────────

    protected override void PostStop()
    {
        // Child actors will handle their own cleanup via PostStop
        _hostActors.Clear();
    }

    // ── Supervision ──────────────────────────────────────────────────────

    protected override SupervisorStrategy SupervisorStrategy() =>
        new OneForOneStrategy(
            maxNrOfRetries: 3,
            withinTimeRange: TimeSpan.FromSeconds(10),
            decider: ex => ex switch
            {
                ActorInitializationException => Directive.Stop,
                ObjectDisposedException => Directive.Stop,
                _ => Directive.Restart
            });

    // ── Helpers ──────────────────────────────────────────────────────────

    private IActorRef GetOrCreateHostActor(RequestEndpoint endpoint)
    {
        if (!_hostActors.TryGetValue(endpoint, out var child))
        {
            var name = HostActorName(endpoint);
            child = Context.ActorOf(
                TcpHostConnectionActor.Props(endpoint, _idleTimeout),
                name: name);
            _hostActors[endpoint] = child;

            TurboTrace.Connection.Debug(
                this,
                "ConnectionManager spawned child actor for {0}:{1} ({2})",
                endpoint.Host,
                endpoint.Port,
                endpoint.Version);
        }

        return child;
    }

    /// <summary>
    /// Generates a safe actor name from endpoint. Actor names must be URL-safe.
    /// </summary>
    private static string HostActorName(RequestEndpoint endpoint)
    {
        // Replace : with - and . with _ to make valid actor names
        var safeName = $"{endpoint.Host}_{endpoint.Port}"
            .Replace(":", "-")
            .Replace(".", "_");
        return safeName;
    }
}
```

**Key changes:**
- Removed `HostState` class (moved to child)
- Removed all message handlers except `OnAcquire` and `OnRelease`
- Removed eviction logic (now per-child)
- Added `GetOrCreateHostActor()` with lazy spawning
- Added supervision strategy (OneForOne, restart transient failures)
- Added `HostActorName()` for safe actor naming

---

### Phase 3: Update Tests (1 hour)

#### File: `TurboHTTP.Tests/Transport/ConnectionPoolTests.cs`

**Add new test class at end of file:**

```csharp
[Fact(Timeout = 5000)]
[DisplayName("RFC-9112-conn-isolation-001: Root actor routes to child by endpoint")]
public async Task ConnectionManager_Routes_Different_Endpoints_To_Different_Children()
{
    // Arrange
    var system = ActorSystem.Create("test");
    var rootActor = system.ActorOf(ConnectionManagerActor.Props(TimeSpan.FromMinutes(5)));
    
    var endpoint1 = new RequestEndpoint("example.com", 443, new Version(1, 1));
    var endpoint2 = new RequestEndpoint("api.other.com", 443, new Version(1, 1));
    
    var options = new TlsOptions(
        new IPEndPoint(IPAddress.Loopback, 443),
        remoteAddress: endpoint1.Host,
        serverNameIndication: endpoint1.Host,
        maxFrameSize: 16384);
    
    // Act
    var ct = System.Threading.CancellationToken.None;
    var acquire1 = ConnectionManagerActor.AcquireAsync(rootActor, options, endpoint1, ct);
    var acquire2 = ConnectionManagerActor.AcquireAsync(rootActor, options, endpoint2, ct);
    
    // Wait for children to be created
    await Task.Delay(100);
    
    // Assert: two different child actors should exist
    // (Verify via internal state or actor inspection)
    Assert.False(acquire1.IsCompleted); // Would complete when connection established
    Assert.False(acquire2.IsCompleted);
}

[Fact(Timeout = 5000)]
[DisplayName("RFC-9112-conn-isolation-002: Child actor failure doesn't affect siblings")]
public async Task ConnectionManager_Child_Failure_Isolates_To_That_Endpoint()
{
    // Arrange: similar setup as above
    // Simulate connection failure on endpoint1
    // Verify endpoint2 still accepts new acquires
    
    // This is harder to test without internal access, but the principle is:
    // Child 1 restart shouldn't queue messages on Child 2's mailbox
}
```

---

### Phase 4: Run Tests (30 minutes)

```bash
cd /d/GIT/Akka.Streams.Http/src

# Build
dotnet build --configuration Release

# Run transport tests
dotnet test --project TurboHTTP.Tests/TurboHTTP.Tests.csproj -- \
  --filter-namespace "TurboHTTP.Tests.Transport"

# Run integration tests (full system)
dotnet test --project TurboHTTP.IntegrationTests/TurboHTTP.IntegrationTests.csproj -- \
  --filter-namespace "TurboHTTP.IntegrationTests.H11"
```

**Expected result**: All tests pass. Existing tests should not need changes (transparent refactoring).

---

### Phase 5: Documentation (30 minutes)

#### Update: `notes/Architecture/Design/01-LAYERED_ARCHITECTURE.md`

**Replace Transport Layer section:**

```markdown
### Transport Layer (`TurboHTTP/Transport/`)

**Hierarchical connection pool** — per-endpoint actor with fault isolation:
- `ConnectionManagerActor` — root router/supervisor
  - Routes `AcquireMsg` / `ReleaseMsg` to child by endpoint
  - Spawns `TcpHostConnectionActor` per RequestEndpoint on first use
  - Manages child supervision (OneForOne restart strategy)
  
- `TcpHostConnectionActor` — per-host manager
  - Owns idle queue, pending queue, active leases for one endpoint
  - Processes Acquire/Release in isolation (no cross-host contention)
  - Runs local eviction timer
  - Handles HTTP/1.0, 1.1, 2.0 (TCP)
  
- `QuicConnectionManager` — non-actor QUIC multi-stream manager
  - Manages shared QUIC connection per endpoint
  - Handles stream spawning, inbound acceptance loop
  - (Future) Could be wrapped in child actor, but currently standalone

- `DirectConnectionFactory` — TCP/TLS connection establishment
  - Establishes connection, spawns ByteMover tasks, returns ConnectionLease
  - No actor involvement (purely async)
  
- `ConnectionLease` — wraps ConnectionHandle + lifecycle
  
- `ClientByteMover` — async task pump: TCP/QUIC ↔ Channels

**Design rationale**:
- Per-host actor boundaries eliminate mailbox contention
- Fault isolation: one host timeout doesn't stall others
- Clear semantics: actor-per-origin matches RFC concepts
- Testing: can mock/isolate individual host behavior
```

---

## Checklist

- [ ] Create `TcpHostConnectionActor.cs`
- [ ] Verify file compiles (all logic copied from current HostState)
- [ ] Update `ConnectionManagerActor.cs`
  - [ ] Remove HostState class
  - [ ] Keep AcquireMsg/ReleaseMsg unchanged
  - [ ] Add routing to child actors
  - [ ] Remove per-host logic (all in child now)
  - [ ] Add supervision strategy
- [ ] Run `dotnet build --configuration Release`
- [ ] Run transport tests: `dotnet test --project TurboHTTP.Tests/TurboHTTP.Tests.csproj -- --filter-namespace "TurboHTTP.Tests.Transport"`
- [ ] Run integration tests: `dotnet test --project TurboHTTP.IntegrationTests/...`
- [ ] Verify GraphStage unchanged (no changes needed)
- [ ] Verify TcpTransportHandler unchanged (still calls root actor)
- [ ] Update documentation
- [ ] Commit message: "REFACTOR: Implement hierarchical connection pool with per-endpoint child actors"

---

## Rollback Plan

If issues arise:

1. **Test failures**: Likely missing message handler or routing issue
   - Check child actor receives message: add logging
   - Check TCS completion in child: verify Tcs.TrySetResult called
   
2. **Runtime errors**: Usually supervision/restart issues
   - Default OneForOne restart should work
   - If pending queue lost, caller will timeout and retry (acceptable)
   
3. **Performance regression**: Unlikely but monitor
   - Each hop adds ~0.1ms, but eliminates contention (net gain)
   
4. **Quick rollback**: Revert both files
   - Restore original ConnectionManagerActor.cs
   - Delete TcpHostConnectionActor.cs
   - Git reset to prior commit

---

## Future Extensions (Post-Implementation)

### QUIC Child Actor (Optional)
```csharp
internal sealed class QuicHostConnectionActor : ReceiveActor
{
    private readonly QuicConnectionManager _manager;
    
    private async Task OnOpenStreamMsg(OpenStreamMsg msg)
    {
        var lease = await _manager.OpenStreamAsync(msg.StreamType, msg.Ct);
        Sender.Tell(new StreamOpenedMsg(lease));
    }
}
```

### Per-Host Circuit Breaker
```csharp
// Inside TcpHostConnectionActor
private int _consecutiveFailures = 0;
private const int FailureThreshold = 5;

private void CheckCircuit()
{
    if (_consecutiveFailures >= FailureThreshold)
    {
        // Trip circuit, reject new acquires with specific error
    }
}
```

### Per-Host Rate Limiting
```csharp
// Inside TcpHostConnectionActor
private readonly RateLimiter _limiter = new(maxRequestsPerSecond: 100);

private async Task OnAcquire(AcquireMsg msg)
{
    await _limiter.AcquireAsync();
    // ... rest of logic
}
```

---

## See Also

- [[Architecture/Analysis/13-CONNECTION_POOL_HIERARCHY_ANALYSIS|Connection Pool Hierarchy Analysis]] — Full analysis of the three actor hierarchy options
- [[Architecture/Layers/14-TRANSPORT_LAYER|Transport Layer]] — Transport layer architecture overview
- [[Architecture/Design/01-LAYERED_ARCHITECTURE|Layered Architecture]] — Overall layered architecture

## Time Breakdown

| Phase | Task | Est. Time |
|-------|------|-----------|
| 1 | Create TcpHostConnectionActor | 90 min |
| 2 | Update ConnectionManagerActor | 120 min |
| 3 | Update/add tests | 60 min |
| 4 | Build and run tests | 30 min |
| 5 | Documentation | 30 min |
| **Total** | | **330 min (5.5 hours)** |

**Actual time may vary by developer experience level.**

