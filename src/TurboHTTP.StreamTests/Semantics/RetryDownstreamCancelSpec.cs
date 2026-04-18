using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Semantics;

public sealed class RetryDownstreamCancelSpec : StreamTestBase
{
    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9110-9.2")]
    public void RetryBidiStage_should_not_crash_when_Out1_cancelled_before_timer_fires()
    {
        var stage = new RetryBidiStage(new RetryPolicy { RespectRetryAfter = true });

        var reqPub = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var respPub = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var reqOutProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var respOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var bidi = b.Add(stage);
            b.From(Source.FromPublisher(reqPub)).To(bidi.Inlet1);
            b.From(bidi.Outlet1).To(Sink.FromSubscriber(reqOutProbe));
            b.From(Source.FromPublisher(respPub)).To(bidi.Inlet2);
            b.From(bidi.Outlet2).To(Sink.FromSubscriber(respOutProbe));
            return ClosedShape.Instance;
        })).Run(Materializer);

        var reqInSub = reqPub.ExpectSubscription(TestContext.Current.CancellationToken);
        var respInSub = respPub.ExpectSubscription(TestContext.Current.CancellationToken);
        var reqOutSub = reqOutProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var respOutSub = respOutProbe.ExpectSubscription(TestContext.Current.CancellationToken);

        // Signal demand on both outlets
        reqOutSub.Request(10);
        respOutSub.Request(10);

        // Push a request through
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/retry-test");
        reqInSub.SendNext(request);
        reqOutProbe.ExpectNext(TestContext.Current.CancellationToken);

        // Push a 503 with Retry-After: 1 second — triggers a timer
        var retryResponse = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            RequestMessage = request
        };
        retryResponse.Headers.TryAddWithoutValidation("Retry-After", "1");
        respInSub.SendNext(retryResponse);

        // Cancel the request outlet downstream — triggers onDownstreamFinish
        // BUG: _requestDemand is not reset to false here
        reqOutSub.Cancel();

        // Wait for the Retry-After timer to fire (~1 second)
        // After fix: _requestDemand is reset, retry is discarded silently
        respOutProbe.ExpectNoMsg(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }
}