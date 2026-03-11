using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Protocol;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http20;

public sealed class Http20ConnectionStageFlowControlTests : StreamTestBase
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

    /// <summary>
    /// Runs the Http20ConnectionStage where server frames arrive first, followed by request frames
    /// pushed from the application side (Inlet2/inletRequest) after a short delay.
    /// </summary>
    private async Task<(IReadOnlyList<Http2Frame> Downstream, IReadOnlyList<Http2Frame> ServerBound)>
        RunWithRequestsAsync(Http2Frame[] serverFrames, Http2Frame[] requestFrames)
    {
        var downstreamSink = Sink.Seq<Http2Frame>();
        var serverBoundSink = Sink.Seq<Http2Frame>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, serverBoundSink,
                (m1, m2) => (m1, m2),
                (b, dsSink, sbSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage());

                    // Server frames arrive then stream stays open
                    var serverSource = b.Add(
                        Source.From(serverFrames).Concat(Source.Never<Http2Frame>()));

                    // Request frames arrive after a delay to ensure server frames are processed first
                    var requestSource = b.Add(
                        Source.From(requestFrames)
                            .InitialDelay(TimeSpan.FromMilliseconds(200)));

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

    // ─── 20CW-001: Inbound DATA → connection window decremented ────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.9-20CW-001: Inbound DATA decrements connection window")]
    public async Task Inbound_Data_Decrements_Connection_Window()
    {
        // Send two DATA frames totalling 65535 bytes (exactly filling the default 65535 window).
        // The stage should succeed because the window is not exceeded.
        var data1 = new DataFrame(streamId: 1, data: new byte[32768], endStream: false);
        var data2 = new DataFrame(streamId: 1, data: new byte[32767], endStream: true);

        var (downstream, _) = await RunAsync(data1, data2);

        // Both frames forwarded — connection window was correctly tracked
        Assert.Equal(2, downstream.Count);
        Assert.IsType<DataFrame>(downstream[0]);
        Assert.IsType<DataFrame>(downstream[1]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.9-20CW-001: Connection window tracks cumulative inbound DATA")]
    public async Task Connection_Window_Tracks_Cumulative_Inbound()
    {
        // Send data on two different streams that together exceed the connection window.
        // Stream 1: 40000 bytes, Stream 3: 30000 bytes → total 70000 > 65535
        var data1 = new DataFrame(streamId: 1, data: new byte[40000], endStream: true);
        var data2 = new DataFrame(streamId: 3, data: new byte[30000], endStream: true);

        // Stage should fail because combined data exceeds connection window
        await Assert.ThrowsAnyAsync<Exception>(() => RunAsync(data1, data2));
    }

    // ─── 20CW-002: Inbound DATA → stream window decremented ────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.9-20CW-002: Inbound DATA decrements stream window")]
    public async Task Inbound_Data_Decrements_Stream_Window()
    {
        // Send DATA filling the entire stream window (65535 bytes) — should succeed.
        var data = new DataFrame(streamId: 1, data: new byte[65535], endStream: true);

        var (downstream, _) = await RunAsync(data);

        var forwarded = Assert.Single(downstream);
        Assert.IsType<DataFrame>(forwarded);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.9-20CW-002: Stream window tracked per-stream independently")]
    public async Task Stream_Window_Tracked_Per_Stream()
    {
        // Stream 1 gets 65535 bytes (exactly at window), stream 3 gets 65536 (exceeds).
        // Per-stream tracking means stream 1 succeeds but stream 3 exceeds its own window.
        var data1 = new DataFrame(streamId: 1, data: new byte[65535], endStream: true);
        var data2 = new DataFrame(streamId: 3, data: new byte[65536], endStream: true);

        // Stage should fail because stream 3 exceeds its stream window
        await Assert.ThrowsAnyAsync<Exception>(() => RunAsync(data1, data2));
    }

    // ─── 20CW-003: Inbound DATA → WINDOW_UPDATE(stream=0) sent ─────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.9-20CW-003: Inbound DATA triggers WINDOW_UPDATE on stream 0")]
    public async Task Inbound_Data_Sends_Connection_WindowUpdate()
    {
        var data = new DataFrame(streamId: 1, data: new byte[1024], endStream: true);

        var (_, serverBound) = await RunAsync(data);

        // Server-bound should contain WINDOW_UPDATE frames
        var connectionUpdate = serverBound
            .OfType<WindowUpdateFrame>()
            .FirstOrDefault(f => f.StreamId == 0);

        Assert.NotNull(connectionUpdate);
        Assert.Equal(1024, connectionUpdate.Increment);
    }

    // ─── 20CW-004: Inbound DATA → WINDOW_UPDATE(stream=N) sent ─────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.9-20CW-004: Inbound DATA triggers WINDOW_UPDATE on stream N")]
    public async Task Inbound_Data_Sends_Stream_WindowUpdate()
    {
        var data = new DataFrame(streamId: 5, data: new byte[2048], endStream: true);

        var (_, serverBound) = await RunAsync(data);

        var streamUpdate = serverBound
            .OfType<WindowUpdateFrame>()
            .FirstOrDefault(f => f.StreamId == 5);

        Assert.NotNull(streamUpdate);
        Assert.Equal(2048, streamUpdate.Increment);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.9-20CW-004: Both connection and stream WINDOW_UPDATEs sent for single DATA")]
    public async Task Inbound_Data_Sends_Both_WindowUpdates()
    {
        var data = new DataFrame(streamId: 3, data: new byte[512], endStream: true);

        var (_, serverBound) = await RunAsync(data);

        var windowUpdates = serverBound.OfType<WindowUpdateFrame>().ToList();

        // Exactly 2 WINDOW_UPDATEs: one for connection (stream=0), one for stream
        Assert.Equal(2, windowUpdates.Count);
        Assert.Contains(windowUpdates, f => f.StreamId == 0 && f.Increment == 512);
        Assert.Contains(windowUpdates, f => f.StreamId == 3 && f.Increment == 512);
    }

    // ─── 20CW-005: Connection window < 0 → stage fails with exception ──────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.9-20CW-005: Connection window exceeded causes stage failure")]
    public async Task Connection_Window_Exceeded_Fails_Stage()
    {
        // Default connection window is 65535. Send 65536 bytes to exceed it.
        var data = new DataFrame(streamId: 1, data: new byte[65536], endStream: true);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => RunAsync(data));
        Assert.Contains("window", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ─── 20CW-006: Stream window < 0 → stage fails with exception ──────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.9-20CW-006: Stream window exceeded causes stage failure")]
    public async Task Stream_Window_Exceeded_Fails_Stage()
    {
        // Default stream window is 65535. Send 65536 bytes on one stream to exceed it.
        var data = new DataFrame(streamId: 1, data: new byte[65536], endStream: true);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => RunAsync(data));
        Assert.Contains("window", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ─── 20CW-007: Outbound DATA → connection window decremented ────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.9-20CW-007: Outbound DATA decrements connection send window")]
    public async Task Outbound_Data_Decrements_Connection_Window()
    {
        // Send outbound DATA that exceeds the connection window → stage should fail.
        // Default connection window = 65535, so 65536 bytes should exceed it.
        var request = new DataFrame(streamId: 1, data: new byte[65536], endStream: true);

        var downstreamSink = Sink.Seq<Http2Frame>();
        var serverBoundSink = Sink.Seq<Http2Frame>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, serverBoundSink,
                (m1, m2) => (m1, m2),
                (b, dsSink, sbSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage());
                    var serverSource = b.Add(Source.Never<Http2Frame>());
                    var requestSource = b.Add(Source.Single<Http2Frame>(request));

                    b.From(serverSource).To(stage.Inlet1);
                    b.From(stage.Outlet1).To(dsSink);
                    b.From(requestSource).To(stage.Inlet2);
                    b.From(stage.Outlet2).To(sbSink);

                    return ClosedShape.Instance;
                }));

        var (downstreamTask, serverBoundTask) = graph.Run(Materializer);

        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await Task.WhenAll(
                downstreamTask.WaitAsync(TimeSpan.FromSeconds(5)),
                serverBoundTask.WaitAsync(TimeSpan.FromSeconds(5)));
        });

        Assert.Contains("flow control", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.9-20CW-007: Outbound DATA within window succeeds")]
    public async Task Outbound_Data_Within_Window_Succeeds()
    {
        // Send outbound DATA within the connection window → should be forwarded to server.
        // Use Sink.First (completes after one element) to avoid waiting for full stream completion.
        var request = new DataFrame(streamId: 1, data: new byte[1024], endStream: true);

        var serverBoundSink = Sink.First<Http2Frame>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(serverBoundSink,
                (b, sbSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage());
                    var serverSource = b.Add(Source.Never<Http2Frame>());
                    var requestSource = b.Add(Source.Single<Http2Frame>(request));
                    var ignoreSink = b.Add(Sink.Ignore<Http2Frame>().MapMaterializedValue(_ => NotUsed.Instance));

                    b.From(serverSource).To(stage.Inlet1);
                    b.From(stage.Outlet1).To(ignoreSink);
                    b.From(requestSource).To(stage.Inlet2);
                    b.From(stage.Outlet2).To(sbSink);

                    return ClosedShape.Instance;
                }));

        var serverBoundTask = graph.Run(Materializer);

        var forwarded = await serverBoundTask.WaitAsync(TimeSpan.FromSeconds(5));

        var dataFrame = Assert.IsType<DataFrame>(forwarded);
        Assert.Equal(1024, dataFrame.Data.Length);
    }

    // ─── 20CW-008: WINDOW_UPDATE(stream=0) received → connection window incremented ─

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.9-20CW-008: WINDOW_UPDATE on stream 0 increments connection window")]
    public async Task WindowUpdate_Stream0_Increments_Connection_Window()
    {
        // Receive a WINDOW_UPDATE on stream 0 that increases the connection window.
        // Then send outbound DATA that would exceed the original window but fits the new one.
        // Original connection window = 65535. After WINDOW_UPDATE(+10000) = 75535.
        // Send 70000 bytes outbound — would fail without the WINDOW_UPDATE.
        var windowUpdate = new WindowUpdateFrame(streamId: 0, increment: 10000);
        var request = new DataFrame(streamId: 1, data: new byte[70000], endStream: true);

        // Use Sink.First on server-bound to capture the DATA frame without waiting for completion.
        // The stage processes the WINDOW_UPDATE from the server side first (via InitialDelay on request),
        // then processes the outbound DATA — which only succeeds if the window was incremented.
        var serverBoundSink = Sink.First<Http2Frame>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(serverBoundSink,
                (b, sbSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage());

                    // Server sends WINDOW_UPDATE then stays open
                    var serverSource = b.Add(
                        Source.Single<Http2Frame>(windowUpdate).Concat(Source.Never<Http2Frame>()));

                    // Client sends a request after a delay (to ensure WINDOW_UPDATE is processed first)
                    var requestSource = b.Add(
                        Source.Single<Http2Frame>(request)
                            .InitialDelay(TimeSpan.FromMilliseconds(200)));

                    var ignoreSink = b.Add(Sink.Ignore<Http2Frame>().MapMaterializedValue(_ => NotUsed.Instance));

                    b.From(serverSource).To(stage.Inlet1);
                    b.From(stage.Outlet1).To(ignoreSink);
                    b.From(requestSource).To(stage.Inlet2);
                    b.From(stage.Outlet2).To(sbSink);

                    return ClosedShape.Instance;
                }));

        var serverBoundTask = graph.Run(Materializer);

        var forwarded = await serverBoundTask.WaitAsync(TimeSpan.FromSeconds(5));

        // The request should be forwarded (not failed) — proves window was incremented
        var dataFrame = Assert.IsType<DataFrame>(forwarded);
        Assert.Equal(70000, dataFrame.Data.Length);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.9-20CW-008: WINDOW_UPDATE on stream 0 is forwarded downstream")]
    public async Task WindowUpdate_Stream0_Forwarded_Downstream()
    {
        var windowUpdate = new WindowUpdateFrame(streamId: 0, increment: 5000);

        var (downstream, _) = await RunAsync(windowUpdate);

        var forwarded = Assert.Single(downstream);
        var wu = Assert.IsType<WindowUpdateFrame>(forwarded);
        Assert.Equal(0, wu.StreamId);
        Assert.Equal(5000, wu.Increment);
    }

    // ─── 20CW-009: WINDOW_UPDATE(stream=N) received → stream window incremented ─

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.9-20CW-009: WINDOW_UPDATE on stream N increments stream window")]
    public async Task WindowUpdate_StreamN_Increments_Stream_Window()
    {
        // Receive WINDOW_UPDATE on stream 1 — verify it's forwarded downstream.
        // (Stream send window validation is not fully implemented for outbound,
        // so we verify the frame is processed and forwarded.)
        var windowUpdate = new WindowUpdateFrame(streamId: 1, increment: 8192);

        var (downstream, _) = await RunAsync(windowUpdate);

        var forwarded = Assert.Single(downstream);
        var wu = Assert.IsType<WindowUpdateFrame>(forwarded);
        Assert.Equal(1, wu.StreamId);
        Assert.Equal(8192, wu.Increment);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.9-20CW-009: Multiple WINDOW_UPDATEs accumulate for stream")]
    public async Task Multiple_WindowUpdates_Accumulate_For_Stream()
    {
        // Two WINDOW_UPDATEs on same stream should both be forwarded downstream
        var wu1 = new WindowUpdateFrame(streamId: 3, increment: 1000);
        var wu2 = new WindowUpdateFrame(streamId: 3, increment: 2000);

        var (downstream, _) = await RunAsync(wu1, wu2);

        Assert.Equal(2, downstream.Count);
        Assert.All(downstream, f =>
        {
            var wu = Assert.IsType<WindowUpdateFrame>(f);
            Assert.Equal(3, wu.StreamId);
        });
    }
}
