using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.RFC9110;

/// <summary>
/// Tests the redirect handling stage per RFC 9110.
/// Verifies that 3xx responses trigger new requests with correct method rewriting, URI resolution, and loop detection.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="RedirectStage"/>.
/// RFC 9110 §15.4: Redirect status codes, method preservation rules, and redirect loop protection.
/// </remarks>
public sealed class RedirectStageTests : StreamTestBase
{
    /// <summary>
    /// Materialises a <see cref="RedirectStage"/> with manual subscriber probes,
    /// gives each outlet <paramref name="demandEach"/> demand, and returns the probes.
    /// Source is concatenated with Source.Never to prevent premature completion.
    /// </summary>
    private (TestSubscriber.ManualProbe<HttpResponseMessage> final,
        TestSubscriber.ManualProbe<HttpRequestMessage> redirect) Run(
            RedirectStage stage,
            int demandEach,
            params HttpResponseMessage[] responses)
    {
        var probeFinal = this.CreateManualSubscriberProbe<HttpResponseMessage>();
        var probeRedirect = this.CreateManualSubscriberProbe<HttpRequestMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            var src = b.Add(Source.From(responses).Concat(Source.Never<HttpResponseMessage>()));

            b.From(src).To(s.In);
            b.From(s.Out0).To(Sink.FromSubscriber(probeFinal));
            b.From(s.Out1).To(Sink.FromSubscriber(probeRedirect));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var subFinal = probeFinal.ExpectSubscription();
        var subRedirect = probeRedirect.ExpectSubscription();

        subFinal.Request(demandEach);
        subRedirect.Request(demandEach);

        return (probeFinal, probeRedirect);
    }

    /// <summary>Builds a redirect response with a Location header.</summary>
    private static HttpResponseMessage BuildRedirect(
        HttpStatusCode statusCode,
        string location,
        string? requestUri = "http://example.com/origin")
    {
        var response = new HttpResponseMessage(statusCode);
        response.Headers.TryAddWithoutValidation("Location", location);
        if (requestUri is not null)
        {
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
        }

        return response;
    }

    /// <summary>
    /// Pre-seeds a <see cref="RedirectHandler"/> on the request's Options,
    /// simulating a request that has already been through prior redirects.
    /// </summary>
    private static void SeedRedirectHandler(
        HttpRequestMessage request,
        RedirectHandler handler)
    {
        request.Options.Set(RedirectStage.RedirectHandlerKey, handler);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-15.4-RDIR-001: 200 OK → forwarded on Out0 (final)")]
    public async Task Should_ForwardOnOut0_When_ResponseIsNonRedirect()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        };
        var (final, redirect) = Run(new RedirectStage(), 1, response);

        Assert.Same(response, await final.ExpectNextAsync());
        await redirect.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-15.4-RDIR-002: 404 Not Found → forwarded on Out0 (final)")]
    public async Task Should_ForwardOnOut0_When_ResponseIs404()
    {
        var response = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/missing")
        };
        var (final, redirect) = Run(new RedirectStage(), 1, response);

        Assert.Same(response, await final.ExpectNextAsync());
        await redirect.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-15.4-RDIR-003: 301 Moved Permanently → redirect request emitted on Out1")]
    public async Task Should_EmitRedirectOnOut1_When_ResponseIs301()
    {
        var response = BuildRedirect(HttpStatusCode.MovedPermanently, "http://example.com/new");
        var (_, redirect) = Run(new RedirectStage(), 1, response);

        var newRequest = await redirect.ExpectNextAsync();
        Assert.Equal("http://example.com/new", newRequest.RequestUri?.AbsoluteUri);
        await redirect.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-15.4-RDIR-004: 302 Found → redirect request emitted on Out1")]
    public async Task Should_EmitRedirectOnOut1_When_ResponseIs302()
    {
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/new");
        var (_, redirect) = Run(new RedirectStage(), 1, response);

        await redirect.ExpectNextAsync();
        await redirect.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-15.4-RDIR-005: 303 See Other → method rewritten to GET on Out1")]
    public async Task Should_RewriteMethodToGet_When_ResponseIs303()
    {
        var response = new HttpResponseMessage(HttpStatusCode.SeeOther)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        };
        response.Headers.TryAddWithoutValidation("Location", "http://example.com/result");

        var (final, redirect) = Run(new RedirectStage(), 1, response);

        var newRequest = await redirect.ExpectNextAsync();
        Assert.Equal(HttpMethod.Get, newRequest.Method);
        await final.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-15.4-RDIR-006: 307 Temporary Redirect → method preserved on Out1")]
    public async Task Should_PreserveMethod_When_ResponseIs307()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Post, "http://example.com/api")
        };
        response.Headers.TryAddWithoutValidation("Location", "http://example.com/api/v2");

