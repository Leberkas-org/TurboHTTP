using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHttp.Protocol;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Streams;

public sealed class RedirectStageTests : StreamTestBase
{
    // ── helpers ────────────────────────────────────────────────────────────────

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

    // ── non-redirect pass-through ──────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "REDIR-001: 200 OK → forwarded on Out0 (final)")]
    public async Task REDIR_001_NonRedirect_ForwardedOnOut0()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        };
        var (final, redirect) = Run(new RedirectStage(), 1, response);

        Assert.Same(response, await final.ExpectNextAsync());
        await redirect.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "REDIR-002: 404 Not Found → forwarded on Out0 (final)")]
    public async Task REDIR_002_404_ForwardedOnOut0()
    {
        var response = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/missing")
        };
        var (final, redirect) = Run(new RedirectStage(), 1, response);

        Assert.Same(response, await final.ExpectNextAsync());
        await redirect.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    // ── redirect forwarding to Out1 ────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "REDIR-003: 301 Moved Permanently → redirect request emitted on Out1")]
    public async Task REDIR_003_301_EmitsRedirectOnOut1()
    {
        var response = BuildRedirect(HttpStatusCode.MovedPermanently, "http://example.com/new");
        var (_, redirect) = Run(new RedirectStage(), 1, response);

        var newRequest = await redirect.ExpectNextAsync();
        Assert.Equal("http://example.com/new", newRequest.RequestUri?.AbsoluteUri);
        await redirect.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "REDIR-004: 302 Found → redirect request emitted on Out1")]
    public async Task REDIR_004_302_EmitsRedirectOnOut1()
    {
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/new");
        var (_, redirect) = Run(new RedirectStage(), 1, response);

        await redirect.ExpectNextAsync();
        await redirect.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    [Fact(Timeout = 10_000, DisplayName = "REDIR-005: 303 See Other → method rewritten to GET on Out1")]
    public async Task REDIR_005_303_MethodRewrittenToGet()
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

    [Fact(Timeout = 10_000, DisplayName = "REDIR-006: 307 Temporary Redirect → method preserved on Out1")]
    public async Task REDIR_006_307_MethodPreserved()
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

    [Fact(Timeout = 10_000, DisplayName = "REDIR-007: 308 Permanent Redirect → method preserved on Out1")]
    public async Task REDIR_007_308_MethodPreserved()
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

    // ── max redirects enforcement ──────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "REDIR-008: max redirects exceeded → final response forwarded on Out0")]
    public async Task REDIR_008_MaxRedirectsExceeded_ForwardedOnOut0()
    {
        var policy = new RedirectPolicy { MaxRedirects = 1 };
        var handler = new RedirectHandler(policy);
        // Exhaust the single allowed redirect
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var res1 = new HttpResponseMessage(HttpStatusCode.Found);
        res1.Headers.TryAddWithoutValidation("Location", "http://example.com/b");
        handler.BuildRedirectRequest(req1, res1);

        // The next redirect should fail with max exceeded
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/c",
            "http://example.com/b");

        var (final, redirect) = Run(new RedirectStage(handler), 1, response);

        Assert.Same(response, await final.ExpectNextAsync());
        await redirect.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    // ── redirect loop detection ────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "REDIR-009: redirect loop detected → final response forwarded on Out0")]
    public async Task REDIR_009_RedirectLoop_ForwardedOnOut0()
    {
        var handler = new RedirectHandler();
        // Prime the handler with a→b
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var res1 = new HttpResponseMessage(HttpStatusCode.Found);
        res1.Headers.TryAddWithoutValidation("Location", "http://example.com/b");
        handler.BuildRedirectRequest(req1, res1);

        // Loop: b → a (already visited)
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/a",
            "http://example.com/b");

        var (final, redirect) = Run(new RedirectStage(handler), 1, response);

        Assert.Same(response, await final.ExpectNextAsync());
        await redirect.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    // ── HTTPS → HTTP downgrade protection ─────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "REDIR-010: HTTPS to HTTP downgrade blocked → final response on Out0")]
    public async Task REDIR_010_HttpsToHttpDowngrade_ForwardedOnOut0()
    {
        var handler = new RedirectHandler(); // AllowHttpsToHttpDowngrade = false
        var response = new HttpResponseMessage(HttpStatusCode.Found)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.com/secure")
        };
        response.Headers.TryAddWithoutValidation("Location", "http://example.com/insecure");

        var (final, redirect) = Run(new RedirectStage(handler), 1, response);

        Assert.Same(response, await final.ExpectNextAsync());
        await redirect.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    // ── missing Location header ────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "REDIR-011: redirect with no Location header → final response on Out0")]
    public async Task REDIR_011_MissingLocationHeader_ForwardedOnOut0()
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

    // ── null RequestMessage ────────────────────────────────────────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "REDIR-012: redirect response with null RequestMessage → passes through on Out0")]
    public async Task REDIR_012_NullRequestMessage_ForwardedOnOut0()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.TryAddWithoutValidation("Location", "http://example.com/new");
        // RequestMessage intentionally not set

        var (final, redirect) = Run(new RedirectStage(), 1, response);

        Assert.Same(response, await final.ExpectNextAsync());
        await redirect.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    // ── default constructor (null handler) ────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "REDIR-013: null handler → uses default RedirectHandler with default policy")]
    public async Task REDIR_013_NullHandler_UsesDefaults()
    {
        // Using default constructor (no handler)
        var stage = new RedirectStage();
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/new");

        var (final, redirect) = Run(stage, 1, response);

        var newRequest = await redirect.ExpectNextAsync();
        Assert.Equal("http://example.com/new", newRequest.RequestUri?.AbsoluteUri);
        await final.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    // ── redirect request URL ───────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "REDIR-014: redirect request on Out1 targets the Location URI")]
    public async Task REDIR_014_RedirectRequest_TargetsLocationUri()
    {
        const string target = "http://other.com/new-location";
        var response = BuildRedirect(HttpStatusCode.MovedPermanently, target);

        var (_, redirect) = Run(new RedirectStage(), 1, response);

        var newRequest = await redirect.ExpectNextAsync();
        Assert.Equal(target, newRequest.RequestUri?.AbsoluteUri);
    }

    // ── cross-origin Authorization header stripping ───────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "REDIR-015: cross-origin redirect strips Authorization header")]
    public async Task REDIR_015_CrossOrigin_AuthorizationStripped()
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

    // ── CookieJar integration ──────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "REDIR-016: with CookieJar, cookies re-applied for same-origin redirect")]
    public async Task REDIR_016_CookieJar_CookiesReapplied_SameOrigin()
    {
        var jar = new CookieJar();
        var setResponse = new HttpResponseMessage(HttpStatusCode.OK);
        setResponse.Headers.TryAddWithoutValidation("Set-Cookie", "session=abc123; Path=/");
        jar.ProcessResponse(new Uri("http://example.com/"), setResponse);

        var response = new HttpResponseMessage(HttpStatusCode.Found)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page")
        };
        response.Headers.TryAddWithoutValidation("Location", "http://example.com/new");

        var (_, redirect) = Run(new RedirectStage(null, jar), 1, response);

        var newRequest = await redirect.ExpectNextAsync();
        Assert.True(newRequest.Headers.Contains("Cookie"),
            "Applicable cookies should be re-applied to the redirect request");
        var cookieHeader = string.Join("; ", newRequest.Headers.GetValues("Cookie"));
        Assert.Contains("session=abc123", cookieHeader);
    }

    [Fact(Timeout = 10_000, DisplayName = "REDIR-017: with CookieJar, cookies NOT applied for different domain")]
    public async Task REDIR_017_CookieJar_NoCookies_DifferentDomain()
    {
        var jar = new CookieJar();
        var setResponse = new HttpResponseMessage(HttpStatusCode.OK);
        setResponse.Headers.TryAddWithoutValidation("Set-Cookie", "session=abc123; Path=/");
        jar.ProcessResponse(new Uri("http://example.com/"), setResponse);

        var response = new HttpResponseMessage(HttpStatusCode.Found)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page")
        };
        response.Headers.TryAddWithoutValidation("Location", "http://other.com/page");

        var (_, redirect) = Run(new RedirectStage(null, jar), 1, response);

        var newRequest = await redirect.ExpectNextAsync();
        Assert.False(newRequest.Headers.Contains("Cookie"),
            "Cookies for example.com must not be sent to other.com");
    }
}