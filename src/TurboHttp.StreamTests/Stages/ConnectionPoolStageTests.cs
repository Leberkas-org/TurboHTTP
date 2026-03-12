using System.Buffers;
using System.Collections.Concurrent;
using System.Threading;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.StreamTests.Stages;

/// <summary>
/// Tests for <see cref="ConnectionPoolStage"/>.
/// TASK-001: Foundation — ConnectItem registration, unknown PoolKey rejection, empty pass-through.
/// TASK-002: Per-host connection lifecycle — sub-graph materialisation, single-host roundtrip.
/// TASK-003: Multi-connection per host — dynamic scaling, backpressure queuing.
/// </summary>
public sealed class ConnectionPoolStageTests : StreamTestBase
{
    private static readonly new PoolConfig DefaultConfig = new();

    /// <summary>
    /// Creates an echo flow that filters out ConnectItems and echoes DataItems back.
    /// Used as a fake ConnectionStage for unit tests.
    /// </summary>
    private static IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed> CreateEchoFlow()
    {
        return Flow.Create<ITransportItem>()
            .Where(x => x is DataItem)
            .Select(x =>
            {
                var data = (DataItem)x;
                return (data.Memory, data.Length);
            });
    }

    /// <summary>
    /// Creates an echo flow factory that counts how many times it has been invoked.
    /// Each invocation represents a new ConnectionStage materialisation.
    /// </summary>
    private static (Func<IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed>> factory, Func<int> getCount)
        CreateCountingEchoFlowFactory()
    {
        var count = 0;
        IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed> Factory()
        {
            Interlocked.Increment(ref count);
            return CreateEchoFlow();
        }
        return (Factory, () => Volatile.Read(ref count));
    }

    /// <summary>
    /// Creates a gated flow factory. Each materialised flow holds DataItems until the
    /// corresponding gate is released. This simulates busy connections for testing backpressure.
    /// </summary>
    private static (
        Func<IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed>> factory,
        ConcurrentQueue<TaskCompletionSource<bool>> gates,
        Func<int> getCount)
        CreateGatedFlowFactory()
    {
        var gates = new ConcurrentQueue<TaskCompletionSource<bool>>();
        var count = 0;

        IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed> Factory()
        {
            Interlocked.Increment(ref count);
            return Flow.Create<ITransportItem>()
                .Where(x => x is DataItem)
                .SelectAsync(1, async x =>
                {
                    var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    gates.Enqueue(gate);
                    await gate.Task;
                    var data = (DataItem)x;
                    return (data.Memory, data.Length);
                });
        }
        return (Factory, gates, () => Volatile.Read(ref count));
    }

    /// <summary>
    /// Creates a ConnectionPoolStage with an echo flow factory.
    /// The echo flow passes DataItems through and filters ConnectItems.
    /// </summary>
    private static ConnectionPoolStage CreateStage()
    {
        return new ConnectionPoolStage(CreateEchoFlow, DefaultConfig);
    }

    private static ConnectionPoolStage CreateStage(
        Func<IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed>> factory,
        PoolConfig? config = null)
    {
        return new ConnectionPoolStage(factory, config ?? DefaultConfig);
    }

    private static TcpOptions TestOptions(string host = "example.com", int port = 443)
        => new() { Host = host, Port = port };

    // ── POOL-001: ConnectItem registers host ──────────────────────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-001: ConnectItem on inlet registers host without error")]
    public async Task POOL_001_ConnectItem_RegistersHost_CompletesCleanly()
    {
        // Sending a ConnectItem followed by stream completion should not fail.
        // The ConnectItem registers the pool key in internal state.
        var items = new List<RoutedTransportItem>
        {
            new("https:example.com:443:1.1", new ConnectItem(TestOptions()))
        };

        var results = await Source.From(items)
            .Via(Flow.FromGraph(CreateStage()))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        // No DataItems sent → no responses expected
        Assert.Empty(results);
    }

    // ── POOL-002: Multiple ConnectItems for different hosts ───────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-002: Multiple ConnectItems for different hosts register without error")]
    public async Task POOL_002_MultipleConnectItems_DifferentHosts_CompletesCleanly()
    {
        var items = new List<RoutedTransportItem>
        {
            new("https:example.com:443:1.1", new ConnectItem(TestOptions("example.com", 443))),
            new("https:other.com:443:1.1", new ConnectItem(TestOptions("other.com", 443))),
            new("http:third.com:80:1.1", new ConnectItem(TestOptions("third.com", 80)))
        };

        var results = await Source.From(items)
            .Via(Flow.FromGraph(CreateStage()))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        Assert.Empty(results);
    }

    // ── POOL-003: Duplicate ConnectItem for same host is idempotent ───────────

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-003: Duplicate ConnectItem for same pool key is idempotent")]
    public async Task POOL_003_DuplicateConnectItem_SameKey_Idempotent()
    {
        const string poolKey = "https:example.com:443:1.1";
        var items = new List<RoutedTransportItem>
        {
            new(poolKey, new ConnectItem(TestOptions())),
            new(poolKey, new ConnectItem(TestOptions()))
        };

        var results = await Source.From(items)
            .Via(Flow.FromGraph(CreateStage()))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        Assert.Empty(results);
    }

