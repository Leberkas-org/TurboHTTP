using System.Collections.Immutable;
using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.RFC9110;

/// <summary>
/// Tests the redirect bidirectional stage per RFC 9110 §15.4.
/// Verifies that the request direction forwards requests unchanged, and the response
/// direction evaluates redirects internally, re-injecting redirect requests on Out1.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="RedirectBidiStage"/>.
/// RFC 9110 §15.4: Redirect status codes, method preservation rules, loop detection, max-redirect enforcement.
/// </remarks>
public sealed class RedirectBidiStageTests : StreamTestBase
{
    /// <summary>
    /// Runs requests through the request direction (In1→Out1) of the BidiStage.
    /// The response direction is wired to empty source / ignored sink.
    /// </summary>
    private Task<IImmutableList<HttpRequestMessage>> RunRequestAsync(
        RedirectBidiStage stage,
        params HttpRequestMessage[] requests)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpRequestMessage>(),
            (builder, sink) =>
            {
                var bidi = builder.Add(stage);
                var source = builder.Add(Source.From(requests));
                var emptyResponseSource = builder.Add(Source.Empty<HttpResponseMessage>());
                var ignoredSink = builder.Add(Sink.Ignore<HttpResponseMessage>());

                builder.From(source).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).To(sink);
                builder.From(emptyResponseSource).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(ignoredSink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

    /// <summary>
    /// Runs responses through the response direction (In2→Out2) of the BidiStage.
    /// The request direction is wired to empty source / ignored sink.
    /// </summary>
    private Task<IImmutableList<HttpResponseMessage>> RunResponseAsync(
        RedirectBidiStage stage,
        params HttpResponseMessage[] responses)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpResponseMessage>(),
            (builder, sink) =>
            {
                var bidi = builder.Add(stage);
                var source = builder.Add(Source.From(responses));
                var emptyRequestSource = builder.Add(Source.Empty<HttpRequestMessage>());
                var ignoredSink = builder.Add(Sink.Ignore<HttpRequestMessage>());

                builder.From(emptyRequestSource).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).To(ignoredSink);
                builder.From(source).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(sink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

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

        var responseSub = responsePublisher.ExpectSubscription();
        var reqOutSub = requestOutProbe.ExpectSubscription();
        var respOutSub = responseOutProbe.ExpectSubscription();

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
        request.Options.Set(RedirectStage.RedirectHandlerKey, handler);
    }

    // ============================
    // Pass-through tests (null policy)
    // ============================

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-15.4-RDBIDI-001: null RedirectPolicy -> request passes through unchanged")]
    public async Task RequestDirection_Should_PassThrough_When_PolicyIsNull()
    {
        var stage = new RedirectBidiStage(null);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Same(request, result);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-15.4-RDBIDI-002: null RedirectPolicy -> response passes through unchanged")]
    public async Task ResponseDirection_Should_PassThrough_When_PolicyIsNull()
    {
        var stage = new RedirectBidiStage(null);
        var response = BuildRedirectResponse(HttpStatusCode.Found, "http://example.com/new");

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        Assert.Same(response, result);
    }

    // ============================
    // Request direction tests
    // ============================

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-15.4-RDBIDI-003: request forwarded unchanged on Out1")]
    public async Task RequestDirection_Should_ForwardRequestUnchanged()
    {
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Same(request, result);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-15.4-RDBIDI-004: multiple requests forwarded in order")]
    public async Task RequestDirection_Should_ForwardMultipleRequestsInOrder()
    {
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");

        var results = await RunRequestAsync(stage, req1, req2);

        Assert.Equal(2, results.Count);
        Assert.Same(req1, results[0]);
        Assert.Same(req2, results[1]);
    }

    // ============================
    // Response direction: non-redirect
    // ============================

    [Fact(DisplayName = "RFC9110-15.4-RDBIDI-005: 200 OK -> forwarded on Out2")]
    public void Should_ForwardFinalResponse_When_200OK()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        // Consume the forwarded request
        reqOut.ExpectNext();

        // Push a 200 OK response
        var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
        pushResp(response);

        Assert.Same(response, respOut.ExpectNext());
    }

    [Fact(DisplayName = "RFC9110-15.4-RDBIDI-006: 404 -> forwarded on Out2")]
    public void Should_ForwardFinalResponse_When_404()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext();

        var response = new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request };
        pushResp(response);

        Assert.Same(response, respOut.ExpectNext());
    }

    [Fact(DisplayName = "RFC9110-15.4-RDBIDI-007: null RequestMessage -> forwarded on Out2")]
    public void Should_ForwardFinalResponse_When_RequestMessageIsNull()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext();

        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.TryAddWithoutValidation("Location", "http://example.com/new");
        // RequestMessage intentionally not set
        pushResp(response);

