using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Semantics.Stages;

public sealed class RedirectDownstreamCancelSpec : StreamTestBase
{
    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void RedirectBidiStage_should_not_crash_when_Out1_cancelled_before_redirect_response()
    {
        var stage = new RedirectBidiStage(new RedirectPolicy());

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

        reqOutSub.Request(10);
        respOutSub.Request(10);

        // Push a request — seed a RedirectHandler so the stage recognizes the chain
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/origin");
        var handler = new RedirectHandler();
        request.Options.Set(RedirectBidiStage.RedirectHandlerKey, handler);
        reqInSub.SendNext(request);
        reqOutProbe.ExpectNext(TestContext.Current.CancellationToken);

        // Cancel the request outlet downstream — triggers onDownstreamFinish
        // BUG: _requestDemand is not reset to false here
        reqOutSub.Cancel();

        // Push a 301 redirect response — triggers TryEmitRedirect
        // BUG: _requestDemand == true → Push on closed outlet → crash
        var redirectResponse = new HttpResponseMessage(HttpStatusCode.MovedPermanently)
        {
            RequestMessage = request
        };
        redirectResponse.Headers.Location = new Uri("http://example.com/new-location");
        respInSub.SendNext(redirectResponse);

        // After fix: _requestDemand is reset, redirect enqueued but not pushed — no crash
        respOutProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
    }
}