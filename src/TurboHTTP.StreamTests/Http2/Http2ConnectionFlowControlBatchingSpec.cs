using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Tests.Shared;
using static TurboHTTP.StreamTests.Http2.Http2ConnectionTestHelper;

namespace TurboHTTP.StreamTests.Http2;

/// <summary>
/// Tests HTTP/2 flow-control batching: initial window configuration and
/// WINDOW_UPDATE accumulation with threshold flush per RFC 9113.
/// </summary>
[Trait("RFC", "RFC9113-6.9")]
public sealed class Http2ConnectionFlowControlBatchingSpec : StreamTestBase
{
    // Default window is 65535 → threshold = max(16384, 65535/4) = 16384.
    private const int DefaultThreshold = 16384;

    private async Task<(IReadOnlyList<HttpResponseMessage> Downstream, IReadOnlyList<Http2Frame> ServerBound)> RunAsync(
        int initialWindowSize,
        params Http2Frame[] serverFrames)
    {
        var downstreamSink = Sink.Seq<HttpResponseMessage>();
        var networkSink = Sink.Seq<IOutputItem>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, networkSink,
                (m1, m2) => (m1, m2),
                (b, dsSink, nwSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage(new Http2Options { InitialConnectionWindowSize = initialWindowSize }.ToEngineOptions()));
                    var serverSource = b.Add(Source.From(FramesToInputs(serverFrames)));
                    var requestSource = b.Add(Source.Never<HttpRequestMessage>());

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutResponse).To(dsSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutNetwork).To(nwSink);

                    return ClosedShape.Instance;
                }));

        var mat = graph.Run(Materializer);
        var (downstreamTask, networkTask) = (mat.Item1, mat.Item2);

        var downstream = await downstreamTask.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var networkItems = await networkTask.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        return (downstream, DecodeFrames(networkItems, skipPreface: true));
    }

    [Fact(Timeout = 5_000)]
    public void Http2Engine_should_have_64_mib_initial_connection_window_when_default_options_used()
    {
        var options = new Http2Options();

        Assert.Equal(64 * 1024 * 1024, options.InitialConnectionWindowSize);
    }

    [Fact(Timeout = 5_000)]
    public async Task Http2ConnectionFlowControlBatching_should_send_no_window_update_when_response_is_headers_only()
    {
        var headers = new HeadersFrame(
            streamId: 1,
            headerBlock: ReadOnlyMemory<byte>.Empty,
            endStream: true,
            endHeaders: true);

        var (_, serverBound) = await RunAsync(65535, headers);

        var windowUpdates = serverBound.OfType<WindowUpdateFrame>().ToList();

        Assert.Empty(windowUpdates);
    }

    [Fact(Timeout = 5_000)]
    public async Task Http2ConnectionFlowControlBatching_should_flush_stream_pending_on_stream_close_when_below_threshold()
    {
        // 1024 bytes is well below the 16384 threshold → no immediate WINDOW_UPDATE.
        // On stream close the stream-level pending is flushed; connection-level is NOT.
        var data = new DataFrame(streamId: 1, data: new byte[1024], endStream: true);

        var (_, serverBound) = await RunAsync(65535, data);

        var windowUpdates = serverBound.OfType<WindowUpdateFrame>().ToList();

        // Stream-level flushed; connection-level batched (not yet at threshold).
        var streamUpdate = Assert.Single(windowUpdates, f => f.StreamId == 1);
        Assert.Equal(1024, streamUpdate.Increment);
        Assert.DoesNotContain(windowUpdates, f => f.StreamId == 0);
    }

    [Fact(Timeout = 5_000)]
    public async Task Http2ConnectionFlowControlBatching_should_send_both_window_updates_when_threshold_crossed_in_single_frame()
    {
        // Exactly 16384 bytes crosses both connection and stream threshold at once.
        var data = new DataFrame(streamId: 1, data: new byte[DefaultThreshold], endStream: true);

        var (_, serverBound) = await RunAsync(65535, data);

        var windowUpdates = serverBound.OfType<WindowUpdateFrame>().ToList();

        Assert.Equal(2, windowUpdates.Count);
        Assert.Contains(windowUpdates, f => f is { StreamId: 0, Increment: DefaultThreshold });
        Assert.Contains(windowUpdates, f => f is { StreamId: 1, Increment: DefaultThreshold });
    }

    [Fact(Timeout = 5_000)]
    public async Task Http2ConnectionFlowControlBatching_should_send_single_batched_window_update_when_multiple_frames_accumulate_to_threshold()
    {
        // Two 8192-byte frames accumulate to 16384 → threshold crossed on second frame.
        var frame1 = new DataFrame(streamId: 1, data: new byte[8192], endStream: false);
        var frame2 = new DataFrame(streamId: 1, data: new byte[8192], endStream: true);

        var (_, serverBound) = await RunAsync(65535, frame1, frame2);

        var connectionUpdates = serverBound.OfType<WindowUpdateFrame>()
            .Where(f => f.StreamId == 0)
            .ToList();
        var streamUpdates = serverBound.OfType<WindowUpdateFrame>()
            .Where(f => f.StreamId == 1)
            .ToList();

        // Exactly one connection-level WINDOW_UPDATE with the full batched increment
        var connUpdate = Assert.Single(connectionUpdates);
        Assert.Equal(DefaultThreshold, connUpdate.Increment);

        // Exactly one stream-level WINDOW_UPDATE (threshold flush; stream close pending = 0)
        var streamUpdate = Assert.Single(streamUpdates);
        Assert.Equal(DefaultThreshold, streamUpdate.Increment);
    }

    [Fact(Timeout = 5_000)]
    public async Task Http2ConnectionFlowControlBatching_should_batch_streams_independently_when_two_streams_send_data_below_threshold()
    {
        // Stream 1: 16384 bytes → hits threshold on its own → stream WU(1) sent.
        // Stream 3: 8192 bytes → below threshold → stream WU(3) flushed only at close.
        var s1 = new DataFrame(streamId: 1, data: new byte[DefaultThreshold], endStream: true);
        var s3 = new DataFrame(streamId: 3, data: new byte[8192], endStream: true);

        var (_, serverBound) = await RunAsync(65535, s1, s3);

        var windowUpdates = serverBound.OfType<WindowUpdateFrame>().ToList();

        // Stream 1 threshold hit → WU(1, 16384)
        Assert.Contains(windowUpdates, f => f is { StreamId: 1, Increment: DefaultThreshold });

        // Stream 3 close-flush → WU(3, 8192)
        Assert.Contains(windowUpdates, f => f is { StreamId: 3, Increment: 8192 });
    }
}
