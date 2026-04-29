using System.Buffers;
using Akka.Actor;
using Servus.Akka.Transport;
using Servus.Akka.Transport.Tcp;

namespace Servus.Akka.Tests.Transport.Tcp;

public sealed class TcpTransportStateMachineSpec
{
    private static readonly TcpTransportOptions TestOptions = new()
    {
        Host = "localhost",
        Port = 8080
    };

    private static readonly IPoolingStrategy TestStrategy = new TestPoolingStrategy();

    private static (TcpTransportStateMachine Sm, MockTransportOperations Ops) CreateStateMachine()
    {
        var ops = new MockTransportOperations();
        var sm = new TcpTransportStateMachine(
            ops,
            ActorRefs.Nobody,
            TestStrategy,
            ActorRefs.Nobody);
        return (sm, ops);
    }

    private static ConnectionLease CreateTestLease()
    {
        var state = new ClientState(Stream.Null);
        var cts = new CancellationTokenSource();
        var handle = new ConnectionHandle(state.OutboundWriter, state.InboundReader, cts.Token);
        return new ConnectionLease(handle, state, cts);
    }

    private static TransportBuffer CreateTestBuffer(params byte[] data)
    {
        var buf = TransportBuffer.Rent(data.Length);
        data.CopyTo(buf.FullMemory.Span);
        buf.Length = data.Length;
        return buf;
    }

    [Fact(Timeout = 5000)]
    public void Dispatch_LeaseAcquired_should_signal_pull_outbound()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();

        sm.Dispatch(new LeaseAcquired(lease));

