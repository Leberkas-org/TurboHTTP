using System;
using System.Buffers;
using System.Net;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit2;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.Tests.IO;

public sealed class ConnectionActorTests : TestKit
{
    private static TcpOptions MakeOptions()
        => new() { Host = "test.local", Port = 8080 };

    private IActorRef CreateConnectionActor(TcpOptions? options = null)
    {
        var opts = options ?? MakeOptions();
        return Sys.ActorOf(Props.Create(() => new ConnectionActor(opts, TestActor)));
    }

    /// <summary>
    /// Creates inbound/outbound channels matching the ClientRunner.ClientConnected shape.
    /// </summary>
    private static (
        Channel<(IMemoryOwner<byte>, int)> inbound,
        Channel<(IMemoryOwner<byte>, int)> outbound,
        ClientRunner.ClientConnected msg
    ) MakeConnectedMessage()
    {
        var inbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var outbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var endpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 8080);
        var msg = new ClientRunner.ClientConnected(endpoint, inbound.Reader, outbound.Writer);
        return (inbound, outbound, msg);
    }

    [Fact(DisplayName = "CA-001: PreStart sends CreateTcpRunner with correct TcpOptions and Self")]
    public void CA_001_PreStart_SendsCreateTcpRunner()
    {
        var options = MakeOptions();
        var actor = CreateConnectionActor(options);

        var create = ExpectMsg<ClientManager.CreateTcpRunner>(TimeSpan.FromSeconds(3));

        Assert.Equal(options.Host, create.Options.Host);
        Assert.Equal(options.Port, create.Options.Port);
        Assert.Equal(actor, create.Handler);
    }

    [Fact(DisplayName = "CA-002: ClientConnected stores inbound/outbound channels and runner ref")]
    public async Task CA_002_ClientConnected_StoresChannelsAndRunner()
    {
        var actor = CreateConnectionActor();
        ExpectMsg<ClientManager.CreateTcpRunner>();

        var (_, outbound, connMsg) = MakeConnectedMessage();

        // Send ClientConnected from TestActor (simulating the runner)
        actor.Tell(connMsg, TestActor);

        // Verify the actor stored the outbound channel by sending a DataItem —
        // if outbound was stored, TryWrite succeeds and data appears on the channel.
        var owner = MemoryPool<byte>.Shared.Rent(16);
        actor.Tell(new DataItem(owner, 16));

        // Wait for the actor to process the DataItem
        var written = await outbound.Reader.ReadAsync();
        Assert.Equal(16, written.Item2);
    }

    [Fact(DisplayName = "CA-003: ClientConnected starts PumpInbound task")]
    public void CA_003_ClientConnected_StartsPumpInbound()
    {
        var actor = CreateConnectionActor();
        ExpectMsg<ClientManager.CreateTcpRunner>();

        var (inbound, _, connMsg) = MakeConnectedMessage();

        actor.Tell(connMsg, TestActor);

        // Write to the inbound channel — if PumpInbound is running, it will
        // read from the channel. The pump reads and forwards to _responseQueue,
        // which is null until GetStreamRefs is called. But the key test is that
        // PumpInbound consumes from the channel without throwing.
        var mem = MemoryPool<byte>.Shared.Rent(8);
        inbound.Writer.TryWrite((mem, 8));

        // Complete the channel — PumpInbound should exit gracefully
        inbound.Writer.Complete();

        // Give PumpInbound a moment to process
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // If PumpInbound wasn't started, the channel item would remain unconsumed.
        // After pump has run + channel completed, TryRead should return false.
        Assert.False(inbound.Reader.TryRead(out _));
    }

    [Fact(DisplayName = "CA-004: ClientDisconnected triggers reconnect (sends CreateTcpRunner again)")]
    public void CA_004_ClientDisconnected_TriggersReconnect()
    {
        var actor = CreateConnectionActor();
        var firstCreate = ExpectMsg<ClientManager.CreateTcpRunner>();

        // Simulate connection
        var (_, _, connMsg) = MakeConnectedMessage();
        actor.Tell(connMsg, TestActor);

        // Simulate disconnection
        var endpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 8080);
        actor.Tell(new ClientRunner.ClientDisconnected(endpoint));

        // Should receive a second CreateTcpRunner (reconnect)
        var secondCreate = ExpectMsg<ClientManager.CreateTcpRunner>(TimeSpan.FromSeconds(3));
        Assert.Equal(firstCreate.Options.Host, secondCreate.Options.Host);
        Assert.Equal(firstCreate.Options.Port, secondCreate.Options.Port);
    }

    [Fact(DisplayName = "CA-005: Terminated of runner triggers reconnect")]
    public void CA_005_Terminated_OfRunner_TriggersReconnect()
    {
        var actor = CreateConnectionActor();
        ExpectMsg<ClientManager.CreateTcpRunner>();

        // Create a probe to act as the runner so we can terminate it
        var runnerProbe = CreateTestProbe();

        var (_, _, connMsg) = MakeConnectedMessage();

        // Send ClientConnected from the runner probe (so _runner = runnerProbe)
        actor.Tell(connMsg, runnerProbe);

        // Stop the runner probe — ConnectionActor watches it, so it gets Terminated
        Sys.Stop(runnerProbe);

        // Should receive a new CreateTcpRunner (reconnect)
        var reconnect = ExpectMsg<ClientManager.CreateTcpRunner>(TimeSpan.FromSeconds(3));
        Assert.NotNull(reconnect);
    }

    [Fact(DisplayName = "CA-006: Reconnect clears old channel references before creating new ones")]
    public async Task CA_006_Reconnect_ClearsOldChannels()
    {
        var actor = CreateConnectionActor();
        ExpectMsg<ClientManager.CreateTcpRunner>();

        // First connection
        var (_, outbound1, connMsg1) = MakeConnectedMessage();
        actor.Tell(connMsg1, TestActor);

        // Verify first outbound works
        var owner1 = MemoryPool<byte>.Shared.Rent(16);
        actor.Tell(new DataItem(owner1, 16));
        await outbound1.Reader.ReadAsync();

        // Disconnect → triggers reconnect → clears old channels
        var endpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 8080);
        actor.Tell(new ClientRunner.ClientDisconnected(endpoint));
        ExpectMsg<ClientManager.CreateTcpRunner>();

        // Between disconnect and new connection, outbound should be null.
        // Sending a DataItem should dispose the memory (not write to old channel).
        var owner2 = MemoryPool<byte>.Shared.Rent(16);
        actor.Tell(new DataItem(owner2, 16));

        // Give actor time to process (discard) the DataItem
        ExpectNoMsg(TimeSpan.FromMilliseconds(200));

        // Old outbound channel should NOT receive the second item
        Assert.False(outbound1.Reader.TryRead(out _));

        // Now simulate a second connection with new channels
        var (_, outbound2, connMsg2) = MakeConnectedMessage();
        actor.Tell(connMsg2, TestActor);

        // New outbound should work
        var owner3 = MemoryPool<byte>.Shared.Rent(16);
        actor.Tell(new DataItem(owner3, 16));
        var written = await outbound2.Reader.ReadAsync();
        Assert.Equal(16, written.Item2);
    }

    // ── TASK-015: Cleanup (PostStop) ─────────────────────────────────

    [Fact(DisplayName = "CA-011: PostStop cancels CancellationTokenSource")]
    public void CA_011_PostStop_CancelsCts()
    {
        var actor = CreateConnectionActor();
        ExpectMsg<ClientManager.CreateTcpRunner>();

        // Use a probe as runner so DoClose goes there, not TestActor
        var runnerProbe = CreateTestProbe();
        var (inbound, _, connMsg) = MakeConnectedMessage();
        actor.Tell(connMsg, runnerProbe);

        // Stop the actor — triggers PostStop which cancels the CTS
        Sys.Stop(actor);

        // Wait for PostStop to complete
        runnerProbe.ExpectMsg<DoClose>(TimeSpan.FromSeconds(3));

        // PumpInbound should have exited because CTS was cancelled.
        // The inbound channel is still open (not completed by us).
        // If PumpInbound were still running, it would consume items we write.
        // After cancellation, writing an item should remain unconsumed.
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));
        var mem = MemoryPool<byte>.Shared.Rent(8);
        Assert.True(inbound.Writer.TryWrite((mem, 8)));
        Assert.True(inbound.Reader.TryRead(out _));
    }

    [Fact(DisplayName = "CA-012: PostStop sends DoClose to runner")]
    public void CA_012_PostStop_SendsDoCloseToRunner()
    {
        var actor = CreateConnectionActor();
        ExpectMsg<ClientManager.CreateTcpRunner>();

        // Use a probe as the runner so we can verify it receives DoClose
        var runnerProbe = CreateTestProbe();
        var (_, _, connMsg) = MakeConnectedMessage();
        actor.Tell(connMsg, runnerProbe);

        // Stop the actor — PostStop should send DoClose to the runner
        Sys.Stop(actor);

        runnerProbe.ExpectMsg<DoClose>(TimeSpan.FromSeconds(3));
    }

    [Fact(DisplayName = "CA-013: PostStop completes _responseQueue")]
    public async Task CA_013_PostStop_CompletesResponseQueue()
    {
        var actor = CreateConnectionActor();
        ExpectMsg<ClientManager.CreateTcpRunner>();

        // Connect first so the actor has a runner
        var (_, _, connMsg) = MakeConnectedMessage();
        actor.Tell(connMsg, TestActor);

        // Request stream refs to materialize _responseQueue
        actor.Tell(new ConnectionActor.GetStreamRefs());
        var refs = ExpectMsg<ConnectionActor.StreamRefsResponse>(TimeSpan.FromSeconds(5));

        // Stop the actor — PostStop calls _responseQueue.Complete()
        Sys.Stop(actor);

        // The source ref should eventually complete (no more items)
        // We verify by sinking the source ref — it should complete without error
        var mat = Sys.Materializer();
        var items = await refs.Responses.Source
            .RunWith(Sink.Seq<IDataItem>(), mat);

        // Should complete with zero items (nothing was offered before stop)
        Assert.Empty(items);
    }

    [Fact(DisplayName = "CA-014: PostStop with null runner does not throw")]
    public void CA_014_PostStop_NullRunner_NoThrow()
    {
        var actor = CreateConnectionActor();
        ExpectMsg<ClientManager.CreateTcpRunner>();

        // Do NOT send ClientConnected — _runner stays null
        // Stop the actor — PostStop should not throw
        Sys.Stop(actor);

        // If PostStop threw, the actor system would log an error.
        // Give time for any error to surface.
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // Actor should be terminated without issues
        Watch(actor);
        ExpectTerminated(actor, TimeSpan.FromSeconds(3));
    }

    [Fact(DisplayName = "CA-015: PostStop with null _responseQueue does not throw")]
    public void CA_015_PostStop_NullResponseQueue_NoThrow()
    {
        var actor = CreateConnectionActor();
        ExpectMsg<ClientManager.CreateTcpRunner>();

        // Connect (sets _runner) but do NOT call GetStreamRefs — _responseQueue stays null
        var runnerProbe = CreateTestProbe();
        var (_, _, connMsg) = MakeConnectedMessage();
        actor.Tell(connMsg, runnerProbe);

        // Stop the actor — PostStop should handle null _responseQueue gracefully
        Sys.Stop(actor);

        // Runner should still get DoClose even though _responseQueue is null
        runnerProbe.ExpectMsg<DoClose>(TimeSpan.FromSeconds(3));
    }

    // ── TASK-014: Data Send ──────────────────────────────────────────

    [Fact(DisplayName = "CA-007: DataItem is written to outbound via TryWrite")]
    public async Task CA_007_DataItem_WrittenToOutbound()
    {
        var actor = CreateConnectionActor();
        ExpectMsg<ClientManager.CreateTcpRunner>();

        var (_, outbound, connMsg) = MakeConnectedMessage();
        actor.Tell(connMsg, TestActor);

        var owner = MemoryPool<byte>.Shared.Rent(32);
        // Write a known byte so we can verify identity
        owner.Memory.Span[0] = 0xAB;
        actor.Tell(new DataItem(owner, 32));

        var (mem, len) = await outbound.Reader.ReadAsync();
        Assert.Equal(32, len);
        Assert.Equal(0xAB, mem.Memory.Span[0]);
        mem.Dispose();
    }

    [Fact(DisplayName = "CA-008: DataItem when outbound is null disposes Memory without crash")]
    public void CA_008_DataItem_OutboundNull_DisposesMemory()
    {
        var actor = CreateConnectionActor();
        ExpectMsg<ClientManager.CreateTcpRunner>();

        // Do NOT send ClientConnected — _outbound remains null
        var owner = new TrackingMemoryOwner(MemoryPool<byte>.Shared.Rent(16));
        actor.Tell(new DataItem(owner, 16));

        // Give actor time to process
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        Assert.True(owner.Disposed, "Memory should be disposed when _outbound is null");
    }

    [Fact(DisplayName = "CA-009: DataItem when TryWrite returns false disposes Memory")]
    public void CA_009_DataItem_TryWriteFails_DisposesMemory()
    {
        var actor = CreateConnectionActor();
        ExpectMsg<ClientManager.CreateTcpRunner>();

        // Create a bounded channel with capacity 1 and use a completed writer to force TryWrite=false
        var outbound = Channel.CreateBounded<(IMemoryOwner<byte>, int)>(1);
        outbound.Writer.Complete(); // TryWrite will always return false on a completed writer

        var inbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var endpoint = new IPEndPoint(IPAddress.Loopback, 8080);
        var connMsg = new ClientRunner.ClientConnected(endpoint, inbound.Reader, outbound.Writer);
        actor.Tell(connMsg, TestActor);

        var owner = new TrackingMemoryOwner(MemoryPool<byte>.Shared.Rent(16));
        actor.Tell(new DataItem(owner, 16));

        // Give actor time to process
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        Assert.True(owner.Disposed, "Memory should be disposed when TryWrite returns false");
    }

    [Fact(DisplayName = "CA-010: After ClientConnected, DataItem flows through outbound channel")]
    public async Task CA_010_AfterConnected_DataFlowsThroughOutbound()
    {
        var actor = CreateConnectionActor();
        ExpectMsg<ClientManager.CreateTcpRunner>();

        var (_, outbound, connMsg) = MakeConnectedMessage();
        actor.Tell(connMsg, TestActor);

        // Send multiple DataItems and verify they all flow through
        for (var i = 0; i < 3; i++)
        {
            var owner = MemoryPool<byte>.Shared.Rent(8);
            owner.Memory.Span[0] = (byte)(i + 1);
            actor.Tell(new DataItem(owner, 8));
        }

        for (var i = 0; i < 3; i++)
        {
            var (mem, len) = await outbound.Reader.ReadAsync();
            Assert.Equal(8, len);
            Assert.Equal((byte)(i + 1), mem.Memory.Span[0]);
            mem.Dispose();
        }
    }

    /// <summary>
    /// Wrapper around IMemoryOwner that tracks whether Dispose was called.
    /// </summary>
    private sealed class TrackingMemoryOwner : IMemoryOwner<byte>
    {
        private readonly IMemoryOwner<byte> _inner;
        public bool Disposed { get; private set; }

        public TrackingMemoryOwner(IMemoryOwner<byte> inner) => _inner = inner;
        public Memory<byte> Memory => _inner.Memory;

        public void Dispose()
        {
            Disposed = true;
            _inner.Dispose();
        }
    }
}
