using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Akka.Actor;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;
using TurboHTTP.Tests.Shared;
using TurboHTTP.Transport.Tcp;

namespace TurboHTTP.StreamTests.Transport;

public sealed class TcpTransportStateMachineErrorSpec
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
    public void Dispatch_AcquisitionFailed_with_socket_exception_should_signal()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new ConnectItem(TestTcpOptions) { Key = TestEndpoint });
        ops.PushedOutputs.Clear();

        var ex = new SocketException(10061);
        sm.Dispatch(new AcquisitionFailed(ex));

        Assert.Contains(ops.PushedOutputs, item => item is CloseSignalItem { CloseKind: TlsCloseKind.AbruptClose });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void Dispatch_AcquisitionFailed_cancelled_without_pending_connect_should_be_ignored()
    {
        var (sm, ops) = CreateStateMachine();

        ops.PushedOutputs.Clear();
        var pullBefore = ops.PullInputCount;

        sm.Dispatch(new AcquisitionFailed(new OperationCanceledException()));

        Assert.Empty(ops.PushedOutputs);
        Assert.Equal(pullBefore, ops.PullInputCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void Dispatch_OutboundWriteFailed_should_push_abrupt_close()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));
        ops.PushedOutputs.Clear();

        var ex = new IOException("Network is unreachable");
        sm.Dispatch(new OutboundWriteFailed(ex));

        Assert.Contains(ops.PushedOutputs, item => item is CloseSignalItem { CloseKind: TlsCloseKind.AbruptClose });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void Dispatch_InboundPumpFailed_should_push_abrupt_close()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));
        ops.PushedOutputs.Clear();

        var ex = new IOException("Inbound read failed");
        sm.Dispatch(new InboundPumpFailed(ex));

        Assert.Contains(ops.PushedOutputs, item => item is CloseSignalItem { CloseKind: TlsCloseKind.AbruptClose });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void OnTimer_connect_timeout_should_cancel_timer_and_signal()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new ConnectItem(TestTcpOptions) { Key = TestEndpoint });
        ops.CancelledTimers.Clear();
        ops.PushedOutputs.Clear();

        sm.OnTimer("connect-timeout");

        Assert.Empty(ops.CancelledTimers);
        Assert.Contains(ops.PushedOutputs, item => item is CloseSignalItem { CloseKind: TlsCloseKind.AbruptClose });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void Dispatch_AcquisitionFailed_with_timeout_exception_should_signal()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new ConnectItem(TestTcpOptions) { Key = TestEndpoint });
        ops.PushedOutputs.Clear();

        var ex = new TimeoutException("Connection timeout");
        sm.Dispatch(new AcquisitionFailed(ex));

        Assert.Contains(ops.PushedOutputs, item => item is CloseSignalItem { CloseKind: TlsCloseKind.AbruptClose });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void Dispatch_InboundBatch_stale_gen_should_return_to_pool()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        var items = ArrayPool<IInputItem>.Shared.Rent(8);
        items[0] = NetworkBufferTestExtensions.FromArray([1, 2, 3]);

        ops.PushedOutputs.Clear();

        sm.Dispatch(new InboundBatch(items, 1, 999));

        Assert.Empty(ops.PushedOutputs);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void Dispatch_InboundComplete_stale_gen_should_not_push_signal()
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
    public void HandleDownstreamFinish_should_cleanup_and_null_lease()
    {
        var (sm, _) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        sm.HandleDownstreamFinish();

        Assert.False(lease.IsAlive);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void Dispatch_OutboundWriteFailed_with_aggregate_exception_should_extract_base()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));
        ops.PushedOutputs.Clear();

        var innerEx = new IOException("Inner write error");
        var aggEx = new AggregateException("Aggregate", innerEx);

        sm.Dispatch(new OutboundWriteFailed(aggEx));

        Assert.Contains(ops.PushedOutputs, item => item is CloseSignalItem { CloseKind: TlsCloseKind.AbruptClose });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void HandleUpstreamFinish_then_Dispatch_InboundComplete_should_complete_immediately()
    {
        var (sm, ops) = CreateStateMachine();
        var lease = CreateTestLease();
        sm.Dispatch(new LeaseAcquired(lease));

        sm.HandleUpstreamFinish();

        Assert.Equal(1, ops.CompleteStageCount);

        var completeBefore = ops.CompleteStageCount;

        sm.Dispatch(new InboundComplete(TlsCloseKind.CleanClose, 1));

        Assert.Equal(completeBefore, ops.CompleteStageCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void PostStop_with_pending_writes_should_dispose_all()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new ConnectItem(TestTcpOptions) { Key = TestEndpoint });

        var buf1 = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        var buf2 = NetworkBufferTestExtensions.FromArray([4, 5, 6]);
        var buf3 = NetworkBufferTestExtensions.FromArray([7, 8, 9]);

        sm.HandlePush(buf1);
        sm.HandlePush(buf2);
        sm.HandlePush(buf3);

        sm.PostStop();

        Assert.Contains(ops.CancelledTimers, k => k == "connect-timeout");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void Dispatch_multiple_events_in_sequence_should_maintain_state()
    {
        var (sm, ops) = CreateStateMachine();
        var lease1 = CreateTestLease();

        sm.Dispatch(new LeaseAcquired(lease1));

        var batch = ArrayPool<IInputItem>.Shared.Rent(8);
        batch[0] = NetworkBufferTestExtensions.FromArray([1, 2, 3]);

        sm.Dispatch(new InboundBatch(batch, 1, 1));

        Assert.Single(ops.PushedOutputs);

        sm.Dispatch(new InboundComplete(TlsCloseKind.CleanClose, 1));

        Assert.Equal(2, ops.PushedOutputs.Count);
    }
}