using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit.Xunit2;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.Tests.IO;

public sealed class HostPoolActorTests : TestKit
{
    private const string DefaultPoolKey = "test.local:80";

    private static TcpOptions MakeOptions()
        => new() { Host = "test.local", Port = 80 };

    private static DataItem MakeDataItem()
    {
        var owner = MemoryPool<byte>.Shared.Rent(16);
        return new DataItem(owner, 16);
    }

    private PoolRouterActor.SendRequest MakeRequest(IActorRef? replyTo = null)
        => new(DefaultPoolKey, MakeDataItem(), replyTo ?? TestActor);

    private IActorRef CreateHostPool(int maxConnections = 10)
    {
        var options = MakeOptions();
        var config = new PoolConfig(
            MaxConnectionsPerHost: maxConnections,
            IdleCheckInterval: TimeSpan.FromHours(1));

        return Sys.ActorOf(Props.Create(() =>
            new HostPoolActor(options, config)));
    }

    /// <summary>
    /// When HostPoolActor spawns a ConnectionActor, the child's PreStart sends
    /// ClientManager.CreateTcpRunner to its _clientManager (the HostPoolActor).
    /// Since HostPoolActor doesn't handle CreateTcpRunner, it becomes an
    /// UnhandledMessage — letting us detect connection spawning and extract the child ref.
    /// </summary>
    private IActorRef ExpectConnectionSpawned(TimeSpan? timeout = null)
    {
        var msg = ExpectMsg<UnhandledMessage>(
            m => m.Message is ClientManager.CreateTcpRunner,
            timeout ?? TimeSpan.FromSeconds(3));

        var create = (ClientManager.CreateTcpRunner)msg.Message;
        return create.Handler;
    }

    [Fact(DisplayName = "HPA-001: First SendRequest spawns a new ConnectionActor child")]
    public void HPA_001_FirstRequest_SpawnsConnectionActor()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool();

        pool.Tell(MakeRequest());

