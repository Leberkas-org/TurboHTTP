using TurboHttp.Protocol;

namespace TurboHttp.Tests.Integration;

/// <summary>
/// Tests for <see cref="PerHostConnectionLimiter"/>.
/// RFC 9112 §9.4 — per-host connection limiting for HTTP/1.x.
/// </summary>
public sealed class PerHostConnectionLimiterTests
{
    // ── Construction ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "CL-001: Default_MaxConnectionsPerHost_Is_6")]
    public void Default_MaxConnectionsPerHost_Is_6()
    {
        var limiter = new PerHostConnectionLimiter();
        Assert.Equal(6, limiter.MaxConnectionsPerHost);
    }

    [Fact(DisplayName = "CL-002: Custom_MaxConnectionsPerHost_Is_Stored")]
    public void Custom_MaxConnectionsPerHost_Is_Stored()
    {
        var limiter = new PerHostConnectionLimiter(maxConnectionsPerHost: 10);
        Assert.Equal(10, limiter.MaxConnectionsPerHost);
    }

    [Fact(DisplayName = "CL-003: Constructor_Throws_When_MaxConnectionsPerHost_Negative")]
    public void Constructor_Throws_When_MaxConnectionsPerHost_Negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PerHostConnectionLimiter(maxConnectionsPerHost: -1));
    }

    // ── TryAcquire ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "CL-004: TryAcquire_Returns_True_For_First_Connection")]
    public void TryAcquire_Returns_True_For_First_Connection()
    {
        var limiter = new PerHostConnectionLimiter(maxConnectionsPerHost: 3);
        Assert.True(limiter.TryAcquire("example.com"));
    }

    [Fact(DisplayName = "CL-005: TryAcquire_Returns_False_When_At_Limit")]
    public void TryAcquire_Returns_False_When_At_Limit()
    {
        var limiter = new PerHostConnectionLimiter(maxConnectionsPerHost: 2);
        Assert.True(limiter.TryAcquire("example.com"));
        Assert.True(limiter.TryAcquire("example.com"));
        Assert.False(limiter.TryAcquire("example.com"));
    }

    [Fact(DisplayName = "CL-006: TryAcquire_Returns_False_When_Max_Is_Zero")]
    public void TryAcquire_Returns_False_When_Max_Is_Zero()
    {
        var limiter = new PerHostConnectionLimiter(maxConnectionsPerHost: 0);
        Assert.False(limiter.TryAcquire("example.com"));
    }

    [Fact(DisplayName = "CL-007: TryAcquire_Tracks_Different_Hosts_Independently")]
    public void TryAcquire_Tracks_Different_Hosts_Independently()
    {
        var limiter = new PerHostConnectionLimiter(maxConnectionsPerHost: 1);
        Assert.True(limiter.TryAcquire("host-a.com"));
        // host-a is at limit, but host-b is separate
        Assert.True(limiter.TryAcquire("host-b.com"));
        // both at limit now
        Assert.False(limiter.TryAcquire("host-a.com"));
        Assert.False(limiter.TryAcquire("host-b.com"));
    }

    [Fact(DisplayName = "CL-008: TryAcquire_Is_Case_Insensitive_For_Host")]
    public void TryAcquire_Is_Case_Insensitive_For_Host()
    {
        var limiter = new PerHostConnectionLimiter(maxConnectionsPerHost: 1);
        Assert.True(limiter.TryAcquire("Example.COM"));
        // Treated as the same host: already at limit
        Assert.False(limiter.TryAcquire("example.com"));
    }

    // ── Release ──────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "CL-009: Release_Decrements_Active_Count")]
    public void Release_Decrements_Active_Count()
    {
        var limiter = new PerHostConnectionLimiter(maxConnectionsPerHost: 1);
        limiter.TryAcquire("example.com");
        Assert.Equal(1, limiter.GetActiveConnections("example.com"));
        limiter.Release("example.com");
        Assert.Equal(0, limiter.GetActiveConnections("example.com"));
    }

    [Fact(DisplayName = "CL-010: TryAcquire_Succeeds_After_Release")]
    public void TryAcquire_Succeeds_After_Release()
    {
        var limiter = new PerHostConnectionLimiter(maxConnectionsPerHost: 1);
        limiter.TryAcquire("example.com");
        Assert.False(limiter.TryAcquire("example.com")); // at limit
        limiter.Release("example.com");
        Assert.True(limiter.TryAcquire("example.com")); // slot freed
    }

    [Fact(DisplayName = "CL-011: Release_On_Unknown_Host_Does_Not_Throw")]
    public void Release_On_Unknown_Host_Does_Not_Throw()
    {
        var limiter = new PerHostConnectionLimiter();
        // Should be a no-op, not throw
        limiter.Release("never-seen.com");
        Assert.Equal(0, limiter.GetActiveConnections("never-seen.com"));
    }

    [Fact(DisplayName = "CL-012: Release_Does_Not_Go_Negative")]
    public void Release_Does_Not_Go_Negative()
    {
        var limiter = new PerHostConnectionLimiter();
        limiter.TryAcquire("example.com");
        limiter.Release("example.com");
        limiter.Release("example.com"); // extra release
        Assert.Equal(0, limiter.GetActiveConnections("example.com"));
    }

    // ── GetActiveConnections ─────────────────────────────────────────────────────

    [Fact(DisplayName = "CL-013: GetActiveConnections_Returns_Zero_For_Unknown_Host")]
    public void GetActiveConnections_Returns_Zero_For_Unknown_Host()
    {
        var limiter = new PerHostConnectionLimiter();
        Assert.Equal(0, limiter.GetActiveConnections("unknown.example.com"));
    }

    [Fact(DisplayName = "CL-014: GetActiveConnections_Returns_Correct_Count_After_Multiple_Acquires")]
    public void GetActiveConnections_Returns_Correct_Count_After_Multiple_Acquires()
    {
        var limiter = new PerHostConnectionLimiter(maxConnectionsPerHost: 5);
        limiter.TryAcquire("example.com");
        limiter.TryAcquire("example.com");
        limiter.TryAcquire("example.com");
        Assert.Equal(3, limiter.GetActiveConnections("example.com"));
    }

    [Fact(DisplayName = "CL-015: GetActiveConnections_Is_Case_Insensitive")]
    public void GetActiveConnections_Is_Case_Insensitive()
    {
        var limiter = new PerHostConnectionLimiter(maxConnectionsPerHost: 5);
        limiter.TryAcquire("Example.COM");
        Assert.Equal(1, limiter.GetActiveConnections("example.com"));
        Assert.Equal(1, limiter.GetActiveConnections("EXAMPLE.COM"));
    }

    // ── Acquire up to limit exactly ──────────────────────────────────────────────

    [Fact(DisplayName = "CL-016: Can_Fill_Exactly_To_MaxConnectionsPerHost")]
    public void Can_Fill_Exactly_To_MaxConnectionsPerHost()
    {
        const int max = 4;
        var limiter = new PerHostConnectionLimiter(maxConnectionsPerHost: max);

        for (var i = 0; i < max; i++)
        {
            Assert.True(limiter.TryAcquire("example.com"), $"Acquire {i + 1} of {max} should succeed");
        }

        // One more should fail
        Assert.False(limiter.TryAcquire("example.com"));
        Assert.Equal(max, limiter.GetActiveConnections("example.com"));
    }

    // ── ConnectionPolicy integration ─────────────────────────────────────────────

    [Fact(DisplayName = "CL-017: ConnectionPolicy_Default_MaxConnectionsPerHost_Is_6")]
    public void ConnectionPolicy_Default_MaxConnectionsPerHost_Is_6()
    {
        var policy = ConnectionPolicy.Default;
        var limiter = new PerHostConnectionLimiter(policy.MaxConnectionsPerHost);
        Assert.Equal(6, limiter.MaxConnectionsPerHost);
    }

    [Fact(DisplayName = "CL-018: ConnectionPolicy_AllowHttp2Multiplexing_Is_True_By_Default")]
    public void ConnectionPolicy_AllowHttp2Multiplexing_Is_True_By_Default()
    {
        var policy = ConnectionPolicy.Default;
        Assert.True(policy.AllowHttp2Multiplexing);
    }
}
