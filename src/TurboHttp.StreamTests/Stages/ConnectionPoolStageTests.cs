using System.Buffers;
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
    /// Creates a ConnectionPoolStage with an echo flow factory.
    /// The echo flow passes DataItems through and filters ConnectItems.
    /// </summary>
    private static ConnectionPoolStage CreateStage()
    {
        return new ConnectionPoolStage(CreateEchoFlow, DefaultConfig);
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
}
