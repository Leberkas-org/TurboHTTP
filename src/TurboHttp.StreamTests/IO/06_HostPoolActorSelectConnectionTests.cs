using System.Buffers;
using System.Net;
using System.Threading.Channels;
using TurboHttp.Internal;
using TurboHttp.Lifecycle;

namespace TurboHttp.StreamTests.IO;

/// <summary>
/// Tests the connection-selection algorithm in <see cref="HostPool"/> for routing requests to the most suitable idle connection.
/// Verifies preference ordering by HTTP version and idle status, with fallback to queuing when all connections are busy.
/// </summary>
/// <remarks>
/// Actor under test: <see cref="HostPool"/>.
/// Validates the selection heuristics for multi-connection host pools.
/// </remarks>
public sealed class HostPoolActorSelectConnectionTests : IOActorTestBase
{
    private ConnectionHandle CreateHandle(Version version)
    {
        var outbound = Channel.CreateUnbounded<(IMemoryOwner<byte> Buffer, int ReadableBytes)>();
        var inbound = Channel.CreateUnbounded<(IMemoryOwner<byte> Buffer, int ReadableBytes)>();
        var key = new RequestEndpoint
        {
            Host = "localhost",
            Port = 443,
            Scheme = "https",
            Version = version
        };
        return new ConnectionHandle(outbound.Writer, inbound.Reader, key, TestActor);
    }

    private ConnectionState CreateActiveConnection(Version version)
    {
        var state = new ConnectionState(TestActor);
        state.SetHandle(CreateHandle(version));
        return state;
    }

    [Fact(DisplayName = "SEL-001: Empty connection list returns null")]
    public void Should_ReturnNull_WhenConnectionListIsEmpty()
    {
        var connections = new List<ConnectionState>();

        var result = HostPool.SelectConnection(connections);

        Assert.Null(result);
    }

    [Fact(DisplayName = "SEL-002: All connections at capacity returns null")]
    public void Should_ReturnNull_WhenAllConnectionsAtCapacity()
    {
        // HTTP/1.0 has MaxConcurrentStreams = 1; mark busy to fill the slot
        var conn = CreateActiveConnection(HttpVersion.Version10);
        conn.MarkBusy();
        var connections = new List<ConnectionState> { conn };

        var result = HostPool.SelectConnection(connections);

        Assert.Null(result);
    }

    [Fact(DisplayName = "SEL-003: Single eligible connection is returned")]
    public void Should_ReturnEligibleConnection_WhenOnlyOneExists()
    {
        var conn = CreateActiveConnection(HttpVersion.Version11);
        var connections = new List<ConnectionState> { conn };

        var result = HostPool.SelectConnection(connections);

        Assert.Same(conn, result);
    }

    [Fact(DisplayName = "SEL-004: Multiple eligible connections returns most recently active")]
    public void Should_ReturnMostRecentlyActiveConnection_WhenMultipleEligibleExist()
    {
        var older = CreateActiveConnection(HttpVersion.Version11);
        Thread.Sleep(15); // ensure distinct LastActivity timestamps
        var newer = CreateActiveConnection(HttpVersion.Version11);
        var connections = new List<ConnectionState> { older, newer };

        var result = HostPool.SelectConnection(connections);

        Assert.Same(newer, result);
    }

    [Fact(DisplayName = "SEL-005: Dead connections are skipped")]
    public void Should_SkipDeadConnections_WhenSelectingConnection()
    {
        var alive = CreateActiveConnection(HttpVersion.Version11);
        Thread.Sleep(15);
        var dead = CreateActiveConnection(HttpVersion.Version11);
        dead.MarkDead(); // Active = false → HasAvailableSlot = false
        var connections = new List<ConnectionState> { alive, dead };

        var result = HostPool.SelectConnection(connections);

        Assert.Same(alive, result);
    }

    [Fact(DisplayName = "SEL-006: Non-reusable connections are skipped")]
    public void Should_SkipNonReusableConnections_WhenSelectingConnection()
    {
        var reusable = CreateActiveConnection(HttpVersion.Version11);
        Thread.Sleep(15);
        var nonReusable = CreateActiveConnection(HttpVersion.Version11);
        nonReusable.MarkNoReuse(); // Reusable = false → HasAvailableSlot = false
        var connections = new List<ConnectionState> { reusable, nonReusable };

        var result = HostPool.SelectConnection(connections);

        Assert.Same(reusable, result);
    }

    [Fact(DisplayName = "SEL-007: Mixed state list returns correct MRU eligible connection")]
    public void Should_ReturnCorrectMruEligible_WhenConnectionListHasMixedStates()
    {
        var dead = CreateActiveConnection(HttpVersion.Version11);
        dead.MarkDead();

        Thread.Sleep(15);
        var full = CreateActiveConnection(HttpVersion.Version10);
        full.MarkBusy(); // HTTP/1.0 max = 1 → at capacity

        Thread.Sleep(15);
        var nonReusable = CreateActiveConnection(HttpVersion.Version11);
        nonReusable.MarkNoReuse();

        Thread.Sleep(15);
        var eligible1 = CreateActiveConnection(HttpVersion.Version11);

        Thread.Sleep(15);
        var eligible2 = CreateActiveConnection(HttpVersion.Version11); // most recent

        var connections = new List<ConnectionState> { dead, full, nonReusable, eligible1, eligible2 };

        var result = HostPool.SelectConnection(connections);

        Assert.Same(eligible2, result);
    }
}
