using System.Buffers;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.StreamTests.IO;

public sealed class ConnectionPoolStageTests : StreamTestBase
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private static DataItem MakeDataItem(byte[] payload)
    {
        var owner = MemoryPool<byte>.Shared.Rent(payload.Length);
        payload.CopyTo(owner.Memory.Span);
        return new DataItem(owner, payload.Length);
    }

    private static RoutedTransportItem MakeRoutedData(string poolKey, byte[] payload)
    {
        return new RoutedTransportItem(poolKey, MakeDataItem(payload));
    }

    private static RoutedTransportItem MakeRoutedConnect(string poolKey)
    {
        return new RoutedTransportItem(
            poolKey,
            new ConnectItem(new TcpOptions { Host = "example.com", Port = 80 }));
    }

    /// <summary>
    /// Wires up the ConnectionPoolStage with manual publisher/subscriber probes
    /// for precise demand control. Returns the upstream subscription and downstream probe.
    /// </summary>
    private (TestPublisher.ManualProbe<RoutedTransportItem> upstream,
             TestSubscriber.ManualProbe<RoutedDataItem> downstream)
        MaterializeWithProbes(TestProbe router)
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
        })).Run(Materializer);

        return (upstream, downstream);
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "POOL-STAGE-001: DataItem is forwarded as SendRequest to the router")]
    public async Task DataItem_Forwarded_As_SendRequest()
    {
        var router = CreateTestProbe();
        var (upstream, downstream) = MaterializeWithProbes(router);

        var upSub = await upstream.ExpectSubscriptionAsync(CancellationToken.None);
        var downSub = await downstream.ExpectSubscriptionAsync(CancellationToken.None);

        downSub.Request(1);

        var item = MakeRoutedData("host-a", [0x01, 0x02]);
        upSub.SendNext(item);

        var msg = router.ExpectMsg<PoolRouterActor.SendRequest>();
        Assert.Equal("host-a", msg.PoolKey);
        Assert.Same(((DataItem)item.Item).Memory, msg.Data.Memory);
        Assert.Equal(2, msg.Data.Length);
    }

    [Fact(Timeout = 10_000, DisplayName = "POOL-STAGE-002: StageActor ref is used as ReplyTo in SendRequest")]
    public async Task StageActor_Ref_Used_As_ReplyTo()
    {
        var router = CreateTestProbe();
        var (upstream, downstream) = MaterializeWithProbes(router);

        var upSub = await upstream.ExpectSubscriptionAsync(CancellationToken.None);
        var downSub = await downstream.ExpectSubscriptionAsync(CancellationToken.None);

        downSub.Request(1);
        upSub.SendNext(MakeRoutedData("host-b", [0xAA]));

        var msg = router.ExpectMsg<PoolRouterActor.SendRequest>();

        // ReplyTo must be a valid actor ref (the StageActor), not the router itself
        Assert.NotNull(msg.ReplyTo);
        Assert.NotEqual(router.Ref, msg.ReplyTo);

        // Verify the ReplyTo is functional: send a response back through it
        var responseMemory = MemoryPool<byte>.Shared.Rent(3);
        new byte[] { 0xBB, 0xCC, 0xDD }.CopyTo(responseMemory.Memory.Span);
        msg.ReplyTo.Tell(new PoolRouterActor.Response("host-b", responseMemory, 3));

        var response = await downstream.ExpectNextAsync(CancellationToken.None);
        Assert.Equal("host-b", response.PoolKey);
        Assert.Equal(3, response.Length);
    }

    [Fact(Timeout = 10_000, DisplayName = "POOL-STAGE-003: After push, inlet pulls immediately (demand signaling)")]
    public async Task After_Push_Inlet_Pulls_Immediately()
    {
        var router = CreateTestProbe();
        var (upstream, downstream) = MaterializeWithProbes(router);

        var upSub = await upstream.ExpectSubscriptionAsync(CancellationToken.None);
        var downSub = await downstream.ExpectSubscriptionAsync(CancellationToken.None);

        downSub.Request(10);

        // Send first item — stage should immediately pull for another
        upSub.SendNext(MakeRoutedData("host-c", [0x01]));
        router.ExpectMsg<PoolRouterActor.SendRequest>();

        // The stage calls Pull(_inlet) right after processing the push,
        // so upstream should be able to send a second item without blocking
        upSub.SendNext(MakeRoutedData("host-c", [0x02]));
        var msg2 = router.ExpectMsg<PoolRouterActor.SendRequest>();
        Assert.Equal(1, msg2.Data.Length);

        // And a third — proving continuous demand
        upSub.SendNext(MakeRoutedData("host-c", [0x03]));
        var msg3 = router.ExpectMsg<PoolRouterActor.SendRequest>();
        Assert.Equal(1, msg3.Data.Length);
    }

    [Fact(Timeout = 10_000, DisplayName = "POOL-STAGE-004: Non-DataItem (ConnectItem) does not crash, inlet pulls again")]
    public async Task NonDataItem_Handled_Gracefully()
    {
        var router = CreateTestProbe();
        var (upstream, downstream) = MaterializeWithProbes(router);

        var upSub = await upstream.ExpectSubscriptionAsync(CancellationToken.None);
        var downSub = await downstream.ExpectSubscriptionAsync(CancellationToken.None);

        downSub.Request(10);

        // Send a ConnectItem (non-DataItem) — should NOT crash
        upSub.SendNext(MakeRoutedConnect("host-d"));

        // Router should NOT have received a SendRequest for ConnectItem
        router.ExpectNoMsg(TimeSpan.FromMilliseconds(200));

        // Stage should still be alive and pulling — send a DataItem afterward
        upSub.SendNext(MakeRoutedData("host-d", [0xFF]));
        var msg = router.ExpectMsg<PoolRouterActor.SendRequest>();
        Assert.Equal("host-d", msg.PoolKey);
        Assert.Equal(1, msg.Data.Length);
    }
}
