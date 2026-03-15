using System;
using System.Buffers;
using System.Net;
using System.Threading.Tasks;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Stream-level tests for <see cref="ConnectionStage"/>.
/// Uses stub actors to isolate ConnectionStage from real TCP.
/// </summary>
public sealed class ConnectionStageTests : StreamTestBase
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Stub router that handles GetGlobalRefs (replies with pre-built GlobalRefs)
    /// and forwards EnsureHost messages to a probe for assertion.
    /// </summary>
    private sealed class StubRouter : ReceiveActor
    {
        public StubRouter(
            ISourceQueueWithComplete<DataItem> requestQueue,
            Source<DataItem, NotUsed> responseSource,
            IActorRef probe)
        {
            Receive<PoolRouterActor.GetGlobalRefs>(_ =>
            {
                Sender.Tell(new PoolRouterActor.GlobalRefs(requestQueue, responseSource));
            });

            Receive<PoolRouterActor.EnsureHost>(msg =>
            {
                probe.Tell(msg);
            });
        }
    }

    private static DataItem MakeData(byte value, int length = 4)
    {
        var owner = MemoryPool<byte>.Shared.Rent(length);
        owner.Memory.Span[..length].Fill(value);
        return new DataItem(owner, length);
    }

    /// <summary>
    /// Builds a pre-materialized request queue + response queue pair,
    /// then wires a ConnectionStage to the stub router.
    /// Returns the stage flow, the response queue (for injecting inbound data),
    /// and a probe that records EnsureHost messages.
    /// </summary>
    private (
        Flow<IOutputItem, IInputItem, NotUsed> stageFlow,
        ISourceQueueWithComplete<DataItem> responseQueue,
        TestProbe routerProbe)
    Build()
    {
        // Response side: Source.Queue that ConnectionStage will subscribe to
        var (responseQueue, responseSource) = Source
            .Queue<DataItem>(16, OverflowStrategy.Backpressure)
            .PreMaterialize(Materializer);

        // Request side: outbound queue (ConnectionStage offers DataItems here)
        var (requestQueue, _) = Source
            .Queue<DataItem>(16, OverflowStrategy.Backpressure)
            .PreMaterialize(Materializer);

        var routerProbe = CreateTestProbe();
        var stubRouter = Sys.ActorOf(Props.Create(() =>
            new StubRouter(requestQueue, responseSource, routerProbe.Ref)));

        var stageFlow = Flow.FromGraph(new ConnectionStage(stubRouter));

        return (stageFlow, responseQueue, routerProbe);
    }

    // ── CS-001: ConnectItem triggers EnsureHost to PoolRouter ────────────────

    [Fact(Timeout = 15_000,
        DisplayName = "CS-001: ConnectItem pushed into inlet triggers EnsureHost to PoolRouter")]
    public async Task CS_001_ConnectItem_TriggersEnsureHost()
    {
        var (stageFlow, responseQueue, routerProbe) = Build();
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        var connectItem = new ConnectItem(options, HttpVersion.Version11);

        var (queue, _) = Source.Queue<IOutputItem>(4, OverflowStrategy.Backpressure)
            .Via(stageFlow)
            .ToMaterialized(Sink.Ignore<IInputItem>(), Keep.Both)
            .Run(Materializer);

        await Task.Delay(200);
        await queue.OfferAsync(connectItem);

        var received = routerProbe.ExpectMsg<PoolRouterActor.EnsureHost>(TimeSpan.FromSeconds(10));
        Assert.Equal("localhost", received.Options.Host);

        responseQueue.Complete();
    }

    // ── CS-002: responses from ResponseSource appear at outlet ───────────────

    [Fact(Timeout = 15_000,
        DisplayName = "CS-002: DataItem injected into ResponseSource appears at outlet")]
    public async Task CS_002_ResponseReachesOutlet()
    {
        var (stageFlow, responseQueue, _) = Build();
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        var connectItem = new ConnectItem(options, HttpVersion.Version11);
        var data = MakeData(0xAB, 4);

        var (inputQueue, resultTask) = Source.Queue<IOutputItem>(4, OverflowStrategy.Backpressure)
            .Via(stageFlow)
            .ToMaterialized(Sink.First<IInputItem>(), Keep.Both)
            .Run(Materializer);

        await Task.Delay(200);

        // Push ConnectItem → triggers GetGlobalRefs → StubRouter replies with GlobalRefs
        await inputQueue.OfferAsync(connectItem);

        // Wait for GlobalRefs to be received and ResponseSource subscription to establish
        await Task.Delay(400);

        // Inject a DataItem through the response queue
        await responseQueue.OfferAsync(data);

        var received = (DataItem)await resultTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(4, received.Length);
        Assert.Equal(0xAB, received.Memory.Memory.Span[0]);

        responseQueue.Complete();
    }
}
