using System.Net;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.StreamTests.RFC9112;

/// <summary>
/// Tests the no-pipelining-after-reconnect guard per RFC 9112 §9.3.2.
/// Verifies that the first request after (re)connect waits for a response before pipelining is allowed,
/// and that reconnect resets the guard.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http1XCorrelationStage"/>.
/// RFC 9112 §9.3.2: A client SHOULD NOT pipeline requests on a newly opened connection
/// until it knows the connection is persistent.
/// </remarks>
public sealed class Http11PipelineReconnectTests : StreamTestBase
{
    private static HttpResponseMessage OkResponse()
        => new(HttpStatusCode.OK);

    /// <summary>
    /// Builds and runs a closed graph that wires requestSource → InRequest, responseSource → InResponse,
    /// resetSource → InReset, Out → responseSink, OutSignal → signalSink.
    /// Returns the collected signals and responses once the stream completes.
    /// </summary>
    private async Task<(List<IControlItem> Signals, List<HttpResponseMessage> Responses)> RunStageAsync(
        Source<HttpRequestMessage, NotUsed> requestSource,
        Source<HttpResponseMessage, NotUsed> responseSource,
        Source<NotUsed, NotUsed> resetSource,
        TimeSpan? timeout = null)
    {
        var signalSink = Sink.Seq<IControlItem>();
        var responseSink = Sink.Seq<HttpResponseMessage>();

        var graph = RunnableGraph.FromGraph(GraphDsl.Create(signalSink, responseSink, (mat1, mat2) => (mat1, mat2), (b, sigSink, resSink) =>
        {
            var corr = b.Add(new Http1XCorrelationStage());
            var reqSrc = b.Add(requestSource);
            var resSrc = b.Add(responseSource);
            var rstSrc = b.Add(resetSource);

            b.From(reqSrc).To(corr.InRequest);
            b.From(resSrc).To(corr.InResponse);
            b.From(rstSrc).To(corr.InReset);
            b.From(corr.OutControl).To(sigSink);
            b.From(corr.OutResponse).To(resSink);

            return ClosedShape.Instance;
        }));

        var (signalTask, responseTask) = graph.Run(Materializer);
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5);
        var signals = await signalTask.WaitAsync(effectiveTimeout);
        var responses = await responseTask.WaitAsync(effectiveTimeout);
        return (signals.ToList(), responses.ToList());
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-9.3.2-PL-001: First request after connect is not pipelined")]
    public async Task Should_WaitForResponse_When_FirstRequestAfterConnect()
    {
        // Two requests emitted immediately, responses delayed and spaced.
        // Guard locks after req1 → req2 cannot be pulled until resp1 arrives and unlocks the guard.
        // After unlock, req2 becomes first-pending → signal #2.
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/1"),
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/2")
        };

        // resp1 at ~200ms, resp2 at ~800ms — spaced so req2 processes before resp2 arrives
        var responseSource = Source.Single(OkResponse())
            .InitialDelay(TimeSpan.FromMilliseconds(200))
            .Concat(Source.Single(OkResponse())
                .InitialDelay(TimeSpan.FromMilliseconds(600)));

        var (signals, responses) = await RunStageAsync(
            Source.From(requests),
            responseSource,
            Source.Never<NotUsed>());

        Assert.Equal(2, responses.Count);
        Assert.Equal(2, signals.Count);
        Assert.All(signals, s => Assert.IsType<StreamAcquireItem>(s));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-9.3.2-PL-002: Second request may pipeline after first response")]
    public async Task Should_Pipeline_When_FirstResponseReceived()
    {
        // Three requests emitted immediately, responses delayed and spaced.
        // Guard locks after req1 → req2 blocked.
        // At ~200ms: resp1 → unlock → pull req2 → req2 arrives (signal #2), pull req3 (unlocked).
        // req3 arrives with pending=1 → NO signal (pipelined behind req2).
        // At ~800ms: resp2+resp3 → correlate remaining.
        // Result: 2 signals proves pipelining occurred (req3 did not become first-pending).
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/1"),
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/2"),
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/3")
        };

        // resp1 at ~200ms, resp2+resp3 at ~800ms — req2+req3 arrive and pipeline before resp2
        var responseSource = Source.Single(OkResponse())
            .InitialDelay(TimeSpan.FromMilliseconds(200))
            .Concat(Source.From(new[] { OkResponse(), OkResponse() })
                .InitialDelay(TimeSpan.FromMilliseconds(600)));

        var (signals, responses) = await RunStageAsync(
            Source.From(requests),
            responseSource,
            Source.Never<NotUsed>());

        Assert.Equal(3, responses.Count);
        // req1 → signal (first-pending, guard locked)
        // req2 → signal (first-pending after resp1 unlocks guard)
        // req3 → no signal (pipelined behind req2 while guard is unlocked)
        Assert.Equal(2, signals.Count);
        Assert.All(signals, s => Assert.IsType<StreamAcquireItem>(s));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-9.3.2-PL-003: Reconnect resets pipeline guard")]
    public async Task Should_ResetGuard_When_ConnectionReestablished()
    {
        // req1 immediate, req2+req3 delayed 400ms (arrive after reset).
        // resp1 at ~100ms, resp2+resp3 at ~600ms.
        // Reset at ~250ms (after resp1 unlocks, before req2 arrives).
        //
        // Timeline:
        //   t=0:   req1 → signal #1, guard locked
        //   t=100: resp1 → correlate → unlock → pull (no req available yet)
        //   t=250: reset → re-lock guard
        //   t=400: req2 → signal #2, guard locked → don't pull req3
        //   t=600: resp2 → correlate → unlock → pull req3
        //          req3 → signal #3
        //          resp3 → correlate
        //
        // Result: 3 signals proves reset re-locked the guard.
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/1");
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/2");
        var req3 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/3");

        var requestSource = Source.Single(req1)
            .Concat(Source.From(new[] { req2, req3 })
                .InitialDelay(TimeSpan.FromMilliseconds(400)));

        var responseSource = Source.Single(OkResponse())
            .InitialDelay(TimeSpan.FromMilliseconds(100))
            .Concat(Source.From(new[] { OkResponse(), OkResponse() })
                .InitialDelay(TimeSpan.FromMilliseconds(500)));

        var resetSource = Source.Single(NotUsed.Instance)
            .InitialDelay(TimeSpan.FromMilliseconds(250));

        var (signals, responses) = await RunStageAsync(
            requestSource,
            responseSource,
            resetSource);

        Assert.Equal(3, responses.Count);
        // req1 → signal (first-pending, guard locked)
        // reset → guard re-locked
        // req2 → signal (first-pending after reset)
        // req3 → signal (first-pending after resp2 unlocks, guard was re-locked)
        Assert.Equal(3, signals.Count);
        Assert.All(signals, s => Assert.IsType<StreamAcquireItem>(s));
    }
}
