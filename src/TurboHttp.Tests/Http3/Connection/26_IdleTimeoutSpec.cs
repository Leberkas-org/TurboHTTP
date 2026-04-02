using TurboHttp.Protocol.Http3;

namespace TurboHttp.Tests.Http3.Connection;

public sealed class IdleTimeoutSpec
{

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void Constructor_SetsTimeout()
    {
        var timeout = TimeSpan.FromSeconds(30);
        var handler = new Http3IdleTimeoutHandler(timeout);

        Assert.Equal(timeout, handler.IdleTimeout);
        Assert.Equal(0, handler.ActiveStreamCount);
        Assert.False(handler.IsTimeoutDisabled);
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void Constructor_ZeroTimeout_DisablesTimeout()
    {
        var handler = new Http3IdleTimeoutHandler(TimeSpan.Zero);

        Assert.True(handler.IsTimeoutDisabled);
        Assert.False(handler.IsIdleTimeoutExpired());
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void Constructor_NegativeTimeout_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Http3IdleTimeoutHandler(TimeSpan.FromSeconds(-1)));
    }


    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void RecordActivity_ResetsTimer()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var handler = new Http3IdleTimeoutHandler(TimeSpan.FromSeconds(10), t0);

        var t1 = t0.AddSeconds(5);
        handler.RecordActivity(t1);

        Assert.Equal(t1, handler.LastActivity);
        Assert.False(handler.IsIdleTimeoutExpired(t1.AddSeconds(6)));
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void OnStreamOpened_IncrementsCountAndResetsTimer()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var handler = new Http3IdleTimeoutHandler(TimeSpan.FromSeconds(10), t0);

        var t1 = t0.AddSeconds(3);
        handler.OnStreamOpened(t1);

        Assert.Equal(1, handler.ActiveStreamCount);
        Assert.Equal(t1, handler.LastActivity);
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void OnStreamClosed_DecrementsCountAndResetsTimer()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var handler = new Http3IdleTimeoutHandler(TimeSpan.FromSeconds(10), t0);

        handler.OnStreamOpened(t0);
        handler.OnStreamOpened(t0);

        var t1 = t0.AddSeconds(5);
        handler.OnStreamClosed(t1);

        Assert.Equal(1, handler.ActiveStreamCount);
        Assert.Equal(t1, handler.LastActivity);
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void OnStreamClosed_NoActiveStreams_Throws()
    {
        var handler = new Http3IdleTimeoutHandler(TimeSpan.FromSeconds(10));

        Assert.Throws<InvalidOperationException>(() => handler.OnStreamClosed());
    }


    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void IsIdleTimeoutExpired_WithinTimeout_ReturnsFalse()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var handler = new Http3IdleTimeoutHandler(TimeSpan.FromSeconds(30), t0);

        Assert.False(handler.IsIdleTimeoutExpired(t0.AddSeconds(29)));
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void IsIdleTimeoutExpired_AtBoundary_ReturnsTrue()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var handler = new Http3IdleTimeoutHandler(TimeSpan.FromSeconds(30), t0);

        Assert.True(handler.IsIdleTimeoutExpired(t0.AddSeconds(30)));
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void IsIdleTimeoutExpired_AfterTimeout_ReturnsTrue()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var handler = new Http3IdleTimeoutHandler(TimeSpan.FromSeconds(10), t0);

        Assert.True(handler.IsIdleTimeoutExpired(t0.AddSeconds(15)));
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void IsIdleTimeoutExpired_Disabled_NeverExpires()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var handler = new Http3IdleTimeoutHandler(TimeSpan.Zero, t0);

        Assert.False(handler.IsIdleTimeoutExpired(t0.AddHours(24)));
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void IsIdleTimeoutExpired_ActivityResetsWindow()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var handler = new Http3IdleTimeoutHandler(TimeSpan.FromSeconds(10), t0);

        // At t+9, not expired
        Assert.False(handler.IsIdleTimeoutExpired(t0.AddSeconds(9)));

