using System.Buffers;
using System.Net;
using System.Threading.Channels;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit;
using TurboHttp.Internal;
using TurboHttp.IO;
using TurboHttp.IO.Stages;
using TurboHttp.Lifecycle;
using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Stream-level tests for <see cref="ConnectionStage"/>.
/// Uses stub actors and in-memory channels to isolate ConnectionStage from real TCP.
/// </summary>
public sealed class ConnectionStageTests : StreamTestBase
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Stub router that handles EnsureHost by replying with a pre-built
    /// <see cref="ConnectionHandle"/>, and forwards the message to a probe.
    /// </summary>
    private sealed class StubRouter : ReceiveActor
    {
        public StubRouter(ConnectionHandle handle, IActorRef probe)
        {
            Receive<PoolRouter.EnsureHost>(msg =>
            {
                Sender.Tell(handle);
                probe.Tell(msg);
            });
        }
    }

    private static readonly RequestEndpoint TestKey = new()
    {
        Host = "localhost",
        Port = 8080,
        Scheme = "http",
        Version = HttpVersion.Version11
    };

    private static DataItem MakeData(byte value, int length = 4)
    {
        var owner = MemoryPool<byte>.Shared.Rent(length);
        owner.Memory.Span[..length].Fill(value);
        return new DataItem(owner, length) { Key = TestKey };
    }

    /// <summary>
    /// Creates in-memory channels simulating the TCP connection,
    /// builds a <see cref="ConnectionHandle"/>, and wires a <see cref="ConnectionStage"/>.
    /// </summary>
    private (
        Flow<IOutputItem, IInputItem, NotUsed> stageFlow,
        ChannelReader<(IMemoryOwner<byte> Buffer, int ReadableBytes)> outboundReader,
        ChannelWriter<(IMemoryOwner<byte> Buffer, int ReadableBytes)> inboundWriter,
        TestProbe routerProbe)
        Build(IActorRef? connectionActor = null)
    {
        // Outbound channel: stage writes here, test reads to verify
        var outbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();

        // Inbound channel: test writes here, stage reads and pushes to outlet
        var inbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();

        var handle = new ConnectionHandle(
            outbound.Writer,
            inbound.Reader,
            TestKey,
            connectionActor ?? ActorRefs.Nobody);

        var routerProbe = CreateTestProbe();
        var stubRouter = Sys.ActorOf(Props.Create(() =>
            new StubRouter(handle, routerProbe.Ref)));

        var stageFlow = Flow.FromGraph(new ConnectionStage(stubRouter));

        return (stageFlow, outbound.Reader, inbound.Writer, routerProbe);
    }

    // ── CS-001: ConnectItem triggers EnsureHost to PoolRouter ────────────────

    [Fact(Timeout = 15_000,
        DisplayName = "CS-001: ConnectItem pushed into inlet triggers EnsureHost to PoolRouter")]
    public async Task Should_TriggerEnsureHost_When_ConnectItemPushedToInlet()
    {
        var (stageFlow, _, inboundWriter, routerProbe) = Build();
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        var connectItem = new ConnectItem(options)
        {
            Key = new RequestEndpoint { Host = "localhost", Port = 8080, Scheme = "Https", Version = HttpVersion.Unknown }
        };

        var (queue, _) = Source.Queue<IOutputItem>(4, OverflowStrategy.Backpressure)
            .Via(stageFlow)
            .ToMaterialized(Sink.Ignore<IInputItem>(), Keep.Both)
            .Run(Materializer);

        await queue.OfferAsync(connectItem);

        var received = await routerProbe.ExpectMsgAsync<PoolRouter.EnsureHost>(TimeSpan.FromSeconds(10));
        Assert.Equal("localhost", received.Options.Host);

        inboundWriter.Complete();
    }

    // ── CS-002: Inbound data from channel appears at outlet ──────────────────

    [Fact(Timeout = 15_000,
        DisplayName = "CS-002: Inbound data from ConnectionHandle.InboundReader appears at outlet")]
    public async Task Should_ReachOutlet_When_InboundDataWrittenToChannel()
    {
        var (stageFlow, _, inboundWriter, _) = Build();
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        var connectItem = new ConnectItem(options)
        {
            Key = new RequestEndpoint { Host = "localhost", Port = 8080, Scheme = "Https", Version = HttpVersion.Unknown }
        };

        var (inputQueue, resultTask) = Source.Queue<IOutputItem>(4, OverflowStrategy.Backpressure)
            .Via(stageFlow)
            .ToMaterialized(Sink.First<IInputItem>(), Keep.Both)
            .Run(Materializer);

        // Push ConnectItem → triggers EnsureHost → StubRouter replies with ConnectionHandle
        await inputQueue.OfferAsync(connectItem);

        // Wait for the handle to be received and inbound pump to start
        await Task.Delay(300);

        // Inject data through the inbound channel
        var owner = MemoryPool<byte>.Shared.Rent(4);
        owner.Memory.Span[..4].Fill(0xAB);
        await inboundWriter.WriteAsync((owner, 4));

        var received = (DataItem)await resultTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(4, received.Length);
        Assert.Equal(0xAB, received.Memory.Memory.Span[0]);

        inboundWriter.Complete();
    }

    // ── CS-003: Outbound DataItem written to ConnectionHandle.OutboundWriter ─

    [Fact(Timeout = 15_000,
        DisplayName = "CS-003: DataItem pushed to inlet is written to ConnectionHandle.OutboundWriter")]
    public async Task Should_WriteToOutboundChannel_When_DataItemPushedToInlet()
    {
        var (stageFlow, outboundReader, inboundWriter, _) = Build();
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        var connectItem = new ConnectItem(options)
        {
            Key = new RequestEndpoint { Host = "localhost", Port = 8080, Scheme = "Https", Version = HttpVersion.Unknown }
        };
        var data = MakeData(0xCD, 8);

        var (inputQueue, _) = Source.Queue<IOutputItem>(4, OverflowStrategy.Backpressure)
            .Via(stageFlow)
            .ToMaterialized(Sink.Ignore<IInputItem>(), Keep.Both)
            .Run(Materializer);

        // Connect first — stage needs a handle before accepting DataItems
        await inputQueue.OfferAsync(connectItem);
        await Task.Delay(300);

        // Push a DataItem
        await inputQueue.OfferAsync(data);

        // Read from outbound channel and verify
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (buffer, length) = await outboundReader.ReadAsync(cts.Token);
        Assert.Equal(8, length);
        Assert.Equal(0xCD, buffer.Memory.Span[0]);

        buffer.Dispose();
        inboundWriter.Complete();
    }

    // ── CS-004: End-to-end byte flow through ConnectionStage ─────────────────

    [Fact(Timeout = 15_000,
        DisplayName = "CS-004: Full round-trip — outbound DataItem written, inbound data read")]
    public async Task Should_CompleteRoundTrip_When_OutboundWrittenAndInboundRead()
    {
        var (stageFlow, outboundReader, inboundWriter, _) = Build();
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        var connectItem = new ConnectItem(options)
        {
            Key = new RequestEndpoint { Host = "localhost", Port = 8080, Scheme = "Https", Version = HttpVersion.Unknown }
        };

        // Materialize with a queue sink so we can pull multiple items
        var (inputQueue, resultTask) = Source.Queue<IOutputItem>(4, OverflowStrategy.Backpressure)
            .Via(stageFlow)
            .ToMaterialized(Sink.Seq<IInputItem>(), Keep.Both)
            .Run(Materializer);

        // 1. Connect
        await inputQueue.OfferAsync(connectItem);
        await Task.Delay(300);

        // 2. Write outbound data
        var outData = MakeData(0x01, 16);
        await inputQueue.OfferAsync(outData);

        // 3. Verify outbound data arrives on the channel
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (outBuffer, outLength) = await outboundReader.ReadAsync(cts.Token);
        Assert.Equal(16, outLength);
        Assert.Equal(0x01, outBuffer.Memory.Span[0]);
        outBuffer.Dispose();

        // 4. Inject inbound data
        var inOwner = MemoryPool<byte>.Shared.Rent(12);
        inOwner.Memory.Span[..12].Fill(0x02);
        await inboundWriter.WriteAsync((inOwner, 12));

        // 5. Complete and collect
        await Task.Delay(300);
        inboundWriter.Complete();
        inputQueue.Complete();

        var results = await resultTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(results);

        var inbound = (DataItem)results[0];
        Assert.Equal(12, inbound.Length);
        Assert.Equal(0x02, inbound.Memory.Memory.Span[0]);
    }

    // ── CS-005: ConnectionReuseItem CanReuse=false sends MarkConnectionNoReuse + StreamCompleted ─

    [Fact(Timeout = 15_000,
        DisplayName =
            "CS-005: ConnectionReuseItem with CanReuse=false sends MarkConnectionNoReuse and StreamCompleted")]
    public async Task Should_SendMarkNoReuseAndStreamCompleted_When_ConnectionReuseItemCanReuseIsFalse()
    {
        var connectionActorProbe = CreateTestProbe();
        var (stageFlow, _, inboundWriter, _) = Build(connectionActorProbe.Ref);
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        var connectItem = new ConnectItem(options)
        {
            Key = new RequestEndpoint { Host = "localhost", Port = 8080, Scheme = "Https", Version = HttpVersion.Unknown }
        };

        var (inputQueue, _) = Source.Queue<IOutputItem>(4, OverflowStrategy.Backpressure)
            .Via(stageFlow)
            .ToMaterialized(Sink.Ignore<IInputItem>(), Keep.Both)
            .Run(Materializer);

        // Connect first — stage needs a handle
        await inputQueue.OfferAsync(connectItem);
        await Task.Delay(300);

        // Push a ConnectionReuseItem with CanReuse = false
        var decision = ConnectionReuseDecision.Close("Connection: close");
        var reuseItem = new ConnectionReuseItem(TestKey, decision);
        await inputQueue.OfferAsync(reuseItem);

        // Verify that MarkConnectionNoReuse is sent first
        var markMsg =
            await connectionActorProbe.ExpectMsgAsync<HostPool.MarkConnectionNoReuse>(TimeSpan.FromSeconds(5));
        Assert.Equal(connectionActorProbe.Ref, markMsg.Connection);

        // Verify that StreamCompleted is sent second
        var streamMsg =
            await connectionActorProbe.ExpectMsgAsync<HostPool.StreamCompleted>(TimeSpan.FromSeconds(5));
        Assert.Equal(connectionActorProbe.Ref, streamMsg.Connection);

        inboundWriter.Complete();
    }

    // ── CS-006: ConnectionReuseItem CanReuse=true sends only StreamCompleted ─

    [Fact(Timeout = 15_000,
        DisplayName = "CS-006: ConnectionReuseItem with CanReuse=true sends only StreamCompleted (no MarkNoReuse)")]
    public async Task Should_SendOnlyStreamCompleted_When_ConnectionReuseItemCanReuseIsTrue()
    {
        var connectionActorProbe = CreateTestProbe();
        var (stageFlow, _, inboundWriter, _) = Build(connectionActorProbe.Ref);
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        var connectItem = new ConnectItem(options)
        {
            Key = new RequestEndpoint { Host = "localhost", Port = 8080, Scheme = "Https", Version = HttpVersion.Unknown }
        };

        var (inputQueue, _) = Source.Queue<IOutputItem>(4, OverflowStrategy.Backpressure)
            .Via(stageFlow)
            .ToMaterialized(Sink.Ignore<IInputItem>(), Keep.Both)
            .Run(Materializer);

        // Connect first
        await inputQueue.OfferAsync(connectItem);
        await Task.Delay(300);

        // Push a ConnectionReuseItem with CanReuse = true
        var decision = ConnectionReuseDecision.KeepAlive("HTTP/1.1 persistent");
        var reuseItem = new ConnectionReuseItem(TestKey, decision);
        await inputQueue.OfferAsync(reuseItem);

        // Verify that only StreamCompleted is sent (no MarkConnectionNoReuse)
        var streamMsg =
            await connectionActorProbe.ExpectMsgAsync<HostPool.StreamCompleted>(TimeSpan.FromSeconds(5));
        Assert.Equal(connectionActorProbe.Ref, streamMsg.Connection);

        // No further messages (specifically no MarkConnectionNoReuse)
        await connectionActorProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500));

        inboundWriter.Complete();
    }

    // ── CS-007: MaxConcurrentStreamsItem forwarded to ConnectionActor ─

    [Fact(Timeout = 15_000,
        DisplayName =
            "CS-007: MaxConcurrentStreamsItem(50) forwarded as UpdateMaxConcurrentStreams to ConnectionActor")]
    public async Task Should_ForwardUpdateMaxConcurrentStreams_When_MaxConcurrentStreamsItemReceived()
    {
        var connectionActorProbe = CreateTestProbe();
        var (stageFlow, _, inboundWriter, _) = Build(connectionActorProbe.Ref);
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        var connectItem = new ConnectItem(options)
        {
            Key = new RequestEndpoint { Host = "localhost", Port = 8080, Scheme = "Https", Version = HttpVersion.Unknown }
        };

        var (inputQueue, _) = Source.Queue<IOutputItem>(4, OverflowStrategy.Backpressure)
            .Via(stageFlow)
            .ToMaterialized(Sink.Ignore<IInputItem>(), Keep.Both)
            .Run(Materializer);

        // Connect first — stage needs a handle
        await inputQueue.OfferAsync(connectItem);
        await Task.Delay(300);

        // Push MaxConcurrentStreamsItem
        await inputQueue.OfferAsync(new MaxConcurrentStreamsItem(50));

        // Verify UpdateMaxConcurrentStreams is sent to the connection actor
        var msg =
            await connectionActorProbe
                .ExpectMsgAsync<HostPool.UpdateMaxConcurrentStreams>(TimeSpan.FromSeconds(5));
        Assert.Equal(connectionActorProbe.Ref, msg.Connection);
        Assert.Equal(50, msg.MaxStreams);

        inboundWriter.Complete();
    }

    // ── CS-008: StreamAcquireItem forwarded to ConnectionActor ─

    [Fact(Timeout = 15_000,
        DisplayName = "CS-008: StreamAcquireItem forwarded as StreamAcquired to ConnectionActor")]
    public async Task Should_ForwardStreamAcquired_When_StreamAcquireItemReceived()
    {
        var connectionActorProbe = CreateTestProbe();
        var (stageFlow, _, inboundWriter, _) = Build(connectionActorProbe.Ref);
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        var connectItem = new ConnectItem(options)
        {
            Key = new RequestEndpoint { Host = "localhost", Port = 8080, Scheme = "Https", Version = HttpVersion.Unknown }
        };

        var (inputQueue, _) = Source.Queue<IOutputItem>(4, OverflowStrategy.Backpressure)
            .Via(stageFlow)
            .ToMaterialized(Sink.Ignore<IInputItem>(), Keep.Both)
            .Run(Materializer);

        // Connect first — stage needs a handle
        await inputQueue.OfferAsync(connectItem);
        await Task.Delay(300);

        // Push StreamAcquireItem
        await inputQueue.OfferAsync(new StreamAcquireItem());

        // Verify StreamAcquired is sent to the connection actor
        var msg = await connectionActorProbe.ExpectMsgAsync<HostPool.StreamAcquired>(TimeSpan.FromSeconds(5));
        Assert.Equal(connectionActorProbe.Ref, msg.Connection);

        inboundWriter.Complete();
    }
}