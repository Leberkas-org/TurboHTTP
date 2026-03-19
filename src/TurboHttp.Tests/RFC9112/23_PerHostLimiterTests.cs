using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.Tests.RFC9112;

public sealed class PerHostLimiterTests
{
    [Fact(DisplayName = "RFC9112-9-PH-001: Default_MaxConnectionsPerHost_Is_6")]
    public void Should_DefaultTo6_When_MaxConnectionsPerHostNotSpecified()
    {
        var limiter = new PerHostConnectionLimiter();
        Assert.Equal(6, limiter.MaxConnectionsPerHost);
    }

    [Fact(DisplayName = "RFC9112-9-PH-002: Custom_MaxConnectionsPerHost_Is_Stored")]
    public void Should_StoreCustomValue_When_MaxConnectionsPerHostIsSet()
    {
        var limiter = new PerHostConnectionLimiter(maxConnectionsPerHost: 10);
        Assert.Equal(10, limiter.MaxConnectionsPerHost);
    }

    [Fact(DisplayName = "RFC9112-9-PH-003: Constructor_Throws_When_MaxConnectionsPerHost_Negative")]
    public void Should_ThrowArgumentOutOfRange_When_MaxConnectionsPerHostIsNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PerHostConnectionLimiter(maxConnectionsPerHost: -1));
    }

    [Fact(DisplayName = "RFC9112-9-PH-004: TryAcquire_Returns_True_For_First_Connection")]
    public void Should_ReturnTrue_When_FirstConnectionAcquired()
    {
        var limiter = new PerHostConnectionLimiter(maxConnectionsPerHost: 3);
        Assert.True(limiter.TryAcquire("example.com"));
    }

    [Fact(DisplayName = "RFC9112-9-PH-005: TryAcquire_Returns_False_When_At_Limit")]
    public void Should_ReturnFalse_When_AtConnectionLimit()
    {
        var limiter = new PerHostConnectionLimiter(maxConnectionsPerHost: 2);
        Assert.True(limiter.TryAcquire("example.com"));
        Assert.True(limiter.TryAcquire("example.com"));
        Assert.False(limiter.TryAcquire("example.com"));
    }

    [Fact(DisplayName = "RFC9112-9-PH-006: TryAcquire_Returns_False_When_Max_Is_Zero")]
    public void Should_ReturnFalse_When_MaxConnectionsIsZero()
    {
        var limiter = new PerHostConnectionLimiter(maxConnectionsPerHost: 0);
        Assert.False(limiter.TryAcquire("example.com"));
    }

    [Fact(DisplayName = "RFC9112-9-PH-007: TryAcquire_Tracks_Different_Hosts_Independently")]
    public void Should_TrackIndependently_When_DifferentHostsAcquire()
    {
        var limiter = new PerHostConnectionLimiter(maxConnectionsPerHost: 1);
        Assert.True(limiter.TryAcquire("host-a.com"));
        // host-a is at limit, but host-b is separate
        Assert.True(limiter.TryAcquire("host-b.com"));
        // both at limit now
        Assert.False(limiter.TryAcquire("host-a.com"));
        Assert.False(limiter.TryAcquire("host-b.com"));
    }

    [Fact(DisplayName = "RFC9112-9-PH-008: TryAcquire_Is_Case_Insensitive_For_Host")]
    public void Should_BeCaseInsensitive_When_HostAcquired()
    {
        var limiter = new PerHostConnectionLimiter(maxConnectionsPerHost: 1);
        Assert.True(limiter.TryAcquire("Example.COM"));
        // Treated as the same host: already at limit
        Assert.False(limiter.TryAcquire("example.com"));
    }

    [Fact(DisplayName = "RFC9112-9-PH-009: Release_Decrements_Active_Count")]
    public void Should_DecrementActiveCount_When_Released()
    {
        var limiter = new PerHostConnectionLimiter(maxConnectionsPerHost: 1);
        limiter.TryAcquire("example.com");
        Assert.Equal(1, limiter.GetActiveConnections("example.com"));
        limiter.Release("example.com");
        Assert.Equal(0, limiter.GetActiveConnections("example.com"));
    }

    [Fact(DisplayName = "RFC9112-9-PH-010: TryAcquire_Succeeds_After_Release")]
    public void Should_SucceedAcquire_When_SlotFreedByRelease()
    {
        var limiter = new PerHostConnectionLimiter(maxConnectionsPerHost: 1);
        limiter.TryAcquire("example.com");
        Assert.False(limiter.TryAcquire("example.com")); // at limit
        limiter.Release("example.com");
        Assert.True(limiter.TryAcquire("example.com")); // slot freed
    }

    [Fact(DisplayName = "RFC9112-9-PH-011: Release_On_Unknown_Host_Does_Not_Throw")]
    public void Should_NotThrow_When_ReleaseCalledForUnknownHost()
    {
        var limiter = new PerHostConnectionLimiter();
        // Should be a no-op, not throw
        limiter.Release("never-seen.com");
        Assert.Equal(0, limiter.GetActiveConnections("never-seen.com"));
    }

    [Fact(DisplayName = "RFC9112-9-PH-012: Release_Does_Not_Go_Negative")]
    public void Should_NotGoNegative_When_ExtraReleasesCalled()
    {
        var limiter = new PerHostConnectionLimiter();
        limiter.TryAcquire("example.com");
        limiter.Release("example.com");
        limiter.Release("example.com"); // extra release
        Assert.Equal(0, limiter.GetActiveConnections("example.com"));
    }

    [Fact(DisplayName = "RFC9112-9-PH-013: GetActiveConnections_Returns_Zero_For_Unknown_Host")]
    public void Should_ReturnZero_When_UnknownHostQueried()
    {
        var limiter = new PerHostConnectionLimiter();
        Assert.Equal(0, limiter.GetActiveConnections("unknown.example.com"));
    }

    [Fact(DisplayName = "RFC9112-9-PH-014: GetActiveConnections_Returns_Correct_Count_After_Multiple_Acquires")]
    public void Should_ReturnCorrectCount_When_MultipleAcquiresCompleted()
    {
        var limiter = new PerHostConnectionLimiter(maxConnectionsPerHost: 5);
        limiter.TryAcquire("example.com");
        limiter.TryAcquire("example.com");
        limiter.TryAcquire("example.com");
        Assert.Equal(3, limiter.GetActiveConnections("example.com"));
    }

    [Fact(DisplayName = "RFC9112-9-PH-015: GetActiveConnections_Is_Case_Insensitive")]
    public void Should_BeCaseInsensitive_When_QueryingActiveConnections()
    {
        var limiter = new PerHostConnectionLimiter(maxConnectionsPerHost: 5);
        limiter.TryAcquire("Example.COM");
        Assert.Equal(1, limiter.GetActiveConnections("example.com"));
        Assert.Equal(1, limiter.GetActiveConnections("EXAMPLE.COM"));
    }

    [Fact(DisplayName = "RFC9112-9-PH-016: Can_Fill_Exactly_To_MaxConnectionsPerHost")]
    public void Should_AllowFill_When_ExactlyAtMaxConnectionsPerHost()
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

    [Fact(DisplayName = "RFC9112-9-PH-017: ConnectionPolicy_Default_MaxConnectionsPerHost_Is_6")]
    public void Should_DefaultTo6_When_ConnectionPolicyMaxConnections()
    {
        var policy = ConnectionPolicy.Default;
        var limiter = new PerHostConnectionLimiter(policy.MaxConnectionsPerHost);
        Assert.Equal(6, limiter.MaxConnectionsPerHost);
    }

    [Fact(DisplayName = "RFC9112-9-PH-018: ConnectionPolicy_AllowHttp2Multiplexing_Is_True_By_Default")]
    public void Should_AllowHttp2Multiplexing_When_DefaultPolicy()
    {
        var policy = ConnectionPolicy.Default;
        Assert.True(policy.AllowHttp2Multiplexing);
    }
}
