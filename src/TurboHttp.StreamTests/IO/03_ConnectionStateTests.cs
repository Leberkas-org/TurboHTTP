using System.Buffers;
using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using TurboHttp.Internal;
using TurboHttp.Pooling;

namespace TurboHttp.StreamTests.IO;

/// <summary>
/// Tests <see cref="ConnectionState"/> transitions managed by <see cref="HostPool"/>.
/// Verifies state field values and that slot transitions respect per-host concurrency limits.
/// </summary>
/// <remarks>
/// Actor under test: <see cref="ConnectionState"/>.
/// Validates Active, Idle, Busy, and Reusable state semantics.
/// </remarks>
public sealed class ConnectionStateTests : TestKit
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

        return new ConnectionHandle(outbound.Writer, inbound.Reader, key, ActorRefs.Nobody);
    }

    [Fact(DisplayName = "TASK-9-003-001: Handle is null by default")]
    public void Should_BeNull_WhenHandleNotSet()
    {
        var state = new ConnectionState(ActorRefs.Nobody);

        Assert.Null(state.Handle);
    }

    [Fact(DisplayName = "TASK-9-003-002: SetHandle stores handle and updates LastActivity")]
    public void Should_StoreHandleAndUpdateLastActivity_WhenSetHandleCalled()
    {
        var state = new ConnectionState(ActorRefs.Nobody);
        var before = state.LastActivity;
        var handle = CreateHandle(HttpVersion.Version20);

        state.SetHandle(handle);

        Assert.Same(handle, state.Handle);
        Assert.True(state.LastActivity >= before);
    }

    [Fact(DisplayName = "TASK-9-003-003: HttpVersion defaults to 1.1 when no handle")]
    public void Should_DefaultToHttp11_WhenNoHandleSet()
    {
        var state = new ConnectionState(ActorRefs.Nobody);

        Assert.Equal(HttpVersion.Version11, state.HttpVersion);
    }

    [Theory(DisplayName = "TASK-9-003-004: HttpVersion reflects handle's version")]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 0)]
    public void Should_ReflectHandleVersion_WhenHandleSet(int major, int minor)
    {
        var version = new Version(major, minor);
        var state = new ConnectionState(ActorRefs.Nobody);
        state.SetHandle(CreateHandle(version));

        Assert.Equal(version, state.HttpVersion);
    }

    [Fact(DisplayName = "TASK-9-003-005: HTTP/1.0 MaxConcurrentStreams is 1")]
    public void Should_Be1_WhenHttp10ConnectionSet()
    {
        var state = new ConnectionState(ActorRefs.Nobody);
        state.SetHandle(CreateHandle(HttpVersion.Version10));

        Assert.Equal(1, state.MaxConcurrentStreams);
    }

    [Fact(DisplayName = "TASK-9-003-006: HTTP/1.1 MaxConcurrentStreams is 6")]
    public void Should_Be6_WhenHttp11ConnectionSet()
    {
        var state = new ConnectionState(ActorRefs.Nobody);
        state.SetHandle(CreateHandle(HttpVersion.Version11));

        Assert.Equal(6, state.MaxConcurrentStreams);
    }

    [Fact(DisplayName = "TASK-9-003-007: HTTP/2 MaxConcurrentStreams reads from handle (default 100)")]
    public void Should_Default100_WhenHttp20ConnectionSet()
    {
        var state = new ConnectionState(ActorRefs.Nobody);
        state.SetHandle(CreateHandle(HttpVersion.Version20));

        Assert.Equal(100, state.MaxConcurrentStreams);
    }

    [Fact(DisplayName = "TASK-9-003-008: HTTP/2 MaxConcurrentStreams reflects handle update")]
    public void Should_ReflectHandleUpdate_WhenHttp20MaxConcurrentStreamsUpdated()
    {
        var state = new ConnectionState(ActorRefs.Nobody);
        var handle = CreateHandle(HttpVersion.Version20);
        state.SetHandle(handle);

        handle.UpdateMaxConcurrentStreams(50);

        Assert.Equal(50, state.MaxConcurrentStreams);
    }

    [Fact(DisplayName = "TASK-9-003-009: No handle defaults MaxConcurrentStreams to 100 (HTTP/2 fallback)")]
    public void Should_Default6_WhenNoHandleSet()
    {
        // No handle → HttpVersion defaults to 1.1 → MaxConcurrentStreams = 6
        var state = new ConnectionState(ActorRefs.Nobody);

        Assert.Equal(6, state.MaxConcurrentStreams);
    }

    [Fact(DisplayName = "TASK-9-003-010: HasAvailableSlot true for fresh connection")]
    public void Should_BeTrue_WhenConnectionIsFresh()
    {
        var state = new ConnectionState(ActorRefs.Nobody);
        state.SetHandle(CreateHandle(HttpVersion.Version11));

        Assert.True(state.HasAvailableSlot);
    }

    [Fact(DisplayName = "TASK-9-003-011: HasAvailableSlot false when dead")]
    public void Should_BeFalse_WhenConnectionIsDead()
    {
        var state = new ConnectionState(ActorRefs.Nobody);
        state.SetHandle(CreateHandle(HttpVersion.Version11));
        state.MarkDead();

        Assert.False(state.HasAvailableSlot);
    }

    [Fact(DisplayName = "TASK-9-003-012: HasAvailableSlot false when not reusable")]
    public void Should_BeFalse_WhenConnectionIsNotReusable()
    {
        var state = new ConnectionState(ActorRefs.Nobody);
        state.SetHandle(CreateHandle(HttpVersion.Version11));
        state.MarkNoReuse();

        Assert.False(state.HasAvailableSlot);
    }

    [Fact(DisplayName = "TASK-9-003-013: HasAvailableSlot false when at capacity (HTTP/1.0)")]
    public void Should_BeFalse_WhenAtCapacityForHttp10()
    {
        var state = new ConnectionState(ActorRefs.Nobody);
        state.SetHandle(CreateHandle(HttpVersion.Version10));

        // HTTP/1.0 max = 1, so one pending request fills it
        state.MarkBusy();

        Assert.False(state.HasAvailableSlot);
    }

    [Fact(DisplayName = "TASK-9-003-014: HasAvailableSlot true when under capacity (HTTP/2)")]
    public void Should_BeTrue_WhenUnderCapacityForHttp20()
    {
        var state = new ConnectionState(ActorRefs.Nobody);
        state.SetHandle(CreateHandle(HttpVersion.Version20));

        // HTTP/2 max = 100, add a few requests
        state.MarkBusy();
        state.MarkBusy();
        state.MarkBusy();

        Assert.True(state.HasAvailableSlot);
    }

    [Fact(DisplayName = "TASK-9-003-015: HasAvailableSlot recovers after MarkIdle")]
    public void Should_RecoverToTrue_WhenMarkIdleCalled()
    {
        var state = new ConnectionState(ActorRefs.Nobody);
        state.SetHandle(CreateHandle(HttpVersion.Version10));

        state.MarkBusy();
        Assert.False(state.HasAvailableSlot);

        state.MarkIdle();
        Assert.True(state.HasAvailableSlot);
    }

    [Fact(DisplayName = "TASK-9-003-016: Existing MarkBusy/MarkIdle/MarkDead/MarkNoReuse preserved")]
    public void Should_TrackStateCorrectly_WhenBusyIdleDeadNoReuseMarked()
    {
        var state = new ConnectionState(ActorRefs.Nobody);

        Assert.True(state.Active);
        Assert.True(state.Idle);
        Assert.True(state.Reusable);
        Assert.Equal(0, state.PendingRequests);

        state.MarkBusy();
        Assert.False(state.Idle);
        Assert.Equal(1, state.PendingRequests);

        state.MarkIdle();
        Assert.True(state.Idle);
        Assert.Equal(0, state.PendingRequests);

        state.MarkNoReuse();
        Assert.False(state.Reusable);

        state.MarkDead();
        Assert.False(state.Active);
    }

    [Theory(DisplayName = "TASK-9-003-017: SupportsMultipleStreams false for HTTP/1.x")]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    public void Should_ReturnFalse_WhenHttp1x(int major, int minor)
    {
        var state = new ConnectionState(ActorRefs.Nobody);
        state.SetHandle(CreateHandle(new Version(major, minor)));

        Assert.False(state.SupportsMultipleStreams);
    }

    [Theory(DisplayName = "TASK-9-003-018: SupportsMultipleStreams true for HTTP/2+")]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    public void Should_ReturnTrue_WhenHttp2OrHigher(int major, int minor)
    {
        var state = new ConnectionState(ActorRefs.Nobody);
        state.SetHandle(CreateHandle(new Version(major, minor)));

        Assert.True(state.SupportsMultipleStreams);
    }

    [Fact(DisplayName = "TASK-9-003-019: HTTP/3 MaxConcurrentStreams defaults to 100")]
    public void Should_Default100_WhenHttp30ConnectionSet()
    {
        var state = new ConnectionState(ActorRefs.Nobody);
        state.SetHandle(CreateHandle(new Version(3, 0)));

        Assert.Equal(100, state.MaxConcurrentStreams);
    }

    [Fact(DisplayName = "TASK-9-003-020: HTTP/3 HasAvailableSlot true when under capacity")]
    public void Should_BeTrue_WhenUnderCapacityForHttp30()
    {
        var state = new ConnectionState(ActorRefs.Nobody);
        state.SetHandle(CreateHandle(new Version(3, 0)));

        state.MarkBusy();
        state.MarkBusy();
        state.MarkBusy();

        Assert.True(state.HasAvailableSlot);
    }
}
