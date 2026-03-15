using System;
using System.Buffers;
using System.Net;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.Tests.IO;

public sealed class ConnectionActorTests : TestKit
{
    private static TcpOptions MakeOptions()
        => new() { Host = "test.local", Port = 8080 };

    /// <summary>
    /// Creates a ConnectionActor as a top-level actor with TestActor as the clientManager.
    /// Context.Parent will be the user guardian — suitable for tests that don't inspect
    /// parent-bound messages.
    /// </summary>
    private IActorRef CreateConnectionActor(TcpOptions? options = null)
    {
        var opts = options ?? MakeOptions();
        return Sys.ActorOf(Props.Create(() => new ConnectionActor(opts, TestActor)));
    }

    /// <summary>
    /// Creates a ConnectionActor as a child of a forwarder proxy.
    /// Returns the clientManager probe and the ConnectionActor's self ref
    /// (extracted from the first CreateTcpRunner message).
    /// The forwarder forwards all child→parent messages to TestActor.
    /// </summary>
    private async Task<(IActorRef connectionActor, TestProbe clientManagerProbe)>
        CreateConnectionActorWithParent(TcpOptions? options = null)
    {
        var opts = options ?? MakeOptions();
        var cmProbe = CreateTestProbe();
        Sys.ActorOf(Props.Create(() => new ConnectionActorParent(opts, cmProbe.Ref, TestActor)));

        var create = await cmProbe.ExpectMsgAsync<ClientManager.CreateTcpRunner>(TimeSpan.FromSeconds(3));
        return (create.Handler, cmProbe);
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
        var endpoint = new IPEndPoint(IPAddress.Loopback, 8080);
        var msg = new ClientRunner.ClientConnected(endpoint, inbound.Reader, outbound.Writer);
        return (inbound, outbound, msg);
    }

    // ── Connection lifecycle ──────────────────────────────────────────

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

    [Fact(DisplayName = "CA-003: ClientConnected starts PumpInbound task")]
    public async Task CA_003_ClientConnected_StartsPumpInbound()
    {
        var (connectionActor, cmProbe) = await CreateConnectionActorWithParent();

        var (inbound, _, connMsg) = MakeConnectedMessage();
        connectionActor.Tell(connMsg, cmProbe.Ref);

        // Wait for materialization to complete (signaled by RegisterConnectionRefs)
        await ExpectMsgAsync<HostPoolActor.RegisterConnectionRefs>(TimeSpan.FromSeconds(10));

        // Write to the inbound channel — PumpInbound should consume it
        var mem = MemoryPool<byte>.Shared.Rent(8);
        inbound.Writer.TryWrite((mem, 8));

        // Complete the channel — PumpInbound should exit gracefully
        inbound.Writer.Complete();

        // Give PumpInbound a moment to process
        await Task.Delay(300);

        // After pump has run + channel completed, TryRead should return false
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
        var endpoint = new IPEndPoint(IPAddress.Loopback, 8080);
        actor.Tell(new ClientRunner.ClientDisconnected(endpoint));

        // Should receive a second CreateTcpRunner (reconnect)
        var secondCreate = ExpectMsg<ClientManager.CreateTcpRunner>(TimeSpan.FromSeconds(5));
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
        var reconnect = ExpectMsg<ClientManager.CreateTcpRunner>(TimeSpan.FromSeconds(5));
        Assert.NotNull(reconnect);
    }

    [Fact(DisplayName = "CA-006: Reconnect creates new StreamRefs and sends RegisterConnectionRefs again")]
    public async Task CA_006_Reconnect_CreatesNewStreamRefs()
    {
        var (connectionActor, cmProbe) = await CreateConnectionActorWithParent();

        // First connection
        var (_, _, connMsg1) = MakeConnectedMessage();
        connectionActor.Tell(connMsg1, cmProbe.Ref);
        var refs1 = await ExpectMsgAsync<HostPoolActor.RegisterConnectionRefs>(TimeSpan.FromSeconds(10));

        // Disconnect
        var endpoint = new IPEndPoint(IPAddress.Loopback, 8080);
        connectionActor.Tell(new ClientRunner.ClientDisconnected(endpoint));

        // Expect reconnect → new CreateTcpRunner
        await cmProbe.ExpectMsgAsync<ClientManager.CreateTcpRunner>(TimeSpan.FromSeconds(5));

        // Second connection
        var (_, _, connMsg2) = MakeConnectedMessage();
        connectionActor.Tell(connMsg2, cmProbe.Ref);
        var refs2 = await ExpectMsgAsync<HostPoolActor.RegisterConnectionRefs>(TimeSpan.FromSeconds(10));

        // New refs should be different objects
        Assert.NotSame(refs1.Source, refs2.Source);
        Assert.NotSame(refs1.Sink, refs2.Sink);
        Assert.Equal(connectionActor, refs2.Connection);
    }

    // ── Cleanup (PostStop) ───────────────────────────────────────────

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
        runnerProbe.ExpectMsg<DoClose>(TimeSpan.FromSeconds(5));

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

