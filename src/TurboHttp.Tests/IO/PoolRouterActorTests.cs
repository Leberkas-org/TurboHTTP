using System.Net;
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

    /// <summary>
    /// A test-only ITransportItem that carries an explicit HostKey,
    /// exercising the non-ConnectItem routing branch in PoolRouterActor.RouteItem.
    /// </summary>
    private sealed record KeyedItem(HostKey ExplicitKey) : IOutputItem
    {
        HostKey IOutputItem.Key => ExplicitKey;
    }

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
            new PoolRouterActor(null, (opts, cfg, key) => hostProbe.Ref)));

        var mat = Sys.Materializer();

        actor.Tell(new PoolRouterActor.GetPoolRefs(), TestActor);
        var refs = await ExpectMsgAsync<PoolRouterActor.PoolRefs>(TimeSpan.FromSeconds(10));

        await Task.Delay(200); // let SinkRef stream subscription establish

        var options = MakeOptions("localhost", 8080);
        Source.Single<IOutputItem>(new ConnectItem(options))
            .RunWith(refs.Sink.Sink, mat);

        var received = hostProbe.ExpectMsg<ConnectItem>(TimeSpan.FromSeconds(5));
        Assert.Equal("localhost", received.Options.Host);
        Assert.Equal(8080, received.Options.Port);
    }

    // ── PR-003: PoolRouterActor routes keyed item to correct host ─────

    [Fact(DisplayName = "PR-003: KeyedItem with HostKey (http,host-a,80) is routed to the correct HostPoolActor")]
    public async Task PR_003_KeyedItem_RoutedToCorrectHostActor()
    {
        var hostProbe = CreateTestProbe();
        var actor = Sys.ActorOf(Props.Create(() =>
            new PoolRouterActor(null, (opts, cfg, key) => hostProbe.Ref)));

        var mat = Sys.Materializer();

        actor.Tell(new PoolRouterActor.GetPoolRefs(), TestActor);
        var refs = await ExpectMsgAsync<PoolRouterActor.PoolRefs>(TimeSpan.FromSeconds(10));

        // Use a queue-based Source so we can push two items through the same SinkRef subscription
        var (queue, queueSource) = Source
            .Queue<IOutputItem>(4, OverflowStrategy.Backpressure)
            .PreMaterialize(mat);
        queueSource.RunWith(refs.Sink.Sink, mat);

        await Task.Delay(200); // let subscription establish

        // Push ConnectItem first to register the host mapping
        var connectOptions = MakeOptions("host-a", 80);
        await queue.OfferAsync(new ConnectItem(connectOptions));
        hostProbe.ExpectMsg<ConnectItem>(TimeSpan.FromSeconds(5));

        // Push a KeyedItem with the same HostKey — must route to the same hostProbe
        var key = new HostKey { Schema = "http", Host = "host-a", Port = 80, HttpVersion = HttpVersion.Unknown };
        await queue.OfferAsync(new KeyedItem(key));

        var routed = hostProbe.ExpectMsg<KeyedItem>(TimeSpan.FromSeconds(5));
        Assert.Equal("host-a", routed.ExplicitKey.Host);
        Assert.Equal((ushort)80, routed.ExplicitKey.Port);
        Assert.Equal("http", routed.ExplicitKey.Schema);

        queue.Complete();
    }
}