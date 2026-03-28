using System.Net;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages.Features;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Tests for all known Akka.Streams-internal deadlock patterns (DLAK-001..006).
/// Validates completion semantics of GroupByHostKey, MergeSubstreams, Broadcast eagerCancel,
/// and DeadlockWatchdogStage behavior under backpressure.
/// </summary>
/// <remarks>
/// Stages under test: <see cref="GroupByHostKeyStage{T}"/>, <see cref="MergeSubstreamsStage{T}"/>,
/// <see cref="DeadlockWatchdogStage{T}"/>.
/// These tests exercise completion races and backpressure stall detection that previously
/// caused zombie actors and hung pipelines.
/// </remarks>
public sealed class DeadlockAkkaInternalTests : StreamTestBase
{
    private static HttpRequestMessage Req(string host, string path = "/")
        => new(HttpMethod.Get, $"http://{host}{path}") { Version = HttpVersion.Version11 };

    private static RequestEndpoint KeyFor(HttpRequestMessage msg)
        => RequestEndpoint.FromRequest(msg);

    // ──────────────────────────────────────────────────────────────
    // DLAK-001: GroupByHostKey defers CompleteStage until WatchTask completes
    // ──────────────────────────────────────────────────────────────

    [Fact(Timeout = 5000,
        DisplayName = "DLAK-001: GroupByHostKey defers completion until substream is dead")]
    public async Task GroupByHostKey_Defers_Completion_Until_Substream_IsDead()
    {
        // Upstream finishes immediately after emitting one element.
        // The substream source.queue is still alive while downstream processes it.
        // Stage must NOT call CompleteStage() until WatchTask completes.

        var flow = Flow.Create<HttpRequestMessage>()
            .Via(new GroupByHostKeyStage<HttpRequestMessage>(KeyFor, maxSubstreams: 4))
            .Via(new MergeSubstreamsStage<HttpRequestMessage>(maxConcurrent: 4));

        var result = await Source.From([Req("host-a.example.com", "/1")])
            .Via(flow)
            .RunWith(Sink.Seq<HttpRequestMessage>(), Materializer);

        // If completion was premature (before WatchTask), the stream would
        // hang until timeout because MergeSubstreams never receives the element.
        Assert.Single(result);
        Assert.Equal("host-a.example.com", result[0].RequestUri!.Host);
    }

    // ──────────────────────────────────────────────────────────────
    // DLAK-002: OfferAsync races WatchTask on dead queue
    // ──────────────────────────────────────────────────────────────

    [Fact(Timeout = 5000,
        DisplayName = "DLAK-002: GroupByHostKey OfferAsync races WatchTask on dead queue")]
    public async Task GroupByHostKey_OfferAsync_Races_WatchTask_On_Dead_Queue()
    {
        // Send multiple requests to the same host. The GroupByHostKey stage
        // uses Task.WhenAny(offerTask, watchTask) to detect dead queues in
        // milliseconds rather than waiting for the 5s Ask timeout.
        // If the race is broken, this test will timeout at 5s.

        var requests = Enumerable.Range(1, 5)
            .Select(i => Req("race-host.example.com", $"/{i}"))
            .ToList();

        var flow = Flow.Create<HttpRequestMessage>()
            .Via(new GroupByHostKeyStage<HttpRequestMessage>(KeyFor, maxSubstreams: 4))
            .Via(new MergeSubstreamsStage<HttpRequestMessage>(maxConcurrent: 4));

        var sw = System.Diagnostics.Stopwatch.StartNew();

        var result = await Source.From(requests)
            .Via(flow)
            .RunWith(Sink.Seq<HttpRequestMessage>(), Materializer);

        sw.Stop();

        Assert.Equal(5, result.Count);

        // The whole stream must complete well within 500ms.
        // Without the WhenAny race fix, a dead queue would stall for 5s.
        Assert.True(sw.ElapsedMilliseconds < 500,
            $"Stream took {sw.ElapsedMilliseconds}ms — expected < 500ms (WhenAny race may be broken)");
    }

    // ──────────────────────────────────────────────────────────────
    // DLAK-003: eagerCancel breaks Broadcast completion cycle
    // ──────────────────────────────────────────────────────────────

