using System.Collections.Immutable;
using Akka.Streams.Dsl;
using TurboHttp.Protocol.RFC6265;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Streams;

public sealed class CookieInjectionStageTests : StreamTestBase
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private Task<IImmutableList<HttpRequestMessage>> RunAsync(
        CookieInjectionStage stage,
        params HttpRequestMessage[] requests)
    {
        return Source.From(requests)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Seq<HttpRequestMessage>(), Materializer);
    }

    private static CookieJar JarWithCookie(string name, string value, string domain, string path = "/")
    {
        var jar = new CookieJar();
        // Build a synthetic Set-Cookie response to populate the jar
        var response = new HttpResponseMessage();
        response.Headers.TryAddWithoutValidation("Set-Cookie", $"{name}={value}; Domain={domain}; Path={path}");
        jar.ProcessResponse(new Uri($"http://{domain}/"), response);
        return jar;
    }

    // ── null jar (pass-through) ────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC6265-5.4-CINJ-001: null CookieJar → request passes through unchanged")]
    public async Task COOK_001_NullJar_PassThrough()
    {
        var stage = new CookieInjectionStage(null);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.False(result.Headers.Contains("Cookie"));
    }

    // ── cookie injection ────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC6265-5.4-CINJ-002: matching cookie in jar → Cookie header injected into request")]
    public async Task COOK_002_MatchingCookie_Injected()
    {
        var jar = JarWithCookie("session", "abc123", "example.com");
        var stage = new CookieInjectionStage(jar);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");

        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("Cookie"));
        var cookieValue = string.Join("; ", result.Headers.GetValues("Cookie"));
        Assert.Contains("session=abc123", cookieValue);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC6265-5.4-CINJ-003: empty jar → no Cookie header added to request")]
    public async Task COOK_003_EmptyJar_NoCookieHeader()
    {
        var jar = new CookieJar();
        var stage = new CookieInjectionStage(jar);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.False(result.Headers.Contains("Cookie"));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC6265-5.4-CINJ-004: non-matching domain → no Cookie header added")]
    public async Task COOK_004_NonMatchingDomain_NoCookieHeader()
    {
        var jar = JarWithCookie("session", "abc123", "other.com");
        var stage = new CookieInjectionStage(jar);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.False(result.Headers.Contains("Cookie"));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC6265-5.4-CINJ-005: request with null RequestUri → passes through without throwing")]
    public async Task COOK_005_NullRequestUri_PassThrough()
    {
        var jar = JarWithCookie("session", "abc123", "example.com");
        var stage = new CookieInjectionStage(jar);
        var request = new HttpRequestMessage { Method = HttpMethod.Get };

        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        // No exception thrown; no Cookie header (RequestUri was null)
        Assert.False(result.Headers.Contains("Cookie"));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC6265-5.4-CINJ-006: multiple requests → each gets cookies injected independently")]
    public async Task COOK_006_MultipleRequests_EachInjected()
    {
        var jar = new CookieJar();
        var response = new HttpResponseMessage();
        response.Headers.TryAddWithoutValidation("Set-Cookie", "token=xyz; Domain=example.com; Path=/");
        jar.ProcessResponse(new Uri("http://example.com/"), response);

        var stage = new CookieInjectionStage(jar);
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");

        var results = new List<HttpRequestMessage>(await RunAsync(stage, req1, req2));

        Assert.Equal(2, results.Count);
        foreach (var result in results)
        {
            Assert.True(result.Headers.Contains("Cookie"));
            var cookieValue = string.Join("; ", result.Headers.GetValues("Cookie"));
            Assert.Contains("token=xyz", cookieValue);
        }
    }
}
