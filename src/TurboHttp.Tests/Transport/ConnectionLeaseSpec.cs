using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using TurboHttp.Internal;
using TurboHttp.Transport.Connection;

namespace TurboHttp.Tests.Transport;

/// <summary>
/// Tests <see cref="ConnectionLease"/> lifecycle management, state transitions,
/// stream tracking, and disposal behavior.
/// </summary>
public sealed class ConnectionLeaseSpec
{
    private static ConnectionHandle CreateHandle(Version version)
    {
        var outbound = Channel.CreateUnbounded<NetworkBuffer>();
        var inbound = Channel.CreateUnbounded<NetworkBuffer>();
        var key = new RequestEndpoint
        {
            Host = "localhost",
            Port = 443,
            Scheme = "https",
            Version = version
        };

        return new ConnectionHandle(outbound.Writer, inbound.Reader, key, ActorRefs.Nobody);
    }

    private static ClientState CreateState()
    {
        return new ClientState(
            maxFrameSize: 16384,
            stream: new MemoryStream(),
            inboundChannel: null,
            outboundChannel: null);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLease_should_set_handle_from_constructor()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        Assert.Same(handle, lease.Handle);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLease_should_reflect_key_from_handle()
    {
        var handle = CreateHandle(HttpVersion.Version20);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        Assert.Equal(handle.Key, lease.Key);
        Assert.Equal("localhost", lease.Key.Host);
        Assert.Equal((ushort)443, lease.Key.Port);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLease_should_be_alive_when_created()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        Assert.True(lease.IsAlive);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLease_should_be_reusable_when_created()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        Assert.True(lease.Reusable);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLease_should_have_zero_active_streams_when_created()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        Assert.Equal(0, lease.ActiveStreams);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLease_should_have_available_slot_when_created()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        Assert.True(lease.HasAvailableSlot);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLease_should_throw_on_null_handle()
    {
        using var state = CreateState();
        Assert.Throws<ArgumentNullException>(() => new ConnectionLease(null!, state));
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLease_should_throw_on_null_state()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        Assert.Throws<ArgumentNullException>(() => new ConnectionLease(handle, null!));
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLease_should_default_max_concurrent_streams_to_1_for_http10()
    {
        var handle = CreateHandle(HttpVersion.Version10);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        Assert.Equal(1, lease.MaxConcurrentStreams);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLease_should_default_max_concurrent_streams_to_6_for_http11()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        Assert.Equal(6, lease.MaxConcurrentStreams);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLease_should_default_max_concurrent_streams_to_100_for_http20()
    {
        var handle = CreateHandle(HttpVersion.Version20);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        Assert.Equal(100, lease.MaxConcurrentStreams);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLease_should_default_max_concurrent_streams_to_100_for_http30()
    {
        var handle = CreateHandle(new Version(3, 0));
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        Assert.Equal(100, lease.MaxConcurrentStreams);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLease_should_increment_active_streams_when_mark_busy()
    {
        var handle = CreateHandle(HttpVersion.Version20);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        lease.MarkBusy();
        Assert.Equal(1, lease.ActiveStreams);

        lease.MarkBusy();
        Assert.Equal(2, lease.ActiveStreams);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLease_should_decrement_active_streams_when_mark_idle()
    {
        var handle = CreateHandle(HttpVersion.Version20);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        lease.MarkBusy();
        lease.MarkBusy();
        lease.MarkIdle();

        Assert.Equal(1, lease.ActiveStreams);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLease_should_update_last_activity_when_mark_busy()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);
        var before = lease.LastActivity;

        Thread.Sleep(1);
        lease.MarkBusy();

        Assert.True(lease.LastActivity >= before);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLease_should_update_last_activity_when_mark_idle()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        lease.MarkBusy();
        var before = lease.LastActivity;

        Thread.Sleep(1);
        lease.MarkIdle();

        Assert.True(lease.LastActivity >= before);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLease_should_set_reusable_false_when_mark_no_reuse()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        lease.MarkNoReuse();

        Assert.False(lease.Reusable);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLease_should_have_no_slot_when_not_reusable()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        lease.MarkNoReuse();

        Assert.False(lease.HasAvailableSlot);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLease_should_update_max_concurrent_streams_on_lease_and_handle()
    {
        var handle = CreateHandle(HttpVersion.Version20);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        lease.UpdateMaxConcurrentStreams(50);

        Assert.Equal(50, lease.MaxConcurrentStreams);
        Assert.Equal(50, handle.MaxConcurrentStreams);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLease_should_have_no_slot_when_at_capacity_http10()
    {
        var handle = CreateHandle(HttpVersion.Version10);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        lease.MarkBusy();

        Assert.False(lease.HasAvailableSlot);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLease_should_have_slot_when_under_capacity_http20()
    {
        var handle = CreateHandle(HttpVersion.Version20);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        lease.MarkBusy();
        lease.MarkBusy();
        lease.MarkBusy();

        Assert.True(lease.HasAvailableSlot);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLease_should_recover_slot_after_mark_idle()
    {
        var handle = CreateHandle(HttpVersion.Version10);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        lease.MarkBusy();
        Assert.False(lease.HasAvailableSlot);

        lease.MarkIdle();
        Assert.True(lease.HasAvailableSlot);
    }

    [Fact(Timeout = 5000)]
    public async Task ConnectionLease_should_set_is_alive_false_when_disposed()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        lease.Dispose();

        Assert.False(lease.IsAlive);
    }

    [Fact(Timeout = 5000)]
    public async Task ConnectionLease_should_cancel_token_when_disposed()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        var state = CreateState();
        var lease = new ConnectionLease(handle, state);
        var token = lease.Token;

        lease.Dispose();

        Assert.True(token.IsCancellationRequested);
    }

    [Fact(Timeout = 5000)]
    public async Task ConnectionLease_should_have_no_slot_after_disposal()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        lease.Dispose();

        Assert.False(lease.HasAvailableSlot);
    }

    [Fact(Timeout = 5000)]
    public async Task ConnectionLease_should_be_safe_when_disposed_twice()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        lease.Dispose();
        lease.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task ConnectionLease_should_dispose_stream_when_disposed()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        var memStream = new MemoryStream();
        var state = new ClientState(
            maxFrameSize: 16384,
            stream: memStream,
            inboundChannel: null,
            outboundChannel: null);
        var lease = new ConnectionLease(handle, state);

        lease.Dispose();

        Assert.Throws<ObjectDisposedException>(() => memStream.ReadByte());
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLease_should_not_be_cancelled_when_created()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        Assert.False(lease.Token.IsCancellationRequested);
    }
}
