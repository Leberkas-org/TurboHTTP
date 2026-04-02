using System.Collections.Immutable;
using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHttp.Protocol.Semantics;
using TurboHttp.Streams.Stages.Features;

namespace TurboHttp.StreamTests.Semantics;

/// <summary>
/// Core tests for the redirect bidirectional stage per RFC 9110 §15.4.
/// Covers pass-through, request direction, non-redirect responses, redirect codes,
/// and RedirectHandler per-chain tracking.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="RedirectBidiStage"/>.
/// RFC 9110 §15.4: Redirect status codes, method preservation rules, loop detection, max-redirect enforcement.
/// </remarks>
public sealed class RedirectCoreSpec : StreamTestBase
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
                var neverResponseSource = builder.Add(Source.Maybe<HttpResponseMessage>());
                var ignoredSink = builder.Add(Sink.Ignore<HttpResponseMessage>());

                var take = builder.Add(Flow.Create<HttpRequestMessage>().Take(requests.Length));

                builder.From(source).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).Via(take).To(sink);
                builder.From(neverResponseSource).To(bidi.Inlet2);
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

    // Pass-through tests (null policy)

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task RequestDirection_Should_PassThrough_When_PolicyIsNull()
    {
        var stage = new RedirectBidiStage(null);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Same(request, result);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task ResponseDirection_Should_PassThrough_When_PolicyIsNull()
    {
        var stage = new RedirectBidiStage(null);
        var response = BuildRedirectResponse(HttpStatusCode.Found, "http://example.com/new");

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        Assert.Same(response, result);
    }

    // Request direction tests

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task RequestDirection_Should_ForwardRequestUnchanged()
    {
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Same(request, result);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9110-15.4")]
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

    // Response direction: non-redirect

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ForwardFinalResponse_When_200OK()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        // Consume the forwarded request
        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        // Push a 200 OK response
        var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
        pushResp(response);

        Assert.Same(response, respOut.ExpectNext(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ForwardFinalResponse_When_404()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        var response = new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request };
        pushResp(response);

        Assert.Same(response, respOut.ExpectNext(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ForwardFinalResponse_When_RequestMessageIsNull()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.TryAddWithoutValidation("Location", "http://example.com/new");
        // RequestMessage intentionally not set
        pushResp(response);

        Assert.Same(response, respOut.ExpectNext(TestContext.Current.CancellationToken));
    }

    // Response direction: redirect

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_EmitRedirectOnOut1_When_301()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/old");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        // Original request forwarded
        Assert.Same(request, reqOut.ExpectNext(TestContext.Current.CancellationToken));

        // 301 response triggers redirect
        var response = BuildRedirectResponse(HttpStatusCode.MovedPermanently, "http://example.com/new", request);
        pushResp(response);

        // Redirect request appears on Out1
        var redirectReq = reqOut.ExpectNext(TestContext.Current.CancellationToken);
        Assert.Equal("http://example.com/new", redirectReq.RequestUri?.AbsoluteUri);

        // No response on Out2 (response was disposed)
        respOut.ExpectNoMsg(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_EmitRedirectOnOut1_When_302()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/old");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        Assert.Same(request, reqOut.ExpectNext(TestContext.Current.CancellationToken));

        var response = BuildRedirectResponse(HttpStatusCode.Found, "http://example.com/new", request);
        pushResp(response);

        var redirectReq = reqOut.ExpectNext(TestContext.Current.CancellationToken);
        Assert.Equal("http://example.com/new", redirectReq.RequestUri?.AbsoluteUri);
        respOut.ExpectNoMsg(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_RewriteMethodToGet_When_303()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        Assert.Same(request, reqOut.ExpectNext(TestContext.Current.CancellationToken));

        var response = BuildRedirectResponse(HttpStatusCode.SeeOther, "http://example.com/result", request);
        pushResp(response);

        var redirectReq = reqOut.ExpectNext(TestContext.Current.CancellationToken);
        Assert.Equal(HttpMethod.Get, redirectReq.Method);
        Assert.Equal("http://example.com/result", redirectReq.RequestUri?.AbsoluteUri);
        respOut.ExpectNoMsg(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_PreserveMethod_When_307()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/api");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        Assert.Same(request, reqOut.ExpectNext(TestContext.Current.CancellationToken));

        var response = BuildRedirectResponse(HttpStatusCode.TemporaryRedirect, "http://example.com/api/v2", request);
        pushResp(response);

        var redirectReq = reqOut.ExpectNext(TestContext.Current.CancellationToken);
        Assert.Equal(HttpMethod.Post, redirectReq.Method);
        respOut.ExpectNoMsg(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_PreserveMethod_When_308()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "http://example.com/resource");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, respOut, pushResp, _) = RunManual(stage, 5, 5, request);

        Assert.Same(request, reqOut.ExpectNext(TestContext.Current.CancellationToken));

        var response = BuildRedirectResponse(HttpStatusCode.PermanentRedirect, "http://example.com/resource/v2", request);
        pushResp(response);

        var redirectReq = reqOut.ExpectNext(TestContext.Current.CancellationToken);
        Assert.Equal(HttpMethod.Put, redirectReq.Method);
        respOut.ExpectNoMsg(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
    }

    // RedirectHandler per request chain in Options

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_CarryRedirectHandlerInOptions()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/old");
        var stage = new RedirectBidiStage(new RedirectPolicy());
        var (reqOut, _, pushResp, _) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        var response = BuildRedirectResponse(HttpStatusCode.Found, "http://example.com/new", request);
        pushResp(response);

        var redirectReq = reqOut.ExpectNext(TestContext.Current.CancellationToken);
        Assert.True(redirectReq.Options.TryGetValue(RedirectBidiStage.RedirectHandlerKey, out var handler));
        Assert.NotNull(handler);
        Assert.Equal(1, handler.RedirectCount);
    }
}