        Assert.Same(response, respOut.ExpectNext());
    }

    // ============================
    // Response direction: redirect
    // ============================

    [Fact(DisplayName = "RFC9110-15.4-RDBIDI-008: 301 -> redirect request emitted on Out1")]
    public void Should_EmitRedirectOnOut1_When_301()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/old");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        // Original request forwarded
        Assert.Same(request, reqOut.ExpectNext());

        // 301 response triggers redirect
        var response = BuildRedirectResponse(HttpStatusCode.MovedPermanently, "http://example.com/new", request);
        pushResp(response);

        // Redirect request appears on Out1
        var redirectReq = reqOut.ExpectNext();
        Assert.Equal("http://example.com/new", redirectReq.RequestUri?.AbsoluteUri);

        // No response on Out2 (response was disposed)
        respOut.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    [Fact(DisplayName = "RFC9110-15.4-RDBIDI-009: 302 -> redirect request emitted on Out1")]
    public void Should_EmitRedirectOnOut1_When_302()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/old");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        Assert.Same(request, reqOut.ExpectNext());

        var response = BuildRedirectResponse(HttpStatusCode.Found, "http://example.com/new", request);
        pushResp(response);

        var redirectReq = reqOut.ExpectNext();
        Assert.Equal("http://example.com/new", redirectReq.RequestUri?.AbsoluteUri);
        respOut.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    [Fact(DisplayName = "RFC9110-15.4-RDBIDI-010: 303 -> method rewritten to GET")]
    public void Should_RewriteMethodToGet_When_303()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        Assert.Same(request, reqOut.ExpectNext());

        var response = BuildRedirectResponse(HttpStatusCode.SeeOther, "http://example.com/result", request);
        pushResp(response);

        var redirectReq = reqOut.ExpectNext();
        Assert.Equal(HttpMethod.Get, redirectReq.Method);
        Assert.Equal("http://example.com/result", redirectReq.RequestUri?.AbsoluteUri);
        respOut.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    [Fact(DisplayName = "RFC9110-15.4-RDBIDI-011: 307 -> method preserved")]
    public void Should_PreserveMethod_When_307()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/api");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        Assert.Same(request, reqOut.ExpectNext());

        var response = BuildRedirectResponse(HttpStatusCode.TemporaryRedirect, "http://example.com/api/v2", request);
        pushResp(response);

        var redirectReq = reqOut.ExpectNext();
        Assert.Equal(HttpMethod.Post, redirectReq.Method);
        respOut.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    [Fact(DisplayName = "RFC9110-15.4-RDBIDI-012: 308 -> method preserved")]
    public void Should_PreserveMethod_When_308()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "http://example.com/resource");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        Assert.Same(request, reqOut.ExpectNext());

        var response = BuildRedirectResponse(HttpStatusCode.PermanentRedirect, "http://example.com/resource/v2", request);
        pushResp(response);

        var redirectReq = reqOut.ExpectNext();
        Assert.Equal(HttpMethod.Put, redirectReq.Method);
        respOut.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    // ============================
    // RedirectHandler per request chain in Options
    // ============================

    [Fact(DisplayName = "RFC9110-15.4-RDBIDI-013: redirect request carries RedirectHandler in Options")]
    public void Should_CarryRedirectHandlerInOptions()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/old");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, _, pushResp, _) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext();

        var response = BuildRedirectResponse(HttpStatusCode.Found, "http://example.com/new", request);
        pushResp(response);

        var redirectReq = reqOut.ExpectNext();
        Assert.True(redirectReq.Options.TryGetValue(RedirectStage.RedirectHandlerKey, out var handler));
        Assert.NotNull(handler);
        Assert.Equal(1, handler.RedirectCount);
    }

    // ============================
    // Max redirects, loop detection, downgrade protection
    // ============================

    [Fact(DisplayName = "RFC9110-15.4-RDBIDI-014: max redirects exceeded -> response forwarded on Out2")]
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

        reqOut.ExpectNext();

        var response = new HttpResponseMessage(HttpStatusCode.Found) { RequestMessage = request };
        response.Headers.TryAddWithoutValidation("Location", "http://example.com/c");
        pushResp(response);

        // Should arrive on Out2 as final response (max exceeded)
        Assert.Same(response, respOut.ExpectNext());
    }

    [Fact(DisplayName = "RFC9110-15.4-RDBIDI-015: redirect loop detected -> response forwarded on Out2")]
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

        reqOut.ExpectNext();

        var response = new HttpResponseMessage(HttpStatusCode.Found) { RequestMessage = request };
        response.Headers.TryAddWithoutValidation("Location", "http://example.com/a");
        pushResp(response);

        Assert.Same(response, respOut.ExpectNext());
    }

    [Fact(DisplayName = "RFC9110-15.4-RDBIDI-016: HTTPS to HTTP downgrade blocked -> response forwarded on Out2")]
    public void Should_ForwardFinalResponse_When_HttpsToHttpDowngrade()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/secure");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext();

        var response = BuildRedirectResponse(HttpStatusCode.Found, "http://example.com/insecure", request);
        pushResp(response);

        Assert.Same(response, respOut.ExpectNext());
    }

    [Fact(DisplayName = "RFC9110-15.4-RDBIDI-017: missing Location header -> response forwarded on Out2")]
    public void Should_ForwardFinalResponse_When_LocationHeaderMissing()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext();

        // Redirect response with no Location header
        var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently) { RequestMessage = request };
        pushResp(response);

        Assert.Same(response, respOut.ExpectNext());
    }

    // ============================
    // State machine: redirect chain followed by final response
    // ============================

    [Fact(DisplayName = "RFC9110-15.4-RDBIDI-018: redirect chain -> final response reaches Out2")]
    public void Should_DeliverFinalResponse_AfterRedirectChain()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        // Original request forwarded
        Assert.Same(request, reqOut.ExpectNext());

        // First redirect: a → b
        var response1 = BuildRedirectResponse(HttpStatusCode.Found, "http://example.com/b", request);
        pushResp(response1);

        var redirectReq1 = reqOut.ExpectNext();
        Assert.Equal("http://example.com/b", redirectReq1.RequestUri?.AbsoluteUri);

        // Final response for /b
        var finalResponse = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = redirectReq1 };
        pushResp(finalResponse);

        Assert.Same(finalResponse, respOut.ExpectNext());
    }

    [Fact(DisplayName = "RFC9110-15.4-RDBIDI-019: two redirects in chain -> final response reaches Out2")]
    public void Should_DeliverFinalResponse_AfterTwoRedirects()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 10, 10, request);

        // Original request
        Assert.Same(request, reqOut.ExpectNext());

        // First redirect: a → b
        var response1 = BuildRedirectResponse(HttpStatusCode.Found, "http://example.com/b", request);
        pushResp(response1);
        var redirectReq1 = reqOut.ExpectNext();
        Assert.Equal("http://example.com/b", redirectReq1.RequestUri?.AbsoluteUri);

        // Second redirect: b → c
        var response2 = BuildRedirectResponse(HttpStatusCode.Found, "http://example.com/c", redirectReq1);
        pushResp(response2);
        var redirectReq2 = reqOut.ExpectNext();
        Assert.Equal("http://example.com/c", redirectReq2.RequestUri?.AbsoluteUri);

        // Final response for /c
        var finalResponse = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = redirectReq2 };
        pushResp(finalResponse);

        Assert.Same(finalResponse, respOut.ExpectNext());
    }

    // ============================
    // Cross-origin redirect strips Authorization
    // ============================

    [Fact(DisplayName = "RFC9110-15.4-RDBIDI-020: cross-origin redirect strips Authorization header")]
    public void Should_StripAuthorizationHeader_When_CrossOriginRedirect()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer token123");

        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, _, pushResp, _) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext();

        var response = BuildRedirectResponse(HttpStatusCode.Found, "http://other.com/api", request);
        pushResp(response);

        var redirectReq = reqOut.ExpectNext();
        Assert.False(redirectReq.Headers.Contains("Authorization"),
            "Authorization must be stripped on cross-origin redirect");
    }

    // ============================
    // Version preservation
    // ============================

    [Fact(DisplayName = "RFC9110-15.4-RDBIDI-021: redirect preserves HTTP/2 version")]
    public void Should_PreserveHttp2Version_When_Redirecting()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page")
        {
            Version = new Version(2, 0)
        };
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, _, pushResp, _) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext();

        var response = BuildRedirectResponse(HttpStatusCode.Found, "http://example.com/new", request);
        pushResp(response);

        var redirectReq = reqOut.ExpectNext();
        Assert.Equal(new Version(2, 0), redirectReq.Version);
    }

    // ============================
    // Upstream failure handling
    // ============================

    [Fact(DisplayName = "RFC9110-15.4-RDBIDI-022: request upstream failure absorbed")]
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

        var reqPubSub = requestPublisher.ExpectSubscription();
        responsePublisher.ExpectSubscription();
        requestOutProbe.ExpectSubscription().Request(5);
        responseOutProbe.ExpectSubscription().Request(5);

        reqPubSub.SendError(new Exception("request boom"));

        requestOutProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
        responseOutProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    [Fact(DisplayName = "RFC9110-15.4-RDBIDI-023: response upstream failure absorbed")]
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

        requestPublisher.ExpectSubscription();
        var respPubSub = responsePublisher.ExpectSubscription();
        requestOutProbe.ExpectSubscription().Request(5);
        responseOutProbe.ExpectSubscription().Request(5);

        respPubSub.SendError(new Exception("response boom"));

        requestOutProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
        responseOutProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }
}
