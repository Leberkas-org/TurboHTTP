using System;
using System.Buffers;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
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
}
