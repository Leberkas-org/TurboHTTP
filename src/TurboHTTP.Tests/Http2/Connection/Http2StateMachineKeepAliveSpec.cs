using Servus.Akka.Transport;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Http2.Connection;

public sealed class Http2StateMachineKeepAliveSpec
{
    private static TurboClientOptions MakeConfig()
    {
        var options = new TurboClientOptions();
        options.Http2.KeepAlivePingDelay = TimeSpan.FromSeconds(10);
        options.Http2.KeepAlivePingTimeout = TimeSpan.FromSeconds(20);
        return options;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void OnTimerFired_should_emit_ping_frame_on_keepalive_timer()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        sm.OnTimerFired("keep-alive-ping");

        Assert.Single(ops.Outbound.OfType<TransportData>());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void OnTimerFired_should_not_emit_duplicate_ping_when_awaiting_ack()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        sm.OnTimerFired("keep-alive-ping");
        sm.OnTimerFired("keep-alive-ping");

        Assert.Single(ops.Outbound.OfType<TransportData>());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void OnTimerFired_should_not_close_when_timeout_not_elapsed()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        sm.OnTimerFired("keep-alive-ping");
        sm.OnTimerFired("keep-alive-ping-timeout");

        Assert.False(ops.StageCompleted);
        Assert.Null(ops.FailException);
    }
}
