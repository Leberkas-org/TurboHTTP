using System;
using System.Buffers;
using System.Net;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using Akka.Streams.Dsl;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.StreamTests.IO;

public sealed class ActorHierarchyStreamRefTests : StreamTestBase
{
    private static TcpOptions MakeOptions(string host = "host-a", int port = 80)
        => new() { Host = host, Port = port };

    // ── ETE-001: Full hierarchy traversal ─────────────────────────────

    [Fact(DisplayName = "ETE-001: ConnectItem pushed to PoolRouterActor SinkRef traverses full hierarchy and DataItem arrives in TCP outbound channel")]
    public async Task ETE_001_FullHierarchy_ItemArrivesInTcpOutboundChannel()
    {
        // Register a TestProbe as the ClientManager BEFORE creating PoolRouterActor so
        // that HostPoolActor.SpawnConnection() → Context.GetActor<ClientManager>() resolves
        // to the probe instead of throwing MissingActorRegistryEntryException.
        var clientManagerProbe = CreateTestProbe();
        ActorRegistry.For(Sys).Register<ClientManager>(clientManagerProbe.Ref);

        // Subscribe to UnhandledMessage so we can intercept the ConnectItem that
        // HostPoolActor receives but has no handler for.
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

        // Create PoolRouterActor with the real default factory (creates real HostPoolActors)
        var router = Sys.ActorOf(Props.Create(() => new PoolRouterActor()));

        // Retrieve SinkRef + SourceRef
        router.Tell(new PoolRouterActor.GetPoolRefs(), TestActor);
        var refs = await ExpectMsgAsync<PoolRouterActor.PoolRefs>(TimeSpan.FromSeconds(10));

        // Push ConnectItem through the SinkRef — PoolRouterActor creates a HostPoolActor
        // and forwards it; HostPoolActor has no ConnectItem handler → UnhandledMessage
        var options = MakeOptions();
        Source.Single<ITransportItem>(new ConnectItem(options))
            .RunWith(refs.Sink.Sink, Materializer);

        var connectUnhandled = await ExpectMsgAsync<UnhandledMessage>(TimeSpan.FromSeconds(10));
        Assert.IsType<ConnectItem>(connectUnhandled.Message);
        var hostPoolActor = connectUnhandled.Recipient;

        // Prepare inbound / outbound channels (simulating a TCP connection)
        var inbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var outbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();

        // Send DataItem directly to HostPoolActor — no connections exist yet, so it spawns
        // a ConnectionActor and enqueues the item as pending.
        var pendingOwner = MemoryPool<byte>.Shared.Rent(8);
        pendingOwner.Memory.Span[0] = 0xEE;
        hostPoolActor.Tell(new DataItem(pendingOwner, 8));

        // ConnectionActor.PreStart sends CreateTcpRunner to clientManagerProbe.
        // Intercept it directly from the probe.
        var createMsg = clientManagerProbe.ExpectMsg<ClientManager.CreateTcpRunner>(TimeSpan.FromSeconds(10));
        var connectionActor = createMsg.Handler;

        // Simulate the TCP runner signalling that the connection is established.
        // ConnectionActor materialises StreamRefs and tells HostPoolActor via
        // RegisterConnectionRefs. HostPoolActor wires the queue, then DrainPending
        // routes the pending DataItem → queue → SinkRef → ForEachAsync → outbound channel.
        var runnerProbe = CreateTestProbe();
        var endpoint = new IPEndPoint(IPAddress.Loopback, 80);
        connectionActor.Tell(
            new ClientRunner.ClientConnected(endpoint, inbound.Reader, outbound.Writer),
            runnerProbe.Ref);

        // The DataItem must arrive in the TCP outbound channel
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));
        var (outboundMem, outboundLen) = await outbound.Reader.ReadAsync(cts.Token);

        Assert.Equal(8, outboundLen);
        Assert.Equal(0xEE, outboundMem.Memory.Span[0]);

        outboundMem.Dispose();
    }
}
