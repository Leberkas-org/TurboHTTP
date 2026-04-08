using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Streams.Stages.Decoding;

namespace TurboHTTP.StreamTests.Http3.Connection;

/// <summary>
/// Stage-behaviour tests for idle timeout in <see cref="Http30ConnectionStage"/>.
/// Verifies that the TimerGraphStageLogic-based idle check sends GOAWAY and
/// completes the stage when no activity occurs within the configured timeout.
/// </summary>
/// <remarks>
/// Replaces deleted Protocol Layer test: 26_IdleTimeoutSpec.cs.
/// RFC 9114 Section 5.1: Idle timeout negotiation and connection closure.
/// Note: The deleted handler tests used injectable DateTime for deterministic timing.
/// Stage-behaviour tests rely on short real timeouts since ConnectionState uses
/// DateTime.UtcNow internally.
/// </remarks>
public sealed class Http30ConnectionIdleTimeoutSpec : StreamTestBase
{
    private async Task<(IReadOnlyList<Http3Frame> Downstream, IReadOnlyList<Http3Frame> ServerBound)> RunAsync(
        TimeSpan idleTimeout, bool keepServerOpen, params Http3Frame[] serverFrames)
    {
        var downstreamSink = Sink.Seq<Http3Frame>();
        var serverBoundSink = Sink.Seq<Http3Frame>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, serverBoundSink,
                (m1, m2) => (m1, m2),
                (b, dsSink, sbSink) =>
                {
                    var stage = b.Add(new Http30ConnectionStage(idleTimeout));
                    var serverSource = b.Add(
                        keepServerOpen
                            ? (serverFrames.Length > 0
                                ? Source.From(serverFrames).Concat(Source.Never<Http3Frame>())
                                : Source.Never<Http3Frame>())
                            : Source.From(serverFrames));
                    var requestSource = b.Add(Source.Never<Http3Frame>());

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutApp).To(dsSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutServer).To(sbSink);

                    return ClosedShape.Instance;
                }));

        var (downstreamTask, serverBoundTask) = graph.Run(Materializer);

        var downstream = await downstreamTask.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var serverBound = await serverBoundTask.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        return (downstream, serverBound);
    }

    // --- Idle timeout expiry (RFC 9114 Section 5.1) ---

    [Fact(Timeout = 15_000)]
    [Trait("RFC", "RFC9114-5.1")]
    public async Task Http30ConnectionIdleTimeout_should_send_goaway_when_idle_timeout_expires_with_no_streams()
    {
        var (_, serverBound) = await RunAsync(TimeSpan.FromMilliseconds(200), keepServerOpen: true);

        Assert.Contains(serverBound, f => f is Http3GoAwayFrame);
    }

    [Fact(Timeout = 15_000)]
    [Trait("RFC", "RFC9114-5.1")]
    public async Task Http30ConnectionIdleTimeout_should_send_goaway_with_stream_id_zero_when_expired()
    {
        var (_, serverBound) = await RunAsync(TimeSpan.FromMilliseconds(200), keepServerOpen: true);

        var goAway = serverBound.OfType<Http3GoAwayFrame>().FirstOrDefault();
        Assert.NotNull(goAway);
        Assert.Equal(0L, goAway.StreamId);
    }

    [Fact(Timeout = 15_000)]
    [Trait("RFC", "RFC9114-5.1")]
    public async Task Http30ConnectionIdleTimeout_should_complete_stage_when_idle_timeout_expires()
    {
        // RunAsync completes only if the stage completes (sinks collect all frames).
        // If the stage hangs, the WaitAsync timeout will fire.
        var (downstream, serverBound) = await RunAsync(TimeSpan.FromMilliseconds(200), keepServerOpen: true);

        // Stage completed — downstream is empty, serverBound has GOAWAY.
        Assert.Empty(downstream);
        Assert.Contains(serverBound, f => f is Http3GoAwayFrame);
    }

    // --- Timeout disabled (RFC 9114 Section 5.1) ---

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-5.1")]
    public async Task Http30ConnectionIdleTimeout_should_not_send_goaway_when_timeout_is_zero()
    {
        // Zero timeout = disabled. Feed frames and let source complete normally.
        var settings = new Http3SettingsFrame([]);
        var data = new Http3DataFrame(new byte[] { 0x01 });

        var (_, serverBound) = await RunAsync(TimeSpan.Zero, keepServerOpen: false, settings, data);

        Assert.DoesNotContain(serverBound, f => f is Http3GoAwayFrame);
    }

    // --- Activity resets idle timer (RFC 9114 Section 5.1) ---

    [Fact(Timeout = 15_000)]
    [Trait("RFC", "RFC9114-5.1")]
    public async Task Http30ConnectionIdleTimeout_should_reset_timer_when_server_frames_arrive()
    {
        // Use a 300ms timeout. Feed SETTINGS immediately (resets timer),
        // then let the source finish. The stage should NOT have sent GOAWAY
        // before it saw the SETTINGS frame.
        var settings = new Http3SettingsFrame([]);
        var data = new Http3DataFrame(new byte[] { 0x01 });

        var (downstream, serverBound) = await RunAsync(
            TimeSpan.FromMilliseconds(300), keepServerOpen: false, settings, data);

        // DATA was forwarded before any idle timeout could fire.
        Assert.Single(downstream);
        Assert.IsType<Http3DataFrame>(downstream[0]);
    }

    // --- Constructor validation (RFC 9114 Section 5.1) ---

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void Http30ConnectionIdleTimeout_should_throw_when_negative_timeout_provided()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Http30ConnectionStage(TimeSpan.FromSeconds(-1)));
    }

    // --- ComputeEffectiveTimeout static utility (RFC 9114 Section 5.1) ---

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void Http30ConnectionIdleTimeout_should_return_minimum_when_both_timeouts_provided()
    {
        var effective = Http30ConnectionStage.ComputeEffectiveTimeout(
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15));

        Assert.Equal(TimeSpan.FromSeconds(15), effective);
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void Http30ConnectionIdleTimeout_should_return_remote_when_local_is_zero()
    {
        var effective = Http30ConnectionStage.ComputeEffectiveTimeout(
            TimeSpan.Zero, TimeSpan.FromSeconds(20));

        Assert.Equal(TimeSpan.FromSeconds(20), effective);
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void Http30ConnectionIdleTimeout_should_return_local_when_remote_is_zero()
    {
        var effective = Http30ConnectionStage.ComputeEffectiveTimeout(
            TimeSpan.FromSeconds(25), TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromSeconds(25), effective);
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void Http30ConnectionIdleTimeout_should_return_zero_when_both_timeouts_are_zero()
    {
        var effective = Http30ConnectionStage.ComputeEffectiveTimeout(
            TimeSpan.Zero, TimeSpan.Zero);

        Assert.Equal(TimeSpan.Zero, effective);
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void Http30ConnectionIdleTimeout_should_throw_when_negative_local_timeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Http30ConnectionStage.ComputeEffectiveTimeout(
                TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(10)));
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void Http30ConnectionIdleTimeout_should_throw_when_negative_remote_timeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Http30ConnectionStage.ComputeEffectiveTimeout(
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(-1)));
    }
}
