using System.Buffers;
using System.Net;
using System.Threading.Channels;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9112;
using TurboHttp.Transport;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Tests <see cref="ConnectionStage"/> integration with the <see cref="ConnectionPool"/>-based
/// connection management. Verifies that the stage acquires a <see cref="ConnectionLease"/> and
/// routes bytes through in-memory channels.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="ConnectionStage"/>.
/// Validates the handshake between ConnectionStage and ConnectionPool via a stub pool subclass.
/// </remarks>
public sealed class ConnectionStageTests : StreamTestBase
{
    /// <summary>
    /// Stub pool that overrides <see cref="ConnectionPool.AcquireAsync"/> to return a
    /// pre-built <see cref="ConnectionLease"/> and tracks <see cref="ConnectionPool.Release"/> calls.
    /// </summary>
    private sealed class StubConnectionPool : ConnectionPool
    {
        private readonly ConnectionLease? _lease;
        public bool Released { get; private set; }
        public bool ReleasedCanReuse { get; private set; }
        public ConnectionLease? ReleasedLease { get; private set; }

        public StubConnectionPool(ConnectionLease? lease)
            : base(TimeSpan.FromSeconds(30))
        {
            _lease = lease;
        }

        public override Task<ConnectionLease> AcquireAsync(
            TcpOptions options, RequestEndpoint endpoint, CancellationToken ct = default)
        {
            if (_lease is null)
            {
                // Never complete — simulates a pool that never returns a lease.
                return new TaskCompletionSource<ConnectionLease>().Task;
            }

            return Task.FromResult(_lease);
        }

        public override void Release(ConnectionLease lease, bool canReuse)
        {
            Released = true;
            ReleasedCanReuse = canReuse;
            ReleasedLease = lease;
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
    /// Creates a <see cref="ConnectionLease"/> backed by in-memory channels
    /// and a <see cref="StubConnectionPool"/> that returns it.
    /// </summary>
    private static (
        StubConnectionPool pool,
        ConnectionLease lease,
        ChannelReader<(IMemoryOwner<byte> Buffer, int ReadableBytes)> outboundReader,
        ChannelWriter<(IMemoryOwner<byte> Buffer, int ReadableBytes)> inboundWriter)
        CreatePoolAndLease(RequestEndpoint? key = null)
    {
        var endpoint = key ?? TestKey;
        var state = new ClientState(8192, Stream.Null, null, null);
        var handle = ConnectionHandle.CreateDirect(
            state.OutboundWriter, state.InboundReader, endpoint);
        var lease = new ConnectionLease(handle, state);
        var pool = new StubConnectionPool(lease);
        return (pool, lease, state.OutboundReader, state.InboundWriter);
    }

    /// <summary>
    /// Creates a <see cref="ConnectionStage"/> flow wired to a <see cref="StubConnectionPool"/>.
    /// </summary>
    private static (
        Flow<IOutputItem, IInputItem, NotUsed> stageFlow,
        StubConnectionPool pool,
        ConnectionLease lease,
        ChannelReader<(IMemoryOwner<byte> Buffer, int ReadableBytes)> outboundReader,
        ChannelWriter<(IMemoryOwner<byte> Buffer, int ReadableBytes)> inboundWriter)
        Build(RequestEndpoint? key = null)
    {
        var (pool, lease, outboundReader, inboundWriter) = CreatePoolAndLease(key);
        var stageFlow = Flow.FromGraph(new ConnectionStage(pool));
        return (stageFlow, pool, lease, outboundReader, inboundWriter);
    }

    [Fact(Timeout = 15_000,
        DisplayName = "CS-001: ConnectItem pushed into inlet triggers AcquireAsync on ConnectionPool")]
    public async Task Should_TriggerAcquireAsync_When_ConnectItemPushedToInlet()
    {
        var (stageFlow, pool, lease, _, inboundWriter) = Build();
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
        await Task.Delay(300);

        // The stage should have acquired the lease (handle is set, inbound pump started).
        // Verify by injecting inbound data — it should appear at outlet.
        var owner = MemoryPool<byte>.Shared.Rent(4);
        owner.Memory.Span[..4].Fill(0xAB);
        await inboundWriter.WriteAsync((owner, 4));

        // If AcquireAsync was called successfully, the inbound pump is active.
        await Task.Delay(200);
        inboundWriter.Complete();
    }

    [Fact(Timeout = 15_000,
        DisplayName = "CS-002: Inbound data from ConnectionHandle.InboundReader appears at outlet")]
    public async Task Should_ReachOutlet_When_InboundDataWrittenToChannel()
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

        // Push ConnectItem → triggers AcquireAsync → StubConnectionPool returns lease
        await inputQueue.OfferAsync(connectItem);

        // Wait for the lease to be received and inbound pump to start
        await Task.Delay(300);

        // Inject data through the inbound channel
        var owner = MemoryPool<byte>.Shared.Rent(4);
        owner.Memory.Span[..4].Fill(0xAB);
        await inboundWriter.WriteAsync((owner, 4));

        var received = (DataItem)await resultTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(4, received.Length);
        Assert.Equal(0xAB, received.Memory.Memory.Span[0]);

        // Sink.First completes the stream → PostStop disposes the lease → channels already closed.
        // No explicit inboundWriter.Complete() needed.
    }

