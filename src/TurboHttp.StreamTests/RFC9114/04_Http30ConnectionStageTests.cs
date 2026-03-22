using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Protocol.RFC9114;
using TurboHttp.Streams.Stages.Decoding;

namespace TurboHttp.StreamTests.RFC9114;

/// <summary>
/// Tests the HTTP/3 connection stage per RFC 9114 §6.2.1, §5.2, §7.2.4.
/// Verifies control stream management (SETTINGS/GOAWAY) and frame routing
/// between app and server.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http30ConnectionStage"/>.
/// RFC 9114 §6.2.1: Control stream — first frame MUST be SETTINGS.
/// RFC 9114 §5.2: GOAWAY — graceful connection shutdown.
/// RFC 9114 §7.2.4: SETTINGS frame processing and validation.
/// </remarks>
public sealed class Http30ConnectionStageTests : StreamTestBase
{
    /// <summary>
    /// Runs the Http30ConnectionStage with the given server frames (arriving on InServer).
    /// Returns (downstream frames from OutApp, server-bound frames from OutServer).
    /// InApp is fed Source.Never so the stage stays alive until InServer finishes.
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

    /// <summary>
    /// Runs the stage with both server frames and app (request) frames.
    /// Returns (downstream from OutApp, server-bound from OutServer).
    /// </summary>
    private async Task<(IReadOnlyList<Http3Frame> Downstream, IReadOnlyList<Http3Frame> ServerBound)> RunWithAppAsync(
        Http3Frame[] serverFrames, Http3Frame[] appFrames)
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
                    var requestSource = b.Add(Source.From(appFrames));

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
    // SETTINGS Tests (RFC 9114 §7.2.4)
    // ──────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.2.4-H3C-001: SETTINGS frame absorbed and not forwarded to app")]
    public async Task Should_AbsorbSettings_When_ReceivedFromServer()
    {
        var settings = new Http3SettingsFrame([(Http3SettingId.MaxFieldSectionSize, 8192)]);

        var (downstream, _) = await RunAsync(settings);

        // SETTINGS is connection-level and should not be forwarded to the app
        Assert.Empty(downstream);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.2.4-H3C-002: Empty SETTINGS frame is valid")]
    public async Task Should_AcceptEmptySettings_When_ReceivedFromServer()
    {
        var settings = new Http3SettingsFrame([]);

        var (downstream, _) = await RunAsync(settings);

        Assert.Empty(downstream);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.2.4-H3C-003: SETTINGS not emitted by ConnectionStage (moved to ControlStreamPrefaceStage)")]
    public async Task Should_NotSendSettings_When_StageStarts()
    {
        // No server frames — just let the stage start and capture outbound
        var (_, serverBound) = await RunAsync();

        // SETTINGS now belongs on the unidirectional control stream, emitted by
        // Http30ControlStreamPrefaceStage — ConnectionStage no longer emits it.
        Assert.DoesNotContain(serverBound, f => f is Http3SettingsFrame);
    }

    // ──────────────────────────────────────────────────────────────────────
    // GOAWAY Tests (RFC 9114 §5.2)
    // ──────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-5.2-H3C-004: GOAWAY frame absorbed and not forwarded to app")]
    public async Task Should_AbsorbGoAway_When_ReceivedFromServer()
    {
        var settings = new Http3SettingsFrame([]);
        var goAway = new Http3GoAwayFrame(0);

        var (downstream, _) = await RunAsync(settings, goAway);

        Assert.Empty(downstream);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-5.2-H3C-005: GOAWAY absorbed on control stream")]
    public async Task Should_AbsorbGoAway_When_ReceivedOnControlStream()
    {
        var settings = new Http3SettingsFrame([]);
        var goAway = new Http3GoAwayFrame(4);

        var (downstream, _) = await RunAsync(settings, goAway);

        // Neither SETTINGS nor GOAWAY should be forwarded to the app
        Assert.Empty(downstream);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-5.2-H3C-006: GOAWAY with decreasing stream ID accepted")]
    public async Task Should_AcceptDecreasingGoAway_When_MultipleReceived()
    {
        var settings = new Http3SettingsFrame([]);
        var goAway1 = new Http3GoAwayFrame(8);
        var goAway2 = new Http3GoAwayFrame(4);

        var (downstream, _) = await RunAsync(settings, goAway1, goAway2);

        // Both GOAWAYs absorbed — no frames forwarded
        Assert.Empty(downstream);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Frame Routing Tests
    // ──────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.2.1-H3C-007: DATA frames forwarded to app")]
    public async Task Should_ForwardDataFrame_When_ReceivedFromServer()
    {
        var settings = new Http3SettingsFrame([]);
        var data = new Http3DataFrame(new byte[] { 0x48, 0x65, 0x6c, 0x6c, 0x6f });

        var (downstream, _) = await RunAsync(settings, data);

        Assert.Single(downstream);
        Assert.IsType<Http3DataFrame>(downstream[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.2.2-H3C-008: HEADERS frames forwarded to app")]
    public async Task Should_ForwardHeadersFrame_When_ReceivedFromServer()
    {
        var settings = new Http3SettingsFrame([]);
        var headers = new Http3HeadersFrame(new byte[] { 0x00, 0x00 });

        var (downstream, _) = await RunAsync(settings, headers);

        Assert.Single(downstream);
        Assert.IsType<Http3HeadersFrame>(downstream[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.2.3-H3C-009: CANCEL_PUSH frame absorbed")]
    public async Task Should_AbsorbCancelPush_When_ReceivedFromServer()
    {
        var settings = new Http3SettingsFrame([]);
        var cancelPush = new Http3CancelPushFrame(0);

        var (downstream, _) = await RunAsync(settings, cancelPush);

        Assert.Empty(downstream);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.2.7-H3C-010: MAX_PUSH_ID frame absorbed")]
    public async Task Should_AbsorbMaxPushId_When_ReceivedFromServer()
    {
        var settings = new Http3SettingsFrame([]);
        var maxPushId = new Http3MaxPushIdFrame(10);

        var (downstream, _) = await RunAsync(settings, maxPushId);

        Assert.Empty(downstream);
    }

    // ──────────────────────────────────────────────────────────────────────
    // App-to-Server Forwarding Tests
    // ──────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-H3C-011: App HEADERS frame forwarded to server")]
    public async Task Should_ForwardAppHeaders_When_Sent()
    {
        var headerBlock = new byte[] { 0x00, 0x00 };
        var appFrame = new Http3HeadersFrame(headerBlock);

        var (_, serverBound) = await RunWithAppAsync(
            [],
            [appFrame]);

        // Should contain initial SETTINGS and the app HEADERS frame
        Assert.Contains(serverBound, f => f is Http3HeadersFrame);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-H3C-012: App DATA frame forwarded to server")]
    public async Task Should_ForwardAppData_When_Sent()
    {
        var appFrame = new Http3DataFrame(new byte[] { 0x01, 0x02, 0x03 });

        var (_, serverBound) = await RunWithAppAsync(
            [],
            [appFrame]);

        Assert.Contains(serverBound, f => f is Http3DataFrame);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Port Name Verification
    // ──────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9114-H3C-013: Port names follow convention")]
    public void Should_HaveCorrectPortNames_When_Inspected()
    {
        var stage = new Http30ConnectionStage();
        var shape = stage.Shape;

        Assert.Equal("H3Connection.In.Server", shape.InServer.Name);
        Assert.Equal("H3Connection.Out.App", shape.OutApp.Name);
        Assert.Equal("H3Connection.In.App", shape.InApp.Name);
        Assert.Equal("H3Connection.Out.Server", shape.OutServer.Name);
    }
}
