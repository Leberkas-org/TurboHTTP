using System.Net;
using System.Threading.Channels;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http11;
using TurboHTTP.Transport.Connection;
using TurboHTTP.Transport.Tcp;

namespace TurboHTTP.StreamTests.Streams;

/// <summary>
/// Tests <see cref="TcpConnectionStage"/> integration with the actor-based connection management.
/// Verifies that the stage acquires a <see cref="ConnectionLease"/> via a
/// <see cref="ConnectionManagerActor"/> and routes bytes through in-memory channels.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="TcpConnectionStage"/>.
/// Uses a <see cref="StubConnectionManagerActor"/> to isolate the stage from real TCP,
/// while exercising the real actor message protocol.
/// </remarks>
public sealed class ConnectionStageSpec : StreamTestBase
{

    /// <summary>
    /// Tracks <see cref="ConnectionManagerActor.Release"/> details for test assertions.
    /// Written on the actor thread; read on the test thread after a delay.
    /// </summary>
    private sealed class ReleaseTracker
    {
        public volatile bool Released;
        public volatile bool ReleasedCanReuse;
        public ConnectionLease? ReleasedLease;
    }

    /// <summary>
    /// Stub actor that handles <see cref="ConnectionManagerActor.Acquire"/> and
    /// <see cref="ConnectionManagerActor.Release"/> with controllable behavior.
    /// </summary>
    private sealed class StubConnectionManagerActor : ReceiveActor
    {
        public StubConnectionManagerActor(ConnectionLease? lease, ReleaseTracker tracker)
        {
            Receive<ConnectionManagerActor.Acquire>(msg =>
            {
                if (lease is not null)
                {
                    msg.Tcs.TrySetResult(lease);
                }
                // else: never complete — simulates an actor that never returns a lease
            });

            Receive<ConnectionManagerActor.Release>(msg =>
            {
                tracker.Released = true;
                tracker.ReleasedCanReuse = msg.CanReuse;
                tracker.ReleasedLease = msg.Lease;
            });
        }

        public static Props Props(ConnectionLease? lease, ReleaseTracker tracker)
            => Akka.Actor.Props.Create(() => new StubConnectionManagerActor(lease, tracker));
    }


    private static readonly RequestEndpoint TestKey = new()
    {
        Host = "localhost",
        Port = 8080,
        Scheme = "http",
        Version = HttpVersion.Version11
    };

    private static NetworkBuffer MakeData(byte value, int length = 4)
    {
        var bytes = new byte[length];
        bytes.AsSpan().Fill(value);
        var buf = NetworkBuffer.FromArray(bytes);
        buf.Key = TestKey;
        return buf;
    }

    /// <summary>
    /// Creates a <see cref="ConnectionLease"/> backed by in-memory channels,
    /// a <see cref="StubConnectionManagerActor"/>, and wires it to a <see cref="ConnectionStage"/>.
    /// </summary>
    private (
        Flow<IOutputItem, IInputItem, NotUsed> stageFlow,
        ReleaseTracker tracker,
        ConnectionLease lease,
        ChannelReader<NetworkBuffer> outboundReader,
        ChannelWriter<NetworkBuffer> inboundWriter)
        Build(RequestEndpoint? key = null)
    {
        var endpoint = key ?? TestKey;
        var state = new ClientState(8192, Stream.Null, null, null);
        var handle = ConnectionHandle.CreateDirect(
            state.OutboundWriter, state.InboundReader, endpoint);
        var lease = new ConnectionLease(handle, state);
        var tracker = new ReleaseTracker();
        var actor = Sys.ActorOf(StubConnectionManagerActor.Props(lease, tracker));
        var stageFlow = Flow.FromGraph(new TcpConnectionStage(actor, new TurboClientOptions()));
        return (stageFlow, tracker, lease, state.OutboundReader, state.InboundWriter);
    }


