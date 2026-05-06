using Servus.Akka.Transport;
using TurboHTTP.Protocol.Http11;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Http11;

public sealed class Http11StateMachineReconnectSpec
{
    private static HttpRequestMessage MakeRequest(string path = "/") =>
        new(HttpMethod.Get, $"http://example.com{path}")
        {
            Version = new Version(1, 1)
        };

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void Http11StateMachine_should_buffer_all_inflight_requests_and_emit_reconnect_item_on_start_reconnect()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, new TurboClientOptions { Http1 = new Http1Options { MaxPipelineDepth = 4, MaxReconnectAttempts = 3 } });
        sm.EncodeRequest(MakeRequest("/a"));
        sm.EncodeRequest(MakeRequest("/b"));
        ops.Outbound.Clear();

        sm.StartReconnect();

        Assert.True(sm.IsReconnecting);
        Assert.False(sm.HasInFlightRequests); // queue drained into buffer
        Assert.Single(ops.Outbound, item => item is ConnectTransport);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void Http11StateMachine_CanAcceptRequest_should_be_false_when_reconnecting()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, new TurboClientOptions { Http1 = new Http1Options { MaxPipelineDepth = 4, MaxReconnectAttempts = 3 } });
        sm.EncodeRequest(MakeRequest());
        sm.StartReconnect();

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void Http11StateMachine_OnConnectionRestored_should_replay_all_buffered_requests()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, new TurboClientOptions { Http1 = new Http1Options { MaxPipelineDepth = 4, MaxReconnectAttempts = 3 } });
        sm.EncodeRequest(MakeRequest("/a"));
        sm.EncodeRequest(MakeRequest("/b"));
        ops.Outbound.Clear();
        sm.StartReconnect();
        ops.Outbound.Clear(); // ignore ConnectTransport (reconnect)

        sm.OnConnectionRestored();

        Assert.False(sm.IsReconnecting);
        Assert.True(sm.HasInFlightRequests); // both re-enqueued
        // Should have emitted TransportData for each replayed request
        Assert.Equal(2, ops.Outbound.OfType<TransportData>().Count());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void Http11StateMachine_OnReconnectAttemptFailed_should_fail_when_max_exceeded()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, new TurboClientOptions { Http1 = new Http1Options { MaxPipelineDepth = 4, MaxReconnectAttempts = 1 } });
        sm.EncodeRequest(MakeRequest());
        sm.StartReconnect(); // attempt 1

        sm.OnReconnectAttemptFailed(); // attempt 2 — exceeds max of 1

        Assert.NotNull(ops.FailException);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void Http11StateMachine_OnReconnectAttemptFailed_should_emit_new_reconnect_item_when_under_limit()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, new TurboClientOptions { Http1 = new Http1Options { MaxPipelineDepth = 4, MaxReconnectAttempts = 3 } });
        sm.EncodeRequest(MakeRequest());
        sm.StartReconnect(); // attempt 1
        var countAfterFirst = ops.Outbound.OfType<ConnectTransport>().Count();

        sm.OnReconnectAttemptFailed(); // attempt 2

        Assert.False(ops.ReconnectFailed);
        Assert.Equal(countAfterFirst + 1, ops.Outbound.OfType<ConnectTransport>().Count());
    }
}