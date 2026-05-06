using Servus.Akka.Transport;
using TurboHTTP.Protocol.Http10;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Http10;

public sealed class Http10StateMachineReconnectSpec
{
    private static HttpRequestMessage MakeRequest() =>
        new(HttpMethod.Get, "http://example.com/");

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-8")]
    public void Http10StateMachine_should_buffer_request_and_emit_reconnect_item_on_disconnect_with_inflight()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, new TurboClientOptions { Http1 = new Http1Options { MaxReconnectAttempts = 3 } });
        var request = MakeRequest();
        sm.OnRequest(request);
        var initialConnectCount = ops.Outbound.OfType<ConnectTransport>().Count();
        ops.Outbound.Clear(); // ignore encode output

        // Simulate disconnect while request in flight
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.True(sm.IsReconnecting);
        Assert.False(sm.HasInFlightRequest); // buffered request is not "in-flight"
        var newConnectCount = ops.Outbound.OfType<ConnectTransport>().Count();
        Assert.Equal(1, newConnectCount); // Should emit a reconnect ConnectTransport
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-8")]
    public void Http10StateMachine_CanAcceptRequest_should_be_false_when_reconnecting()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, new TurboClientOptions { Http1 = new Http1Options { MaxReconnectAttempts = 3 } });
        sm.OnRequest(MakeRequest());
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-8")]
    public void Http10StateMachine_should_replay_buffered_request_on_reconnect_connected()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, new TurboClientOptions { Http1 = new Http1Options { MaxReconnectAttempts = 3 } });
        sm.OnRequest(MakeRequest());
        ops.Outbound.Clear();
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        ops.Outbound.Clear(); // ignore ConnectTransport (reconnect)

        sm.DecodeServerData(new TransportConnected(default!));

        Assert.False(sm.IsReconnecting);
        Assert.True(sm.HasInFlightRequest); // re-encoded, back in flight
        // Should have emitted TransportData for the replayed request
        Assert.Contains(ops.Outbound, o => o is TransportData);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-8")]
    public void Http10StateMachine_should_fail_when_max_reconnect_attempts_exceeded()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, new TurboClientOptions { Http1 = new Http1Options { MaxReconnectAttempts = 1 } });
        sm.OnRequest(MakeRequest());
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error)); // attempt 1

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error)); // attempt 2 — exceeds max of 1

        Assert.True(ops.StageCompleted);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-8")]
    public void Http10StateMachine_should_emit_new_reconnect_item_when_under_limit()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, new TurboClientOptions { Http1 = new Http1Options { MaxReconnectAttempts = 3 } });
        sm.OnRequest(MakeRequest());
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error)); // attempt 1
        var countAfterFirst = ops.Outbound.OfType<ConnectTransport>().Count();

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error)); // attempt 2

        Assert.Null(ops.FailException);
        Assert.Equal(countAfterFirst + 1, ops.Outbound.OfType<ConnectTransport>().Count());
    }
}
