using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Streams.Stages.Encoding;

namespace TurboHTTP.StreamTests.Http3;

/// <summary>
/// Tests that <see cref="Http30Request2FrameStage"/> does not serialize the entire HTTP/3
/// pipeline when the QPACK encoder instruction outlet is backpressured (TASK-030-017).
///
/// RFC 9204 §2.1.2: Blocked streams wait for encoder ACKs, but other requests must
/// continue to flow. A full encoder stream must not block unrelated request processing.
/// </summary>
public sealed class Http30Request2FrameBackpressureSpec : StreamTestBase
{
    private static HttpRequestMessage MakeRequest(string path = "/test")
        => new(HttpMethod.Get, $"https://example.com{path}");

    /// <summary>
    /// Creates a graph with manual probes so we can control backpressure independently
    /// on each outlet.
    /// </summary>
    private (
        TestPublisher.ManualProbe<HttpRequestMessage> RequestProbe,
        TestSubscriber.ManualProbe<Http3Frame> FrameOut,
        TestSubscriber.ManualProbe<ReadOnlyMemory<byte>> EncoderOut)
        CreateProbes(Http3RequestEncoder? encoder = null)
    {
        encoder ??= new Http3RequestEncoder(maxTableCapacity: 4096);
        var requestProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var frameOut = this.CreateManualSubscriberProbe<Http3Frame>();
        var encoderOut = this.CreateManualSubscriberProbe<ReadOnlyMemory<byte>>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(b =>
            {
                var stage = b.Add(new Http30Request2FrameStage(encoder));
                var reqSrc = b.Add(Source.FromPublisher(requestProbe));

                b.From(reqSrc).To(stage.In);
                b.From(stage.OutFrame).To(Sink.FromSubscriber(frameOut));
                b.From(stage.OutEncoder).To(Sink.FromSubscriber(encoderOut));

                return ClosedShape.Instance;
            }));

        graph.Run(Materializer);

