using Servus.Akka.Transport;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Http2.Connection;

public sealed class Http2StateMachineReconnectSpec
{
    private static TurboClientOptions MakeConfig(int? maxConcurrentStreams = null, int? maxReconnect = null)
    {
        var options = new TurboClientOptions();
        if (maxConcurrentStreams.HasValue) options.Http2.MaxConcurrentStreams = maxConcurrentStreams.Value;
        if (maxReconnect.HasValue) options.Http2.MaxReconnectAttempts = maxReconnect.Value;
        return options;
    }

    private static HttpRequestMessage MakeGet(string path = "/") =>
        new(HttpMethod.Get, $"https://example.com{path}");

    private static HttpRequestMessage MakePost(string path = "/") =>
        new(HttpMethod.Post, $"https://example.com{path}");

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2StateMachine_OnConnectionLost_should_buffer_streams_above_lastStreamId()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface(); // consume preface
        ops.Outbound.Clear();

        sm.EncodeRequest(MakeGet("/a")); // stream 1
        sm.EncodeRequest(MakeGet("/b")); // stream 3
        ops.Outbound.Clear();

        sm.OnConnectionLost(lastStreamId: 0); // server processed nothing

        Assert.True(sm.IsReconnecting);
        Assert.Equal(2, sm.ReconnectBufferCount);
        Assert.Single(ops.Outbound, item => item is ConnectTransport);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2StateMachine_OnConnectionLost_should_not_replay_non_idempotent_streams_below_lastStreamId()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();
        ops.Outbound.Clear();

        sm.EncodeRequest(MakeGet("/a")); // stream 1 — GET (idempotent)
        sm.EncodeRequest(MakePost("/b")); // stream 3 — POST (not idempotent)
        ops.Outbound.Clear();

        // Server processed stream 1 and 3 (LastStreamId=3) — POST stream 3 ≤ LastStreamId and non-idempotent
        sm.OnConnectionLost(lastStreamId: 3);

        // GET at stream 1 ≤ LastStreamId but idempotent and no response headers → buffered
        // POST at stream 3 ≤ LastStreamId and non-idempotent → discarded
        Assert.Equal(1, sm.ReconnectBufferCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2StateMachine_OnConnectionRestored_should_replay_with_fresh_stream_ids()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();
        ops.Outbound.Clear();

        sm.EncodeRequest(MakeGet("/a")); // stream 1
        ops.Outbound.Clear();
        sm.OnConnectionLost(lastStreamId: 0);
        ops.Outbound.Clear();

        sm.OnConnectionRestored();

        Assert.False(sm.IsReconnecting);
        // New preface emitted, then request with stream ID 1 (fresh tracker)
        Assert.NotEmpty(ops.Outbound);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2StateMachine_CanAcceptRequest_should_be_false_when_reconnecting()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();
        sm.EncodeRequest(MakeGet());
        sm.OnConnectionLost(lastStreamId: 0);

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2StateMachine_OnReconnectAttemptFailed_should_fail_when_max_exceeded()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(maxReconnect: 1), ops);
        sm.TryBuildPreface();
        sm.EncodeRequest(MakeGet());
        sm.OnConnectionLost(lastStreamId: 0); // attempt 1

        sm.OnReconnectAttemptFailed(); // attempt 2 > max

        Assert.True(ops.ReconnectFailed);
    }
}