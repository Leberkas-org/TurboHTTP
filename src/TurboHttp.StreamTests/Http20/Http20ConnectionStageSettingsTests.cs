using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Protocol;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http20;

public sealed class Http20ConnectionStageSettingsTests : StreamTestBase
{
    /// <summary>
    /// Runs the Http20ConnectionStage with the given server frames (arriving on Inlet1/inletRaw).
    /// Returns (downstream frames from Outlet1/outletStream, server-bound frames from Outlet2/outletRaw).
    /// Inlet2 (inletRequest) is fed Source.Never so the stage stays alive until inletRaw finishes.
    /// </summary>
    private async Task<(IReadOnlyList<Http2Frame> Downstream, IReadOnlyList<Http2Frame> ServerBound)> RunAsync(
        params Http2Frame[] serverFrames)
    {
        var downstreamSink = Sink.Seq<Http2Frame>();
        var serverBoundSink = Sink.Seq<Http2Frame>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, serverBoundSink,
                (m1, m2) => (m1, m2),
                (b, dsSink, sbSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage());
                    var serverSource = b.Add(Source.From(serverFrames));
                    var requestSource = b.Add(Source.Never<Http2Frame>());

                    b.From(serverSource).To(stage.Inlet1);
                    b.From(stage.Outlet1).To(dsSink);
                    b.From(requestSource).To(stage.Inlet2);
                    b.From(stage.Outlet2).To(sbSink);

                    return ClosedShape.Instance;
                }));

        var (downstreamTask, serverBoundTask) = graph.Run(Materializer);

        var downstream = await downstreamTask.WaitAsync(TimeSpan.FromSeconds(5));
        var serverBound = await serverBoundTask.WaitAsync(TimeSpan.FromSeconds(5));

        return (downstream, serverBound);
    }

    // ─── 20CS-001: Server SETTINGS received → SETTINGS ACK sent ─────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.5-20CS-001: Server SETTINGS received produces SETTINGS ACK")]
    public async Task Server_Settings_Received_Sends_Ack()
    {
        var settings = new SettingsFrame(
            [(SettingsParameter.MaxConcurrentStreams, 100u)]);

        var (_, serverBound) = await RunAsync(settings);

        var ack = Assert.Single(serverBound);
        var ackFrame = Assert.IsType<SettingsFrame>(ack);
        Assert.True(ackFrame.IsAck, "Response must be a SETTINGS ACK");
        Assert.Empty(ackFrame.Parameters);
    }

    // ─── 20CS-002: SETTINGS with ACK flag → no ACK sent back ────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.5-20CS-002: SETTINGS with ACK flag does not trigger another ACK")]
    public async Task Settings_Ack_Does_Not_Trigger_Another_Ack()
    {
        var settingsAck = new SettingsFrame([], isAck: true);

        var (downstream, serverBound) = await RunAsync(settingsAck);

        // The ACK frame should be forwarded downstream
        Assert.Single(downstream);
        // No ACK should be sent back to the server
        Assert.Empty(serverBound);
    }

    // ─── 20CS-003: INITIAL_WINDOW_SIZE parameter → _initialStreamWindow updated ─

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.5-20CS-003: INITIAL_WINDOW_SIZE parameter updates internal stream window")]
    public async Task Initial_Window_Size_Updates_Stream_Window()
    {
        // Send SETTINGS with INITIAL_WINDOW_SIZE = 32768, then a DATA frame
        // on a new stream. The stage tracks stream windows starting from _initialStreamWindow.
        // If the window was correctly updated, the stage won't fail for data within the new window.
        var settings = new SettingsFrame(
            [(SettingsParameter.InitialWindowSize, 32768u)]);

        // A DATA frame of exactly 32768 bytes should succeed if _initialStreamWindow was updated
        var data = new DataFrame(streamId: 1, data: new byte[32768], endStream: true);

        var (downstream, serverBound) = await RunAsync(settings, data);

        // Both frames should be forwarded downstream (stage didn't fail)
        Assert.Equal(2, downstream.Count);
        Assert.IsType<SettingsFrame>(downstream[0]);
        Assert.IsType<DataFrame>(downstream[1]);

        // Server-bound: SETTINGS ACK + WINDOW_UPDATE(stream=0) + WINDOW_UPDATE(stream=1)
        Assert.True(serverBound.Count >= 1, "At least SETTINGS ACK expected");
        Assert.IsType<SettingsFrame>(serverBound[0]);
        Assert.True(((SettingsFrame)serverBound[0]).IsAck);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.5-20CS-003: INITIAL_WINDOW_SIZE exceeded causes stage failure")]
    public async Task Initial_Window_Size_Exceeded_Causes_Failure()
    {
        // Send SETTINGS with INITIAL_WINDOW_SIZE = 100, then a DATA frame of 200 bytes.
        // The stage should fail because the stream window is exceeded.
        var settings = new SettingsFrame(
            [(SettingsParameter.InitialWindowSize, 100u)]);
        var data = new DataFrame(streamId: 1, data: new byte[200], endStream: true);

        // The stage should fail — either the materialized task faults or we get an exception
        await Assert.ThrowsAnyAsync<Exception>(() => RunAsync(settings, data));
    }

    // ─── 20CS-004: SETTINGS frame forwarded downstream ──────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.5-20CS-004: SETTINGS frame is forwarded downstream")]
    public async Task Settings_Frame_Forwarded_Downstream()
    {
        var settings = new SettingsFrame(
            [(SettingsParameter.MaxFrameSize, 32768u),
             (SettingsParameter.HeaderTableSize, 8192u)]);

        var (downstream, _) = await RunAsync(settings);

        var forwarded = Assert.Single(downstream);
        var forwardedSettings = Assert.IsType<SettingsFrame>(forwarded);
        Assert.False(forwardedSettings.IsAck, "Original frame (not ACK) should be forwarded");
        Assert.Equal(2, forwardedSettings.Parameters.Count);
    }

    // ─── 20CS-005: Multiple consecutive SETTINGS → one ACK each ─────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.5-20CS-005: Multiple SETTINGS each produce exactly one ACK")]
    public async Task Multiple_Settings_Each_Produce_One_Ack()
    {
        var settings1 = new SettingsFrame(
            [(SettingsParameter.MaxConcurrentStreams, 50u)]);
        var settings2 = new SettingsFrame(
            [(SettingsParameter.MaxConcurrentStreams, 200u)]);
        var settings3 = new SettingsFrame(
            [(SettingsParameter.InitialWindowSize, 16384u)]);

        var (downstream, serverBound) = await RunAsync(settings1, settings2, settings3);

        // All 3 SETTINGS forwarded downstream
        Assert.Equal(3, downstream.Count);
        Assert.All(downstream, f => Assert.IsType<SettingsFrame>(f));

        // Exactly 3 ACKs sent server-bound
        Assert.Equal(3, serverBound.Count);
        Assert.All(serverBound, f =>
        {
            var sf = Assert.IsType<SettingsFrame>(f);
            Assert.True(sf.IsAck);
            Assert.Empty(sf.Parameters);
        });
    }
}
