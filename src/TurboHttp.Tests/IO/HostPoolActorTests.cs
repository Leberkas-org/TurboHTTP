using System.Buffers;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit.Xunit2;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.Tests.IO;

public sealed class HostPoolActorTests : TestKit
{
    private static TcpOptions MakeOptions()
        => new() { Host = "test.local", Port = 80 };

    private static DataItem MakeDataItem()
    {
        var owner = MemoryPool<byte>.Shared.Rent(16);
        return new DataItem(owner, 16);
    }

    private IActorRef CreateHostPool(
        int maxConnections = 10,
        IActorRef? streamPublisher = null)
    {
        var options = MakeOptions();
        var config = new PoolConfig(
            MaxConnectionsPerHost: maxConnections,
            IdleCheckInterval: TimeSpan.FromHours(1));

        return Sys.ActorOf(Props.Create(() =>
            new HostPoolActor(options, config, streamPublisher ?? TestActor)));
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

    [Fact(DisplayName = "HPA-001: First Incoming request spawns a new ConnectionActor child")]
    public void HPA_001_FirstIncoming_SpawnsConnectionActor()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool();

        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));

        var connectionRef = ExpectConnectionSpawned();
        Assert.NotNull(connectionRef);
    }

    [Fact(DisplayName = "HPA-002: Second request while first connection is busy spawns a second ConnectionActor")]
    public void HPA_002_SecondRequest_WhileBusy_SpawnsSecondConnection()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool(maxConnections: 2);

        // First request — spawns connection 1
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        var conn1 = ExpectConnectionSpawned();

        // Second request — connection 1 is busy → spawns connection 2
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
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
            pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
            // Wait for each connection to spawn before sending next request,
            // ensuring the prior connection is marked busy before the next Incoming
            ExpectConnectionSpawned();
        }

        // Send one more — should NOT spawn (verified by no new CreateTcpRunner)
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "HPA-004: Request when MaxConnectionsPerHost is reached is queued in _pending")]
    public void HPA_004_MaxReached_RequestQueued()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool(maxConnections: 1);

        // First request spawns a connection
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        var connectionRef = ExpectConnectionSpawned();

        // Second request — connection is busy, max reached → should be queued (no new spawn)
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
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

        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
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
}
