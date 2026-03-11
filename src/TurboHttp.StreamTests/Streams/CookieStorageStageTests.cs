using System.Collections.Immutable;
using Akka.Streams.Dsl;
using TurboHttp.Protocol;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Streams;

public sealed class CookieStorageStageTests : StreamTestBase
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private Task<IImmutableList<HttpResponseMessage>> RunAsync(
        CookieStorageStage stage,
        params HttpResponseMessage[] responses)
    {
        return Source.From(responses)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);
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

    // ── null jar (pass-through) ────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "CSTO-001: null CookieJar → response passes through unchanged")]
    public async Task CSTO_001_NullJar_PassThrough()
    {
        var stage = new CookieStorageStage(null);
        var response = MakeResponse("http://example.com/", "session=abc; Domain=example.com");

        var results = await RunAsync(stage, response);

        Assert.Single(results);
        Assert.Same(response, results[0]);
    }

    // ── cookie storage ─────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "CSTO-002: Set-Cookie in response → stored in jar for next request")]
    public async Task CSTO_002_SetCookie_StoredInJar()
    {
        var jar = new CookieJar();
        var stage = new CookieStorageStage(jar);
        var response = MakeResponse("http://example.com/", "session=abc123; Domain=example.com; Path=/");

        await RunAsync(stage, response);

        var nextRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        jar.AddCookiesToRequest(new Uri("http://example.com/page"), ref nextRequest);
        Assert.True(nextRequest.Headers.Contains("Cookie"));
        var cookieValue = string.Join("; ", nextRequest.Headers.GetValues("Cookie"));
        Assert.Contains("session=abc123", cookieValue);
    }

    [Fact(Timeout = 10_000, DisplayName = "CSTO-003: response is NOT modified by the stage")]
    public async Task CSTO_003_ResponseNotModified()
    {
        var jar = new CookieJar();
        var stage = new CookieStorageStage(jar);
        var response = MakeResponse("http://example.com/", "token=xyz; Domain=example.com; Path=/");
        var originalStatusCode = response.StatusCode;

        var results = await RunAsync(stage, response);

        var result = Assert.Single(results);
        Assert.Same(response, result);
        Assert.Equal(originalStatusCode, result.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "CSTO-004: no Set-Cookie header → jar remains empty")]
    public async Task CSTO_004_NoSetCookie_JarEmpty()
    {
        var jar = new CookieJar();
        var stage = new CookieStorageStage(jar);
        var response = MakeResponse("http://example.com/");

        await RunAsync(stage, response);

        var nextRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(new Uri("http://example.com/"), ref nextRequest);
        Assert.False(nextRequest.Headers.Contains("Cookie"));
    }

    [Fact(Timeout = 10_000, DisplayName = "CSTO-005: response with null RequestMessage → passes through without throwing")]
    public async Task CSTO_005_NullRequestMessage_PassThrough()
    {
        var jar = new CookieJar();
        var stage = new CookieStorageStage(jar);
        var response = MakeResponse(null, "session=abc; Domain=example.com");

        var results = await RunAsync(stage, response);

        Assert.Single(results);
        // No exception thrown; jar remains empty because RequestUri is null
        var nextRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(new Uri("http://example.com/"), ref nextRequest);
        Assert.False(nextRequest.Headers.Contains("Cookie"));
    }

    [Fact(Timeout = 10_000, DisplayName = "CSTO-006: multiple responses → cookies accumulated across all responses")]
    public async Task CSTO_006_MultipleResponses_CookiesAccumulated()
    {
        var jar = new CookieJar();
        var stage = new CookieStorageStage(jar);
        var resp1 = MakeResponse("http://example.com/", "a=1; Domain=example.com; Path=/");
        var resp2 = MakeResponse("http://example.com/", "b=2; Domain=example.com; Path=/");

        await RunAsync(stage, resp1, resp2);

        var nextRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(new Uri("http://example.com/"), ref nextRequest);
        Assert.True(nextRequest.Headers.Contains("Cookie"));
        var cookieValue = string.Join("; ", nextRequest.Headers.GetValues("Cookie"));
        Assert.Contains("a=1", cookieValue);
        Assert.Contains("b=2", cookieValue);
    }
}
