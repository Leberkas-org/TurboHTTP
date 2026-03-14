using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit2;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.Tests.IO;

public sealed class PoolRouterActorTests : TestKit
{
    private static TcpOptions MakeOptions(string host = "localhost", int port = 8080)
        => new() { Host = host, Port = port };

    // ── PR-001: GetPoolRefs returns valid SinkRef + SourceRef ─────────

    [Fact(DisplayName = "PR-001: GetPoolRefs returns valid SinkRef + SourceRef after actor start")]
    public async Task PR_001_GetPoolRefs_ReturnsValidRefs()
    {
        var actor = Sys.ActorOf(Props.Create(() => new PoolRouterActor()));

        actor.Tell(new PoolRouterActor.GetPoolRefs(), TestActor);

        var refs = await ExpectMsgAsync<PoolRouterActor.PoolRefs>(TimeSpan.FromSeconds(10));

        Assert.NotNull(refs.Sink);
        Assert.NotNull(refs.Source);
    }

    // ── PR-002: ConnectItem routed to HostPoolActor by derived HostKey ─

    [Fact(DisplayName = "PR-002: ConnectItem pushed into SinkRef is forwarded to the correct HostPoolActor")]
    public async Task PR_002_ConnectItem_ForwardedToHostPoolActor()
    {
        var hostProbe = CreateTestProbe();
        var actor = Sys.ActorOf(Props.Create(() =>
            new PoolRouterActor(null, (opts, cfg) => hostProbe.Ref)));

        var mat = Sys.Materializer();

        actor.Tell(new PoolRouterActor.GetPoolRefs(), TestActor);
        var refs = await ExpectMsgAsync<PoolRouterActor.PoolRefs>(TimeSpan.FromSeconds(10));

        await Task.Delay(200); // let SinkRef stream subscription establish

        var options = MakeOptions("localhost", 8080);
        Source.Single<ITransportItem>(new ConnectItem(options))
            .RunWith(refs.Sink.Sink, mat);

        var received = hostProbe.ExpectMsg<ConnectItem>(TimeSpan.FromSeconds(5));
        Assert.Equal("localhost", received.Options.Host);
        Assert.Equal(8080, received.Options.Port);
    }
}
