using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Streams.Stages.Decoding;
using static TurboHTTP.StreamTests.Http2.Http2ConnectionTestHelper;

namespace TurboHTTP.StreamTests.Http2;

/// <summary>
/// Tests HTTP/2 connection-level flow control in the connection stage per RFC 9113.
/// Verifies that WINDOW_UPDATE frames are processed and that the connection window is correctly maintained.
/// </summary>
[Trait("RFC", "RFC9113-5.2")]
public sealed class Http2ConnectionFlowControlSpec : StreamTestBase
{
    private Task<(IReadOnlyList<HttpResponseMessage> Downstream, IReadOnlyList<Http2Frame> ServerBound)> RunAsync(
        params Http2Frame[] serverFrames)
        => RunFlowAsync(new Http20ConnectionStage(), serverFrames);

    private async Task<(IReadOnlyList<HttpResponseMessage> Downstream, IReadOnlyList<Http2Frame> ServerBound)> RunFlowAsync(
        Http20ConnectionStage connectionStage,
        params Http2Frame[] serverFrames)
    {
        var downstreamSink = Sink.Seq<HttpResponseMessage>();
        var networkSink = Sink.Seq<IOutputItem>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, networkSink,
                (m1, m2) => (m1, m2),
                (b, dsSink, nwSink) =>
                {
                    var stage = b.Add(connectionStage);
                    var serverSource = b.Add(Source.From(FramesToInputs(serverFrames)));
                    var requestSource = b.Add(Source.Never<HttpRequestMessage>());

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutResponse).To(dsSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutNetwork).To(nwSink);

                    return ClosedShape.Instance;
                }));

        var (downstreamTask, networkTask) = graph.Run(Materializer);

        var downstream = await downstreamTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var networkItems = await networkTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        return (downstream, DecodeFrames(networkItems, skipPreface: true));
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.9")]
    public async Task Http2ConnectionFlowControl_should_decrement_connection_window_when_data_received_inbound()
    {
        // Send two DATA frames totalling 65535 bytes (exactly filling the default 65535 window).
        // DATA frames are assembled into HttpResponseMessage only when HEADERS precede them;
        // these arrive on unknown streams and are dropped — OutResponse receives nothing.
        var data1 = new DataFrame(streamId: 1, data: new byte[32768], endStream: false);
        var data2 = new DataFrame(streamId: 1, data: new byte[32767], endStream: true);

        var (downstream, _) = await RunAsync(data1, data2);

        Assert.Empty(downstream);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.9")]
    public async Task Http2ConnectionFlowControl_should_decrement_stream_window_when_data_received_inbound()
    {
        // Send DATA filling the entire stream window (65535 bytes) — should succeed.
        var data = new DataFrame(streamId: 1, data: new byte[65535], endStream: true);

        var (downstream, _) = await RunAsync(data);

        Assert.Empty(downstream);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.9")]
    public async Task Http2ConnectionFlowControl_should_send_connection_window_update_when_data_reaches_threshold()
    {
        // Explicit 65535-byte window → threshold = max(8192, 65535/4) = 16384.
        // Sending exactly 16384 bytes crosses the threshold in a single DATA frame.
        var stage = new Http20ConnectionStage(new Http2ConnectionConfig(InitialRecvWindowSize: 65535));
        var data = new DataFrame(streamId: 1, data: new byte[16384], endStream: true);

        var (_, serverBound) = await RunFlowAsync(stage, data);

        var connectionUpdate = serverBound
            .OfType<WindowUpdateFrame>()
            .FirstOrDefault(f => f.StreamId == 0);

        Assert.NotNull(connectionUpdate);
        Assert.Equal(16384, connectionUpdate.Increment);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.9")]
    public async Task Http2ConnectionFlowControl_should_send_stream_window_update_when_data_received_inbound()
    {
        var data = new DataFrame(streamId: 5, data: new byte[2048], endStream: true);

        var (_, serverBound) = await RunAsync(data);

        var streamUpdate = serverBound
            .OfType<WindowUpdateFrame>()
            .FirstOrDefault(f => f.StreamId == 5);

        Assert.NotNull(streamUpdate);
        Assert.Equal(2048, streamUpdate.Increment);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.9")]
    public async Task Http2ConnectionFlowControl_should_send_both_window_updates_when_threshold_crossed()
    {
        // Explicit 65535-byte window → threshold = 16384. Exactly 16384 bytes on a single
        // DATA frame crosses both the connection and stream thresholds simultaneously.
        var stage = new Http20ConnectionStage(new Http2ConnectionConfig(InitialRecvWindowSize: 65535));
        var data = new DataFrame(streamId: 3, data: new byte[16384], endStream: true);

        var (_, serverBound) = await RunFlowAsync(stage, data);

        var windowUpdates = serverBound.OfType<WindowUpdateFrame>().ToList();

        Assert.Equal(2, windowUpdates.Count);
        Assert.Contains(windowUpdates, f => f is { StreamId: 0, Increment: 16384 });
        Assert.Contains(windowUpdates, f => f is { StreamId: 3, Increment: 16384 });
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.9")]
    public async Task Http2ConnectionFlowControl_should_survive_and_log_when_connection_window_exceeded()
    {
        var data = new DataFrame(streamId: 1, data: new byte[65536], endStream: true);

        var downstreamSink = Sink.Seq<HttpResponseMessage>();
        var networkSink = Sink.Seq<IOutputItem>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, networkSink,
                (m1, m2) => (m1, m2),
                (b, dsSink, nwSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage());
                    var serverSource = b.Add(Source.From(FramesToInputs([data])));
                    var requestSource = b.Add(Source.Never<HttpRequestMessage>());

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutResponse).To(dsSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutNetwork).To(nwSink);

                    return ClosedShape.Instance;
                }));

        var (downstreamTask, networkTask) = graph.Run(Materializer);

        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        Assert.False(downstreamTask.IsFaulted,
            "Downstream task must not fault when connection window is exceeded");
        Assert.False(networkTask.IsFaulted,
            "Network task must not fault when connection window is exceeded");
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.9")]
    public async Task Http2ConnectionFlowControl_should_survive_and_log_when_stream_window_exceeded()
    {
        var data = new DataFrame(streamId: 1, data: new byte[65536], endStream: true);

        var downstreamSink = Sink.Seq<HttpResponseMessage>();
        var networkSink = Sink.Seq<IOutputItem>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, networkSink,
                (m1, m2) => (m1, m2),
                (b, dsSink, nwSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage());
                    var serverSource = b.Add(Source.From(FramesToInputs([data])));
                    var requestSource = b.Add(Source.Never<HttpRequestMessage>());

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutResponse).To(dsSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutNetwork).To(nwSink);

                    return ClosedShape.Instance;
                }));

        var (downstreamTask, networkTask) = graph.Run(Materializer);

        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        Assert.False(downstreamTask.IsFaulted,
            "Downstream task must not fault when stream window is exceeded");
        Assert.False(networkTask.IsFaulted,
            "Network task must not fault when stream window is exceeded");
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.9")]
    public async Task Http2ConnectionFlowControl_should_survive_and_log_when_outbound_flow_control_exceeded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var downstreamSink = Sink.Seq<HttpResponseMessage>();
        var networkSink = Sink.Seq<IOutputItem>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, networkSink,
                (m1, m2) => (m1, m2),
                (b, dsSink, nwSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage());
                    var serverSource = b.Add(Source.Never<IInputItem>());
                    var requestSource = b.Add(Source.Single<HttpRequestMessage>(request));

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutResponse).To(dsSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutNetwork).To(nwSink);

                    return ClosedShape.Instance;
                }));

        var (downstreamTask, networkTask) = graph.Run(Materializer);

        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        Assert.False(downstreamTask.IsFaulted,
            "Downstream task must not fault when outbound flow control window is exceeded");
        Assert.False(networkTask.IsFaulted,
            "Network task must not fault when outbound flow control window is exceeded");
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.9")]
    public async Task Http2ConnectionFlowControl_should_forward_data_when_outbound_data_within_window()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var networkSink = Sink.First<IOutputItem>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(networkSink,
                (b, nwSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage());
                    var serverSource = b.Add(Source.Never<IInputItem>());
                    var requestSource = b.Add(Source.Single<HttpRequestMessage>(request));
                    var ignoreSink = b.Add(Sink.Ignore<HttpResponseMessage>().MapMaterializedValue(_ => NotUsed.Instance));

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutResponse).To(ignoreSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutNetwork).To(nwSink);

                    return ClosedShape.Instance;
                }));

        var networkTask = graph.Run(Materializer);

        var firstItem = await networkTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // The combined stage emits the connection preface as its first output on OutNetwork.
        // Sink.First captures only this first item — a NetworkBuffer containing magic + SETTINGS + WINDOW_UPDATE.
        Assert.IsType<NetworkBuffer>(firstItem);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.9")]
    public async Task Http2ConnectionFlowControl_should_increment_connection_window_when_window_update_on_stream0()
    {
        var connWindowUpdate = new WindowUpdateFrame(streamId: 0, increment: 10000);
        var streamWindowUpdate = new WindowUpdateFrame(streamId: 1, increment: 10000);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var networkSink = Sink.Seq<IOutputItem>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(networkSink,
                (b, nwSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage());

                    // Server sends WINDOW_UPDATEs immediately, then a harmless SETTINGS ACK
                    // after a delay to keep InServer alive until the request has been processed.
                    var serverSource = b.Add(
                        Source.From(FramesToInputs([connWindowUpdate, streamWindowUpdate]))
                            .Concat(Source.From(FramesToInputs([new SettingsFrame([], isAck: true)]))
                                .InitialDelay(TimeSpan.FromMilliseconds(500))));

                    var requestSource = b.Add(
                        Source.Single<HttpRequestMessage>(request)
                            .InitialDelay(TimeSpan.FromMilliseconds(200)));

                    var ignoreSink = b.Add(Sink.Ignore<HttpResponseMessage>().MapMaterializedValue(_ => NotUsed.Instance));

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutResponse).To(ignoreSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutNetwork).To(nwSink);

                    return ClosedShape.Instance;
                }));

        var networkTask = graph.Run(Materializer);

        var networkItems = await networkTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var frames = DecodeFrames(networkItems, skipPreface: true);
        Assert.Contains(frames, f => f is HeadersFrame);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.9")]
    public async Task Http2ConnectionFlowControl_should_not_forward_window_update_to_out_response_when_stream0()
    {
        var windowUpdate = new WindowUpdateFrame(streamId: 0, increment: 5000);

        var (downstream, _) = await RunAsync(windowUpdate);

        Assert.Empty(downstream);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.9")]
    public async Task Http2ConnectionFlowControl_should_not_forward_window_update_to_out_response_when_stream_n()
    {
        var windowUpdate = new WindowUpdateFrame(streamId: 1, increment: 8192);

        var (downstream, _) = await RunAsync(windowUpdate);

        Assert.Empty(downstream);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.9")]
    public async Task Http2ConnectionFlowControl_should_not_forward_any_window_update_when_multiple_received_for_same_stream()
    {
        var wu1 = new WindowUpdateFrame(streamId: 3, increment: 1000);
        var wu2 = new WindowUpdateFrame(streamId: 3, increment: 2000);

        var (downstream, _) = await RunAsync(wu1, wu2);

        Assert.Empty(downstream);
    }
}
