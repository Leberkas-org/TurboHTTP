using System.Buffers;
using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;
using TurboHTTP.Protocol.Http11;
using TurboHTTP.Tests.Shared;
using TurboHTTP.Transport.Tcp;

namespace TurboHTTP.StreamTests.Transport;

public sealed class TcpTransportStateMachineSpec
{
    private sealed class MockTransportOperations : ITransportOperations
    {
        public List<IInputItem> PushedOutputs { get; } = [];
        public int PullInputCount { get; private set; }
        public int CompleteStageCount { get; private set; }
        public List<(string Key, TimeSpan Delay)> ScheduledTimers { get; } = [];
        public List<string> CancelledTimers { get; } = [];

        public void OnPushOutput(IInputItem item) => PushedOutputs.Add(item);
        public void OnSignalPullInput() => PullInputCount++;
        public void OnCompleteStage() => CompleteStageCount++;
        public void OnScheduleTimer(string key, TimeSpan delay) => ScheduledTimers.Add((key, delay));
        public void OnCancelTimer(string key) => CancelledTimers.Add(key);
        public ILoggingAdapter Log { get; } = NoLogger.Instance;
    }

    private static readonly RequestEndpoint TestEndpoint = new()
    {
        Scheme = "http",
        Host = "localhost",
        Port = 8080,
        Version = HttpVersion.Version11
    };

    private static readonly TcpOptions TestTcpOptions = new()
    {
        Host = "localhost",
        Port = 8080
    };

    private static (TcpTransportStateMachine Sm, MockTransportOperations Ops) CreateStateMachine()
    {
        var ops = new MockTransportOperations();
        var sm = new TcpTransportStateMachine(
            ops,
            ActorRefs.Nobody,
            new TurboClientOptions(),
            ActorRefs.Nobody);
        return (sm, ops);
    }

