using System;
using TurboHttp.Protocol.RFC9114;
using Xunit;

namespace TurboHttp.Tests.RFC9114;

public sealed class IdleTimeoutTests
{
    // ───────────── Construction ─────────────

    [Fact(DisplayName = "RFC9114-5.1-IT-001: Handler initialises with configured timeout")]
    public void Constructor_SetsTimeout()
    {
        var timeout = TimeSpan.FromSeconds(30);
        var handler = new Http3IdleTimeoutHandler(timeout);

        Assert.Equal(timeout, handler.IdleTimeout);
        Assert.Equal(0, handler.ActiveStreamCount);
        Assert.False(handler.IsTimeoutDisabled);
    }

    [Fact(DisplayName = "RFC9114-5.1-IT-002: Zero timeout disables idle timeout")]
    public void Constructor_ZeroTimeout_DisablesTimeout()
    {
        var handler = new Http3IdleTimeoutHandler(TimeSpan.Zero);

        Assert.True(handler.IsTimeoutDisabled);
        Assert.False(handler.IsIdleTimeoutExpired());
    }

    [Fact(DisplayName = "RFC9114-5.1-IT-003: Negative timeout rejected")]
    public void Constructor_NegativeTimeout_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Http3IdleTimeoutHandler(TimeSpan.FromSeconds(-1)));
    }

    // ───────────── Activity tracking ─────────────

    [Fact(DisplayName = "RFC9114-5.1-IT-004: RecordActivity resets idle timer")]
    public void RecordActivity_ResetsTimer()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var handler = new Http3IdleTimeoutHandler(TimeSpan.FromSeconds(10), t0);

        var t1 = t0.AddSeconds(5);
        handler.RecordActivity(t1);

        Assert.Equal(t1, handler.LastActivity);
        Assert.False(handler.IsIdleTimeoutExpired(t1.AddSeconds(6)));
    }

    [Fact(DisplayName = "RFC9114-5.1-IT-005: Stream open resets idle timer and increments count")]
    public void OnStreamOpened_IncrementsCountAndResetsTimer()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var handler = new Http3IdleTimeoutHandler(TimeSpan.FromSeconds(10), t0);

        var t1 = t0.AddSeconds(3);
        handler.OnStreamOpened(t1);

        Assert.Equal(1, handler.ActiveStreamCount);
        Assert.Equal(t1, handler.LastActivity);
    }

    [Fact(DisplayName = "RFC9114-5.1-IT-006: Stream close resets idle timer and decrements count")]
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

    [Fact(DisplayName = "RFC9114-5.1-IT-007: Closing stream with no active streams throws")]
    public void OnStreamClosed_NoActiveStreams_Throws()
    {
        var handler = new Http3IdleTimeoutHandler(TimeSpan.FromSeconds(10));

        Assert.Throws<InvalidOperationException>(() => handler.OnStreamClosed());
    }

    // ───────────── Idle timeout expiry ─────────────

    [Fact(DisplayName = "RFC9114-5.1-IT-008: Connection not expired within timeout")]
    public void IsIdleTimeoutExpired_WithinTimeout_ReturnsFalse()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var handler = new Http3IdleTimeoutHandler(TimeSpan.FromSeconds(30), t0);

        Assert.False(handler.IsIdleTimeoutExpired(t0.AddSeconds(29)));
    }

    [Fact(DisplayName = "RFC9114-5.1-IT-009: Connection expired at exact timeout boundary")]
    public void IsIdleTimeoutExpired_AtBoundary_ReturnsTrue()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var handler = new Http3IdleTimeoutHandler(TimeSpan.FromSeconds(30), t0);

        Assert.True(handler.IsIdleTimeoutExpired(t0.AddSeconds(30)));
    }

    [Fact(DisplayName = "RFC9114-5.1-IT-010: Connection expired after timeout")]
    public void IsIdleTimeoutExpired_AfterTimeout_ReturnsTrue()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var handler = new Http3IdleTimeoutHandler(TimeSpan.FromSeconds(10), t0);

        Assert.True(handler.IsIdleTimeoutExpired(t0.AddSeconds(15)));
    }

    [Fact(DisplayName = "RFC9114-5.1-IT-011: Disabled timeout never expires")]
    public void IsIdleTimeoutExpired_Disabled_NeverExpires()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var handler = new Http3IdleTimeoutHandler(TimeSpan.Zero, t0);

        Assert.False(handler.IsIdleTimeoutExpired(t0.AddHours(24)));
    }

    [Fact(DisplayName = "RFC9114-5.1-IT-012: Activity resets expiry window")]
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

    // ───────────── Effective timeout computation ─────────────

    [Fact(DisplayName = "RFC9114-5.1-IT-013: Effective timeout is minimum of local and remote")]
    public void ComputeEffectiveTimeout_TakesMinimum()
    {
        var result = Http3IdleTimeoutHandler.ComputeEffectiveTimeout(
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(15));

        Assert.Equal(TimeSpan.FromSeconds(15), result);
    }

    [Fact(DisplayName = "RFC9114-5.1-IT-014: Zero local timeout uses remote value")]
    public void ComputeEffectiveTimeout_LocalZero_UsesRemote()
    {
        var result = Http3IdleTimeoutHandler.ComputeEffectiveTimeout(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(20));

        Assert.Equal(TimeSpan.FromSeconds(20), result);
    }

    [Fact(DisplayName = "RFC9114-5.1-IT-015: Zero remote timeout uses local value")]
    public void ComputeEffectiveTimeout_RemoteZero_UsesLocal()
    {
        var result = Http3IdleTimeoutHandler.ComputeEffectiveTimeout(
            TimeSpan.FromSeconds(25),
            TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromSeconds(25), result);
    }

    [Fact(DisplayName = "RFC9114-5.1-IT-016: Both zero means timeout disabled")]
    public void ComputeEffectiveTimeout_BothZero_ReturnsZero()
    {
        var result = Http3IdleTimeoutHandler.ComputeEffectiveTimeout(
            TimeSpan.Zero, TimeSpan.Zero);

        Assert.Equal(TimeSpan.Zero, result);
    }

    [Fact(DisplayName = "RFC9114-5.1-IT-017: Negative local timeout rejected")]
    public void ComputeEffectiveTimeout_NegativeLocal_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Http3IdleTimeoutHandler.ComputeEffectiveTimeout(
                TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(10)));
    }

    [Fact(DisplayName = "RFC9114-5.1-IT-018: Negative remote timeout rejected")]
    public void ComputeEffectiveTimeout_NegativeRemote_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Http3IdleTimeoutHandler.ComputeEffectiveTimeout(
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(-1)));
    }

    // ───────────── Time until expiry ─────────────

    [Fact(DisplayName = "RFC9114-5.1-IT-019: TimeUntilExpiry returns remaining time")]
    public void TimeUntilExpiry_ReturnsRemainingTime()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var handler = new Http3IdleTimeoutHandler(TimeSpan.FromSeconds(30), t0);

        var remaining = handler.TimeUntilExpiry(t0.AddSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(20), remaining);
    }

    [Fact(DisplayName = "RFC9114-5.1-IT-020: TimeUntilExpiry returns zero when expired")]
    public void TimeUntilExpiry_Expired_ReturnsZero()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var handler = new Http3IdleTimeoutHandler(TimeSpan.FromSeconds(10), t0);

        var remaining = handler.TimeUntilExpiry(t0.AddSeconds(15));
        Assert.Equal(TimeSpan.Zero, remaining);
    }

    [Fact(DisplayName = "RFC9114-5.1-IT-021: TimeUntilExpiry returns MaxValue when disabled")]
    public void TimeUntilExpiry_Disabled_ReturnsMaxValue()
    {
        var handler = new Http3IdleTimeoutHandler(TimeSpan.Zero);

        Assert.Equal(TimeSpan.MaxValue, handler.TimeUntilExpiry());
    }

    // ───────────── Reconnection policy ─────────────

    [Fact(DisplayName = "RFC9114-5.1-RC-001: Reconnection after idle timeout succeeds")]
    public void ShouldReconnect_IdleTimeout_ReturnsTrue()
    {
        var policy = new Http3ReconnectionPolicy(maxAttempts: 3);

        var decision = policy.ShouldReconnect(Http3ConnectionLossReason.IdleTimeout);

        Assert.True(decision.ShouldAttempt);
        Assert.Equal(TimeSpan.FromSeconds(1), decision.Delay);
    }

    [Fact(DisplayName = "RFC9114-5.1-RC-002: Reconnection after GOAWAY succeeds")]
    public void ShouldReconnect_GoAway_ReturnsTrue()
    {
        var policy = new Http3ReconnectionPolicy(maxAttempts: 3);

        var decision = policy.ShouldReconnect(Http3ConnectionLossReason.GoAway);

        Assert.True(decision.ShouldAttempt);
    }

    [Fact(DisplayName = "RFC9114-5.1-RC-003: Reconnection after transport error succeeds")]
    public void ShouldReconnect_TransportError_ReturnsTrue()
    {
        var policy = new Http3ReconnectionPolicy(maxAttempts: 3);

        var decision = policy.ShouldReconnect(Http3ConnectionLossReason.TransportError);

        Assert.True(decision.ShouldAttempt);
    }

    [Fact(DisplayName = "RFC9114-5.1-RC-004: Protocol error prevents reconnection")]
    public void ShouldReconnect_ProtocolError_ReturnsFalse()
    {
        var policy = new Http3ReconnectionPolicy(maxAttempts: 3);

        var decision = policy.ShouldReconnect(Http3ConnectionLossReason.ProtocolError);

        Assert.False(decision.ShouldAttempt);
    }

    [Fact(DisplayName = "RFC9114-5.1-RC-005: Exponential backoff increases delay")]
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

    [Fact(DisplayName = "RFC9114-5.1-RC-006: Delay capped at maxDelay")]
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

    [Fact(DisplayName = "RFC9114-5.1-RC-007: Max attempts exhausted prevents reconnection")]
    public void ShouldReconnect_MaxAttemptsExhausted_ReturnsFalse()
    {
        var policy = new Http3ReconnectionPolicy(maxAttempts: 2);

        policy.ShouldReconnect(Http3ConnectionLossReason.IdleTimeout);
        policy.ShouldReconnect(Http3ConnectionLossReason.IdleTimeout);

        Assert.True(policy.IsExhausted);
        var decision = policy.ShouldReconnect(Http3ConnectionLossReason.IdleTimeout);
        Assert.False(decision.ShouldAttempt);
    }

    [Fact(DisplayName = "RFC9114-5.1-RC-008: Reset clears attempt counter")]
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

    [Fact(DisplayName = "RFC9114-5.1-RC-009: Server closed allows reconnection")]
    public void ShouldReconnect_ServerClosed_ReturnsTrue()
    {
        var policy = new Http3ReconnectionPolicy(maxAttempts: 3);

        var decision = policy.ShouldReconnect(Http3ConnectionLossReason.ServerClosed);

        Assert.True(decision.ShouldAttempt);
    }

    [Fact(DisplayName = "RFC9114-5.1-RC-010: Zero max attempts means no reconnection")]
    public void Constructor_ZeroMaxAttempts_AlwaysExhausted()
    {
        var policy = new Http3ReconnectionPolicy(maxAttempts: 0);

        Assert.True(policy.IsExhausted);
        var decision = policy.ShouldReconnect(Http3ConnectionLossReason.IdleTimeout);
        Assert.False(decision.ShouldAttempt);
    }

    [Fact(DisplayName = "RFC9114-5.1-RC-011: Negative max attempts rejected")]
    public void Constructor_NegativeMaxAttempts_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Http3ReconnectionPolicy(maxAttempts: -1));
    }

    [Fact(DisplayName = "RFC9114-5.1-RC-012: Default policy has sensible defaults")]
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