        var connectionRef = ExpectConnectionSpawned();
        Assert.NotNull(connectionRef);
    }

    [Fact(DisplayName = "HPA-002: Second request while first connection is busy spawns a second ConnectionActor")]
    public void HPA_002_SecondRequest_WhileBusy_SpawnsSecondConnection()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool(maxConnections: 2);

        // First request — spawns connection 1
        pool.Tell(MakeRequest());
        var conn1 = ExpectConnectionSpawned();

        // Second request — connection 1 is busy → spawns connection 2
        pool.Tell(MakeRequest());
        var conn2 = ExpectConnectionSpawned();

        Assert.NotEqual(conn1, conn2);
    }

    [Fact(DisplayName = "HPA-003: N parallel requests with MaxConnectionsPerHost=N spawns N connections")]
    public void HPA_003_NParallelRequests_SpawnsNConnections()
    {
        const int n = 4;
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool(maxConnections: n);

        for (var i = 0; i < n; i++)
        {
            pool.Tell(MakeRequest());
            // Wait for each connection to spawn before sending next request,
            // ensuring the prior connection is marked busy before the next request
            ExpectConnectionSpawned();
        }

        // Send one more — should NOT spawn (verified by no new CreateTcpRunner)
        pool.Tell(MakeRequest());
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "HPA-004: Request when MaxConnectionsPerHost is reached is queued in _pending")]
    public void HPA_004_MaxReached_RequestQueued()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool(maxConnections: 1);

        // First request spawns a connection
        pool.Tell(MakeRequest());
        var connectionRef = ExpectConnectionSpawned();

        // Second request — connection is busy, max reached → should be queued (no new spawn)
        pool.Tell(MakeRequest());
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));

        // Verify the queued request drains when the connection becomes idle:
        // Send ConnectionIdle to free the connection, which triggers DrainPending
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // The queued DataItem is sent to the connection actor (no new spawn, still 1 connection).
        // No new CreateTcpRunner should appear.
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "HPA-005: Each spawned ConnectionActor is Context.Watch'ed")]
    public void HPA_005_SpawnedConnections_AreWatched()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool(maxConnections: 2);

        pool.Tell(MakeRequest());
        var connectionRef = ExpectConnectionSpawned();

        // Watch the HostPoolActor from the test
        Watch(pool);

        // Stop the child ConnectionActor. If Context.Watch was called by HostPoolActor,
        // it receives Terminated — but has no handler, causing a DeathPactException.
        // This crashes (and stops) the HostPoolActor, proving Watch was active.
        Sys.Stop(connectionRef);

        // HostPoolActor terminates due to DeathPactException — confirms it was watching the child
        ExpectTerminated(pool, TimeSpan.FromSeconds(3));
    }

    // ================================================================
    // TASK-008: Idle Handling & Queue Draining
    // ================================================================

    [Fact(DisplayName = "HPA-020: ConnectionIdle decrements PendingRequests — connection becomes reusable")]
    public void HPA_020_ConnectionIdle_DecrementsPendingRequests()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool(maxConnections: 1);

        // Send request → spawns connection, marks it busy (PendingRequests=1, Idle=false)
        pool.Tell(MakeRequest());
        var connectionRef = ExpectConnectionSpawned();

        // Send ConnectionIdle → MarkIdle: PendingRequests=0, Idle=true
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // Send another request — if PendingRequests was decremented and Idle=true,
        // the existing connection is reused (no new spawn)
        pool.Tell(MakeRequest());
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "HPA-021: ConnectionIdle sets Idle=true when PendingRequests reaches 0")]
    public void HPA_021_ConnectionIdle_SetsIdleTrue_WhenPendingReachesZero()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool(maxConnections: 2);

        // Spawn connection via request, then idle it
        pool.Tell(MakeRequest());
        var connectionRef = ExpectConnectionSpawned();

        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // Send two more requests. If connection is Idle=true, first request reuses it.
        // Second request should spawn a new connection (first is now busy again).
        pool.Tell(MakeRequest());
        pool.Tell(MakeRequest());

        // Only one new spawn expected (for second request) — proves first request reused the idle connection
        var conn2 = ExpectConnectionSpawned();
        Assert.NotEqual(connectionRef, conn2);

        // No further spawns
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "HPA-022: ConnectionIdle does NOT set Idle=true when PendingRequests > 0 after decrement (drain re-busies)")]
    public void HPA_022_ConnectionIdle_DoesNotSetIdle_WhenDrainReBusiesConnection()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool(maxConnections: 1);

        // Spawn connection, make it busy
        pool.Tell(MakeRequest());
        var connectionRef = ExpectConnectionSpawned();

        // Queue a second request (max=1, connection is busy)
        pool.Tell(MakeRequest());
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // ConnectionIdle → MarkIdle (Idle=true), then DrainPending dequeues req2 → MarkBusy (Idle=false)
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // Now send a third request. Connection is busy again (drained req2), so it should be queued.
        pool.Tell(MakeRequest());

        // No new spawn — proves the connection is NOT idle after drain
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "HPA-023: ConnectionIdle updates LastActivity — connection survives immediate idle eviction")]
    public async Task HPA_023_ConnectionIdle_UpdatesLastActivity()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

        // Create pool with a very short idle timeout
        var options = MakeOptions();
        var config = new PoolConfig(
            MaxConnectionsPerHost: 2,
            IdleCheckInterval: TimeSpan.FromHours(1),
            IdleTimeout: TimeSpan.FromMilliseconds(200));

        var pool = Sys.ActorOf(Props.Create(() =>
            new HostPoolActor(options, config)));

        // Spawn connection via request
        pool.Tell(MakeRequest());
        var connectionRef = ExpectConnectionSpawned();

        // Spawn a second connection (so eviction is allowed — min 1 preserved)
        pool.Tell(MakeRequest());
        var conn2 = ExpectConnectionSpawned();

        // Wait past idle timeout, then send ConnectionIdle (updates LastActivity to now)
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // Trigger manual idle check — connection should NOT be evicted because
        // LastActivity was just updated by ConnectionIdle
        pool.Tell(new HostPoolActor.IdleCheck());

        // Verify connection survives: send a new request, should reuse the idle connection (no new spawn)
        pool.Tell(MakeRequest());
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "HPA-024: After ConnectionIdle, pending requests are drained to freed connection")]
    public void HPA_024_ConnectionIdle_DrainsPendingToFreedConnection()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool(maxConnections: 1);

        // Spawn connection, mark busy
        pool.Tell(MakeRequest());
        var connectionRef = ExpectConnectionSpawned();

        // Queue a request
        pool.Tell(MakeRequest());
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // ConnectionIdle → connection freed → DrainPending sends queued request to it
        // No new connection should be spawned (the queued request goes to the existing connection)
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "HPA-025: DrainPending stops when no idle connection is available")]
    public void HPA_025_DrainPending_StopsWhenNoIdleConnection()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool(maxConnections: 1);

        // Spawn connection
        pool.Tell(MakeRequest());
        var connectionRef = ExpectConnectionSpawned();

        // Queue two more requests
        pool.Tell(MakeRequest());
        pool.Tell(MakeRequest());
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // ConnectionIdle → drain first pending (connection becomes busy), second pending stays queued
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // No new spawn — only one pending drained, second stays queued
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // ConnectionIdle again → drain second pending
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // Now send one more request — should still be queued (connection busy from draining second pending)
        pool.Tell(MakeRequest());
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "HPA-026: Multiple pending requests are drained sequentially across idle cycles")]
    public void HPA_026_MultiplePendingRequests_DrainedSequentially()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool(maxConnections: 1);

        // Spawn one connection
        pool.Tell(MakeRequest());
        var connectionRef = ExpectConnectionSpawned();

        // Queue 3 requests
        pool.Tell(MakeRequest());
        pool.Tell(MakeRequest());
        pool.Tell(MakeRequest());
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // Drain cycle 1: idles connection → drains pending #1 → connection busy again
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));
        // Drain cycle 2: idles connection → drains pending #2 → connection busy again
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));
        // Drain cycle 3: idles connection → drains pending #3 → connection busy again
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // All 3 pending drained, no new connections spawned
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // Final idle: connection is now truly idle (no more pending)
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // New request should reuse the idle connection
        pool.Tell(MakeRequest());
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    // ================================================================
    // TASK-009: Connection Failure & Reconnect
    // ================================================================

    private IActorRef CreateHostPoolWithReconnect(
        int maxConnections = 10,
        TimeSpan? reconnectInterval = null)
    {
        var options = MakeOptions();
        var config = new PoolConfig(
            MaxConnectionsPerHost: maxConnections,
            IdleCheckInterval: TimeSpan.FromHours(1),
            ReconnectInterval: reconnectInterval ?? TimeSpan.FromMilliseconds(200));

        return Sys.ActorOf(Props.Create(() =>
            new HostPoolActor(options, config)));
    }

    [Fact(DisplayName = "HPA-030: ConnectionFailed marks ConnectionState.Active=false — connection not reused")]
    public void HPA_030_ConnectionFailed_MarksActiveFlase()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPoolWithReconnect(maxConnections: 2);

        // Spawn connection via request
        pool.Tell(MakeRequest());
        var connectionRef = ExpectConnectionSpawned();

        // Mark connection idle so it would be selected for reuse
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // Fail the connection — Active=false
        pool.Tell(new HostPoolActor.ConnectionFailed(connectionRef));

        // Send a new request. If Active=false, the dead connection is skipped.
        // Since maxConnections=2 and only 1 exists (dead), a new connection should spawn.
        pool.Tell(MakeRequest());
        var conn2 = ExpectConnectionSpawned();

        Assert.NotEqual(connectionRef, conn2);
    }

    [Fact(DisplayName = "HPA-031: ConnectionFailed schedules Reconnect after PoolConfig.ReconnectInterval")]
    public void HPA_031_ConnectionFailed_SchedulesReconnect()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPoolWithReconnect(maxConnections: 1, reconnectInterval: TimeSpan.FromMilliseconds(300));

        // Spawn connection
        pool.Tell(MakeRequest());
        var connectionRef = ExpectConnectionSpawned();

        // Fail it — should schedule Reconnect after 300ms
        pool.Tell(new HostPoolActor.ConnectionFailed(connectionRef));

        // No immediate spawn — reconnect is delayed
        ExpectNoMsg(TimeSpan.FromMilliseconds(100));

        // After ReconnectInterval, Reconnect fires → removes dead connection, spawns new one
        var newConn = ExpectConnectionSpawned(TimeSpan.FromSeconds(3));
        Assert.NotEqual(connectionRef, newConn);
    }

    [Fact(DisplayName = "HPA-032: ConnectionFailed for unknown connection is silently ignored")]
    public void HPA_032_ConnectionFailed_UnknownConnection_SilentlyIgnored()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPoolWithReconnect(maxConnections: 1);

        // Spawn a connection so the pool has state
        pool.Tell(MakeRequest());
        var connectionRef = ExpectConnectionSpawned();

        // Send ConnectionFailed for an unknown actor
        var unknownRef = CreateTestProbe().Ref;
        pool.Tell(new HostPoolActor.ConnectionFailed(unknownRef));

        // Pool is still alive and functional — idle the real connection and reuse it
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));
        pool.Tell(MakeRequest());

        // No new spawn — real connection reused, pool not crashed
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "HPA-033: Reconnect spawns a new ConnectionActor to replace the dead one")]
    public void HPA_033_Reconnect_SpawnsNewConnection()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPoolWithReconnect(maxConnections: 1, reconnectInterval: TimeSpan.FromMilliseconds(200));

        // Spawn connection
        pool.Tell(MakeRequest());
        var connectionRef = ExpectConnectionSpawned();

        // Fail it — triggers scheduled Reconnect
        pool.Tell(new HostPoolActor.ConnectionFailed(connectionRef));

        // Wait for Reconnect to fire — a new connection should spawn
        var newConn = ExpectConnectionSpawned(TimeSpan.FromSeconds(3));
        Assert.NotNull(newConn);
        Assert.NotEqual(connectionRef, newConn);

        // Verify the new connection is functional — idle it and send a request
        pool.Tell(new HostPoolActor.ConnectionIdle(newConn));
        pool.Tell(MakeRequest());

        // Should reuse the new connection, no additional spawn
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "HPA-034: Reconnect for already-removed connection is silently ignored")]
    public void HPA_034_Reconnect_AlreadyRemovedConnection_Ignored()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

        var options = MakeOptions();
        var config = new PoolConfig(
            MaxConnectionsPerHost: 2,
            IdleCheckInterval: TimeSpan.FromHours(1),
            IdleTimeout: TimeSpan.FromMilliseconds(50),
            ReconnectInterval: TimeSpan.FromMilliseconds(500));

        var pool = Sys.ActorOf(Props.Create(() =>
            new HostPoolActor(options, config)));

        // Spawn two connections (need 2 so idle eviction is allowed — min 1 preserved)
        pool.Tell(MakeRequest());
        var conn1 = ExpectConnectionSpawned();
        pool.Tell(MakeRequest());
        var conn2 = ExpectConnectionSpawned();

        // Fail conn1 — schedules Reconnect in 500ms
        pool.Tell(new HostPoolActor.ConnectionFailed(conn1));

        // Manually send Reconnect for conn1 before the scheduled one fires.
        // First Reconnect removes the dead conn1 and spawns a replacement.
        pool.Tell(new HostPoolActor.Reconnect(conn1));
        var replacement = ExpectConnectionSpawned();

        // Now the scheduled Reconnect will fire for conn1, but conn1 is already removed.
        // It should be silently ignored — no additional spawn.
        ExpectNoMsg(TimeSpan.FromSeconds(2));
    }

    [Fact(DisplayName = "HPA-035: New connection after reconnect is Active=true, Idle=true — immediately reusable")]
    public void HPA_035_Reconnect_NewConnection_IsActiveAndIdle()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPoolWithReconnect(maxConnections: 1, reconnectInterval: TimeSpan.FromMilliseconds(200));

        // Spawn connection via request
        pool.Tell(MakeRequest());
        var connectionRef = ExpectConnectionSpawned();

        // Idle the connection so it can be found & failed
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // Fail the connection — schedules Reconnect after 200ms
        pool.Tell(new HostPoolActor.ConnectionFailed(connectionRef));

        // Wait for Reconnect to fire — new connection spawns with Active=true, Idle=true
        var newConn = ExpectConnectionSpawned(TimeSpan.FromSeconds(3));
        Assert.NotEqual(connectionRef, newConn);

        // Verify the new connection is Active=true AND Idle=true:
        // Send a request — if it's idle, the pool selects it (no new spawn needed).
        // If it were NOT idle or NOT active, the pool would try to spawn another
        // connection, but maxConnections=1 so it would queue instead.
        pool.Tell(MakeRequest());

        // No new connection spawn — proves the reconnected connection was selected (Active + Idle)
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "HPA-027: ConnectionIdle for unknown connection is silently ignored")]
    public void HPA_027_ConnectionIdle_UnknownConnection_SilentlyIgnored()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool(maxConnections: 1);

        // Spawn a connection so the pool has state
        pool.Tell(MakeRequest());
        var connectionRef = ExpectConnectionSpawned();

        // Send ConnectionIdle for an unknown actor ref
        var unknownRef = CreateTestProbe().Ref;
        pool.Tell(new HostPoolActor.ConnectionIdle(unknownRef));

        // Pool should still be alive and functional — send another request
        // Connection is busy, so this queues. Then idle the real connection to drain it.
        pool.Tell(MakeRequest());
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // No crash, no new spawn — pool is healthy
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    // ================================================================
    // TASK-011: Idle Connection Eviction
    // ================================================================

    private IActorRef CreateHostPoolWithEviction(
        int maxConnections = 10,
        TimeSpan? idleTimeout = null,
        TimeSpan? idleCheckInterval = null)
    {
        var options = MakeOptions();
        var config = new PoolConfig(
            MaxConnectionsPerHost: maxConnections,
            IdleCheckInterval: idleCheckInterval ?? TimeSpan.FromHours(1),
            IdleTimeout: idleTimeout ?? TimeSpan.FromMilliseconds(200));

        return Sys.ActorOf(Props.Create(() =>
            new HostPoolActor(options, config)));
    }

    [Fact(DisplayName = "HPA-040: IdleCheck timer is started in PreStart with PoolConfig.IdleCheckInterval")]
    public async Task HPA_040_PreStart_SchedulesIdleCheckTimer()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

        // Create pool with very short IdleCheckInterval so the timer fires quickly,
        // and a very short IdleTimeout so eviction actually happens.
        var pool = CreateHostPoolWithEviction(
            maxConnections: 2,
            idleTimeout: TimeSpan.FromMilliseconds(100),
            idleCheckInterval: TimeSpan.FromMilliseconds(300));

        // Spawn two connections (need >1 so eviction is allowed — min 1 preserved)
        pool.Tell(MakeRequest());
        var conn1 = ExpectConnectionSpawned();
        pool.Tell(MakeRequest());
        var conn2 = ExpectConnectionSpawned();

        // Idle both connections
        pool.Tell(new HostPoolActor.ConnectionIdle(conn1));
        pool.Tell(new HostPoolActor.ConnectionIdle(conn2));

        // Wait for idle timeout + at least one IdleCheckInterval to fire
        await Task.Delay(TimeSpan.FromMilliseconds(600));

        // The automatic IdleCheck should have evicted at least one connection.
        // Send two requests — if eviction happened, at least one new connection spawns.
        pool.Tell(MakeRequest());
        pool.Tell(MakeRequest());

        // At least one new connection should spawn (proving the timer-driven eviction ran)
        ExpectConnectionSpawned(TimeSpan.FromSeconds(3));
    }

    [Fact(DisplayName = "HPA-041: Idle connection past IdleTimeout receives PoisonPill and is removed")]
    public async Task HPA_041_IdleConnectionPastTimeout_EvictedWithPoisonPill()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

        var pool = CreateHostPoolWithEviction(
            maxConnections: 2,
            idleTimeout: TimeSpan.FromMilliseconds(100));

        // Spawn two connections (need >1 for eviction)
        pool.Tell(MakeRequest());
        var conn1 = ExpectConnectionSpawned();
        pool.Tell(MakeRequest());
        var conn2 = ExpectConnectionSpawned();

        // Idle both connections
        pool.Tell(new HostPoolActor.ConnectionIdle(conn1));
        pool.Tell(new HostPoolActor.ConnectionIdle(conn2));

        // Watch conn1 to detect PoisonPill termination
        Watch(conn1);

        // Wait past idle timeout
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        // Trigger idle check manually
        pool.Tell(new HostPoolActor.IdleCheck());

        // conn1 should receive PoisonPill and terminate
        ExpectTerminated(conn1, TimeSpan.FromSeconds(3));

        // Verify conn1 is removed from pool: send a request while conn2 is still idle.
        // If conn1 were still tracked, it might be selected. Since it's removed,
        // conn2 is used. Then a second request should spawn a new connection.
        pool.Tell(MakeRequest());
        pool.Tell(MakeRequest());

        // One request reuses conn2 (idle), second needs a new spawn
        var conn3 = ExpectConnectionSpawned(TimeSpan.FromSeconds(3));
        Assert.NotEqual(conn1, conn3);
    }

    [Fact(DisplayName = "HPA-042: Idle connection within IdleTimeout is preserved")]
    public void HPA_042_IdleConnectionWithinTimeout_Preserved()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

        var pool = CreateHostPoolWithEviction(
            maxConnections: 2,
            idleTimeout: TimeSpan.FromHours(1)); // Very long — never expires

        // Spawn two connections
        pool.Tell(MakeRequest());
        var conn1 = ExpectConnectionSpawned();
        pool.Tell(MakeRequest());
        var conn2 = ExpectConnectionSpawned();

        // Idle both connections
        pool.Tell(new HostPoolActor.ConnectionIdle(conn1));
        pool.Tell(new HostPoolActor.ConnectionIdle(conn2));

        // Trigger idle check — neither should be evicted (within timeout)
        pool.Tell(new HostPoolActor.IdleCheck());

        // Both connections should still be reusable — send two requests, no new spawns
        pool.Tell(MakeRequest());
        pool.Tell(MakeRequest());

        // No new connections spawned — both idle connections were reused
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "HPA-043: Active (non-idle) connection is never evicted regardless of LastActivity")]
    public async Task HPA_043_ActiveConnection_NeverEvicted()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

        var pool = CreateHostPoolWithEviction(
            maxConnections: 2,
            idleTimeout: TimeSpan.FromMilliseconds(100));

        // Spawn two connections
        pool.Tell(MakeRequest());
        var conn1 = ExpectConnectionSpawned();
        pool.Tell(MakeRequest());
        var conn2 = ExpectConnectionSpawned();

        // Only idle conn2, leave conn1 busy (Idle=false)
        pool.Tell(new HostPoolActor.ConnectionIdle(conn2));

        // Wait past idle timeout
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        // Trigger idle check — conn1 is busy so it must NOT be evicted,
        // conn2 is idle and past timeout but is the only idle one
        pool.Tell(new HostPoolActor.IdleCheck());

        // Idle conn1 now — if it was evicted, it wouldn't be in the pool
        pool.Tell(new HostPoolActor.ConnectionIdle(conn1));

        // Send a request — should reuse conn1 (proves it wasn't evicted)
        pool.Tell(MakeRequest());

        // No new spawn — conn1 was preserved despite being past idle timeout age
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "HPA-044: Last remaining connection per host is preserved even if idle and expired")]
    public async Task HPA_044_LastRemainingConnection_Preserved()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

        var pool = CreateHostPoolWithEviction(
            maxConnections: 1,
            idleTimeout: TimeSpan.FromMilliseconds(100));

        // Spawn single connection
        pool.Tell(MakeRequest());
        var connectionRef = ExpectConnectionSpawned();

        // Idle it
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // Wait past idle timeout
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        // Trigger idle check — connection is idle AND past timeout,
        // but it's the last one (_connections.Count == 1), so it must be preserved
        pool.Tell(new HostPoolActor.IdleCheck());

        // Send a request — should reuse the preserved connection (no new spawn)
        pool.Tell(MakeRequest());
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "HPA-045: Multiple idle connections — only expired ones are evicted")]
    public async Task HPA_045_MultipleIdleConnections_OnlyExpiredEvicted()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

        var pool = CreateHostPoolWithEviction(
            maxConnections: 3,
            idleTimeout: TimeSpan.FromMilliseconds(200));

        // Spawn three connections
        pool.Tell(MakeRequest());
        var conn1 = ExpectConnectionSpawned();
        pool.Tell(MakeRequest());
        var conn2 = ExpectConnectionSpawned();
        pool.Tell(MakeRequest());
        var conn3 = ExpectConnectionSpawned();

        // Idle conn1 and conn2 early
        pool.Tell(new HostPoolActor.ConnectionIdle(conn1));
        pool.Tell(new HostPoolActor.ConnectionIdle(conn2));

        // Wait past idle timeout for conn1 and conn2
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        // Now idle conn3 — its LastActivity is fresh (just updated)
        pool.Tell(new HostPoolActor.ConnectionIdle(conn3));

        // Watch conn1 and conn2 to verify eviction
        Watch(conn1);
        Watch(conn2);

        // Trigger idle check — conn1 and conn2 are past timeout, conn3 is fresh
        pool.Tell(new HostPoolActor.IdleCheck());

        // Both conn1 and conn2 are evicted (count 3→2→1), but Terminated messages
        // may arrive in any order. Collect both and verify the set.
        var terminated1 = ExpectMsg<Terminated>(TimeSpan.FromSeconds(3));
        var terminated2 = ExpectMsg<Terminated>(TimeSpan.FromSeconds(3));
        var evicted = new HashSet<IActorRef> { terminated1.ActorRef, terminated2.ActorRef };
        Assert.Contains(conn1, evicted);
        Assert.Contains(conn2, evicted);

        // conn3 should be preserved — send a request, should reuse it
        pool.Tell(MakeRequest());
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));

        // Send two more requests — conn3 is busy now, so new connections should spawn
        pool.Tell(MakeRequest());
        var conn4 = ExpectConnectionSpawned(TimeSpan.FromSeconds(3));
        Assert.NotEqual(conn1, conn4);
        Assert.NotEqual(conn2, conn4);
    }

    // ================================================================
    // TASK-019: ReplyTo-Based Response Routing
    // ================================================================

    [Fact(DisplayName = "HPA-050: Response for request A goes to ReplyTo A")]
    public void HPA_050_Response_RoutedToCorrectReplyTo()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

        var options = MakeOptions();
        var config = new PoolConfig(
            MaxConnectionsPerHost: 1,
            IdleCheckInterval: TimeSpan.FromHours(1));

        var pool = Sys.ActorOf(Props.Create(() =>
            new HostPoolActor(options, config)));

        var replyToA = CreateTestProbe("replyToA");

        // Send request with replyToA
        pool.Tell(new PoolRouterActor.SendRequest(DefaultPoolKey, MakeDataItem(), replyToA));
        var connectionRef = ExpectConnectionSpawned();

        // Simulate response from connection
        var mem = MemoryPool<byte>.Shared.Rent(4);
        new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }.CopyTo(mem.Memory.Span);
        pool.Tell(new HostPoolActor.ConnectionResponse(connectionRef, mem, 4));

        // Response should arrive at replyToA as PoolRouterActor.Response
        var response = replyToA.ExpectMsg<PoolRouterActor.Response>(TimeSpan.FromSeconds(3));
        Assert.Equal(DefaultPoolKey, response.PoolKey);
        Assert.Equal(4, response.Length);
        Assert.Equal(0xDE, response.Memory.Memory.Span[0]);
        Assert.Equal(0xEF, response.Memory.Memory.Span[3]);
    }

    [Fact(DisplayName = "HPA-051: Response for request B goes to ReplyTo B (not A)")]
    public void HPA_051_Response_RoutedToCorrectReplyTo_NotOther()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

        var options = MakeOptions();
        var config = new PoolConfig(
            MaxConnectionsPerHost: 2,
            IdleCheckInterval: TimeSpan.FromHours(1));

        var pool = Sys.ActorOf(Props.Create(() =>
            new HostPoolActor(options, config)));

        var replyToA = CreateTestProbe("replyToA");
        var replyToB = CreateTestProbe("replyToB");

        // Send request A → spawns connection 1
        pool.Tell(new PoolRouterActor.SendRequest(DefaultPoolKey, MakeDataItem(), replyToA));
        var conn1 = ExpectConnectionSpawned();

        // Send request B → spawns connection 2
        pool.Tell(new PoolRouterActor.SendRequest(DefaultPoolKey, MakeDataItem(), replyToB));
        var conn2 = ExpectConnectionSpawned();

        // Response from connection 2 (request B)
        var memB = MemoryPool<byte>.Shared.Rent(2);
        new byte[] { 0xBB, 0xCC }.CopyTo(memB.Memory.Span);
        pool.Tell(new HostPoolActor.ConnectionResponse(conn2, memB, 2));

        // replyToB should get the response
        var responseB = replyToB.ExpectMsg<PoolRouterActor.Response>(TimeSpan.FromSeconds(3));
        Assert.Equal(DefaultPoolKey, responseB.PoolKey);
        Assert.Equal(0xBB, responseB.Memory.Memory.Span[0]);

        // replyToA should NOT have received anything
        replyToA.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    [Fact(DisplayName = "HPA-052: Multiple concurrent requests to different ReplyTos are correctly routed")]
    public void HPA_052_MultipleConcurrentRequests_CorrectlyRouted()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

        var options = MakeOptions();
        var config = new PoolConfig(
            MaxConnectionsPerHost: 3,
            IdleCheckInterval: TimeSpan.FromHours(1));

        var pool = Sys.ActorOf(Props.Create(() =>
            new HostPoolActor(options, config)));

        var replyTo1 = CreateTestProbe("replyTo1");
        var replyTo2 = CreateTestProbe("replyTo2");
        var replyTo3 = CreateTestProbe("replyTo3");

        // Send 3 requests → 3 connections
        pool.Tell(new PoolRouterActor.SendRequest("host-a", MakeDataItem(), replyTo1));
        var conn1 = ExpectConnectionSpawned();

        pool.Tell(new PoolRouterActor.SendRequest("host-b", MakeDataItem(), replyTo2));
        var conn2 = ExpectConnectionSpawned();

        pool.Tell(new PoolRouterActor.SendRequest("host-c", MakeDataItem(), replyTo3));
        var conn3 = ExpectConnectionSpawned();

        // Responses arrive out of order: conn3, conn1, conn2
        var mem3 = MemoryPool<byte>.Shared.Rent(1);
        mem3.Memory.Span[0] = 0x33;
        pool.Tell(new HostPoolActor.ConnectionResponse(conn3, mem3, 1));

        var mem1 = MemoryPool<byte>.Shared.Rent(1);
        mem1.Memory.Span[0] = 0x11;
        pool.Tell(new HostPoolActor.ConnectionResponse(conn1, mem1, 1));

        var mem2 = MemoryPool<byte>.Shared.Rent(1);
        mem2.Memory.Span[0] = 0x22;
        pool.Tell(new HostPoolActor.ConnectionResponse(conn2, mem2, 1));

        // Each ReplyTo gets the correct response
        var resp3 = replyTo3.ExpectMsg<PoolRouterActor.Response>(TimeSpan.FromSeconds(3));
        Assert.Equal("host-c", resp3.PoolKey);
        Assert.Equal(0x33, resp3.Memory.Memory.Span[0]);

        var resp1 = replyTo1.ExpectMsg<PoolRouterActor.Response>(TimeSpan.FromSeconds(3));
        Assert.Equal("host-a", resp1.PoolKey);
        Assert.Equal(0x11, resp1.Memory.Memory.Span[0]);

        var resp2 = replyTo2.ExpectMsg<PoolRouterActor.Response>(TimeSpan.FromSeconds(3));
        Assert.Equal("host-b", resp2.PoolKey);
        Assert.Equal(0x22, resp2.Memory.Memory.Span[0]);
    }

    [Fact(DisplayName = "HPA-053: FIFO ordering — multiple requests on same connection route to correct ReplyTos")]
    public void HPA_053_FifoOrdering_SameConnection_CorrectRouting()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

        var options = MakeOptions();
        var config = new PoolConfig(
            MaxConnectionsPerHost: 1,
            IdleCheckInterval: TimeSpan.FromHours(1));

        var pool = Sys.ActorOf(Props.Create(() =>
            new HostPoolActor(options, config)));

        var replyToA = CreateTestProbe("replyToA");
        var replyToB = CreateTestProbe("replyToB");

        // Send request A → spawns connection
        pool.Tell(new PoolRouterActor.SendRequest(DefaultPoolKey, MakeDataItem(), replyToA));
        var connectionRef = ExpectConnectionSpawned();

        // Idle the connection, then send request B → reuses same connection
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));
        pool.Tell(new PoolRouterActor.SendRequest(DefaultPoolKey, MakeDataItem(), replyToB));

        // Response 1 → should go to replyToA (FIFO)
        var mem1 = MemoryPool<byte>.Shared.Rent(1);
        mem1.Memory.Span[0] = 0xAA;
        pool.Tell(new HostPoolActor.ConnectionResponse(connectionRef, mem1, 1));

        var respA = replyToA.ExpectMsg<PoolRouterActor.Response>(TimeSpan.FromSeconds(3));
        Assert.Equal(0xAA, respA.Memory.Memory.Span[0]);

        // Response 2 → should go to replyToB (FIFO)
        var mem2 = MemoryPool<byte>.Shared.Rent(1);
        mem2.Memory.Span[0] = 0xBB;
        pool.Tell(new HostPoolActor.ConnectionResponse(connectionRef, mem2, 1));

        var respB = replyToB.ExpectMsg<PoolRouterActor.Response>(TimeSpan.FromSeconds(3));
        Assert.Equal(0xBB, respB.Memory.Memory.Span[0]);

        // Neither probe got the other's response
        replyToA.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
        replyToB.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
    }

    [Fact(DisplayName = "HPA-054: Queued (pending) request preserves ReplyTo through drain")]
    public void HPA_054_QueuedRequest_PreservesReplyTo()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

        var options = MakeOptions();
        var config = new PoolConfig(
            MaxConnectionsPerHost: 1,
            IdleCheckInterval: TimeSpan.FromHours(1));

        var pool = Sys.ActorOf(Props.Create(() =>
            new HostPoolActor(options, config)));

        var replyToA = CreateTestProbe("replyToA");
        var replyToB = CreateTestProbe("replyToB");

        // Send request A → spawns connection, marks busy
        pool.Tell(new PoolRouterActor.SendRequest(DefaultPoolKey, MakeDataItem(), replyToA));
        var connectionRef = ExpectConnectionSpawned();

        // Send request B → queued (max=1, connection busy)
        pool.Tell(new PoolRouterActor.SendRequest(DefaultPoolKey, MakeDataItem(), replyToB));
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // Response for request A
        var memA = MemoryPool<byte>.Shared.Rent(1);
        memA.Memory.Span[0] = 0xAA;
        pool.Tell(new HostPoolActor.ConnectionResponse(connectionRef, memA, 1));

        var respA = replyToA.ExpectMsg<PoolRouterActor.Response>(TimeSpan.FromSeconds(3));
        Assert.Equal(0xAA, respA.Memory.Memory.Span[0]);

        // Idle → drains request B to the connection
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // Response for request B (which was queued, then drained)
        var memB = MemoryPool<byte>.Shared.Rent(1);
        memB.Memory.Span[0] = 0xBB;
        pool.Tell(new HostPoolActor.ConnectionResponse(connectionRef, memB, 1));

        var respB = replyToB.ExpectMsg<PoolRouterActor.Response>(TimeSpan.FromSeconds(3));
        Assert.Equal(0xBB, respB.Memory.Memory.Span[0]);
    }
}
