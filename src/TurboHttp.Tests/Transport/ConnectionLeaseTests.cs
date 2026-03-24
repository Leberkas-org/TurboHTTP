using System.Buffers;
using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using TurboHttp.Internal;
using TurboHttp.Pooling;
using TurboHttp.Transport;

namespace TurboHttp.StreamTests.Transport;

/// <summary>
/// Tests <see cref="ConnectionLease"/> lifecycle management, state transitions,
/// stream tracking, and disposal behavior.
/// </summary>
public sealed class ConnectionLeaseTests
{
    private static ConnectionHandle CreateHandle(Version version)
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

    #region Constructor and initial state

    [Fact(DisplayName = "TASK-026-002-001: Handle is set from constructor")]
    public void Should_SetHandle_FromConstructor()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        Assert.Same(handle, lease.Handle);
    }

    [Fact(DisplayName = "TASK-026-002-002: Key reflects handle's RequestEndpoint")]
    public void Should_ReflectKey_FromHandle()
    {
        var handle = CreateHandle(HttpVersion.Version20);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        Assert.Equal(handle.Key, lease.Key);
        Assert.Equal("localhost", lease.Key.Host);
        Assert.Equal((ushort)443, lease.Key.Port);
    }

    [Fact(DisplayName = "TASK-026-002-003: IsAlive is true initially")]
    public void Should_BeAlive_WhenCreated()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        Assert.True(lease.IsAlive);
    }

    [Fact(DisplayName = "TASK-026-002-004: Reusable is true initially")]
    public void Should_BeReusable_WhenCreated()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        Assert.True(lease.Reusable);
    }

    [Fact(DisplayName = "TASK-026-002-005: ActiveStreams is zero initially")]
    public void Should_HaveZeroActiveStreams_WhenCreated()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        Assert.Equal(0, lease.ActiveStreams);
    }

    [Fact(DisplayName = "TASK-026-002-006: HasAvailableSlot is true initially")]
    public void Should_HaveAvailableSlot_WhenCreated()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        Assert.True(lease.HasAvailableSlot);
    }

    [Fact(DisplayName = "TASK-026-002-007: Throws on null handle")]
    public void Should_Throw_WhenHandleIsNull()
    {
        using var state = CreateState();
        Assert.Throws<ArgumentNullException>(() => new ConnectionLease(null!, state));
    }

    [Fact(DisplayName = "TASK-026-002-008: Throws on null state")]
    public void Should_Throw_WhenStateIsNull()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        Assert.Throws<ArgumentNullException>(() => new ConnectionLease(handle, null!));
    }

    #endregion

    #region MaxConcurrentStreams defaults

    [Fact(DisplayName = "TASK-026-002-009: HTTP/1.0 MaxConcurrentStreams is 1")]
    public void Should_DefaultMaxConcurrentStreams_To1_ForHttp10()
    {
        var handle = CreateHandle(HttpVersion.Version10);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        Assert.Equal(1, lease.MaxConcurrentStreams);
    }

    [Fact(DisplayName = "TASK-026-002-010: HTTP/1.1 MaxConcurrentStreams is 6")]
    public void Should_DefaultMaxConcurrentStreams_To6_ForHttp11()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        Assert.Equal(6, lease.MaxConcurrentStreams);
    }

    [Fact(DisplayName = "TASK-026-002-011: HTTP/2 MaxConcurrentStreams is 100")]
    public void Should_DefaultMaxConcurrentStreams_To100_ForHttp20()
    {
        var handle = CreateHandle(HttpVersion.Version20);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        Assert.Equal(100, lease.MaxConcurrentStreams);
    }

    [Fact(DisplayName = "TASK-026-002-012: HTTP/3 MaxConcurrentStreams is 100")]
    public void Should_DefaultMaxConcurrentStreams_To100_ForHttp30()
    {
        var handle = CreateHandle(new Version(3, 0));
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        Assert.Equal(100, lease.MaxConcurrentStreams);
    }

    #endregion

    #region MarkBusy / MarkIdle

    [Fact(DisplayName = "TASK-026-002-013: MarkBusy increments ActiveStreams")]
    public void Should_IncrementActiveStreams_WhenMarkBusy()
    {
        var handle = CreateHandle(HttpVersion.Version20);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        lease.MarkBusy();
        Assert.Equal(1, lease.ActiveStreams);

        lease.MarkBusy();
        Assert.Equal(2, lease.ActiveStreams);
    }

    [Fact(DisplayName = "TASK-026-002-014: MarkIdle decrements ActiveStreams")]
    public void Should_DecrementActiveStreams_WhenMarkIdle()
    {
        var handle = CreateHandle(HttpVersion.Version20);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        lease.MarkBusy();
        lease.MarkBusy();
        lease.MarkIdle();

        Assert.Equal(1, lease.ActiveStreams);
    }

    [Fact(DisplayName = "TASK-026-002-015: MarkBusy updates LastActivity")]
    public void Should_UpdateLastActivity_WhenMarkBusy()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);
        var before = lease.LastActivity;

        Thread.Sleep(1);
        lease.MarkBusy();

        Assert.True(lease.LastActivity >= before);
    }

    [Fact(DisplayName = "TASK-026-002-016: MarkIdle updates LastActivity")]
    public void Should_UpdateLastActivity_WhenMarkIdle()
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

    #endregion

    #region MarkNoReuse

    [Fact(DisplayName = "TASK-026-002-017: MarkNoReuse sets Reusable to false")]
    public void Should_SetReusableFalse_WhenMarkNoReuse()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        lease.MarkNoReuse();

        Assert.False(lease.Reusable);
    }

    [Fact(DisplayName = "TASK-026-002-018: HasAvailableSlot false when not reusable")]
    public void Should_HaveNoSlot_WhenNotReusable()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        lease.MarkNoReuse();

        Assert.False(lease.HasAvailableSlot);
    }

    #endregion

    #region UpdateMaxConcurrentStreams

    [Fact(DisplayName = "TASK-026-002-019: UpdateMaxConcurrentStreams updates lease and handle")]
    public void Should_UpdateMaxConcurrentStreams_OnLeaseAndHandle()
    {
        var handle = CreateHandle(HttpVersion.Version20);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        lease.UpdateMaxConcurrentStreams(50);

        Assert.Equal(50, lease.MaxConcurrentStreams);
        Assert.Equal(50, handle.MaxConcurrentStreams);
    }

    #endregion

    #region HasAvailableSlot

    [Fact(DisplayName = "TASK-026-002-020: HasAvailableSlot false when at capacity (HTTP/1.0)")]
    public void Should_HaveNoSlot_WhenAtCapacity_Http10()
    {
        var handle = CreateHandle(HttpVersion.Version10);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        lease.MarkBusy();

        Assert.False(lease.HasAvailableSlot);
    }

    [Fact(DisplayName = "TASK-026-002-021: HasAvailableSlot true when under capacity (HTTP/2)")]
    public void Should_HaveSlot_WhenUnderCapacity_Http20()
    {
        var handle = CreateHandle(HttpVersion.Version20);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        lease.MarkBusy();
        lease.MarkBusy();
        lease.MarkBusy();

        Assert.True(lease.HasAvailableSlot);
    }

    [Fact(DisplayName = "TASK-026-002-022: HasAvailableSlot recovers after MarkIdle")]
    public void Should_RecoverSlot_AfterMarkIdle()
    {
        var handle = CreateHandle(HttpVersion.Version10);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        lease.MarkBusy();
        Assert.False(lease.HasAvailableSlot);

        lease.MarkIdle();
        Assert.True(lease.HasAvailableSlot);
    }

    #endregion

    #region DisposeAsync

    [Fact(Timeout = 5000, DisplayName = "TASK-026-002-023: DisposeAsync sets IsAlive to false")]
    public async Task Should_SetIsAliveFalse_WhenDisposed()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        await lease.DisposeAsync();

        Assert.False(lease.IsAlive);
    }

    [Fact(Timeout = 5000, DisplayName = "TASK-026-002-024: DisposeAsync cancels the CancellationToken")]
    public async Task Should_CancelToken_WhenDisposed()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        var state = CreateState();
        var lease = new ConnectionLease(handle, state);
        var token = lease.Token;

        await lease.DisposeAsync();

        Assert.True(token.IsCancellationRequested);
    }

    [Fact(Timeout = 5000, DisplayName = "TASK-026-002-025: HasAvailableSlot false after disposal")]
    public async Task Should_HaveNoSlot_AfterDisposal()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        await lease.DisposeAsync();

        Assert.False(lease.HasAvailableSlot);
    }

    [Fact(Timeout = 5000, DisplayName = "TASK-026-002-026: Double disposal is safe")]
    public async Task Should_BeSafe_WhenDisposedTwice()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        await lease.DisposeAsync();
        await lease.DisposeAsync(); // Should not throw
    }

    [Fact(Timeout = 5000, DisplayName = "TASK-026-002-027: State stream is disposed after DisposeAsync")]
    public async Task Should_DisposeStream_WhenDisposed()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        var memStream = new MemoryStream();
        var state = new ClientState(
            maxFrameSize: 16384,
            stream: memStream,
            inboundChannel: null,
            outboundChannel: null);
        var lease = new ConnectionLease(handle, state);

        await lease.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => memStream.ReadByte());
    }

    #endregion

    #region Token

    [Fact(DisplayName = "TASK-026-002-028: Token is not cancelled initially")]
    public void Should_NotBeCancelled_WhenCreated()
    {
        var handle = CreateHandle(HttpVersion.Version11);
        using var state = CreateState();
        var lease = new ConnectionLease(handle, state);

        Assert.False(lease.Token.IsCancellationRequested);
    }

    #endregion
}