        return (requestProbe, frameOut, encoderOut);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9204-2.1.2")]
    public async Task Http30Request2Frame_should_emit_frames_even_when_encoder_outlet_has_not_pulled()
    {
        // Arrange: encoder outlet never pulls — simulates a full encoder stream.
        var (requestProbe, frameOut, _) = CreateProbes();

        // The stage needs both outlets to signal demand initially (Akka.Streams wiring).
        // The frame outlet requests demand; the encoder outlet does NOT request demand.
        var reqSub = await requestProbe.ExpectSubscriptionAsync();
        var frameSub = await frameOut.ExpectSubscriptionAsync();

        // Request demand on the frame outlet only.
        frameSub.Request(10);

        // Act: push a request.
        reqSub.SendNext(MakeRequest("/first"));

        // Assert: frames should still arrive even though encoder outlet hasn't pulled.
        await frameOut.ExpectNextAsync(TimeSpan.FromSeconds(3));
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9204-2.1.2")]
    public async Task Http30Request2Frame_should_process_multiple_requests_when_encoder_outlet_is_slow()
    {
        // Arrange: encoder outlet pulls once, then stops — simulates slow drain.
        var (requestProbe, frameOut, encoderOut) = CreateProbes();

        var reqSub = await requestProbe.ExpectSubscriptionAsync();
        var frameSub = await frameOut.ExpectSubscriptionAsync();
        var encSub = await encoderOut.ExpectSubscriptionAsync();

        // Request demand on both outlets, but encoder outlet only requests 1.
        frameSub.Request(10);
        encSub.Request(1);

        // Push first request — both outlets drain.
        reqSub.SendNext(MakeRequest("/first"));
        await frameOut.ExpectNextAsync(TimeSpan.FromSeconds(3));

        // Encoder outlet consumes the instruction, but does NOT request more.
        // (encSub.Request(1) was already used up by the first instruction.)

        // Push second request — frame outlet should still work.
        reqSub.SendNext(MakeRequest("/second"));
        await frameOut.ExpectNextAsync(TimeSpan.FromSeconds(3));

        // Push third request — still flowing.
        reqSub.SendNext(MakeRequest("/third"));
        await frameOut.ExpectNextAsync(TimeSpan.FromSeconds(3));
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9204-2.1.2")]
    public async Task Http30Request2Frame_should_drain_queued_instructions_when_encoder_outlet_catches_up()
    {
        // Arrange
        var (requestProbe, frameOut, encoderOut) = CreateProbes();

        var reqSub = await requestProbe.ExpectSubscriptionAsync();
        var frameSub = await frameOut.ExpectSubscriptionAsync();
        var encSub = await encoderOut.ExpectSubscriptionAsync();

        // Frame outlet always has demand; encoder starts with 1.
        frameSub.Request(10);
        encSub.Request(1);

        // Push two requests — first instruction drains, second queues.
        reqSub.SendNext(MakeRequest("/first"));
        await frameOut.ExpectNextAsync(TimeSpan.FromSeconds(3));
        var firstInstructions = await encoderOut.ExpectNextAsync(TimeSpan.FromSeconds(3));
        Assert.True(firstInstructions.Length > 0);

        reqSub.SendNext(MakeRequest("/second"));
        await frameOut.ExpectNextAsync(TimeSpan.FromSeconds(3));

        // Now request demand on encoder outlet — queued instructions should arrive.
        encSub.Request(5);
        var secondInstructions = await encoderOut.ExpectNextAsync(TimeSpan.FromSeconds(3));
        Assert.True(secondInstructions.Length > 0);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9204-2.1.2")]
    public async Task Http30Request2Frame_should_not_deadlock_when_encoder_stream_never_drains()
    {
        // Arrange: encoder outlet never pulls — total deadlock scenario.
        var (requestProbe, frameOut, _) = CreateProbes();

        var reqSub = await requestProbe.ExpectSubscriptionAsync();
        var frameSub = await frameOut.ExpectSubscriptionAsync();

        // Only frame outlet has demand.
        frameSub.Request(10);

        // Push multiple requests — none should deadlock.
        reqSub.SendNext(MakeRequest("/a"));
        await frameOut.ExpectNextAsync(TimeSpan.FromSeconds(3));

        reqSub.SendNext(MakeRequest("/b"));
        await frameOut.ExpectNextAsync(TimeSpan.FromSeconds(3));

        reqSub.SendNext(MakeRequest("/c"));
        await frameOut.ExpectNextAsync(TimeSpan.FromSeconds(3));

        // Complete upstream — stage should complete frame outlet without waiting on encoder.
        reqSub.SendComplete();

        // Frame outlet should complete (instructions queue stays buffered but stage still
        // completes the frame outlet).
        await frameOut.ExpectCompleteAsync();
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9204-2.1.2")]
    public async Task Http30Request2Frame_should_complete_both_outlets_when_all_drained()
    {
        var (requestProbe, frameOut, encoderOut) = CreateProbes();

        var reqSub = await requestProbe.ExpectSubscriptionAsync();
        var frameSub = await frameOut.ExpectSubscriptionAsync();
        var encSub = await encoderOut.ExpectSubscriptionAsync();

        frameSub.Request(10);
        encSub.Request(10);

        reqSub.SendNext(MakeRequest("/only"));
        await frameOut.ExpectNextAsync(TimeSpan.FromSeconds(3));

        // Consume any encoder instructions that were emitted.
        await encoderOut.ExpectNextAsync(TimeSpan.FromSeconds(3));

        // Complete upstream.
        reqSub.SendComplete();

        // Both outlets should complete.
        await frameOut.ExpectCompleteAsync();
        await encoderOut.ExpectCompleteAsync();
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9204-2.1.2")]
    public async Task Http30Request2Frame_should_not_emit_instructions_when_dynamic_table_disabled()
    {
        // With maxTableCapacity=0, no encoder instructions are generated.
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = MakeRequest("/static-only");

        var frameSink = Sink.Seq<Http3Frame>();
        var encoderSink = Sink.Seq<ReadOnlyMemory<byte>>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(frameSink, encoderSink,
                (m1, m2) => (m1, m2),
                (b, fSink, eSink) =>
                {
                    var source = b.Add(Source.Single(request));
                    var stage = b.Add(new Http30Request2FrameStage(encoder));

                    b.From(source).To(stage.In);
                    b.From(stage.OutFrame).To(fSink);
                    b.From(stage.OutEncoder).To(eSink);

                    return ClosedShape.Instance;
                }));

        var (framesTask, instructionsTask) = graph.Run(Materializer);

        var frames = await framesTask;
        var instructions = await instructionsTask;

        Assert.NotEmpty(frames);
        // With dynamic table disabled, all instructions should be empty.
        Assert.All(instructions, i => Assert.Equal(0, i.Length));
    }
}
