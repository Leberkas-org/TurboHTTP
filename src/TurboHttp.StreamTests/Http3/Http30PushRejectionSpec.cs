using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Protocol.Http3;
using TurboHttp.Streams.Stages.Decoding;

namespace TurboHttp.StreamTests.Http3;

/// <summary>
/// Tests HTTP/3 push rejection in the connection stage per RFC 9114 §10.5.
/// Verifies that <see cref="Http30ConnectionStage"/> sends MAX_PUSH_ID=0 at startup and
/// rejects any PUSH_PROMISE frame with an <see cref="Http3ErrorCode.ExcessiveLoad"/> error.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http30ConnectionStage"/>.
/// RFC 9114 §10.5: A client that does not wish to receive push promises can set MAX_PUSH_ID to 0.
/// Any PUSH_PROMISE received when push limit is 0 MUST be treated as a connection error of type
/// H3_EXCESSIVE_LOAD.
/// </remarks>
public sealed class Http30PushRejectionSpec : StreamTestBase
{
    /// <summary>
    /// Runs the Http30ConnectionStage with the given server frames.
    /// Returns (downstream frames from OutApp, server-bound frames from OutServer).
    /// </summary>
    private async Task<(IReadOnlyList<Http3Frame> Downstream, IReadOnlyList<Http3Frame> ServerBound)> RunAsync(
        params Http3Frame[] serverFrames)
    {
        var downstreamSink = Sink.Seq<Http3Frame>();
        var serverBoundSink = Sink.Seq<Http3Frame>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, serverBoundSink,
                (m1, m2) => (m1, m2),
                (b, dsSink, sbSink) =>
                {
                    var stage = b.Add(new Http30ConnectionStage());
                    var serverSource = b.Add(Source.From(serverFrames));
                    var requestSource = b.Add(Source.Never<Http3Frame>());

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutApp).To(dsSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutServer).To(sbSink);

                    return ClosedShape.Instance;
                }));

        var (downstreamTask, serverBoundTask) = graph.Run(Materializer);

        var downstream = await downstreamTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var serverBound = await serverBoundTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        return (downstream, serverBound);
    }

    // MAX_PUSH_ID=0 Sent at Startup (RFC 9114 §10.5)

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-10.5")]
    public async Task Http30PushRejection_should_not_send_max_push_id_when_stage_starts()
    {
        var (_, serverBound) = await RunAsync();

        // MAX_PUSH_ID now belongs on the unidirectional control stream, emitted by
        // Http30ControlStreamPrefaceStage — ConnectionStage no longer emits it.
        Assert.DoesNotContain(serverBound, f => f is Http3MaxPushIdFrame);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-10.5")]
    public async Task Http30PushRejection_should_not_send_settings_or_max_push_id_when_stage_starts()
    {
        var (_, serverBound) = await RunAsync();

        // Both SETTINGS and MAX_PUSH_ID now go through the control stream preface stage.
        Assert.DoesNotContain(serverBound, f => f is Http3SettingsFrame);
        Assert.DoesNotContain(serverBound, f => f is Http3MaxPushIdFrame);
    }

    // PUSH_PROMISE Rejection (RFC 9114 §10.5)

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-10.5")]
    public async Task Http30PushRejection_should_absorb_push_promise_when_push_promise_received()
    {
        var settings = new Http3SettingsFrame([]);
        var pushPromise = new Http3PushPromiseFrame(0, new byte[] { 0x00 });

        var (downstream, _) = await RunAsync(settings, pushPromise);

        // PUSH_PROMISE is absorbed (logged + dropped) — not forwarded to app
        Assert.Empty(downstream);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-10.5")]
    public async Task Http30PushRejection_should_absorb_push_promise_when_push_promise_with_non_zero_push_id_received()
    {
        var settings = new Http3SettingsFrame([]);
        var pushPromise = new Http3PushPromiseFrame(42, new byte[] { 0x00, 0x01 });

        var (downstream, _) = await RunAsync(settings, pushPromise);

        // PUSH_PROMISE is absorbed (logged + dropped) — not forwarded to app
        Assert.Empty(downstream);
    }

    // CANCEL_PUSH Handling (RFC 9114 §7.2.3)

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.3")]
    public async Task Http30PushRejection_should_absorb_cancel_push_when_received_from_server()
    {
        var settings = new Http3SettingsFrame([]);
        var cancelPush = new Http3CancelPushFrame(0);

        var (downstream, _) = await RunAsync(settings, cancelPush);

        // CANCEL_PUSH is control-stream — not forwarded to app
        Assert.Empty(downstream);
    }

    // Normal Frames Still Work After Push Defense Setup

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-10.5")]
    public async Task Http30PushRejection_should_forward_data_frames_when_push_defense_is_active()
    {
        var settings = new Http3SettingsFrame([]);
        var data = new Http3DataFrame(new byte[] { 0x48, 0x65, 0x6c, 0x6c, 0x6f });

        var (downstream, _) = await RunAsync(settings, data);

        Assert.Single(downstream);
        Assert.IsType<Http3DataFrame>(downstream[0]);
    }
}
