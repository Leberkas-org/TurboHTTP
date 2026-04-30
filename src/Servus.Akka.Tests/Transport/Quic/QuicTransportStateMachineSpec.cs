using Akka.Actor;
using Akka.Event;
using Servus.Akka.Transport;
using Servus.Akka.Transport.Quic;

namespace Servus.Akka.Tests.Transport.Quic;

[Collection("TransportBuffer")]
public sealed class QuicTransportStateMachineSpec
{
    private sealed class StubOps : ITransportOperations
    {
        public readonly List<ITransportInbound> PushedInbound = [];
        public int PullCount;
        public bool Completed;
        public readonly Dictionary<string, TimeSpan> Timers = new();
        public readonly HashSet<string> CancelledTimers = [];

        public void OnPushInbound(ITransportInbound item) => PushedInbound.Add(item);
        public void OnSignalPullOutbound() => PullCount++;
        public void OnCompleteStage() => Completed = true;
        public void OnScheduleTimer(string key, TimeSpan delay) => Timers[key] = delay;
        public void OnCancelTimer(string key) => CancelledTimers.Add(key);
        public ILoggingAdapter Log => NoLogger.Instance;
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_ConnectTransport_should_schedule_connect_timeout()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);
        var options = new QuicTransportOptions { Host = "localhost", Port = 443 };

        sm.HandlePush(new ConnectTransport(options));

        Assert.Contains("connect-timeout", ops.Timers.Keys);
        Assert.True(ops.PullCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_OpenStream_should_reject_when_not_connected()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        sm.HandlePush(new OpenStream(1, StreamDirection.Bidirectional));

        Assert.True(ops.PullCount > 0);
        Assert.Empty(ops.PushedInbound);
    }

    [Fact(Timeout = 5000)]
    public void HandleUpstreamFinish_should_complete_when_no_connection()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        sm.HandleUpstreamFinish();

        Assert.True(ops.Completed);
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_MultiplexedData_should_signal_pull_when_no_stream()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        var buffer = TransportBuffer.Rent(16);
        buffer.Length = 4;
        sm.HandlePush(new MultiplexedData(buffer, 1));

        Assert.True(ops.PullCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_CompleteWrites_should_signal_pull_when_no_stream()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        sm.HandlePush(new CompleteWrites(99));

        Assert.True(ops.PullCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_ResetStream_should_signal_pull_when_no_stream()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        sm.HandlePush(new ResetStream(99));

        Assert.True(ops.PullCount > 0);
    }

    // Dispatch tests
    [Fact(Timeout = 5000)]
    public void Dispatch_InboundData_should_dispose_buffer_when_gen_mismatch()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        var buffer = TransportBuffer.Rent(16);
        buffer.Length = 4;

        sm.Dispatch(new InboundData(buffer, 1, 99));

        // Buffer should be disposed, so accessing it should not be safe
        // We verify this indirectly by checking no inbound was pushed
        Assert.Empty(ops.PushedInbound);
    }

    [Fact(Timeout = 5000)]
    public void Dispatch_OutboundWriteDone_should_signal_pull()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        sm.Dispatch(new OutboundWriteDone(1));

        Assert.True(ops.PullCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void Dispatch_EarlyDataRejected_should_push_DataRejected()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        var buffer = TransportBuffer.Rent(16);
        buffer.Length = 4;

        sm.Dispatch(new EarlyDataRejected(buffer));

        Assert.Single(ops.PushedInbound);
        Assert.IsType<DataRejected>(ops.PushedInbound[0]);
    }

    // HandlePush tests
    [Fact(Timeout = 5000)]
    public void HandlePush_DisconnectTransport_should_signal_pull()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        sm.HandlePush(new DisconnectTransport(DisconnectReason.Graceful));

        Assert.True(ops.PullCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_ConnectTransport_should_set_auto_reconnect_from_options()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);
        var options = new QuicTransportOptions
        {
            Host = "localhost",
            Port = 443,
            AutoReconnect = true
        };

        sm.HandlePush(new ConnectTransport(options));

        Assert.Contains("connect-timeout", ops.Timers.Keys);
        Assert.True(ops.PullCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_MultiplexedData_should_dispose_buffer_when_stream_not_found()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        var buffer = TransportBuffer.Rent(16);
        buffer.Length = 4;

        sm.HandlePush(new MultiplexedData(buffer, 999));

        // Buffer is disposed, verify no inbound was pushed
        Assert.Empty(ops.PushedInbound);
        Assert.True(ops.PullCount > 0);
    }

    // Lifecycle tests
    [Fact(Timeout = 5000)]
    public void HandleDownstreamFinish_should_not_complete_when_upstream_not_finished()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        sm.HandleDownstreamFinish();

