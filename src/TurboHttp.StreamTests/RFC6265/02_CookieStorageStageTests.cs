using System.Collections.Immutable;
using Akka.Streams.Dsl;
using TurboHttp.Protocol.RFC6265;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.RFC6265;

/// <summary>
/// Tests the cookie storage stage per RFC 6265.
/// Verifies that Set-Cookie response headers are parsed and stored in the cookie jar correctly.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="CookieStorageStage"/>.
/// RFC 6265 §5.2–§5.3: Cookie attribute processing, storage model, and domain/path matching.
/// </remarks>
public sealed class CookieStorageStageTests : StreamTestBase
{
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

    [Fact(Timeout = 10_000, DisplayName = "RFC6265-5.3-CSTO-001: null CookieJar → response passes through unchanged")]
    public async Task Should_PassThroughUnchanged_When_CookieJarIsNull()
    {
        var stage = new CookieStorageStage(null);
        var response = MakeResponse("http://example.com/", "session=abc; Domain=example.com");

        var results = await RunAsync(stage, response);

        Assert.Single(results);
        Assert.Same(response, results[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC6265-5.3-CSTO-002: Set-Cookie in response → stored in jar for next request")]
    public async Task Should_StoreCookieInJar_When_SetCookieHeaderPresent()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC6265-5.3-CSTO-003: response is NOT modified by the stage")]
    public async Task Should_NotModifyResponse_When_StoringCookies()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC6265-5.3-CSTO-004: no Set-Cookie header → jar remains empty")]
    public async Task Should_KeepJarEmpty_When_NoSetCookieHeader()
    {
        var jar = new CookieJar();
        var stage = new CookieStorageStage(jar);
        var response = MakeResponse("http://example.com/");

        await RunAsync(stage, response);

        var nextRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(new Uri("http://example.com/"), ref nextRequest);
        Assert.False(nextRequest.Headers.Contains("Cookie"));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC6265-5.3-CSTO-005: response with null RequestMessage → passes through without throwing")]
    public async Task Should_PassThroughSafely_When_RequestMessageIsNull()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC6265-5.3-CSTO-006: multiple responses → cookies accumulated across all responses")]
    public async Task Should_AccumulateCookies_When_MultipleResponsesProcessed()
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