        // Record activity at t+9
        handler.RecordActivity(t0.AddSeconds(9));

        // At t+18 (9 seconds after activity), not expired yet
        Assert.False(handler.IsIdleTimeoutExpired(t0.AddSeconds(18)));

        // At t+19 (10 seconds after activity), now expired
        Assert.True(handler.IsIdleTimeoutExpired(t0.AddSeconds(19)));
    }


    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void ComputeEffectiveTimeout_TakesMinimum()
    {
        var result = Http3IdleTimeoutHandler.ComputeEffectiveTimeout(
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(15));

        Assert.Equal(TimeSpan.FromSeconds(15), result);
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void ComputeEffectiveTimeout_LocalZero_UsesRemote()
    {
        var result = Http3IdleTimeoutHandler.ComputeEffectiveTimeout(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(20));

        Assert.Equal(TimeSpan.FromSeconds(20), result);
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void ComputeEffectiveTimeout_RemoteZero_UsesLocal()
    {
        var result = Http3IdleTimeoutHandler.ComputeEffectiveTimeout(
            TimeSpan.FromSeconds(25),
            TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromSeconds(25), result);
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void ComputeEffectiveTimeout_BothZero_ReturnsZero()
    {
        var result = Http3IdleTimeoutHandler.ComputeEffectiveTimeout(
            TimeSpan.Zero, TimeSpan.Zero);

        Assert.Equal(TimeSpan.Zero, result);
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void ComputeEffectiveTimeout_NegativeLocal_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Http3IdleTimeoutHandler.ComputeEffectiveTimeout(
                TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(10)));
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void ComputeEffectiveTimeout_NegativeRemote_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Http3IdleTimeoutHandler.ComputeEffectiveTimeout(
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(-1)));
    }


    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void TimeUntilExpiry_ReturnsRemainingTime()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var handler = new Http3IdleTimeoutHandler(TimeSpan.FromSeconds(30), t0);

        var remaining = handler.TimeUntilExpiry(t0.AddSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(20), remaining);
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void TimeUntilExpiry_Expired_ReturnsZero()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var handler = new Http3IdleTimeoutHandler(TimeSpan.FromSeconds(10), t0);

        var remaining = handler.TimeUntilExpiry(t0.AddSeconds(15));
        Assert.Equal(TimeSpan.Zero, remaining);
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void TimeUntilExpiry_Disabled_ReturnsMaxValue()
    {
        var handler = new Http3IdleTimeoutHandler(TimeSpan.Zero);

        Assert.Equal(TimeSpan.MaxValue, handler.TimeUntilExpiry());
    }


    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void ShouldReconnect_IdleTimeout_ReturnsTrue()
    {
        var policy = new Http3ReconnectionPolicy(maxAttempts: 3);

        var decision = policy.ShouldReconnect(Http3ConnectionLossReason.IdleTimeout);

        Assert.True(decision.ShouldAttempt);
        Assert.Equal(TimeSpan.FromSeconds(1), decision.Delay);
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void ShouldReconnect_GoAway_ReturnsTrue()
    {
        var policy = new Http3ReconnectionPolicy(maxAttempts: 3);

        var decision = policy.ShouldReconnect(Http3ConnectionLossReason.GoAway);

        Assert.True(decision.ShouldAttempt);
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void ShouldReconnect_TransportError_ReturnsTrue()
    {
        var policy = new Http3ReconnectionPolicy(maxAttempts: 3);

        var decision = policy.ShouldReconnect(Http3ConnectionLossReason.TransportError);

        Assert.True(decision.ShouldAttempt);
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void ShouldReconnect_ProtocolError_ReturnsFalse()
    {
        var policy = new Http3ReconnectionPolicy(maxAttempts: 3);

        var decision = policy.ShouldReconnect(Http3ConnectionLossReason.ProtocolError);

        Assert.False(decision.ShouldAttempt);
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void ShouldReconnect_ExponentialBackoff()
    {
        var policy = new Http3ReconnectionPolicy(
            maxAttempts: 5,
            baseDelay: TimeSpan.FromSeconds(1),
            maxDelay: TimeSpan.FromSeconds(30));

        var d1 = policy.ShouldReconnect(Http3ConnectionLossReason.IdleTimeout);
        Assert.Equal(TimeSpan.FromSeconds(1), d1.Delay);   // 2^0 * 1s

        var d2 = policy.ShouldReconnect(Http3ConnectionLossReason.IdleTimeout);
        Assert.Equal(TimeSpan.FromSeconds(2), d2.Delay);   // 2^1 * 1s

        var d3 = policy.ShouldReconnect(Http3ConnectionLossReason.IdleTimeout);
        Assert.Equal(TimeSpan.FromSeconds(4), d3.Delay);   // 2^2 * 1s

        var d4 = policy.ShouldReconnect(Http3ConnectionLossReason.IdleTimeout);
        Assert.Equal(TimeSpan.FromSeconds(8), d4.Delay);   // 2^3 * 1s
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void ShouldReconnect_DelayCapped()
    {
        var policy = new Http3ReconnectionPolicy(
            maxAttempts: 10,
            baseDelay: TimeSpan.FromSeconds(1),
            maxDelay: TimeSpan.FromSeconds(5));

        // 2^0=1, 2^1=2, 2^2=4, 2^3=8→capped to 5
        policy.ShouldReconnect(Http3ConnectionLossReason.IdleTimeout); // 1s
        policy.ShouldReconnect(Http3ConnectionLossReason.IdleTimeout); // 2s
        policy.ShouldReconnect(Http3ConnectionLossReason.IdleTimeout); // 4s

        var d4 = policy.ShouldReconnect(Http3ConnectionLossReason.IdleTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), d4.Delay);
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void ShouldReconnect_MaxAttemptsExhausted_ReturnsFalse()
    {
        var policy = new Http3ReconnectionPolicy(maxAttempts: 2);

        policy.ShouldReconnect(Http3ConnectionLossReason.IdleTimeout);
        policy.ShouldReconnect(Http3ConnectionLossReason.IdleTimeout);

        Assert.True(policy.IsExhausted);
        var decision = policy.ShouldReconnect(Http3ConnectionLossReason.IdleTimeout);
        Assert.False(decision.ShouldAttempt);
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void Reset_ClearsAttemptCounter()
    {
        var policy = new Http3ReconnectionPolicy(maxAttempts: 2);

        policy.ShouldReconnect(Http3ConnectionLossReason.IdleTimeout);
        policy.ShouldReconnect(Http3ConnectionLossReason.IdleTimeout);
        Assert.True(policy.IsExhausted);

        policy.Reset();

        Assert.False(policy.IsExhausted);
        Assert.Equal(0, policy.CurrentAttempt);

        var decision = policy.ShouldReconnect(Http3ConnectionLossReason.IdleTimeout);
        Assert.True(decision.ShouldAttempt);
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void ShouldReconnect_ServerClosed_ReturnsTrue()
    {
        var policy = new Http3ReconnectionPolicy(maxAttempts: 3);

        var decision = policy.ShouldReconnect(Http3ConnectionLossReason.ServerClosed);

        Assert.True(decision.ShouldAttempt);
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void Constructor_ZeroMaxAttempts_AlwaysExhausted()
    {
        var policy = new Http3ReconnectionPolicy(maxAttempts: 0);

        Assert.True(policy.IsExhausted);
        var decision = policy.ShouldReconnect(Http3ConnectionLossReason.IdleTimeout);
        Assert.False(decision.ShouldAttempt);
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void Constructor_NegativeMaxAttempts_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Http3ReconnectionPolicy(maxAttempts: -1));
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void Constructor_Defaults()
    {
        var policy = new Http3ReconnectionPolicy();

        Assert.Equal(5, policy.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(1), policy.BaseDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), policy.MaxDelay);
        Assert.Equal(0, policy.CurrentAttempt);
        Assert.False(policy.IsExhausted);
    }
}