    [Fact(Timeout = 15_000)]
    public async Task ConnectionStage_should_trigger_acquire_async_when_connect_item_pushed_to_inlet()
    {
        var (stageFlow, _, _, _, inboundWriter) = Build();
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        var connectItem = new ConnectItem(options)
        {
            Key = new RequestEndpoint
                { Host = "localhost", Port = 8080, Scheme = "Https", Version = HttpVersion.Unknown }
        };

        var (queue, _) = Source.Queue<IOutputItem>(4, OverflowStrategy.Backpressure)
            .Via(stageFlow)
            .ToMaterialized(Sink.Ignore<IInputItem>(), Keep.Both)
            .Run(Materializer);

        await queue.OfferAsync(connectItem);

        // Give stage time to process the ConnectItem and acquire the lease.
        await Task.Delay(300, TestContext.Current.CancellationToken);

        // Verify by injecting inbound data — it should appear at outlet.
        var buf = NetworkBuffer.FromArray([0xAB, 0xAB, 0xAB, 0xAB]);
        await inboundWriter.WriteAsync(buf, TestContext.Current.CancellationToken);

        await Task.Delay(200, TestContext.Current.CancellationToken);
        inboundWriter.Complete();
    }

    [Fact(Timeout = 15_000)]
    public async Task ConnectionStage_should_reach_outlet_when_inbound_data_written_to_channel()
    {
        var (stageFlow, _, _, _, inboundWriter) = Build();
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        var connectItem = new ConnectItem(options)
        {
            Key = new RequestEndpoint
                { Host = "localhost", Port = 8080, Scheme = "Https", Version = HttpVersion.Unknown }
        };

        var (inputQueue, resultTask) = Source.Queue<IOutputItem>(4, OverflowStrategy.Backpressure)
            .Via(stageFlow)
            .ToMaterialized(Sink.First<IInputItem>(), Keep.Both)
            .Run(Materializer);

        await inputQueue.OfferAsync(connectItem);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        var buf = NetworkBuffer.Rent(4);
        buf.FullMemory.Span[..4].Fill(0xAB);
        buf.Length = 4;
        await inboundWriter.WriteAsync(buf, TestContext.Current.CancellationToken);

        var received = (NetworkBuffer)await resultTask.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(4, received.Length);
        Assert.Equal(0xAB, received.Span[0]);
    }

    [Fact(Timeout = 15_000)]
    public async Task ConnectionStage_should_write_to_outbound_channel_when_data_item_pushed_to_inlet()
    {
        var (stageFlow, _, _, outboundReader, inboundWriter) = Build();
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        var connectItem = new ConnectItem(options)
        {
            Key = new RequestEndpoint
                { Host = "localhost", Port = 8080, Scheme = "Https", Version = HttpVersion.Unknown }
        };
        var data = MakeData(0xCD, 8);

        var (inputQueue, _) = Source.Queue<IOutputItem>(4, OverflowStrategy.Backpressure)
            .Via(stageFlow)
            .ToMaterialized(Sink.Ignore<IInputItem>(), Keep.Both)
            .Run(Materializer);

        await inputQueue.OfferAsync(connectItem);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        await inputQueue.OfferAsync(data);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = await outboundReader.ReadAsync(cts.Token);
        Assert.Equal(8, buffer.Length);
        Assert.Equal(0xCD, buffer.Span[0]);

        buffer.Dispose();
        inboundWriter.Complete();
    }

