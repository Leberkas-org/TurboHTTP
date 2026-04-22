using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http10;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Http10;

public sealed class Http10StateMachineReconnectSpec
{
    private static HttpRequestMessage MakeRequest() =>
        new(HttpMethod.Get, "http://example.com/");

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-8")]
    public void Http10StateMachine_should_buffer_request_and_emit_reconnect_item_on_start_reconnect()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, new TurboClientOptions { Http1 = new Http1Options { MaxReconnectAttempts = 3 } });
        var request = MakeRequest();
        sm.EncodeRequest(request);
        ops.Outbound.Clear(); // ignore encode output

        sm.StartReconnect();

        Assert.True(sm.IsReconnecting);
        Assert.False(sm.HasInFlightRequest);
        Assert.Single(ops.Outbound, item => item is ConnectItem c && c.IsReconnect);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-8")]
    public void Http10StateMachine_CanAcceptRequest_should_be_false_when_reconnecting()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, new TurboClientOptions { Http1 = new Http1Options { MaxReconnectAttempts = 3 } });
        sm.EncodeRequest(MakeRequest());
        sm.StartReconnect();

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-8")]
    public void Http10StateMachine_OnConnectionRestored_should_replay_buffered_request()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, new TurboClientOptions { Http1 = new Http1Options { MaxReconnectAttempts = 3 } });
        sm.EncodeRequest(MakeRequest());
        ops.Outbound.Clear();
        sm.StartReconnect();
        ops.Outbound.Clear(); // ignore ConnectItem (reconnect)

        sm.OnConnectionRestored();

        Assert.False(sm.IsReconnecting);
        Assert.True(sm.HasInFlightRequest); // re-encoded, back in flight
        // Should have emitted StreamAcquireItem + NetworkBuffer for the replayed request
        Assert.Contains(ops.Outbound, o => o is StreamAcquireItem);
        Assert.Contains(ops.Outbound, o => o is NetworkBuffer);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-8")]
    public void Http10StateMachine_OnReconnectAttemptFailed_should_fail_when_max_exceeded()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, new TurboClientOptions { Http1 = new Http1Options { MaxReconnectAttempts = 1 } });
        sm.EncodeRequest(MakeRequest());
        sm.StartReconnect(); // attempt 1

        sm.OnReconnectAttemptFailed(); // attempt 2 — exceeds max of 1

        Assert.True(ops.ReconnectFailed);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-8")]
    public void Http10StateMachine_OnReconnectAttemptFailed_should_emit_new_reconnect_item_when_under_limit()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, new TurboClientOptions { Http1 = new Http1Options { MaxReconnectAttempts = 3 } });
        sm.EncodeRequest(MakeRequest());
        sm.StartReconnect(); // attempt 1
        var countAfterFirst = ops.Outbound.OfType<ConnectItem>().Count(c => c.IsReconnect);

        sm.OnReconnectAttemptFailed(); // attempt 2

        Assert.False(ops.ReconnectFailed);
        Assert.Equal(countAfterFirst + 1, ops.Outbound.OfType<ConnectItem>().Count(c => c.IsReconnect));
    }
}