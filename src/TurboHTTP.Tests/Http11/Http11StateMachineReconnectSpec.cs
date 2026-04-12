using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http11;

namespace TurboHTTP.Tests.Http11;

public sealed class Http11StateMachineReconnectSpec
{
    private sealed class FakeOps : IHttp11StageOperations
    {
        public List<HttpResponseMessage> Responses { get; } = [];
        public List<IOutputItem> Outbound { get; } = [];
        public List<string> Warnings { get; } = [];
        public bool ReconnectFailed { get; private set; }

        public void OnResponse(HttpResponseMessage response) => Responses.Add(response);
        public void OnOutbound(IOutputItem item) => Outbound.Add(item);
        public void OnWarning(string message) => Warnings.Add(message);
        public void OnReconnectFailed() => ReconnectFailed = true;
    }

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
        var sm = new Http11StateMachine(ops, maxPipelineDepth: 4, maxReconnectAttempts: 3);
        sm.EncodeRequest(MakeRequest("/a"));
        sm.EncodeRequest(MakeRequest("/b"));
        ops.Outbound.Clear();

        sm.StartReconnect();

        Assert.True(sm.IsReconnecting);
        Assert.False(sm.HasInFlightRequests); // queue drained into buffer
        Assert.Single(ops.Outbound.OfType<ReconnectItem>());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void Http11StateMachine_CanAcceptRequest_should_be_false_when_reconnecting()
    {
        var ops = new FakeOps();
        var sm = new Http11StateMachine(ops, maxPipelineDepth: 4, maxReconnectAttempts: 3);
        sm.EncodeRequest(MakeRequest());
        sm.StartReconnect();

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void Http11StateMachine_HandleConnectedSignal_should_replay_all_buffered_requests()
    {
        var ops = new FakeOps();
        var sm = new Http11StateMachine(ops, maxPipelineDepth: 4, maxReconnectAttempts: 3);
        sm.EncodeRequest(MakeRequest("/a"));
        sm.EncodeRequest(MakeRequest("/b"));
        ops.Outbound.Clear();
        sm.StartReconnect();
        ops.Outbound.Clear(); // ignore ReconnectItem

        sm.HandleConnectedSignal();

        Assert.False(sm.IsReconnecting);
        Assert.True(sm.HasInFlightRequests); // both re-enqueued
        // Should have emitted StreamAcquireItem + NetworkBuffer for each replayed request
        Assert.Equal(2, ops.Outbound.OfType<StreamAcquireItem>().Count());
        Assert.Equal(2, ops.Outbound.OfType<NetworkBuffer>().Count());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void Http11StateMachine_HandleReconnectAttempt_should_fail_when_max_exceeded()
    {
        var ops = new FakeOps();
        var sm = new Http11StateMachine(ops, maxPipelineDepth: 4, maxReconnectAttempts: 1);
        sm.EncodeRequest(MakeRequest());
        sm.StartReconnect(); // attempt 1

        sm.HandleReconnectAttempt(); // attempt 2 — exceeds max of 1

        Assert.True(ops.ReconnectFailed);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void Http11StateMachine_HandleReconnectAttempt_should_emit_new_reconnect_item_when_under_limit()
    {
        var ops = new FakeOps();
        var sm = new Http11StateMachine(ops, maxPipelineDepth: 4, maxReconnectAttempts: 3);
        sm.EncodeRequest(MakeRequest());
        sm.StartReconnect(); // attempt 1
        var countAfterFirst = ops.Outbound.OfType<ReconnectItem>().Count();

        sm.HandleReconnectAttempt(); // attempt 2

        Assert.False(ops.ReconnectFailed);
        Assert.Equal(countAfterFirst + 1, ops.Outbound.OfType<ReconnectItem>().Count());
    }
}
