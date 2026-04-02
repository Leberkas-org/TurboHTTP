using System.Net;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.StreamTests.Http11;

/// <summary>
/// Tests the strict one-request-in-flight back-pressure contract per RFC 9112 §9.
/// Verifies that each request waits for its own response before the next request is pulled.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http1XCorrelationStage"/>.
/// RFC 9112 §9: HTTP/1.x requests and responses MUST be sent and received in order.
/// The InReset inlet has been removed; strict serial back-pressure is always enforced.
/// </remarks>
public sealed class Http11PipelineReconnectSpec : StreamTestBase
{
    private static HttpResponseMessage OkResponse()
        => new(HttpStatusCode.OK);

    private async Task<(List<IOutputItem> Signals, List<HttpResponseMessage> Responses)> RunStageAsync(
        Source<HttpRequestMessage, NotUsed> requestSource,
        Source<HttpResponseMessage, NotUsed> responseSource,
        TimeSpan? timeout = null)
    {
        var signalSink = Sink.Seq<IOutputItem>();
        var responseSink = Sink.Seq<HttpResponseMessage>();

        var graph = RunnableGraph.FromGraph(GraphDsl.Create(signalSink, responseSink, (mat1, mat2) => (mat1, mat2), (b, sigSink, resSink) =>
        {
            var corr = b.Add(new Http1XCorrelationStage());
            var reqSrc = b.Add(requestSource);
            var resSrc = b.Add(responseSource);

            b.From(reqSrc).To(corr.InRequest);
            b.From(resSrc).To(corr.InResponse);
            b.From(corr.OutControl).To(sigSink);
            b.From(corr.OutResponse).To(resSink);

            return ClosedShape.Instance;
        }));

        var (signalTask, responseTask) = graph.Run(Materializer);
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5);
        var signals = await signalTask.WaitAsync(effectiveTimeout, TestContext.Current.CancellationToken);
        var responses = await responseTask.WaitAsync(effectiveTimeout, TestContext.Current.CancellationToken);
        return (signals.ToList(), responses.ToList());
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11PipelineReconnect_should_emit_signal_per_request_when_each_request_waits_for_response()
    {
        // Two requests emitted immediately, responses delayed and spaced.
        // With strict back-pressure: req1 is pulled → blocked on InResponse.
        // resp1 arrives → req1 paired, req2 pulled → signal #2.
        // resp2 arrives → req2 paired.
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/1"),
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/2")
        };

        // resp1 at ~200ms, resp2 at ~800ms
        var responseSource = Source.Single(OkResponse())
            .InitialDelay(TimeSpan.FromMilliseconds(200))
            .Concat(Source.Single(OkResponse())
                .InitialDelay(TimeSpan.FromMilliseconds(600)));

        var (signals, responses) = await RunStageAsync(
            Source.From(requests),
            responseSource);

        Assert.Equal(2, responses.Count);
        // 2 StreamAcquireItems (per request) + 2 ConnectionReuseItems (per response) = 4 signals
        Assert.Equal(4, signals.Count);
        Assert.Equal(2, signals.Count(s => s is StreamAcquireItem));
        Assert.Equal(2, signals.Count(s => s is ConnectionReuseItem));
    }
}