    [Fact(Timeout = 15_000)]
    public async Task ConnectionStage_should_complete_round_trip_when_outbound_written_and_inbound_read()
    {
        var (stageFlow, _, _, outboundReader, inboundWriter) = Build();
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        var connectItem = new ConnectItem(options)
        {
            Key = new RequestEndpoint
                { Host = "localhost", Port = 8080, Scheme = "Https", Version = HttpVersion.Unknown }
        };

        var (inputQueue, resultTask) = Source.Queue<IOutputItem>(4, OverflowStrategy.Backpressure)
            .Via(stageFlow)
            .ToMaterialized(Sink.Seq<IInputItem>(), Keep.Both)
            .Run(Materializer);

        await inputQueue.OfferAsync(connectItem);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        var outData = MakeData(0x01, 16);
        await inputQueue.OfferAsync(outData);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var outBuffer = await outboundReader.ReadAsync(cts.Token);
        Assert.Equal(16, outBuffer.Length);
        Assert.Equal(0x01, outBuffer.Span[0]);
        outBuffer.Dispose();

        var inBuf = NetworkBuffer.FromArray([0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02]);
        await inboundWriter.WriteAsync(inBuf, TestContext.Current.CancellationToken);

        await Task.Delay(300, TestContext.Current.CancellationToken);
        inboundWriter.Complete();
        inputQueue.Complete();

        var results = await resultTask.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(2, results.Count);

        var inbound = (NetworkBuffer)results[0];
        Assert.Equal(12, inbound.Length);
        Assert.Equal(0x02, inbound.Span[0]);

        Assert.IsType<CloseSignalItem>(results[1]);
    }

    [Fact(Timeout = 15_000)]
    public async Task ConnectionStage_should_release_with_no_reuse_when_connection_reuse_item_can_reuse_is_false()
    {
        var (stageFlow, tracker, lease, _, inboundWriter) = Build();
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        var connectItem = new ConnectItem(options)
        {
            Key = new RequestEndpoint
                { Host = "localhost", Port = 8080, Scheme = "Https", Version = HttpVersion.Unknown }
        };

        var (inputQueue, _) = Source.Queue<IOutputItem>(4, OverflowStrategy.Backpressure)
            .Via(stageFlow)
            .ToMaterialized(Sink.Ignore<IInputItem>(), Keep.Both)
            .Run(Materializer);

        await inputQueue.OfferAsync(connectItem);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        var decision = ConnectionReuseDecision.Close("Connection: close");
        var reuseItem = ConnectionReuseItem.Rent(TestKey, decision);
        await inputQueue.OfferAsync(reuseItem);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        Assert.False(lease.Reusable);
        Assert.True(tracker.Released);
        Assert.False(tracker.ReleasedCanReuse);
        Assert.Same(lease, tracker.ReleasedLease);

        inboundWriter.Complete();
    }

    [Fact(Timeout = 15_000)]
    public async Task ConnectionStage_should_release_with_can_reuse_when_connection_reuse_item_can_reuse_is_true()
    {
        var (stageFlow, tracker, lease, _, inboundWriter) = Build();
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        var connectItem = new ConnectItem(options)
        {
            Key = new RequestEndpoint
                { Host = "localhost", Port = 8080, Scheme = "Https", Version = HttpVersion.Unknown }
        };

        var (inputQueue, _) = Source.Queue<IOutputItem>(4, OverflowStrategy.Backpressure)
            .Via(stageFlow)
            .ToMaterialized(Sink.Ignore<IInputItem>(), Keep.Both)
            .Run(Materializer);

        await inputQueue.OfferAsync(connectItem);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        var decision = ConnectionReuseDecision.KeepAlive("HTTP/1.1 persistent");
        var reuseItem = ConnectionReuseItem.Rent(TestKey, decision);
        await inputQueue.OfferAsync(reuseItem);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        // With exclusive connection ownership, canReuse=true does NOT release the
        // lease back to the actor — the stage keeps it for subsequent requests.
        // Only canReuse=false or stage cleanup releases the lease.
        Assert.True(lease.Reusable);
        Assert.False(tracker.Released);

        inboundWriter.Complete();
    }

    [Fact(Timeout = 15_000)]
    public async Task ConnectionStage_should_update_lease_max_concurrent_streams_when_max_concurrent_streams_item_received()
    {
        var (stageFlow, _, lease, _, inboundWriter) = Build();
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        var connectItem = new ConnectItem(options)
        {
            Key = new RequestEndpoint
                { Host = "localhost", Port = 8080, Scheme = "Https", Version = HttpVersion.Unknown }
        };

        var (inputQueue, _) = Source.Queue<IOutputItem>(4, OverflowStrategy.Backpressure)
            .Via(stageFlow)
            .ToMaterialized(Sink.Ignore<IInputItem>(), Keep.Both)
            .Run(Materializer);

        await inputQueue.OfferAsync(connectItem);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        await inputQueue.OfferAsync(new MaxConcurrentStreamsItem(50));
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Equal(50, lease.MaxConcurrentStreams);

        inboundWriter.Complete();
    }

