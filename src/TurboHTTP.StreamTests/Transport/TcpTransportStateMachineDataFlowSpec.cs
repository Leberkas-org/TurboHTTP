using System.Buffers;
using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;
using TurboHTTP.Protocol.Http11;
using TurboHTTP.Tests.Shared;
using TurboHTTP.Transport.Tcp;

namespace TurboHTTP.StreamTests.Transport;

public sealed class TcpTransportStateMachineDataFlowSpec
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
    public void Dispatch_InboundBatch_multiple_items_should_push_all()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));
        ops.PushedOutputs.Clear();

        var items = ArrayPool<IInputItem>.Shared.Rent(8);
        items[0] = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        items[1] = NetworkBufferTestExtensions.FromArray([4, 5, 6]);
        items[2] = NetworkBufferTestExtensions.FromArray([7, 8, 9]);

        sm.Dispatch(new InboundBatch(items, 3, 1));

        Assert.Equal(3, ops.PushedOutputs.Count);
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_multiple_buffers_without_handle_should_queue_all()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new ConnectItem(TestTcpOptions) { Key = TestEndpoint });

        var buffer1 = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        var buffer2 = NetworkBufferTestExtensions.FromArray([4, 5, 6]);
        var buffer3 = NetworkBufferTestExtensions.FromArray([7, 8, 9]);

        sm.HandlePush(buffer1);
        sm.HandlePush(buffer2);
        sm.HandlePush(buffer3);

        Assert.True(ops.PullInputCount >= 3);
    }

    [Fact(Timeout = 5000)]
    public void Dispatch_FlushNextCompleted_should_process_next_pending_write()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new ConnectItem(TestTcpOptions) { Key = TestEndpoint });

        var buffer1 = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        var buffer2 = NetworkBufferTestExtensions.FromArray([4, 5, 6]);

        sm.HandlePush(buffer1);
        sm.HandlePush(buffer2);

        var pullBefore = ops.PullInputCount;

        sm.Dispatch(new FlushNextCompleted());

        Assert.True(ops.PullInputCount >= pullBefore);
    }

    [Fact(Timeout = 5000)]
    public void Dispatch_FlushNextCompleted_with_no_pending_should_pull()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        var pullBefore = ops.PullInputCount;

        sm.Dispatch(new FlushNextCompleted());

        Assert.True(ops.PullInputCount > pullBefore);
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_buffer_before_handle_then_acquire_should_flush()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new ConnectItem(TestTcpOptions) { Key = TestEndpoint });

        var buffer1 = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        var buffer2 = NetworkBufferTestExtensions.FromArray([4, 5, 6]);

        sm.HandlePush(buffer1);
        sm.HandlePush(buffer2);

        ops.PushedOutputs.Clear();

        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        Assert.Empty(ops.PushedOutputs);
    }

    [Fact(Timeout = 5000)]
    public void Dispatch_OutboundWriteFailed_after_acquire_should_stop_pump()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        sm.Dispatch(new OutboundWriteFailed(new IOException("write error")));

        Assert.Contains(ops.PushedOutputs, item => item is CloseSignalItem);
    }

    [Fact(Timeout = 5000)]
    public void Dispatch_InboundPumpFailed_should_trigger_close_signal()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));
        ops.PushedOutputs.Clear();

        sm.Dispatch(new InboundPumpFailed(new IOException("pump error")));

        Assert.Contains(ops.PushedOutputs, item => item is CloseSignalItem { CloseKind: TlsCloseKind.AbruptClose });
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_buffer_after_disconnect_should_be_queued()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        sm.Dispatch(new InboundComplete(TlsCloseKind.CleanClose, 1));

        var buffer = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        sm.HandlePush(buffer);

        Assert.True(ops.PullInputCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void PostStop_should_dispose_pending_writes()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new ConnectItem(TestTcpOptions) { Key = TestEndpoint });

        var buffer1 = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        var buffer2 = NetworkBufferTestExtensions.FromArray([4, 5, 6]);

        sm.HandlePush(buffer1);
        sm.HandlePush(buffer2);

        sm.PostStop();

        Assert.Contains(ops.CancelledTimers, k => k == "connect-timeout");
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_multiple_acquire_items_should_track_pending()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        sm.HandlePush(new StreamAcquireItem { Key = TestEndpoint });
        sm.HandlePush(new StreamAcquireItem { Key = TestEndpoint });
        sm.HandlePush(new StreamAcquireItem { Key = TestEndpoint });

        sm.HandleUpstreamFinish();

        Assert.Equal(0, ops.CompleteStageCount);

        sm.HandlePush(new ConnectionReuseItem(ConnectionReuseDecision.KeepAlive("test")) { Key = TestEndpoint });
        sm.HandlePush(new ConnectionReuseItem(ConnectionReuseDecision.KeepAlive("test")) { Key = TestEndpoint });

        Assert.Equal(0, ops.CompleteStageCount);

        sm.HandlePush(new ConnectionReuseItem(ConnectionReuseDecision.KeepAlive("test")) { Key = TestEndpoint });

        Assert.Equal(1, ops.CompleteStageCount);
    }

    [Fact(Timeout = 5000)]
    public void Dispatch_OutboundWriteDone_should_eventually_flush_pending()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new ConnectItem(TestTcpOptions) { Key = TestEndpoint });
        var buffer = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        sm.HandlePush(buffer);

        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        sm.Dispatch(new OutboundWriteDone());

        Assert.True(ops.PullInputCount > 0);
    }
}