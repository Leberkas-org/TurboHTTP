using System;
using System.Buffers;
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
    /// A minimal router stub that responds to GetPoolRefs with pre-built refs.
    /// Avoids any TCP infrastructure.
    /// </summary>
    private sealed class StubRouter : ReceiveActor
    {
        public StubRouter(ISinkRef<ITransportItem> sinkRef, ISourceRef<IDataItem> sourceRef)
        {
            Receive<PoolRouterActor.GetPoolRefs>(_ =>
                Sender.Tell(new PoolRouterActor.PoolRefs(sinkRef, sourceRef)));
        }
    }

    private static DataItem MakeData(byte value, int length = 4)
    {
        var owner = MemoryPool<byte>.Shared.Rent(length);
        owner.Memory.Span[..length].Fill(value);
        return new DataItem(owner, length);
    }

    /// <summary>
    /// Builds a pre-materialized SinkRef→probe pair and a SourceRef→queue pair,
    /// then wires a ConnectionStage to the stub router.
    /// Returns the stage flow and the two queues/probes for injection/observation.
    /// </summary>
    private async Task<(
        Flow<ITransportItem, IDataItem, NotUsed> stageFlow,
        ISourceQueueWithComplete<IDataItem> responseQueue,
        TestProbe requestProbe)>
    BuildAsync()
    {
        // Request side: SinkRef that forwards items to a TestProbe
        var requestProbe = CreateTestProbe();
        var sinkRefTask = Sink
            .ForEach<ITransportItem>(item => requestProbe.Tell(item))
            .RunWith(StreamRefs.SinkRef<ITransportItem>(), Materializer);
        var sinkRef = await sinkRefTask.WaitAsync(TimeSpan.FromSeconds(10));

        // Response side: SourceQueue → SourceRef that ConnectionStage subscribes to
        var (responseQueue, responseSource) = Source
            .Queue<IDataItem>(16, OverflowStrategy.Backpressure)
            .PreMaterialize(Materializer);
        var sourceRefTask = responseSource.RunWith(
            StreamRefs.SourceRef<IDataItem>(), Materializer);
        var sourceRef = await sourceRefTask.WaitAsync(TimeSpan.FromSeconds(10));

        // Stub router responds to GetPoolRefs
        var stubRouter = Sys.ActorOf(Props.Create(() => new StubRouter(sinkRef, sourceRef)));
        var stageFlow = Flow.FromGraph(new ConnectionStage(stubRouter));

        return (stageFlow, responseQueue, requestProbe);
    }

    // ── CS-001: requests reach the PoolRouter's SinkRef ──────────────────────

    [Fact(Timeout = 15_000,
        DisplayName = "CS-001: ConnectItem pushed into inlet reaches PoolRouter's SinkRef")]
    public async Task CS_001_RequestReachesSinkRef()
    {
        var (stageFlow, responseQueue, requestProbe) = await BuildAsync();
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        var connectItem = new ConnectItem(options);

        // Run the stage: push one ConnectItem, collect nothing (no responses)
        var stageSource = Source.Queue<ITransportItem>(4, OverflowStrategy.Backpressure)
            .Via(stageFlow);

        var (queue, _) = stageSource
            .ToMaterialized(Sink.Ignore<IDataItem>(), Keep.Both)
            .Run(Materializer);

        await Task.Delay(300); // let ConnectionStage establish the sub-streams

        await queue.OfferAsync(connectItem);

        var received = requestProbe.ExpectMsg<ConnectItem>(TimeSpan.FromSeconds(10));
        Assert.Equal("localhost", received.Options.Host);

        // Cleanup
        responseQueue.Complete();
    }

    // ── CS-002: responses from SourceRef appear at outlet ────────────────────

    [Fact(Timeout = 15_000,
        DisplayName = "CS-002: DataItem injected into PoolRouter's SourceRef appears at outlet")]
    public async Task CS_002_ResponseReachesOutlet()
    {
        var (stageFlow, responseQueue, _) = await BuildAsync();
        var data = MakeData(0xAB, 4);

        // Run the stage: no inbound items, collect from outlet
        var resultTask = Source.Queue<ITransportItem>(4, OverflowStrategy.Backpressure)
            .Via(stageFlow)
            .RunWith(Sink.First<IDataItem>(), Materializer);

        await Task.Delay(300); // let ConnectionStage establish the sub-streams

        // Inject a response via the SourceRef-backed queue
        await responseQueue.OfferAsync(data);

        var received = await resultTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(4, received.Length);
        Assert.Equal(0xAB, received.Memory.Memory.Span[0]);

        // Cleanup
        responseQueue.Complete();
    }
}