        var (final, redirect) = Run(new RedirectStage(), 1, response);

        var newRequest = await redirect.ExpectNextAsync();
        Assert.Equal(HttpMethod.Post, newRequest.Method);
        await final.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-15.4-RDIR-007: 308 Permanent Redirect → method preserved on Out1")]
    public async Task Should_PreserveMethod_When_ResponseIs308()
    {
        var response = new HttpResponseMessage(HttpStatusCode.PermanentRedirect)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Put, "http://example.com/resource")
        };
        response.Headers.TryAddWithoutValidation("Location", "http://example.com/resource/v2");

        var (final, redirect) = Run(new RedirectStage(), 1, response);

        var newRequest = await redirect.ExpectNextAsync();
        Assert.Equal(HttpMethod.Put, newRequest.Method);
        await final.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-15.4-RDIR-008: max redirects exceeded → final response forwarded on Out0")]
    public async Task Should_ForwardOnOut0_When_MaxRedirectsExceeded()
    {
        var policy = new RedirectPolicy { MaxRedirects = 1 };
        var handler = new RedirectHandler(policy);
        // Exhaust the single allowed redirect
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var res1 = new HttpResponseMessage(HttpStatusCode.Found);
        res1.Headers.TryAddWithoutValidation("Location", "http://example.com/b");
        handler.BuildRedirectRequest(req1, res1);

        // The next redirect should fail with max exceeded — seed handler on request Options
        var requestMsg = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        SeedRedirectHandler(requestMsg, handler);
        var response = new HttpResponseMessage(HttpStatusCode.Found)
        {
            RequestMessage = requestMsg
        };
        response.Headers.TryAddWithoutValidation("Location", "http://example.com/c");

        var (final, redirect) = Run(new RedirectStage(policy), 1, response);

        Assert.Same(response, await final.ExpectNextAsync());
        await redirect.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-15.4-RDIR-009: redirect loop detected → final response forwarded on Out0")]
    public async Task Should_ForwardOnOut0_When_RedirectLoopDetected()
    {
        var handler = new RedirectHandler();
        // Prime the handler with a→b
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var res1 = new HttpResponseMessage(HttpStatusCode.Found);
        res1.Headers.TryAddWithoutValidation("Location", "http://example.com/b");
        handler.BuildRedirectRequest(req1, res1);

        // Loop: b → a (already visited) — seed handler on request Options
        var requestMsg = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        SeedRedirectHandler(requestMsg, handler);
        var response = new HttpResponseMessage(HttpStatusCode.Found)
        {
            RequestMessage = requestMsg
        };
        response.Headers.TryAddWithoutValidation("Location", "http://example.com/a");

        var (final, redirect) = Run(new RedirectStage(), 1, response);

        Assert.Same(response, await final.ExpectNextAsync());
        await redirect.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-15.4-RDIR-010: HTTPS to HTTP downgrade blocked → final response on Out0")]
    public async Task Should_ForwardOnOut0_When_HttpsToHttpDowngradeAttempted()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.com/secure")
        };
        response.Headers.TryAddWithoutValidation("Location", "http://example.com/insecure");

        var (final, redirect) = Run(new RedirectStage(), 1, response);

        Assert.Same(response, await final.ExpectNextAsync());
        await redirect.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-15.4-RDIR-011: redirect with no Location header → final response on Out0")]
    public async Task Should_ForwardOnOut0_When_LocationHeaderIsMissing()
    {
        // No Location header — BuildRedirectRequest will throw RedirectException
        var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page")
        };

        var (final, redirect) = Run(new RedirectStage(), 1, response);

        Assert.Same(response, await final.ExpectNextAsync());
        await redirect.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9110-15.4-RDIR-012: redirect response with null RequestMessage → passes through on Out0")]
    public async Task Should_ForwardOnOut0_When_RequestMessageIsNull()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.TryAddWithoutValidation("Location", "http://example.com/new");
        // RequestMessage intentionally not set

        var (final, redirect) = Run(new RedirectStage(), 1, response);

        Assert.Same(response, await final.ExpectNextAsync());
        await redirect.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-15.4-RDIR-013: null policy → uses default RedirectPolicy")]
    public async Task Should_UseDefaultRedirectPolicy_When_PolicyIsNull()
    {
        // Using default constructor (no policy)
        var stage = new RedirectStage();
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/new");

        var (final, redirect) = Run(stage, 1, response);

        var newRequest = await redirect.ExpectNextAsync();
        Assert.Equal("http://example.com/new", newRequest.RequestUri?.AbsoluteUri);
        await final.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-15.4-RDIR-014: redirect request on Out1 targets the Location URI")]
    public async Task Should_TargetLocationUri_When_EmittingRedirectRequest()
    {
        const string target = "http://other.com/new-location";
        var response = BuildRedirect(HttpStatusCode.MovedPermanently, target);

        var (_, redirect) = Run(new RedirectStage(), 1, response);

        var newRequest = await redirect.ExpectNextAsync();
        Assert.Equal(target, newRequest.RequestUri?.AbsoluteUri);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-15.4-RDIR-015: cross-origin redirect strips Authorization header")]
    public async Task Should_StripAuthorizationHeader_When_CrossOriginRedirect()
    {
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api");
        original.Headers.TryAddWithoutValidation("Authorization", "Bearer token123");

        var response = new HttpResponseMessage(HttpStatusCode.Found)
        {
            RequestMessage = original
        };
        response.Headers.TryAddWithoutValidation("Location", "http://other.com/api");

        var (_, redirect) = Run(new RedirectStage(), 1, response);

        var newRequest = await redirect.ExpectNextAsync();
        Assert.False(newRequest.Headers.Contains("Authorization"),
            "Authorization must be stripped on cross-origin redirect");
    }

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9110-15.4-RDIR-016: Request A exhausts redirects, Request B starts fresh")]
    public async Task Should_StartFreshRedirectCount_When_PreviousRequestExhaustedRedirects()
    {
        var policy = new RedirectPolicy { MaxRedirects = 1 };

        // Request A: exhaust the single redirect via pre-seeded handler
        var handlerA = new RedirectHandler(policy);
        var reqA = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var resA1 = new HttpResponseMessage(HttpStatusCode.Found);
        resA1.Headers.TryAddWithoutValidation("Location", "http://example.com/a2");
        handlerA.BuildRedirectRequest(reqA, resA1);
        // handlerA is now at max (1 redirect used)

        // Response A2: second redirect for chain A — should be forwarded as final (max exceeded)
        var reqMsgA2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a2");
        SeedRedirectHandler(reqMsgA2, handlerA);
        var responseA = new HttpResponseMessage(HttpStatusCode.Found) { RequestMessage = reqMsgA2 };
        responseA.Headers.TryAddWithoutValidation("Location", "http://example.com/a3");

        // Response B: first redirect for chain B — should succeed (fresh handler)
        var responseB = BuildRedirect(HttpStatusCode.Found, "http://example.com/b2",
            "http://example.com/b");

        var (final, redirect) = Run(new RedirectStage(policy), 5, responseA, responseB);

        // A should be forwarded as final (max exceeded)
        Assert.Same(responseA, await final.ExpectNextAsync());

        // B should get a redirect request (fresh handler, count = 0)
        var newRequestB = await redirect.ExpectNextAsync();
        Assert.Equal("http://example.com/b2", newRequestB.RequestUri?.AbsoluteUri);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9110-15.4-RDIR-017: Independent requests can visit same URI without false loop detection")]
    public async Task Should_NotDetectFalseLoop_When_IndependentRequestsVisitSameUri()
    {
        // Request A redirects to /shared
        var responseA = BuildRedirect(HttpStatusCode.Found, "http://example.com/shared",
            "http://example.com/a");

        // Request B also redirects to /shared — should NOT trigger loop detection
        var responseB = BuildRedirect(HttpStatusCode.Found, "http://example.com/shared",
            "http://example.com/b");

        var (final, redirect) = Run(new RedirectStage(), 5, responseA, responseB);

        // Both should produce redirect requests (no false loop detection)
        var newRequestA = await redirect.ExpectNextAsync();
        Assert.Equal("http://example.com/shared", newRequestA.RequestUri?.AbsoluteUri);

        var newRequestB = await redirect.ExpectNextAsync();
        Assert.Equal("http://example.com/shared", newRequestB.RequestUri?.AbsoluteUri);

        await final.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9110-15.4-RDIR-018: Redirect from HTTP/2 request preserves Version 2.0")]
    public async Task Should_PreserveHttp2Version_When_RedirectingHttp2Request()
    {
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page")
        {
            Version = new Version(2, 0)
        };
        var response = new HttpResponseMessage(HttpStatusCode.Found)
        {
            RequestMessage = original
        };
        response.Headers.TryAddWithoutValidation("Location", "http://example.com/new");

        var (_, redirect) = Run(new RedirectStage(), 1, response);

        var newRequest = await redirect.ExpectNextAsync();
        Assert.Equal(new Version(2, 0), newRequest.Version);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9110-15.4-RDIR-019: Redirect from HTTP/1.0 request preserves Version 1.0")]
    public async Task Should_PreserveHttp10Version_When_RedirectingHttp10Request()
    {
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page")
        {
            Version = new Version(1, 0)
        };
        var response = new HttpResponseMessage(HttpStatusCode.Found)
        {
            RequestMessage = original
        };
        response.Headers.TryAddWithoutValidation("Location", "http://example.com/new");

        var (_, redirect) = Run(new RedirectStage(), 1, response);

        var newRequest = await redirect.ExpectNextAsync();
        Assert.Equal(new Version(1, 0), newRequest.Version);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9110-15.4-RDIR-020: Cross-origin redirect preserves Version")]
    public async Task Should_PreserveVersion_When_CrossOriginRedirect()
    {
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api")
        {
            Version = new Version(2, 0)
        };
        var response = new HttpResponseMessage(HttpStatusCode.Found)
        {
            RequestMessage = original
        };
        response.Headers.TryAddWithoutValidation("Location", "http://other.com/api");

        var (_, redirect) = Run(new RedirectStage(), 1, response);

        var newRequest = await redirect.ExpectNextAsync();
        Assert.Equal(new Version(2, 0), newRequest.Version);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9110-15.4-RDIR-021: Redirect request carries RedirectHandler in Options")]
    public async Task Should_CarryRedirectHandlerInOptions_When_EmittingRedirectRequest()
    {
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/new");

        var (_, redirect) = Run(new RedirectStage(), 1, response);

        var newRequest = await redirect.ExpectNextAsync();
        Assert.True(newRequest.Options.TryGetValue(RedirectStage.RedirectHandlerKey, out var handler));
        Assert.NotNull(handler);
        Assert.Equal(1, handler.RedirectCount);
    }
}
