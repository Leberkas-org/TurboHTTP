using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Protocol.RFC9114;
using TurboHttp.Streams.Stages.Decoding;

namespace TurboHttp.StreamTests.RFC9114;

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
public sealed class Http30PushRejectionStageTests : StreamTestBase
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

        var downstream = await downstreamTask.WaitAsync(TimeSpan.FromSeconds(5));
        var serverBound = await serverBoundTask.WaitAsync(TimeSpan.FromSeconds(5));

        return (downstream, serverBound);
    }

    // ──────────────────────────────────────────────────────────────────────
    // MAX_PUSH_ID=0 Sent at Startup (RFC 9114 §10.5)
    // ──────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-10.5-PR-001: MAX_PUSH_ID not emitted by ConnectionStage (moved to ControlStreamPrefaceStage)")]
    public async Task Should_NotSendMaxPushId_When_StageStarts()
    {
        var (_, serverBound) = await RunAsync();

        // MAX_PUSH_ID now belongs on the unidirectional control stream, emitted by
        // Http30ControlStreamPrefaceStage — ConnectionStage no longer emits it.
        Assert.DoesNotContain(serverBound, f => f is Http3MaxPushIdFrame);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-10.5-PR-002: Neither SETTINGS nor MAX_PUSH_ID emitted by ConnectionStage")]
    public async Task Should_NotSendSettingsOrMaxPushId_When_StageStarts()
    {
        var (_, serverBound) = await RunAsync();

        // Both SETTINGS and MAX_PUSH_ID now go through the control stream preface stage.
        Assert.DoesNotContain(serverBound, f => f is Http3SettingsFrame);
        Assert.DoesNotContain(serverBound, f => f is Http3MaxPushIdFrame);
    }

    // ──────────────────────────────────────────────────────────────────────
    // PUSH_PROMISE Rejection (RFC 9114 §10.5)
    // ──────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-10.5-PR-003: PUSH_PROMISE causes ExcessiveLoad connection error")]
    public async Task Should_FailStage_When_PushPromiseReceived()
    {
        var settings = new Http3SettingsFrame([]);
        var pushPromise = new Http3PushPromiseFrame(0, new byte[] { 0x00 });

        var ex = await Assert.ThrowsAsync<Http3ConnectionException>(() => RunAsync(settings, pushPromise));

        Assert.Equal(Http3ErrorCode.ExcessiveLoad, ex.ErrorCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-10.5-PR-004: PUSH_PROMISE with any push ID causes ExcessiveLoad")]
    public async Task Should_FailStage_When_PushPromiseWithNonZeroPushIdReceived()
    {
        var settings = new Http3SettingsFrame([]);
        var pushPromise = new Http3PushPromiseFrame(42, new byte[] { 0x00, 0x01 });

        var ex = await Assert.ThrowsAsync<Http3ConnectionException>(() => RunAsync(settings, pushPromise));

        Assert.Equal(Http3ErrorCode.ExcessiveLoad, ex.ErrorCode);
    }

    // ──────────────────────────────────────────────────────────────────────
    // CANCEL_PUSH Handling (RFC 9114 §7.2.3)
    // ──────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.2.3-PR-005: CANCEL_PUSH frame absorbed without error")]
    public async Task Should_AbsorbCancelPush_When_ReceivedFromServer()
    {
        var settings = new Http3SettingsFrame([]);
        var cancelPush = new Http3CancelPushFrame(0);

        var (downstream, _) = await RunAsync(settings, cancelPush);

        // CANCEL_PUSH is control-stream — not forwarded to app
        Assert.Empty(downstream);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Normal Frames Still Work After Push Defense Setup
    // ──────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-10.5-PR-006: DATA frames still forwarded after push defense setup")]
    public async Task Should_ForwardDataFrames_When_PushDefenseIsActive()
    {
        var settings = new Http3SettingsFrame([]);
        var data = new Http3DataFrame(new byte[] { 0x48, 0x65, 0x6c, 0x6c, 0x6f });

        var (downstream, _) = await RunAsync(settings, data);

        Assert.Single(downstream);
        Assert.IsType<Http3DataFrame>(downstream[0]);
    }
}
