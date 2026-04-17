using System.Net;
using TurboHTTP.Internal;
using TurboHTTP.Tests.Shared;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Tests.Transport;

public sealed class QuicConnectionLeaseSpec
{
    private static readonly RequestEndpoint TestEndpoint = new()
    {
        Scheme = "https",
        Host = "localhost",
        Port = 443,
        Version = HttpVersion.Version30
    };

    private static readonly QuicOptions TestOptions = new() { Host = "localhost", Port = 443 };

    private static QuicConnectionLease CreateLease(FakeClientProvider? provider = null)
    {
        var p = provider ?? new FakeClientProvider();
        var handle = new QuicConnectionHandle(p, TestOptions, TestEndpoint);
        return new QuicConnectionLease(handle);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void New_lease_should_be_alive_and_reusable()
    {
        using var lease = CreateLease();

        Assert.True(lease.IsAlive);
        Assert.True(lease.Reusable);
        Assert.Equal(0, lease.ActiveStreams);
        Assert.True(lease.CanAcceptStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void MarkBusy_should_increment_active_streams()
    {
        using var lease = CreateLease();

        lease.MarkBusy();

        Assert.Equal(1, lease.ActiveStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void MarkIdle_should_decrement_active_streams()
    {
        using var lease = CreateLease();

        lease.MarkBusy();
        lease.MarkIdle();

        Assert.Equal(0, lease.ActiveStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void CanAcceptStream_should_respect_max_concurrent_streams()
    {
        using var lease = CreateLease();
        lease.MaxConcurrentStreams = 2;

        lease.MarkBusy();
        Assert.True(lease.CanAcceptStream);

        lease.MarkBusy();
        Assert.False(lease.CanAcceptStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void MarkNoReuse_should_prevent_further_reuse()
    {
        using var lease = CreateLease();

        lease.MarkNoReuse();

        Assert.False(lease.Reusable);
        Assert.False(lease.CanAcceptStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void IsExpired_should_return_false_within_lifetime()
    {
        using var lease = CreateLease();

        Assert.False(lease.IsExpired(TimeSpan.FromHours(1)));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void IsExpired_should_return_false_for_infinite_lifetime()
    {
        using var lease = CreateLease();

        Assert.False(lease.IsExpired(Timeout.InfiniteTimeSpan));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void Dispose_should_mark_not_alive()
    {
        var lease = CreateLease();

        lease.Dispose();

        Assert.False(lease.IsAlive);
        Assert.False(lease.CanAcceptStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void Dispose_should_be_idempotent()
    {
        var lease = CreateLease();

        lease.Dispose();
        lease.Dispose();

        Assert.False(lease.IsAlive);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void LastActivity_should_update_on_mark_busy()
    {
        using var lease = CreateLease();
        var initial = lease.LastActivity;

        Thread.Sleep(20);
        lease.MarkBusy();

        Assert.True(lease.LastActivity > initial);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void Key_should_match_handle_endpoint()
    {
        using var lease = CreateLease();

        Assert.Equal(TestEndpoint, lease.Key);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void MaxConcurrentStreams_should_default_to_1()
    {
        using var lease = CreateLease();

        Assert.Equal(1, lease.MaxConcurrentStreams);
    }
}