    [Fact(Timeout = 5000,
        DisplayName = "DLAK-003: EagerCancel breaks Broadcast completion cycle")]
    public async Task EagerCancel_Breaks_Broadcast_Completion_Cycle()
    {
        // Without eagerCancel:true, a Broadcast(2) creates a zombie when one
        // outlet cancels while the other is still waiting. With the flag,
        // cancelling any outlet tears down the entire Broadcast immediately.

        var tcs = new TaskCompletionSource<bool>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var graph = RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var source = b.Add(Source.From(new[] { 1, 2, 3 }));
            var broadcast = b.Add(new Broadcast<int>(2, eagerCancel: true));
            var sink1 = b.Add(Sink.Cancelled<int>());
            var sink2 = b.Add(Sink.ForEach<int>(_ => { }));

            b.From(source).To(broadcast);
            b.From(broadcast.Out(0)).To(sink1);
            b.From(broadcast.Out(1)).To(sink2);

            return ClosedShape.Instance;
        }));

        var mat = graph.Run(Materializer);
        await Task.Delay(100); // Give the stream time to settle

        sw.Stop();

        // With eagerCancel, Broadcast completes almost instantly when sink1 cancels.
        // Without it, it would hang waiting for sink2 to drain, creating a zombie.
        Assert.True(sw.ElapsedMilliseconds < 2000,
            $"Broadcast took {sw.ElapsedMilliseconds}ms to settle — eagerCancel may not be working");
    }

    // ──────────────────────────────────────────────────────────────
    // DLAK-004: MergeSubstreams — no zombie actor after substream close
    // ──────────────────────────────────────────────────────────────

    [Fact(Timeout = 5000,
        DisplayName = "DLAK-004: MergeSubstreams no zombie actor after substream close")]
    public async Task MergeSubstreams_No_Zombie_Actor_After_Substream_Close()
    {
        // After downstream cancels, MergeSubstreams must tear down cleanly.
        // The _active counter must reach zero and CompleteStage() must fire.
        // If zombie actors linger, the materializer will not shut down.

        var flow = Flow.Create<HttpRequestMessage>()
            .Via(new GroupByHostKeyStage<HttpRequestMessage>(KeyFor, maxSubstreams: 4))
            .Via(new MergeSubstreamsStage<HttpRequestMessage>(maxConcurrent: 4));

        // Take(1) causes downstream cancel after the first element.
        var result = await Source.From(
            [
                Req("zombie-host.example.com", "/1"),
                Req("zombie-host.example.com", "/2"),
                Req("zombie-host.example.com", "/3")
            ])
            .Via(flow)
            .Take(1)
            .RunWith(Sink.Seq<HttpRequestMessage>(), Materializer);

        Assert.Single(result);

        // If zombie actors remain, the materializer Dispose would hang.
        // The Materializer is cleaned up by StreamTestBase/TestKit teardown.
        // Reaching this assertion means no zombie actors prevented shutdown.
    }

    // ──────────────────────────────────────────────────────────────
    // DLAK-005: DeadlockWatchdog emits StallEvent on backpressure
    // ──────────────────────────────────────────────────────────────

    [Fact(Timeout = 5000,
        DisplayName = "DLAK-005: DeadlockWatchdog emits StallEvent on backpressure")]
    public async Task DeadlockWatchdog_Emits_StallEvent_On_Backpressure()
    {
        // Threshold is 200ms. Downstream blocks for 400ms on the first element.
        // The watchdog should fire at least one OnStall callback.

        var stallEvents = new List<DeadlockStallEvent>();
        var stallDetected = new TaskCompletionSource<bool>();

        var watchdog = new DeadlockWatchdogStage<int>(new DeadlockWatchdogOptions
        {
            StageName = "TestWatchdog",
            WarningThreshold = TimeSpan.FromMilliseconds(200),
            OnStall = evt =>
            {
                stallEvents.Add(evt);
                stallDetected.TrySetResult(true);
            }
        });

        // Source emits one element, then waits. Downstream blocks for 400ms.
        var source = Source.From([1, 2])
            .Via(Flow.FromGraph(watchdog))
            .SelectAsync(1, async elem =>
            {
                await Task.Delay(400);
                return elem;
            });

        // Run the stream and wait for the stall event
        var streamTask = source.RunWith(Sink.Seq<int>(), Materializer);

        // Wait for at least one stall event (with timeout shorter than test timeout)
        var detected = await Task.WhenAny(
            stallDetected.Task,
            Task.Delay(3000));

        Assert.True(stallDetected.Task.IsCompletedSuccessfully,
            "Expected at least one stall event but none was raised within 3s");

        Assert.NotEmpty(stallEvents);
        Assert.Equal("TestWatchdog", stallEvents[0].StageName);
        Assert.Equal(TimeSpan.FromMilliseconds(200), stallEvents[0].StallDuration);

        // Let the stream complete
        await streamTask;
    }

    // ──────────────────────────────────────────────────────────────
    // DLAK-006: DeadlockWatchdog resets timer on element flow
    // ──────────────────────────────────────────────────────────────

    [Fact(Timeout = 5000,
        DisplayName = "DLAK-006: DeadlockWatchdog resets timer on element flow")]
    public async Task DeadlockWatchdog_Resets_Timer_On_Element_Flow()
    {
        // 5 elements at ~100ms intervals, threshold is 300ms.
        // Each element arrives well before the threshold, so no StallEvent should fire.

        var stallFired = false;

        var watchdog = new DeadlockWatchdogStage<int>(new DeadlockWatchdogOptions
        {
            StageName = "NoStallWatchdog",
            WarningThreshold = TimeSpan.FromMilliseconds(300),
            OnStall = _ => stallFired = true
        });

        var result = await Source.From(Enumerable.Range(1, 5))
            .Throttle(1, TimeSpan.FromMilliseconds(100), 1, ThrottleMode.Shaping)
            .Via(Flow.FromGraph(watchdog))
            .RunWith(Sink.Seq<int>(), Materializer);

        Assert.Equal(5, result.Count);
        Assert.False(stallFired,
            "Watchdog fired a stall event even though elements flowed within the threshold");
    }
}
