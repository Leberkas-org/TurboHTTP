using System.Collections.Immutable;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Protocol.RFC6265;
using TurboHttp.Streams.Stages;
using TurboHttp.Streams.Stages.Features;

namespace TurboHttp.StreamTests.RFC6265;

/// <summary>
/// Tests the cookie bidirectional stage per RFC 6265.
/// Verifies that the request direction injects cookies from the jar and the response direction
/// stores Set-Cookie headers into the jar.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="CookieBidiStage"/>.
/// RFC 6265 §5.4: Cookie header construction (request path).
/// RFC 6265 §5.2–§5.3: Set-Cookie storage (response path).
/// </remarks>
public sealed class CookieBidiStageTests : StreamTestBase
{
    /// <summary>
    /// Runs requests through the request direction (In1→Out1) of the BidiStage.
    /// The response direction is wired to empty source / ignored sink.
    /// </summary>
    private Task<IImmutableList<HttpRequestMessage>> RunRequestAsync(
        CookieBidiStage stage,
        params HttpRequestMessage[] requests)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpRequestMessage>(),
            (builder, sink) =>
            {
                var bidi = builder.Add(stage);
                var source = builder.Add(Source.From(requests));
                var emptyResponseSource = builder.Add(Source.Empty<HttpResponseMessage>());
                var ignoredRequestSink = builder.Add(Sink.Ignore<HttpResponseMessage>());

                builder.From(source).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).To(sink);
                builder.From(emptyResponseSource).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(ignoredRequestSink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

    /// <summary>
    /// Runs responses through the response direction (In2→Out2) of the BidiStage.
    /// The request direction is wired to empty source / ignored sink.
    /// </summary>
    private Task<IImmutableList<HttpResponseMessage>> RunResponseAsync(
        CookieBidiStage stage,
        params HttpResponseMessage[] responses)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpResponseMessage>(),
            (builder, sink) =>
            {
                var bidi = builder.Add(stage);
                var source = builder.Add(Source.From(responses));
                var emptyRequestSource = builder.Add(Source.Empty<HttpRequestMessage>());
                var ignoredResponseSink = builder.Add(Sink.Ignore<HttpRequestMessage>());

                builder.From(emptyRequestSource).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).To(ignoredResponseSink);
                builder.From(source).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(sink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

    private static CookieJar JarWithCookie(string name, string value, string domain, string path = "/")
    {
        var jar = new CookieJar();
        var response = new HttpResponseMessage();
        response.Headers.TryAddWithoutValidation("Set-Cookie", $"{name}={value}; Domain={domain}; Path={path}");
        jar.ProcessResponse(new Uri($"http://{domain}/"), response);
        return jar;
    }

    private static HttpResponseMessage MakeResponse(string? requestUri, string? setCookie = null)
    {
        var response = new HttpResponseMessage();
        if (requestUri is not null)
        {
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
        }
        if (setCookie is not null)
        {
            response.Headers.TryAddWithoutValidation("Set-Cookie", setCookie);
        }
        return response;
    }

    // ============================
    // Request direction tests
    // ============================

    [Fact(Timeout = 10_000, DisplayName = "RFC6265-5.4-CBIDI-001: null CookieJar → request passes through unchanged")]
    public async Task RequestDirection_Should_PassThrough_When_CookieJarIsNull()
    {
        var stage = new CookieBidiStage(null);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.False(result.Headers.Contains("Cookie"));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC6265-5.4-CBIDI-002: matching cookie → Cookie header injected")]
    public async Task RequestDirection_Should_InjectCookie_When_MatchingCookieInJar()
    {
        var jar = JarWithCookie("session", "abc123", "example.com");
        var stage = new CookieBidiStage(jar);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("Cookie"));
        var cookieValue = string.Join("; ", result.Headers.GetValues("Cookie"));
        Assert.Contains("session=abc123", cookieValue);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC6265-5.4-CBIDI-003: empty jar → no Cookie header")]
    public async Task RequestDirection_Should_NotAddCookie_When_JarIsEmpty()
    {
        var jar = new CookieJar();
        var stage = new CookieBidiStage(jar);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.False(result.Headers.Contains("Cookie"));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC6265-5.4-CBIDI-004: non-matching domain → no Cookie header")]
    public async Task RequestDirection_Should_NotAddCookie_When_DomainDoesNotMatch()
    {
        var jar = JarWithCookie("session", "abc123", "other.com");
        var stage = new CookieBidiStage(jar);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.False(result.Headers.Contains("Cookie"));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC6265-5.4-CBIDI-005: null RequestUri → passes through safely")]
    public async Task RequestDirection_Should_PassThrough_When_RequestUriIsNull()
    {
        var jar = JarWithCookie("session", "abc123", "example.com");
        var stage = new CookieBidiStage(jar);
        var request = new HttpRequestMessage { Method = HttpMethod.Get };

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.False(result.Headers.Contains("Cookie"));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC6265-5.4-CBIDI-006: multiple requests → each gets cookies independently")]
    public async Task RequestDirection_Should_InjectCookiesIndependently_ForMultipleRequests()
    {
        var jar = JarWithCookie("token", "xyz", "example.com");
        var stage = new CookieBidiStage(jar);
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");

        var results = new List<HttpRequestMessage>(await RunRequestAsync(stage, req1, req2));

        Assert.Equal(2, results.Count);
        foreach (var result in results)
        {
            Assert.True(result.Headers.Contains("Cookie"));
            var cookieValue = string.Join("; ", result.Headers.GetValues("Cookie"));
            Assert.Contains("token=xyz", cookieValue);
        }
    }

    // ============================
    // Response direction tests
    // ============================

    [Fact(Timeout = 10_000, DisplayName = "RFC6265-5.3-CBIDI-007: null CookieJar → response passes through unchanged")]
    public async Task ResponseDirection_Should_PassThrough_When_CookieJarIsNull()
    {
        var stage = new CookieBidiStage(null);
        var response = MakeResponse("http://example.com/", "session=abc; Domain=example.com");

        var results = await RunResponseAsync(stage, response);

        Assert.Single(results);
        Assert.Same(response, results[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC6265-5.3-CBIDI-008: Set-Cookie → stored in jar")]
    public async Task ResponseDirection_Should_StoreCookie_When_SetCookieHeaderPresent()
    {
        var jar = new CookieJar();
        var stage = new CookieBidiStage(jar);
        var response = MakeResponse("http://example.com/", "session=abc123; Domain=example.com; Path=/");

        await RunResponseAsync(stage, response);

        var nextRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        jar.AddCookiesToRequest(new Uri("http://example.com/page"), ref nextRequest);
        Assert.True(nextRequest.Headers.Contains("Cookie"));
        var cookieValue = string.Join("; ", nextRequest.Headers.GetValues("Cookie"));
        Assert.Contains("session=abc123", cookieValue);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC6265-5.3-CBIDI-009: response is NOT modified by the stage")]
    public async Task ResponseDirection_Should_NotModifyResponse()
    {
        var jar = new CookieJar();
        var stage = new CookieBidiStage(jar);
        var response = MakeResponse("http://example.com/", "token=xyz; Domain=example.com; Path=/");
        var originalStatusCode = response.StatusCode;

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        Assert.Same(response, result);
        Assert.Equal(originalStatusCode, result.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC6265-5.3-CBIDI-010: no Set-Cookie → jar remains empty")]
    public async Task ResponseDirection_Should_KeepJarEmpty_When_NoSetCookieHeader()
    {
        var jar = new CookieJar();
        var stage = new CookieBidiStage(jar);
        var response = MakeResponse("http://example.com/");

        await RunResponseAsync(stage, response);

        var nextRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(new Uri("http://example.com/"), ref nextRequest);
        Assert.False(nextRequest.Headers.Contains("Cookie"));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC6265-5.3-CBIDI-011: null RequestMessage → passes through safely")]
    public async Task ResponseDirection_Should_PassThrough_When_RequestMessageIsNull()
    {
        var jar = new CookieJar();
        var stage = new CookieBidiStage(jar);
        var response = MakeResponse(null, "session=abc; Domain=example.com");

        var results = await RunResponseAsync(stage, response);

        Assert.Single(results);
        var nextRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(new Uri("http://example.com/"), ref nextRequest);
        Assert.False(nextRequest.Headers.Contains("Cookie"));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC6265-5.3-CBIDI-012: multiple responses → cookies accumulated")]
    public async Task ResponseDirection_Should_AccumulateCookies_ForMultipleResponses()
    {
        var jar = new CookieJar();
        var stage = new CookieBidiStage(jar);
        var resp1 = MakeResponse("http://example.com/", "a=1; Domain=example.com; Path=/");
        var resp2 = MakeResponse("http://example.com/", "b=2; Domain=example.com; Path=/");

        await RunResponseAsync(stage, resp1, resp2);

        var nextRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(new Uri("http://example.com/"), ref nextRequest);
        Assert.True(nextRequest.Headers.Contains("Cookie"));
        var cookieValue = string.Join("; ", nextRequest.Headers.GetValues("Cookie"));
        Assert.Contains("a=1", cookieValue);
        Assert.Contains("b=2", cookieValue);
    }

    // ============================
    // Bidirectional integration test
    // ============================

    [Fact(Timeout = 10_000, DisplayName = "RFC6265-5.4-CBIDI-013: response stores cookie → next request gets it injected")]
    public async Task Bidirectional_Should_InjectCookieStoredFromPreviousResponse()
    {
        var jar = new CookieJar();

        // First: process a response with Set-Cookie to populate the jar
        var stage1 = new CookieBidiStage(jar);
        var response = MakeResponse("http://example.com/login", "auth=token123; Domain=example.com; Path=/");
        await RunResponseAsync(stage1, response);

        // Second: process a request — the cookie should be injected from the jar
        var stage2 = new CookieBidiStage(jar);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/dashboard");
        var results = await RunRequestAsync(stage2, request);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("Cookie"));
        var cookieValue = string.Join("; ", result.Headers.GetValues("Cookie"));
        Assert.Contains("auth=token123", cookieValue);
    }
}
