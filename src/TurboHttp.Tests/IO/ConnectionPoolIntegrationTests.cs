using System.Buffers;
using Akka.Actor;
using Akka.DependencyInjection;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit.Xunit2;
using Microsoft.Extensions.DependencyInjection;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.Tests.IO;

/// <summary>
/// TASK-020: Integration tests exercising the complete actor hierarchy
/// (PoolRouterActor → HostPoolActor → ConnectionActor) wired through
/// the ConnectionPoolStage GraphStage bridge.
/// </summary>
public sealed class ConnectionPoolIntegrationTests : TestKit
{
    private readonly IMaterializer _materializer;

    public ConnectionPoolIntegrationTests() : base(CreateSystem())
    {
        _materializer = Sys.Materializer();
    }

    private static ActorSystem CreateSystem()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new TcpOptions { Host = "default", Port = 0 });
        services.AddSingleton(new PoolConfig(
            MaxConnectionsPerHost: 1,
            IdleCheckInterval: TimeSpan.FromHours(1),
            ReconnectInterval: TimeSpan.FromMilliseconds(200)));

        var provider = services.BuildServiceProvider();
        var diSetup = DependencyResolverSetup.Create(provider);

        return ActorSystem.Create(
            "pool-int-" + Guid.NewGuid().ToString("N")[..8],
            BootstrapSetup.Create().And(diSetup));
    }

    // ── helpers ──────────────────────────────────────────────────────

    private static DataItem MakeDataItem(byte[] payload)
    {
        var owner = MemoryPool<byte>.Shared.Rent(payload.Length);
        payload.CopyTo(owner.Memory.Span);
        return new DataItem(owner, payload.Length);
    }

    private static RoutedTransportItem MakeRoutedData(string poolKey, byte[] payload)
        => new(poolKey, MakeDataItem(payload));

    private static IMemoryOwner<byte> MakeResponseMemory(params byte[] data)
    {
        var mem = MemoryPool<byte>.Shared.Rent(data.Length);
        data.CopyTo(mem.Memory.Span);
        return mem;
    }

    private IActorRef ExpectConnectionSpawned(TimeSpan? timeout = null)
    {
        var msg = ExpectMsg<UnhandledMessage>(
            m => m.Message is ClientManager.CreateTcpRunner,
            timeout ?? TimeSpan.FromSeconds(3));
        return ((ClientManager.CreateTcpRunner)msg.Message).Handler;
    }

    private IActorRef CreateRouter()
        => Sys.ActorOf(Props.Create(() => new PoolRouterActor()));

    private (TestPublisher.ManualProbe<RoutedTransportItem> upstream,
             TestSubscriber.ManualProbe<RoutedDataItem> downstream)
        MaterializeStage(IActorRef router)
    {
        var stage = new ConnectionPoolStage(router);
        var upstream = this.CreateManualPublisherProbe<RoutedTransportItem>();
        var downstream = this.CreateManualSubscriberProbe<RoutedDataItem>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var flow = b.Add(Flow.FromGraph(stage));
            var src = b.Add(Source.FromPublisher(upstream));
            var snk = b.Add(Sink.FromSubscriber(downstream));
            b.From(src).Via(flow).To(snk);
            return ClosedShape.Instance;
        })).Run(_materializer);

        return (upstream, downstream);
    }

    private IActorRef ResolveHostPool(IActorRef router, string poolKey)
    {
        Sys.ActorSelection(router.Path / poolKey).Tell(new Identify(poolKey));
        var identity = ExpectMsg<ActorIdentity>(
            m => poolKey.Equals(m.MessageId),
            TimeSpan.FromSeconds(3));
        return identity.Subject!;
    }

    // ── tests ───────────────────────────────────────────────────────

    [Fact(Timeout = 15_000, DisplayName = "POOL-INT-001: Full pipeline — RegisterHost → stream request → ConnectionActor spawned → response flows back")]
    public async Task FullPipeline_RegisterHost_SendRequest_ResponseFlowsBack()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

        var router = CreateRouter();
        router.Tell(new PoolRouterActor.RegisterHost("host-a:80",
            new TcpOptions { Host = "host-a", Port = 80 }));

        var (upstream, downstream) = MaterializeStage(router);
        var upSub = await upstream.ExpectSubscriptionAsync(CancellationToken.None);
        var downSub = await downstream.ExpectSubscriptionAsync(CancellationToken.None);
        downSub.Request(10);

        // Push request through stream inlet
        upSub.SendNext(MakeRoutedData("host-a:80", [0x01, 0x02]));

        // ConnectionActor spawned (CreateTcpRunner becomes UnhandledMessage)
        var connectionRef = ExpectConnectionSpawned();
        Assert.NotNull(connectionRef);

        // Get HostPoolActor to simulate TCP response
        var hostPool = ResolveHostPool(router, "host-a:80");

        // Simulate response from connection
        hostPool.Tell(new HostPoolActor.ConnectionResponse(
            connectionRef, MakeResponseMemory(0xAA, 0xBB, 0xCC), 3));

        // Response flows back through stream outlet
        var response = await downstream.ExpectNextAsync(CancellationToken.None);
        Assert.Equal("host-a:80", response.PoolKey);
        Assert.Equal(3, response.Length);
        Assert.Equal(0xAA, response.Memory.Memory.Span[0]);
        Assert.Equal(0xCC, response.Memory.Memory.Span[2]);
    }

    [Fact(Timeout = 15_000, DisplayName = "POOL-INT-002: Multiple PoolKeys (hosts) work in parallel")]
    public async Task MultiplePoolKeys_WorkInParallel()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

        var router = CreateRouter();
        router.Tell(new PoolRouterActor.RegisterHost("alpha:80",
            new TcpOptions { Host = "alpha", Port = 80 }));
        router.Tell(new PoolRouterActor.RegisterHost("beta:443",
            new TcpOptions { Host = "beta", Port = 443 }));

        var (upstream, downstream) = MaterializeStage(router);
        var upSub = await upstream.ExpectSubscriptionAsync(CancellationToken.None);
        var downSub = await downstream.ExpectSubscriptionAsync(CancellationToken.None);
        downSub.Request(10);

        // Push requests for both hosts
        upSub.SendNext(MakeRoutedData("alpha:80", [0x01]));
        var connAlpha = ExpectConnectionSpawned();

        upSub.SendNext(MakeRoutedData("beta:443", [0x02]));
        var connBeta = ExpectConnectionSpawned();

        // Different connection actors spawned for different hosts
        Assert.NotEqual(connAlpha, connBeta);

        // Resolve both HostPoolActors
        var hostAlpha = ResolveHostPool(router, "alpha:80");
        var hostBeta = ResolveHostPool(router, "beta:443");
        Assert.NotEqual(hostAlpha, hostBeta);

        // Respond out of order: beta first, alpha second
        hostBeta.Tell(new HostPoolActor.ConnectionResponse(
            connBeta, MakeResponseMemory(0xBB), 1));
        hostAlpha.Tell(new HostPoolActor.ConnectionResponse(
            connAlpha, MakeResponseMemory(0xAA), 1));

        // Both responses arrive on the outlet
        var resp1 = await downstream.ExpectNextAsync(CancellationToken.None);
        var resp2 = await downstream.ExpectNextAsync(CancellationToken.None);

        var keys = new HashSet<string> { resp1.PoolKey, resp2.PoolKey };
        Assert.Contains("alpha:80", keys);
        Assert.Contains("beta:443", keys);
    }

    [Fact(Timeout = 15_000, DisplayName = "POOL-INT-003: Pool under load — max reached → queued → connection freed → drained")]
    public async Task PoolUnderLoad_RequestsQueued_ThenDrained()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

        var router = CreateRouter();
        router.Tell(new PoolRouterActor.RegisterHost("load:80",
            new TcpOptions { Host = "load", Port = 80 }));

        var (upstream, downstream) = MaterializeStage(router);
        var upSub = await upstream.ExpectSubscriptionAsync(CancellationToken.None);
        var downSub = await downstream.ExpectSubscriptionAsync(CancellationToken.None);
        downSub.Request(10);

        // Request 1 → spawns connection (MaxConnectionsPerHost=1)
        upSub.SendNext(MakeRoutedData("load:80", [0x01]));
        var connectionRef = ExpectConnectionSpawned();

        // Request 2 → queued (max reached, connection busy)
        upSub.SendNext(MakeRoutedData("load:80", [0x02]));
        ExpectNoMsg(TimeSpan.FromMilliseconds(300)); // No second connection spawned

        var hostPool = ResolveHostPool(router, "load:80");

        // Response for request 1
        hostPool.Tell(new HostPoolActor.ConnectionResponse(
            connectionRef, MakeResponseMemory(0xAA), 1));

        var resp1 = await downstream.ExpectNextAsync(CancellationToken.None);
        Assert.Equal(0xAA, resp1.Memory.Memory.Span[0]);

        // Connection freed → DrainPending sends queued request 2 to connection
        hostPool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // Response for request 2 (now drained to the connection)
        hostPool.Tell(new HostPoolActor.ConnectionResponse(
            connectionRef, MakeResponseMemory(0xBB), 1));

        var resp2 = await downstream.ExpectNextAsync(CancellationToken.None);
        Assert.Equal(0xBB, resp2.Memory.Memory.Span[0]);
    }

    [Fact(Timeout = 15_000, DisplayName = "POOL-INT-004: Connection failure → reconnect → subsequent request succeeds")]
    public async Task ConnectionFailure_Reconnect_SubsequentRequestSucceeds()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

        var router = CreateRouter();
        router.Tell(new PoolRouterActor.RegisterHost("fail:80",
            new TcpOptions { Host = "fail", Port = 80 }));

        var (upstream, downstream) = MaterializeStage(router);
        var upSub = await upstream.ExpectSubscriptionAsync(CancellationToken.None);
        var downSub = await downstream.ExpectSubscriptionAsync(CancellationToken.None);
        downSub.Request(10);

        // Request 1 → spawns connection
        upSub.SendNext(MakeRoutedData("fail:80", [0x01]));
        var conn1 = ExpectConnectionSpawned();

        var hostPool = ResolveHostPool(router, "fail:80");

        // Connection fails
        hostPool.Tell(new HostPoolActor.ConnectionFailed(conn1));

        // Reconnect fires after ReconnectInterval (200ms) → new connection spawns
        var conn2 = ExpectConnectionSpawned(TimeSpan.FromSeconds(3));
        Assert.NotEqual(conn1, conn2);

        // Request 2 → uses the new idle connection (no additional spawn)
        upSub.SendNext(MakeRoutedData("fail:80", [0x02]));
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // Response for request 2 via new connection
        hostPool.Tell(new HostPoolActor.ConnectionResponse(
            conn2, MakeResponseMemory(0xDD, 0xEE), 2));

        var response = await downstream.ExpectNextAsync(CancellationToken.None);
        Assert.Equal("fail:80", response.PoolKey);
        Assert.Equal(2, response.Length);
        Assert.Equal(0xDD, response.Memory.Memory.Span[0]);
        Assert.Equal(0xEE, response.Memory.Memory.Span[1]);
    }
}
