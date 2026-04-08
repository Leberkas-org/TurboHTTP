using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Streams.Stages.Decoding;

namespace TurboHTTP.StreamTests.Http3;

/// <summary>
/// Tests HTTP/3 idle timeout integration in the connection stage per RFC 9114 §5.1.
/// Verifies that idle timeout logic inside <see cref="Http30ConnectionStage"/>
/// sends GOAWAY and completes the stage when idle timeout expires with no active streams.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http30ConnectionStage"/>.
/// RFC 9114 §5.1: An HTTP/3 connection can be idle for some time. Endpoints SHOULD send GOAWAY
/// before closing an idle connection. The idle timeout is reconciled between local and remote values.
/// </remarks>
public sealed class Http30IdleTimeoutSpec : StreamTestBase
{
    /// <summary>
    /// Runs the Http30ConnectionStage with a specific idle timeout and the given server frames.
    /// Returns (downstream frames from OutApp, server-bound frames from OutServer).
    /// </summary>
    private Task<(IReadOnlyList<Http3Frame> Downstream, IReadOnlyList<Http3Frame> ServerBound)> RunAsync(
        TimeSpan idleTimeout, params Http3Frame[] serverFrames)
        => RunCoreAsync(idleTimeout, keepServerOpen: true, serverFrames);

    private async Task<(IReadOnlyList<Http3Frame> Downstream, IReadOnlyList<Http3Frame> ServerBound)> RunCoreAsync(
        TimeSpan idleTimeout, bool keepServerOpen, Http3Frame[] serverFrames)
    {
        var downstreamSink = Sink.Seq<Http3Frame>();
        var serverBoundSink = Sink.Seq<Http3Frame>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, serverBoundSink,
                (m1, m2) => (m1, m2),
                (b, dsSink, sbSink) =>
                {
                    var stage = b.Add(new Http30ConnectionStage(idleTimeout));
                    // When keepServerOpen is true, concatenate with Never so the inlet
                    // stays open until the stage itself completes (e.g. idle timeout).
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

    // Idle Timeout Expiry (RFC 9114 §5.1)

    [Fact(Timeout = 15_000)]
    [Trait("RFC", "RFC9114-5.1")]
    public async Task Http30IdleTimeout_should_send_goaway_and_complete_when_idle_timeout_expires_with_no_active_streams()
    {
        // Use a very short idle timeout so the timer fires quickly.
        var (_, serverBound) = await RunAsync(TimeSpan.FromMilliseconds(200));

        // The stage should emit SETTINGS + MAX_PUSH_ID from PreStart,
        // then GOAWAY when idle timeout expires.
        Assert.Contains(serverBound, f => f is Http3GoAwayFrame);
    }

    [Fact(Timeout = 15_000)]
    [Trait("RFC", "RFC9114-5.1")]
    public async Task Http30IdleTimeout_should_send_goaway_with_stream_id_zero_when_idle_timeout_expires()
    {
        var (_, serverBound) = await RunAsync(TimeSpan.FromMilliseconds(200));

        var goAway = serverBound.OfType<Http3GoAwayFrame>().FirstOrDefault();
        Assert.NotNull(goAway);
        Assert.Equal(0L, goAway.StreamId);
    }

    // Timeout Disabled (RFC 9114 §5.1)

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-5.1")]
    public async Task Http30IdleTimeout_should_not_send_goaway_when_timeout_is_zero()
    {
        // TimeSpan.Zero disables idle timeout; feed frames and let source complete.
        var (_, serverBound) = await RunCoreAsync(
            TimeSpan.Zero,
            keepServerOpen: false,
            [new Http3SettingsFrame([]), new Http3DataFrame(new byte[] { 0x01 })]);

        Assert.DoesNotContain(serverBound, f => f is Http3GoAwayFrame);
    }

    // Constructor Validation

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void Http30IdleTimeout_should_throw_argument_out_of_range_when_negative_timeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Http30ConnectionStage(TimeSpan.FromSeconds(-1)));
    }

    // Idle Timeout Handler Behavior

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void Http30IdleTimeout_should_return_minimum_when_both_timeouts_provided()
    {
        var local = TimeSpan.FromSeconds(30);
        var remote = TimeSpan.FromSeconds(15);

        var effective = Http30ConnectionStage.ComputeEffectiveTimeout(local, remote);

        Assert.Equal(TimeSpan.FromSeconds(15), effective);
    }

    [Fact]
    [Trait("RFC", "RFC9114-5.1")]
    public void Http30IdleTimeout_should_return_remote_when_local_timeout_is_zero()
    {
        var local = TimeSpan.Zero;
        var remote = TimeSpan.FromSeconds(20);

        var effective = Http30ConnectionStage.ComputeEffectiveTimeout(local, remote);

        Assert.Equal(TimeSpan.FromSeconds(20), effective);
    }
}