        runnerProbe.ExpectMsg<DoClose>(TimeSpan.FromSeconds(5));
    }

    [Fact(DisplayName = "CA-013: PostStop completes _responseQueue, closing the SourceRef stream")]
    public async Task CA_013_PostStop_CompletesResponseQueue()
    {
        var (connectionActor, cmProbe) = await CreateConnectionActorWithParent();

        // Connect to materialize _responseQueue
        var (_, _, connMsg) = MakeConnectedMessage();
        connectionActor.Tell(connMsg, cmProbe.Ref);
        var refs = await ExpectMsgAsync<HostPoolActor.RegisterConnectionRefs>(TimeSpan.FromSeconds(10));

        // Subscribe to the SourceRef before stopping
        var mat = Sys.Materializer();
        var itemsTask = refs.Source.Source.RunWith(Sink.Seq<DataItem>(), mat);

        // Stop the actor — PostStop calls _responseQueue.Complete()
        Sys.Stop(connectionActor);

        // The SourceRef stream should complete with zero items
        var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await itemsTask.WaitAsync(cts.Token);
        Assert.Empty(result);
    }

    [Fact(DisplayName = "CA-014: PostStop with null runner does not throw")]
    public void CA_014_PostStop_NullRunner_NoThrow()
    {
        var actor = CreateConnectionActor();
        ExpectMsg<ClientManager.CreateTcpRunner>();

        // Do NOT send ClientConnected — _runner stays null
        // Stop the actor — PostStop should not throw
        Watch(actor);
        Sys.Stop(actor);

        // Actor should terminate without issues
        ExpectTerminated(actor, TimeSpan.FromSeconds(3));
    }

    // ── TASK-4B-002: StreamRef push on connect ───────────────────────

    [Fact(DisplayName = "CA-016: ClientConnected tells parent RegisterConnectionRefs with valid refs")]
    public async Task CA_016_ClientConnected_TellsParentRegisterConnectionRefs()
    {
        var (connectionActor, cmProbe) = await CreateConnectionActorWithParent();

        var (_, _, connMsg) = MakeConnectedMessage();
        connectionActor.Tell(connMsg, cmProbe.Ref);

        var refs = await ExpectMsgAsync<HostPoolActor.RegisterConnectionRefs>(TimeSpan.FromSeconds(10));

        Assert.Equal(connectionActor, refs.Connection);
        Assert.NotNull(refs.Sink);
        Assert.NotNull(refs.Source);
    }

    [Fact(DisplayName = "CA-017: TCP bytes written to inbound channel are emitted on the registered SourceRef")]
    public async Task CA_017_InboundTcpBytes_EmittedOnSourceRef()
    {
        var (connectionActor, cmProbe) = await CreateConnectionActorWithParent();

        var (inbound, _, connMsg) = MakeConnectedMessage();
        connectionActor.Tell(connMsg, cmProbe.Ref);

        // Wait for materialization
        var refs = await ExpectMsgAsync<HostPoolActor.RegisterConnectionRefs>(TimeSpan.FromSeconds(10));

        // Subscribe to the SourceRef
        var mat = Sys.Materializer();
        var resultChannel = Channel.CreateUnbounded<DataItem>();
        _ = refs.Source.Source.RunForeach(item => resultChannel.Writer.TryWrite(item), mat);

        // Give the subscription a moment to establish
        await Task.Delay(100);

        // Write bytes to the inbound channel (simulating TCP data arriving)
        var owner = MemoryPool<byte>.Shared.Rent(8);
        owner.Memory.Span[0] = 0x42;
        await inbound.Writer.WriteAsync((owner, 8));

        // Read the emitted DataItem from the SourceRef
        var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
        var item = await resultChannel.Reader.ReadAsync(cts.Token);

        Assert.Equal(8, item.Length);
        Assert.Equal(0x42, item.Memory.Memory.Span[0]);

        item.Memory.Dispose();
    }

    // ── TASK-4B-008: SinkRef sends item to TCP outbound channel ──────

    [Fact(DisplayName = "CA-018: DataItem offered to ConnectionActor SinkRef appears in TCP outbound channel")]
    public async Task CA_018_SinkRef_ItemAppearsInOutboundChannel()
    {
        var (connectionActor, cmProbe) = await CreateConnectionActorWithParent();

        var (_, outbound, connMsg) = MakeConnectedMessage();
        connectionActor.Tell(connMsg, cmProbe.Ref);

        var refs = await ExpectMsgAsync<HostPoolActor.RegisterConnectionRefs>(TimeSpan.FromSeconds(10));

        var mat = Sys.Materializer();

        // Offer a DataItem via the SinkRef (simulates HostPoolActor writing a request to this connection)
        var owner = MemoryPool<byte>.Shared.Rent(4);
        owner.Memory.Span[0] = 0xDE;
        var item = new DataItem(owner, 4);

        Source.Single<DataItem>(item)
            .RunWith(refs.Sink.Sink, mat);

        // The ConnectionActor's ForEachAsync writes each item to the TCP outbound channel
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (mem, len) = await outbound.Reader.ReadAsync(cts.Token);

        Assert.Equal(4, len);
        Assert.Equal(0xDE, mem.Memory.Span[0]);

        mem.Dispose();
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Proxy actor that spawns a ConnectionActor as its child and forwards all
    /// messages received from the child (via Context.Parent.Tell) to <paramref name="forwardTo"/>.
    /// This allows TestActor to intercept parent-bound messages in unit tests.
    /// </summary>
    private sealed class ConnectionActorParent : ReceiveActor
    {
        public ConnectionActorParent(TcpOptions options, IActorRef clientManager, IActorRef forwardTo)
        {
            Context.ActorOf(Props.Create(() => new ConnectionActor(options, clientManager)), "connection");
            ReceiveAny(msg => forwardTo.Forward(msg));
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
