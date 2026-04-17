using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http11;
using TurboHTTP.Tests.Shared;
using TurboHTTP.Transport.Connection;
using TurboHTTP.Transport.Quic;
using Quic = TurboHTTP.Transport.Quic;
using TurboHTTP.Transport.Tcp;

namespace TurboHTTP.StreamTests.Transport;

public sealed class QuicTransportStateMachineSpec
{
    private sealed class MockTransportOperations : ITransportOperations
    {
        public List<IInputItem> PushedOutputs { get; } = [];
        public int PullInputCount { get; set; }
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
        Scheme = "https",
        Host = "localhost",
        Port = 443,
        Version = HttpVersion.Version30
    };

    private static readonly QuicOptions TestQuicOptions = new()
    {
        Host = "localhost",
        Port = 443
    };

    private static (QuicTransportStateMachine Sm, MockTransportOperations Ops) CreateStateMachine(
        bool allowConnectionMigration = true)
    {
        var ops = new MockTransportOperations();
        var sm = new QuicTransportStateMachine(
            ops,
            ActorRefs.Nobody,
            ActorRefs.Nobody,
            new TurboClientOptions(),
            allowConnectionMigration);
        return (sm, ops);
    }

    private static QuicConnectionLease CreateTestQuicLease()
    {
        var provider = new FakeClientProvider();
        var handle = new QuicConnectionHandle(provider, TestQuicOptions, TestEndpoint);
        return new QuicConnectionLease(handle);
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

    // --- Dispatch: InboundData ---

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void Dispatch_InboundData_should_push_output_when_gen_matches()
    {
        var (sm, ops) = CreateStateMachine();
        var buffer = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        buffer.Key = TestEndpoint;

        sm.Dispatch(new InboundData(buffer, 0));

        Assert.Single(ops.PushedOutputs);
        Assert.Same(buffer, ops.PushedOutputs[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void Dispatch_InboundData_should_ignore_stale_generation()
    {
        var (sm, ops) = CreateStateMachine();
        var buffer = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        buffer.Key = TestEndpoint;

        sm.Dispatch(new InboundData(buffer, 99));

        Assert.Empty(ops.PushedOutputs);
    }

    // --- Dispatch: OutboundWriteDone ---

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void Dispatch_OutboundWriteDone_should_signal_pull_input()
    {
        var (sm, ops) = CreateStateMachine();

        sm.Dispatch(new Quic.OutboundWriteDone());

        Assert.Equal(1, ops.PullInputCount);
    }

    // --- Dispatch: OutboundWriteFailed ---

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void Dispatch_OutboundWriteFailed_should_push_quic_close_item()
    {
        var (sm, ops) = CreateStateMachine();

        sm.Dispatch(new Quic.OutboundWriteFailed(new IOException("write failed")));

        Assert.Contains(ops.PushedOutputs, item => item is QuicCloseItem { Kind: QuicCloseKind.WriteFailed });
    }

    // --- Dispatch: AcquisitionFailed ---

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void Dispatch_AcquisitionFailed_should_cancel_connect_timer()
    {
        var (sm, ops) = CreateStateMachine();

        var connectItem = new ConnectItem(TestQuicOptions) { Key = TestEndpoint };
        sm.HandlePush(connectItem);
        ops.CancelledTimers.Clear();

        sm.Dispatch(new Quic.AcquisitionFailed(new Exception("failed")));

        Assert.Contains("connect-timeout", ops.CancelledTimers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void Dispatch_AcquisitionFailed_should_push_close_and_pull()
    {
        var (sm, ops) = CreateStateMachine();

        var connectItem = new ConnectItem(TestQuicOptions) { Key = TestEndpoint };
        sm.HandlePush(connectItem);
        ops.PushedOutputs.Clear();
        ops.PullInputCount = 0;

        sm.Dispatch(new Quic.AcquisitionFailed(new Exception("failed")));

        Assert.Contains(ops.PushedOutputs, item => item is QuicCloseItem { Kind: QuicCloseKind.AcquisitionFailed });
        Assert.True(ops.PullInputCount > 0);
    }

    // --- Dispatch: InboundComplete ---

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void Dispatch_InboundComplete_clean_should_push_request_stream_complete()
    {
        var (sm, ops) = CreateStateMachine();

        sm.Dispatch(new Quic.InboundComplete(TlsCloseKind.CleanClose, 0, StreamId: 1));

        Assert.Contains(ops.PushedOutputs,
            item => item is QuicCloseItem { Kind: QuicCloseKind.RequestStreamComplete });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void Dispatch_InboundComplete_abrupt_should_push_connection_failure()
    {
        var (sm, ops) = CreateStateMachine();

        sm.Dispatch(new Quic.InboundComplete(TlsCloseKind.AbruptClose, 0, StreamId: 1));

        Assert.Contains(ops.PushedOutputs,
            item => item is QuicCloseItem { Kind: QuicCloseKind.ConnectionFailure });
    }

    // --- Dispatch: InboundPumpFailed ---

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void Dispatch_InboundPumpFailed_should_treat_as_abrupt_close()
    {
        var (sm, ops) = CreateStateMachine();

        sm.Dispatch(new Quic.InboundPumpFailed(new IOException("pump failed"), StreamId: 1));

        Assert.Contains(ops.PushedOutputs,
            item => item is QuicCloseItem { Kind: QuicCloseKind.ConnectionFailure });
    }

    // --- Dispatch: ConnectionMigrated ---

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9000-9")]
    public void Dispatch_ConnectionMigrated_should_allow_when_migration_enabled()
    {
        var (sm, ops) = CreateStateMachine(allowConnectionMigration: true);
        var old = new IPEndPoint(IPAddress.Loopback, 1000);
        var @new = new IPEndPoint(IPAddress.Loopback, 2000);

        sm.Dispatch(new ConnectionMigrated(old, @new));

        Assert.Empty(ops.PushedOutputs);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9000-9")]
    public void Dispatch_ConnectionMigrated_should_push_close_when_migration_disabled()
    {
        var (sm, ops) = CreateStateMachine(allowConnectionMigration: false);
        var old = new IPEndPoint(IPAddress.Loopback, 1000);
        var @new = new IPEndPoint(IPAddress.Loopback, 2000);

        sm.Dispatch(new ConnectionMigrated(old, @new));

        Assert.Contains(ops.PushedOutputs,
            item => item is QuicCloseItem { Kind: QuicCloseKind.MigrationDisallowed });
    }

    // --- HandlePush: ConnectItem ---

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void HandlePush_ConnectItem_should_schedule_connect_timeout()
    {
        var (sm, ops) = CreateStateMachine();

        var connectItem = new ConnectItem(TestQuicOptions) { Key = TestEndpoint };
        sm.HandlePush(connectItem);

        Assert.Contains(ops.ScheduledTimers, t => t.Key == "connect-timeout");
    }

    // --- HandlePush: Http3NetworkBuffer (tagged) ---

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void HandlePush_tagged_buffer_should_signal_pull_when_no_connection()
    {
        var (sm, ops) = CreateStateMachine();

        var dataItem = Http3NetworkBuffer.Rent(4);
        dataItem.StreamType = Http3StreamType.Request;
        dataItem.StreamId = 1;
        dataItem.Length = 3;
        dataItem.Key = TestEndpoint;

        sm.HandlePush(dataItem);

        Assert.True(ops.PullInputCount > 0);
    }

    // --- HandlePush: NetworkBuffer (untagged) ---

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void HandlePush_untagged_buffer_should_signal_pull_when_no_streams()
    {
        var (sm, ops) = CreateStateMachine();

        var buffer = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        buffer.Key = TestEndpoint;

        sm.HandlePush(buffer);

        Assert.True(ops.PullInputCount > 0);
    }

    // --- HandlePush: Http3EndOfRequestItem ---

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void HandlePush_EndOfRequest_should_signal_pull()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new Http3EndOfRequestItem { Key = TestEndpoint, StreamId = 1 });

        Assert.True(ops.PullInputCount > 0);
    }

    // --- HandlePush: ConnectionReuseItem ---

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void HandlePush_ConnectionReuseItem_should_signal_pull()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new ConnectionReuseItem(ConnectionReuseDecision.KeepAlive("reuse")) { Key = TestEndpoint });

        Assert.Equal(1, ops.PullInputCount);
    }

    // --- HandlePush: StreamAcquireItem ---

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void HandlePush_StreamAcquireItem_should_signal_pull()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new StreamAcquireItem { Key = TestEndpoint });

        Assert.Equal(1, ops.PullInputCount);
    }

    // --- HandlePush: MaxConcurrentStreamsItem ---

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void HandlePush_MaxConcurrentStreamsItem_should_signal_pull()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandlePush(new MaxConcurrentStreamsItem(100) { Key = TestEndpoint });

        Assert.Equal(1, ops.PullInputCount);
    }

    // --- HandleUpstreamFinish ---

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void HandleUpstreamFinish_should_complete_stage()
    {
        var (sm, ops) = CreateStateMachine();

        sm.HandleUpstreamFinish();

        Assert.Equal(1, ops.CompleteStageCount);
    }

    // --- OnTimer ---

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void OnTimer_connect_timeout_should_push_acquisition_failed_close()
    {
        var (sm, ops) = CreateStateMachine();

        var connectItem = new ConnectItem(TestQuicOptions) { Key = TestEndpoint };
        sm.HandlePush(connectItem);
        ops.PushedOutputs.Clear();
        ops.PullInputCount = 0;

        sm.OnTimer("connect-timeout");

        Assert.Contains(ops.PushedOutputs,
            item => item is QuicCloseItem { Kind: QuicCloseKind.AcquisitionFailed });
        Assert.True(ops.PullInputCount > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void OnTimer_unknown_key_should_be_noop()
    {
        var (sm, ops) = CreateStateMachine();

        sm.OnTimer("unknown-timer");

        Assert.Empty(ops.PushedOutputs);
        Assert.Equal(0, ops.PullInputCount);
    }

    // --- PostStop ---

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void PostStop_should_cancel_connect_timer()
    {
        var (sm, ops) = CreateStateMachine();

        sm.PostStop();

        Assert.Contains("connect-timeout", ops.CancelledTimers);
    }

    // --- EarlyDataRejected ---

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void Dispatch_EarlyDataRejected_should_signal_pull()
    {
        var (sm, ops) = CreateStateMachine();
        var buffer = NetworkBufferTestExtensions.FromArray([1, 2, 3]);

        sm.Dispatch(new EarlyDataRejected(buffer));

        Assert.True(ops.PullInputCount > 0);
    }
}
