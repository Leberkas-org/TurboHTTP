using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.Http2;
using TurboHttp.Streams.Stages.Decoding;

namespace TurboHttp.StreamTests.Http2.Connection;

/// <summary>
/// Tests SETTINGS frame handling in the HTTP/2 connection stage per RFC 9113.
/// Verifies that SETTINGS frames are acknowledged and that advertised parameters are applied correctly.
/// </summary>
[Trait("RFC", "RFC9113-6.5")]
public sealed class Http2ConnectionSettingsSpec : StreamTestBase
{
    private async Task<(IReadOnlyList<HttpResponseMessage> Downstream, IReadOnlyList<Http2Frame> ServerBound)> RunAsync(
        params Http2Frame[] serverFrames)
    {
        var downstreamSink = Sink.Seq<HttpResponseMessage>();
        var serverBoundSink = Sink.Seq<Http2Frame>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, serverBoundSink,
                (m1, m2) => (m1, m2),
                (b, dsSink, sbSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage());
                    var serverSource = b.Add(Source.From(serverFrames));
                    var requestSource = b.Add(Source.Never<HttpRequestMessage>());
                    var signalSink = b.Add(Sink.Ignore<IControlItem>().MapMaterializedValue(_ => NotUsed.Instance));

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutResponse).To(dsSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutServer).To(sbSink);
                    b.From(stage.OutSignal).To(signalSink);

                    return ClosedShape.Instance;
                }));

        var (downstreamTask, serverBoundTask) = graph.Run(Materializer);

        var downstream = await downstreamTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var serverBound = await serverBoundTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        return (downstream, serverBound);
    }

    private async Task<(IReadOnlyList<HttpResponseMessage> Downstream, IReadOnlyList<Http2Frame> ServerBound, IReadOnlyList<IControlItem> Signals)> RunWithSignalsAsync(
        params Http2Frame[] serverFrames)
    {
        var downstreamSink = Sink.Seq<HttpResponseMessage>();
        var serverBoundSink = Sink.Seq<Http2Frame>();
        var signalSeqSink = Sink.Seq<IControlItem>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, serverBoundSink, signalSeqSink,
                (m1, m2, m3) => (m1, m2, m3),
                (b, dsSink, sbSink, sigSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage());
                    var serverSource = b.Add(Source.From(serverFrames));
                    var requestSource = b.Add(Source.Never<HttpRequestMessage>());

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutResponse).To(dsSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutServer).To(sbSink);
                    b.From(stage.OutSignal).To(sigSink);

                    return ClosedShape.Instance;
                }));

        var (downstreamTask, serverBoundTask, signalTask) = graph.Run(Materializer);

        var downstream = await downstreamTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var serverBound = await serverBoundTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var signals = await signalTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        return (downstream, serverBound, signals);
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2ConnectionSettings_should_send_ack_when_server_settings_received()
    {
        var settings = new SettingsFrame(
            [(SettingsParameter.MaxConcurrentStreams, 100u)]);

        var (_, serverBound) = await RunAsync(settings);

        var ack = Assert.Single(serverBound);
        var ackFrame = Assert.IsType<SettingsFrame>(ack);
        Assert.True(ackFrame.IsAck, "Response must be a SETTINGS ACK");
        Assert.Empty(ackFrame.Parameters);
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2ConnectionSettings_should_not_trigger_another_ack_when_settings_ack_received()
    {
        var settingsAck = new SettingsFrame([], isAck: true);

        var (downstream, serverBound) = await RunAsync(settingsAck);

        Assert.Empty(downstream);
        Assert.Empty(serverBound);
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2ConnectionSettings_should_update_stream_window_when_initial_window_size_setting_received()
    {
        var settings = new SettingsFrame(
            [(SettingsParameter.InitialWindowSize, 32768u)]);
        var data = new DataFrame(streamId: 1, data: new byte[32768], endStream: true);

        var (downstream, serverBound) = await RunAsync(settings, data);

        Assert.Empty(downstream);

        Assert.True(serverBound.Count >= 1, "At least SETTINGS ACK expected");
        Assert.IsType<SettingsFrame>(serverBound[0]);
        Assert.True(((SettingsFrame)serverBound[0]).IsAck);
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2ConnectionSettings_should_survive_when_inbound_data_exceeds_stream_window()
    {
        var data = new DataFrame(streamId: 1, data: new byte[65536], endStream: true);

        var downstreamSink = Sink.Seq<HttpResponseMessage>();
        var serverBoundSink = Sink.Seq<Http2Frame>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, serverBoundSink,
                (m1, m2) => (m1, m2),
                (b, dsSink, sbSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage());
                    var serverSource = b.Add(Source.Single<Http2Frame>(data));
                    var requestSource = b.Add(Source.Never<HttpRequestMessage>());
                    var signalSink = b.Add(Sink.Ignore<IControlItem>().MapMaterializedValue(_ => NotUsed.Instance));

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutResponse).To(dsSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutServer).To(sbSink);
                    b.From(stage.OutSignal).To(signalSink);

                    return ClosedShape.Instance;
                }));

        var (downstreamTask, serverBoundTask) = graph.Run(Materializer);

        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        Assert.False(downstreamTask.IsFaulted,
            "Downstream task must not fault when inbound stream window is exceeded");
        Assert.False(serverBoundTask.IsFaulted,
            "ServerBound task must not fault when inbound stream window is exceeded");
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2ConnectionSettings_should_not_forward_settings_frame_to_out_response()
    {
        var settings = new SettingsFrame(
            [(SettingsParameter.MaxFrameSize, 32768u),
             (SettingsParameter.HeaderTableSize, 8192u)]);

        var (downstream, serverBound) = await RunAsync(settings);

        Assert.Empty(downstream);
        var ack = Assert.Single(serverBound);
        Assert.IsType<SettingsFrame>(ack);
        Assert.True(((SettingsFrame)ack).IsAck);
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2ConnectionSettings_should_produce_one_ack_per_settings_frame_when_multiple_received()
    {
        var settings1 = new SettingsFrame(
            [(SettingsParameter.MaxConcurrentStreams, 50u)]);
        var settings2 = new SettingsFrame(
            [(SettingsParameter.MaxConcurrentStreams, 200u)]);
        var settings3 = new SettingsFrame(
            [(SettingsParameter.InitialWindowSize, 16384u)]);

        var (downstream, serverBound) = await RunAsync(settings1, settings2, settings3);

        Assert.Empty(downstream);

        Assert.Equal(3, serverBound.Count);
        Assert.All(serverBound, f =>
        {
            var sf = Assert.IsType<SettingsFrame>(f);
            Assert.True(sf.IsAck);
            Assert.Empty(sf.Parameters);
        });
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public async Task Http2ConnectionSettings_should_emit_signal_when_max_concurrent_streams_settings_received()
    {
        var settings = new SettingsFrame(
            [(SettingsParameter.MaxConcurrentStreams, 50u)]);

        var (_, _, signals) = await RunWithSignalsAsync(settings);

        var signal = Assert.Single(signals);
        var item = Assert.IsType<MaxConcurrentStreamsItem>(signal);
        Assert.Equal(50, item.MaxStreams);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public async Task Http2ConnectionSettings_should_not_emit_signal_when_settings_ack_received()
    {
        var settingsAck = new SettingsFrame([], isAck: true);

        var (_, _, signals) = await RunWithSignalsAsync(settingsAck);

        Assert.Empty(signals);
    }
}
