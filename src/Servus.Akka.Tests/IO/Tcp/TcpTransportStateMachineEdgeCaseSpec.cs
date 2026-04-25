using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using Servus.Akka.IO;
using Servus.Akka.IO.Tcp;
using Servus.Akka.Tests.Utils;

namespace Servus.Akka.Tests.IO.Tcp;

public sealed class TcpTransportStateMachineEdgeCaseSpec
{
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
    public void FlushNext_without_handle_should_dispose_orphaned_buffers()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new ConnectItem(TestTcpOptions) { Key = TestEndpoint });

        var buf1 = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        var buf2 = NetworkBufferTestExtensions.FromArray([4, 5, 6]);
        sm.HandlePush(buf1);
        sm.HandlePush(buf2);

        sm.HandleDownstreamFinish();

        sm.Dispatch(new FlushNextCompleted());

        Assert.True(ops.PullInputCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void PostStop_with_active_lease_should_dispose_lease()
    {
        var (sm, _) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        sm.PostStop();

        Assert.False(lease.IsAlive);
    }

    [Fact(Timeout = 5000)]
    public void HandleUpstreamFinish_with_pending_responses_and_writes_should_defer()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        sm.HandlePush(new StreamAcquireItem { Key = TestEndpoint });

        sm.HandleUpstreamFinish();

        Assert.Equal(0, ops.CompleteStageCount);
    }

    [Fact(Timeout = 5000)]
    public void Reconnect_sequence_should_cleanup_old_and_acquire_new()
    {
        var (sm, ops) = CreateStateMachine();
        var lease1 = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease1));

        sm.HandlePush(new ConnectItem(TestTcpOptions)
        { Key = TestEndpoint, IsReconnect = true });

        Assert.False(lease1.IsAlive);

        var lease2 = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease2));

        Assert.Contains(ops.PushedOutputs, item => item is ConnectedSignalItem);
    }

    [Fact(Timeout = 5000)]
    public void Multiple_reconnects_should_dispose_intermediate_leases()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new ConnectItem(TestTcpOptions) { Key = TestEndpoint });
        var lease1 = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease1));

        sm.HandlePush(new ConnectItem(TestTcpOptions)
        { Key = TestEndpoint, IsReconnect = true });
        Assert.False(lease1.IsAlive);

        var lease2 = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease2));

        sm.HandlePush(new ConnectItem(TestTcpOptions)
        { Key = TestEndpoint, IsReconnect = true });
        Assert.False(lease2.IsAlive);

        var lease3 = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease3));

        Assert.True(lease3.IsAlive);
    }

    [Fact(Timeout = 5000)]
    public void HandleConnectionReuseItem_canReuse_false_should_null_handle()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        sm.HandlePush(new StreamAcquireItem { Key = TestEndpoint });

        sm.HandlePush(new ConnectionReuseItem(false) { Key = TestEndpoint });

        var buf = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        sm.HandlePush(buf);

        Assert.True(ops.PullInputCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void OnTimer_null_key_should_be_ignored()
    {
        var (sm, ops) = CreateStateMachine();

        sm.OnTimer(null);

        Assert.Empty(ops.PushedOutputs);
        Assert.Equal(0, ops.CompleteStageCount);
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_MaxConcurrentStreamsItem_without_lease_should_pull()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new MaxConcurrentStreamsItem(42) { Key = TestEndpoint });

        Assert.True(ops.PullInputCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_StreamAcquireItem_without_lease_should_pull()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new StreamAcquireItem { Key = TestEndpoint });

        Assert.True(ops.PullInputCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void Dispatch_AcquisitionFailed_without_pending_connect_should_not_push()
    {
        var (sm, ops) = CreateStateMachine();

        sm.Dispatch(new AcquisitionFailed(new IOException("no pending connect")));

        Assert.Empty(ops.PushedOutputs);
    }

    [Fact(Timeout = 5000)]
    public void HandleUpstreamFinish_after_no_reuse_should_complete_stage()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        sm.HandlePush(new StreamAcquireItem { Key = TestEndpoint });
        sm.HandlePush(new ConnectionReuseItem(false) { Key = TestEndpoint });

        sm.HandleUpstreamFinish();

        Assert.Equal(1, ops.CompleteStageCount);
    }

    [Fact(Timeout = 5000)]
    public void Dispatch_InboundComplete_clean_close_should_signal_and_pull()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));
        ops.PushedOutputs.Clear();
        var pullBefore = ops.PullInputCount;

        sm.Dispatch(new InboundComplete(TlsCloseKind.CleanClose, 1));

        Assert.Contains(ops.PushedOutputs, item => item is CloseSignalItem { CloseKind: TlsCloseKind.CleanClose });
        Assert.True(ops.PullInputCount > pullBefore);
    }

    [Fact(Timeout = 5000)]
    public void Dispatch_InboundComplete_abrupt_close_should_signal_and_pull()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));
        ops.PushedOutputs.Clear();
        var pullBefore = ops.PullInputCount;

        sm.Dispatch(new InboundComplete(TlsCloseKind.AbruptClose, 1));

        Assert.Contains(ops.PushedOutputs, item => item is CloseSignalItem { CloseKind: TlsCloseKind.AbruptClose });
        Assert.True(ops.PullInputCount > pullBefore);
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_ConnectItem_with_zero_timeout_should_use_default()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new ConnectItem(new TcpOptions
        {
            Host = "localhost",
            Port = 8080,
            ConnectTimeout = TimeSpan.Zero
        })
        { Key = TestEndpoint });

        var timer = Assert.Single(ops.ScheduledTimers, t => t.Key == "connect-timeout");
        Assert.Equal(TimeSpan.FromSeconds(10), timer.Delay);
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_ConnectItem_with_custom_timeout_should_use_it()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new ConnectItem(new TcpOptions
        {
            Host = "localhost",
            Port = 8080,
            ConnectTimeout = TimeSpan.FromSeconds(30)
        })
        { Key = TestEndpoint });

        var timer = Assert.Single(ops.ScheduledTimers, t => t.Key == "connect-timeout");
        Assert.Equal(TimeSpan.FromSeconds(30), timer.Delay);
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_ConnectItem_with_negative_timeout_should_use_default()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new ConnectItem(new TcpOptions
        {
            Host = "localhost",
            Port = 8080,
            ConnectTimeout = TimeSpan.FromSeconds(-1)
        })
        { Key = TestEndpoint });

        var timer = Assert.Single(ops.ScheduledTimers, t => t.Key == "connect-timeout");
        Assert.Equal(TimeSpan.FromSeconds(10), timer.Delay);
    }
}