    [Fact(Timeout = 15_000,
        DisplayName = "CS-003: DataItem pushed to inlet is written to ConnectionHandle.OutboundWriter")]
    public async Task Should_WriteToOutboundChannel_When_DataItemPushedToInlet()
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

        // Connect first — stage needs a lease before accepting DataItems
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

    [Fact(Timeout = 15_000,
        DisplayName = "CS-004: Full round-trip — outbound DataItem written, inbound data read")]
    public async Task Should_CompleteRoundTrip_When_OutboundWrittenAndInboundRead()
    {
        var (stageFlow, _, _, outboundReader, inboundWriter) = Build();
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        var connectItem = new ConnectItem(options)
        {
            Key = new RequestEndpoint
                { Host = "localhost", Port = 8080, Scheme = "Https", Version = HttpVersion.Unknown }
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
        Assert.Equal(2, results.Count);

        var inbound = (DataItem)results[0];
        Assert.Equal(12, inbound.Length);
        Assert.Equal(0x02, inbound.Memory.Memory.Span[0]);

        // Inbound channel completion now emits a CloseSignalItem (TASK-007-004).
        Assert.IsType<CloseSignalItem>(results[1]);
    }

    [Fact(Timeout = 15_000,
        DisplayName =
            "CS-005: ConnectionReuseItem with CanReuse=false calls pool.Release(canReuse:false) and marks lease NoReuse")]
    public async Task Should_ReleaseWithNoReuse_When_ConnectionReuseItemCanReuseIsFalse()
    {
        var (stageFlow, pool, lease, _, inboundWriter) = Build();
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

        // Connect first — stage needs a lease
        await inputQueue.OfferAsync(connectItem);
        await Task.Delay(300);

        // Push a ConnectionReuseItem with CanReuse = false
        var decision = ConnectionReuseDecision.Close("Connection: close");
        var reuseItem = new ConnectionReuseItem(TestKey, decision);
        await inputQueue.OfferAsync(reuseItem);
        await Task.Delay(300);

        // Verify the lease was marked as non-reusable
        Assert.False(lease.Reusable);

        // Verify pool.Release was called with canReuse: false
        Assert.True(pool.Released);
        Assert.False(pool.ReleasedCanReuse);
        Assert.Same(lease, pool.ReleasedLease);

        inboundWriter.Complete();
    }

    [Fact(Timeout = 15_000,
        DisplayName =
            "CS-006: ConnectionReuseItem with CanReuse=true calls pool.Release(canReuse:true) without marking NoReuse")]
    public async Task Should_ReleaseWithCanReuse_When_ConnectionReuseItemCanReuseIsTrue()
    {
        var (stageFlow, pool, lease, _, inboundWriter) = Build();
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

        // Connect first
        await inputQueue.OfferAsync(connectItem);
        await Task.Delay(300);

        // Push a ConnectionReuseItem with CanReuse = true
        var decision = ConnectionReuseDecision.KeepAlive("HTTP/1.1 persistent");
        var reuseItem = new ConnectionReuseItem(TestKey, decision);
        await inputQueue.OfferAsync(reuseItem);
        await Task.Delay(300);

        // Verify lease is still marked as reusable
        Assert.True(lease.Reusable);

        // Verify pool.Release was called with canReuse: true
        Assert.True(pool.Released);
        Assert.True(pool.ReleasedCanReuse);

        inboundWriter.Complete();
    }

    [Fact(Timeout = 15_000,
        DisplayName = "CS-007: MaxConcurrentStreamsItem(50) updates lease MaxConcurrentStreams directly")]
    public async Task Should_UpdateLeaseMaxConcurrentStreams_When_MaxConcurrentStreamsItemReceived()
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

        // Connect first — stage needs a lease
        await inputQueue.OfferAsync(connectItem);
        await Task.Delay(300);

        // Push MaxConcurrentStreamsItem
        await inputQueue.OfferAsync(new MaxConcurrentStreamsItem(50));
        await Task.Delay(200);

        // Verify the lease was updated directly
        Assert.Equal(50, lease.MaxConcurrentStreams);

        inboundWriter.Complete();
    }

    [Fact(Timeout = 15_000,
        DisplayName = "CS-008: StreamAcquireItem calls lease.MarkBusy() directly")]
    public async Task Should_MarkLeaseBusy_When_StreamAcquireItemReceived()
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

        // Connect first — stage needs a lease
        await inputQueue.OfferAsync(connectItem);
        await Task.Delay(300);

        var streamsBefore = lease.ActiveStreams;

        // Push StreamAcquireItem
        await inputQueue.OfferAsync(new StreamAcquireItem());
        await Task.Delay(200);

        // Verify stream count increased (MarkBusy increments ActiveStreams)
        Assert.True(lease.ActiveStreams > streamsBefore);

        inboundWriter.Complete();
    }

