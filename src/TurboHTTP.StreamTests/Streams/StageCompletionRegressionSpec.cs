using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Streams;

public sealed class StageCompletionRegressionSpec : StreamTestBase
{
    [Fact(Timeout = 5000)]
    public async Task TracingBidiStage_outlet_should_terminate_when_upstream_fails()
    {
        // TracingBidiStage is a BidiShape — test the response direction (Inlet2 → Outlet2)
        // which was the broken path. Wire request direction with an empty source.
        var error = new InvalidOperationException("upstream error");

        var responseSource = Source.From([new HttpResponseMessage()])
            .Concat(Source.Failed<HttpResponseMessage>(error));

        var graph = GraphDsl.Create(
            Sink.Seq<HttpResponseMessage>(),
            (builder, responseSink) =>
            {
                var bidi = builder.Add(new TracingBidiStage());
                var reqSource = builder.Add(Source.Empty<HttpRequestMessage>());
                var reqSink = builder.Add(Sink.Ignore<HttpRequestMessage>());
                var respSource = builder.Add(responseSource);

                builder.From(reqSource).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).To(reqSink);
                builder.From(respSource).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(responseSink);

                return ClosedShape.Instance;
            });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await RunnableGraph.FromGraph(graph).Run(Materializer));
    }
}