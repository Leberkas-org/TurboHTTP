using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages.Features;

namespace TurboHTTP.StreamTests.Semantics;

/// <summary>
/// Chain, loop-detection, protection, and upstream-failure tests for the redirect bidirectional
/// stage per RFC 9110 §15.4.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="RedirectBidiStage"/>.
/// RFC 9110 §15.4: Max-redirect enforcement, loop detection, downgrade protection, cross-origin stripping.
/// </remarks>
public sealed class RedirectChainSpec : StreamTestBase
{
    /// <summary>
    /// Materialises a <see cref="RedirectBidiStage"/> with manual subscriber probes on both outlets
    /// and a manual publisher probe on the response inlet. The request inlet receives the given
    /// requests concatenated with Source.Never to prevent premature completion.
    /// </summary>
    private (TestSubscriber.ManualProbe<HttpRequestMessage> requestOut,
        TestSubscriber.ManualProbe<HttpResponseMessage> responseOut,
        Action<HttpResponseMessage> pushResponse,
        Action completeResponse) RunManual(
            RedirectBidiStage stage,
            int requestOutDemand,
            int responseOutDemand,
            params HttpRequestMessage[] requests)
    {
        var responsePublisher = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var requestOutProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var responseOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var bidi = b.Add(stage);
            var reqSrc = b.Add(Source.From(requests).Concat(Source.Never<HttpRequestMessage>()));
            var respSrc = b.Add(Source.FromPublisher(responsePublisher));

            b.From(reqSrc).To(bidi.Inlet1);
            b.From(bidi.Outlet1).To(Sink.FromSubscriber(requestOutProbe));
            b.From(respSrc).To(bidi.Inlet2);
            b.From(bidi.Outlet2).To(Sink.FromSubscriber(responseOutProbe));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var responseSub = responsePublisher.ExpectSubscription(TestContext.Current.CancellationToken);
        var reqOutSub = requestOutProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var respOutSub = responseOutProbe.ExpectSubscription(TestContext.Current.CancellationToken);

        reqOutSub.Request(requestOutDemand);
        respOutSub.Request(responseOutDemand);

        return (requestOutProbe, responseOutProbe, responseSub.SendNext, responseSub.SendComplete);
    }

    private static HttpResponseMessage BuildRedirectResponse(
        HttpStatusCode statusCode,
        string location,
        HttpRequestMessage? requestMessage = null)
    {
        var response = new HttpResponseMessage(statusCode);
        response.Headers.TryAddWithoutValidation("Location", location);
        response.RequestMessage = requestMessage
            ?? new HttpRequestMessage(HttpMethod.Get, "http://example.com/origin");
        return response;
    }

    private static void SeedRedirectHandler(HttpRequestMessage request, RedirectHandler handler)
    {
        request.Options.Set(RedirectBidiStage.RedirectHandlerKey, handler);
    }

