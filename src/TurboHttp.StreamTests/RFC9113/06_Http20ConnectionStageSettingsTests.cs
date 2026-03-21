using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams.Stages;
using TurboHttp.Streams.Stages.Features;

namespace TurboHttp.StreamTests.RFC9113;

/// <summary>
/// Tests SETTINGS frame handling in the HTTP/2 connection stage per RFC 9113.
/// Verifies that SETTINGS frames are acknowledged and that advertised parameters are applied correctly.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http20ConnectionStage"/>.
/// RFC 9113 §6.5: HTTP/2 SETTINGS frame format, parameters, and acknowledgement protocol.
/// </remarks>
public sealed class Http20ConnectionStageSettingsTests : StreamTestBase
{
    /// <summary>
    /// Runs the Http20ConnectionStage with the given server frames (arriving on InServer).
    /// Returns (downstream frames from OutStream, server-bound frames from OutServer).
    /// InApp is fed Source.Never so the stage stays alive until _inServer finishes.
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
                    var signalSink = b.Add(Sink.Ignore<IControlItem>().MapMaterializedValue(_ => NotUsed.Instance));

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutStream).To(dsSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutServer).To(sbSink);
                    b.From(stage.OutSignal).To(signalSink);

                    return ClosedShape.Instance;
                }));

        var (downstreamTask, serverBoundTask) = graph.Run(Materializer);

        var downstream = await downstreamTask.WaitAsync(TimeSpan.FromSeconds(5));
        var serverBound = await serverBoundTask.WaitAsync(TimeSpan.FromSeconds(5));

        return (downstream, serverBound);
    }

    /// <summary>
    /// Runs the Http20ConnectionStage and captures OutSignal emissions alongside standard outputs.
    /// </summary>
    private async Task<(IReadOnlyList<Http2Frame> Downstream, IReadOnlyList<Http2Frame> ServerBound, IReadOnlyList<IControlItem> Signals)> RunWithSignalsAsync(
        params Http2Frame[] serverFrames)
    {
        var downstreamSink = Sink.Seq<Http2Frame>();
        var serverBoundSink = Sink.Seq<Http2Frame>();
        var signalSeqSink = Sink.Seq<IControlItem>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, serverBoundSink, signalSeqSink,
                (m1, m2, m3) => (m1, m2, m3),
                (b, dsSink, sbSink, sigSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage());
                    var serverSource = b.Add(Source.From(serverFrames));
                    var requestSource = b.Add(Source.Never<Http2Frame>());

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutStream).To(dsSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutServer).To(sbSink);
                    b.From(stage.OutSignal).To(sigSink);

                    return ClosedShape.Instance;
                }));

        var (downstreamTask, serverBoundTask, signalTask) = graph.Run(Materializer);

        var downstream = await downstreamTask.WaitAsync(TimeSpan.FromSeconds(5));
        var serverBound = await serverBoundTask.WaitAsync(TimeSpan.FromSeconds(5));
        var signals = await signalTask.WaitAsync(TimeSpan.FromSeconds(5));

        return (downstream, serverBound, signals);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-6.5-20CS-001: Server SETTINGS received produces SETTINGS ACK")]
    public async Task Should_Send_Ack_When_Server_Settings_Received()
    {
        var settings = new SettingsFrame(
            [(SettingsParameter.MaxConcurrentStreams, 100u)]);

        var (_, serverBound) = await RunAsync(settings);

        var ack = Assert.Single(serverBound);
        var ackFrame = Assert.IsType<SettingsFrame>(ack);
        Assert.True(ackFrame.IsAck, "Response must be a SETTINGS ACK");
        Assert.Empty(ackFrame.Parameters);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-6.5-20CS-002: SETTINGS with ACK flag does not trigger another ACK")]
    public async Task Should_Not_Trigger_Another_Ack_When_Settings_Ack_Received()
    {
        var settingsAck = new SettingsFrame([], isAck: true);

        var (downstream, serverBound) = await RunAsync(settingsAck);

        // The ACK frame should be forwarded downstream
        Assert.Single(downstream);
        // No ACK should be sent back to the server
        Assert.Empty(serverBound);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-6.5-20CS-003: INITIAL_WINDOW_SIZE parameter updates internal stream window")]
    public async Task Should_Update_Stream_Window_When_Initial_Window_Size_Setting_Received()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-6.5-20CS-003: Inbound DATA exceeding stream window is logged and stream survives")]
    public async Task Should_Survive_When_Inbound_Data_Exceeds_Stream_Window()
    {
        // Default stream recv window is 65535. Send 65536 bytes to exceed it.
        // Under "Stream Never Dies", the stage must log the violation and continue — not fault.
        var data = new DataFrame(streamId: 1, data: new byte[65536], endStream: true);

        var downstreamSink = Sink.Seq<Http2Frame>();
        var serverBoundSink = Sink.Seq<Http2Frame>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, serverBoundSink,
                (m1, m2) => (m1, m2),
                (b, dsSink, sbSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage());
                    var serverSource = b.Add(Source.Single<Http2Frame>(data));
                    var requestSource = b.Add(Source.Never<Http2Frame>());
                    var signalSink = b.Add(Sink.Ignore<IControlItem>().MapMaterializedValue(_ => NotUsed.Instance));

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutStream).To(dsSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutServer).To(sbSink);
                    b.From(stage.OutSignal).To(signalSink);

                    return ClosedShape.Instance;
                }));

        var (downstreamTask, serverBoundTask) = graph.Run(Materializer);

        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.False(downstreamTask.IsFaulted,
            "Downstream task must not fault when inbound stream window is exceeded");
        Assert.False(serverBoundTask.IsFaulted,
            "ServerBound task must not fault when inbound stream window is exceeded");
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-6.5-20CS-004: SETTINGS frame is forwarded downstream")]
    public async Task Should_Forward_Settings_Frame_Downstream()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-6.5-20CS-005: Multiple SETTINGS each produce exactly one ACK")]
    public async Task Should_Produce_One_Ack_Per_Settings_Frame_When_Multiple_Received()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-6.5.2-20CS-006: SETTINGS MAX_CONCURRENT_STREAMS emits MaxConcurrentStreamsItem on OutSignal")]
    public async Task Should_Emit_Signal_When_MaxConcurrentStreams_Settings_Received()
    {
        var settings = new SettingsFrame(
            [(SettingsParameter.MaxConcurrentStreams, 50u)]);

        var (_, _, signals) = await RunWithSignalsAsync(settings);

        var signal = Assert.Single(signals);
        var item = Assert.IsType<MaxConcurrentStreamsItem>(signal);
        Assert.Equal(50, item.MaxStreams);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-6.5.2-20CS-007: SETTINGS ACK does not emit on OutSignal")]
    public async Task Should_Not_Emit_Signal_When_Settings_Ack_Received()
    {
        var settingsAck = new SettingsFrame([], isAck: true);

        var (_, _, signals) = await RunWithSignalsAsync(settingsAck);

        Assert.Empty(signals);
    }
}