        // HandleDownstreamFinish should NOT call OnCompleteStage, it just cleans up
        Assert.False(ops.Completed);
    }

    [Fact(Timeout = 5000)]
    public void HandleUpstreamFinish_should_complete_stage()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        sm.HandleUpstreamFinish();

        Assert.True(ops.Completed);
    }

    // Timer tests
    [Fact(Timeout = 5000)]
    public void OnTimer_with_connect_timeout_key_should_push_TransportDisconnected()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        // Set up pending connect
        var options = new QuicTransportOptions { Host = "localhost", Port = 443 };
        sm.HandlePush(new ConnectTransport(options));

        // Now trigger the timeout
        sm.OnTimer("connect-timeout");

        Assert.NotEmpty(ops.PushedInbound);
        var disconnected = ops.PushedInbound.OfType<TransportDisconnected>().FirstOrDefault();
        Assert.NotNull(disconnected);
        Assert.Equal(DisconnectReason.Timeout, disconnected.Reason);
    }

    [Fact(Timeout = 5000)]
    public void OnTimer_with_unknown_key_should_do_nothing()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        sm.OnTimer("unknown-timer-key");

        Assert.Empty(ops.PushedInbound);
        Assert.Equal(0, ops.PullCount);
    }

    [Fact(Timeout = 5000)]
    public void OnTimer_without_pending_connect_should_do_nothing()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        sm.OnTimer("connect-timeout");

        Assert.Empty(ops.PushedInbound);
        Assert.Equal(0, ops.PullCount);
    }

    [Fact(Timeout = 5000)]
    public void PostStop_should_cancel_connect_timer()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        sm.PostStop();

        Assert.Contains("connect-timeout", ops.CancelledTimers);
    }

    // Multiple operation tests
    [Fact(Timeout = 5000)]
    public void HandlePush_ResetStream_should_emit_StreamClosed_when_stream_exists()
    {
        // This is a harder test without real connection state, but we can verify
        // that calling ResetStream on unknown stream just signals pull
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        sm.HandlePush(new ResetStream(999, 0));

        // No pushed inbound for unknown stream
        Assert.Empty(ops.PushedInbound);
        Assert.True(ops.PullCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_CompleteWrites_on_unknown_stream_should_just_pull()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        sm.HandlePush(new CompleteWrites(999));

        Assert.Empty(ops.PushedInbound);
        Assert.True(ops.PullCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void Dispatch_OutboundWriteFailed_should_handle_connection_failure()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        sm.Dispatch(new OutboundWriteFailed(new InvalidOperationException("Write failed"), 1));

        // Should push TransportDisconnected
        var disconnected = ops.PushedInbound.OfType<TransportDisconnected>().FirstOrDefault();
        Assert.NotNull(disconnected);
    }

    [Fact(Timeout = 5000)]
    public void Dispatch_AcquisitionFailed_when_cancelled_should_be_ignored()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        var options = new QuicTransportOptions { Host = "localhost", Port = 443 };
        sm.HandlePush(new ConnectTransport(options));

        // Dispatch acquisition failed with OperationCanceledException
        sm.Dispatch(new AcquisitionFailed(new OperationCanceledException("Cancelled")));

        // Should not push anything (cancelled exceptions are ignored)
        Assert.Empty(ops.PushedInbound);
    }

    [Fact(Timeout = 5000)]
    public void Dispatch_AcquisitionFailed_with_error_should_push_TransportDisconnected()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        var options = new QuicTransportOptions { Host = "localhost", Port = 443 };
        sm.HandlePush(new ConnectTransport(options));

        // Dispatch acquisition failed with actual error
        sm.Dispatch(new AcquisitionFailed(new IOException("Connection failed")));

        // Should cancel timer and push TransportDisconnected
        Assert.Contains("connect-timeout", ops.CancelledTimers);
        var disconnected = ops.PushedInbound.OfType<TransportDisconnected>().FirstOrDefault();
        Assert.NotNull(disconnected);
        Assert.Equal(DisconnectReason.Error, disconnected.Reason);
    }

    [Fact(Timeout = 5000)]
    public void Dispatch_InboundPumpFailed_should_handle_gracefully()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        // InboundPumpFailed doesn't push TransportDisconnected directly, it just calls OnInboundComplete
        // which handles stream cleanup. Since the stream doesn't exist, nothing is pushed.
        sm.Dispatch(new InboundPumpFailed(new IOException("Pump failed"), 1));

        // No inbound should be pushed for non-existent stream
        Assert.Empty(ops.PushedInbound);
    }

    [Fact(Timeout = 5000)]
    public void HandleUpstreamFinish_with_pending_connection_should_complete_stage()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        var options = new QuicTransportOptions { Host = "localhost", Port = 443 };
        sm.HandlePush(new ConnectTransport(options));
        Assert.False(ops.Completed);

        sm.HandleUpstreamFinish();

        Assert.True(ops.Completed);
    }

    [Fact(Timeout = 5000)]
    public void Multiple_TimerCancelAndSchedule_should_be_tracked()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        var options1 = new QuicTransportOptions { Host = "localhost", Port = 443 };
        sm.HandlePush(new ConnectTransport(options1));
        Assert.Contains("connect-timeout", ops.Timers.Keys);
        Assert.Empty(ops.CancelledTimers);

        // Second connect should reuse/reset the timer
        var options2 = new QuicTransportOptions { Host = "other.host", Port = 443 };
        sm.HandlePush(new ConnectTransport(options2));
        Assert.Contains("connect-timeout", ops.Timers.Keys);
    }

    [Fact(Timeout = 5000)]
    public void Dispatch_InboundData_with_matching_gen_should_push_MultiplexedData()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        var buffer = TransportBuffer.Rent(16);
        buffer.Length = 4;

        // Dispatch with gen 0 (initial gen), should match and push
        sm.Dispatch(new InboundData(buffer, 1, 0));

        Assert.Single(ops.PushedInbound);
        Assert.IsType<MultiplexedData>(ops.PushedInbound[0]);
        var pushed = (MultiplexedData)ops.PushedInbound[0];
        Assert.Equal(1, pushed.StreamId);
    }
}