    // Max redirects, loop detection, downgrade protection

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ForwardFinalResponse_When_MaxRedirectsExceeded()
    {
        var policy = new RedirectPolicy { MaxRedirects = 1 };
        var handler = new RedirectHandler(policy);
        // Exhaust the single allowed redirect
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var res1 = new HttpResponseMessage(HttpStatusCode.Found);
        res1.Headers.TryAddWithoutValidation("Location", "http://example.com/b");
        handler.BuildRedirectRequest(req1, res1);

        // Second redirect should fail — seed handler on request Options
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        SeedRedirectHandler(request, handler);

        var stage = new RedirectBidiStage(policy);
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        var response = new HttpResponseMessage(HttpStatusCode.Found) { RequestMessage = request };
        response.Headers.TryAddWithoutValidation("Location", "http://example.com/c");
        pushResp(response);

        // Should arrive on Out2 as final response (max exceeded)
        Assert.Same(response, respOut.ExpectNext(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ForwardFinalResponse_When_RedirectLoopDetected()
    {
        var handler = new RedirectHandler();
        // Prime the handler with a→b
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var res1 = new HttpResponseMessage(HttpStatusCode.Found);
        res1.Headers.TryAddWithoutValidation("Location", "http://example.com/b");
        handler.BuildRedirectRequest(req1, res1);

        // Loop: b → a (already visited)
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        SeedRedirectHandler(request, handler);

        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        var response = new HttpResponseMessage(HttpStatusCode.Found) { RequestMessage = request };
        response.Headers.TryAddWithoutValidation("Location", "http://example.com/a");
        pushResp(response);

        Assert.Same(response, respOut.ExpectNext(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ForwardFinalResponse_When_HttpsToHttpDowngrade()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/secure");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        var response = BuildRedirectResponse(HttpStatusCode.Found, "http://example.com/insecure", request);
        pushResp(response);

        Assert.Same(response, respOut.ExpectNext(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ForwardFinalResponse_When_LocationHeaderMissing()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        // Redirect response with no Location header
        var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently) { RequestMessage = request };
        pushResp(response);

        Assert.Same(response, respOut.ExpectNext(TestContext.Current.CancellationToken));
    }

    // State machine: redirect chain followed by final response

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_DeliverFinalResponse_AfterRedirectChain()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        // Original request forwarded
        Assert.Same(request, reqOut.ExpectNext(TestContext.Current.CancellationToken));

        // First redirect: a → b
        var response1 = BuildRedirectResponse(HttpStatusCode.Found, "http://example.com/b", request);
        pushResp(response1);

        var redirectReq1 = reqOut.ExpectNext(TestContext.Current.CancellationToken);
        Assert.Equal("http://example.com/b", redirectReq1.RequestUri?.AbsoluteUri);

        // Final response for /b
        var finalResponse = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = redirectReq1 };
        pushResp(finalResponse);

        Assert.Same(finalResponse, respOut.ExpectNext(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_DeliverFinalResponse_AfterTwoRedirects()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 10, 10, request);

        // Original request
        Assert.Same(request, reqOut.ExpectNext(TestContext.Current.CancellationToken));

        // First redirect: a → b
        var response1 = BuildRedirectResponse(HttpStatusCode.Found, "http://example.com/b", request);
        pushResp(response1);
        var redirectReq1 = reqOut.ExpectNext(TestContext.Current.CancellationToken);
        Assert.Equal("http://example.com/b", redirectReq1.RequestUri?.AbsoluteUri);

        // Second redirect: b → c
        var response2 = BuildRedirectResponse(HttpStatusCode.Found, "http://example.com/c", redirectReq1);
        pushResp(response2);
        var redirectReq2 = reqOut.ExpectNext(TestContext.Current.CancellationToken);
        Assert.Equal("http://example.com/c", redirectReq2.RequestUri?.AbsoluteUri);

        // Final response for /c
        var finalResponse = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = redirectReq2 };
        pushResp(finalResponse);

        Assert.Same(finalResponse, respOut.ExpectNext(TestContext.Current.CancellationToken));
    }

    // Cross-origin redirect strips Authorization

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_StripAuthorizationHeader_When_CrossOriginRedirect()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer token123");

        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, _, pushResp, _) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        var response = BuildRedirectResponse(HttpStatusCode.Found, "http://other.com/api", request);
        pushResp(response);

        var redirectReq = reqOut.ExpectNext(TestContext.Current.CancellationToken);
        Assert.False(redirectReq.Headers.Contains("Authorization"),
            "Authorization must be stripped on cross-origin redirect");
    }

    // Version preservation

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_PreserveHttp2Version_When_Redirecting()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page")
        {
            Version = new Version(2, 0)
        };
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, _, pushResp, _) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        var response = BuildRedirectResponse(HttpStatusCode.Found, "http://example.com/new", request);
        pushResp(response);

        var redirectReq = reqOut.ExpectNext(TestContext.Current.CancellationToken);
        Assert.Equal(new Version(2, 0), redirectReq.Version);
    }

    // Upstream failure handling

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_AbsorbRequestUpstreamFailure()
    {
        var stage = new RedirectBidiStage(new RedirectPolicy());

        var requestPublisher = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var responsePublisher = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var requestOutProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var responseOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var bidi = b.Add(stage);
            b.From(Source.FromPublisher(requestPublisher)).To(bidi.Inlet1);
            b.From(bidi.Outlet1).To(Sink.FromSubscriber(requestOutProbe));
            b.From(Source.FromPublisher(responsePublisher)).To(bidi.Inlet2);
            b.From(bidi.Outlet2).To(Sink.FromSubscriber(responseOutProbe));
            return ClosedShape.Instance;
        })).Run(Materializer);

        var reqPubSub = requestPublisher.ExpectSubscription(TestContext.Current.CancellationToken);
        responsePublisher.ExpectSubscription(TestContext.Current.CancellationToken);
        requestOutProbe.ExpectSubscription(TestContext.Current.CancellationToken).Request(5);
        responseOutProbe.ExpectSubscription(TestContext.Current.CancellationToken).Request(5);

        reqPubSub.SendError(new Exception("request boom"));

        // Stage absorbs the error (no OnError) but gracefully completes _outRequest
        requestOutProbe.ExpectComplete(TestContext.Current.CancellationToken);
        responseOutProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_AbsorbResponseUpstreamFailure()
    {
        var stage = new RedirectBidiStage(new RedirectPolicy());

        var requestPublisher = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var responsePublisher = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var requestOutProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var responseOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var bidi = b.Add(stage);
            b.From(Source.FromPublisher(requestPublisher)).To(bidi.Inlet1);
            b.From(bidi.Outlet1).To(Sink.FromSubscriber(requestOutProbe));
            b.From(Source.FromPublisher(responsePublisher)).To(bidi.Inlet2);
            b.From(bidi.Outlet2).To(Sink.FromSubscriber(responseOutProbe));
            return ClosedShape.Instance;
        })).Run(Materializer);

        requestPublisher.ExpectSubscription(TestContext.Current.CancellationToken);
        var respPubSub = responsePublisher.ExpectSubscription(TestContext.Current.CancellationToken);
        requestOutProbe.ExpectSubscription(TestContext.Current.CancellationToken).Request(5);
        responseOutProbe.ExpectSubscription(TestContext.Current.CancellationToken).Request(5);

        respPubSub.SendError(new Exception("response boom"));

        // Stage absorbs the error (no OnError) but gracefully completes _outResponse
        requestOutProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
        responseOutProbe.ExpectComplete(TestContext.Current.CancellationToken);
    }
}