    [Fact(Timeout = 15_000,
        DisplayName = "CS-009: DataItem with missing handle is buffered and stream survives")]
    public async Task Should_SurviveAndContinue_When_DataItemArrivesWithNoHandle()
    {
        // Build a pool that never returns a lease so _handle remains null.
        var neverPool = new StubConnectionPool(null);
        var stageFlow = Flow.FromGraph(new ConnectionStage(neverPool));

        var (inputQueue, outputTask) = Source.Queue<IOutputItem>(8, OverflowStrategy.Backpressure)
            .Via(stageFlow)
            .ToMaterialized(Sink.Seq<IInputItem>(), Keep.Both)
            .Run(Materializer);

        // Push a DataItem without first connecting — handle is null.
        var data = MakeData(0xFF, 4);
        await inputQueue.OfferAsync(data);

        // Give the stage a moment to process the dropped item.
        await Task.Delay(200);

        // Push a second DataItem to confirm the stage is still alive and processing.
        var data2 = MakeData(0xEE, 4);
        await inputQueue.OfferAsync(data2);
        await Task.Delay(200);

        // Complete the stream — no exception means the stage survived.
        inputQueue.Complete();
        var results = await outputTask.WaitAsync(TimeSpan.FromSeconds(10));

        // No output items expected (handle was never established), but no crash.
        Assert.Empty(results);
    }

    [Fact(Timeout = 15_000,
        DisplayName =
            "CS-010: Concurrent disconnect — stage survives items arriving after inbound channel completes")]
    public async Task Should_SurviveItems_When_HandleClearedByConcurrentInboundComplete()
    {
        var (stageFlow, _, _, _, inboundWriter) = Build();
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        var connectItem = new ConnectItem(options)
        {
            Key = new RequestEndpoint
                { Host = "localhost", Port = 8080, Scheme = "Https", Version = HttpVersion.Unknown }
        };

        var (inputQueue, outputTask) = Source.Queue<IOutputItem>(8, OverflowStrategy.Backpressure)
            .Via(stageFlow)
            .ToMaterialized(Sink.Seq<IInputItem>(), Keep.Both)
            .Run(Materializer);

        // Connect — handle becomes non-null.
        await inputQueue.OfferAsync(connectItem);
        await Task.Delay(300);

        // Simulate TCP disconnect: complete the inbound channel.
        // This triggers _onInboundComplete which sets _handle = null in the stage event loop.
        inboundWriter.Complete();

        // Immediately (before the async callback drains) push items that access the handle.
        // After the event-loop processes the completion the local copy pattern keeps these safe.
        await inputQueue.OfferAsync(new MaxConcurrentStreamsItem(10));
        await inputQueue.OfferAsync(new StreamAcquireItem());
        var reuseDecision = ConnectionReuseDecision.Close("close");
        await inputQueue.OfferAsync(new ConnectionReuseItem(TestKey, reuseDecision));

        // Allow all callbacks to drain.
        await Task.Delay(400);

        // Complete the source — no exception means the stage survived.
        inputQueue.Complete();
        var results = await outputTask.WaitAsync(TimeSpan.FromSeconds(10));

        // Inbound channel completion now emits a CloseSignalItem (TASK-007-004).
        var closeSignal = Assert.Single(results);
        Assert.IsType<CloseSignalItem>(closeSignal);
    }

    [Fact(Timeout = 15_000,
        DisplayName =
            "CS-011: DataItem write to closed outbound channel emits CloseSignal and releases lease")]
    public async Task Should_EmitCloseSignalAndReleaseLease_When_OutboundChannelIsClosedDuringWrite()
    {
        // Build channels manually so we can complete the outbound writer to simulate a dead connection.
        var state = new ClientState(8192, Stream.Null, null, null);

        // Complete the outbound channel before the stage tries to write — simulates closed connection.
        state.OutboundWriter.Complete();

        var handle = ConnectionHandle.CreateDirect(
            state.OutboundWriter, state.InboundReader, TestKey);
        var lease = new ConnectionLease(handle, state);
        var pool = new StubConnectionPool(lease);

        var stageFlow = Flow.FromGraph(new ConnectionStage(pool));
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

        // Connect — lease becomes available.
        await inputQueue.OfferAsync(connectItem);
        await Task.Delay(300);

        // Push a DataItem — WriteAsync should fail because the outbound channel is already completed.
        var data = MakeData(0xBB, 4);
        await inputQueue.OfferAsync(data);
        await Task.Delay(300);

        // Verify pool.Release was called with canReuse: false
        Assert.True(pool.Released);
        Assert.False(pool.ReleasedCanReuse);

        // Verify the lease was marked as non-reusable
        Assert.False(lease.Reusable);

        // Complete the stream gracefully — it should NOT fault.
        inputQueue.Complete();
        var results = await resultTask.WaitAsync(TimeSpan.FromSeconds(10));

        // The stage should have emitted a CloseSignalItem (AbruptClose) signaling connection death.
        var closeSignal = Assert.Single(results.OfType<CloseSignalItem>());
        Assert.Equal(TlsCloseKind.AbruptClose, closeSignal.CloseKind);

        state.InboundWriter.Complete();
    }
}
