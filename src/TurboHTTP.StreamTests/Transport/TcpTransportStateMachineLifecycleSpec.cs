using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;
using TurboHTTP.Protocol.Http11;
using TurboHTTP.Tests.Shared;
using TurboHTTP.Transport.Tcp;

namespace TurboHTTP.StreamTests.Transport;

public sealed class TcpTransportStateMachineLifecycleSpec
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

    private static readonly RequestEndpoint AltEndpoint = new()
    {
        Scheme = "http",
        Host = "example.com",
        Port = 8081,
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
    public void Dispatch_LeaseAcquired_during_reconnect_should_push_connected_signal()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new ReconnectItem { Key = TestEndpoint });
        ops.PushedOutputs.Clear();

        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        Assert.Contains(ops.PushedOutputs, item => item is ConnectedSignalItem);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void Dispatch_LeaseAcquired_duplicate_should_skip()
    {
        var (sm, ops) = CreateStateMachine();
        var lease1 = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease1));
        var pullBefore = ops.PullInputCount;

        var lease2 = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease2));

        Assert.Equal(pullBefore, ops.PullInputCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void HandlePush_NetworkBuffer_with_handle_should_write_immediately()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        var pullBefore = ops.PullInputCount;

        var buffer = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        sm.HandlePush(buffer);

        Assert.Empty(ops.PushedOutputs);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void OnTimer_connect_timeout_without_pending_connect_should_be_ignored()
    {
        var (sm, ops) = CreateStateMachine();

        sm.OnTimer("connect-timeout");

        Assert.Empty(ops.PushedOutputs);
        Assert.Equal(0, ops.CompleteStageCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void HandleConnectionReuseItem_canReuse_true_with_multiple_pending_should_decrement_and_pull()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        sm.HandlePush(new StreamAcquireItem { Key = TestEndpoint });
        sm.HandlePush(new StreamAcquireItem { Key = TestEndpoint });

        var pullBefore = ops.PullInputCount;
        sm.HandlePush(new ConnectionReuseItem(ConnectionReuseDecision.KeepAlive("test")) { Key = TestEndpoint });

        Assert.True(ops.PullInputCount > pullBefore);
        Assert.Equal(0, ops.CompleteStageCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void HandleConnectionReuseItem_canReuse_true_with_single_pending_should_mark_idle()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        sm.HandlePush(new StreamAcquireItem { Key = TestEndpoint });

        var pullBefore = ops.PullInputCount;
        sm.HandlePush(new ConnectionReuseItem(ConnectionReuseDecision.KeepAlive("test")) { Key = TestEndpoint });

        Assert.True(ops.PullInputCount > pullBefore);
        Assert.Equal(0, ops.CompleteStageCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void HandleConnectionReuseItem_canReuse_true_with_upstream_finished_should_complete()
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
    public void HandleConnectionReuseItem_canReuse_false_with_upstream_finished_should_complete()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        sm.HandlePush(new StreamAcquireItem { Key = TestEndpoint });
        sm.HandleUpstreamFinish();

        Assert.Equal(0, ops.CompleteStageCount);

        sm.HandlePush(new ConnectionReuseItem(ConnectionReuseDecision.Close("server close")) { Key = TestEndpoint });

        Assert.Equal(1, ops.CompleteStageCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void AutoConnect_with_different_endpoint_should_trigger_acquire()
    {
        var (sm, ops) = CreateStateMachine();

        var buffer = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        buffer.Key = AltEndpoint;
        sm.HandlePush(buffer);

        Assert.Contains(ops.ScheduledTimers, t => t.Key == "connect-timeout");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void ReconnectItem_should_teardown_and_acquire()
    {
        var (sm, ops) = CreateStateMachine();
        var lease1 = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease1));
        ops.PushedOutputs.Clear();

        sm.HandlePush(new ReconnectItem { Key = AltEndpoint });

        Assert.Contains(ops.ScheduledTimers, t => t.Key == "connect-timeout");
        Assert.False(lease1.IsAlive);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void Dispatch_InboundComplete_should_mark_no_reuse_on_lease()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));
        ops.PushedOutputs.Clear();

        sm.Dispatch(new InboundComplete(TlsCloseKind.CleanClose, 1));

        Assert.False(lease.Reusable);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void Dispatch_OutboundWriteFailed_should_mark_no_reuse()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        sm.Dispatch(new OutboundWriteFailed(new IOException("write failed")));

        Assert.False(lease.Reusable);
    }
}