    private static ConnectionLease CreateTestLease(RequestEndpoint? endpoint = null)
    {
        var key = endpoint ?? TestEndpoint;
        var inbound = Channel.CreateUnbounded<NetworkBuffer>();
        var outbound = Channel.CreateUnbounded<NetworkBuffer>();

        var handle = ConnectionHandle.CreateDirect(
            outbound.Writer,
            inbound.Reader,
            key);

        var state = new ClientState(
            Stream.Null,
            inbound,
            outbound);

        return new ConnectionLease(handle, state);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void Dispatch_LeaseAcquired_should_signal_pull_input()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();

        sm.Dispatch(new LeaseAcquired(lease));

        Assert.True(ops.PullInputCount > 0);
        Assert.Contains(ops.CancelledTimers, k => k == "connect-timeout");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void Dispatch_LeaseAcquired_with_pending_writes_should_flush()
    {
        var (sm, ops) = CreateStateMachine();

        var buffer = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        sm.HandlePush(new ConnectItem(TestTcpOptions) { Key = TestEndpoint });

        sm.HandlePush(buffer);

        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        Assert.Contains(ops.CancelledTimers, k => k == "connect-timeout");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void Dispatch_InboundBatch_should_push_output_items()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        var items = ArrayPool<IInputItem>.Shared.Rent(8);
        items[0] = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        items[1] = NetworkBufferTestExtensions.FromArray([4, 5, 6]);

        sm.Dispatch(new InboundBatch(items, 2, 1));

        Assert.Equal(2, ops.PushedOutputs.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void Dispatch_InboundBatch_stale_gen_should_be_ignored()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));
        ops.PushedOutputs.Clear();

        var items = ArrayPool<IInputItem>.Shared.Rent(8);
        items[0] = NetworkBufferTestExtensions.FromArray([1, 2, 3]);

        sm.Dispatch(new InboundBatch(items, 1, 999));

        Assert.Empty(ops.PushedOutputs);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void Dispatch_OutboundWriteDone_should_pull_next()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));
        var pullBefore = ops.PullInputCount;

        sm.Dispatch(new OutboundWriteDone());

        Assert.True(ops.PullInputCount > pullBefore);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void Dispatch_OutboundWriteFailed_should_push_close_signal()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));
        ops.PushedOutputs.Clear();

        sm.Dispatch(new OutboundWriteFailed(new IOException("write failed")));

        Assert.Contains(ops.PushedOutputs, item => item is CloseSignalItem { CloseKind: TlsCloseKind.AbruptClose });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void Dispatch_AcquisitionFailed_should_push_close_signal_and_pull()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new ConnectItem(TestTcpOptions) { Key = TestEndpoint });
        ops.PushedOutputs.Clear();
        var pullBefore = ops.PullInputCount;

        sm.Dispatch(new AcquisitionFailed(new IOException("connection refused")));

        Assert.Contains(ops.PushedOutputs, item => item is CloseSignalItem { CloseKind: TlsCloseKind.AbruptClose });
        Assert.True(ops.PullInputCount > pullBefore);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void Dispatch_AcquisitionFailed_cancelled_should_be_ignored()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new ConnectItem(TestTcpOptions) { Key = TestEndpoint });
        ops.PushedOutputs.Clear();
        var pullBefore = ops.PullInputCount;

        sm.Dispatch(new AcquisitionFailed(new OperationCanceledException()));

        Assert.Empty(ops.PushedOutputs);
        Assert.Equal(pullBefore, ops.PullInputCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void HandlePush_ConnectItem_should_schedule_connect_timeout()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new ConnectItem(TestTcpOptions) { Key = TestEndpoint });

        Assert.Contains(ops.ScheduledTimers, t => t.Key == "connect-timeout");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void HandlePush_NetworkBuffer_without_handle_should_buffer_and_pull()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new ConnectItem(TestTcpOptions) { Key = TestEndpoint });
        var pullBefore = ops.PullInputCount;

        var buffer = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        sm.HandlePush(buffer);

        Assert.True(ops.PullInputCount > pullBefore);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void HandlePush_ConnectionReuseItem_canReuse_true_should_pull()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        sm.HandlePush(new StreamAcquireItem { Key = TestEndpoint });
        var pullBefore = ops.PullInputCount;

        sm.HandlePush(new ConnectionReuseItem(ConnectionReuseDecision.KeepAlive("test")) { Key = TestEndpoint });

        Assert.True(ops.PullInputCount > pullBefore);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void HandlePush_ConnectionReuseItem_canReuse_false_should_teardown_and_pull()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        sm.HandlePush(new StreamAcquireItem { Key = TestEndpoint });
        var pullBefore = ops.PullInputCount;

        sm.HandlePush(new ConnectionReuseItem(ConnectionReuseDecision.Close("server close")) { Key = TestEndpoint });

        Assert.True(ops.PullInputCount > pullBefore);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void HandlePush_StreamAcquireItem_should_pull()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));
        var pullBefore = ops.PullInputCount;

        sm.HandlePush(new StreamAcquireItem { Key = TestEndpoint });

        Assert.True(ops.PullInputCount > pullBefore);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void HandlePush_MaxConcurrentStreamsItem_should_update_lease_and_pull()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));
        var pullBefore = ops.PullInputCount;

        sm.HandlePush(new MaxConcurrentStreamsItem(42) { Key = TestEndpoint });

        Assert.Equal(42, lease.MaxConcurrentStreams);
        Assert.True(ops.PullInputCount > pullBefore);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void HandleUpstreamFinish_without_handle_should_complete_stage()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandleUpstreamFinish();

        Assert.Equal(1, ops.CompleteStageCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void HandleUpstreamFinish_with_idle_handle_should_complete_stage()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        sm.HandleUpstreamFinish();

        Assert.Equal(1, ops.CompleteStageCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void HandleUpstreamFinish_with_pending_responses_should_defer_complete()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        sm.HandlePush(new StreamAcquireItem { Key = TestEndpoint });

        sm.HandleUpstreamFinish();

        Assert.Equal(0, ops.CompleteStageCount);

        sm.HandlePush(new ConnectionReuseItem(ConnectionReuseDecision.KeepAlive("test")) { Key = TestEndpoint });

        Assert.Equal(1, ops.CompleteStageCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void OnTimer_connect_timeout_should_push_close_signal()
    {
        var (sm, ops) = CreateStateMachine();
        sm.HandlePush(new ConnectItem(TestTcpOptions) { Key = TestEndpoint });
        ops.PushedOutputs.Clear();

        sm.OnTimer("connect-timeout");

        Assert.Contains(ops.PushedOutputs, item => item is CloseSignalItem { CloseKind: TlsCloseKind.AbruptClose });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void OnTimer_unknown_key_should_be_ignored()
    {
        var (sm, ops) = CreateStateMachine();

        sm.OnTimer("unknown-timer");

        Assert.Empty(ops.PushedOutputs);
        Assert.Equal(0, ops.CompleteStageCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void Dispatch_InboundComplete_should_push_close_signal()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));
        ops.PushedOutputs.Clear();

        sm.Dispatch(new InboundComplete(TlsCloseKind.CleanClose, 1));

        Assert.Contains(ops.PushedOutputs, item => item is CloseSignalItem { CloseKind: TlsCloseKind.CleanClose });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void Dispatch_InboundComplete_stale_gen_should_be_ignored()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));
        ops.PushedOutputs.Clear();

        sm.Dispatch(new InboundComplete(TlsCloseKind.CleanClose, 999));

        Assert.Empty(ops.PushedOutputs);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void Dispatch_InboundComplete_with_upstream_finished_should_complete_stage()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));
        sm.HandlePush(new StreamAcquireItem { Key = TestEndpoint });

        sm.HandleUpstreamFinish();
        Assert.Equal(0, ops.CompleteStageCount);

        sm.Dispatch(new InboundComplete(TlsCloseKind.CleanClose, 1));

        Assert.Equal(1, ops.CompleteStageCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void Dispatch_InboundPumpFailed_should_push_close_signal()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));
        ops.PushedOutputs.Clear();

        sm.Dispatch(new InboundPumpFailed(new IOException("pump error")));

        Assert.Contains(ops.PushedOutputs, item => item is CloseSignalItem { CloseKind: TlsCloseKind.AbruptClose });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void PostStop_should_cancel_connect_timer()
    {
        var (sm, ops) = CreateStateMachine();

        sm.PostStop();

        Assert.Contains(ops.CancelledTimers, k => k == "connect-timeout");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void HandleDownstreamFinish_should_cleanup_transport()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        sm.HandleDownstreamFinish();

        Assert.False(lease.IsAlive);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void AutoConnect_should_trigger_on_first_data_item()
    {
        var (sm, ops) = CreateStateMachine();

        var buffer = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        buffer.Key = TestEndpoint;
        sm.HandlePush(buffer);

        Assert.Contains(ops.ScheduledTimers, t => t.Key == "connect-timeout");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void Multiple_StreamAcquire_then_Reuse_should_complete_all()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        sm.HandlePush(new StreamAcquireItem { Key = TestEndpoint });
        sm.HandlePush(new StreamAcquireItem { Key = TestEndpoint });

        sm.HandlePush(new ConnectionReuseItem(ConnectionReuseDecision.KeepAlive("test")) { Key = TestEndpoint });
        sm.HandlePush(new ConnectionReuseItem(ConnectionReuseDecision.KeepAlive("test")) { Key = TestEndpoint });

        sm.HandleUpstreamFinish();
        Assert.Equal(1, ops.CompleteStageCount);
    }
}
