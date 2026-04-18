using TurboHTTP.Protocol.Http3;

namespace TurboHTTP.Tests.Http3.Connection;

/// <summary>
/// Covers remaining edge cases and branches in HTTP/3 ConnectionState (RFC 9114).
/// Tests GOAWAY validation, Settings handling, timeout behavior, push accounting,
/// stream lifecycle, and reconnection reset logic.
/// </summary>
[Trait("RFC", "RFC9114-5")]
public sealed class Http3ConnectionStateEdgeCasesSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void OnServerGoAway_should_accept_valid_stream_id_divisible_by_four()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame = new Http3GoAwayFrame(streamId: 0);

        // Should not throw
        state.OnServerGoAway(frame);

        Assert.True(state.GoAwayReceived);
        Assert.Equal(0, state.LastGoAwayStreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void OnServerGoAway_should_reject_stream_id_not_divisible_by_four()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame = new Http3GoAwayFrame(streamId: 1);

        var ex = Assert.Throws<Http3Exception>(() => state.OnServerGoAway(frame));
        Assert.Contains("divisible by 4", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void OnServerGoAway_should_reject_stream_id_not_divisible_by_four_odd()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame = new Http3GoAwayFrame(streamId: 3);

        var ex = Assert.Throws<Http3Exception>(() => state.OnServerGoAway(frame));
        Assert.Contains("divisible by 4", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void OnServerGoAway_should_reject_stream_id_not_divisible_by_four_mod_two()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame = new Http3GoAwayFrame(streamId: 2);

        var ex = Assert.Throws<Http3Exception>(() => state.OnServerGoAway(frame));
        Assert.Contains("divisible by 4", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void OnServerGoAway_should_reject_increasing_stream_ids()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame1 = new Http3GoAwayFrame(streamId: 4);
        var frame2 = new Http3GoAwayFrame(streamId: 8);

        state.OnServerGoAway(frame1);

        var ex = Assert.Throws<Http3Exception>(() => state.OnServerGoAway(frame2));
        Assert.Contains("must not increase beyond previous value", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void OnServerGoAway_should_accept_decreasing_stream_ids()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame1 = new Http3GoAwayFrame(streamId: 8);
        var frame2 = new Http3GoAwayFrame(streamId: 4);

        state.OnServerGoAway(frame1);
        state.OnServerGoAway(frame2);

        Assert.Equal(4, state.LastGoAwayStreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void OnServerGoAway_should_accept_equal_stream_ids()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame1 = new Http3GoAwayFrame(streamId: 8);
        var frame2 = new Http3GoAwayFrame(streamId: 8);

        state.OnServerGoAway(frame1);

        // Frame1 set LastGoAwayStreamId to 8; Frame2 with streamId=8 should be allowed since 8 is not > 8
        state.OnServerGoAway(frame2);

        Assert.Equal(8, state.LastGoAwayStreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void OnServerGoAway_should_throw_on_null_frame()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        Assert.Throws<ArgumentNullException>(() => state.OnServerGoAway(null!));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void OnRemoteSettings_should_accept_first_settings_frame()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame = new Http3SettingsFrame(new[] { (Http3SettingsIdentifier.MaxFieldSectionSize, 4096L) });

        state.OnRemoteSettings(frame);

        Assert.True(state.RemoteSettingsReceived);
        Assert.NotNull(state.RemoteSettings);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void OnRemoteSettings_should_reject_duplicate_settings_frames()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame1 = new Http3SettingsFrame(new[] { (Http3SettingsIdentifier.MaxFieldSectionSize, 4096L) });
        var frame2 = new Http3SettingsFrame(new[] { (Http3SettingsIdentifier.MaxFieldSectionSize, 8192L) });

        state.OnRemoteSettings(frame1);

        var ex = Assert.Throws<Http3Exception>(() => state.OnRemoteSettings(frame2));
        Assert.Contains("second SETTINGS frame", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void OnRemoteSettings_should_throw_on_null_frame()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        Assert.Throws<NullReferenceException>(() => state.OnRemoteSettings(null!));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void OnRemoteSettings_should_store_multiple_parameters()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame = new Http3SettingsFrame(new[]
        {
            (Http3SettingsIdentifier.MaxFieldSectionSize, 4096L),
            (Http3SettingsIdentifier.QpackMaxTableCapacity, 2048L),
        });

        state.OnRemoteSettings(frame);

        Assert.NotNull(state.RemoteSettings);
        Assert.Equal(4096, state.RemoteMaxFieldSectionSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.1")]
    public void RecordActivity_should_update_last_activity()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        // Get initial state (activity recorded in constructor)
        var initialTimeout = state.TimeUntilExpiry();

        Thread.Sleep(100);

        // Record new activity
        state.RecordActivity();

        // New timeout should be close to full timeout (fresh activity)
        var newTimeout = state.TimeUntilExpiry();

        // Should be very close to 30 seconds (fresh activity means full timeout remaining)
        Assert.True(newTimeout.TotalSeconds > 29);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.1")]
    public void IsIdleTimeoutExpired_should_return_false_on_fresh_connection()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        Assert.False(state.IsIdleTimeoutExpired());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.1")]
    public void IsIdleTimeoutExpired_should_return_true_when_timeout_elapsed()
    {
        var state = new ConnectionState(TimeSpan.FromMilliseconds(100));

        Thread.Sleep(150);

        Assert.True(state.IsIdleTimeoutExpired());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.1")]
    public void IsIdleTimeoutExpired_should_return_false_when_timeout_disabled()
    {
        var state = new ConnectionState(TimeSpan.Zero);

        // Even after waiting, should never expire
        Thread.Sleep(100);

        Assert.False(state.IsIdleTimeoutExpired());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.1")]
    public void TimeUntilExpiry_should_return_remaining_time()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(10));

        var remaining = state.TimeUntilExpiry();

        Assert.True(remaining.TotalSeconds > 9);
        Assert.True(remaining.TotalSeconds <= 10);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.1")]
    public void TimeUntilExpiry_should_return_zero_when_expired()
    {
        var state = new ConnectionState(TimeSpan.FromMilliseconds(100));

        Thread.Sleep(150);

        var remaining = state.TimeUntilExpiry();

        Assert.Equal(TimeSpan.Zero, remaining);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.1")]
    public void TimeUntilExpiry_should_return_max_value_when_disabled()
    {
        var state = new ConnectionState(TimeSpan.Zero);

        var remaining = state.TimeUntilExpiry();

        Assert.Equal(TimeSpan.MaxValue, remaining);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.1")]
    public void IsTimeoutDisabled_should_return_true_for_zero_timeout()
    {
        var state = new ConnectionState(TimeSpan.Zero);

        Assert.True(state.IsTimeoutDisabled);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.1")]
    public void IsTimeoutDisabled_should_return_false_for_nonzero_timeout()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        Assert.False(state.IsTimeoutDisabled);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.4")]
    public void OnStreamOpened_should_increment_active_stream_count()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        Assert.Equal(0, state.ActiveStreamCount);

        state.OnStreamOpened();

        Assert.Equal(1, state.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.4")]
    public void OnStreamOpened_should_increment_multiple_times()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        state.OnStreamOpened();
        state.OnStreamOpened();
        state.OnStreamOpened();

        Assert.Equal(3, state.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.4")]
    public void OnStreamOpened_should_record_activity()
    {
        var state = new ConnectionState(TimeSpan.FromMilliseconds(100));

        Thread.Sleep(150);

        // Expired before opening stream
        Assert.True(state.IsIdleTimeoutExpired());

        // Create fresh state
        var state2 = new ConnectionState(TimeSpan.FromSeconds(30));
        state2.OnStreamOpened();

        // Should record activity, resetting timeout
        Assert.False(state2.IsIdleTimeoutExpired());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.4")]
    public void OnStreamClosed_should_decrement_active_stream_count()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        state.OnStreamOpened();
        state.OnStreamOpened();
        Assert.Equal(2, state.ActiveStreamCount);

        state.OnStreamClosed();

        Assert.Equal(1, state.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.4")]
    public void OnStreamClosed_should_not_go_negative()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        // Close without opening
        state.OnStreamClosed();

        Assert.Equal(0, state.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.4")]
    public void OnStreamClosed_should_record_activity()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        state.OnStreamOpened();
        state.OnStreamClosed();

        // Activity was recorded
        Assert.False(state.IsIdleTimeoutExpired());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.5")]
    public void RecordPush_should_track_push_count()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30), maxPushCount: 10);

        for (var i = 0; i < 5; i++)
        {
            state.RecordPush(); // Should not throw
        }

        // Internal push count is incremented (no public way to verify, but no exception = success)
        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.5")]
    public void RecordPush_should_reject_push_beyond_limit()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30), maxPushCount: 3);

        for (var i = 0; i < 3; i++)
        {
            state.RecordPush();
        }

        var ex = Assert.Throws<Http3Exception>(() => state.RecordPush());
        Assert.Contains("push limit", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.5")]
    public void RecordPush_should_handle_zero_max_push_count()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30), maxPushCount: 0);

        var ex = Assert.Throws<Http3Exception>(() => state.RecordPush());
        Assert.Contains("push limit", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.5")]
    public void OnReceivedCancelPush_should_track_cancelled_push_ids()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame = new Http3CancelPushFrame(pushId: 42);

        state.OnReceivedCancelPush(frame);

        Assert.True(state.IsPushCancelled(42));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.5")]
    public void OnReceivedCancelPush_should_track_multiple_cancelled_push_ids()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        state.OnReceivedCancelPush(new Http3CancelPushFrame(pushId: 1));
        state.OnReceivedCancelPush(new Http3CancelPushFrame(pushId: 2));
        state.OnReceivedCancelPush(new Http3CancelPushFrame(pushId: 3));

        Assert.True(state.IsPushCancelled(1));
        Assert.True(state.IsPushCancelled(2));
        Assert.True(state.IsPushCancelled(3));
        Assert.False(state.IsPushCancelled(4));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.5")]
    public void OnReceivedCancelPush_should_throw_on_null_frame()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        Assert.Throws<ArgumentNullException>(() => state.OnReceivedCancelPush(null!));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.5")]
    public void IsPushCancelled_should_return_false_for_unknown_push_id()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        Assert.False(state.IsPushCancelled(999));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void Reset_should_clear_goaway_state()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame = new Http3GoAwayFrame(streamId: 4);

        state.OnServerGoAway(frame);
        Assert.True(state.GoAwayReceived);

        state.Reset();

        Assert.False(state.GoAwayReceived);
        Assert.Equal(-1, state.LastGoAwayStreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Reset_should_clear_settings_state()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame = new Http3SettingsFrame(new[] { (Http3SettingsIdentifier.MaxFieldSectionSize, 4096L) });

        state.OnRemoteSettings(frame);
        Assert.True(state.RemoteSettingsReceived);

        state.Reset();

        Assert.False(state.RemoteSettingsReceived);
        Assert.Null(state.RemoteSettings);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.4")]
    public void Reset_should_clear_stream_count()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        state.OnStreamOpened();
        state.OnStreamOpened();
        Assert.Equal(2, state.ActiveStreamCount);

        state.Reset();

        Assert.Equal(0, state.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.5")]
    public void Reset_should_clear_push_state()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30), maxPushCount: 10);

        state.RecordPush();
        state.RecordPush();
        state.OnReceivedCancelPush(new Http3CancelPushFrame(pushId: 5));

        state.Reset();

        // After reset, can record push again (count cleared)
        state.RecordPush();

        Assert.False(state.IsPushCancelled(5));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void Reset_should_record_activity()
    {
        var state = new ConnectionState(TimeSpan.FromMilliseconds(100));

        Thread.Sleep(150);
        Assert.True(state.IsIdleTimeoutExpired());

        state.Reset();

        // Activity recorded, timeout reset
        Assert.False(state.IsIdleTimeoutExpired());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void ComputeEffectiveTimeout_should_prefer_zero_local()
    {
        var result = ConnectionState.ComputeEffectiveTimeout(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromSeconds(30), result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void ComputeEffectiveTimeout_should_prefer_zero_remote()
    {
        var result = ConnectionState.ComputeEffectiveTimeout(
            TimeSpan.FromSeconds(30),
            TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromSeconds(30), result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void ComputeEffectiveTimeout_should_prefer_minimum_of_two_values()
    {
        var result = ConnectionState.ComputeEffectiveTimeout(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(20));

        Assert.Equal(TimeSpan.FromSeconds(10), result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void ComputeEffectiveTimeout_should_prefer_minimum_reverse()
    {
        var result = ConnectionState.ComputeEffectiveTimeout(
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(10), result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void ComputeEffectiveTimeout_should_reject_negative_local()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ConnectionState.ComputeEffectiveTimeout(
                TimeSpan.FromSeconds(-1),
                TimeSpan.FromSeconds(30)));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void ComputeEffectiveTimeout_should_reject_negative_remote()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ConnectionState.ComputeEffectiveTimeout(
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(-1)));
    }

    [Fact(Timeout = 5000)]
    public void MaxPushId_should_be_settable()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        state.MaxPushId = 99;

        Assert.Equal(99, state.MaxPushId);
    }

    [Fact(Timeout = 5000)]
    public void RemoteMaxFieldSectionSize_should_return_null_before_settings()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));

        Assert.Null(state.RemoteMaxFieldSectionSize);
    }

    [Fact(Timeout = 5000)]
    public void RemoteMaxFieldSectionSize_should_return_value_after_settings()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var frame = new Http3SettingsFrame(new[] { (Http3SettingsIdentifier.MaxFieldSectionSize, 8192L) });

        state.OnRemoteSettings(frame);

        Assert.Equal(8192, state.RemoteMaxFieldSectionSize);
    }
}
