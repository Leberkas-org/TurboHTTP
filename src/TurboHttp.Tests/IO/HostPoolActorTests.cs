using System;
using System.Buffers;
using System.Net;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Hosting;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit2;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.Tests.IO;

public sealed class HostPoolActorTests : TestKit
{
    private static TcpOptions MakeOptions(string host = "test.local", int port = 8080)
        => new() { Host = host, Port = port };

    private static PoolConfig MakeConfig()
        => new(MaxConnectionsPerHost: 5, MaxRequestsPerConnection: 10);

    /// <summary>
    /// Creates a HostPoolActor inside a bidirectional proxy.
    /// Messages sent to the returned proxy ref are forwarded to the HostPoolActor child.
    /// Messages sent by the HostPoolActor to its parent (the proxy) are forwarded to TestActor.
    /// </summary>
    private IActorRef CreateProxy(TcpOptions? options = null, PoolConfig? config = null)
    {
        var opts = options ?? MakeOptions();
        var cfg = config ?? MakeConfig();
        return Sys.ActorOf(Props.Create(() => new HostPoolActorProxy(opts, cfg, TestActor)));
    }

    // ── HA-001: MergeHub aggregates responses from two connection sources ─────

    [Fact(DisplayName = "HA-001: Two connection ResponseSources registered → both responses appear on merged output")]
    public async Task HA_001_TwoSourceRefs_BothAppearOnMergedOutput()
    {
        var clientManagerProbe = CreateTestProbe();
        ActorRegistry.For(Sys).Register<ClientManager>(clientManagerProbe.Ref);

        var proxy = CreateProxy();
        var mat = Sys.Materializer();

        // HostPoolActor sends RegisterHostResponseSource to its parent (proxy) in PreStart.
        // The proxy forwards it to TestActor.
        var registerMsg = await ExpectMsgAsync<PoolRouterActor.RegisterHostResponseSource>(TimeSpan.FromSeconds(10));
        Assert.NotNull(registerMsg.ResponseSource);

        // Subscribe to the merged response source
        var resultChannel = Channel.CreateUnbounded<DataItem>();
        _ = registerMsg.ResponseSource.RunForeach(item => resultChannel.Writer.TryWrite(item), mat);

        await Task.Delay(200); // let subscription establish

        // Create two fake connection response queues
        var (queue1, source1) = Source.Queue<DataItem>(10, OverflowStrategy.Backpressure).PreMaterialize(mat);
        var (queue2, source2) = Source.Queue<DataItem>(10, OverflowStrategy.Backpressure).PreMaterialize(mat);

        // Register both connections with HostPoolActor
        var probe1 = CreateTestProbe();
        var probe2 = CreateTestProbe();
        proxy.Tell(new HostPoolActor.RegisterConnectionRefs(probe1.Ref, source1));
        proxy.Tell(new HostPoolActor.RegisterConnectionRefs(probe2.Ref, source2));

        await Task.Delay(300); // let stream wiring complete

        // Push one item from each connection's response queue
        var owner1 = MemoryPool<byte>.Shared.Rent(4);
        owner1.Memory.Span[0] = 0xAA;
        await queue1.OfferAsync(new DataItem(owner1, 4));

        var owner2 = MemoryPool<byte>.Shared.Rent(4);
        owner2.Memory.Span[0] = 0xBB;
        await queue2.OfferAsync(new DataItem(owner2, 4));

        // Both items must appear on the merged output (order is not guaranteed)
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));

        var item1 = await resultChannel.Reader.ReadAsync(cts.Token);
        var item2 = await resultChannel.Reader.ReadAsync(cts.Token);

        var firstByte1 = item1.Memory.Memory.Span[0];
        var firstByte2 = item2.Memory.Memory.Span[0];

        var bytes = new[] { firstByte1, firstByte2 };
        Array.Sort(bytes);

        Assert.Equal(0xAA, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);

        item1.Memory.Dispose();
        item2.Memory.Dispose();
    }

    // ── HA-002: Request routing selects idle connection's queue ───────────────────────

    [Fact(DisplayName = "HA-002: Request routing selects idle connection's queue")]
    public async Task HA_002_RoutingSelectsIdleConnectionQueue()
    {
        // Register a TestProbe as the ClientManager BEFORE creating the proxy so that
        // HostPoolActor.SpawnConnection() → Context.GetActor<ClientManager>() resolves
        // to the probe instead of throwing MissingActorRegistryEntryException.
        var clientManagerProbe = CreateTestProbe();
        ActorRegistry.For(Sys).Register<ClientManager>(clientManagerProbe.Ref);

        var proxy = CreateProxy();

        // Consume the RegisterHostResponseSource that HostPoolActor sends in PreStart.
        await ExpectMsgAsync<PoolRouterActor.RegisterHostResponseSource>(TimeSpan.FromSeconds(10));

        // Send a DataItem to HostPoolActor — no connections have queues yet, so it enqueues as pending.
        var pendingOwner = MemoryPool<byte>.Shared.Rent(8);
        pendingOwner.Memory.Span[0] = 0xCC;
        proxy.Tell(new DataItem(pendingOwner, 8));

        // Capture the CreateTcpRunner that ConnectionActor sends to its clientManager.
        var createMsg = clientManagerProbe.ExpectMsg<ClientManager.CreateTcpRunner>(TimeSpan.FromSeconds(5));
        var connectionActor = createMsg.Handler;

        // Create inbound/outbound channels simulating a TCP connection
        var inbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var outbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var endpoint = new IPEndPoint(IPAddress.Loopback, 8080);
        var connectedMsg = new ClientRunner.ClientConnected(endpoint, inbound.Reader, outbound.Writer);

        // Send ClientConnected to ConnectionActor — it will pre-materialize its response queue and
        // tell its parent (HostPoolActor) with RegisterConnectionRefs.
        // HostPoolActor then:
        //   1. Creates a per-connection queue
        //   2. Wires queue → Sink.ForEach → connection.Tell(item)
        //   3. Wires ResponseSource → MergeHub
        //   4. Calls DrainPending → routes the pending DataItem to the connection's queue
        //      → ForEach fires → connection.Tell(dataItem) → writes to outbound channel
        var runnerProbe = CreateTestProbe();
        connectionActor.Tell(connectedMsg, runnerProbe.Ref);

        // The pending DataItem should flow through to the TCP outbound channel
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (outboundMem, outboundLen) = await outbound.Reader.ReadAsync(cts.Token);

        Assert.Equal(8, outboundLen);
        Assert.Equal(0xCC, outboundMem.Memory.Span[0]);

        outboundMem.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bidirectional proxy actor.
    /// Messages from the HostPoolActor child (sender == _hostPool) are forwarded to <paramref name="forwardTo"/>.
    /// All other messages are forwarded to the HostPoolActor child.
    /// </summary>
    private sealed class HostPoolActorProxy : ReceiveActor
    {
        private readonly IActorRef _hostPool;

        public HostPoolActorProxy(TcpOptions options, PoolConfig config, IActorRef forwardTo)
        {
            _hostPool = Context.ActorOf(
                Props.Create(() => new HostPoolActor(new HostPoolActor.HostPoolConfig(options, config, HostKey.Default))),
                "host-pool");

            ReceiveAny(msg =>
            {
                if (Sender.Equals(_hostPool))
                {
                    forwardTo.Forward(msg);
                }
                else
                {
                    _hostPool.Forward(msg);
                }
            });
        }
    }
}
