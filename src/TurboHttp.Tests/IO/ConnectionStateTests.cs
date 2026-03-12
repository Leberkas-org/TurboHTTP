using Akka.Actor;
using TurboHttp.IO;

namespace TurboHttp.Tests.IO;

public sealed class ConnectionStateTests
{
    private static ConnectionState CreateState() => new(ActorRefs.Nobody);

    [Fact]
    public void CS_001_DefaultState_Active_Idle_ZeroPending()
    {
        var state = CreateState();

        Assert.True(state.Active);
        Assert.True(state.Idle);
        Assert.Equal(0, state.PendingRequests);
        Assert.Equal(ActorRefs.Nobody, state.Actor);
    }

    [Fact]
    public void CS_002_DefaultLastActivity_IsApproximatelyUtcNow()
    {
        var before = DateTime.UtcNow;
        var state = CreateState();
        var after = DateTime.UtcNow;

        Assert.InRange(state.LastActivity, before, after);
    }

    [Fact]
    public void CS_003_MarkBusy_SetsIdleFalse_IncrementsPending_UpdatesLastActivity()
    {
        var state = CreateState();
        var before = DateTime.UtcNow;

        state.MarkBusy();

        Assert.False(state.Idle);
        Assert.Equal(1, state.PendingRequests);
        Assert.True(state.LastActivity >= before);
    }

    [Fact]
    public void CS_004_MultipleBusy_IncrementsPendingEachTime()
    {
        var state = CreateState();

        state.MarkBusy();
        state.MarkBusy();
        state.MarkBusy();

        Assert.False(state.Idle);
        Assert.Equal(3, state.PendingRequests);
    }

    [Fact]
    public void CS_005_MarkIdle_DecrementsPending_SetsIdleTrueWhenZero()
    {
        var state = CreateState();
        state.MarkBusy();

        var before = DateTime.UtcNow;
        state.MarkIdle();

        Assert.True(state.Idle);
        Assert.Equal(0, state.PendingRequests);
        Assert.True(state.LastActivity >= before);
    }

    [Fact]
    public void CS_006_MarkIdle_DoesNotSetIdleTrueWhenPendingAboveZero()
    {
        var state = CreateState();
        state.MarkBusy();
        state.MarkBusy();

        state.MarkIdle();

        Assert.False(state.Idle);
        Assert.Equal(1, state.PendingRequests);
    }

    [Fact]
    public void CS_007_MarkDead_SetsActiveFalse()
    {
        var state = CreateState();

        state.MarkDead();

        Assert.False(state.Active);
    }

    [Fact]
    public void CS_008_MarkDead_DoesNotAffectIdleOrPending()
    {
        var state = CreateState();
        state.MarkBusy();

        state.MarkDead();

        Assert.False(state.Active);
        Assert.False(state.Idle);
        Assert.Equal(1, state.PendingRequests);
    }

    [Fact]
    public void CS_009_FullLifecycle_Busy_Idle_Busy_Dead()
    {
        var state = CreateState();

        Assert.True(state.Idle);
        Assert.True(state.Active);

        state.MarkBusy();
        Assert.False(state.Idle);
        Assert.Equal(1, state.PendingRequests);

        state.MarkIdle();
        Assert.True(state.Idle);
        Assert.Equal(0, state.PendingRequests);

        state.MarkBusy();
        Assert.False(state.Idle);

        state.MarkDead();
        Assert.False(state.Active);
    }

    [Fact]
    public void CS_010_MarkIdle_UpdatesLastActivity()
    {
        var state = CreateState();
        state.MarkBusy();

        var initialActivity = state.LastActivity;

        state.MarkIdle();

        Assert.True(state.LastActivity >= initialActivity);
    }

    [Fact]
    public void CS_011_Actor_ReturnsConstructorRef()
    {
        var state = CreateState();

        Assert.Same(ActorRefs.Nobody, state.Actor);
    }
}