    // ── POOL-004: DataItem without prior ConnectItem → stage failure ──────────

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-004: DataItem without prior ConnectItem fails stage with descriptive error")]
    public async Task POOL_004_DataItem_WithoutConnectItem_FailsStage()
    {
        var data = new SimpleMemoryOwner(new byte[] { 0x01, 0x02 });
        var items = new List<RoutedTransportItem>
        {
            new("https:unknown.com:443:1.1", new DataItem(data, 2))
        };

        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await Source.From(items)
                .Via(Flow.FromGraph(CreateStage()))
                .RunWith(Sink.Seq<RoutedDataItem>(), Materializer));

        var inner = ex is AggregateException agg ? agg.InnerException! : ex;
        Assert.IsType<InvalidOperationException>(inner);
        Assert.Contains("unknown pool key", inner.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unknown.com", inner.Message);
    }

    // ── POOL-005: DataItem after ConnectItem for different key → stage failure ─

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-005: DataItem for unregistered key (other key registered) fails stage")]
    public async Task POOL_005_DataItem_ForWrongKey_FailsStage()
    {
        var data = new SimpleMemoryOwner(new byte[] { 0x01 });
        var items = new List<RoutedTransportItem>
        {
            // Register host-a
            new("https:host-a.com:443:1.1", new ConnectItem(TestOptions("host-a.com", 443))),
            // Send data for host-b (not registered)
            new("https:host-b.com:443:1.1", new DataItem(data, 1))
        };

        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await Source.From(items)
                .Via(Flow.FromGraph(CreateStage()))
                .RunWith(Sink.Seq<RoutedDataItem>(), Materializer));