    [Fact(Timeout = 15_000)]
    public async Task ConnectionStage_should_mark_lease_busy_when_stream_acquire_item_received()
    {
        var (stageFlow, _, lease, _, inboundWriter) = Build();
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        var connectItem = new ConnectItem(options)
        {
            Key = new RequestEndpoint
                { Host = "localhost", Port = 8080, Scheme = "Https", Version = HttpVersion.Unknown }
        };

        var (inputQueue, _) = Source.Queue<IOutputItem>(4, OverflowStrategy.Backpressure)
            .Via(stageFlow)
            .ToMaterialized(Sink.Ignore<IInputItem>(), Keep.Both)
            .Run(Materializer);

        await inputQueue.OfferAsync(connectItem);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        var streamsBefore = lease.ActiveStreams;

        await inputQueue.OfferAsync(StreamAcquireItem.Rent(TestKey));
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.True(lease.ActiveStreams > streamsBefore);

        inboundWriter.Complete();
    }

    [Fact(Timeout = 15_000)]
    public async Task ConnectionStage_should_survive_and_continue_when_data_item_arrives_with_no_handle()
    {
        // Actor that never returns a lease
        var tracker = new ReleaseTracker();
        var neverActor = Sys.ActorOf(StubConnectionManagerActor.Props(null, tracker));
        var stageFlow = Flow.FromGraph(new TcpConnectionStage(neverActor, new TurboClientOptions()));

        var (inputQueue, outputTask) = Source.Queue<IOutputItem>(8, OverflowStrategy.Backpressure)
            .Via(stageFlow)
            .ToMaterialized(Sink.Seq<IInputItem>(), Keep.Both)
            .Run(Materializer);

        var data = MakeData(0xFF, 4);
        await inputQueue.OfferAsync(data);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        var data2 = MakeData(0xEE, 4);
        await inputQueue.OfferAsync(data2);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        inputQueue.Complete();
        var results = await outputTask.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Empty(results);
    }

    [Fact(Timeout = 15_000)]
    public async Task ConnectionStage_should_emit_close_signal_and_release_lease_when_outbound_channel_closed_during_write()
    {
        var state = new ClientState(8192, Stream.Null, null, null);
        state.OutboundWriter.Complete();

        var handle = ConnectionHandle.CreateDirect(
            state.OutboundWriter, state.InboundReader, TestKey);
        var lease = new ConnectionLease(handle, state);
        var tracker = new ReleaseTracker();
        var actor = Sys.ActorOf(StubConnectionManagerActor.Props(lease, tracker));

        var stageFlow = Flow.FromGraph(new TcpConnectionStage(actor, new TurboClientOptions()));
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        var connectItem = new ConnectItem(options)
        {
            Key = new RequestEndpoint
                { Host = "localhost", Port = 8080, Scheme = "Https", Version = HttpVersion.Unknown }
        };

        var (inputQueue, resultTask) = Source.Queue<IOutputItem>(4, OverflowStrategy.Backpressure)
            .Via(stageFlow)
            .ToMaterialized(Sink.Seq<IInputItem>(), Keep.Both)
            .Run(Materializer);

        await inputQueue.OfferAsync(connectItem);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        var data = MakeData(0xBB, 4);
        await inputQueue.OfferAsync(data);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        Assert.True(tracker.Released);
        Assert.False(tracker.ReleasedCanReuse);
        Assert.False(lease.Reusable);

        inputQueue.Complete();
        var results = await resultTask.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var closeSignal = Assert.Single(results.OfType<CloseSignalItem>());
        Assert.Equal(TlsCloseKind.AbruptClose, closeSignal.CloseKind);

        state.InboundWriter.Complete();
    }
}