        Assert.True(ops.PullOutboundCount > 0);
        Assert.Contains(ops.CancelledTimers, k => k == "connect-timeout");
    }

    [Fact(Timeout = 5000)]
    public void Dispatch_LeaseAcquired_with_pending_writes_should_flush()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new ConnectTransport(TestOptions));

        var buffer = CreateTestBuffer(1, 2, 3);
        sm.HandlePush(new TransportData(buffer));

        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        Assert.Contains(ops.CancelledTimers, k => k == "connect-timeout");
    }

    [Fact(Timeout = 5000)]
    public void Dispatch_InboundBatch_should_push_inbound_items()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));
        ops.PushedInbound.Clear();

        var items = ArrayPool<ITransportInbound>.Shared.Rent(8);
        items[0] = new TransportData(CreateTestBuffer(1, 2, 3));
        items[1] = new TransportData(CreateTestBuffer(4, 5, 6));

        sm.Dispatch(new InboundBatch(items, 2, 1));

        Assert.Equal(2, ops.PushedInbound.Count);
    }

    [Fact(Timeout = 5000)]
    public void Dispatch_InboundBatch_stale_gen_should_be_ignored()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));
        ops.PushedInbound.Clear();

        var items = ArrayPool<ITransportInbound>.Shared.Rent(8);
        items[0] = new TransportData(CreateTestBuffer(1, 2, 3));

        sm.Dispatch(new InboundBatch(items, 1, 999));

        Assert.Empty(ops.PushedInbound);
    }

    [Fact(Timeout = 5000)]
    public void Dispatch_AcquisitionFailed_should_push_disconnected_and_pull()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new ConnectTransport(TestOptions));
        ops.PushedInbound.Clear();
        var pullBefore = ops.PullOutboundCount;

        sm.Dispatch(new AcquisitionFailed(new IOException("connection refused")));

        Assert.Contains(ops.PushedInbound, item => item is TransportDisconnected { Reason: DisconnectReason.Error });
        Assert.True(ops.PullOutboundCount > pullBefore);
    }

    [Fact(Timeout = 5000)]
    public void Dispatch_AcquisitionFailed_cancelled_should_be_ignored()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new ConnectTransport(TestOptions));
        ops.PushedInbound.Clear();
        var pullBefore = ops.PullOutboundCount;

        sm.Dispatch(new AcquisitionFailed(new OperationCanceledException()));

        Assert.Empty(ops.PushedInbound);
        Assert.Equal(pullBefore, ops.PullOutboundCount);
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_ConnectTransport_should_schedule_connect_timeout()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new ConnectTransport(TestOptions));

        Assert.Contains(ops.ScheduledTimers, t => t.Key == "connect-timeout");
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_TransportData_without_handle_should_buffer_and_pull()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new ConnectTransport(TestOptions));
        var pullBefore = ops.PullOutboundCount;

        var buffer = CreateTestBuffer(1, 2, 3);
        sm.HandlePush(new TransportData(buffer));

        Assert.True(ops.PullOutboundCount > pullBefore);
    }

    [Fact(Timeout = 5000)]
    public void HandleUpstreamFinish_without_handle_should_complete_stage()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandleUpstreamFinish();

        Assert.Equal(1, ops.CompleteStageCount);
    }

    [Fact(Timeout = 5000)]
    public void HandleUpstreamFinish_with_idle_handle_should_complete_stage()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        sm.HandleUpstreamFinish();

        Assert.Equal(1, ops.CompleteStageCount);
    }

    [Fact(Timeout = 5000)]
    public void OnTimer_connect_timeout_should_push_disconnected()
    {
        var (sm, ops) = CreateStateMachine();
        sm.HandlePush(new ConnectTransport(TestOptions));
        ops.PushedInbound.Clear();

        sm.OnTimer("connect-timeout");

        Assert.Contains(ops.PushedInbound, item => item is TransportDisconnected { Reason: DisconnectReason.Timeout });
    }

    [Fact(Timeout = 5000)]
    public void OnTimer_unknown_key_should_be_ignored()
    {
        var (sm, ops) = CreateStateMachine();

        sm.OnTimer("unknown-timer");

        Assert.Empty(ops.PushedInbound);
        Assert.Equal(0, ops.CompleteStageCount);
    }

    [Fact(Timeout = 5000)]
    public void Dispatch_InboundComplete_should_push_disconnected()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));
        ops.PushedInbound.Clear();

        sm.Dispatch(new InboundComplete(DisconnectReason.Graceful, 1));

        Assert.Contains(ops.PushedInbound, item => item is TransportDisconnected { Reason: DisconnectReason.Graceful });
    }

    [Fact(Timeout = 5000)]
    public void Dispatch_InboundComplete_stale_gen_should_be_ignored()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));
        ops.PushedInbound.Clear();

        sm.Dispatch(new InboundComplete(DisconnectReason.Graceful, 999));

        Assert.Empty(ops.PushedInbound);
    }

    [Fact(Timeout = 5000)]
    public void Dispatch_InboundComplete_with_upstream_finished_should_complete_stage()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        sm.HandleUpstreamFinish();

        sm.Dispatch(new InboundComplete(DisconnectReason.Graceful, 1));

        Assert.Equal(1, ops.CompleteStageCount);
    }

    [Fact(Timeout = 5000)]
    public void Dispatch_InboundPumpFailed_should_push_disconnected()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));
        ops.PushedInbound.Clear();

        sm.Dispatch(new InboundPumpFailed(new IOException("pump error")));

        Assert.Contains(ops.PushedInbound, item => item is TransportDisconnected { Reason: DisconnectReason.Error });
    }

    [Fact(Timeout = 5000)]
    public void Dispatch_OutboundWriteFailed_should_push_disconnected()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));
        ops.PushedInbound.Clear();

        sm.Dispatch(new OutboundWriteFailed(new IOException("write failed")));

        Assert.Contains(ops.PushedInbound, item => item is TransportDisconnected { Reason: DisconnectReason.Error });
    }

    [Fact(Timeout = 5000)]
    public void PostStop_should_cancel_connect_timer()
    {
        var (sm, ops) = CreateStateMachine();

        sm.PostStop();

        Assert.Contains(ops.CancelledTimers, k => k == "connect-timeout");
    }

    [Fact(Timeout = 5000)]
    public void HandleDownstreamFinish_should_cleanup_transport()
    {
        var (sm, _) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        sm.HandleDownstreamFinish();

        Assert.False(lease.IsAlive());
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_DisconnectTransport_should_cleanup_and_pull()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));
        var pullBefore = ops.PullOutboundCount;

        sm.HandlePush(new DisconnectTransport(DisconnectReason.Graceful));

        Assert.True(ops.PullOutboundCount > pullBefore);
        Assert.False(lease.IsAlive());
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_ConnectTransport_with_existing_lease_should_reconnect()
    {
        var (sm, ops) = CreateStateMachine();
        var lease1 = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease1));

        sm.HandlePush(new ConnectTransport(TestOptions));

        Assert.False(lease1.IsAlive());
        Assert.Contains(ops.ScheduledTimers, t => t.Key == "connect-timeout");
    }

    private sealed class TestPoolingStrategy : IPoolingStrategy
    {
        public int MaxConnectionsPerHost => 6;
        public TimeSpan IdleTimeout => TimeSpan.FromSeconds(5);
        public TimeSpan ConnectionLifetime => Timeout.InfiniteTimeSpan;

        public bool CanReuse(TransportOptions options) => true;
        public PoolAction OnRelease(TransportOptions options) => PoolAction.Reuse;
        public PoolAction OnIdle(object lease) => PoolAction.Dispose;
        public PoolAction OnDisconnect(object lease, DisconnectReason reason) => PoolAction.Dispose;
        public PoolAction OnUpstreamFinish(object lease) => PoolAction.Reuse;
    }
}
