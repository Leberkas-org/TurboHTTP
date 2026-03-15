using System;
using System.Net;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Hosting;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.StreamTests.IO;

public sealed class ActorHierarchyStreamRefTests : StreamTestBase
{
    private static TcpOptions MakeOptions(string host = "host-a", int port = 80)
        => new() { Host = host, Port = port };

    // ── ETE-001: Full hierarchy traversal ─────────────────────────────

    [Fact(DisplayName =
        "ETE-001: EnsureHost sent to PoolRouterActor traverses full hierarchy and reaches ClientManager")]
    public async Task ETE_001_FullHierarchy_ConnectItemReachesClientManager()
    {
        // Register a TestProbe as the ClientManager BEFORE creating PoolRouterActor so
        // that HostPoolActor.SpawnConnection() → Context.GetActor<ClientManager>() resolves
        // to the probe instead of throwing MissingActorRegistryEntryException.
        var clientManagerProbe = CreateTestProbe();
        ActorRegistry.For(Sys).Register<ClientManager>(clientManagerProbe.Ref);

        // Create PoolRouterActor with the real default factory (creates real HostPoolActors)
        var router = Sys.ActorOf(Props.Create(() => new PoolRouterActor()));

        var options = MakeOptions();
        var connectItem = new ConnectItem(options, HttpVersion.Version11);
        var key = connectItem.Key;

        // GetGlobalRefs verifies the router is fully initialized after PreStart.
        router.Tell(new PoolRouterActor.GetGlobalRefs(), TestActor);
        await ExpectMsgAsync<PoolRouterActor.GlobalRefs>(TimeSpan.FromSeconds(5));

        // EnsureHost — PoolRouterActor creates a HostPoolActor which eagerly
        // spawns a ConnectionActor in PreStart(), triggering CreateTcpRunner.
        router.Tell(new PoolRouterActor.EnsureHost(key, options));

        var connectCreateMsg =
            clientManagerProbe.ExpectMsg<ClientManager.CreateTcpRunner>(TimeSpan.FromSeconds(10));

        Assert.Equal(options.Host, connectCreateMsg.Options.Host);
        Assert.Equal(options.Port, connectCreateMsg.Options.Port);
    }
}
