using TurboHTTP.Protocol.Syntax.Http2;

namespace TurboHTTP.Tests.Protocol.Multiplexed;

public sealed class FlowControllerSpec
{
    [Fact(Timeout = 5000)]
    public void FlowController_should_return_min_of_connection_and_stream_window()
    {
        var fc = new FlowController(
            connectionWindowSize: 65535,
            streamWindowSize: 65535,
            initialConnectionSendWindow: 1000,
            initialStreamSendWindow: 500);

        fc.InitStreamSendWindow(1);
        Assert.Equal(500, fc.GetSendWindow(1));
    }

    [Fact(Timeout = 5000)]
    public void FlowController_should_decrement_both_windows_on_data_sent()
    {
        var fc = new FlowController(
            connectionWindowSize: 65535,
            streamWindowSize: 65535,
            initialConnectionSendWindow: 1000,
            initialStreamSendWindow: 1000);

        fc.InitStreamSendWindow(1);
        fc.OnDataSent(1, 300);
        Assert.Equal(700, fc.GetSendWindow(1));
    }

    [Fact(Timeout = 5000)]
    public void FlowController_should_detect_connection_flow_control_violation()
    {
        var fc = new FlowController(
            connectionWindowSize: 100,
            streamWindowSize: 65535);

        var result = fc.OnInboundData(1, 200);
        Assert.False(result.Success);
        Assert.True(result.IsConnectionViolation);
    }

    [Fact(Timeout = 5000)]
    public void FlowController_should_detect_stream_flow_control_violation()
    {
        var fc = new FlowController(
            connectionWindowSize: 65535,
            streamWindowSize: 100);

        var result = fc.OnInboundData(1, 200);
        Assert.False(result.Success);
        Assert.True(result.IsStreamViolation);
        Assert.Equal(1, result.ViolationStreamId);
    }

    [Fact(Timeout = 5000)]
    public void FlowController_should_batch_window_updates()
    {
        var windowSize = 65535;
        var fc = new FlowController(
            connectionWindowSize: windowSize,
            streamWindowSize: windowSize);

        var result = fc.OnInboundData(1, 100);
        Assert.True(result.Success);
        Assert.Null(result.ConnectionWindowUpdate);

        var threshold = Math.Max(8192, windowSize / 2);
        result = fc.OnInboundData(1, threshold);
        Assert.True(result.Success);
        Assert.NotNull(result.ConnectionWindowUpdate);
        Assert.NotNull(result.StreamWindowUpdate);
    }

    [Fact(Timeout = 5000)]
    public void FlowController_should_increment_send_window_on_update()
    {
        var fc = new FlowController(
            connectionWindowSize: 65535,
            streamWindowSize: 65535,
            initialConnectionSendWindow: 100,
            initialStreamSendWindow: 100);

        fc.InitStreamSendWindow(1);
        fc.OnSendWindowUpdate(0, 500);
        fc.OnSendWindowUpdate(1, 500);
        Assert.Equal(600, fc.GetSendWindow(1));
    }

    [Fact(Timeout = 5000)]
    public void FlowController_should_track_goaway()
    {
        var fc = new FlowController(connectionWindowSize: 65535, streamWindowSize: 65535);
        Assert.False(fc.GoAwayReceived);
        fc.OnGoAway();
        Assert.True(fc.GoAwayReceived);
    }

    [Fact(Timeout = 5000)]
    public void FlowController_should_return_pending_update_on_stream_close()
    {
        var windowSize = 65535;
        var fc = new FlowController(
            connectionWindowSize: windowSize,
            streamWindowSize: windowSize);

        fc.OnInboundData(1, 100);
        var signal = fc.OnStreamClosed(1);
        Assert.NotNull(signal);
        Assert.Equal(1, signal.Value.StreamId);
        Assert.Equal(100, signal.Value.Increment);
    }

    [Fact(Timeout = 5000)]
    public void FlowController_should_reset_all_state()
    {
        var fc = new FlowController(connectionWindowSize: 65535, streamWindowSize: 65535);
        fc.OnInboundData(1, 100);
        fc.OnGoAway();
        fc.Reset(65535, 65535);
        Assert.False(fc.GoAwayReceived);
    }

    [Fact(Timeout = 5000)]
    public void FlowController_should_apply_initial_window_size_delta()
    {
        var fc = new FlowController(
            connectionWindowSize: 65535,
            streamWindowSize: 65535,
            initialConnectionSendWindow: 1000,
            initialStreamSendWindow: 500);

        fc.InitStreamSendWindow(1);
        fc.ApplyInitialWindowSizeDelta(200);
        Assert.Equal(700, fc.GetSendWindow(1));
    }
}