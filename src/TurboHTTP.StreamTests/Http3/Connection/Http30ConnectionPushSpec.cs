using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Streams.Stages.Decoding;

namespace TurboHTTP.StreamTests.Http3.Connection;

/// <summary>
/// Stage-behaviour tests for push-related frame handling in <see cref="Http30ConnectionStage"/>.
/// Verifies PUSH_PROMISE rejection (push limit is 0), CANCEL_PUSH absorption,
/// and MAX_PUSH_ID absorption through observable stage output.
/// </summary>
/// <remarks>
/// Replaces deleted Protocol Layer tests: 27_MaxPushIdSpec.cs, 28_PushPromiseValidationSpec.cs,
/// 29_CancelPushSpec.cs, 32_PushLimitingSpec.cs.
/// RFC 9114 Section 7.2.3: CANCEL_PUSH frame.
/// RFC 9114 Section 7.2.5: PUSH_PROMISE frame.
/// RFC 9114 Section 7.2.7: MAX_PUSH_ID frame.
/// RFC 9114 Section 10.5: Push limit and ExcessiveLoad protection.
/// </remarks>
public sealed class Http30ConnectionPushSpec : StreamTestBase
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

    // --- CANCEL_PUSH absorption (RFC 9114 Section 7.2.3) ---

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.3")]
    public async Task Http30ConnectionPush_should_absorb_cancel_push_when_received_from_server()
    {
        var settings = new Http3SettingsFrame([]);
        var cancelPush = new Http3CancelPushFrame(0);

        var (downstream, _) = await RunAsync(settings, cancelPush);

        Assert.Empty(downstream);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.3")]
    public async Task Http30ConnectionPush_should_absorb_multiple_cancel_push_frames_when_received()
    {
        var settings = new Http3SettingsFrame([]);
        var cancel1 = new Http3CancelPushFrame(0);
        var cancel2 = new Http3CancelPushFrame(1);
        var cancel3 = new Http3CancelPushFrame(2);

        var (downstream, _) = await RunAsync(settings, cancel1, cancel2, cancel3);

        Assert.Empty(downstream);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.3")]
    public async Task Http30ConnectionPush_should_continue_routing_after_cancel_push_received()
    {
        var settings = new Http3SettingsFrame([]);
        var cancelPush = new Http3CancelPushFrame(0);
        var data = new Http3DataFrame(new byte[] { 0x01 });

        var (downstream, _) = await RunAsync(settings, cancelPush, data);

        Assert.Single(downstream);
        Assert.IsType<Http3DataFrame>(downstream[0]);
    }

    // --- PUSH_PROMISE rejection (RFC 9114 Section 10.5) ---

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-10.5")]
    public async Task Http30ConnectionPush_should_reject_push_promise_when_push_limit_is_zero()
    {
        var settings = new Http3SettingsFrame([]);
        var pushPromise = new Http3PushPromiseFrame(0, new byte[] { 0x00 });

        var (downstream, _) = await RunAsync(settings, pushPromise);

        // PUSH_PROMISE is rejected (logged) and absorbed — never forwarded to app.
        Assert.Empty(downstream);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-10.5")]
    public async Task Http30ConnectionPush_should_continue_routing_after_push_promise_rejected()
    {
        var settings = new Http3SettingsFrame([]);
        var pushPromise = new Http3PushPromiseFrame(0, new byte[] { 0x00 });
        var data = new Http3DataFrame(new byte[] { 0xAA });

        var (downstream, _) = await RunAsync(settings, pushPromise, data);

        // PUSH_PROMISE rejection doesn't kill the stage — DATA still passes through.
        Assert.Single(downstream);
        Assert.IsType<Http3DataFrame>(downstream[0]);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-10.5")]
    public async Task Http30ConnectionPush_should_reject_multiple_push_promises_when_limit_is_zero()
    {
        var settings = new Http3SettingsFrame([]);
        var push1 = new Http3PushPromiseFrame(0, new byte[] { 0x00 });
        var push2 = new Http3PushPromiseFrame(1, new byte[] { 0x00 });
        var data = new Http3DataFrame(new byte[] { 0xBB });

        var (downstream, _) = await RunAsync(settings, push1, push2, data);

        // Both PUSH_PROMISEs rejected, DATA still arrives.
        Assert.Single(downstream);
        Assert.IsType<Http3DataFrame>(downstream[0]);
    }

    // --- MAX_PUSH_ID absorption (RFC 9114 Section 7.2.7) ---

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.7")]
    public async Task Http30ConnectionPush_should_absorb_max_push_id_when_received_from_server()
    {
        var settings = new Http3SettingsFrame([]);
        var maxPushId = new Http3MaxPushIdFrame(10);

        var (downstream, _) = await RunAsync(settings, maxPushId);

        Assert.Empty(downstream);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.7")]
    public async Task Http30ConnectionPush_should_absorb_multiple_max_push_id_frames_when_received()
    {
        var settings = new Http3SettingsFrame([]);
        var maxPush1 = new Http3MaxPushIdFrame(5);
        var maxPush2 = new Http3MaxPushIdFrame(10);
        var maxPush3 = new Http3MaxPushIdFrame(20);

        var (downstream, _) = await RunAsync(settings, maxPush1, maxPush2, maxPush3);

        Assert.Empty(downstream);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.7")]
    public async Task Http30ConnectionPush_should_continue_routing_after_max_push_id_received()
    {
        var settings = new Http3SettingsFrame([]);
        var maxPushId = new Http3MaxPushIdFrame(10);
        var headers = new Http3HeadersFrame(new byte[] { 0x00, 0x00 });

        var (downstream, _) = await RunAsync(settings, maxPushId, headers);

        Assert.Single(downstream);
        Assert.IsType<Http3HeadersFrame>(downstream[0]);
    }

    // --- Mixed push control frames (RFC 9114 Section 7.2) ---

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2")]
    public async Task Http30ConnectionPush_should_absorb_all_push_control_frames_when_interleaved()
    {
        var settings = new Http3SettingsFrame([]);
        var maxPushId = new Http3MaxPushIdFrame(5);
        var cancelPush = new Http3CancelPushFrame(0);
        var pushPromise = new Http3PushPromiseFrame(1, new byte[] { 0x00 });
        var data = new Http3DataFrame(new byte[] { 0xCC });

        var (downstream, _) = await RunAsync(settings, maxPushId, cancelPush, pushPromise, data);

        // All push control frames absorbed; only DATA forwarded.
        Assert.Single(downstream);
        Assert.IsType<Http3DataFrame>(downstream[0]);
    }
}
