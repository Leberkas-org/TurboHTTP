using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Pooling;
using TurboHttp.Protocol.RFC9112;
using TurboHttp.Transport;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Regression tests for the HTTP/1.0 reconnection race condition (TASK-026).
/// Verifies that stale async callbacks from a disposed connection cannot push
/// <see cref="CloseSignalItem"/> into the decoder of a newly-acquired connection.
/// </summary>
/// <remarks>
/// The race condition occurs when sequential HTTP/1.0 requests reuse a single
/// materialised pipeline: after <see cref="ConnectionReuseItem"/> with
/// <c>CanReuse=false</c>, the old inbound pump may still be draining and can
/// post a stale <see cref="CloseSignalItem"/> via <c>GetAsyncCallback</c>.
/// The generation guard (<c>_connectionGen</c>) introduced in TASK-026-001/002/003
/// prevents these stale callbacks from reaching the new connection's decoder.
/// </remarks>
public sealed class ConnectionStageReconnectionRaceTests : StreamTestBase
{
    private static readonly RequestEndpoint TestKey = new()
    {
        Host = "localhost",
        Port = 8080,
        Scheme = "http",
        Version = HttpVersion.Version10
    };

    /// <summary>
    /// Pool that creates a fresh <see cref="ConnectionLease"/> for each
    /// <see cref="ConnectionPool.AcquireAsync"/> call, simulating HTTP/1.0 behaviour
    /// where every request gets a new TCP connection.
    /// </summary>
    private sealed class MultiLeaseConnectionPool : ConnectionPool
    {
        private readonly ConcurrentQueue<ChannelWriter<(IMemoryOwner<byte> Buffer, int ReadableBytes)>>
            _inboundWriters = new();

        private int _acquireCount;

        public int AcquireCount => Volatile.Read(ref _acquireCount);

        public MultiLeaseConnectionPool() : base(TimeSpan.FromSeconds(30))
        {
        }

        /// <summary>
        /// Returns the Nth inbound writer (0-based). Call only after the Nth
        /// <see cref="AcquireAsync"/> has completed.
        /// </summary>
        public ChannelWriter<(IMemoryOwner<byte> Buffer, int ReadableBytes)> GetInboundWriter(int index)
        {
            var writers = _inboundWriters.ToArray();
            return writers[index];
        }

        public override Task<ConnectionLease> AcquireAsync(
            TcpOptions options, RequestEndpoint endpoint, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _acquireCount);

            var state = new ClientState(8192, Stream.Null, null, null);
            var handle = ConnectionHandle.CreateDirect(
                state.OutboundWriter, state.InboundReader, endpoint);
            var lease = new ConnectionLease(handle, state);

            _inboundWriters.Enqueue(state.InboundWriter);

            return Task.FromResult(lease);
        }

        public override void Release(ConnectionLease lease, bool canReuse)
        {
            // No-op: we do not dispose the lease here so the inbound pump
            // can drain naturally and the race condition is provoked.
        }
    }

    [Fact(Timeout = 5000,
        DisplayName =
            "CS-RC-001: Three sequential HTTP/1.0 requests on one pipeline — no stale CloseSignalItem")]
    public async Task Should_NotEmitStaleCloseSignal_When_ThreeSequentialHttp10RequestsReuseOnePipeline()
    {
        var pool = new MultiLeaseConnectionPool();
        var stageFlow = Flow.FromGraph(new ConnectionStage(pool));
        var options = new TcpOptions { Host = "localhost", Port = 8080 };

        var (inputQueue, resultTask) = Source.Queue<IOutputItem>(16, OverflowStrategy.Backpressure)
            .Via(stageFlow)
            .ToMaterialized(Sink.Seq<IInputItem>(), Keep.Both)
            .Run(Materializer);

        const int requestCount = 3;

        for (var i = 0; i < requestCount; i++)
        {
            // ── 1. Connect: push ConnectItem → triggers AcquireAsync → fresh lease ──
            var connectItem = new ConnectItem(options) { Key = TestKey };
            await inputQueue.OfferAsync(connectItem);
            await Task.Delay(300); // Wait for _onLeaseAcquired + pump start

            // ── 2. Outbound: push DataItem (simulated HTTP/1.0 request) ──
            var requestOwner = MemoryPool<byte>.Shared.Rent(8);
            requestOwner.Memory.Span[..8].Fill((byte)(0x10 + i));
            await inputQueue.OfferAsync(new DataItem(requestOwner, 8) { Key = TestKey });
            await Task.Delay(100);

            // ── 3. Inbound: inject response data through the connection's channel ──
            var inboundWriter = pool.GetInboundWriter(i);
            var responseOwner = MemoryPool<byte>.Shared.Rent(16);
            responseOwner.Memory.Span[..16].Fill((byte)(0xA0 + i));
            await inboundWriter.WriteAsync((responseOwner, 16));
            await Task.Delay(200);

            // ── 4. Reuse decision: HTTP/1.0 → CanReuse=false ──
            // HandleConnectionReuseItem increments _connectionGen, stops the pump,
            // and clears _handle BEFORE the pump can post its completion callback.
            var decision = ConnectionReuseDecision.Close("HTTP/1.0 default close");
            await inputQueue.OfferAsync(new ConnectionReuseItem(TestKey, decision));
            await Task.Delay(100);

            // ── 5. PROVOKE THE RACE: complete the inbound channel AFTER the reuse ──
            // The old pump reads EOF and tries to post _onInboundComplete, but the
            // generation guard (gen != _connectionGen) rejects the stale callback.
            inboundWriter.Complete();
            await Task.Delay(150); // Let stale callbacks drain
        }

        // ── 6. Complete the stream and collect all output items ──
        inputQueue.Complete();
        var results = await resultTask.WaitAsync(TimeSpan.FromSeconds(3));

        // ── Assertions ──

        // A) Pool was called exactly 3 times — one fresh connection per request.
        Assert.Equal(requestCount, pool.AcquireCount);

        // B) Exactly 3 response DataItems appeared at the outlet.
        var dataItems = results.OfType<DataItem>().ToList();
        Assert.Equal(requestCount, dataItems.Count);

        // C) Each response carries the correct marker byte (proves correct ordering).
        for (var i = 0; i < requestCount; i++)
        {
            Assert.Equal(
                (byte)(0xA0 + i),
                dataItems[i].Memory.Memory.Span[0]);
        }

        // D) CRITICAL: No CloseSignalItem from a stale connection reached the outlet.
        //    Without TASK-026 fixes, stale pump completions would inject spurious
        //    CloseSignalItems that corrupt the decoder for the next request.
        var closeSignals = results.OfType<CloseSignalItem>().ToList();
        Assert.Empty(closeSignals);
    }
}
