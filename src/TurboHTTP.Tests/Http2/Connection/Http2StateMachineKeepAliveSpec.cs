using Servus.Akka.IO;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Http2.Connection;

public sealed class Http2StateMachineKeepAliveSpec
{
    private static TurboClientOptions MakeConfig() => new();

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void StateMachine_SendKeepAlivePing_should_emit_ping_frame()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();
        ops.Outbound.Clear();

        sm.SendKeepAlivePing();

        var ping = Assert.Single(ops.Outbound.OfType<NetworkBuffer>());
        Assert.True(ping.Length > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void StateMachine_SendKeepAlivePing_should_not_emit_duplicate_when_awaiting_ack()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();
        ops.Outbound.Clear();

        sm.SendKeepAlivePing();
        sm.SendKeepAlivePing(); // duplicate — should be ignored

        Assert.Single(ops.Outbound.OfType<NetworkBuffer>());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void StateMachine_IsKeepAliveTimedOut_should_return_false_when_no_ping_sent()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        Assert.False(sm.IsKeepAliveTimedOut(TimeSpan.FromSeconds(20)));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void StateMachine_IsKeepAliveTimedOut_should_return_false_immediately_after_ping()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.SendKeepAlivePing();

        // Immediately after sending, timeout should not have elapsed
        Assert.False(sm.IsKeepAliveTimedOut(TimeSpan.FromSeconds(20)));
    }
}