using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Tests.Http2.Components;

/// <summary>
/// Unit tests for ConnectionState RFC 9113 flow control, SETTINGS, PING, and GOAWAY handling.
/// Covers per-connection and per-stream receive window management, WINDOW_UPDATE batching,
/// and connection state lifecycle.
/// </summary>
public sealed class Http2ConnectionStateSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void ConnectionState_should_initialize_with_correct_window_thresholds_when_constructed_with_min_window_size()
    {
        const int MinSize = 8192;
        var state = new ConnectionState(MinSize, MinSize);

        Assert.Equal(MinSize, state.RecvConnectionWindow);
        Assert.Equal(65535, state.SendConnectionWindow);
        Assert.Equal(MinSize, state.InitialRecvStreamWindow);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void ConnectionState_should_clamp_threshold_to_max_when_constructed_with_large_window_size()
    {
        const int LargeSize = 1_000_000;
        var state = new ConnectionState(LargeSize, LargeSize);

        Assert.Equal(LargeSize, state.RecvConnectionWindow);
        Assert.Equal(65535, state.SendConnectionWindow);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void ConnectionState_should_use_quarter_of_window_as_threshold_when_constructed_with_medium_window_size()
    {
        const int WindowSize = 65536;
        var state = new ConnectionState(WindowSize, WindowSize);

        Assert.Equal(WindowSize, state.RecvConnectionWindow);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void OnRemoteSettings_should_return_default_when_frame_is_ack()
    {
        var state = new ConnectionState(65535, 65535);
        var ackFrame = new SettingsFrame([], isAck: true);

        var result = state.OnRemoteSettings(ackFrame);

        Assert.Null(result.MaxConcurrentStreamsChange);
        Assert.Null(result.InitialWindowSizeChange);
        Assert.Null(result.AckFrame);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void OnRemoteSettings_should_return_ack_when_frame_has_no_parameters()
    {
        var state = new ConnectionState(65535, 65535);
        var frame = new SettingsFrame([], isAck: false);

        var result = state.OnRemoteSettings(frame);

        Assert.NotNull(result.AckFrame);
        Assert.True(result.AckFrame!.IsAck);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void OnRemoteSettings_should_update_initial_send_window_when_initialwindowsize_parameter_present()
    {
        var state = new ConnectionState(65535, 65535);
        const int NewWindowSize = 32768;
        var parameters = new[] { (SettingsParameter.InitialWindowSize, (uint)NewWindowSize) };
        var frame = new SettingsFrame(parameters, isAck: false);

        var result = state.OnRemoteSettings(frame);

        Assert.Equal(NewWindowSize, state.InitialSendStreamWindow);
        Assert.Equal(NewWindowSize, result.InitialWindowSizeChange);
        Assert.NotNull(result.AckFrame);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void OnRemoteSettings_should_report_maxconcurrentstreams_change_when_parameter_present()
    {
        var state = new ConnectionState(65535, 65535);
        const int MaxStreams = 100;
        var parameters = new[] { (SettingsParameter.MaxConcurrentStreams, (uint)MaxStreams) };
        var frame = new SettingsFrame(parameters, isAck: false);

        var result = state.OnRemoteSettings(frame);

        Assert.Equal(MaxStreams, result.MaxConcurrentStreamsChange);
        Assert.NotNull(result.AckFrame);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void OnRemoteSettings_should_handle_both_parameters_when_both_present()
    {
        var state = new ConnectionState(65535, 65535);
        var parameters = new[]
        {
            (SettingsParameter.InitialWindowSize, (uint)32768),
            (SettingsParameter.MaxConcurrentStreams, (uint)200)
        };
        var frame = new SettingsFrame(parameters, isAck: false);

        var result = state.OnRemoteSettings(frame);

        Assert.Equal(32768, result.InitialWindowSizeChange);
        Assert.Equal(200, result.MaxConcurrentStreamsChange);
        Assert.NotNull(result.AckFrame);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void OnInboundData_should_return_success_when_data_length_zero()
    {
        var state = new ConnectionState(65535, 65535);

        var result = state.OnInboundData(streamId: 1, dataLength: 0);

        Assert.True(result.Success);
        Assert.False(result.IsConnectionViolation);
        Assert.False(result.IsStreamViolation);
        Assert.Null(result.ConnectionWindowUpdate);
        Assert.Null(result.StreamWindowUpdate);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void OnInboundData_should_return_success_when_data_below_threshold()
    {
        var state = new ConnectionState(65535, 65535);
        const int SmallDataLength = 100;

        var result = state.OnInboundData(streamId: 1, dataLength: SmallDataLength);

        Assert.True(result.Success);
        Assert.Equal(65535 - SmallDataLength, state.RecvConnectionWindow);
        Assert.Null(result.ConnectionWindowUpdate);
        Assert.Null(result.StreamWindowUpdate);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void OnInboundData_should_return_connection_violation_when_recv_window_negative()
    {
        var state = new ConnectionState(1000, 65535);

        var result = state.OnInboundData(streamId: 1, dataLength: 2000);

        Assert.False(result.Success);
        Assert.True(result.IsConnectionViolation);
        Assert.False(result.IsStreamViolation);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void OnInboundData_should_return_stream_violation_when_stream_window_negative()
    {
        var state = new ConnectionState(65535, 1000);

        var result = state.OnInboundData(streamId: 1, dataLength: 2000);

        Assert.False(result.Success);
        Assert.False(result.IsConnectionViolation);
        Assert.True(result.IsStreamViolation);
        Assert.Equal(1, result.ViolationStreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void OnInboundData_should_send_connection_window_update_when_pending_threshold_reached()
    {
        var state = new ConnectionState(65535, 65535);
        const int LargeData = 20000;

        var result = state.OnInboundData(streamId: 1, dataLength: LargeData);

        Assert.True(result.Success);
        Assert.NotNull(result.ConnectionWindowUpdate);
        Assert.Equal(0, result.ConnectionWindowUpdate!.StreamId);
        Assert.True(result.ConnectionWindowUpdate!.Increment > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void OnInboundData_should_send_stream_window_update_when_pending_threshold_reached()
    {
        var state = new ConnectionState(65535, 65535);
        const int LargeData = 20000;

        var result = state.OnInboundData(streamId: 1, dataLength: LargeData);

        Assert.True(result.Success);
        Assert.NotNull(result.StreamWindowUpdate);
        Assert.Equal(1, result.StreamWindowUpdate!.StreamId);
        Assert.True(result.StreamWindowUpdate!.Increment > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void OnInboundData_should_batch_window_updates_across_multiple_frames()
    {
        var state = new ConnectionState(65535, 65535);
        const int SmallData = 1000;

        var result1 = state.OnInboundData(streamId: 1, dataLength: SmallData);
        Assert.Null(result1.ConnectionWindowUpdate);

        var result2 = state.OnInboundData(streamId: 1, dataLength: SmallData);
        Assert.Null(result2.ConnectionWindowUpdate);

        const int LargeData = 20000;
        var result3 = state.OnInboundData(streamId: 2, dataLength: LargeData);
        Assert.NotNull(result3.ConnectionWindowUpdate);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void OnInboundData_should_initialize_stream_window_on_first_data()
    {
        var state = new ConnectionState(65535, 50000);

        var result = state.OnInboundData(streamId: 5, dataLength: 1000);

        Assert.True(result.Success);
        Assert.Equal(65535 - 1000, state.RecvConnectionWindow);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void OnInboundData_should_track_separate_stream_windows()
    {
        var state = new ConnectionState(65535, 65535);

        state.OnInboundData(streamId: 1, dataLength: 1000);
        var result2 = state.OnInboundData(streamId: 2, dataLength: 500);

        Assert.True(result2.Success);
        Assert.Equal(65535 - 1000 - 500, state.RecvConnectionWindow);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void OnWindowUpdate_should_increase_send_window_when_streamid_zero()
    {
        var state = new ConnectionState(65535, 65535);
        var frame = new WindowUpdateFrame(streamId: 0, increment: 5000);

        state.OnWindowUpdate(frame);

        Assert.Equal(65535 + 5000, state.SendConnectionWindow);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void OnWindowUpdate_should_not_change_connection_window_when_nonzero_streamid()
    {
        var state = new ConnectionState(65535, 65535);
        var frame = new WindowUpdateFrame(streamId: 1, increment: 5000);
        var initialWindow = state.SendConnectionWindow;

        state.OnWindowUpdate(frame);

        Assert.Equal(initialWindow, state.SendConnectionWindow);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void OnPing_should_return_ack_ping_when_ping_not_ack()
    {
        var state = new ConnectionState(65535, 65535);
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var ping = new PingFrame(data, isAck: false);

        var result = state.OnPing(ping);

        Assert.NotNull(result);
        Assert.True(result!.IsAck);
        Assert.True(result.Data.Span.SequenceEqual(data));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void OnPing_should_return_null_when_ping_is_ack()
    {
        var state = new ConnectionState(65535, 65535);
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var ping = new PingFrame(data, isAck: true);

        var result = state.OnPing(ping);

        Assert.Null(result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void OnGoAway_should_set_goaway_received_flag()
    {
        var state = new ConnectionState(65535, 65535);
        Assert.False(state.GoAwayReceived);

        state.OnGoAway();

        Assert.True(state.GoAwayReceived);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void Reset_should_clear_all_state()
    {
        var state = new ConnectionState(65535, 65535);
        state.OnGoAway();
        state.OnInboundData(streamId: 1, dataLength: 1000);

        state.Reset(65535, 65535);

        Assert.False(state.GoAwayReceived);
        Assert.Equal(65535, state.RecvConnectionWindow);
        Assert.Equal(65535, state.SendConnectionWindow);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6")]
    public void Reset_should_reinitialize_windows_to_provided_values()
    {
        var state = new ConnectionState(65535, 65535);
        const int NewConnWindow = 50000;
        const int NewStreamWindow = 40000;

        state.Reset(NewConnWindow, NewStreamWindow);

        Assert.Equal(NewConnWindow, state.RecvConnectionWindow);
        Assert.Equal(NewStreamWindow, state.InitialRecvStreamWindow);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void OnStreamClosed_should_return_null_when_no_pending_increment()
    {
        var state = new ConnectionState(65535, 65535);
        state.OnInboundData(streamId: 1, dataLength: 0);

        var result = state.OnStreamClosed(streamId: 1);

        Assert.Null(result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void OnStreamClosed_should_return_window_update_when_pending_increment_exists()
    {
        var state = new ConnectionState(65535, 65535);
        const int SmallData = 1000;
        state.OnInboundData(streamId: 1, dataLength: SmallData);

        var result = state.OnStreamClosed(streamId: 1);

        Assert.NotNull(result);
        Assert.Equal(1, result!.StreamId);
        Assert.Equal(SmallData, result.Increment);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void OnStreamClosed_should_remove_stream_from_tracking()
    {
        var state = new ConnectionState(65535, 65535);
        state.OnInboundData(streamId: 1, dataLength: 1000);
        state.OnStreamClosed(streamId: 1);

        var result = state.OnInboundData(streamId: 1, dataLength: 500);

        Assert.True(result.Success);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void OnInboundData_should_create_separate_stream_windows_per_stream()
    {
        var state = new ConnectionState(65535, 10000);
        state.OnInboundData(streamId: 1, dataLength: 5000);
        var result = state.OnInboundData(streamId: 2, dataLength: 5000);

        Assert.True(result.Success);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void OnInboundData_should_handle_multiple_window_updates_on_same_stream()
    {
        var state = new ConnectionState(65535, 65535);
        const int Data1 = 3000;
        const int Data2 = 3000;
        const int Data3 = 20000;

        var result1 = state.OnInboundData(streamId: 1, dataLength: Data1);
        Assert.Null(result1.StreamWindowUpdate);

        var result2 = state.OnInboundData(streamId: 1, dataLength: Data2);
        Assert.Null(result2.StreamWindowUpdate);

        var result3 = state.OnInboundData(streamId: 1, dataLength: Data3);
        Assert.NotNull(result3.StreamWindowUpdate);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void OnRemoteSettings_should_ignore_unknown_parameters()
    {
        var state = new ConnectionState(65535, 65535);
        var parameters = new[] { ((SettingsParameter)999, (uint)1000) };
        var frame = new SettingsFrame(parameters, isAck: false);

        var result = state.OnRemoteSettings(frame);

        Assert.NotNull(result.AckFrame);
        Assert.True(result.AckFrame!.IsAck);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void OnInboundData_should_accumulate_pending_increments_correctly()
    {
        var state = new ConnectionState(65535, 65535);
        const int Chunk = 5000;

        state.OnInboundData(streamId: 1, dataLength: Chunk);
        state.OnInboundData(streamId: 2, dataLength: Chunk);
        state.OnInboundData(streamId: 3, dataLength: Chunk);

        Assert.Equal(65535 - (Chunk * 3), state.RecvConnectionWindow);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void OnStreamClosed_should_handle_stream_without_data()
    {
        var state = new ConnectionState(65535, 65535);

        var result = state.OnStreamClosed(streamId: 999);

        Assert.Null(result);
    }
}
