using System.Buffers;
using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using TurboHttp.IO;
using TurboHttp.IO.Stages;
using Xunit;

namespace TurboHttp.StreamTests.IO;

/// <summary>
/// Unit tests for <see cref="ConnectionState"/> — handle association, version-aware
/// MaxConcurrentStreams, and HasAvailableSlot logic.
/// </summary>
public sealed class ConnectionStateTests : TestKit
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private ConnectionHandle CreateHandle(Version version)
    {
        var outbound = Channel.CreateUnbounded<(IMemoryOwner<byte> Buffer, int ReadableBytes)>();
        var inbound = Channel.CreateUnbounded<(IMemoryOwner<byte> Buffer, int ReadableBytes)>();
        var key = new HostKey
        {
            Host = "localhost",
            Port = 443,
            Scheme = "https",
            Version = version
        };

        return new ConnectionHandle(outbound.Writer, inbound.Reader, key, ActorRefs.Nobody);
    }

    // ── Handle property ─────────────────────────────────────────────────────

    [Fact(DisplayName = "TASK-9-003-001: Handle is null by default")]
    public void Handle_IsNull_ByDefault()
    {
        var state = new ConnectionState(ActorRefs.Nobody);

        Assert.Null(state.Handle);
    }

    [Fact(DisplayName = "TASK-9-003-002: SetHandle stores handle and updates LastActivity")]
    public void SetHandle_StoresHandle_UpdatesLastActivity()
    {
        var state = new ConnectionState(ActorRefs.Nobody);
        var before = state.LastActivity;
        var handle = CreateHandle(HttpVersion.Version20);

        state.SetHandle(handle);

        Assert.Same(handle, state.Handle);
        Assert.True(state.LastActivity >= before);
    }

    // ── HttpVersion (computed) ──────────────────────────────────────────────

    [Fact(DisplayName = "TASK-9-003-003: HttpVersion defaults to 1.1 when no handle")]
    public void HttpVersion_DefaultsTo11_WhenNoHandle()
    {
        var state = new ConnectionState(ActorRefs.Nobody);

        Assert.Equal(HttpVersion.Version11, state.HttpVersion);
    }

    [Theory(DisplayName = "TASK-9-003-004: HttpVersion reflects handle's version")]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 0)]
    public void HttpVersion_ReflectsHandle(int major, int minor)
    {
        var version = new Version(major, minor);
        var state = new ConnectionState(ActorRefs.Nobody);
        state.SetHandle(CreateHandle(version));

        Assert.Equal(version, state.HttpVersion);
    }

    // ── MaxConcurrentStreams (version-dependent) ────────────────────────────

    [Fact(DisplayName = "TASK-9-003-005: HTTP/1.0 MaxConcurrentStreams is 1")]
    public void MaxConcurrentStreams_Http10_Is1()
    {
        var state = new ConnectionState(ActorRefs.Nobody);
        state.SetHandle(CreateHandle(HttpVersion.Version10));

        Assert.Equal(1, state.MaxConcurrentStreams);
    }

    [Fact(DisplayName = "TASK-9-003-006: HTTP/1.1 MaxConcurrentStreams is 6")]
    public void MaxConcurrentStreams_Http11_Is6()
    {
        var state = new ConnectionState(ActorRefs.Nobody);
        state.SetHandle(CreateHandle(HttpVersion.Version11));

        Assert.Equal(6, state.MaxConcurrentStreams);
    }

    [Fact(DisplayName = "TASK-9-003-007: HTTP/2 MaxConcurrentStreams reads from handle (default 100)")]
    public void MaxConcurrentStreams_Http20_DefaultIs100()
    {
        var state = new ConnectionState(ActorRefs.Nobody);
        state.SetHandle(CreateHandle(HttpVersion.Version20));

        Assert.Equal(100, state.MaxConcurrentStreams);
    }

    [Fact(DisplayName = "TASK-9-003-008: HTTP/2 MaxConcurrentStreams reflects handle update")]
    public void MaxConcurrentStreams_Http20_ReflectsHandleUpdate()
    {
        var state = new ConnectionState(ActorRefs.Nobody);
        var handle = CreateHandle(HttpVersion.Version20);
        state.SetHandle(handle);

        handle.UpdateMaxConcurrentStreams(50);

        Assert.Equal(50, state.MaxConcurrentStreams);
    }

    [Fact(DisplayName = "TASK-9-003-009: No handle defaults MaxConcurrentStreams to 100 (HTTP/2 fallback)")]
    public void MaxConcurrentStreams_NoHandle_DefaultsVia11Path()
    {
        // No handle → HttpVersion defaults to 1.1 → MaxConcurrentStreams = 6
        var state = new ConnectionState(ActorRefs.Nobody);

        Assert.Equal(6, state.MaxConcurrentStreams);
    }

    // ── HasAvailableSlot ────────────────────────────────────────────────────

    [Fact(DisplayName = "TASK-9-003-010: HasAvailableSlot true for fresh connection")]
    public void HasAvailableSlot_True_ForFreshConnection()
    {
        var state = new ConnectionState(ActorRefs.Nobody);
        state.SetHandle(CreateHandle(HttpVersion.Version11));

        Assert.True(state.HasAvailableSlot);
    }

    [Fact(DisplayName = "TASK-9-003-011: HasAvailableSlot false when dead")]
    public void HasAvailableSlot_False_WhenDead()
    {
        var state = new ConnectionState(ActorRefs.Nobody);
        state.SetHandle(CreateHandle(HttpVersion.Version11));
        state.MarkDead();

        Assert.False(state.HasAvailableSlot);
    }

    [Fact(DisplayName = "TASK-9-003-012: HasAvailableSlot false when not reusable")]
    public void HasAvailableSlot_False_WhenNotReusable()
    {
        var state = new ConnectionState(ActorRefs.Nobody);
        state.SetHandle(CreateHandle(HttpVersion.Version11));
        state.MarkNoReuse();

        Assert.False(state.HasAvailableSlot);
    }

    [Fact(DisplayName = "TASK-9-003-013: HasAvailableSlot false when at capacity (HTTP/1.0)")]
    public void HasAvailableSlot_False_AtCapacity_Http10()
    {
        var state = new ConnectionState(ActorRefs.Nobody);
        state.SetHandle(CreateHandle(HttpVersion.Version10));

        // HTTP/1.0 max = 1, so one pending request fills it
        state.MarkBusy();

        Assert.False(state.HasAvailableSlot);
    }

    [Fact(DisplayName = "TASK-9-003-014: HasAvailableSlot true when under capacity (HTTP/2)")]
    public void HasAvailableSlot_True_UnderCapacity_Http20()
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
    public void HasAvailableSlot_Recovers_AfterMarkIdle()
    {
        var state = new ConnectionState(ActorRefs.Nobody);
        state.SetHandle(CreateHandle(HttpVersion.Version10));

        state.MarkBusy();
        Assert.False(state.HasAvailableSlot);

        state.MarkIdle();
        Assert.True(state.HasAvailableSlot);
    }

    // ── Existing methods still work ─────────────────────────────────────────

    [Fact(DisplayName = "TASK-9-003-016: Existing MarkBusy/MarkIdle/MarkDead/MarkNoReuse preserved")]
    public void ExistingMethods_StillWork()
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
}
