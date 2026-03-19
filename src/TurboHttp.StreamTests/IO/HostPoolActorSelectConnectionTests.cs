using System.Buffers;
using System.Net;
using System.Threading.Channels;
using TurboHttp.Internal;
using TurboHttp.IO;
using TurboHttp.IO.Stages;
using TurboHttp.Lifecycle;

namespace TurboHttp.StreamTests.IO;

/// <summary>
/// Unit tests for <see cref="HostPool.SelectConnection"/> — MRU ordering,
/// capacity filtering, and dead/non-reusable connection skipping.
/// </summary>
public sealed class HostPoolActorSelectConnectionTests : IoActorTestBase
{
    // ── Helpers ──────────────────────────────────────────────────────────────

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

    // ── SEL-001: Empty list → null ───────────────────────────────────────────

    [Fact(DisplayName = "SEL-001: Empty connection list returns null")]
    public void SEL_001_EmptyList_ReturnsNull()
    {
        var connections = new List<ConnectionState>();

        var result = HostPool.SelectConnection(connections);

        Assert.Null(result);
    }

    // ── SEL-002: All connections at capacity → null ──────────────────────────

    [Fact(DisplayName = "SEL-002: All connections at capacity returns null")]
    public void SEL_002_AllAtCapacity_ReturnsNull()
    {
        // HTTP/1.0 has MaxConcurrentStreams = 1; mark busy to fill the slot
        var conn = CreateActiveConnection(HttpVersion.Version10);
        conn.MarkBusy();
        var connections = new List<ConnectionState> { conn };

        var result = HostPool.SelectConnection(connections);

        Assert.Null(result);
    }

    // ── SEL-003: Single eligible connection → returns it ─────────────────────

    [Fact(DisplayName = "SEL-003: Single eligible connection is returned")]
    public void SEL_003_SingleEligible_ReturnsIt()
    {
        var conn = CreateActiveConnection(HttpVersion.Version11);
        var connections = new List<ConnectionState> { conn };

        var result = HostPool.SelectConnection(connections);

        Assert.Same(conn, result);
    }

    // ── SEL-004: Multiple eligible → returns most recently active ────────────

    [Fact(DisplayName = "SEL-004: Multiple eligible connections returns most recently active")]
    public void SEL_004_MultipleEligible_ReturnsMRU()
    {
        var older = CreateActiveConnection(HttpVersion.Version11);
        Thread.Sleep(15); // ensure distinct LastActivity timestamps
        var newer = CreateActiveConnection(HttpVersion.Version11);
        var connections = new List<ConnectionState> { older, newer };

        var result = HostPool.SelectConnection(connections);

        Assert.Same(newer, result);
    }

    // ── SEL-005: Dead connections are skipped ────────────────────────────────

    [Fact(DisplayName = "SEL-005: Dead connections are skipped")]
    public void SEL_005_DeadConnection_Skipped()
    {
        var alive = CreateActiveConnection(HttpVersion.Version11);
        Thread.Sleep(15);
        var dead = CreateActiveConnection(HttpVersion.Version11);
        dead.MarkDead(); // Active = false → HasAvailableSlot = false
        var connections = new List<ConnectionState> { alive, dead };

        var result = HostPool.SelectConnection(connections);

        Assert.Same(alive, result);
    }

    // ── SEL-006: Non-reusable connections are skipped ────────────────────────

    [Fact(DisplayName = "SEL-006: Non-reusable connections are skipped")]
    public void SEL_006_NonReusableConnection_Skipped()
    {
        var reusable = CreateActiveConnection(HttpVersion.Version11);
        Thread.Sleep(15);
        var nonReusable = CreateActiveConnection(HttpVersion.Version11);
        nonReusable.MarkNoReuse(); // Reusable = false → HasAvailableSlot = false
        var connections = new List<ConnectionState> { reusable, nonReusable };

        var result = HostPool.SelectConnection(connections);

        Assert.Same(reusable, result);
    }

    // ── SEL-007: Mixed — dead, full, non-reusable, eligible ──────────────────

    [Fact(DisplayName = "SEL-007: Mixed state list returns correct MRU eligible connection")]
    public void SEL_007_MixedStates_ReturnsCorrectMRU()
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
