using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Streams.Stages.Decoding;

namespace TurboHTTP.StreamTests.Http3.Connection;

/// <summary>
/// Stage-behaviour tests for GOAWAY handling in <see cref="Http30ConnectionStage"/>.
/// Pushes GOAWAY frames into InServer and verifies observable effects:
/// frame absorption, outbound request dropping, and error absorption.
/// </summary>
/// <remarks>
/// Replaces deleted Protocol Layer test: 23_GoAwaySpec.cs.
/// RFC 9114 Section 5.2: GOAWAY — graceful connection shutdown.
/// Stream ID MUST be divisible by 4 (client-initiated bidirectional).
/// Stream ID MUST NOT increase on subsequent GOAWAYs.
/// After GOAWAY, new outbound requests MUST be dropped.
/// </remarks>
public sealed class Http30ConnectionGoAwaySpec : StreamTestBase
{
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

        var downstream = await downstreamTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var serverBound = await serverBoundTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        return (downstream, serverBound);
    }

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
                    // Use idle timeout so the stage self-completes after GOAWAY + dropped frames.
                    var stage = b.Add(new Http30ConnectionStage(TimeSpan.FromMilliseconds(500)));
                    // Concat with Never so InServer stays open while app frames are processed.
                    var serverSource = b.Add(
                        serverFrames.Length > 0
                            ? Source.From(serverFrames).Concat(Source.Never<Http3Frame>())
                            : Source.Never<Http3Frame>());
                    // Delay app frames so server GOAWAY is processed first.
                    var requestSource = b.Add(
                        Source.From(appFrames).InitialDelay(TimeSpan.FromMilliseconds(200)));

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

    // --- GOAWAY absorption (RFC 9114 Section 5.2) ---

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-5.2")]
    public async Task Http30ConnectionGoAway_should_absorb_goaway_with_stream_id_zero_when_received()
    {
        var settings = new Http3SettingsFrame([]);
        var goAway = new Http3GoAwayFrame(0);

        var (downstream, _) = await RunAsync(settings, goAway);

        Assert.Empty(downstream);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-5.2")]
    public async Task Http30ConnectionGoAway_should_absorb_goaway_with_valid_stream_id_when_received()
    {
        var settings = new Http3SettingsFrame([]);
        var goAway = new Http3GoAwayFrame(8);

        var (downstream, _) = await RunAsync(settings, goAway);

        Assert.Empty(downstream);
    }

    // --- Decreasing GOAWAY stream IDs (RFC 9114 Section 5.2) ---

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-5.2")]
    public async Task Http30ConnectionGoAway_should_accept_decreasing_stream_ids_when_multiple_goaway_received()
    {
        var settings = new Http3SettingsFrame([]);
        var goAway1 = new Http3GoAwayFrame(12);
        var goAway2 = new Http3GoAwayFrame(8);
        var goAway3 = new Http3GoAwayFrame(4);

        var (downstream, _) = await RunAsync(settings, goAway1, goAway2, goAway3);

        // All GOAWAYs absorbed — stage completes normally
        Assert.Empty(downstream);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-5.2")]
    public async Task Http30ConnectionGoAway_should_accept_same_stream_id_when_repeated_goaway_received()
    {
        var settings = new Http3SettingsFrame([]);
        var goAway1 = new Http3GoAwayFrame(8);
        var goAway2 = new Http3GoAwayFrame(8);

        var (downstream, _) = await RunAsync(settings, goAway1, goAway2);

        Assert.Empty(downstream);
    }

    // --- Increasing GOAWAY stream ID (RFC 9114 Section 5.2 violation, absorbed) ---

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-5.2")]
    public async Task Http30ConnectionGoAway_should_absorb_increasing_stream_id_error_when_invalid_goaway_received()
    {
        var settings = new Http3SettingsFrame([]);
        var goAway1 = new Http3GoAwayFrame(4);
        var goAway2 = new Http3GoAwayFrame(8); // Violation: ID increased
        var data = new Http3DataFrame(new byte[] { 0x01 });

        // Stage absorbs the IdError and sets GoAwayReceived = true.
        // No stage failure — subsequent frames still route.
        var (downstream, _) = await RunAsync(settings, goAway1, goAway2, data);

        // DATA should still be forwarded (stage is alive)
        Assert.Single(downstream);
        Assert.IsType<Http3DataFrame>(downstream[0]);
    }

    // --- Invalid GOAWAY stream ID (RFC 9114 Section 5.2 violation, absorbed) ---

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-5.2")]
    public async Task Http30ConnectionGoAway_should_absorb_invalid_stream_id_error_when_not_divisible_by_four()
    {
        var settings = new Http3SettingsFrame([]);
        var goAway = new Http3GoAwayFrame(5); // Not divisible by 4
        var data = new Http3DataFrame(new byte[] { 0x02 });

        // IdError is absorbed by HandleGoAway — stage continues.
        var (downstream, _) = await RunAsync(settings, goAway, data);

        Assert.Single(downstream);
        Assert.IsType<Http3DataFrame>(downstream[0]);
    }

    [Theory(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-5.2")]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(9)]
    public async Task Http30ConnectionGoAway_should_absorb_non_client_initiated_stream_id_when_received(long invalidStreamId)
    {
        var settings = new Http3SettingsFrame([]);
        var goAway = new Http3GoAwayFrame(invalidStreamId);

        // Should not fail the stage
        var (downstream, _) = await RunAsync(settings, goAway);

        Assert.Empty(downstream);
    }

    // --- Outbound request dropping after GOAWAY (RFC 9114 Section 5.2) ---

    [Fact(Timeout = 15_000)]
    [Trait("RFC", "RFC9114-5.2")]
    public async Task Http30ConnectionGoAway_should_drop_outbound_headers_when_goaway_already_received()
    {
        var settings = new Http3SettingsFrame([]);
        var goAway = new Http3GoAwayFrame(0);
        var appHeaders = new Http3HeadersFrame(new byte[] { 0x00, 0x00 });

        var (_, serverBound) = await RunWithAppAsync(
            [settings, goAway],
            [appHeaders]);

        // After GOAWAY, outbound HEADERS must be dropped — not forwarded to server.
        Assert.DoesNotContain(serverBound, f => f is Http3HeadersFrame);
    }

    [Fact(Timeout = 15_000)]
    [Trait("RFC", "RFC9114-5.2")]
    public async Task Http30ConnectionGoAway_should_drop_outbound_data_when_goaway_already_received()
    {
        var settings = new Http3SettingsFrame([]);
        var goAway = new Http3GoAwayFrame(0);
        var appData = new Http3DataFrame(new byte[] { 0x01, 0x02, 0x03 });

        var (_, serverBound) = await RunWithAppAsync(
            [settings, goAway],
            [appData]);

        Assert.DoesNotContain(serverBound, f => f is Http3DataFrame);
    }

    // --- GOAWAY followed by server response frames ---

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-5.2")]
    public async Task Http30ConnectionGoAway_should_still_forward_server_data_after_goaway_received()
    {
        var settings = new Http3SettingsFrame([]);
        var goAway = new Http3GoAwayFrame(4);
        var data = new Http3DataFrame(new byte[] { 0xAA, 0xBB });

        var (downstream, _) = await RunAsync(settings, goAway, data);

        // GOAWAY only stops new outbound requests; in-flight server responses still arrive.
        Assert.Single(downstream);
        Assert.IsType<Http3DataFrame>(downstream[0]);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-5.2")]
    public async Task Http30ConnectionGoAway_should_still_forward_server_headers_after_goaway_received()
    {
        var settings = new Http3SettingsFrame([]);
        var goAway = new Http3GoAwayFrame(4);
        var headers = new Http3HeadersFrame(new byte[] { 0x00, 0x00 });

        var (downstream, _) = await RunAsync(settings, goAway, headers);

        Assert.Single(downstream);
        Assert.IsType<Http3HeadersFrame>(downstream[0]);
    }
}
