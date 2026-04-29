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
    public void HandlePush_OpenStream_should_enqueue_when_not_connected()
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
}