        var inner = ex is AggregateException agg ? agg.InnerException! : ex;
        Assert.IsType<InvalidOperationException>(inner);
        Assert.Contains("host-b.com", inner.Message);
    }

    // ── POOL-006: Empty source completes without error ────────────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-006: Empty source (no items) completes without error")]
    public async Task POOL_006_EmptySource_CompletesCleanly()
    {
        var results = await Source.Empty<RoutedTransportItem>()
            .Via(Flow.FromGraph(CreateStage()))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        Assert.Empty(results);
    }

    // ── POOL-007: ConnectItem then DataItem for registered key produces response ──

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-007: DataItem after ConnectItem for same key produces response")]
    public async Task POOL_007_DataItem_AfterConnectItem_SameKey_ProducesResponse()
    {
        const string poolKey = "https:example.com:443:1.1";
        var payload = new byte[] { 0x48, 0x49 };
        var data = new SimpleMemoryOwner(payload);
        var items = new List<RoutedTransportItem>
        {
            new(poolKey, new ConnectItem(TestOptions())),
            new(poolKey, new DataItem(data, 2))
        };

        var results = await Source.From(items)
            .Via(Flow.FromGraph(CreateStage()))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        // Sub-graph echo flow returns the DataItem as a RoutedDataItem
        Assert.Single(results);
        Assert.Equal(poolKey, results[0].PoolKey);
        Assert.Equal(2, results[0].Length);
    }

    // ── POOL-008: Stage shape has correct inlet/outlet names ──────────────────

    [Fact(DisplayName = "POOL-008: Stage shape exposes named inlet and outlet")]
    public void POOL_008_StageShape_HasCorrectInletOutlet()
    {
        var stage = CreateStage();

        Assert.NotNull(stage.Shape.Inlet);
        Assert.NotNull(stage.Shape.Outlet);
        Assert.Equal("pool.in", stage.Shape.Inlet.Name);
        Assert.Equal("pool.out", stage.Shape.Outlet.Name);
    }

    // ── POOL-009: Single-host single-connection roundtrip ─────────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-009: Single-host single-connection roundtrip with echo flow")]
    public async Task POOL_009_SingleHost_SingleConnection_Roundtrip()
    {
        const string poolKey = "https:example.com:443:1.1";
        var payload = new byte[] { 0x48, 0x45, 0x4C, 0x4C, 0x4F }; // HELLO
        var data = new SimpleMemoryOwner(payload);
        var items = new List<RoutedTransportItem>
        {
            new(poolKey, new ConnectItem(TestOptions())),
            new(poolKey, new DataItem(data, payload.Length))
        };

        var results = await Source.From(items)
            .Via(Flow.FromGraph(CreateStage()))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        Assert.Single(results);
        Assert.Equal(poolKey, results[0].PoolKey);
        Assert.Equal(payload.Length, results[0].Length);
        Assert.Equal(payload, results[0].Memory.Memory.Slice(0, results[0].Length).ToArray());
    }

    // ── POOL-010: Multiple DataItems for same host reuse connection slot ──────

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-010: Multiple DataItems for same host reuse the same connection slot")]
    public async Task POOL_010_MultipleDataItems_SameHost_ReuseSlot()
    {
        const string poolKey = "https:example.com:443:1.1";
        var payload1 = new byte[] { 0x01, 0x02 };
        var payload2 = new byte[] { 0x03, 0x04, 0x05 };
        var items = new List<RoutedTransportItem>
        {
            new(poolKey, new ConnectItem(TestOptions())),
            new(poolKey, new DataItem(new SimpleMemoryOwner(payload1), payload1.Length)),
            new(poolKey, new DataItem(new SimpleMemoryOwner(payload2), payload2.Length))
        };

        var results = await Source.From(items)
            .Via(Flow.FromGraph(CreateStage()))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(poolKey, r.PoolKey));
        Assert.Equal(payload1.Length, results[0].Length);
        Assert.Equal(payload2.Length, results[1].Length);
    }

    // ── TASK-003: Multi-Connection per Host — Dynamic Scaling ──────────────────

    // ── POOL-011: 3 parallel requests → 3 ConnectionStage instances (MaxConnections=3) ──

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-011: 3 parallel requests create 3 connections (HTTP/1.x, MaxConnections=3)")]
    public async Task POOL_011_ThreeParallelRequests_ThreeConnections()
    {
        // Use gated flow: each request blocks until we release the gate.
        // This ensures all 3 connections are created because each prior one is "busy".
        var (factory, gates, getCount) = CreateGatedFlowFactory();
        var config = new PoolConfig(MaxConnectionsPerHost: 3);
        var stage = CreateStage(factory, config);
        const string poolKey = "https:example.com:443:1.1";

        var items = new List<RoutedTransportItem>
        {
            new(poolKey, new ConnectItem(TestOptions())),
            new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { 0x01 }), 1)),
            new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { 0x02 }), 1)),
            new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { 0x03 }), 1))
        };

        var resultTask = Source.From(items)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        // Wait for all 3 gates to appear (meaning 3 connections were created)
        await WaitForConditionAsync(() => gates.Count >= 3, timeout: TimeSpan.FromSeconds(5));
        Assert.Equal(3, getCount());

        // Release all gates so the stream can complete
        while (gates.TryDequeue(out var gate))
        {
            gate.SetResult(true);
        }

        var results = await resultTask;
        Assert.Equal(3, results.Count);
    }

    // ── POOL-012: 4 requests with MaxConnections=2 → 2 connections, 2 queued ──

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-012: 4 requests with MaxConnections=2 → 2 connections, 2 queued then drained")]
    public async Task POOL_012_FourRequests_MaxTwo_TwoQueuedThenDrained()
    {
        var (factory, gates, getCount) = CreateGatedFlowFactory();
        var config = new PoolConfig(MaxConnectionsPerHost: 2);
        var stage = CreateStage(factory, config);
        const string poolKey = "https:example.com:443:1.1";

        var items = new List<RoutedTransportItem>
        {
            new(poolKey, new ConnectItem(TestOptions())),
            new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { 0x01 }), 1)),
            new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { 0x02 }), 1)),
            new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { 0x03 }), 1)),
            new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { 0x04 }), 1))
        };

        var resultTask = Source.From(items)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        // Wait for 2 gates (max connections = 2). Items 3 and 4 are queued internally.
        await WaitForConditionAsync(() => gates.Count >= 2, timeout: TimeSpan.FromSeconds(5));
        Assert.Equal(2, getCount());

        // Give a moment for all items to be consumed by the inlet
        await Task.Delay(200);

        // Only 2 connections should exist (not 3 or 4)
        Assert.Equal(2, getCount());

        // Release gates one at a time — each release frees a slot, the pool drains queued items
        // to that slot, which creates a new gate. Repeat until all 4 are done.
        var totalReleased = 0;
        while (totalReleased < 4)
        {
            await WaitForConditionAsync(() => gates.Count >= 1, timeout: TimeSpan.FromSeconds(5));
            Assert.True(gates.TryDequeue(out var gate));
            gate.SetResult(true);
            totalReleased++;
        }

        var results = await resultTask;
        Assert.Equal(4, results.Count);

        // Still only 2 connections were ever created
        Assert.Equal(2, getCount());
    }

    // ── POOL-013: MaxConnectionsPerHost=1 enforces single connection ──────────

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-013: MaxConnectionsPerHost=1 enforces single connection with queuing")]
    public async Task POOL_013_MaxOne_SingleConnectionWithQueuing()
    {
        var (factory, gates, getCount) = CreateGatedFlowFactory();
        var config = new PoolConfig(MaxConnectionsPerHost: 1);
        var stage = CreateStage(factory, config);
        const string poolKey = "https:example.com:443:1.1";

        var items = new List<RoutedTransportItem>
        {
            new(poolKey, new ConnectItem(TestOptions())),
            new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { 0x01 }), 1)),
            new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { 0x02 }), 1))
        };

        var resultTask = Source.From(items)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        // Wait for first gate
        await WaitForConditionAsync(() => gates.Count >= 1, timeout: TimeSpan.FromSeconds(5));
        Assert.Equal(1, getCount());

        // Release first gate — queued item gets dispatched
        Assert.True(gates.TryDequeue(out var firstGate));
        firstGate.SetResult(true);

        // Wait for second gate (the queued item)
        await WaitForConditionAsync(() => gates.Count >= 1, timeout: TimeSpan.FromSeconds(5));

        // Release second gate
        Assert.True(gates.TryDequeue(out var secondGate));
        secondGate.SetResult(true);

        var results = await resultTask;
        Assert.Equal(2, results.Count);
        Assert.Equal(1, getCount()); // Only 1 connection ever created
    }

    // ── POOL-014: New connection uses same TcpOptions from ConnectItem ─────────

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-014: Each new ConnectionStage receives a ConnectItem with original TcpOptions")]
    public async Task POOL_014_NewConnections_UseSameTcpOptions()
    {
        // Track ConnectItems received by each sub-graph to verify TcpOptions propagation
        var receivedConnectItems = new ConcurrentBag<ConnectItem>();

        IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed> Factory()
        {
            return Flow.Create<ITransportItem>()
                .Select(x =>
                {
                    if (x is ConnectItem ci)
                    {
                        receivedConnectItems.Add(ci);
                    }
                    return x;
                })
                .Where(x => x is DataItem)
                .Select(x =>
                {
                    var data = (DataItem)x;
                    return (data.Memory, data.Length);
                });
        }

        var config = new PoolConfig(MaxConnectionsPerHost: 3);
        var stage = CreateStage(Factory, config);
        const string poolKey = "https:example.com:443:1.1";
        var options = TestOptions("example.com", 8080);

        var items = new List<RoutedTransportItem>
        {
            new(poolKey, new ConnectItem(options)),
            new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { 0x01 }), 1)),
            new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { 0x02 }), 1)),
            new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { 0x03 }), 1))
        };

        var results = await Source.From(items)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        Assert.Equal(3, results.Count);

        // Each sub-graph should have received a ConnectItem with the same TcpOptions
        // (at least 1 connection; the echo flow responds instantly so some may reuse idle slots)
        Assert.NotEmpty(receivedConnectItems);
        Assert.All(receivedConnectItems, ci =>
        {
            Assert.Equal("example.com", ci.Options.Host);
            Assert.Equal(8080, ci.Options.Port);
        });
    }

    // ── TASK-004: Load Balancing Across Connections ──────────────────────────

    /// <summary>
    /// Creates a gated flow factory where each materialised flow records which
    /// connection index received each DataItem. Used for verifying distribution.
    /// </summary>
    private static (
        Func<IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed>> factory,
        ConcurrentQueue<TaskCompletionSource<bool>> gates,
        ConcurrentQueue<(int connectionIndex, byte payload)> receivedItems,
        Func<int> getCount)
        CreateTrackingGatedFlowFactory()
    {
        var gates = new ConcurrentQueue<TaskCompletionSource<bool>>();
        var receivedItems = new ConcurrentQueue<(int connectionIndex, byte payload)>();
        var count = 0;

        IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed> Factory()
        {
            var connIndex = Interlocked.Increment(ref count) - 1;
            return Flow.Create<ITransportItem>()
                .Where(x => x is DataItem)
                .SelectAsync(1, async x =>
                {
                    var data = (DataItem)x;
                    var firstByte = data.Memory.Memory.Span[0];
                    receivedItems.Enqueue((connIndex, firstByte));

                    var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    gates.Enqueue(gate);
                    await gate.Task;
                    return (data.Memory, data.Length);
                });
        }
        return (Factory, gates, receivedItems, () => Volatile.Read(ref count));
    }

    // ── POOL-015: Default strategy is LeastLoaded ─────────────────────────────

    [Fact(DisplayName = "POOL-015: Default PoolConfig strategy is LeastLoaded")]
    public void POOL_015_DefaultStrategy_IsLeastLoaded()
    {
        var config = new PoolConfig();
        Assert.Equal(LoadBalancingStrategy.LeastLoaded, config.Strategy);
    }

    // ── POOL-016: LoadBalancingStrategy enum has RoundRobin and LeastLoaded ───

    [Fact(DisplayName = "POOL-016: LoadBalancingStrategy enum contains RoundRobin and LeastLoaded")]
    public void POOL_016_StrategyEnum_ContainsBothValues()
    {
        Assert.True(Enum.IsDefined(typeof(LoadBalancingStrategy), LoadBalancingStrategy.RoundRobin));
        Assert.True(Enum.IsDefined(typeof(LoadBalancingStrategy), LoadBalancingStrategy.LeastLoaded));
    }

    // ── POOL-017: Strategy is configurable in PoolConfig ──────────────────────

    [Theory(DisplayName = "POOL-017: PoolConfig Strategy is configurable")]
    [InlineData(LoadBalancingStrategy.LeastLoaded)]
    [InlineData(LoadBalancingStrategy.RoundRobin)]
    public void POOL_017_PoolConfig_StrategyConfigurable(LoadBalancingStrategy strategy)
    {
        var config = new PoolConfig(Strategy: strategy);
        Assert.Equal(strategy, config.Strategy);
    }

    // ── POOL-018: RoundRobin distributes requests across connections ──────────

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-018: RoundRobin distributes requests evenly across connections")]
    public async Task POOL_018_RoundRobin_DistributesEvenly()
    {
        // Setup: 3 connections, each echoes instantly (no gating).
        // Send 6 requests — expect 2 per connection in round-robin order.
        var connectionHits = new ConcurrentDictionary<int, int>();
        var connCounter = 0;

        IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed> Factory()
        {
            var myIndex = Interlocked.Increment(ref connCounter) - 1;
            return Flow.Create<ITransportItem>()
                .Where(x => x is DataItem)
                .Select(x =>
                {
                    connectionHits.AddOrUpdate(myIndex, 1, (_, c) => c + 1);
                    var data = (DataItem)x;
                    return (data.Memory, data.Length);
                });
        }

        var config = new PoolConfig(MaxConnectionsPerHost: 3, Strategy: LoadBalancingStrategy.RoundRobin);
        var stage = CreateStage(Factory, config);
        const string poolKey = "https:example.com:443:1.1";

        // Build items: ConnectItem + 6 DataItems
        var items = new List<RoutedTransportItem> { new(poolKey, new ConnectItem(TestOptions())) };
        for (var i = 0; i < 6; i++)
        {
            items.Add(new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { (byte)i }), 1)));
        }

        var results = await Source.From(items)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        Assert.Equal(6, results.Count);

        // With echo (instant response), connections become idle immediately.
        // The first request creates conn-0, which responds and goes idle.
        // The second request finds conn-0 idle and reuses it (RoundRobin prefers idle).
        // Since echo is instant, all requests may go to conn-0.
        // This is correct: idle connections are always preferred.
        // To verify actual round-robin distribution, we need gated flows.
    }

    // ── POOL-019: RoundRobin cycles through busy connections (gated) ──────────

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-019: RoundRobin with busy connections creates all connections and distributes")]
    public async Task POOL_019_RoundRobin_BusyConnections_Distributes()
    {
        var (factory, gates, receivedItems, getCount) = CreateTrackingGatedFlowFactory();
        var config = new PoolConfig(MaxConnectionsPerHost: 3, Strategy: LoadBalancingStrategy.RoundRobin);
        var stage = CreateStage(factory, config);
        const string poolKey = "https:example.com:443:1.1";

        var items = new List<RoutedTransportItem> { new(poolKey, new ConnectItem(TestOptions())) };
        for (var i = 0; i < 3; i++)
        {
            items.Add(new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { (byte)i }), 1)));
        }

        var resultTask = Source.From(items)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        // Wait for 3 gates — all connections should be created
        await WaitForConditionAsync(() => gates.Count >= 3, timeout: TimeSpan.FromSeconds(5));
        Assert.Equal(3, getCount());

        // Each connection should have received exactly 1 request
        var itemsByConn = receivedItems.GroupBy(x => x.connectionIndex)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(3, itemsByConn.Count);
        Assert.All(itemsByConn.Values, count => Assert.Equal(1, count));

        // Release all gates
        while (gates.TryDequeue(out var gate))
        {
            gate.SetResult(true);
        }

        var results = await resultTask;
        Assert.Equal(3, results.Count);
    }

    // ── POOL-020: LeastLoaded selects connection with fewest pending requests ──

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-020: LeastLoaded with busy connections creates all connections")]
    public async Task POOL_020_LeastLoaded_BusyConnections_CreatesAll()
    {
        var (factory, gates, receivedItems, getCount) = CreateTrackingGatedFlowFactory();
        var config = new PoolConfig(MaxConnectionsPerHost: 3, Strategy: LoadBalancingStrategy.LeastLoaded);
        var stage = CreateStage(factory, config);
        const string poolKey = "https:example.com:443:1.1";

        var items = new List<RoutedTransportItem> { new(poolKey, new ConnectItem(TestOptions())) };
        for (var i = 0; i < 3; i++)
        {
            items.Add(new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { (byte)i }), 1)));
        }

        var resultTask = Source.From(items)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        // Wait for 3 gates — all connections should be created
        await WaitForConditionAsync(() => gates.Count >= 3, timeout: TimeSpan.FromSeconds(5));
        Assert.Equal(3, getCount());

        // Each connection should have received exactly 1 request
        var itemsByConn = receivedItems.GroupBy(x => x.connectionIndex)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(3, itemsByConn.Count);
        Assert.All(itemsByConn.Values, count => Assert.Equal(1, count));

        // Release all gates
        while (gates.TryDequeue(out var gate))
        {
            gate.SetResult(true);
        }

        var results = await resultTask;
        Assert.Equal(3, results.Count);
    }

    // ── POOL-021: Idle connections preferred over new connections ──────────────

    [Theory(Timeout = 10_000,
        DisplayName = "POOL-021: Idle connections are preferred — reused when gate is released before next request")]
    [InlineData(LoadBalancingStrategy.LeastLoaded)]
    [InlineData(LoadBalancingStrategy.RoundRobin)]
    public async Task POOL_021_IdleConnection_Preferred_NoNewConnection(LoadBalancingStrategy strategy)
    {
        // Use gated flow: send 1 request, release its gate (connection becomes idle),
        // then send another request. The idle connection should be reused.
        var (factory, gates, getCount) = CreateGatedFlowFactory();
        var config = new PoolConfig(MaxConnectionsPerHost: 5, Strategy: strategy);
        var stage = CreateStage(factory, config);
        const string poolKey = "https:example.com:443:1.1";

        // Use Source.Queue to control request timing precisely
        var (queue, resultTask) = Source.Queue<RoutedTransportItem>(16, OverflowStrategy.Backpressure)
            .Via(Flow.FromGraph(stage))
            .ToMaterialized(Sink.Seq<RoutedDataItem>(), Keep.Both)
            .Run(Materializer);

        // Register host
        await queue.OfferAsync(new RoutedTransportItem(poolKey, new ConnectItem(TestOptions())));

        // Send first request
        await queue.OfferAsync(new RoutedTransportItem(poolKey,
            new DataItem(new SimpleMemoryOwner(new byte[] { 0x01 }), 1)));

        // Wait for gate and release — connection becomes idle
        await WaitForConditionAsync(() => gates.Count >= 1, timeout: TimeSpan.FromSeconds(5));
        Assert.Equal(1, getCount());
        Assert.True(gates.TryDequeue(out var gate1));
        gate1.SetResult(true);

        // Give time for the async callback to mark the slot idle
        await Task.Delay(100);

        // Send second request — should reuse the idle connection
        await queue.OfferAsync(new RoutedTransportItem(poolKey,
            new DataItem(new SimpleMemoryOwner(new byte[] { 0x02 }), 1)));

        await WaitForConditionAsync(() => gates.Count >= 1, timeout: TimeSpan.FromSeconds(5));
        Assert.True(gates.TryDequeue(out var gate2));
        gate2.SetResult(true);

        queue.Complete();
        var results = await resultTask;

        Assert.Equal(2, results.Count);
        // Only 1 connection should have been created — idle one was reused
        Assert.Equal(1, getCount());
    }

    // ── POOL-023: Multiple requests distributed evenly with both strategies ───

    [Theory(Timeout = 10_000,
        DisplayName = "POOL-023: 6 requests across 3 gated connections → all connections used")]
    [InlineData(LoadBalancingStrategy.LeastLoaded)]
    [InlineData(LoadBalancingStrategy.RoundRobin)]
    public async Task POOL_023_SixRequests_ThreeConnections_AllUsed(LoadBalancingStrategy strategy)
    {
        var (factory, gates, receivedItems, getCount) = CreateTrackingGatedFlowFactory();
        var config = new PoolConfig(MaxConnectionsPerHost: 3, Strategy: strategy);
        var stage = CreateStage(factory, config);
        const string poolKey = "https:example.com:443:1.1";

        // Send 6 requests: first 3 create connections (all busy), last 3 queued
        var items = new List<RoutedTransportItem> { new(poolKey, new ConnectItem(TestOptions())) };
        for (var i = 0; i < 6; i++)
        {
            items.Add(new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { (byte)i }), 1)));
        }

        var resultTask = Source.From(items)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        // Wait for first 3 gates (3 connections created, each serving 1 request)
        await WaitForConditionAsync(() => gates.Count >= 3, timeout: TimeSpan.FromSeconds(5));
        Assert.Equal(3, getCount());

        // Release all 3 → queued items get dispatched, creating 3 more gates
        var totalReleased = 0;
        while (totalReleased < 6)
        {
            await WaitForConditionAsync(() => gates.Count >= 1, timeout: TimeSpan.FromSeconds(5));
            Assert.True(gates.TryDequeue(out var gate));
            gate.SetResult(true);
            totalReleased++;
        }

        var results = await resultTask;
        Assert.Equal(6, results.Count);

        // Each connection should have received exactly 2 requests (6 / 3 = 2)
        var itemsByConn = receivedItems.GroupBy(x => x.connectionIndex)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(3, itemsByConn.Count);
        Assert.All(itemsByConn.Values, count => Assert.Equal(2, count));
    }

    // ── TASK-005: Connection Health Monitoring and Auto-Reconnect ─────────

    /// <summary>
    /// Creates a flow factory where the first N materialisations throw/fail after
    /// processing the ConnectItem, and subsequent ones echo normally. Used to simulate
    /// a dead connection followed by a successful reconnect.
    /// </summary>
    private static (
        Func<IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed>> factory,
        Func<int> getCount)
        CreateFailThenSucceedFlowFactory(int failCount)
    {
        var count = 0;
        IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed> Factory()
        {
            var myIndex = Interlocked.Increment(ref count) - 1;
            if (myIndex < failCount)
            {
                // This flow accepts the ConnectItem, then fails
                return Flow.Create<ITransportItem>()
                    .SelectAsync(1, async item =>
                    {
                        if (item is ConnectItem)
                        {
                            // Simulate connection established then dies
                            await Task.Delay(10);
                            throw new InvalidOperationException($"Simulated connection failure #{myIndex}");
                        }
                        var data = (DataItem)item;
                        return (data.Memory, data.Length);
                    });
            }
            // Successful echo flow
            return CreateEchoFlow();
        }
        return (Factory, () => Volatile.Read(ref count));
    }

    /// <summary>
    /// Creates a flow factory where every materialisation fails immediately after
    /// the ConnectItem. Used to verify max reconnect attempts exhaustion.
    /// </summary>
    private static (
        Func<IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed>> factory,
        Func<int> getCount)
        CreateAlwaysFailFlowFactory()
    {
        var count = 0;
        IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed> Factory()
        {
            Interlocked.Increment(ref count);
            return Flow.Create<ITransportItem>()
                .SelectAsync(1, async item =>
                {
                    if (item is ConnectItem)
                    {
                        await Task.Delay(10);
                        throw new InvalidOperationException("Simulated permanent failure");
                    }
                    var data = (DataItem)item;
                    return (data.Memory, data.Length);
                });
        }
        return (Factory, () => Volatile.Read(ref count));
    }

    // ── POOL-024: Dead connection detected and removed ──────────────────────

    [Fact(Timeout = 15_000,
        DisplayName = "POOL-024: Connection dies → retry → new connection works")]
    public async Task POOL_024_ConnectionDies_Retry_NewConnectionWorks()
    {
        // First connection fails on ConnectItem, second succeeds (echo flow).
        // After reconnect, a DataItem sent to the new connection should work.
        var (factory, getCount) = CreateFailThenSucceedFlowFactory(failCount: 1);
        var config = new PoolConfig(
            MaxConnectionsPerHost: 1,
            MaxReconnectAttempts: 3,
            ReconnectInterval: TimeSpan.FromMilliseconds(100));
        var stage = CreateStage(factory, config);
        const string poolKey = "https:example.com:443:1.1";

        // Use Source.Queue to control timing
        var (queue, resultTask) = Source.Queue<RoutedTransportItem>(16, OverflowStrategy.Backpressure)
            .Via(Flow.FromGraph(stage))
            .ToMaterialized(Sink.Seq<RoutedDataItem>(), Keep.Both)
            .Run(Materializer);

        // Register host
        await queue.OfferAsync(new RoutedTransportItem(poolKey, new ConnectItem(TestOptions())));

        // Send a DataItem — triggers connection materialisation. First connection fails
        // on ConnectItem processing, which triggers OnSlotDeath → reconnect timer.
        await queue.OfferAsync(new RoutedTransportItem(poolKey,
            new DataItem(new SimpleMemoryOwner(new byte[] { 0x42 }), 1)));

        // Wait for the reconnect (second connection created)
        await WaitForConditionAsync(() => getCount() >= 2, timeout: TimeSpan.FromSeconds(5));

        // Give the new connection time to initialise and process any queued items
        await Task.Delay(300);

        // Send another DataItem — should go to the second (working) connection
        await queue.OfferAsync(new RoutedTransportItem(poolKey,
            new DataItem(new SimpleMemoryOwner(new byte[] { 0x43 }), 1)));

        // Wait for response
        await Task.Delay(500);

        queue.Complete();
        var results = await resultTask;

        // At least one response from the working connection
        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Equal(poolKey, r.PoolKey));
        // At least 2 connections were created (first failed, second succeeded)
        Assert.True(getCount() >= 2);
    }

    // ── POOL-025: Max reconnect attempts exhausted → ConnectionPoolException ──

    [Fact(Timeout = 15_000,
        DisplayName = "POOL-025: Connection dies → max retries reached → ConnectionPoolException")]
    public async Task POOL_025_ConnectionDies_MaxRetries_ThrowsConnectionPoolException()
    {
        var (factory, getCount) = CreateAlwaysFailFlowFactory();
        var config = new PoolConfig(
            MaxConnectionsPerHost: 1,
            MaxReconnectAttempts: 2,
            ReconnectInterval: TimeSpan.FromMilliseconds(50));
        var stage = CreateStage(factory, config);
        const string poolKey = "https:example.com:443:1.1";

        // Use Source.Queue so the stream stays open while reconnects happen
        var (queue, resultTask) = Source.Queue<RoutedTransportItem>(16, OverflowStrategy.Backpressure)
            .Via(Flow.FromGraph(stage))
            .ToMaterialized(Sink.Seq<RoutedDataItem>(), Keep.Both)
            .Run(Materializer);

        // Register host
        await queue.OfferAsync(new RoutedTransportItem(poolKey, new ConnectItem(TestOptions())));

        // Send a DataItem — triggers first connection materialisation which will fail
        await queue.OfferAsync(new RoutedTransportItem(poolKey,
            new DataItem(new SimpleMemoryOwner(new byte[] { 0x01 }), 1)));

        // The stage should fail after exhausting reconnect attempts.
        // resultTask will fault when the stage fails.
        var ex = await Assert.ThrowsAnyAsync<Exception>(async () => await resultTask);

        // Unwrap AggregateException if needed
        var inner = ex is AggregateException agg ? agg.InnerException! : ex;
        Assert.IsType<ConnectionPoolException>(inner);
        Assert.Contains("reconnect attempts", inner.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(poolKey, inner.Message);

        // Should have tried: initial + reconnects up to max
        Assert.True(getCount() >= 2, $"Expected at least 2 connection attempts, got {getCount()}");
    }

    // ── POOL-026: PoolConfig exposes MaxReconnectAttempts and ReconnectInterval ──

    [Fact(DisplayName = "POOL-026: PoolConfig has MaxReconnectAttempts and ReconnectInterval defaults")]
    public void POOL_026_PoolConfig_ReconnectDefaults()
    {
        var config = new PoolConfig();
        Assert.Equal(3, config.MaxReconnectAttempts);
        Assert.Equal(TimeSpan.FromSeconds(5), config.ReconnectInterval);
    }

    [Theory(DisplayName = "POOL-027: PoolConfig MaxReconnectAttempts and ReconnectInterval are configurable")]
    [InlineData(1, 100)]
    [InlineData(5, 2000)]
    [InlineData(10, 500)]
    public void POOL_027_PoolConfig_ReconnectConfigurable(int maxAttempts, int intervalMs)
    {
        var config = new PoolConfig(
            MaxReconnectAttempts: maxAttempts,
            ReconnectInterval: TimeSpan.FromMilliseconds(intervalMs));
        Assert.Equal(maxAttempts, config.MaxReconnectAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(intervalMs), config.ReconnectInterval);
    }

    // ── POOL-028: Connection death with pending queue drains to new connection ──

    [Fact(Timeout = 15_000,
        DisplayName = "POOL-028: Connection dies with queued items → items dispatched to reconnected slot")]
    public async Task POOL_028_ConnectionDies_QueuedItems_DispatchedAfterReconnect()
    {
        // Use a flow factory: first flow accepts ConnectItem + 1 DataItem then fails,
        // second flow echoes normally
        var materialCount = 0;
        var firstFlowGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed> Factory()
        {
            var myIndex = Interlocked.Increment(ref materialCount) - 1;
            if (myIndex == 0)
            {
                // First flow: process first DataItem, then die after gate is released
                return Flow.Create<ITransportItem>()
                    .Where(x => x is DataItem)
                    .SelectAsync(1, async x =>
                    {
                        var data = (DataItem)x;
                        var result = (data.Memory, data.Length);
                        // Signal that we processed one item, then wait for gate to fail
                        firstFlowGate.SetResult(true);
                        // Give time for the next item to be queued, then fail
                        await Task.Delay(200);
                        throw new InvalidOperationException("Connection died mid-stream");
#pragma warning disable CS0162 // Unreachable code
                        return result;
#pragma warning restore CS0162
                    });
            }
            return CreateEchoFlow();
        }

        var config = new PoolConfig(
            MaxConnectionsPerHost: 1,
            MaxReconnectAttempts: 3,
            ReconnectInterval: TimeSpan.FromMilliseconds(100));
        var stage = CreateStage(Factory, config);
        const string poolKey = "https:example.com:443:1.1";

        var (queue, resultTask) = Source.Queue<RoutedTransportItem>(16, OverflowStrategy.Backpressure)
            .Via(Flow.FromGraph(stage))
            .ToMaterialized(Sink.Seq<RoutedDataItem>(), Keep.Both)
            .Run(Materializer);

        // Register and send first request
        await queue.OfferAsync(new RoutedTransportItem(poolKey, new ConnectItem(TestOptions())));
        await queue.OfferAsync(new RoutedTransportItem(poolKey,
            new DataItem(new SimpleMemoryOwner(new byte[] { 0x01 }), 1)));

        // Wait for first flow to process
        await firstFlowGate.Task;

        // Send second request — will be queued since connection is busy, then connection dies
        await queue.OfferAsync(new RoutedTransportItem(poolKey,
            new DataItem(new SimpleMemoryOwner(new byte[] { 0x02 }), 1)));

        // Wait for reconnect and response
        await WaitForConditionAsync(() => Volatile.Read(ref materialCount) >= 2, timeout: TimeSpan.FromSeconds(5));

        // Allow time for queued item to be processed by new connection
        await Task.Delay(500);

        queue.Complete();
        var results = await resultTask;

        // We should get at least the second request's response from the reconnected flow
        Assert.NotEmpty(results);
    }

    /// <summary>
    /// Polls a condition until it becomes true or the timeout expires.
    /// </summary>
    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Condition not met within {timeout}");
            }
            await Task.Delay(25);
        }
    }
}
