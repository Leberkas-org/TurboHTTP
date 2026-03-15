using System;
using System.Buffers;
using System.Net;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
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

    [Fact(DisplayName =
        "ETE-001: ConnectItem pushed to PoolRouterActor SinkRef traverses full hierarchy and reaches ClientManager")]
    public async Task ETE_001_FullHierarchy_ConnectItemReachesClientManager()
    {
        // Register a TestProbe as the ClientManager BEFORE creating PoolRouterActor so
        // that HostPoolActor.SpawnConnection() → Context.GetActor<ClientManager>() resolves
        // to the probe instead of throwing MissingActorRegistryEntryException.
        var clientManagerProbe = CreateTestProbe();
        ActorRegistry.For(Sys).Register<ClientManager>(clientManagerProbe.Ref);

        // Create PoolRouterActor with the real default factory (creates real HostPoolActors)
        var router = Sys.ActorOf(Props.Create(() => new PoolRouterActor()));

        // Retrieve SinkRef + SourceRef
        router.Tell(new PoolRouterActor.GetPoolRefs(), TestActor);
        var refs = await ExpectMsgAsync<PoolRouterActor.PoolRefs>(TimeSpan.FromSeconds(10));

        // Allow the SinkRef stream subscription to establish before pushing items
        await Task.Delay(200);

        // Push ConnectItem through the SinkRef — PoolRouterActor creates a HostPoolActor
        // and forwards it; HostPoolActor handles ConnectItem by spawning a connection.
        var options = MakeOptions();
        Source.Single<IOutputItem>(new ConnectItem(options))
            .RunWith(refs.Sink.Sink, Materializer);

        // ConnectItem triggers SpawnConnection → CreateTcpRunner to clientManagerProbe
        var connectCreateMsg = clientManagerProbe.ExpectMsg<ClientManager.CreateTcpRunner>(TimeSpan.FromSeconds(10));

        Assert.Equal(options.Host, connectCreateMsg.Options.Host);
        Assert.Equal(options.Port, connectCreateMsg.Options.Port);
    }
}