using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Tests.Http2.Connection;

public sealed class Http2StateMachineReconnectSpec
{
    private sealed class FakeOps : IHttp2StageOperations
    {
        public List<HttpResponseMessage> Responses { get; } = [];
        public List<IOutputItem> Outbound { get; } = [];
        public List<string> Warnings { get; } = [];
        public bool ReconnectFailed { get; private set; }

        public void OnResponse(HttpResponseMessage r) => Responses.Add(r);
        public void OnOutbound(IOutputItem item) => Outbound.Add(item);
        public void OnWarning(string msg) => Warnings.Add(msg);
        public void OnReconnectFailed() => ReconnectFailed = true;
    }

    private static HttpRequestMessage MakeGet(string path = "/") =>
        new(HttpMethod.Get, $"https://example.com{path}");

    private static HttpRequestMessage MakePost(string path = "/") =>
        new(HttpMethod.Post, $"https://example.com{path}");

    private static Http2ConnectionConfig MakeConfig(int maxReconnect = 3) =>
        new(MaxReconnectAttempts: maxReconnect);

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2StateMachine_BufferOrphanedRequests_should_buffer_streams_above_lastStreamId()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface(); // consume preface
        ops.Outbound.Clear();

        sm.EncodeRequest(MakeGet("/a")); // stream 1
        sm.EncodeRequest(MakeGet("/b")); // stream 3
        ops.Outbound.Clear();

        sm.BufferOrphanedRequests(lastStreamId: 0); // server processed nothing

        Assert.True(sm.IsReconnecting);
        Assert.Equal(2, sm.ReconnectBufferCount);
        Assert.Single(ops.Outbound.OfType<ReconnectItem>());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2StateMachine_BufferOrphanedRequests_should_not_replay_non_idempotent_streams_below_lastStreamId()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();
        ops.Outbound.Clear();

        sm.EncodeRequest(MakeGet("/a"));  // stream 1 — GET (idempotent)
        sm.EncodeRequest(MakePost("/b")); // stream 3 — POST (not idempotent)
        ops.Outbound.Clear();

        // Server processed stream 1 and 3 (LastStreamId=3) — POST stream 3 ≤ LastStreamId and non-idempotent
        sm.BufferOrphanedRequests(lastStreamId: 3);

        // GET at stream 1 ≤ LastStreamId but idempotent and no response headers → buffered
        // POST at stream 3 ≤ LastStreamId and non-idempotent → discarded
        Assert.Equal(1, sm.ReconnectBufferCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2StateMachine_HandleConnectedSignal_should_replay_with_fresh_stream_ids()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();
        ops.Outbound.Clear();

        sm.EncodeRequest(MakeGet("/a")); // stream 1
        ops.Outbound.Clear();
        sm.BufferOrphanedRequests(lastStreamId: 0);
        ops.Outbound.Clear();

        sm.HandleConnectedSignal();

        Assert.False(sm.IsReconnecting);
        // New preface emitted, then request with stream ID 1 (fresh tracker)
        var acquire = ops.Outbound.OfType<StreamAcquireItem>().ToList();
        Assert.Single(acquire);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2StateMachine_CanAcceptRequest_should_be_false_when_reconnecting()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();
        sm.EncodeRequest(MakeGet());
        sm.BufferOrphanedRequests(lastStreamId: 0);

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2StateMachine_HandleReconnectAttempt_should_fail_when_max_exceeded()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(maxReconnect: 1), ops);
        sm.TryBuildPreface();
        sm.EncodeRequest(MakeGet());
        sm.BufferOrphanedRequests(lastStreamId: 0); // attempt 1

        sm.HandleReconnectAttempt(); // attempt 2 > max

        Assert.True(ops.ReconnectFailed);
    }
}
